sealed record ProcessResult(int ExitCode, byte[] Stdout, string Stderr, bool TimedOut)
{
    public string Output => Encoding.UTF8.GetString(Stdout);
    public bool Killed { get; init; }
}

static class Proc
{
    public static async Task<ProcessResult> RunAsync(string file, IReadOnlyList<string> args, string cwd, CancellationToken ct, TimeSpan? timeout = null, bool belowNormal = false, IReadOnlyDictionary<string, string>? environment = null)
    {
        if (!Path.IsPathFullyQualified(cwd)) throw new HarnessException("child working directory must be absolute");
        ct.ThrowIfCancellationRequested();
        var psi = new ProcessStartInfo(file)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = cwd,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var arg in args) psi.ArgumentList.Add(arg);
        if (environment is not null)
        {
            psi.Environment.Clear();
            foreach (var (name, value) in environment) psi.Environment[name] = value;
        }

        using var process = new Process { StartInfo = psi };
        process.Start();
        using var window = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (timeout is { } t) window.CancelAfter(t);
        using var drain = new CancellationTokenSource();
        using var output = new MemoryStream();
        using var error = new MemoryStream();
        var captureLock = new object();
        var captureClosed = false;
        var stdout = ReadAsync(process.StandardOutput.BaseStream, output);
        var stderr = ReadAsync(process.StandardError.BaseStream, error);
        if (belowNormal && OperatingSystem.IsWindows())
        {
            try
            {
                // Builds and tests run beside interactive work; children inherit the below-normal class.
                process.PriorityClass = ProcessPriorityClass.BelowNormal;
            }
            catch (InvalidOperationException)
            {
                // The command already exited; there is nothing left to lower.
            }
        }

        var completion = Task.WhenAll(process.WaitForExitAsync(CancellationToken.None), stdout, stderr);
        var timedOut = false;
        var killed = false;
        try
        {
            await completion.WaitAsync(window.Token);
        }
        catch (OperationCanceledException)
        {
            timedOut = true;
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    killed = true;
                }
            }
            catch (InvalidOperationException) when (process.HasExited) { /* Exit raced cancellation. */ }
            try { await completion.WaitAsync(TimeSpan.FromSeconds(2)); }
            catch (TimeoutException)
            {
                // An orphan can retain inherited pipes after its parent has already exited.
                drain.Cancel();
            }
        }

        lock (captureLock)
        {
            captureClosed = true;
            return new ProcessResult(timedOut ? -1 : process.ExitCode, output.ToArray(),
                Encoding.UTF8.GetString(error.ToArray()) + (timedOut ? "\n[harness] timed out" : ""), timedOut) { Killed = killed && process.HasExited };
        }

        async Task ReadAsync(Stream source, MemoryStream destination)
        {
            var buffer = new byte[8192];
            try
            {
                while (await source.ReadAsync(buffer, drain.Token) is var count && count > 0)
                {
                    lock (captureLock)
                    {
                        if (captureClosed) return;
                        destination.Write(buffer, 0, count);
                    }
                }
            }
            catch (OperationCanceledException) when (drain.IsCancellationRequested)
            {
                // The bounded drain grace expired; the captured prefix is the result.
            }
        }
    }
}
