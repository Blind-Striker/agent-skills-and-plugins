static class Verifier
{
    public static string ResolveShell(TaskSpec spec, string? path = null)
    {
        var shell = spec.Shell ?? (OperatingSystem.IsWindows() ? "pwsh" : "bash");
        if (shell is not ("bash" or "pwsh")) throw new HarnessException("shell must be bash or pwsh");
        return Executables.Resolve(shell, path ?? Environment.GetEnvironmentVariable("PATH") ?? "");
    }

    public static async Task VerifyAsync(Ledger ledger, TaskRun run, TaskResult result, CancellationToken ct)
    {
        var log = new StringBuilder();
        if (result.Outcome == "succeeded" && run.Record.Spec.Verify is { Count: > 0 } commands)
        {
            var shell = ResolveShell(run.Record.Spec);
            result.VerifyShell = shell;
            var environment = WorkerEnvironment.Environment(run.Id, ledger.RunId).Variables;
            environment["MSBUILDDISABLENODEREUSE"] = "1";
            environment["UseSharedCompilation"] = "false";
            foreach (var command in commands)
            {
                var started = Stopwatch.StartNew();
                string[] args = (run.Record.Spec.Shell ?? (OperatingSystem.IsWindows() ? "pwsh" : "bash")) == "pwsh"
                    ? ["-NoLogo", "-NoProfile", "-NonInteractive", "-Command", command]
                    : ["--noprofile", "--norc", "-c", command];
                var executable = shell;
                if (!OperatingSystem.IsWindows())
                {
                    executable = Executables.Resolve("nice");
                    args = ["-n", "10", shell, .. args];
                }
                var outcome = await Proc.RunAsync(executable, args, run.Record.Workspace.Path!, ct, Constants.VerifyTimeout, belowNormal: true, environment: environment);
                result.Verify.Add(new VerifyResult { Command = command, ExitCode = outcome.ExitCode, Seconds = Math.Round(started.Elapsed.TotalSeconds, 1), TimedOut = outcome.TimedOut });
                log.AppendLine($"$ {command}").AppendLine("[stdout]").AppendLine(outcome.Output)
                    .AppendLine("[stderr]").AppendLine(outcome.Stderr).AppendLine($"[exit {outcome.ExitCode} after {started.Elapsed.TotalSeconds:0.0}s]").AppendLine();
            }
        }
        else if (run.Record.Spec.Verify is { Count: > 0 }) log.AppendLine($"verification skipped: outcome {result.Outcome}");
        await ledger.WriteArtifactAsync(run.Id, "verify.log", log.ToString(), ct);
        ledger.Write(run.Id, "verified", new JsonObject { ["commands"] = result.Verify.Count, ["failed"] = result.Verify.Count(x => x.ExitCode != 0) });
    }
}
