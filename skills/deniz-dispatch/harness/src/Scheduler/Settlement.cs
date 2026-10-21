static class Settlement
{
    public static async Task<TaskResult> SettleAsync(IWorkerBackend backend, Ledger ledger, TaskRun run, string? outcomeOverride, CancellationToken ct, Git? git = null)
    {
        var record = run.Record;
        var report = run.Zombie ? new WorkerReport("lost", null, new(), null, false, [], null, false)
            : await backend.CollectAsync(run.SavedWorker(), ct);
        var outcome = outcomeOverride ?? (run.CancelRequested ? "cancelled" : run.TimedOut ? "timeout" : null) ?? report.Outcome;
        await ledger.WriteArtifactAsync(run.Id, "final.md", $"""
            # {ledger.RunId}/{run.Id}

            Outcome: {outcome}

            {report.FinalText ?? "_No final assistant text without an error._"}
            """, ct);
        var result = new TaskResult
        {
            Task = run.Id, Run = ledger.RunId, Kind = record.Spec.Kind, Backend = backend.Name, Outcome = outcome,
            Session = record.Session, RouteRequested = record.Settings.Route?.Value, RoutesUsed = report.RoutesUsed.ToList(),
            Cost = report.Cost, Tokens = report.Usage, TokensComplete = report.UsageComplete,
            Asks = run.Asks, AsksEscalated = run.Escalated, FormsCancelled = run.Forms, Retries = run.Retries,
            TimedOut = run.TimedOut, CancelRequested = run.CancelRequested, WorkerStopped = run.WorkerStopped, ExecutionError = report.ExecutionError,
            BaseSha = record.Workspace.BaseSha, CommitMode = Worktree.CommitMode(record), Error = run.StartFailure,
        };
        if (!run.Zombie)
        {
            await Commit.CollectAsync(ledger, run, result, ct, git);
            await Verifier.VerifyAsync(ledger, run, result, ct);
        }
        if (outcome != "succeeded" && outcome != "cancelled") result.Failures.Add($"outcome:{outcome ?? "unknown"}");
        if (run.Zombie) result.Failures.Add("zombie");
        if (outcome == "succeeded" && string.IsNullOrWhiteSpace(report.FinalText))
            result.Error ??= "the session succeeded but left no final assistant text; run `dispatch collect` later";
        result.State = outcome switch
        {
            "cancelled" => "cancelled", "succeeded" when result.Error is null => "done", _ => "failed",
        };
        ledger.SaveResult(result);
        ledger.Write(run.Id, "outcome", new JsonObject
        {
            ["outcome"] = outcome, ["cost"] = result.Cost, ["tokensIn"] = result.Tokens?.Input,
            ["tokensOut"] = result.Tokens?.Output, ["tokensComplete"] = result.TokensComplete, ["error"] = result.ExecutionError ?? result.Error,
        });
        return result;
    }
}
