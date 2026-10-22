using OpenCode.Sdk;
using OpenCode.Sdk.Models;

sealed record BusDisconnected(string Reason);

sealed record BusConnected(int Attempt);

sealed record BusBarrier(TaskCompletionSource Completion);

sealed class HarnessServer : IAsyncDisposable
{
    readonly CancellationTokenSource _pumpCts = new();
    readonly Channel<object> _bus = Channel.CreateUnbounded<object>(new UnboundedChannelOptions { SingleReader = true });
    Task _pump = Task.CompletedTask;
    readonly Process _process;
    Task? _disposal;
    readonly object _disposeGate = new();
    public bool IsStopping { get; private set; }

    HarnessServer(OpenCodeServer server, OpenCodeClient client, OpenCodeServerOutput output, string commandDescription)
    {
        Server = server;
        Client = client;
        Output = output;
        CommandDescription = commandDescription;
        _process = Process.GetProcessById(server.ProcessId);
        _ = _process.Handle;
        _process.EnableRaisingEvents = true;
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

    public static async Task<HarnessServer> StartAsync(bool subscribe, BackendContext context, string profileRoot, Modes modes, CancellationToken ct)
    {
        var output = new OpenCodeServerOutput();
        var command = Command(context.Executable);
        OpenCodeServer server;
        var environment = context.Passthrough.ToDictionary(x => x.Key, x => x.Value);
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
    static IReadOnlyList<string> Command(string? executable)
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
        if (!_process.HasExited) _process.Kill(entireProcessTree: true);
        await WaitForExitConfirmedAsync(ct);
        var stopped = _process.HasExited;
        await DisposeAsync();
        return stopped;
    }

    public ValueTask DisposeAsync()
    {
        lock (_disposeGate) return new(_disposal ??= DisposeCoreAsync());
    }

    Task WaitForExitConfirmedAsync(CancellationToken ct = default)
    {
        // Windows can report an exit code before process rundown releases its directory and files.
        // WaitForExitAsync short-circuits on HasExited; a retained handle's synchronous wait does not.
        return OperatingSystem.IsWindows() ? Task.Run(_process.WaitForExit, ct) : _process.WaitForExitAsync(ct);
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
                    if (!_process.HasExited) _process.Kill(entireProcessTree: true);
                    await WaitForExitConfirmedAsync();
                    await Server.DisposeAsync();
                }
                finally
                {
                    _bus.Writer.TryComplete();
                    _process.Dispose();
                    _pumpCts.Dispose();
                }
            }
        }
    }
}
