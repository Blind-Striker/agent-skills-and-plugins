static class Verifier
{
    public static async Task VerifyAsync(Ledger ledger, TaskRun run, TaskResult result, CancellationToken ct)
    {
        var log = new StringBuilder();
        if (result.Outcome == "succeeded")
        {
            foreach (var command in run.Record.Spec.Verify ?? [])
            {
                var started = Stopwatch.StartNew();
                var shell = OperatingSystem.IsWindows()
                    ? ("pwsh", new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-Command", command })
                    : ("bash", new[] { "-lc", command });
                var outcome = await Proc.RunAsync(shell.Item1, shell.Item2, run.Record.Workspace.Path!, ct, Constants.VerifyTimeout, belowNormal: true);
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
