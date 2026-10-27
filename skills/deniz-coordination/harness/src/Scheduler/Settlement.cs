static class Settlement
{
    public static async Task<TaskResult> SettleAsync(IWorkerBackend backend, Ledger ledger, TaskRun run, string? outcomeOverride, CancellationToken ct, Git? git = null)
    {
        var record = run.Record;
        var report = run.Zombie && !run.ReportReady ? new WorkerReport("lost", null, new(), null, false, [], null, false)
            : await backend.CollectAsync(run.SavedWorker(), ct);
        RecordStop(ledger, run, report.StopResult, outcomeOverride ?? report.Outcome);
        if (backend.Name == "codex") run.SaveWorker(run.SavedWorker(), ledger);
        // Codex release owns process/home cleanup; confirm it before touching worker-owned bytes.
        if (backend.Name == "codex" && !run.Zombie)
            RecordStop(ledger, run, await backend.ReleaseAsync(run.SavedWorker(), ct), outcomeOverride ?? report.Outcome);
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
            Prepare = record.Prepare,
            Session = record.Session, RouteRequested = record.Settings.Route?.Value, RouteResolved = record.RouteResolved,
            RoutesUsed = report.RoutesUsed.ToList(),
            Cost = report.Cost, Tokens = report.Usage is { Input: null, Output: null, Reasoning: null, CacheRead: null, CacheWrite: null } ? null : report.Usage,
            TokensComplete = report.UsageComplete,
            ContextWindow = report.ContextWindow,
            Asks = run.Asks, AsksEscalated = run.Escalated, FormsCancelled = run.Forms, Retries = run.Retries,
            TimedOut = run.TimedOut, CancelRequested = run.CancelRequested, WorkerStopped = run.WorkerStopped, ExecutionError = report.ExecutionError,
            BaseSha = record.Spec.Kind == "structured" ? null : record.Workspace.BaseSha,
            CommitMode = record.Spec.Kind == "structured" ? null : Worktree.CommitMode(record), Error = run.StartFailure,
            CapabilityNotes = ConfigResolver.CapabilityNotes(record.Settings) is { Count: > 0 } notes ? notes : null,
        };
        (result.RouteVerified, var drift) = CheckRoutes(record.RouteResolved ?? record.Settings.Route?.Value, result.RoutesUsed);
        result.RouteDrift = drift.Count > 0 ? drift : null;
        if (record.Spec.Kind != "structured") result.WorkerStatus = ReadStatus(report.FinalText);
        // A worker whose stop is unconfirmed may still be writing: no commit and no verify then.
        if (record.Spec.Kind == "structured")
        {
            ledger.Write(run.Id, "collected", new JsonObject { ["outcome"] = outcome, ["routesUsed"] = Json.Array(result.RoutesUsed) });
            await StructuredOutput.ValidateAsync(ledger, run, result, report.FinalText, ct);
            if (!run.Zombie)
            {
                try { Scratch.Delete(record, ledger); }
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
            result.State = run.Zombie && backend.Name == "codex" ? "failed" : "cancelled";
        }
        else
        {
            if (outcome != "succeeded") result.Failures.Add($"outcome:{outcome ?? "unknown"}");
            if (result.Error is not null)
                result.Failures.Add(result.Error.StartsWith("harness commit failed:", StringComparison.Ordinal) ? "commit-failed" : "settle-error");
            if (result.ExecutionError is not null) result.Failures.Add("settle-error");
            if (report.RecoveredErrors is { Count: > 0 } recovered)
            {
                result.RecoveredErrors = [.. recovered];
                (result.Warnings ??= []).Add("recovered-model-error");
            }
            if (string.IsNullOrWhiteSpace(report.FinalText))
            {
                result.Failures.Add("no-final-text");
                if (outcome == "succeeded")
                    result.Error ??= "the session succeeded but left no final assistant text; run `dispatch collect` later";
            }
            if (result.Verify.Any(x => x.Advisory != true && Verifier.Failed(x))) result.Failures.Add("verify-failed");
            if (result.Verify.Any(x => x.Advisory == true && Verifier.Failed(x))) (result.Warnings ??= []).Add("advisory-verify-failed");
            if (result.OutOfScope.Count > 0) result.Failures.Add("out-of-scope");
            if (result.RouteDrift is not null) result.Failures.Add("route-drift");
            // A worker that reports it stopped or was blocked did not do the task, whatever the session outcome says.
            if (result.WorkerStatus is "stopped" or "blocked") result.Failures.Add("worker-stopped");
            if (record.Spec.Kind != "structured" && record.Spec.RawPrompt != true && result.WorkerStatus is null
                && !string.IsNullOrWhiteSpace(report.FinalText))
                (result.Warnings ??= []).Add("worker-status-missing");
            if (record.Spec.Kind == "structured" && result.Validation?.Ok != true) result.Failures.Add("invalid-output");
            result.Failures = result.Failures.Distinct().ToList();
            result.State = result.Failures.Count == 0 ? "done" : "failed";
        }
        ledger.SaveResult(result);
        var writtenCalls = ledger.Read().Count(line => line.Task == run.Id && line.Type == "usage");
        foreach (var call in (report.Calls ?? []).Skip(writtenCalls))
        {
            var data = new JsonObject();
            if (call.Input is { } input) data["input"] = input;
            if (call.CacheRead is { } cacheRead) data["cacheRead"] = cacheRead;
            if (call.Output is { } output) data["output"] = output;
            if (call.Reasoning is { } reasoning) data["reasoning"] = reasoning;
            ledger.Write(run.Id, "usage", data);
        }
        var outcomeData = new JsonObject
        {
            ["outcome"] = outcome, ["cost"] = result.Cost, ["tokensIn"] = result.Tokens?.Input,
            ["tokensOut"] = result.Tokens?.Output, ["tokensComplete"] = result.TokensComplete, ["error"] = result.ExecutionError ?? result.Error,
        };
        if (backend.Name == "codex") outcomeData["workerStopped"] = run.WorkerStopped;
        ledger.Write(run.Id, "outcome", outcomeData);
        return result;
    }

    public static void RecordStop(Ledger ledger, TaskRun run, InterruptResult? stop, string? outcome = null)
    {
        if (stop is null) return;
        var wasZombie = run.Zombie;
        run.WorkerStopped = stop.Stopped;
        run.Zombie = !stop.Stopped;
        if (stop.Stopped) run.WorkerEnded = true;
        else if (!wasZombie)
            ledger.Write(run.Id, "stop-unconfirmed", new JsonObject { ["outcome"] = outcome, ["detail"] = stop.Detail });
    }

    public const string StatusPrefix = "Status:";

    // The last status line of the final text wins; the worker may quote the instruction earlier.
    public static string? ReadStatus(string? text) => text is null ? null : Regex
        .Matches(text, @"^[ \t>*_`]*Status:[ \t*_`]*(done|stopped|blocked)\b", RegexOptions.Multiline | RegexOptions.IgnoreCase)
        .LastOrDefault()?.Groups[1].Value.ToLowerInvariant();

    // Verified only when the backend reported at least one route and every route it reported is the
    // requested one. A requested route without a variant accepts any variant of that model. Drift is a
    // reported route of another model or variant; an unreadable route is unverified, not drift.
    public static (bool Verified, List<string> Drift) CheckRoutes(string? requested, IReadOnlyList<string> used)
    {
        // A requested "#default" names no variant: an agent pin without one is recorded that way.
        var want = requested is null ? null : ParseRoute(requested);
        if (want is { Variant: "default" }) want = want with { Variant = null };
        var parsed = used.Select(text => (Text: text, Route: ParseRoute(text))).ToList();
        var drift = want is null ? [] : parsed.Where(x => x.Route is { } route && !Satisfies(route, want)).Select(x => x.Text).ToList();
        var verified = want is not null && parsed.Count > 0 && parsed.All(x => x.Route is { } route && Satisfies(route, want));
        return (verified, drift);
    }

    static bool Satisfies(RouteRef used, RouteRef requested) =>
        used.Provider == requested.Provider && used.Model == requested.Model
        && (requested.Variant is null || used.VariantOrDefault == requested.VariantOrDefault);

    // Qualified or bare route text; null for an agent route or text that names no model.
    static RouteRef? ParseRoute(string text)
    {
        if (text.StartsWith("codex:", StringComparison.Ordinal))
        {
            try { return CodexBackend.ParseRoute(text); }
            catch (HarnessException) { return null; }
        }
        if (text.StartsWith("opencode:", StringComparison.Ordinal)) text = text["opencode:".Length..];
        return RouteRef.TryParse(text, out var route, out _) ? route : null;
    }
}
