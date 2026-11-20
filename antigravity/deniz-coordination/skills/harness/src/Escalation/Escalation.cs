sealed record HeldEscalation(PendingAsk Ask, BlockerRef Blocker, DateTimeOffset Queued);

sealed class Escalation(IReadOnlyDictionary<string, IWorkerBackend> backends, Ledger ledger, Policy policy)
{
    readonly ConcurrentDictionary<string, HeldEscalation> pending = new(StringComparer.Ordinal);

    public async Task AnswerAsync(TaskRun run, WorkerSignal.Ask signal, CancellationToken ct)
    {
        lock (run.Answered) { if (!run.Answered.Add(signal.BlockerRef.AskId)) return; }
        run.Asks++;
        var blocker = signal.BlockerRef;
        var child = blocker.OriginChildId;
        var stopped = run.IsFinal || run.State is TaskState.Settling or TaskState.Interrupted;
        var readOnly = (run.Record.Settings.Access?.Value ?? run.Record.Spec.Access) == "read-only";
        var readOnlyShell = readOnly && signal.Action is "shell" or "bash";
        var shellDeclined = readOnlyShell && (run.Record.Spec.Kind == "structured"
            || (run.Record.Settings.ReadOnlyShell?.Value ?? run.Record.Spec.ReadOnlyShell ?? "decline") != "escalate");
        var readOnlyWrite = readOnly
            && (signal.Action == "edit" || signal.Action == "permissions"
                && signal.Resources.Any(resource => resource.StartsWith("fileSystem.write:", StringComparison.Ordinal)));
        var denied = policy.DenialFor(signal.Action, signal.Resources);
        if (!stopped && !readOnlyWrite && !shellDeclined && denied is null && policy.EscalationFor(signal.Action, signal.Resources) is { } rule)
        {
            run.Escalated++;
            var askId = $"{run.Id}-{run.Asks}";
            var ask = new PendingAsk
            {
                Run = ledger.RunId,
                Task = run.Id,
                Backend = blocker.Backend,
                Session = child ?? blocker.WorkerId,
                RootSession = blocker.WorkerId,
                AskId = askId,
                BackendRef = new JsonObject { ["requestId"] = blocker.AskId },
                Action = signal.Action,
                Resources = signal.Resources.ToList(),
                Rule = rule.Name!,
                Time = Json.Timestamp(),
                Reply = ["reply", ledger.RunId, askId, "once|reject"],
            };
            if (!Locations.Same(Locations.LedgerRoot, Path.Combine(Locations.StateRoot, "ledgers")))
                ask.Reply.AddRange(["--ledger", Locations.LedgerRoot]);
            pending[askId] = new(ask, blocker, DateTimeOffset.UtcNow);
            AskQueue.Write(ask);
            ledger.Write(run.Id, "escalated", new JsonObject
            {
                ["id"] = askId,
                ["session"] = child,
                ["action"] = signal.Action,
                ["resources"] = Json.Array(signal.Resources),
                ["rule"] = rule.Name,
                ["file"] = Path.Combine(Locations.AsksDir(ledger.RunId), $"{askId}.json"),
            });
            return;
        }
        BlockerAnswer answer = stopped ? new BlockerAnswer.Reject("This task has ended; do not execute further calls.")
            : readOnlyWrite ? new BlockerAnswer.Reject("This task is read-only; write access is not permitted.")
            : readOnlyShell ? new BlockerAnswer.Reject("This task is read-only; shell access requires readOnlyShell escalate and a matching escalation rule.")
            : denied is not null ? new BlockerAnswer.Reject($"This action is denied by rule {denied.Name}.")
            : !readOnly && blocker.Backend == "opencode" ? new BlockerAnswer.Once() : new BlockerAnswer.Reject(Policy.LeadDeclined);
        var reply = await backends[blocker.Backend].AnswerAsync(blocker, answer, ct);
        ledger.Write(run.Id, "permission", new JsonObject
        {
            ["id"] = blocker.AskId,
            ["session"] = child,
            ["action"] = signal.Action,
            ["resources"] = Json.Array(signal.Resources),
            ["answer"] = answer is BlockerAnswer.Once ? "once" : "reject",
            ["by"] = "harness",
            ["status"] = reply.Status,
        });
    }

    public async Task CancelFormAsync(TaskRun run, WorkerSignal.Form signal, CancellationToken ct)
    {
        lock (run.Answered) { if (!run.Answered.Add(signal.BlockerRef.AskId)) return; }
        run.Forms++;
        var reply = await backends[signal.BlockerRef.Backend].AnswerAsync(signal.BlockerRef, new BlockerAnswer.Cancel(Policy.NoHuman), ct);
        ledger.Write(run.Id, "form", new JsonObject
        {
            ["id"] = signal.BlockerRef.AskId,
            ["session"] = signal.BlockerRef.OriginChildId,
            ["title"] = signal.Title,
            ["answer"] = "cancel",
            ["status"] = reply.Status,
        });
    }

    public void Close(WorkerSignal.AskClosed closed)
    {
        var match = pending.FirstOrDefault(x => x.Value.Ask.Task == closed.TaskId && x.Value.Blocker.AskId == closed.AskId);
        if (match.Key is null || !pending.TryRemove(match.Key, out var held)) return;
        AskQueue.Remove(ledger.RunId, held.Ask.AskId);
        if (closed.By == "gone")
            ledger.Write(held.Ask.Task, "escalation-dropped", new JsonObject { ["id"] = held.Ask.AskId, ["action"] = held.Ask.Action, ["reason"] = "no longer pending on the server" });
        else
            ledger.Write(held.Ask.Task, "permission", new JsonObject
            {
                ["id"] = held.Ask.AskId,
                ["action"] = held.Ask.Action,
                ["answer"] = closed.Answer is BlockerAnswer.Once ? "once" : "reject",
                ["by"] = "server"
            });
    }

    public async Task CheckDecisionsAsync(CancellationToken ct)
    {
        foreach (var decision in AskQueue.TakeDecisions(ledger.RunId, (file, message) =>
            ledger.Write(null, "error", new JsonObject { ["while"] = "decision", ["file"] = file, ["message"] = message })))
        {
            try
            {
                if (!pending.TryRemove(decision.AskId, out var held))
                {
                    ledger.Write(null, "decision-refused", new JsonObject { ["askId"] = decision.AskId, ["reason"] = "unknown-ask" });
                    continue;
                }
                var ask = held.Ask;
                var reject = decision.Decision == "reject";
                BlockerAnswer answer = reject ? new BlockerAnswer.Reject(decision.Message ?? Policy.LeadDeclined) : new BlockerAnswer.Once();
                var reply = await backends[held.Blocker.Backend].AnswerAsync(held.Blocker, answer, ct);
                AskQueue.Remove(ledger.RunId, ask.AskId);
                if (!reply.Confirmed)
                    throw new HarnessException($"answer to {ask.AskId} was not confirmed: HTTP {reply.Status}");
                ledger.Write(ask.Task, "permission", new JsonObject
                {
                    ["id"] = ask.AskId,
                    ["session"] = held.Blocker.OriginChildId,
                    ["action"] = ask.Action,
                    ["resources"] = Json.Array(ask.Resources),
                    ["answer"] = reject ? "reject" : "once",
                    ["by"] = "operator",
                    ["message"] = reject ? decision.Message ?? Policy.LeadDeclined : null,
                    ["waitedSeconds"] = Math.Round((DateTimeOffset.UtcNow - held.Queued).TotalSeconds, 1),
                    ["status"] = reply.Status,
                });
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            { ledger.Write(null, "error", new JsonObject { ["while"] = "decision", ["id"] = decision.AskId, ["message"] = ex.Message }); }
        }
    }

    public void Drop(string task, string reason)
    {
        foreach (var held in pending.Values.Where(x => x.Ask.Task == task).ToList())
        {
            if (!pending.TryRemove(held.Ask.AskId, out _)) continue;
            AskQueue.Remove(ledger.RunId, held.Ask.AskId);
            ledger.Write(task, "escalation-dropped", new JsonObject { ["id"] = held.Ask.AskId, ["action"] = held.Ask.Action, ["reason"] = reason });
        }
    }

    public void CheckCancelRequests(IReadOnlyList<TaskRun> tasks)
    {
        var dir = Locations.CancelDir(ledger.RunId);
        if (!Directory.Exists(dir)) return;
        foreach (var file in Directory.GetFiles(dir, "*.json"))
        {
            try
            {
                var request = JsonSerializer.Deserialize(File.ReadAllText(file), ControlJson.Files.CancelRequest)
                    ?? throw new JsonException("expected a cancel request");
                var task = tasks.FirstOrDefault(x => x.Id == request.Task);
                if (request.Run != ledger.RunId || task is null || task.IsFinal)
                    throw new JsonException("cancel must name a non-final task of this run");
                task.CancelRequested = true;
                task.PrepareCancellation?.Cancel();
                ledger.Write(task.Id, "cancel", new JsonObject { ["time"] = request.Time, ["state"] = task.State.ToString().ToLowerInvariant() });
            }
            catch (JsonException ex)
            {
                ledger.Write(null, "error", new JsonObject { ["while"] = "cancel", ["file"] = Path.GetFileName(file), ["message"] = ex.Message });
            }
            File.Delete(file);
        }
    }
}
