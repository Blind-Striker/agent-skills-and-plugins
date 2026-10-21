using OpenCode.Sdk;
using OpenCode.Sdk.Models;

sealed partial class OpenCodeBackend(bool subscribe = true) : IWorkerBackend
{
    readonly SemaphoreSlim hostGate = new(1);
    readonly CancellationTokenSource lifetime = new();
    readonly Channel<WorkerSignal> signals = Channel.CreateUnbounded<WorkerSignal>();
    readonly ConcurrentDictionary<string, WorkerRef> workers = new(StringComparer.Ordinal);
    readonly ConcurrentDictionary<string, WorkerLaunch> launches = new(StringComparer.Ordinal);
    readonly object eventGate = new();
    readonly Dictionary<string, List<Action<WorkerRef>>> admissions = new(StringComparer.Ordinal);
    readonly Dictionary<string, List<WorkerRef>> turns = new(StringComparer.Ordinal);
    readonly ConcurrentDictionary<string, string> initialSweeps = new(StringComparer.Ordinal);
    readonly ConcurrentDictionary<string, InterruptKind> interrupts = new(StringComparer.Ordinal);
    HarnessServer? host;
    Task pump = Task.CompletedTask;

    public string Name => "opencode";
    public BackendCapabilities Capabilities { get; } = new("policy", "policy", "text", true);
    public ChannelReader<WorkerSignal> Signals => signals.Reader;

    public Task<IReadOnlyList<LiveCheck>> ValidateAsync(IReadOnlyList<WorkerPlan> tasks, CancellationToken ct) => GuardAsync(() => ValidateCoreAsync(tasks, ct));
    public Task<CreateResult> CreateAsync(WorkerLaunch launch, CancellationToken ct) => GuardAsync(() => CreateCoreAsync(launch, ct));
    public Task<WorkerRef> PromptAsync(WorkerRef worker, WorkerPrompt prompt, CancellationToken ct) => GuardAsync(() => PromptCoreAsync(worker, prompt, ct));
    public Task<AnswerResult> AnswerAsync(BlockerRef blocker, BlockerAnswer answer, CancellationToken ct) => GuardAsync(() => AnswerCoreAsync(blocker, answer, ct));
    public Task<InterruptResult> InterruptAsync(WorkerRef worker, InterruptKind kind, CancellationToken ct) => GuardAsync(() => InterruptCoreAsync(worker, kind, ct));
    public Task<PollResult> PollAsync(IReadOnlyList<WorkerRef> live, CancellationToken ct) => GuardAsync(() => PollCoreAsync(live, ct));
    public Task<WorkerReport> CollectAsync(WorkerRef worker, CancellationToken ct) => GuardAsync(() => CollectCoreAsync(worker, ct));
    public Task<ReattachResult> ReattachAsync(WorkerRef worker, CancellationToken ct) => GuardAsync(() => ReattachCoreAsync(worker, ct));
    public Task<RouteListing> ListRoutesAsync(string directory, CancellationToken ct) => GuardAsync(() => ListRoutesCoreAsync(directory, ct));
    public async Task ReleaseAsync(WorkerRef worker, CancellationToken ct)
    {
        try { await ReleaseCoreAsync(worker, ct); }
        catch (OpenCodeException ex) { throw Failure(ex); }
    }

    static async Task<T> GuardAsync<T>(Func<Task<T>> call)
    {
        try { return await call(); }
        catch (OpenCodeException ex) { throw Failure(ex); }
    }

    async Task<HarnessServer> HostAsync(CancellationToken ct)
    {
        await hostGate.WaitAsync(ct);
        try
        {
            if (host is null)
            {
                host = await HarnessServer.StartAsync(subscribe, ct);
                pump = PumpAsync(host, lifetime.Token);
            }
            return host;
        }
        finally { hostGate.Release(); }
    }

    public async Task<BackendProbe> ProbeAsync(ProbeRequest request, CancellationToken ct)
    {
        try
        {
            var server = await HostAsync(ct);
            var checks = request.CheckRules is null ? null : await CheckAsync(server, request.Directory, request.CheckRules, ct);
            return new(checks?.All(x => x.Ok) ?? true, server.Version, [], [], checks);
        }
        catch (OpenCodeException ex) { throw Failure(ex); }
    }

    async Task<IReadOnlyList<LiveCheck>> ValidateCoreAsync(IReadOnlyList<WorkerPlan> tasks, CancellationToken ct)
    {
        var server = await HostAsync(ct);
        var result = new List<LiveCheck>();
        foreach (var task in tasks)
        {
            var catalog = await Catalog.LoadAsync(server.Client, task.Directory, TimeSpan.FromSeconds(60), ct);
            var errors = new List<string>();
            RouteRef? route = task.Route;
            if (task.Agent is { } agent)
            {
                if (catalog.CheckAgent(agent) is { } why) errors.Add($"agent {agent}: {why}");
                else route = OpenCodeModels.From(catalog.Agents.First(x => x.Id == agent).Model!);
            }
            else
            {
                var persona = task.Persona ?? Constants.DefaultPersona;
                if (catalog.Agents.All(x => x.Id != persona)) errors.Add($"persona {persona} not found");
                else if (catalog.Manual.Contains(persona)) errors.Add($"persona {persona} is manual-class");
                if (catalog.IsManualRoute(task.Route)) errors.Add($"route {task.Route} equals a manual-class agent's model");
                else if (catalog.CheckRoute(task.Route) is { } why) errors.Add($"route {task.Route}: {why}");
            }
            result.Add(new(task.TaskId, errors, [], route is null ? "unknown" : catalog.Lineage(route), task.Modes.Profile, null));
        }
        return result;
    }

    async Task<CreateResult> CreateCoreAsync(WorkerLaunch launch, CancellationToken ct)
    {
        var server = await HostAsync(ct);
        var client = server.Client;
        var catalog = await Catalog.LoadAsync(client, launch.Directory, TimeSpan.FromSeconds(60), ct);
        var route = launch.Route;
        var agent = launch.Agent ?? launch.Persona ?? Constants.DefaultPersona;
        if (catalog.Manual.Contains(agent)) throw new HarnessException($"agent {agent} is manual-class");
        if (launch.Agent is { } agentId)
        {
            var info = await client.Agents.GetAgentAsync(agentId, new AgentRequest { Location = new LocationSelector { Directory = launch.Directory } }, cancellationToken: ct);
            route = info.Agent.Model is { } model ? OpenCodeModels.From(model) : throw new HarnessException($"agent {agentId} pins no model");
        }
        else if (catalog.IsManualRoute(route)) throw new HarnessException($"route {route} equals a manual-class agent's model");
        await WaitForRouteAsync(client, route, launch.Directory, ct);
        var rules = OpenCodeRules.Render(launch.Rules, launch.RunId);
        var created = await client.Sessions.CreateSessionAsync(new SessionCreateRequest
        {
            Title = $"dispatch {launch.RunId}/{launch.TaskId}",
            Agent = agent,
            Model = OpenCodeModels.ToModelRef(route),
            Location = new LocationPublicRef { Directory = launch.Directory },
            Permissions = rules,
            Metadata = OpenCodeJson.Metadata(launch.RunId, launch.TaskId),
        }, cancellationToken: ct);
        var worker = new WorkerRef(Name, launch.TaskId, created.Session.Id, null, new JsonObject
        {
            ["projectId"] = created.Session.ProjectId,
            ["host"] = "profile=full;project=on",
        });
        lock (eventGate)
        {
            admissions[worker.Id] = [];
            workers[worker.Id] = worker;
        }
        launches[worker.Id] = launch;
        directories[worker.Id] = launch.Directory;
        try
        {
            await client.Sessions.GetSessionClient(worker.Id).SetEnvironmentAsync(new SessionEnvironmentRequest
            { Variables = launch.Environment.ToDictionary(x => x.Key, x => x.Value) }, cancellationToken: ct);
        }
        catch (Exception ex)
        {
            try { await ReleaseCoreAsync(worker, CancellationToken.None); }
            catch (Exception releaseError)
            { throw new WorkerCreationException(worker, $"worker setup failed: {ex.Message}; release unconfirmed: {releaseError.Message}"); }
            throw;
        }
        return new(worker, new("none", "none", "policy"), new($"opencode:{route}", agent, catalog.Lineage(route),
            created.Session.ProjectId, rules.Select(x => $"{x.Action} {x.Resource} {x.Effect.ToString().ToLowerInvariant()}").ToList()));
    }

    async Task<WorkerRef> PromptCoreAsync(WorkerRef worker, WorkerPrompt prompt, CancellationToken ct)
    {
        var session = (await HostAsync(ct)).Client.Sessions.GetSessionClient(worker.Id);
        if (prompt.IsContinue)
        {
            lock (eventGate) admissions[worker.Id] = [];
            var variables = launches.TryGetValue(worker.Id, out var launch) ? launch.Environment : WorkerEnvironment.Environment(worker.TaskId).Variables;
            await session.SetEnvironmentAsync(new SessionEnvironmentRequest { Variables = variables.ToDictionary(x => x.Key, x => x.Value) }, cancellationToken: ct);
            interrupts.TryRemove(worker.Id, out _);
        }
        var response = await session.PromptAsync(new SessionPromptRequest { Text = prompt.Text }, cancellationToken: ct);
        var state = (JsonObject)worker.State.DeepClone();
        state["promptId"] = response.Prompt.Id;
        state["promptCreated"] = response.Prompt.Time.Created;
        var prompted = worker with { Turn = response.Prompt.Id, State = state };
        lock (eventGate)
        {
            workers[worker.Id] = prompted;
            RememberTurn(worker);
            RememberTurn(prompted);
            initialSweeps[worker.Id] = prompted.Turn!;
            if (admissions.Remove(worker.Id, out var buffered))
                foreach (var publish in buffered) publish(prompted);
        }
        return prompted;
    }

    async Task<AnswerResult> AnswerCoreAsync(BlockerRef blocker, BlockerAnswer answer, CancellationToken ct)
    {
        var session = (await HostAsync(ct)).Client.Sessions.GetSessionClient(blocker.OriginChildId ?? blocker.WorkerId);
        if (answer is BlockerAnswer.Cancel cancel)
        {
            var reply = await session.CancelFormAsync(blocker.AskId, new SessionFormCancelRequest { Message = cancel.Message }, OpenCodeRequestOptions.NoThrow, ct);
            return new(!reply.IsError, null, reply.Status);
        }
        var request = answer switch
        {
            BlockerAnswer.Once => new SessionPermissionReplyRequest { Decision = PermissionReply.Once },
            BlockerAnswer.Reject reject => new SessionPermissionReplyRequest { Decision = PermissionReply.Reject, Message = reject.Message },
            _ => throw new ArgumentException("unsupported blocker answer", nameof(answer)),
        };
        var response = await session.ReplyToPermissionAsync(blocker.AskId, request, OpenCodeRequestOptions.NoThrow, ct);
        return new(!response.IsError, null, response.Status);
    }

    async Task<InterruptResult> InterruptCoreAsync(WorkerRef worker, InterruptKind kind, CancellationToken ct)
    {
        interrupts[worker.Id] = kind;
        var response = await (await HostAsync(ct)).Client.Sessions.GetSessionClient(worker.Id)
            .InterruptAsync(new SessionInterruptRequest { Resume = false }, OpenCodeRequestOptions.NoThrow, ct);
        return new(false, response.IsError ? $"HTTP {response.Status}" : "interrupt", response.IsError ? null : response.Interrupt.Interrupted, response.Status);
    }

    async Task<ReattachResult> ReattachCoreAsync(WorkerRef worker, CancellationToken ct)
    {
        var read = await (await HostAsync(ct)).Client.Sessions.GetSessionClient(worker.Id).GetAsync(OpenCodeRequestOptions.NoThrow, ct);
        if (read.IsError) return new ReattachResult.Unreadable($"session {worker.Id} unreadable: HTTP {read.Status} {read.Error?.Tag}");
        lock (eventGate)
        {
            workers[worker.Id] = worker;
            RememberTurn(worker);
        }
        directories[worker.Id] = read.Session.Location.Directory;
        if (read.Session.Outcome == SessionInfoOutcome.Succeeded) return new ReattachResult.Ended("succeeded");
        if (read.Session.Outcome == SessionInfoOutcome.Failed) return new ReattachResult.Ended("failed");
        return new ReattachResult.Interrupted();
    }

    async Task ReleaseCoreAsync(WorkerRef worker, CancellationToken ct)
    {
        var response = await (await HostAsync(ct)).Client.Sessions.GetSessionClient(worker.Id).RemoveAsync(OpenCodeRequestOptions.NoThrow, ct);
        if (response.IsError && response.Status != 404) throw new HarnessException($"session {worker.Id} release failed: HTTP {response.Status}");
        lock (eventGate)
        {
            workers.TryRemove(worker.Id, out _);
            admissions.Remove(worker.Id);
        }
    }

    public Task<IReadOnlyList<WorkerRef>> FindOrphansAsync(OrphanQuery query, CancellationToken ct) =>
        throw new NotSupportedException("orphan discovery is not available");

    async Task<RouteListing> ListRoutesCoreAsync(string directory, CancellationToken ct)
    {
        var catalog = await Catalog.LoadAsync((await HostAsync(ct)).Client, directory, TimeSpan.FromSeconds(60), ct);
        return new(catalog.Models.Select(x => new RouteEntry(new(x.ProviderId, x.Id, null), x.Enabled,
            x.Variants.Select(v => v.Id).ToList(), catalog.Lineage(new(x.ProviderId, x.Id, null)))).ToList(),
            catalog.Agents.Select(x => new AgentEntry(x.Id, catalog.CheckAgent(x.Id) is null, catalog.CheckAgent(x.Id),
                x.Model is { } m ? OpenCodeModels.From(m).ToString() : null, x.Mode.ToString().ToLowerInvariant(), x.Hidden, catalog.Manual.Contains(x.Id))).ToList(), []);
    }

    static async Task WaitForRouteAsync(OpenCodeClient client, RouteRef route, string directory, CancellationToken ct)
    {
        var deadline = DateTimeOffset.UtcNow + Constants.RouteWait;
        while (true)
        {
            string why;
            try
            {
                var catalog = await Catalog.LoadAsync(client, directory, TimeSpan.Zero, ct);
                if (catalog.CheckRoute(route) is not { } reason) return;
                why = reason;
            }
            catch (NotReadyException ex) { why = ex.Message; }
            if (DateTimeOffset.UtcNow >= deadline) throw new HarnessException($"route {route} at {directory}: {why}");
            await Task.Delay(TimeSpan.FromSeconds(3), ct);
        }
    }

    static HarnessException Failure(OpenCodeException ex) => new($"the server call failed: {ex.GetType().Name}: {ex.Message}");

    static async Task<IReadOnlyList<ProbeCheck>> CheckAsync(HarnessServer host, string directory, WorkerRules policy, CancellationToken ct)
    {
        var checks = new List<ProbeCheck>();
        void Check(string name, bool ok, string detail) => checks.Add(new(name, ok, detail));
        Check("server", true, $"opencode {host.Version} pid {host.Pid} (OPENCODE_DISABLE_FILEWATCHER=1, command {host.CommandDescription})");
        Check("pin", host.Version == Constants.PinnedUpstream, $"server {host.Version}, SDK pin {Constants.PinnedUpstream}");
        Check("bus", true, "EventServerConnected received before any session call");
        var catalog = await Catalog.LoadAsync(host.Client, directory, TimeSpan.FromSeconds(90), ct);
        Check("agents", catalog.Agents.Count > 0, $"{catalog.Agents.Count} agents at the worktree, {catalog.Agents.Count(x => x.Model is not null)} pin a model, {catalog.Manual.Count} manual-class");
        Check("models", catalog.Models.Count > 0, $"{catalog.Models.Count} models, {catalog.Models.Count(x => x.Enabled)} enabled, {catalog.Models.Select(x => x.ProviderId).Distinct().Count()} providers");
        var mcp = await Catalog.McpServerNamesAsync(host.Client, directory, ct);
        var rules = OpenCodeRules.Render(policy);
        Check("mcp", true, $"{mcp.Count} MCP servers at the worktree, allowed: {string.Join(", ", mcp)}");
        var created = await host.Client.Sessions.CreateSessionAsync(new SessionCreateRequest
        {
            Title = "dispatch selfcheck (no prompt)",
            Location = new LocationPublicRef { Directory = directory },
            Permissions = rules,
            Metadata = OpenCodeJson.Metadata("selfcheck", "selfcheck"),
        }, cancellationToken: ct);
        var session = host.Client.Sessions.GetSessionClient(created.Session.Id);
        Check("session create", true, $"{created.Session.Id} project {created.Session.ProjectId}");
        var read = (await session.GetAsync(cancellationToken: ct)).Session;
        var stored = read.Permissions ?? [];
        var same = stored.Count == rules.Count && stored.Zip(rules).All(p => p.First == p.Second);
        Check("rules stored", same, $"{stored.Count} rules read back, {rules.Count} sent, order and content {(same ? "identical" : "differ")}");
        Check("location", Locations.Same(read.Location.Directory, directory), read.Location.Directory);
        var active = await host.Client.Sessions.GetActiveAsync(cancellationToken: ct);
        Check("idle", !active.Active.ContainsKey(read.Id), $"session absent from GetActiveAsync ({active.Active.Count} active)");
        var pending = await session.ListRequestsAsync(cancellationToken: ct);
        var forms = await session.ListFormsAsync(cancellationToken: ct);
        Check("blockers", pending.Requests.Count == 0 && forms.Forms.Count == 0, $"{pending.Requests.Count} permission requests, {forms.Forms.Count} forms");
        await session.RemoveAsync(cancellationToken: ct);
        var gone = await session.GetAsync(OpenCodeRequestOptions.NoThrow, ct);
        Check("session remove", gone.IsError && gone.Error is SessionNotFoundError, $"read after remove: HTTP {gone.Status} {gone.Error?.Tag}");
        return checks;
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await lifetime.CancelAsync();
            try { await pump; }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { /* Owned shutdown. */ }
        }
        finally
        {
            try { if (host is not null) await host.DisposeAsync(); }
            finally
            {
                signals.Writer.TryComplete();
                lifetime.Dispose();
                hostGate.Dispose();
            }
        }
    }
}
