sealed record ProcessResult(int ExitCode, string Output, bool TimedOut);

static class Proc
{
    public static async Task<ProcessResult> RunAsync(string file, IReadOnlyList<string> args, string? cwd, CancellationToken ct, TimeSpan? timeout = null, bool belowNormal = false)
    {
        var psi = new ProcessStartInfo(file)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = cwd ?? "",
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var arg in args) psi.ArgumentList.Add(arg);

        // Verify builds never join a long-lived MSBuild server (see Policy.Environment).
        psi.Environment["DOTNET_CLI_USE_MSBUILD_SERVER"] = "0";

        using var process = new Process { StartInfo = psi };
        var output = new StringBuilder();
        var gate = new Lock();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) lock (gate) output.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) lock (gate) output.AppendLine(e.Data); };
        process.Start();
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

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        using var window = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (timeout is { } t) window.CancelAfter(t);
        try
        {
            await process.WaitForExitAsync(window.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
            ct.ThrowIfCancellationRequested();
            lock (gate) return new ProcessResult(-1, output.ToString() + "\n[harness] timed out", true);
        }

        process.WaitForExit();
        lock (gate) return new ProcessResult(process.ExitCode, output.ToString(), false);
    }
}
