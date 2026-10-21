using OpenCode.Sdk;
using OpenCode.Sdk.Models;

sealed partial class OpenCodeBackend
{
    readonly ConcurrentDictionary<string, string> directories = new(StringComparer.Ordinal);
    readonly ConcurrentDictionary<string, string> owners = new(StringComparer.Ordinal);
    readonly ConcurrentDictionary<string, int> absent = new(StringComparer.Ordinal);
    readonly ConcurrentDictionary<string, string> ended = new(StringComparer.Ordinal);
    readonly ConcurrentDictionary<string, WorkerSignal.Ask> asks = new(StringComparer.Ordinal);

    async Task PumpAsync(HarnessServer server, CancellationToken ct)
    {
        await foreach (var item in server.Events.ReadAllAsync(ct))
        {
            switch (item)
            {
                case BusConnected connected:
                    Emit(new WorkerSignal.Host("profile=full;project=on", "connected", "", connected.Attempt));
                    break;
                case BusDisconnected gone:
                    Emit(new WorkerSignal.Host("profile=full;project=on", "disconnected", gone.Reason, 0));
                    break;
                case PermissionAsked asked:
                    await AskAsync(asked.Data.SessionId, asked.Data.Id, asked.Data.Action, asked.Data.Resources, ct);
                    break;
                case PermissionReplied replied:
                    if (asks.TryRemove(replied.Data.RequestId, out var ask))
                        Emit(new WorkerSignal.AskClosed(ask.TaskId, ask.Turn, ask.BlockerRef.AskId, "server",
                            replied.Data.Reply == PermissionReply.Once ? new BlockerAnswer.Once() : new BlockerAnswer.Reject(Policy.LeadDeclined)));
                    break;
                case FormCreated form:
                    await FormAsync(form.Data.Form.SessionId, form.Data.Form.Id, form.Data.Form.Title, ct);
                    break;
                case SessionStatusUpdated { Data.Status: SessionStatusRetry retry } status when workers.TryGetValue(status.Data.SessionId, out var worker) && worker.Turn is not null:
                    Emit(new WorkerSignal.Retry(worker.TaskId, worker.Turn, checked((int)retry.Attempt), retry.Message, DateTimeOffset.FromUnixTimeMilliseconds(checked((long)retry.Next)), retry.Action?.Reason, retry.Action?.Provider));
                    break;
                case SessionExecutionSucceeded done:
                    await StatusAsync(done.Data.SessionId, "succeeded", null, ct);
                    break;
                case SessionExecutionFailed failed:
                    await StatusAsync(failed.Data.SessionId, "failed", OpenCodeJson.Error(failed.Data.Error)?.ToJsonString(), ct);
                    break;
                case SessionExecutionInterrupted interrupted:
                    await StatusAsync(interrupted.Data.SessionId, "interrupted", interrupted.Data.Reason.ToString(), ct);
                    break;
            }
        }
    }

    void Emit(WorkerSignal signal) => signals.Writer.TryWrite(signal);

    async Task StatusAsync(string id, string execution, string? detail, CancellationToken ct)
    {
        // Admission can race the bus: poll after PromptAsync has installed the turn.
        if (!workers.TryGetValue(id, out var worker)) return;
        worker = await AdmittedAsync(worker, ct);
        if (worker?.Turn is null) return;
        Emit(new WorkerSignal.Status(worker.TaskId, worker.Turn, execution, detail));
        await CheckDoneAsync(worker, (await (await HostAsync(ct)).Client.Sessions.GetActiveAsync(cancellationToken: ct)).Active, ct);
    }

    async Task<WorkerRef?> OwnerAsync(string id, CancellationToken ct)
    {
        if (workers.TryGetValue(id, out var root)) return root;
        if (owners.TryGetValue(id, out var known)) return workers.GetValueOrDefault(known);
        var chain = new List<string> { id };
        var current = id;
        for (var depth = 0; depth < 8; depth++)
        {
            var read = await (await HostAsync(ct)).Client.Sessions.GetSessionClient(current).GetAsync(OpenCodeRequestOptions.NoThrow, ct);
            if (read.IsError) return null;
            if (read.Session.ParentId is not { } parent) break;
            if (workers.TryGetValue(parent, out root))
            {
                foreach (var child in chain) owners.TryAdd(child, root.Id);
                return root;
            }
            chain.Add(parent);
            current = parent;
        }
        return null;
    }

    async Task AskAsync(string session, string id, string action, IReadOnlyList<string> resources, CancellationToken ct)
    {
        if (await OwnerAsync(session, ct) is not { } owner || await AdmittedAsync(owner, ct) is not { Turn: { } turn } worker) return;
        var ask = new WorkerSignal.Ask(worker.TaskId, turn, new(Name, worker.Id, session == worker.Id ? null : session, id), action, resources);
        asks[id] = ask;
        Emit(ask);
    }

    async Task FormAsync(string session, string id, string title, CancellationToken ct)
    {
        if (await OwnerAsync(session, ct) is { } owner && await AdmittedAsync(owner, ct) is { Turn: { } turn } worker)
            Emit(new WorkerSignal.Form(worker.TaskId, turn, new(Name, worker.Id, session == worker.Id ? null : session, id), title));
    }

    async Task<WorkerRef?> AdmittedAsync(WorkerRef worker, CancellationToken ct)
    {
        if (worker.Turn is null && admissions.TryGetValue(worker.Id, out var admission)) await admission.Task.WaitAsync(ct);
        return workers.GetValueOrDefault(worker.Id);
    }

    async Task<PollResult> PollCoreAsync(IReadOnlyList<WorkerRef> live, CancellationToken ct)
    {
        var client = (await HostAsync(ct)).Client;
        var active = (await client.Sessions.GetActiveAsync(cancellationToken: ct)).Active;
        foreach (var worker in live)
        {
            if (worker.Turn is null) continue;
            if (directories.TryGetValue(worker.Id, out var directory))
            {
                var where = new LocationSelector { Directory = directory };
                var requests = (await client.Permissions.ListRequestsAsync(new PermissionRequestListRequest { Location = where }, cancellationToken: ct)).Requests;
                foreach (var request in requests) await AskAsync(request.SessionId, request.Id, request.Action, request.Resources, ct);
                var pending = requests.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
                foreach (var ask in asks.Values.Where(x => x.TaskId == worker.TaskId && !pending.Contains(x.BlockerRef.AskId)).ToList())
                    if (asks.TryRemove(ask.BlockerRef.AskId, out _)) Emit(new WorkerSignal.AskClosed(ask.TaskId, ask.Turn, ask.BlockerRef.AskId, "gone", null));
                foreach (var form in (await client.Forms.ListFormsAsync(new FormListRequest { Location = where }, cancellationToken: ct)).Forms)
                    await FormAsync(form.SessionId, form.Id, form.Title, ct);
            }
            await CheckDoneAsync(worker, active, ct);
        }
        return new([]);
    }

    async Task CheckDoneAsync(WorkerRef worker, IReadOnlyDictionary<string, SessionActive> active, CancellationToken ct)
    {
        if (worker.Turn is null || ended.GetValueOrDefault(worker.Id) == worker.Turn) return;
        if (active.ContainsKey(worker.Id)) { absent[worker.Id] = 0; return; }
        var info = (await (await HostAsync(ct)).Client.Sessions.GetSessionClient(worker.Id).GetAsync(cancellationToken: ct)).Session;
        var created = worker.State["promptCreated"]?.GetValue<double>();
        var idleAfterPrompt = info.Time.Idle is null || created is null || info.Time.Idle >= created;
        var outcome = info.Outcome is not null && idleAfterPrompt ? info.Outcome.Value.ToString().ToLowerInvariant() : null;
        if (outcome is null && absent.AddOrUpdate(worker.Id, 1, (_, count) => count + 1) >= 4) outcome = "lost";
        if (outcome is null) return;
        ended[worker.Id] = worker.Turn;
        Emit(new WorkerSignal.Ended(worker.TaskId, worker.Turn, outcome, null));
    }
}
