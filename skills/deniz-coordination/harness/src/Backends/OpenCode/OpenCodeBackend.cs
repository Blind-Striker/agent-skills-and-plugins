using OpenCode.Sdk;
using OpenCode.Sdk.Models;

sealed partial class OpenCodeBackend(bool subscribe = true, BackendContext? context = null, string? runId = null) : IWorkerBackend
{
    readonly SemaphoreSlim hostGate = new(1);
    readonly SemaphoreSlim workerGate = new(1);
    readonly CancellationTokenSource lifetime = new();
    readonly Channel<WorkerSignal> signals = Channel.CreateUnbounded<WorkerSignal>();
    readonly ConcurrentDictionary<string, WorkerRef> workers = new(StringComparer.Ordinal);
    readonly ConcurrentDictionary<string, WorkerLaunch> launches = new(StringComparer.Ordinal);
    readonly ConcurrentDictionary<string, byte> liveWorkers = new(StringComparer.Ordinal);
    readonly object eventGate = new();
    readonly Dictionary<string, List<Action<WorkerRef>>> admissions = new(StringComparer.Ordinal);
    readonly Dictionary<string, List<WorkerRef>> turns = new(StringComparer.Ordinal);
    readonly ConcurrentDictionary<string, string> initialSweeps = new(StringComparer.Ordinal);
    readonly ConcurrentDictionary<string, InterruptKind> interrupts = new(StringComparer.Ordinal);
    readonly ConcurrentDictionary<string, InterruptResult> hardStops = new(StringComparer.Ordinal);
    readonly ConcurrentDictionary<string, HarnessServer> hosts = new(StringComparer.Ordinal);
    readonly ConcurrentDictionary<string, Task> pumps = new(StringComparer.Ordinal);
    readonly ConcurrentDictionary<string, string> sessionHosts = new(StringComparer.Ordinal);
    BackendContext? backendContext = context;
    OpenCodeProfile? profile;
    string? ownedControl;

    public string Name => "opencode";
    public BackendCapabilities Capabilities { get; } = new("policy", "policy", "text", true);
    public ChannelReader<WorkerSignal> Signals => signals.Reader;

    public Task<IReadOnlyList<LiveCheck>> ValidateAsync(IReadOnlyList<WorkerPlan> tasks, CancellationToken ct) => GuardAsync(() => ValidateCoreAsync(tasks, ct));
    public async Task<CreateResult> CreateAsync(WorkerLaunch launch, CancellationToken ct)
    {
        await workerGate.WaitAsync(ct);
        try { return await GuardAsync(() => CreateCoreAsync(launch, ct)); }
        catch (Exception failure)
        {
            try { await DisposeFenceAsync($"fence={launch.RunId}/{launch.TaskId}"); }
            catch (Exception disposal)
            {
                var message = $"{failure.Message}; fence disposal failed: {disposal.Message}";
                if (failure is WorkerCreationException creation) throw new WorkerCreationException(creation.Worker, message);
                throw new HarnessException(message);
            }
            throw;
        }
        finally { workerGate.Release(); }
    }
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
        finally { await DisposeFenceAsync(HostKey(worker)); }
    }

    static async Task<T> GuardAsync<T>(Func<Task<T>> call)
    {
        try { return await call(); }
        catch (OpenCodeException ex) { throw Failure(ex); }
    }

    const string DefaultHost = "profile=full;project=on";
    static string HostKey(Modes modes) => $"profile={modes.Profile};project={modes.Project}";
    static string HostKey(WorkerLaunch launch)
    {
        if (Ready.Fenced(launch.Modes)) return $"fence={launch.RunId}/{launch.TaskId}";
        var key = HostKey(launch.Modes);
        if (launch.ConfiguredEnvironment is not { Count: > 0 } environment) return key;
        var ordered = new JsonObject(environment.OrderBy(x => x.Key, WorkerEnvironment.NameComparer)
            .Select(x => new KeyValuePair<string, JsonNode?>(OperatingSystem.IsWindows() ? x.Key.ToUpperInvariant() : x.Key, JsonValue.Create(x.Value))));
        return key + ";environment=" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(ordered.ToJsonString())));
    }
    static string HostKey(WorkerRef worker) => worker.State["host"]?.GetValue<string>() ?? DefaultHost;
    static Modes HostModes(string key) => key switch
    {
        "profile=clean;project=on" => new("clean", "on", "workspace", "on", "worktree"),
        "profile=clean;project=off" => new("clean", "off", "workspace", "on", "worktree"),
        "profile=full;project=off" => new("full", "off", "workspace", "on", "worktree"),
        DefaultHost => new("full", "on", "workspace", "on", "worktree"),
        _ => throw new HarnessException("invalid opencode host key"),
    };

    Task<HarnessServer> HostAsync(WorkerRef worker, CancellationToken ct)
    {
        var key = HostKey(worker);
        if (!hosts.ContainsKey(key) && key.Contains(";environment=", StringComparison.Ordinal))
        {
            var launch = RecordedLaunch(worker);
            return HostAsync(ct, launch.Modes, launch.Route, launch.RunId, launch: launch);
        }
        return HostAsync(key, ct);
    }

    WorkerLaunch RecordedLaunch(WorkerRef worker)
    {
        var ledger = new Ledger(runId ?? throw new HarnessException("continuing a reattached worker requires its run id"));
        var record = ledger.LoadRecord(worker.TaskId);
        RouteRef.TryParse(record.Spec.Route ?? "agent/placeholder", out var route, out _);
        var configured = record.Settings.Environment?.ToDictionary(x => x.Key, x => x.Value.Value!, WorkerEnvironment.NameComparer);
        var modes = TaskStarter.ModesFor(record);
        return new(ledger.RunId, worker.TaskId, record.Spec.Kind!, route!, record.Spec.Agent, record.Spec.Persona,
            modes, record.Workspace.Path!, new([], []),
            configured ?? new Dictionary<string, string>(), null, record.Settings.Profile?.Actual ?? modes.Profile,
            record.Settings.Profile?.Reason, record.Settings.WritableRoots?.Value, record.Settings.PrivateFolders?.Value, configured);
    }

    Task<HarnessServer> HostAsync(string key, CancellationToken ct) => hosts.TryGetValue(key, out var host)
        ? Task.FromResult(host) : key.StartsWith("fence=", StringComparison.Ordinal)
            ? throw new HarnessException("fenced opencode server is no longer live") : HostAsync(ct, HostModes(key));

    bool LostFence(WorkerRef worker) => HostKey(worker).StartsWith("fence=", StringComparison.Ordinal)
        && (!hosts.TryGetValue(HostKey(worker), out var host) || host.IsStopping || host.Server.Exited.IsCompleted);

    async Task<HarnessServer> HostAsync(CancellationToken ct, Modes? modes = null, RouteRef? route = null, string? run = null,
        IEnumerable<RouteRef>? routes = null, WorkerLaunch? launch = null)
    {
        await hostGate.WaitAsync(ct);
        try
        {
            if (backendContext is null)
            {
                string control;
                if (run is not null && File.Exists(Path.Combine(Locations.LedgerRoot, run, "run.json"))) control = Locations.ControlDir(run);
                else
                {
                    using var process = Process.GetCurrentProcess();
                    control = ownedControl = Locations.CreateControlFolder($"cmd-{process.Id}-{process.StartTime.ToUniversalTime().Ticks}-{Locations.NewControlId()[..13]}");
                }
                backendContext = new(Path.Combine(control, "profiles"), ConfigLayers.User?.Backends?.Opencode?.Executable,
                    Cli.RootEnvironment?.Passthrough(ConfigLayers.User?.Backends?.Opencode?.Env ?? []) ?? new Dictionary<string, string>());
            }
            profile ??= new(backendContext.ProfilesFolder);
            modes ??= HostModes(DefaultHost);
            OpenCodeProfile.CreateFolder(backendContext.ProfilesFolder);
            OpenCodeProfile.CreateFolder(profile.Root);
            if (modes.Profile == "clean") profile.Prepare(routes ?? (route is null ? [] : [route]));
            var key = launch is not null ? HostKey(launch) : HostKey(modes);
            if (!hosts.TryGetValue(key, out var host))
            {
                host = await HarnessServer.StartAsync(subscribe, backendContext, profile.Root, modes, ct, launch);
                hosts[key] = host;
                pumps[key] = PumpAsync(host, key, lifetime.Token);
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
        var result = new List<LiveCheck>();
        foreach (var task in tasks)
        {
            if (Ready.Fenced(task.Modes) && await Ready.FenceAsync(ct) is { } refusal)
            {
                result.Add(new(task.TaskId, [refusal], [], "unknown", task.Modes.Profile, null));
                continue;
            }
            var server = await HostAsync(ct, task.Modes, task.Route,
                routes: tasks.Where(x => x.Modes.Profile == "clean").Select(x => x.Route));
            // The SDK decodes only its pinned wire contract; another server version can lose events.
            if (server.Version != Constants.PinnedUpstream)
                throw new HarnessException($"opencode server version '{server.Version}' differs from pin {Constants.PinnedUpstream}; install with npm install -g @opencode/cli@{Constants.PinnedUpstream}");
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
                if (catalog.Agents.All(x => x.Id != persona))
                    errors.Add(task.Modes.Profile == "clean" ? $"persona {persona} not found: profile clean loads no user agents; set profile full or use a built-in persona"
                        : $"persona {persona} not found");
                else if (catalog.Manual.Contains(persona)) errors.Add($"persona {persona} is manual-class");
                if (catalog.IsManualRoute(task.Route)) errors.Add($"route {task.Route} equals a manual-class agent's model");
                else if (catalog.CheckRoute(task.Route) is { } why) errors.Add($"route {task.Route}: {why}");
            }
            var window = route is null ? null : catalog.FindModel(route)?.Limit.Context;
            result.Add(new(task.TaskId, errors, [], route is null ? "unknown" : catalog.Lineage(route), task.Modes.Profile, null,
                route is null ? null : [window is null ? $"context window of opencode:{route} is not listed in the catalog" : $"context window {window} tokens (opencode:{route})"]));
        }
        return result;
    }

    async Task<CreateResult> CreateCoreAsync(WorkerLaunch launch, CancellationToken ct)
    {
        var server = await HostAsync(ct, launch.Modes, launch.Route, launch.RunId, launch: launch);
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
        var rules = OpenCodeRules.Render(launch.Rules, launch.RunId, launch.Modes);
        var created = await client.Sessions.CreateSessionAsync(new SessionCreateRequest
        {
            Title = $"dispatch {launch.RunId}/{launch.TaskId}",
            Agent = agent,
            Model = OpenCodeModels.ToModelRef(route),
            Location = new LocationPublicRef { Directory = launch.Directory },
            Permissions = rules,
            Metadata = OpenCodeJson.Metadata(launch.RunId, launch.TaskId, RunControlId(launch.RunId)),
        }, cancellationToken: ct);
        var worker = new WorkerRef(Name, launch.TaskId, created.Session.Id, null, new JsonObject
        {
            ["projectId"] = created.Session.ProjectId,
            ["host"] = HostKey(launch),
        });
        lock (eventGate)
        {
            admissions[worker.Id] = [];
            workers[worker.Id] = worker;
            liveWorkers[worker.Id] = 0;
        }
        launches[worker.Id] = launch;
        sessionHosts[worker.Id] = HostKey(worker);
        directories[worker.Id] = launch.Directory;
        try
        {
            await client.Sessions.GetSessionClient(worker.Id).SetEnvironmentAsync(new SessionEnvironmentRequest
            { Variables = SessionVariables(launch.Environment, server) }, cancellationToken: ct);
        }
        catch (Exception ex)
        {
            try { await ReleaseCoreAsync(worker, CancellationToken.None); }
            catch (Exception releaseError)
            { throw new WorkerCreationException(worker, $"worker setup failed: {ex.Message}; release unconfirmed: {releaseError.Message}"); }
            throw;
        }
        return new(worker, OpenCodeRules.Enforcement(launch.Modes), new($"opencode:{route}", agent, catalog.Lineage(route),
            created.Session.ProjectId, rules.Select(x => $"{x.Action} {x.Resource} {x.Effect.ToString().ToLowerInvariant()}").ToList()));
    }

    static Dictionary<string, string> SessionVariables(IReadOnlyDictionary<string, string> variables, HarnessServer host)
    {
        var copy = variables.ToDictionary(x => x.Key, x => x.Value);
        foreach (var (name, value) in host.SessionOverrides) copy[name] = value;
        return copy;
    }

    async Task<WorkerRef> PromptCoreAsync(WorkerRef worker, WorkerPrompt prompt, CancellationToken ct)
    {
        var host = await HostAsync(worker, ct);
        var session = host.Client.Sessions.GetSessionClient(worker.Id);
        if (prompt.IsContinue)
        {
            lock (eventGate) admissions[worker.Id] = [];
            if (!launches.TryGetValue(worker.Id, out var launch) && HostKey(worker).Contains(";environment=", StringComparison.Ordinal))
            {
                var ledger = new Ledger(runId!);
                var record = ledger.LoadRecord(worker.TaskId);
                launch = TaskStarter.BuildLaunch(record, ledger.RunId, Name, Policy.From(ConfigLayers.Frozen(ledger.Dir)), record.Settings.TempRoot?.Value).Launch;
            }
            var variables = launch?.Environment
                ?? WorkerEnvironment.Environment(worker.TaskId, runId ?? throw new HarnessException("continuing a reattached worker requires its run id"),
                    Git.RecordedTempRoot(worker.TaskId, runId)).Variables;
            await session.SetEnvironmentAsync(new SessionEnvironmentRequest { Variables = SessionVariables(variables, host) }, cancellationToken: ct);
            interrupts.TryRemove(worker.Id, out _);
        }
        liveWorkers[worker.Id] = 0;
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

    async Task<AnswerResult> AnswerCoreAsync(BlockerRef blocker, BlockerAnswer answer, CancellationToken ct, HarnessServer? server = null)
    {
        server ??= workers.TryGetValue(blocker.WorkerId, out var worker)
            ? await HostAsync(worker, ct) : await HostAsync(sessionHosts.GetValueOrDefault(blocker.WorkerId, DefaultHost), ct);
        var session = server.Client.Sessions.GetSessionClient(blocker.OriginChildId ?? blocker.WorkerId);
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
        if (hardStops.TryGetValue(worker.Id, out var previous)) return previous;
        if (kind == InterruptKind.Hard && LostFence(worker))
        {
            await DisposeFenceAsync(HostKey(worker));
            return new(true, "fenced server is gone");
        }
        if (kind == InterruptKind.Hard)
        {
            await workerGate.WaitAsync(ct);
            try
            {
                if (hardStops.TryGetValue(worker.Id, out previous)) return previous;
                await hostGate.WaitAsync(ct);
                try
                {
                    var key = HostKey(worker);
                    var shared = liveWorkers.Keys.Any(id => id != worker.Id && workers.TryGetValue(id, out var other) && HostKey(other) == key);
                    if (!shared && hosts.TryGetValue(key, out var server))
                    {
                        var stopped = await server.StopTreeAsync(ct);
                        await pumps[key];
                        hosts.TryRemove(key, out _);
                        if (stopped) liveWorkers.TryRemove(worker.Id, out _);
                        return hardStops[worker.Id] = new(stopped, "tree-kill");
                    }
                }
                finally { hostGate.Release(); }
            }
            finally { workerGate.Release(); }
        }
        var response = await (await HostAsync(worker, ct)).Client.Sessions.GetSessionClient(worker.Id)
            .InterruptAsync(new SessionInterruptRequest { Resume = false }, OpenCodeRequestOptions.NoThrow, ct);
        return new(false, response.IsError && kind != InterruptKind.Hard ? $"HTTP {response.Status}" : "interrupt",
            response.IsError ? null : response.Interrupt.Interrupted, response.Status);
    }

    async Task<ReattachResult> ReattachCoreAsync(WorkerRef worker, CancellationToken ct)
    {
        if (LostFence(worker)) return new ReattachResult.Ended("lost");
        var read = await (await HostAsync(worker, ct)).Client.Sessions.GetSessionClient(worker.Id).GetAsync(OpenCodeRequestOptions.NoThrow, ct);
        if (read.IsError) return new ReattachResult.Unreadable($"session {worker.Id} unreadable: HTTP {read.Status} {read.Error?.Tag}");
        lock (eventGate)
        {
            workers[worker.Id] = worker;
            RememberTurn(worker);
        }
        directories[worker.Id] = read.Session.Location.Directory;
        sessionHosts[worker.Id] = HostKey(worker);
        if (read.Session.Outcome == SessionInfoOutcome.Succeeded) return new ReattachResult.Ended("succeeded");
        if (read.Session.Outcome == SessionInfoOutcome.Failed) return new ReattachResult.Ended("failed");
        return new ReattachResult.Interrupted();
    }

    async Task ReleaseCoreAsync(WorkerRef worker, CancellationToken ct)
    {
        var server = LostFence(worker) ? await HostAsync(ct) : await HostAsync(worker, ct);
        var response = await server.Client.Sessions.GetSessionClient(worker.Id).RemoveAsync(OpenCodeRequestOptions.NoThrow, ct);
        if (response.IsError && response.Status != 404) throw new HarnessException($"session {worker.Id} release failed: HTTP {response.Status}");
        lock (eventGate)
        {
            workers.TryRemove(worker.Id, out _);
            liveWorkers.TryRemove(worker.Id, out _);
            admissions.Remove(worker.Id);
        }
    }

    async Task DisposeFenceAsync(string key)
    {
        if (key.StartsWith("fence=", StringComparison.Ordinal) && hosts.TryRemove(key, out var server))
        {
            await server.DisposeAsync();
            await pumps[key];
        }
    }

    public Task<IReadOnlyList<WorkerRef>> FindOrphansAsync(OrphanQuery query, CancellationToken ct) =>
        GuardAsync<IReadOnlyList<WorkerRef>>(async () =>
        {
            var controlId = RunControlId(query.RunId);
            if (controlId is null) return [];
            var server = await HostAsync(ct);
            var knownTasks = query.KnownWorkers.Where(x => x.Backend == Name).Select(x => x.TaskId).ToHashSet(StringComparer.Ordinal);
            var found = new Dictionary<string, WorkerRef>(StringComparer.Ordinal);
            foreach (var directory in query.Directories.Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal))
            {
                await foreach (var session in server.Client.Sessions.EnumerateSessionsAsync(new SessionListRequest { Directory = directory }, cancellationToken: ct))
                {
                    if (session.Metadata is not { } metadata || !metadata.TryGetValue("dispatch.run", out var run)
                        || run.ValueKind != JsonValueKind.String || run.GetString() != query.RunId
                        || !metadata.TryGetValue("dispatch.controlId", out var control) || control.ValueKind != JsonValueKind.String
                        || control.GetString() != controlId
                        || !metadata.TryGetValue("dispatch.task", out var task) || task.ValueKind != JsonValueKind.String
                        || string.IsNullOrEmpty(task.GetString()) || knownTasks.Contains(task.GetString()!)) continue;
                    found.TryAdd(session.Id, new(Name, task.GetString()!, session.Id, null,
                        new JsonObject { ["projectId"] = session.ProjectId, ["host"] = DefaultHost }));
                }
            }
            return found.Values.ToList();
        });

    static string? RunControlId(string run) => File.Exists(Path.Combine(Locations.LedgerRoot, run, "run.json"))
        ? Path.GetFileName(Locations.ControlDir(run)) : null;

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
            try { await Task.WhenAll(pumps.Values); }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { /* Owned shutdown. */ }
        }
        finally
        {
            try { await Task.WhenAll(hosts.Values.Select(async host => await host.DisposeAsync())); }
            finally
            {
                signals.Writer.TryComplete();
                if (ownedControl is not null && Directory.Exists(ownedControl)) Directory.Delete(ownedControl, recursive: true);
                lifetime.Dispose();
                hostGate.Dispose();
                workerGate.Dispose();
            }
        }
    }
}
