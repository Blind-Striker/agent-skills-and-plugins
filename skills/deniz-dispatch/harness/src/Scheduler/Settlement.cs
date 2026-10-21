static class Settlement
{
    public static async Task<TaskResult> SettleAsync(IWorkerBackend backend, Ledger ledger, TaskRun run, string? outcomeOverride, CancellationToken ct, Git? git = null)
    {
        var record = run.Record;
        var report = run.Zombie ? new WorkerReport("lost", null, new(), null, false, [], null, false)
            : await backend.CollectAsync(run.SavedWorker(), ct);
        var outcome = outcomeOverride ?? (run.CancelRequested ? "cancelled" : run.TimedOut ? "timeout" : null) ?? report.Outcome;
        var timeout = run.TimedOut ? $"\nTimeout limit (timeoutMinutes: {record.Spec.TimeoutMinutes ?? Constants.DefaultTimeoutMinutes}).\n" : "";
        await ledger.WriteArtifactAsync(run.Id, "final.md", $"""
            # {ledger.RunId}/{run.Id}

            Outcome: {outcome}
            {timeout}

            {report.FinalText ?? "_No final assistant text without an error._"}
            """, ct);
        var result = new TaskResult
        {
            Task = run.Id, Run = ledger.RunId, Kind = record.Spec.Kind, Backend = backend.Name, Outcome = outcome,
            Session = record.Session, RouteRequested = record.Settings.Route?.Value, RoutesUsed = report.RoutesUsed.ToList(),
            Cost = report.Cost, Tokens = report.Usage is { Input: null, Output: null, Reasoning: null, CacheRead: null, CacheWrite: null } ? null : report.Usage,
            TokensComplete = report.UsageComplete,
            Asks = run.Asks, AsksEscalated = run.Escalated, FormsCancelled = run.Forms, Retries = run.Retries,
            TimedOut = run.TimedOut, CancelRequested = run.CancelRequested, WorkerStopped = run.WorkerStopped, ExecutionError = report.ExecutionError,
            BaseSha = record.Spec.Kind == "structured" ? null : record.Workspace.BaseSha,
            CommitMode = record.Spec.Kind == "structured" ? null : Worktree.CommitMode(record), Error = run.StartFailure,
        };
        // A worker whose stop is unconfirmed may still be writing: no commit and no verify then.
        if (record.Spec.Kind == "structured")
        {
            ledger.Write(run.Id, "collected", new JsonObject { ["outcome"] = outcome, ["routesUsed"] = Json.Array(result.RoutesUsed) });
            await StructuredOutput.ValidateAsync(ledger, run, result, report.FinalText, ct);
            if (!run.Zombie)
            {
                try { Scratch.Delete(record); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or HarnessException)
                { result.Error = "scratch cleanup failed: " + error.Message; }
            }
        }
        else if (!run.Zombie)
        {
            try { await Commit.CollectAsync(ledger, run, result, ct, git); }
            catch (GitConfigChangedException) { result.Failures.Add("git-config-changed"); }
            await Verifier.VerifyAsync(ledger, run, result, ct);
        }
        if (run.Zombie) result.Failures.Add("zombie");
        if (outcome == "cancelled")
        {
            result.Failures = result.Failures.Where(x => x == "zombie").Distinct().ToList();
            result.State = "cancelled";
        }
        else
        {
            if (outcome != "succeeded") result.Failures.Add($"outcome:{outcome ?? "unknown"}");
            if (result.Error is not null)
                result.Failures.Add(result.Error.StartsWith("harness commit failed:", StringComparison.Ordinal) ? "commit-failed" : "settle-error");
            if (result.ExecutionError is not null) result.Failures.Add("settle-error");
            if (string.IsNullOrWhiteSpace(report.FinalText))
            {
                result.Failures.Add("no-final-text");
                if (outcome == "succeeded")
                    result.Error ??= "the session succeeded but left no final assistant text; run `dispatch collect` later";
            }
            if (result.Verify.Any(x => x.ExitCode != 0 || x.TimedOut)) result.Failures.Add("verify-failed");
            if (result.OutOfScope.Count > 0) result.Failures.Add("out-of-scope");
            if (record.Spec.Kind == "structured" && result.Validation?.Ok != true) result.Failures.Add("invalid-output");
            result.Failures = result.Failures.Distinct().ToList();
            result.State = result.Failures.Count == 0 ? "done" : "failed";
        }
        ledger.SaveResult(result);
        ledger.Write(run.Id, "outcome", new JsonObject
        {
            ["outcome"] = outcome, ["cost"] = result.Cost, ["tokensIn"] = result.Tokens?.Input,
            ["tokensOut"] = result.Tokens?.Output, ["tokensComplete"] = result.TokensComplete, ["error"] = result.ExecutionError ?? result.Error,
        });
        return result;
    }
}
