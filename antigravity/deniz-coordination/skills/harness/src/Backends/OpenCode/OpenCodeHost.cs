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

    static string Subpath(string path)
    {
        if (OperatingSystem.IsMacOS()) path = Worktree.RealPath(path);
        return $"(subpath \"{path.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)}\")";
    }

    public static string FenceProfile(IReadOnlyList<string> writable, IReadOnlyList<string> readOnly) =>
        "(version 1) (allow default) (deny file-write*) (allow file-write* "
        + string.Join(" ", writable.Append("/dev").Distinct(StringComparer.Ordinal).Select(Subpath)) + ")"
        + string.Concat(readOnly.Select(path => $" (deny file-write* {Subpath(path)})"));

    public static IReadOnlyList<string> FenceCommand(string kind, string executable, IReadOnlyList<string> command,
        IReadOnlyList<string> writable, IReadOnlyList<string> readOnly, string? directory, string? runtime,
        IReadOnlyList<(string Source, string Target)>? privateFolders = null) => kind == "bwrap"
        ? [executable, "--ro-bind", "/", "/", "--dev", "/dev", "--ro-bind", "/proc", "/proc", "--tmpfs", "/tmp",
            .. directory is null ? Array.Empty<string>() : ["--ro-bind", directory, directory],
            .. writable.Where(path => path != "/dev").SelectMany(path => new[] { "--bind", path, path }),
            .. (privateFolders ?? []).SelectMany(bind => new[] { "--bind", bind.Source, bind.Target }),
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
    string[] _privateFolders = [];

    // What a fenced server's sessions' shells take from the server instead of the task environment.
    public IReadOnlyDictionary<string, string> SessionOverrides { get; private set; } = new Dictionary<string, string>();
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
        OpenCodeServer? server = null;
        string? temporary = null;
        var privateSources = new List<string>();
        bool temporaryCreated = false;
        var original = WorkerEnvironment.Snapshot();
        var environment = WorkerEnvironment.Copy(context.Passthrough);
        if (launch is not null && Ready.Fenced(modes))
            foreach (var (name, value) in launch.Environment) environment[name] = value;
        foreach (var (name, value) in launch?.ConfiguredEnvironment ?? new Dictionary<string, string>()) environment[name] = value;
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
        try
        {
            if (launch is not null && Ready.Fenced(modes))
            {
                if (await Ready.FenceAsync(ct) is { } refusal) throw new HarnessException(refusal);
                environment["XDG_CACHE_HOME"] = Path.Combine(launch.Environment["TEMP"], "cache");
                // Unix socket paths are capped near 108 bytes (104 on macOS); keep tool sockets inside the fence.
                temporary = (OperatingSystem.IsMacOS() ? "/private/tmp" : "/var/tmp") + "/dispatch-"
                    + Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(6)).ToLowerInvariant();
                foreach (var name in new[] { "TMPDIR", "TEMP", "TMP" })
                    environment[name] = temporary;
                var inherited = WorkerEnvironment.Copy(original);
                foreach (var name in new[] { "TEMP", "TMP", "TMPDIR", "XDG_CACHE_HOME" }) inherited[name] = environment[name];
                if (launch.Environment.TryGetValue("XDG_RUNTIME_DIR", out var taskRuntime)) inherited["XDG_RUNTIME_DIR"] = taskRuntime;
                string? common = null;
                var git = await Git.ForTask(launch.TaskId, launch.RunId, profileRoot)
                    .RunAsync(launch.Directory, ct, "rev-parse", "--git-common-dir");
                if (git.ExitCode == 0) common = Path.GetFullPath(git.Output.Trim(), launch.Directory);
                var privateTargets = (launch.PrivateFolders ?? []).Select(entry => (Entry: entry, Target: Worktree.RealPath(entry)))
                    .DistinctBy(x => x.Target, StringComparer.Ordinal).ToArray();
                var home = inherited.GetValueOrDefault("HOME") ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                var writable = Ready.WritableSet(launch, profileRoot, home, inherited, common);
                if (OperatingSystem.IsLinux())
                    foreach (var (entry, target) in privateTargets)
                        foreach (var (folder, name) in new[] { (launch.Directory, "workspace"), (common, "git common folder") }
                            .Concat(writable.Append(temporary).Select(folder => ((string?)folder, "writable folder"))))
                            if (folder is not null && Worktree.IsInside(Worktree.RealPath(folder), target))
                                throw new HarnessException($"{launch.TaskId}: privateFolders entry {entry} would hide the {name}: {folder}");
                foreach (var path in writable.Where(path => path != "/dev")) Directory.CreateDirectory(path);
                Directory.CreateDirectory(environment["XDG_CACHE_HOME"]);
                if (OperatingSystem.IsMacOS() || OperatingSystem.IsLinux())
                {
                    CreatePrivateFolder(launch.TaskId, temporary);
                    temporaryCreated = true;
                    writable = [.. writable, temporary];
                }
                var privateFolders = new List<(string Source, string Target)>();
                if (OperatingSystem.IsLinux())
                {
                    var originalHome = original.GetValueOrDefault("HOME") ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                    // Protect the harness's own file-based build cache, independent of task home overrides.
                    var self = Path.Combine(original.GetValueOrDefault("XDG_DATA_HOME") is { Length: > 0 } data && data.StartsWith('/')
                        ? data : Path.Combine(originalHome, ".local/share"), "dotnet", "runfile");
                    foreach (var target in privateTargets.Select(x => x.Target).Append(Worktree.RealPath(self)))
                    {
                        var source = Path.Combine(temporary, "private-" + privateFolders.Count.ToString(CultureInfo.InvariantCulture));
                        CreatePrivateFolder(launch.TaskId, source);
                        privateSources.Add(source);
                        privateFolders.Add((source, Directory.CreateDirectory(target).FullName));
                    }
                }
                var readOnly = new List<string> { Path.Combine(writable[0], "bin") };
                if (Ready.FenceKind == "bwrap" && modes.Access == "read-only" && common is not null && Worktree.IsInside(common, "/tmp"))
                    readOnly.Add(common);
                if (common is not null) readOnly.AddRange(new[] { Path.Combine(common, "hooks"), Path.Combine(common, "config") }.Where(Path.Exists));
                if (Ready.FenceKind == "bwrap") readOnly.RemoveAll(path => !Path.Exists(path));
                var runtime = inherited.GetValueOrDefault("XDG_RUNTIME_DIR");
                command = Ready.FenceCommand(Ready.FenceKind!, Ready.FenceExecutable, command, writable, readOnly,
                    modes.Access == "read-only" ? launch.Directory : null, Directory.Exists(runtime) ? runtime : null, privateFolders);
            }
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
        finally
        {
            if (server is null)
                foreach (var folder in privateSources.Concat(temporaryCreated ? [temporary!] : []))
                    try { Directory.Delete(folder, recursive: true); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }

        var description = command[0].EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? "opencode.exe (shim resolved)" : command[0];
        var host = new HarnessServer(server, server.CreateClient(), output, description);
        if (launch is not null && Ready.Fenced(modes))
        {
            host._privateFolders = [environment["XDG_CACHE_HOME"], environment["TMPDIR"], .. privateSources];
            host.SessionOverrides = new[] { "TEMP", "TMP", "TMPDIR" }.ToDictionary(name => name, _ => environment["TMPDIR"]);
        }
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

    static void CreatePrivateFolder(string taskId, string source)
    {
        if (mkdir(source, 448) != 0)
            throw new HarnessException(System.Runtime.InteropServices.Marshal.GetLastPInvokeError() == 17
                ? $"{taskId}: refused an existing private folder: {source}"
                : $"{taskId}: cannot create private folder: {source}");
        if ((OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            && File.GetUnixFileMode(source) != (UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute))
        {
            Directory.Delete(source);
            throw new HarnessException($"{taskId}: private folder must have mode 0700: {source}");
        }
    }

    [System.Runtime.InteropServices.DllImport("libc", SetLastError = true)]
    static extern int mkdir(string path, uint mode);

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
                    return File.Exists(exe) ? [exe, "serve"] : [Path.Combine(dir, "opencode.cmd"), "serve"];
                }
            }
        }

        return [Executables.Resolve("opencode"), "serve"];
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
                    if (Server.Exited.IsCompletedSuccessfully)
                        foreach (var folder in _privateFolders)
                        {
                            // The exiting server can still be removing its own temp entries while this runs.
                            try { if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true); }
                            catch (DirectoryNotFoundException) { }
                        }
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
