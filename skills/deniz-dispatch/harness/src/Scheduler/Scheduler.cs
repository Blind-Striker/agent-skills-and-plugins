sealed class Scheduler(IReadOnlyDictionary<string, IWorkerBackend> backends, Ledger ledger, string repo, AllowList allow, Policy policy, int max, CancellationToken ct, Git? git = null)
{
    readonly Escalation escalation = new(backends, ledger, policy);
    readonly ConcurrentQueue<Task> work = new();
    readonly HashSet<WorkerSignal> observed = [];
    readonly Dictionary<string, List<WorkerSignal>> pending = new(StringComparer.Ordinal);
    readonly Dictionary<(string Task, string Turn, string Id), DateTimeOffset> toolStarts = [];
    readonly HashSet<(string Task, string Turn, string Id)> completedTools = [];
    List<TaskRun> runs = [];
    int watchdogFailures;

    IWorkerBackend Backend(TaskRun run) => backends[run.Record.Settings.Backend?.Value ?? "opencode"];
    TaskStarter Starter(TaskRun run) => new(Backend(run), ledger, repo, allow, policy, git);

    public async Task<int> RunAsync(List<TaskRun> tasks)
    {
        runs = tasks;
        return await LoopAsync();
    }

    public async Task<int> ResumeAsync(List<TaskRun> tasks, bool proceed)
    {
        runs = tasks;
        try
        {
            foreach (var run in runs.Where(x => x.Record.Session.Id is not null))
            {
                run.State = TaskState.Dispatching;
                Track(run, ResumeOneAsync(run, proceed));
            }
            return await LoopAsync();
        }
        finally { await DrainAsync(); }
    }

    async Task ResumeOneAsync(TaskRun run, bool proceed)
    {
        try
        {
            var backend = Backend(run);
            var result = await backend.ReattachAsync(run.SavedWorker(), ct);
            if (result is ReattachResult.Interrupted && proceed && !run.CancelRequested && !run.TimedOut)
            {
                Starter(run).CheckAllowed(run);
                var (variables, removed) = WorkerEnvironment.Environment(run.Id, ledger.RunId);
                ledger.Write(run.Id, "environment", new JsonObject { ["variables"] = variables.Count, ["removed"] = Json.Array(removed) });
                var prompt = new WorkerPrompt(run.Record.Spec.RawPrompt == true ? "Continue the task from where you stopped."
                    : "Continue the task from where you stopped. The standing instructions and the task above still apply.", true);
                var worker = await backend.PromptAsync(run.SavedWorker(), prompt, ct);
                run.SaveWorker(worker, ledger);
                var data = Prompt.Event(prompt.Text, worker.Turn!, raw: run.Record.Spec.RawPrompt == true, isContinue: true);
                data["promptId"] = worker.Turn;
                data["resume"] = true;
                ledger.Write(run.Id, "prompt", data);
                run.Deadline = DateTimeOffset.UtcNow.AddMinutes(run.Record.Spec.TimeoutMinutes ?? Constants.DefaultTimeoutMinutes);
                run.State = TaskState.Running;
                await backend.PollAsync([worker], ct);
            }
            else if (result is ReattachResult.Live) run.State = TaskState.Running;
            else if (result is ReattachResult.Unreadable unreadable)
            {
                await backend.ReleaseAsync(run.SavedWorker(), ct);
                run.WorkerEnded = true;
                Finish(run, TaskState.Failed, "unknown", unreadable.Reason);
            }
            else
            {
                run.WorkerEnded = true;
                var outcome = result switch
                {
                    ReattachResult.Ended ended => ended.Outcome,
                    ReattachResult.Interrupted => "interrupted",
                    _ => "unknown",
                };
                BeginSettle(run, run.CancelRequested ? "cancelled" : run.TimedOut ? "timeout" : outcome);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (run.State == TaskState.Running)
                ledger.Write(run.Id, "error", new JsonObject { ["while"] = "resume poll", ["message"] = ex.Message });
            else await FailStartAsync(run, ex, "resume");
            ct.ThrowIfCancellationRequested();
        }
    }

    async Task<int> LoopAsync()
    {
        try
        {
            var watchdog = DateTimeOffset.UtcNow;
            while (runs.Any(x => !x.IsFinal && x.State != TaskState.Interrupted && !x.ReleaseUnconfirmed))
            {
                ct.ThrowIfCancellationRequested();
                foreach (var failed in work.Where(x => x.IsFaulted).ToList()) await failed;
                StartReady();
                if (!runs.Any(x => !x.ReleaseUnconfirmed && x.State is TaskState.Dispatching or TaskState.Running or TaskState.Settling)) break;
                CheckCancelRequests();
                await escalation.CheckDecisionsAsync(ct);
                var received = false;
                foreach (var backend in backends.Values)
                {
                    while (backend.Signals.TryRead(out var signal))
                    {
                        received = true;
                        await HandleAsync(signal);
                    }
                    if (backend.Signals.Completion.IsFaulted) await backend.Signals.Completion;
                }
                foreach (var id in pending.Keys.ToList())
                    if (runs.First(x => x.Id == id).State != TaskState.Dispatching && pending.Remove(id, out var buffered))
                        foreach (var signal in buffered) await HandleAsync(signal);
                if (DateTimeOffset.UtcNow - watchdog >= Constants.Watchdog)
                {
                    watchdog = DateTimeOffset.UtcNow;
                    await PollAsync();
                }
                foreach (var run in runs.Where(x => x.State == TaskState.Running && !x.ReleaseUnconfirmed).ToList())
                {
                    if (run.CancelRequested && !run.CancelSent)
                    {
                        run.CancelSent = true;
                        await InterruptAsync(run, InterruptKind.Cancel);
                    }
                    if (run.Deadline <= DateTimeOffset.UtcNow && !run.TimedOut)
                    {
                        run.TimedOut = true;
                        await InterruptAsync(run, InterruptKind.Timeout);
                    }
                    if (run.NextCheck <= DateTimeOffset.UtcNow)
                    {
                        run.NextCheck = null;
                        await PollAsync();
                    }
                }
                if (!received) await Task.Delay(TimeSpan.FromMilliseconds(20), ct);
            }
            return runs.All(x => x.State == TaskState.Done) ? 0 : 1;
        }
        catch (Exception ex) when (ex is OperationCanceledException or HarnessException)
        {
            ledger.Write(null, "aborted", new JsonObject { ["reason"] = ex.Message, ["running"] = Json.Array(runs.Where(x => !x.IsFinal).Select(x => x.Id)) });
            throw;
        }
        finally { await DrainAsync(); }
    }

    async Task DrainAsync()
    {
        // A start can schedule settlement. Drain successive snapshots until all owned work ended.
        var count = 0;
        while (count != work.Count)
        {
            var snapshot = work.ToArray();
            count = snapshot.Length;
            await Task.WhenAll(snapshot);
        }
    }

    void StartReady()
    {
        foreach (var run in runs.Where(x => x.State == TaskState.Pending).ToList())
        {
            if (runs.Count(x => x.State is TaskState.Dispatching or TaskState.Running or TaskState.Settling) >= max) return;
            if (run.CancelRequested) { Finish(run, TaskState.Cancelled, null, "cancelled before dispatch"); continue; }
            if (Worktree.InPlace(run.Record) && runs.TakeWhile(x => x != run).Any(x => Worktree.InPlace(x.Record) && !x.IsFinal)) continue;
            if (run.Record.Spec.ReviewOf is { } authorId)
            {
                var author = runs.FirstOrDefault(x => x.Id == authorId);
                var state = author?.State ?? StateFromLedger(authorId);
                if (state is TaskState.Pending or TaskState.Dispatching or TaskState.Running or TaskState.Settling or TaskState.Interrupted) continue;
                var result = ledger.LoadResult(authorId);
                if (state != TaskState.Done || result?.Outcome != "succeeded" || result.Commit is null)
                {
                    Finish(run, TaskState.Cancelled, null, $"reviewed task {authorId} produced no successful commit");
                    continue;
                }
            }
            run.State = TaskState.Dispatching;
            Track(run, StartAsync(run));
        }
    }

    void Track(TaskRun run, Task task) { run.Work = task; work.Enqueue(task); }

    async Task StartAsync(TaskRun run)
    {
        try
        {
            await Starter(run).StartAsync(run, ct);
            await Backend(run).PollAsync([run.SavedWorker()], ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (run.State == TaskState.Running)
            {
                ledger.Write(run.Id, "error", new JsonObject { ["while"] = "dispatch", ["message"] = ex.Message });
                return;
            }
            await FailStartAsync(run, ex, "dispatch");
            ct.ThrowIfCancellationRequested();
        }
    }

    async Task FailStartAsync(TaskRun run, Exception ex, string operation)
    {
        if (ex is WorkspaceDirtyException)
            ledger.SaveResult(new TaskResult { Run = ledger.RunId, Task = run.Id, Kind = run.Record.Spec.Kind,
                State = "failed", CommitMode = Worktree.CommitMode(run.Record), Failures = ["workspace-dirty"], Error = ex.Message });
        if (ex is WorkerCreationException creation)
        {
            run.SaveWorker(creation.Worker, ledger);
            ledger.Write(run.Id, "session", new JsonObject { ["sessionId"] = creation.Worker.Id });
        }
        if (run.Worker is { } worker && !run.WorkerEnded)
        {
            try { await Backend(run).InterruptAsync(worker, InterruptKind.Cancel, CancellationToken.None); }
            catch (Exception interruptError)
            { ledger.Write(run.Id, "error", new JsonObject { ["while"] = "interrupt", ["message"] = interruptError.Message }); }
            try { await Backend(run).ReleaseAsync(worker, CancellationToken.None); }
            catch (Exception releaseError)
            {
                ledger.Write(run.Id, "error", new JsonObject { ["while"] = "release", ["message"] = releaseError.Message });
                run.State = TaskState.Dispatching;
                run.ReleaseUnconfirmed = true;
                ledger.Write(run.Id, "error", new JsonObject { ["while"] = operation, ["message"] = $"{operation} failed; worker release unconfirmed: {ex.Message}" });
                return;
            }
            run.WorkerEnded = true;
        }
        Finish(run, TaskState.Failed, "unknown", $"{operation} failed: {ex.GetType().Name}: {ex.Message}");
    }

    TaskState StateFromLedger(string task) => ledger.TaskStates().GetValueOrDefault(task) switch
    {
        "done" => TaskState.Done, "failed" => TaskState.Failed, "cancelled" => TaskState.Cancelled, "interrupted" => TaskState.Interrupted, _ => TaskState.Pending,
    };

    async Task HandleAsync(WorkerSignal signal)
    {
        try { await HandleCoreAsync(signal); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        { ledger.Write(null, "error", new JsonObject { ["while"] = signal.GetType().Name, ["message"] = ex.Message }); }
    }

    async Task HandleCoreAsync(WorkerSignal signal)
    {
        if (signal is WorkerSignal.Fault fault)
        {
            ledger.Write(fault.TaskId, "error", new JsonObject { ["while"] = fault.Operation, ["message"] = fault.Message });
            return;
        }
        if (signal is WorkerSignal.Host host)
        {
            if (host.State == "disconnected" || host.Attempt > 0)
            {
                ledger.Write(null, "bus", host.State == "disconnected" ? new JsonObject { ["state"] = "disconnected", ["reason"] = host.Reason }
                    : new JsonObject { ["state"] = "reconnected" });
                await PollAsync();
            }
            return;
        }
        if (signal is not WorkerSignal.TaskSignal task || runs.FirstOrDefault(x => x.Id == task.TaskId) is not { } run) return;
        if (run.State == TaskState.Dispatching)
        {
            if (!pending.TryGetValue(run.Id, out var buffered)) pending[run.Id] = buffered = [];
            buffered.Add(signal);
            return;
        }
        if (pending.Remove(run.Id, out var admitted))
            foreach (var queued in admitted) await HandleAsync(queued);
        if (signal is WorkerSignal.Ended or WorkerSignal.Status or WorkerSignal.ToolCall && task.Turn != run.Worker?.Turn) return;
        if (signal is WorkerSignal.Ended or WorkerSignal.Status or WorkerSignal.Retry && !observed.Add(signal)) return;
        switch (signal)
        {
            case WorkerSignal.ToolCall tool when !run.IsFinal:
                var key = (tool.TaskId, tool.Turn, tool.Id);
                if (completedTools.Contains(key)) break;
                if (tool.Marker == "start") toolStarts.TryAdd(key, tool.Time);
                else if (completedTools.Add(key))
                {
                    var duration = toolStarts.Remove(key, out var start) ? Math.Max(0, (tool.Time - start).TotalSeconds) : 0;
                    ledger.Write(run.Id, "tool", new ToolEvent(tool.Tool, tool.Resource, tool.Outcome!, duration).Data());
                }
                break;
            case WorkerSignal.Ask ask: await escalation.AnswerAsync(run, ask, ct); break;
            case WorkerSignal.AskClosed closed: escalation.Close(closed); break;
            case WorkerSignal.Form form: await escalation.CancelFormAsync(run, form, ct); break;
            case WorkerSignal.Retry retry when !run.IsFinal:
                run.Retries++;
                ledger.Write(run.Id, "retry", new JsonObject { ["attempt"] = retry.Attempt, ["message"] = retry.Message, ["next"] = retry.Next?.ToUnixTimeMilliseconds(), ["reason"] = retry.Reason, ["provider"] = retry.Provider });
                break;
            case WorkerSignal.Status status when !run.IsFinal:
                var data = new JsonObject { ["execution"] = status.Execution };
                if (status.Execution == "failed") data["error"] = status.Detail is null ? null : JsonNode.Parse(status.Detail);
                if (status.Execution == "interrupted") data["reason"] = status.Detail;
                ledger.Write(run.Id, "status", data);
                run.NextCheck = DateTimeOffset.UtcNow.AddSeconds(2);
                break;
            case WorkerSignal.Ended ended when !run.IsFinal && run.State != TaskState.Settling:
                run.WorkerEnded = true;
                var decision = OutcomeMapping.FromEnded(ended.Outcome, run.CancelRequested ? InterruptKind.Cancel : run.TimedOut ? InterruptKind.Timeout : null, reason: ended.Reason);
                if (decision.Action == OutcomeAction.Interrupted)
                {
                    escalation.Drop(run.Id, "task interrupted");
                    run.State = TaskState.Interrupted;
                    ledger.Write(run.Id, "interrupted", new JsonObject { ["reason"] = decision.Reason });
                }
                else BeginSettle(run, decision.Outcome);
                break;
        }
    }

    async Task PollAsync()
    {
        try
        {
            foreach (var backend in backends.Values)
                await backend.PollAsync(runs.Where(x => x.State == TaskState.Running && Backend(x) == backend).Select(x => x.SavedWorker()).ToList(), ct);
            watchdogFailures = 0;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ledger.Write(null, "error", new JsonObject { ["while"] = "watchdog", ["message"] = ex.Message });
            if (++watchdogFailures >= 3) throw new HarnessException($"the private server stopped answering; run `resume {ledger.RunId}` after checking it");
        }
    }

    void CheckCancelRequests()
    {
        var control = Locations.ControlDir(ledger.RunId);
        foreach (var run in runs.Where(x => !x.IsFinal && !x.CancelRequested))
        {
            var file = Path.Combine(control, $"{run.Id}.cancel");
            if (!File.Exists(file)) continue;
            File.Delete(file);
            run.CancelRequested = true;
            if (run.State != TaskState.Running) ledger.Write(run.Id, "cancel", new JsonObject { ["state"] = run.State.ToString().ToLowerInvariant() });
        }
    }

    async Task InterruptAsync(TaskRun run, InterruptKind kind)
    {
        try
        {
            var response = await Backend(run).InterruptAsync(run.SavedWorker(), kind, ct);
            ledger.Write(run.Id, kind == InterruptKind.Cancel ? "cancel" : "timeout", new JsonObject { ["interrupted"] = response.Interrupted, ["status"] = response.Status });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        { ledger.Write(run.Id, "error", new JsonObject { ["while"] = "interrupt", ["message"] = ex.Message }); }
        run.NextCheck = DateTimeOffset.UtcNow.AddSeconds(2);
    }

    void BeginSettle(TaskRun run, string? outcome)
    {
        if (run.State == TaskState.Settling || run.IsFinal) return;
        run.State = TaskState.Settling;
        Track(run, SettleAsync(run, outcome));
    }

    async Task SettleAsync(TaskRun run, string? outcome)
    {
        try
        {
            var result = await Settlement.SettleAsync(Backend(run), ledger, run, outcome, CancellationToken.None, git);
            Finish(run, result.State switch { "done" => TaskState.Done, "cancelled" => TaskState.Cancelled, _ => TaskState.Failed }, result.Outcome, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        { Finish(run, TaskState.Failed, null, $"settle failed: {ex.GetType().Name}: {ex.Message}"); }
    }

    void Finish(TaskRun run, TaskState state, string? outcome, string? reason)
    {
        if (run.Worker is not null && !run.WorkerEnded)
            throw new InvalidOperationException("cannot finalize a task before its worker ended or was released");
        if (reason is not null && state == TaskState.Failed) ledger.Write(run.Id, "error", new JsonObject { ["message"] = reason });
        escalation.Drop(run.Id, $"task ended ({state.ToString().ToLowerInvariant()})");
        run.State = state;
        ledger.Write(run.Id, "final", new JsonObject { ["state"] = state.ToString().ToLowerInvariant(), ["outcome"] = outcome, ["reason"] = reason });
    }
}
