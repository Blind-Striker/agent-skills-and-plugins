using OpenCode.Sdk;
using OpenCode.Sdk.Models;

static partial class Ready
{
    public static string? FenceKind => OperatingSystem.IsLinux() ? "bwrap" : OperatingSystem.IsMacOS() ? "sandbox-exec" : null;
    public static bool Fenced(Modes modes) => FenceKind is not null && modes.Access is "read-only" or "workspace";
    static readonly Lazy<Task<string?>> fenceReadiness = new(CheckFenceAsync);
    public static Task<string?> FenceAsync(CancellationToken ct) => fenceReadiness.Value.WaitAsync(ct);

    static async Task<string?> CheckFenceAsync()
    {
        if (FenceKind is not { } kind) return null;
        string? detail;
        try
        {
            var executable = Executables.Resolve(kind);
            var result = await Proc.RunAsync(executable, kind == "bwrap"
                ? ["--ro-bind", "/", "/", "--dev", "/dev", "true"]
                : ["-p", "(version 1)(allow default)", "/usr/bin/true"], Environment.CurrentDirectory,
                CancellationToken.None, TimeSpan.FromSeconds(30));
            if (result.ExitCode == 0) return null;
            detail = $"{kind} exited {result.ExitCode}: {result.Stderr.Trim()}";
        }
        catch (Exception ex) when (ex is HarnessException or System.ComponentModel.Win32Exception or IOException)
        { detail = ex.Message; }
        return $"opencode fence unavailable ({detail})" + (kind == "bwrap"
            ? "; install bubblewrap, and on Ubuntu 24.04 set kernel.apparmor_restrict_unprivileged_userns=0 (root)" : "");
    }

    public static IReadOnlyList<string> WritableSet(WorkerLaunch launch, string profileRoot, string home,
        IReadOnlyDictionary<string, string> environment, string? gitCommon)
    {
        var roots = new List<string>();
        foreach (var (name, fallback) in new[] { ("DATA", ".local/share"), ("STATE", ".local/state"), ("CACHE", ".cache"), ("CONFIG", ".config") })
            roots.Add(Path.Combine(environment.GetValueOrDefault($"XDG_{name}_HOME") is { Length: > 0 } xdg
                ? xdg : Path.Combine(home, fallback), "opencode"));
        roots.AddRange([profileRoot, launch.Environment["TEMP"], "/dev"]);
        if (launch.Modes.Access == "workspace")
        {
            roots.Add(launch.Directory);
            if (gitCommon is not null) roots.Add(gitCommon);
            roots.AddRange(launch.WritableRoots ?? []);
        }
        return roots.Distinct(StringComparer.Ordinal).ToArray();
    }

    public static string FenceProfile(IReadOnlyList<string> writable) =>
        "(version 1) (allow default) (deny file-write*) (allow file-write* "
        + string.Join(" ", writable.Concat(["/dev", "/tmp", "/private/tmp", "/private/var/folders"]).Distinct(StringComparer.Ordinal)
            .Select(path => $"(subpath \"{path.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)}\")")) + ")";

    public static IReadOnlyList<string> FenceCommand(string kind, IReadOnlyList<string> command, IReadOnlyList<string> writable) => kind == "bwrap"
        ? ["bwrap", "--ro-bind", "/", "/", "--dev", "/dev", "--ro-bind", "/proc", "/proc", "--tmpfs", "/tmp",
            .. writable.Where(path => path != "/dev").SelectMany(path => new[] { "--bind", path, path }), "--", .. command]
        : ["sandbox-exec", "-p", FenceProfile(writable), .. command];
}

sealed record BusDisconnected(string Reason);

sealed record BusConnected(int Attempt);

sealed record BusBarrier(TaskCompletionSource Completion);

sealed class HarnessServer : IAsyncDisposable
{
    static readonly TimeSpan ExitConfirmation = TimeSpan.FromSeconds(60);
    readonly CancellationTokenSource _pumpCts = new();
    readonly Channel<object> _bus = Channel.CreateUnbounded<object>(new UnboundedChannelOptions { SingleReader = true });
    Task _pump = Task.CompletedTask;
    Task? _disposal;
    readonly object _disposeGate = new();
    public bool IsStopping { get; private set; }

    HarnessServer(OpenCodeServer server, OpenCodeClient client, OpenCodeServerOutput output, string commandDescription)
    {
        Server = server;
        Client = client;
        Output = output;
        CommandDescription = commandDescription;
    }

    public OpenCodeServer Server { get; }

    public OpenCodeClient Client { get; }

    public OpenCodeServerOutput Output { get; }

    public string CommandDescription { get; }

    public string Version { get; private set; } = "";

    public long Pid { get; private set; }

    public ChannelReader<object> Events => _bus.Reader;

    public Task DrainEventsAsync(CancellationToken ct)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_bus.Writer.TryWrite(new BusBarrier(completion)))
            throw new HarnessException("the event stream is closed");
        return completion.Task.WaitAsync(ct);
    }

    public static async Task<HarnessServer> StartAsync(bool subscribe, BackendContext context, string profileRoot, Modes modes, CancellationToken ct,
        WorkerLaunch? launch = null)
    {
        var output = new OpenCodeServerOutput();
        var command = Command(context.Executable);
        OpenCodeServer server;
        var environment = WorkerEnvironment.Copy(context.Passthrough);
        if (launch is not null && Ready.Fenced(modes))
            foreach (var (name, value) in launch.Environment) environment[name] = value;
        environment["OPENCODE_FILEWATCHER_DISABLE"] = "1";
        environment["OPENCODE_DISABLE_FILEWATCHER"] = "1";
        if (modes.Profile == "clean")
        {
            environment["OPENCODE_CONFIG_DIR"] = profileRoot;
            environment["OPENCODE_CONFIG"] = "";
            environment["OPENCODE_CONFIG_CONTENT"] = "{}";
            environment["OPENCODE_DISABLE_CLAUDE_CODE"] = "1";
        }
        if (modes.Project == "off")
        {
            environment["OPENCODE_CONFIG_PROJECT_DISABLE"] = "1";
            environment["OPENCODE_DISABLE_PROJECT_CONFIG"] = "1";
        }
        if (launch is not null && Ready.Fenced(modes))
        {
            if (await Ready.FenceAsync(ct) is { } refusal) throw new HarnessException(refusal);
            var inherited = WorkerEnvironment.Snapshot();
            foreach (var (name, value) in environment) inherited[name] = value;
            string? common = null;
            if (modes.Access == "workspace")
            {
                var git = await Git.Default.RunAsync(launch.Directory, ct, "rev-parse", "--git-common-dir");
                if (git.ExitCode == 0) common = Path.GetFullPath(git.Output.Trim(), launch.Directory);
            }
            var writable = Ready.WritableSet(launch, profileRoot, inherited.GetValueOrDefault("HOME")
                ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), inherited, common);
            foreach (var path in writable.Where(path => path != "/dev")) Directory.CreateDirectory(path);
            command = Ready.FenceCommand(Ready.FenceKind!, command, writable);
        }
        try
        {
            server = await OpenCodeServer.StartAsync(new OpenCodeServerOptions
            {
                Command = command,
                WorkingDirectory = profileRoot,
                Environment = environment,
                Output = output,
                ReadinessTimeout = TimeSpan.FromSeconds(90),
            }, ct);
        }
        catch (OpenCodeServerException ex)
        {
            var tail = output.GetSnapshot().StandardError.TakeLast(20);
            throw new HarnessException($"server start failed: {ex.Message}\n{string.Join('\n', tail)}");
        }

        var description = command[0].EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? "opencode.exe (shim resolved)" : command[0];
        var host = new HarnessServer(server, server.CreateClient(), output, description);
        try
        {
            var info = await Ready.PollAsync(() => host.Client.Server.GetInfoAsync(cancellationToken: ct), TimeSpan.FromSeconds(60), "server info", ct);
            host.Version = info.ServerInfo.Version;
            host.Pid = info.ServerInfo.Pid;
            if (subscribe)
            {
                await host.SubscribeAsync(ct);
            }

            return host;
        }
        catch
        {
            await host.DisposeAsync();
            throw;
        }
    }

    // On Windows the npm shim runs through cmd.exe and leaves the real server outside the job
    // that ends it with its owner; start opencode.exe directly when the shim's layout is found.
    internal static IReadOnlyList<string> Command(string? executable)
    {
        if (executable is { } explicitPath)
        {
            return [explicitPath, "serve"];
        }

        if (OperatingSystem.IsWindows())
        {
            foreach (var dir in (System.Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                if (File.Exists(Path.Combine(dir, "opencode.cmd")))
                {
                    var exe = Path.Combine(dir, "node_modules", "@opencode", "cli", "bin", "opencode.exe");
                    return File.Exists(exe) ? [exe, "serve"] : ["opencode", "serve"];
                }
            }
        }

        return ["opencode", "serve"];
    }

    async Task SubscribeAsync(CancellationToken ct)
    {
        var connected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var token = _pumpCts.Token;
        _pump = Task.Run(async () =>
        {
            var attempt = 0;
            while (!token.IsCancellationRequested)
            {
                string reason;
                try
                {
                    await foreach (var e in Client.Events.SubscribeAsync(cancellationToken: token))
                    {
                        if (e is EventServerConnected)
                        {
                            connected.TrySetResult();
                            await _bus.Writer.WriteAsync(new BusConnected(attempt), token);
                            attempt = 0;
                            continue;
                        }

                        await _bus.Writer.WriteAsync(e, token);
                    }

                    reason = "stream ended";
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    break;
                }
                catch (OpenCodeException ex)
                {
                    reason = $"{ex.GetType().Name}: {ex.Message}";
                }

                // The bus has no replay: report the gap so the supervisor polls, then resubscribe.
                attempt++;
                await _bus.Writer.WriteAsync(new BusDisconnected(reason), token);
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(Math.Min(30, 1 << Math.Min(attempt, 5))), token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }, CancellationToken.None);

        try
        {
            await connected.Task.WaitAsync(TimeSpan.FromSeconds(30), ct);
        }
        catch (TimeoutException)
        {
            throw new HarnessException("the event bus did not connect within 30 s");
        }
    }

    public async Task<bool> StopTreeAsync(CancellationToken ct)
    {
        IsStopping = true;
        try { await DisposeAsync(); }
        catch (Exception)
        {
            // Failed disposal cannot confirm a stop, even if the server has reported an exit.
            return false;
        }
        return Server.Exited.IsCompletedSuccessfully;
    }

    public ValueTask DisposeAsync()
    {
        lock (_disposeGate) return new(_disposal ??= DisposeCoreAsync());
    }

    async Task DisposeCoreAsync()
    {
        IsStopping = true;
        await _pumpCts.CancelAsync();
        try
        {
            await _pump;
        }
        catch (Exception ex) when (ex is OperationCanceledException or ChannelClosedException)
        {
            // The pump ends by cancellation; nothing to report.
        }

        finally
        {
            try { Client.Dispose(); }
            finally
            {
                try
                {
                    await Server.DisposeAsync();
                    // Disposal is bounded and can return before Windows process rundown releases
                    // the profile folder; the SDK's Exited completes only once the handle signals.
                    try { await Server.Exited.WaitAsync(ExitConfirmation); }
                    catch (TimeoutException) { /* Unconfirmed: StopTreeAsync reports not stopped. */ }
                }
                finally
                {
                    _bus.Writer.TryComplete();
                    _pumpCts.Dispose();
                }
            }
        }
    }
}
