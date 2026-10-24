using OpenCode.Sdk;
using OpenCode.Sdk.Models;

static partial class Ready
{
    public static string? FenceKind => OperatingSystem.IsLinux() ? "bwrap" : OperatingSystem.IsMacOS() ? "sandbox-exec" : null;
    public static bool Fenced(Modes modes) => FenceKind is not null && modes.Access is "read-only" or "workspace";
    static readonly Lazy<Task<string?>> fenceReadiness = new(CheckFenceAsync);
    static string? fenceExecutable;
    public static Task<string?> FenceAsync(CancellationToken ct) => fenceReadiness.Value.WaitAsync(ct);

    static async Task<string?> CheckFenceAsync()
    {
        if (FenceKind is not { } kind) return null;
        string? detail;
        try
        {
            fenceExecutable = Executables.Resolve(kind);
            var temp = Directory.CreateTempSubdirectory("dispatch-fence-").FullName;
            ProcessResult result;
            try
            {
                result = await Proc.RunAsync(fenceExecutable, kind == "bwrap"
                    ? FenceCommand(kind, fenceExecutable, ["true"], [temp], [], null, null).Skip(1).ToArray()
                    : ["-p", "(version 1)(allow default)", "/usr/bin/true"], Environment.CurrentDirectory,
                    CancellationToken.None, TimeSpan.FromSeconds(30));
            }
            finally { Directory.Delete(temp, recursive: true); }
            if (result.ExitCode == 0) return null;
            detail = $"{kind} exited {result.ExitCode}: {result.Stderr.Trim()}";
        }
        catch (Exception ex)
        { detail = ex.Message; }
        return $"opencode fence unavailable ({detail})" + (kind == "bwrap"
            ? "; install bubblewrap, and on Ubuntu 24.04 set kernel.apparmor_restrict_unprivileged_userns=0 (root)" : "");
    }

    public static IReadOnlyList<string> WritableSet(WorkerLaunch launch, string profileRoot, string home,
        IReadOnlyDictionary<string, string> environment, string? gitCommon)
    {
        var roots = new List<string>();
        foreach (var (name, fallback) in new[] { ("DATA", ".local/share"), ("STATE", ".local/state") })
            roots.Add(Path.Combine(environment.GetValueOrDefault($"XDG_{name}_HOME") is { Length: > 0 } xdg
                ? xdg : Path.Combine(home, fallback), "opencode"));
        roots.AddRange([launch.Environment["TEMP"], "/dev"]);
        if (launch.Modes.Access == "workspace")
        {
            roots.Add(launch.Directory);
            if (gitCommon is not null) roots.Add(gitCommon);
            roots.AddRange(launch.WritableRoots ?? []);
        }
        foreach (var root in roots)
            foreach (var (folder, name) in new[] { (Locations.ControlRoot, "control root"), (Locations.LedgerRoot, "ledger root") })
                if (Worktree.IsInside(Worktree.RealPath(root), Worktree.RealPath(folder)) || Worktree.IsInside(Worktree.RealPath(folder), Worktree.RealPath(root)))
                    throw new HarnessException($"{launch.TaskId}: opencode fence writable root overlaps {name}: {root}");
        return roots.Distinct(StringComparer.Ordinal).ToArray();
    }

    static string Subpath(string path) => $"(subpath \"{path.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)}\")";

    public static string FenceProfile(IReadOnlyList<string> writable, IReadOnlyList<string> readOnly) =>
        "(version 1) (allow default) (deny file-write*) (allow file-write* "
        + string.Join(" ", writable.Append("/dev").Distinct(StringComparer.Ordinal).Select(Subpath)) + ")"
        + string.Concat(readOnly.Select(path => $" (deny file-write* {Subpath(path)})"));

    public static IReadOnlyList<string> FenceCommand(string kind, string executable, IReadOnlyList<string> command,
        IReadOnlyList<string> writable, IReadOnlyList<string> readOnly, string? directory, string? runtime) => kind == "bwrap"
        ? [executable, "--ro-bind", "/", "/", "--dev", "/dev", "--ro-bind", "/proc", "/proc", "--tmpfs", "/tmp",
            .. directory is null ? Array.Empty<string>() : ["--ro-bind", directory, directory],
            .. writable.Where(path => path != "/dev").SelectMany(path => new[] { "--bind", path, path }),
            .. runtime is null ? Array.Empty<string>() : ["--tmpfs", runtime],
            .. readOnly.SelectMany(path => new[] { "--ro-bind", path, path }), "--", .. command]
        : [executable, "-p", FenceProfile(writable, readOnly), .. command];

    public static string FenceExecutable => fenceExecutable ?? throw new HarnessException("opencode fence readiness has not resolved an executable");
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
            environment["XDG_CACHE_HOME"] = Path.Combine(launch.Environment["TEMP"], "cache");
            foreach (var name in new[] { "TMPDIR", "TEMP", "TMP" })
                environment[name] = Path.Combine(launch.Environment["TEMP"], "tmp");
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
            Directory.CreateDirectory(environment["XDG_CACHE_HOME"]);
            Directory.CreateDirectory(environment["TMPDIR"]);
            var readOnly = new List<string> { Path.Combine(writable[0], "bin") };
            if (common is not null) readOnly.AddRange(new[] { Path.Combine(common, "hooks"), Path.Combine(common, "config") }.Where(Path.Exists));
            if (Ready.FenceKind == "bwrap") readOnly.RemoveAll(path => !Path.Exists(path));
            var runtime = inherited.GetValueOrDefault("XDG_RUNTIME_DIR");
            command = Ready.FenceCommand(Ready.FenceKind!, Ready.FenceExecutable, command, writable, readOnly,
                modes.Access == "read-only" ? launch.Directory : null, Directory.Exists(runtime) ? runtime : null);
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
