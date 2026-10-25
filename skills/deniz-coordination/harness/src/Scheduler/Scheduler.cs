sealed class Scheduler(IReadOnlyDictionary<string, IWorkerBackend> backends, Ledger ledger, string repo, AllowList allow, Policy policy, int max, CancellationToken ct, Git? git = null, TimeProvider? timeProvider = null,
    Func<TaskRun, string, IReadOnlyDictionary<string, string>, CancellationToken, Task<(VerifyResult Result, string Log, Exception? Error)>>? prepareCommand = null,
    Func<Git, string, string, bool, CancellationToken, Task<string>>? prepareSnapshot = null,
    Func<Git, string, string, bool, CancellationToken, Task<string>>? prepareHead = null)
{
    readonly TimeProvider time = timeProvider ?? TimeProvider.System;
    readonly Escalation escalation = new(backends, ledger, policy);
    readonly ConcurrentQueue<Task> work = new();
    readonly HashSet<WorkerSignal> observed = [];
    readonly Dictionary<string, List<WorkerSignal>> pending = new(StringComparer.Ordinal);
    readonly Dictionary<(string Task, string Turn, string Id), DateTimeOffset> toolStarts = [];
    readonly HashSet<(string Task, string Turn, string Id)> completedTools = [];
    List<TaskRun> runs = [];
    readonly Dictionary<string, int> watchdogFailures = new(StringComparer.Ordinal);
    volatile bool ledgerFailed;

    void Error(TaskRun? run, string operation, Exception ex)
    {
        if (ex is not LedgerWriteException && !ledgerFailed)
        {
            try
            {
                ledger.Write(run?.Id, "error", new JsonObject { ["while"] = operation, ["message"] = ex.Message });
                return;
            }
            catch (LedgerWriteException failure) { ex = failure; }
        }
        Console.Error.WriteLine($"{run?.Id ?? "supervisor"}: {operation}: {ex.Message}");
        ledgerFailed = true;
        if (run is not null && !run.IsFinal) run.State = TaskState.Interrupted;
    }

    IWorkerBackend Backend(TaskRun run) => backends[run.Record.Settings.Backend?.Value ?? "opencode"];
    TaskStarter Starter(TaskRun run) => new(Backend(run), ledger, repo, allow, policy, git, time, prepareCommand, prepareSnapshot, prepareHead);

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
            run.SavedWorker();
            if (run.Record.Spec.Kind == "structured") StructuredOutput.Load(ledger, run);
            if (Scratch.Required(run.Record.Spec)) Scratch.Prepare(run, ledger);
            var result = await backend.ReattachAsync(run.SavedWorker(), ct);
            if (result is ReattachResult.Interrupted && proceed && !run.CancelRequested && !run.TimedOut)
            {
                Starter(run).CheckAllowed(run);
                if (run.Record.Spec.Kind == "structured") StructuredOutput.Load(ledger, run);
                var (variables, removed) = WorkerEnvironment.Environment(run.Id, ledger.RunId, run.Record.Settings.TempRoot?.Value);
                ledger.Write(run.Id, "environment", new JsonObject { ["variables"] = variables.Count, ["removed"] = Json.Array(removed) });
                var prompt = new WorkerPrompt(run.Record.Spec.RawPrompt == true ? "Continue the task from where you stopped."
                    : "Continue the task from where you stopped. The standing instructions and the task above still apply.", true);
                run.PromptAttempted = true;
                var worker = await backend.PromptAsync(run.SavedWorker(), prompt, ct);
                run.SaveWorker(worker, ledger);
                var data = Prompt.Event(prompt.Text, worker.Turn!, raw: run.Record.Spec.RawPrompt == true, isContinue: true);
                data["promptId"] = worker.Turn;
                data["resume"] = true;
                ledger.Write(run.Id, "prompt", data);
                run.Deadline = time.GetUtcNow().AddMinutes(run.Record.Spec.TimeoutMinutes ?? Constants.DefaultTimeoutMinutes);
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
                BeginSettle(run, run.StopKind is not null ? StopOutcome(run) : run.CancelRequested ? "cancelled" : run.TimedOut ? "timeout" : outcome);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (ex is LedgerWriteException) { Error(run, "resume", ex); return; }
            if (run.State == TaskState.Running)
                Error(run, "resume poll", ex);
            else await FailStartAsync(run, ex, "resume");
            ct.ThrowIfCancellationRequested();
        }
    }

    async Task<int> LoopAsync()
    {
        try
        {
            var watchdog = time.GetUtcNow();
            while (!ledgerFailed && runs.Any(x => !x.IsFinal && x.State != TaskState.Interrupted && !x.ReleaseUnconfirmed))
            {
                ct.ThrowIfCancellationRequested();
                foreach (var failed in work.Where(x => x.IsFaulted).ToList()) await failed;
                StartReady();
                if (!runs.Any(x => !x.ReleaseUnconfirmed && x.State is TaskState.Dispatching or TaskState.Running or TaskState.Settling)) break;
                escalation.CheckCancelRequests(runs);
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
                if (time.GetUtcNow() - watchdog >= Constants.Watchdog)
                {
                    watchdog = time.GetUtcNow();
                    await PollAsync();
                }
                foreach (var run in runs.Where(x => x.State == TaskState.Running && !x.ReleaseUnconfirmed).ToList())
                {
                    if (run.StopStarted is null && run.CancelRequested && !run.CancelSent)
                    {
                        run.CancelSent = true;
                        StartStop(run, InterruptKind.Cancel);
                        RequestInterrupt(run, InterruptKind.Cancel);
                    }
                    if (run.State != TaskState.Running) continue;
                    if (run.StopStarted is null && run.Deadline <= time.GetUtcNow() && !run.TimedOut)
                    {
                        run.TimedOut = true;
                        StartStop(run, InterruptKind.Timeout);
                        RequestInterrupt(run, InterruptKind.Timeout);
                    }
                    if (run.State != TaskState.Running) continue;
                    if (run.StopStarted is { } started)
                    {
                        var elapsed = time.GetUtcNow() - started;
                        if (elapsed >= TimeSpan.FromMinutes(2) && !run.HardStopSent)
                        {
                            run.HardStopSent = true;
                            RequestInterrupt(run, InterruptKind.Hard);
                        }
                        else if (elapsed < TimeSpan.FromMinutes(2) && run.NextInterrupt <= time.GetUtcNow() && run.InterruptWork?.IsCompleted != false)
                            RequestInterrupt(run, run.StopKind!.Value, repeat: true);
                        if (run.State == TaskState.Running && elapsed >= TimeSpan.FromMinutes(4))
                        {
                            if (run.InterruptWork is { } interrupt) await interrupt;
                            if (run.State != TaskState.Running) continue;
                            run.Zombie = true;
                            run.WorkerStopped = false;
                            BeginSettle(run, StopOutcome(run));
                        }
                    }
                    if (run.State != TaskState.Running) continue;
                    if (run.NextCheck <= time.GetUtcNow())
                    {
                        run.NextCheck = null;
                        await PollAsync();
                    }
                }
                if (!received) await Task.Delay(TimeSpan.FromMilliseconds(20), time, ct);
            }
            return runs.All(x => x.State == TaskState.Done) ? 0 : 1;
        }
        catch (LedgerWriteException ex)
        {
            Error(null, "supervisor", ex);
            return 1;
        }
        catch (Exception ex) when (ex is OperationCanceledException or HarnessException)
        {
            try { ledger.Write(null, "aborted", new JsonObject { ["reason"] = ex.Message, ["running"] = Json.Array(runs.Where(x => !x.IsFinal).Select(x => x.Id)) }); }
            catch (LedgerWriteException failure) { Error(null, "abort", failure); }
            throw;
        }
        finally
        {
            await DrainAsync();
            if (ledgerFailed)
                foreach (var run in runs.Where(x => !x.IsFinal)) run.State = TaskState.Interrupted;
        }
    }

    async Task DrainAsync()
    {
        // A start can schedule settlement. Drain successive snapshots until all owned work ended.
        var count = 0;
        while (count != work.Count)
        {
            var snapshot = work.ToArray();
            count = snapshot.Length;
            try { await Task.WhenAll(snapshot); }
            catch (LedgerWriteException ex) { Error(null, "drain", ex); }
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
            if (await Starter(run).StartAsync(run, ct) is { } terminal)
            {
                await FailStartCoreAsync(run, new HarnessException(terminal.Error ?? terminal.State), "prepare", terminal);
                return;
            }
            await Backend(run).PollAsync([run.SavedWorker()], ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (ex is LedgerWriteException) { Error(run, "dispatch", ex); return; }
            if (run.State == TaskState.Running)
            {
                Error(run, "dispatch", ex);
                return;
            }
            await FailStartAsync(run, ex, "dispatch");
            ct.ThrowIfCancellationRequested();
        }
    }

    async Task FailStartAsync(TaskRun run, Exception ex, string operation)
    {
        try { await FailStartCoreAsync(run, ex, operation); }
        catch (LedgerWriteException failure) { Error(run, operation, failure); }
    }

    async Task FailStartCoreAsync(TaskRun run, Exception ex, string operation, TaskResult? prepareResult = null)
    {
        if (prepareResult is not null)
        {
            ledger.SaveResult(prepareResult);
            Finish(run, prepareResult.State == "cancelled" ? TaskState.Cancelled : TaskState.Failed, prepareResult.Outcome, prepareResult.Error);
            return;
        }
        if (run.PromptAttempted && run.Worker is not null)
        {
            run.StartFailure = $"{operation} failed: {ex.GetType().Name}: {ex.Message}";
            Error(run, operation, ex);
            if (ledgerFailed) return;
            run.State = TaskState.Running;
            StartStop(run, InterruptKind.Cancel);
            await InterruptAsync(run, InterruptKind.Cancel);
            return;
        }
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
        if (ex is FrozenInputException)
            ledger.SaveResult(new TaskResult { Run = ledger.RunId, Task = run.Id, Kind = run.Record.Spec.Kind,
                State = "failed", Failures = ["invalid-output"], Error = ex.Message,
                Validation = new() { Errors = [new() { Message = ex.Message }] } });
        Finish(run, TaskState.Failed, "unknown", $"{operation} failed: {ex.GetType().Name}: {ex.Message}");
    }

    TaskState StateFromLedger(string task) => ledger.TaskStates().GetValueOrDefault(task) switch
    {
        "done" => TaskState.Done,
        "failed" => TaskState.Failed,
        "cancelled" => TaskState.Cancelled,
        "interrupted" => TaskState.Interrupted,
        _ => TaskState.Pending,
    };

    async Task HandleAsync(WorkerSignal signal)
    {
        try { await HandleCoreAsync(signal); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        { Error(signal is WorkerSignal.TaskSignal task ? runs.FirstOrDefault(x => x.Id == task.TaskId) : null, signal.GetType().Name, ex); }
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
        if (signal is WorkerSignal.Ended or WorkerSignal.StopUnconfirmed or WorkerSignal.Status or WorkerSignal.ToolCall && task.Turn != run.Worker?.Turn) return;
        if (run.Zombie && signal is not (WorkerSignal.Ask or WorkerSignal.Form)) return;
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
            case WorkerSignal.Form form when run.IsFinal || run.State is TaskState.Settling or TaskState.Interrupted:
                await RejectEndedFormAsync(run, form);
                break;
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
                run.NextCheck = time.GetUtcNow().AddSeconds(2);
                break;
            // A stop already under way (cancel, timeout) owns its own zombie decision.
            case WorkerSignal.StopUnconfirmed unconfirmed when run.State == TaskState.Running && run.StopStarted is null:
                ledger.Write(run.Id, "stop-unconfirmed", new JsonObject { ["outcome"] = unconfirmed.Outcome, ["detail"] = unconfirmed.Detail });
                escalation.Drop(run.Id, "worker stop unconfirmed");
                run.Zombie = true;
                run.WorkerStopped = false;
                run.ReportReady = true;
                BeginSettle(run, unconfirmed.Outcome);
                break;
            case WorkerSignal.Ended ended when !run.IsFinal && run.State != TaskState.Settling:
                run.WorkerEnded = true;
                run.InterruptCancellation?.Cancel();
                if (run.InterruptWork is { } interrupt) await interrupt;
                if (run.HardStopSent) run.WorkerStopped = true;
                var decision = OutcomeMapping.FromEnded(ended.Outcome, run.StopKind ?? (run.CancelRequested ? InterruptKind.Cancel : run.TimedOut ? InterruptKind.Timeout : null), reason: ended.Reason);
                if (decision.Action == OutcomeAction.Interrupted)
                {
                    escalation.Drop(run.Id, "task interrupted");
                    run.State = TaskState.Interrupted;
                    ledger.Write(run.Id, "interrupted", new JsonObject { ["reason"] = decision.Reason });
                }
                else BeginSettle(run, run.StartFailure is not null ? "unknown" : decision.Outcome);
                break;
        }
    }

    async Task PollAsync()
    {
        foreach (var backend in backends.Values)
        {
            try
            {
                var active = runs.Where(x => x.State == TaskState.Running && Backend(x) == backend).ToList();
                await backend.PollAsync(active.Select(x => x.SavedWorker()).ToList(), ct);
                // Codex recovery needs the latest observed descendant identities on disk.
                if (backend.Name == "codex")
                    foreach (var run in active) run.SaveWorker(run.SavedWorker(), ledger);
                watchdogFailures[backend.Name] = 0;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                ledger.Write(null, "error", new JsonObject { ["while"] = "watchdog", ["backend"] = backend.Name, ["message"] = ex.Message });
                var failures = watchdogFailures[backend.Name] = watchdogFailures.GetValueOrDefault(backend.Name) + 1;
                var server = backend.Name == "codex" ? "codex app-server" : $"{backend.Name} server";
                if (failures >= Constants.WatchdogFailureLimit) throw new HarnessException($"the {server} stopped answering; run `resume {ledger.RunId}` after checking it");
            }
        }
    }

    async Task RejectEndedFormAsync(TaskRun run, WorkerSignal.Form form)
    {
        lock (run.Answered) { if (!run.Answered.Add(form.BlockerRef.AskId)) return; }
        run.Forms++;
        var reply = await Backend(run).AnswerAsync(form.BlockerRef, new BlockerAnswer.Reject("the task has ended"), ct);
        ledger.Write(run.Id, "form", new JsonObject { ["id"] = form.BlockerRef.AskId, ["title"] = form.Title, ["answer"] = "reject", ["status"] = reply.Status });
    }

    void StartStop(TaskRun run, InterruptKind kind)
    {
        run.StopStarted = time.GetUtcNow();
        run.StopKind = kind;
    }

    static string StopOutcome(TaskRun run) => run.StartFailure is not null ? "unknown" : run.StopKind == InterruptKind.Timeout ? "timeout" : "cancelled";

    void RequestInterrupt(TaskRun run, InterruptKind kind, bool repeat = false)
    {
        run.InterruptCancellation?.Cancel();
        var task = InterruptAsync(run, kind, repeat);
        run.InterruptWork = task;
        work.Enqueue(task);
    }

    async Task InterruptAsync(TaskRun run, InterruptKind kind, bool repeat = false)
    {
        InterruptResult response;
        var logged = false;
        using var request = CancellationTokenSource.CreateLinkedTokenSource(ct);
        run.InterruptCancellation = request;
        try
        {
            var deadline = run.StopStarted!.Value + TimeSpan.FromMinutes(kind == InterruptKind.Hard ? 4 : 2);
            var responseTask = Backend(run).InterruptAsync(run.SavedWorker(), kind, request.Token);
            if (!responseTask.IsCompleted && kind != InterruptKind.Hard && !repeat)
            {
                ledger.Write(run.Id, kind == InterruptKind.Cancel ? "cancel" : "timeout", new JsonObject { ["interrupted"] = null, ["status"] = null });
                logged = true;
            }
            var remaining = deadline - time.GetUtcNow();
            response = await responseTask.WaitAsync(remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero, time, request.Token);
        }
        catch (Exception ex) when (ex is not LedgerWriteException && !ct.IsCancellationRequested)
        {
            if (ex is not OperationCanceledException) Error(run, "interrupt", ex);
            response = new(false, ex is TimeoutException ? "stop confirmation deadline passed" : $"{ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            request.Cancel();
            if (run.InterruptCancellation == request) run.InterruptCancellation = null;
        }
        if (kind == InterruptKind.Hard)
        {
            run.WorkerStopped = response.Stopped;
            ledger.Write(run.Id, "hard-stop", new JsonObject { ["kind"] = run.StopKind == InterruptKind.Timeout ? "timeout" : "cancel", ["stopped"] = response.Stopped, ["detail"] = response.Detail });
        }
        else if (!repeat && !logged)
            ledger.Write(run.Id, kind == InterruptKind.Cancel ? "cancel" : "timeout", new JsonObject { ["interrupted"] = response.Interrupted, ["status"] = response.Status });
        run.NextInterrupt = time.GetUtcNow() + Constants.Watchdog;
        run.NextCheck = time.GetUtcNow().AddSeconds(2);
        if (response.Stopped && !run.WorkerEnded && !run.IsFinal)
        {
            run.WorkerEnded = true;
            BeginSettle(run, StopOutcome(run));
        }
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
            if (run.State == TaskState.Done)
            {
                try { await Backend(run).ReleaseAsync(run.SavedWorker(), CancellationToken.None); }
                catch (Exception ex) { Error(run, "release", ex); }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Error(run, "settle", ex);
            if (!ledgerFailed) Finish(run, TaskState.Failed, outcome, $"settle failed: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            if (run.IsFinal && run.Worker is { Backend: "opencode" } worker
                && worker.State["host"]?.GetValue<string>()?.StartsWith("fence=", StringComparison.Ordinal) == true)
                try
                {
                    if (!(await Backend(run).InterruptAsync(worker, InterruptKind.Hard, CancellationToken.None)).Stopped)
                        throw new HarnessException("fenced server stop unconfirmed");
                }
                catch (Exception ex) { Error(run, "fence disposal", ex); }
        }
    }

    void Finish(TaskRun run, TaskState state, string? outcome, string? reason)
    {
        if (run.Worker is not null && !run.WorkerEnded && !run.Zombie)
            throw new InvalidOperationException("cannot finalize a task before its worker ended or was released");
        if (!run.Zombie && Scratch.Required(run.Record.Spec))
        {
            try { Scratch.Delete(run.Record, ledger); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or HarnessException)
            {
                Error(run, "scratch cleanup", error);
                state = TaskState.Failed;
                reason = "scratch cleanup failed: " + error.Message;
            }
        }
        try
        {
            if (reason is not null && state == TaskState.Failed) ledger.Write(run.Id, "error", new JsonObject { ["message"] = reason });
            try { escalation.Drop(run.Id, $"task ended ({state.ToString().ToLowerInvariant()})"); }
            catch (Exception ex) when (ex is not OperationCanceledException) { Error(run, "drop escalations", ex); }
            if (ledgerFailed) return;
            ledger.Write(run.Id, "final", new JsonObject { ["state"] = state.ToString().ToLowerInvariant(), ["outcome"] = outcome, ["reason"] = reason });
            run.State = state;
        }
        catch (LedgerWriteException ex) { Error(run, "final", ex); }
    }
}
