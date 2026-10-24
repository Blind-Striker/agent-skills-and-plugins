using OpenCode.Sdk;
using OpenCode.Sdk.Models;

sealed partial class OpenCodeBackend
{
    readonly ConcurrentDictionary<string, string> directories = new(StringComparer.Ordinal);
    readonly ConcurrentDictionary<string, string> owners = new(StringComparer.Ordinal);
    readonly ConcurrentDictionary<string, int> absent = new(StringComparer.Ordinal);
    readonly ConcurrentDictionary<string, int> hostFailures = new(StringComparer.Ordinal);
    readonly ConcurrentDictionary<string, string> ended = new(StringComparer.Ordinal);
    readonly ConcurrentDictionary<string, WorkerSignal.Ask> asks = new(StringComparer.Ordinal);
    readonly ConcurrentDictionary<string, string> completionChecks = new(StringComparer.Ordinal);
    readonly Dictionary<string, string> toolNames = new(StringComparer.Ordinal);
    readonly Dictionary<string, WorkerSignal.ToolCall> toolStarts = new(StringComparer.Ordinal);
    readonly HashSet<string> completedTools = new(StringComparer.Ordinal);

    async Task PumpAsync(HarnessServer server, string hostKey, CancellationToken ct)
    {
        try
        {
            await foreach (var item in server.Events.ReadAllAsync(ct))
            {
                try
                {
                    switch (item)
                    {
                        case BusBarrier barrier:
                            barrier.Completion.TrySetResult();
                            break;
                        case BusConnected connected:
                            Emit(new WorkerSignal.Host(hostKey, "connected", "", connected.Attempt));
                            break;
                        case BusDisconnected gone:
                            Emit(new WorkerSignal.Host(hostKey, "disconnected", gone.Reason, 0));
                            break;
                        case PermissionAsked asked:
                            await AskAsync(server, asked.Data.SessionId, asked.Data.Id, asked.Data.Action, asked.Data.Resources, ct);
                            break;
                        case PermissionReplied replied:
                            if (asks.TryRemove(replied.Data.RequestId, out var ask))
                                Emit(new WorkerSignal.AskClosed(ask.TaskId, ask.Turn, ask.BlockerRef.AskId, "server",
                                    replied.Data.Reply == PermissionReply.Once ? new BlockerAnswer.Once() : new BlockerAnswer.Reject(Policy.LeadDeclined)));
                            break;
                        case FormCreated form:
                            await FormAsync(server, form.Data.Form.SessionId, form.Data.Form.Id, form.Data.Form.Title, ct);
                            break;
                        case SessionToolInputStarted tool:
                            lock (eventGate) toolNames.TryAdd(ToolKey(tool.Data.SessionId, tool.Data.AssistantMessageId, tool.Data.Id), tool.Data.Name);
                            break;
                        case SessionToolCalled tool:
                            await ToolStartedAsync(server, tool, ct);
                            break;
                        case SessionToolSuccess tool:
                            await ToolEndedAsync(server, tool.Data.SessionId, ToolKey(tool.Data.SessionId, tool.Data.AssistantMessageId, tool.Data.Id), tool.Created, "succeeded", ct);
                            break;
                        case SessionToolFailed tool:
                            await ToolEndedAsync(server, tool.Data.SessionId, ToolKey(tool.Data.SessionId, tool.Data.AssistantMessageId, tool.Data.Id), tool.Created, "failed", ct);
                            break;
                        case SessionStatusUpdated { Data.Status: SessionStatusRetry retry } status when workers.TryGetValue(status.Data.SessionId, out var worker):
                            var attempt = checked((int)retry.Attempt);
                            var next = DateTimeOffset.FromUnixTimeMilliseconds(checked((long)retry.Next));
                            Publish(worker, admitted => Emit(new WorkerSignal.Retry(admitted.TaskId, admitted.Turn!, attempt, retry.Message, next, retry.Action?.Reason, retry.Action?.Provider)));
                            break;
                        case SessionExecutionSucceeded done:
                            Status(done.Data.SessionId, done.Created, "succeeded", null);
                            break;
                        case SessionExecutionFailed failed:
                            Status(failed.Data.SessionId, failed.Created, "failed", OpenCodeJson.Error(failed.Data.Error)?.ToJsonString());
                            break;
                        case SessionExecutionInterrupted interrupted:
                            Status(interrupted.Data.SessionId, interrupted.Created, "interrupted", interrupted.Data.Reason.ToString());
                            break;
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                { Emit(new WorkerSignal.Fault(null, item.GetType().Name, ex.Message)); }
            }
            if (!ct.IsCancellationRequested && !server.IsStopping) throw new HarnessException("the event mapping stream ended unexpectedly");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { /* Owned shutdown. */ }
        catch (Exception ex)
        {
            if (hostKey.StartsWith("fence=", StringComparison.Ordinal))
            { Emit(new WorkerSignal.Host(hostKey, "disconnected", ex.Message, 0)); return; }
            Emit(new WorkerSignal.Fault(null, "event pump", ex.Message));
            signals.Writer.TryComplete(new HarnessException($"the event mapping stream failed: {ex.Message}"));
        }
    }

    void Emit(WorkerSignal signal) => signals.Writer.TryWrite(signal);

    static string ToolKey(string session, string message, string id) => $"{session}/{message}/{id}";

    async Task ToolStartedAsync(HarnessServer server, SessionToolCalled tool, CancellationToken ct)
    {
        var key = ToolKey(tool.Data.SessionId, tool.Data.AssistantMessageId, tool.Data.Id);
        if (await OwnerAsync(server, tool.Data.SessionId, ct) is not { } owner) return;
        Publish(owner, admitted =>
        {
            var turn = turns.GetValueOrDefault(admitted.Id)?.LastOrDefault(x =>
                x.State["promptCreated"] is not { } time || time.GetValue<double>() <= tool.Created) ?? admitted;
            if (turn.Turn is null || completedTools.Contains(key) || toolStarts.ContainsKey(key)) return;
            var name = toolNames.GetValueOrDefault(key, "unknown");
            // The skill tool names its skill by id; an audit needs to see which skill loaded.
            var resource = (name == "skill" ? ["id"] : new[] { "filePath", "path", "url", "command", "pattern", "query" })
                .Select(field => tool.Data.Input.TryGetValue(field, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null)
                .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? "";
            var start = new WorkerSignal.ToolCall(turn.TaskId, turn.Turn, key, name, resource,
                "start", null, DateTimeOffset.FromUnixTimeMilliseconds(checked((long)tool.Created)));
            toolStarts[key] = start;
            Emit(start);
        });
    }

    async Task ToolEndedAsync(HarnessServer server, string session, string key, double created, string outcome, CancellationToken ct)
    {
        // Publish also buffers a completion that races prompt admission. The start keeps the
        // immutable owning turn, so a delayed completion cannot be relabelled on continue.
        if (await OwnerAsync(server, session, ct) is not { } owner) return;
        Publish(owner, _ =>
        {
            if (!toolStarts.Remove(key, out var begun) || !completedTools.Add(key)) return;
            toolNames.Remove(key);
            Emit(new WorkerSignal.ToolCall(begun.TaskId, begun.Turn, key, begun.Tool, begun.Resource,
                "end", outcome, DateTimeOffset.FromUnixTimeMilliseconds(checked((long)created))));
        });
    }

    void RememberTurn(WorkerRef worker)
    {
        if (worker.Turn is null) return;
        if (!turns.TryGetValue(worker.Id, out var history)) turns[worker.Id] = history = [];
        if (history.All(x => x.Turn != worker.Turn)) history.Add(worker);
    }

    void Status(string id, double created, string execution, string? detail)
    {
        if (workers.TryGetValue(id, out var worker))
            Publish(worker, admitted =>
            {
                // Execution events carry a timestamp, not a prompt id. Resolve against immutable
                // stored turn snapshots, never relabel a delayed event with the current prompt.
                var owner = turns.GetValueOrDefault(id)?.LastOrDefault(x =>
                    x.State["promptCreated"] is not { } time || time.GetValue<double>() <= created);
                if (owner is null) return;
                Emit(new WorkerSignal.Status(owner.TaskId, owner.Turn!, execution, detail));
                if (owner.Turn == admitted.Turn) completionChecks[owner.Id] = owner.Turn!;
            });
    }

    async Task<WorkerRef?> OwnerAsync(HarnessServer server, string id, CancellationToken ct)
    {
        var chain = new HashSet<string>(StringComparer.Ordinal);
        var current = id;
        while (true)
        {
            if (workers.TryGetValue(current, out var root)
                || owners.TryGetValue(current, out var known) && workers.TryGetValue(known, out root))
            {
                foreach (var child in chain) owners.TryAdd(child, root.Id);
                return root;
            }
            // A released worker is not a negative ownership result. Re-read the ancestry.
            owners.TryRemove(current, out _);
            if (chain.Count == 64 || !chain.Add(current)) return Cut();
            var read = await server.Client.Sessions.GetSessionClient(current).GetAsync(OpenCodeRequestOptions.NoThrow, ct);
            if (read.IsError) return Cut();
            if (read.Session.ParentId is not { } parent) return null;
            current = parent;
        }

        WorkerRef? Cut()
        {
            Emit(new WorkerSignal.Fault(null, "owner-chain-cut", new JsonObject { ["session"] = id, ["depth"] = chain.Count }.ToJsonString()));
            return null;
        }
    }

    async Task AskAsync(HarnessServer server, string session, string id, string action, IReadOnlyList<string> resources, CancellationToken ct)
    {
        if (await OwnerAsync(server, session, ct) is not { } owner)
        {
            var reply = await AnswerCoreAsync(new(Name, session, null, id),
                new BlockerAnswer.Reject("This session has no owning task; the request was rejected."), ct, server);
            if (!reply.Confirmed)
                Emit(new WorkerSignal.Fault(null, "unowned permission", $"request {id} rejection unconfirmed: HTTP {reply.Status}"));
            return;
        }
        Publish(owner, worker =>
        {
            var ask = new WorkerSignal.Ask(worker.TaskId, worker.Turn!, new(Name, worker.Id, session == worker.Id ? null : session, id), action, resources);
            asks[id] = ask;
            Emit(ask);
        });
    }

    async Task FormAsync(HarnessServer server, string session, string id, string title, CancellationToken ct)
    {
        if (await OwnerAsync(server, session, ct) is { } owner)
            Publish(owner, worker => Emit(new WorkerSignal.Form(worker.TaskId, worker.Turn!, new(Name, worker.Id, session == worker.Id ? null : session, id), title)));
    }

    void Publish(WorkerRef worker, Action<WorkerRef> publish)
    {
        lock (eventGate)
        {
            if (admissions.TryGetValue(worker.Id, out var buffered)) buffered.Add(publish);
            else if (worker.Turn is not null) publish(worker);
        }
    }

    async Task<PollResult> PollCoreAsync(IReadOnlyList<WorkerRef> live, CancellationToken ct)
    {
        foreach (var worker in live.Where(x => hardStops.TryGetValue(x.Id, out var stopped) && stopped.Stopped))
            if (worker.Turn is not null && ended.GetValueOrDefault(worker.Id) != worker.Turn)
            {
                ended[worker.Id] = worker.Turn;
                Emit(new WorkerSignal.Ended(worker.TaskId, worker.Turn, "interrupted", null));
            }
        foreach (var group in live.Where(x => !hardStops.ContainsKey(x.Id)).GroupBy(HostKey))
        {
            HarnessServer server;
            IReadOnlyDictionary<string, SessionActive> active;
            try
            {
                if (LostFence(group.First())) throw new HarnessException("fenced opencode server is no longer live");
                server = await HostAsync(group.First(), ct);
                active = (await server.Client.Sessions.GetActiveAsync(cancellationToken: ct)).Active;
                hostFailures[group.Key] = 0;
            }
            catch (Exception) when (group.Key.StartsWith("fence=", StringComparison.Ordinal) && !ct.IsCancellationRequested)
            {
                if (!LostFence(group.First()) && hostFailures.AddOrUpdate(group.Key, 1, (_, count) => count + 1) < Constants.WatchdogFailureLimit)
                    continue;
                foreach (var worker in group.Where(x => x.Turn is not null && ended.GetValueOrDefault(x.Id) != x.Turn))
                {
                    ended[worker.Id] = worker.Turn!;
                    liveWorkers.TryRemove(worker.Id, out _);
                    Emit(new WorkerSignal.Ended(worker.TaskId, worker.Turn!, "lost", null));
                }
                try { await DisposeFenceAsync(group.Key); }
                catch (Exception ex) { Emit(new WorkerSignal.Fault(group.First().TaskId, "fence disposal", ex.Message)); }
                continue;
            }
            var client = server.Client;
            foreach (var worker in group)
            {
                if (worker.Turn is null) continue;
                var initial = initialSweeps.TryRemove(new KeyValuePair<string, string>(worker.Id, worker.Turn));
                var completionOnly = !initial && completionChecks.TryRemove(new KeyValuePair<string, string>(worker.Id, worker.Turn));
                try
                {
                    if (!completionOnly && directories.TryGetValue(worker.Id, out var directory))
                    {
                        var where = new LocationSelector { Directory = directory };
                        var before = asks.Values.Where(x => x.TaskId == worker.TaskId).ToList();
                        var requests = (await client.Permissions.ListRequestsAsync(new PermissionRequestListRequest { Location = where }, cancellationToken: ct)).Requests;
                        foreach (var request in requests) await AskAsync(server, request.SessionId, request.Id, request.Action, request.Resources, ct);
                        var pending = requests.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
                        foreach (var ask in before.Where(x => !pending.Contains(x.BlockerRef.AskId)))
                            if (asks.TryRemove(ask.BlockerRef.AskId, out _)) Emit(new WorkerSignal.AskClosed(ask.TaskId, ask.Turn, ask.BlockerRef.AskId, "gone", null));
                        foreach (var form in (await client.Forms.ListFormsAsync(new FormListRequest { Location = where }, cancellationToken: ct)).Forms)
                            await FormAsync(server, form.SessionId, form.Id, form.Title, ct);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                { Emit(new WorkerSignal.Fault(worker.TaskId, "watchdog", ex.Message)); }
                // Admission sweeps blockers only; status schedules a later completion-only check.
                if (!initial)
                {
                    try
                    {
                        var drained = server.DrainEventsAsync(ct);
                        await Task.WhenAny(drained, pumps[group.Key]).WaitAsync(ct);
                        if (!drained.IsCompleted) throw new HarnessException("the event mapping stream stopped before its completion check");
                        await drained;
                        await CheckDoneAsync(worker, active, ct);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    { Emit(new WorkerSignal.Fault(worker.TaskId, "completion", ex.Message)); }
                }
            }
        }
        return new([]);
    }

    async Task CheckDoneAsync(WorkerRef worker, IReadOnlyDictionary<string, SessionActive> active, CancellationToken ct)
    {
        if (worker.Turn is null || ended.GetValueOrDefault(worker.Id) == worker.Turn) return;
        if (active.ContainsKey(worker.Id)) { absent[worker.Id] = 0; return; }
        var info = (await (await HostAsync(worker, ct)).Client.Sessions.GetSessionClient(worker.Id).GetAsync(cancellationToken: ct)).Session;
        var created = worker.State["promptCreated"]?.GetValue<double>();
        var idleAfterPrompt = info.Time.Idle is { } idle && created is { } promptCreated && idle >= promptCreated;
        var outcome = info.Outcome is not null && idleAfterPrompt ? info.Outcome.Value.ToString().ToLowerInvariant() : null;
        if (outcome is null && absent.AddOrUpdate(worker.Id, 1, (_, count) => count + 1) >= 4) outcome = "lost";
        if (outcome is null) return;
        ended[worker.Id] = worker.Turn;
        liveWorkers.TryRemove(worker.Id, out _);
        Emit(new WorkerSignal.Ended(worker.TaskId, worker.Turn, outcome, null));
    }
}
