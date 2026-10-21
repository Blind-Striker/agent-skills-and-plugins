static class Settlement
{
    public static async Task<TaskResult> SettleAsync(OpenCodeClient client, Ledger ledger, TaskRun run, string? outcomeOverride, CancellationToken ct)
    {
        var record = run.Record;
        var dir = ledger.TaskDir(run.Id);
        var session = client.Sessions.GetSessionClient(record.Session.Id!);
        SessionInfo info;
        List<SessionMessageAssistant> assistants;
        SessionMessageIdle? idle;
        SessionMessageAssistant? final;
        string? text;
        // A session can report its outcome before its last assistant message is complete: the
        // message is there without usage and without text. Read
        // again until it completes, so "succeeded" never comes with an empty report.
        var reads = 0;
        while (true)
        {
            info = (await session.GetAsync(cancellationToken: ct)).Session;

            // Messages newest first, back to this task's prompt (the user message id).
            assistants = [];
            idle = null;
            await foreach (var message in session.EnumerateMessagesAsync(new SessionMessageListRequest { Order = ListOrder.Descending }, cancellationToken: ct))
            {
                if (message.Id is not null && message.Id == record.BackendState.PromptId) break;
                if (message is SessionMessageAssistant assistant) assistants.Add(assistant);
                if (message is SessionMessageIdle marker && idle is null) idle = marker;
            }

            final = assistants.FirstOrDefault(x => x.Error is null);
            text = final is null
                ? null
                : string.Join("\n\n", final.Content.OfType<SessionMessageAssistantText>().Select(x => x.Text));
            var settled = outcomeOverride is not null || run.CancelRequested || run.TimedOut
                || info.Outcome?.ToString().ToLowerInvariant() != "succeeded"
                || (!string.IsNullOrWhiteSpace(text) && assistants.All(x => x.Tokens is not null));
            if (settled || ++reads >= Constants.SettleReads) break;
            await Task.Delay(Constants.SettleReadDelay, ct);
        }
        var outcome = outcomeOverride
            ?? (run.CancelRequested ? "cancelled" : run.TimedOut ? "timeout" : null)
            ?? info.Outcome?.ToString().ToLowerInvariant()
            ?? "unknown";
        await File.WriteAllTextAsync(Path.Combine(dir, "final.md"), $"""
            # {ledger.RunId}/{run.Id}

            Outcome: {outcome}

            {text ?? "_No final assistant text without an error._"}
            """, ct);

        var result = new TaskResult
        {
            Task = run.Id,
            Run = ledger.RunId,
            Kind = record.Spec.Kind,
            Backend = "opencode",
            Outcome = outcome,
            Session = record.Session,
            RouteRequested = record.Settings.Route?.Value,
            RoutesUsed = assistants.Select(x => RouteRef.From(x.Model).ToString()).Distinct().ToList(),
            Cost = info.Cost,
            Tokens = new TokenTotals
            {
                Input = info.Tokens.Input,
                Output = info.Tokens.Output,
                Reasoning = info.Tokens.Reasoning,
                CacheRead = info.Tokens.Cache.Read,
                CacheWrite = info.Tokens.Cache.Write,
            },
            // A failed step can leave an assistant message without usage; then totals are partial.
            TokensComplete = assistants.All(x => x.Tokens is not null),
            Asks = run.Asks,
            AsksEscalated = run.Escalated,
            FormsCancelled = run.Forms,
            Retries = run.Retries,
            TimedOut = run.TimedOut,
            CancelRequested = run.CancelRequested,
            ExecutionError = idle?.Error?.Message ?? assistants.FirstOrDefault(x => x.Error is not null)?.Error?.Message,
            BaseSha = record.Workspace.BaseSha,
            CommitMode = "harness",
        };

        // Git is the ground truth. The harness commits on its own branch; it never pushes or merges.
        var wt = record.Workspace.Path!;
        var git = Git.ForTask(run.Id);
        if ((await git.CheckAsync(wt, ct, "status", "--porcelain")).Trim().Length > 0)
        {
            await git.CheckAsync(wt, ct, "add", "-A");
            var commit = await git.RunAsync(wt, ct, "commit", "-q", "--no-verify", "-m", $"dispatch: {ledger.RunId}/{run.Id}");
            if (commit.ExitCode != 0) result.Error = $"harness commit failed: {commit.Stderr.Trim()}";
        }

        var head = await git.IdAsync(wt, ct, "rev-parse", "HEAD");
        result.Commit = head == record.Workspace.BaseSha ? null : head;
        result.DiffStat = (await git.CheckAsync(wt, ct, "diff", "--ignore-submodules=none", "--no-ext-diff", "--no-textconv", "--shortstat", record.Workspace.BaseSha!, "HEAD")).Trim();
        result.FilesChanged = (await git.CheckAsync(wt, ct, "diff", "--ignore-submodules=none", "--no-ext-diff", "--no-textconv", "--name-only", "-z", "--no-renames", record.Workspace.BaseSha!, "HEAD"))
            .Split('\0', StringSplitOptions.RemoveEmptyEntries).ToList();
        // Nothing fences a read-only task's edits anymore, so every file it changed is flagged.
        result.OutOfScope = record.Spec.Kind is "review" or "research"
            ? [.. result.FilesChanged]
            : result.FilesChanged.Where(x => !Scope.InScope(x, record.Spec.Scope)).ToList();
        var patch = await git.CheckBytesAsync(wt, ct, "diff", "--ignore-submodules=none", "--submodule=short", "--binary", "--full-index", "--no-color", "--no-ext-diff", "--no-textconv", "--src-prefix=a/", "--dst-prefix=b/", record.Workspace.BaseSha!, "HEAD");
        await File.WriteAllBytesAsync(Path.Combine(dir, "diff.patch"), patch, ct);
        ledger.Write(run.Id, "collected", new JsonObject
        {
            ["outcome"] = outcome,
            ["commit"] = result.Commit,
            ["diffstat"] = result.DiffStat,
            ["files"] = result.FilesChanged.Count,
            ["outOfScope"] = Json.Array(result.OutOfScope),
            ["routesUsed"] = Json.Array(result.RoutesUsed),
        });

        // The harness runs verification itself; a model's "tests pass" is never accepted.
        var log = new StringBuilder();
        if (outcome == "succeeded")
        {
            foreach (var command in record.Spec.Verify ?? [])
            {
                var started = Stopwatch.StartNew();
                var shell = OperatingSystem.IsWindows()
                    ? ("pwsh", new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-Command", command })
                    : ("bash", new[] { "-lc", command });
                var outcomeOfCommand = await Proc.RunAsync(shell.Item1, shell.Item2, wt, ct, Constants.VerifyTimeout, belowNormal: true);
                result.Verify.Add(new VerifyResult { Command = command, ExitCode = outcomeOfCommand.ExitCode, Seconds = Math.Round(started.Elapsed.TotalSeconds, 1), TimedOut = outcomeOfCommand.TimedOut });
                log.AppendLine($"$ {command}").AppendLine("[stdout]").AppendLine(outcomeOfCommand.Output)
                    .AppendLine("[stderr]").AppendLine(outcomeOfCommand.Stderr).AppendLine($"[exit {outcomeOfCommand.ExitCode} after {started.Elapsed.TotalSeconds:0.0}s]").AppendLine();
            }
        }
        else if (record.Spec.Verify is { Count: > 0 })
        {
            log.AppendLine($"verification skipped: outcome {outcome}");
        }

        await File.WriteAllTextAsync(Path.Combine(dir, "verify.log"), log.ToString(), ct);
        ledger.Write(run.Id, "verified", new JsonObject
        {
            ["commands"] = result.Verify.Count,
            ["failed"] = result.Verify.Count(x => x.ExitCode != 0),
        });

        if (outcome == "succeeded" && string.IsNullOrWhiteSpace(text))
        {
            result.Error ??= "the session succeeded but left no final assistant text; run `dispatch collect` later";
        }

        result.State = outcome switch
        {
            "cancelled" => "cancelled",
            "succeeded" when result.Error is null => "done",
            _ => "failed",
        };
        Json.WriteFile(Path.Combine(dir, "result.json"), result, LedgerJson.Files.TaskResult);
        ledger.Write(run.Id, "outcome", new JsonObject
        {
            ["outcome"] = outcome,
            ["cost"] = result.Cost,
            ["tokensIn"] = result.Tokens?.Input,
            ["tokensOut"] = result.Tokens?.Output,
            ["tokensComplete"] = result.TokensComplete,
            ["error"] = result.ExecutionError ?? result.Error,
        });
        return result;
    }
}
