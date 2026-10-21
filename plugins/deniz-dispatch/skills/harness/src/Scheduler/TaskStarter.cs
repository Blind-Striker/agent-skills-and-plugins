sealed class TaskStarter(IWorkerBackend backend, Ledger ledger, string repo, AllowList allow, Policy policy, Git? git = null, TimeProvider? timeProvider = null)
{
    public static Modes ModesFor(TaskRecord record) => new(
        record.Settings.Profile?.Value ?? "full", record.Settings.Project?.Value ?? "on",
        record.Settings.Access?.Value ?? (record.Spec.Kind is "review" or "research" ? "read-only" : "workspace"),
        record.Settings.Network?.Value ?? "on", record.Settings.Workspace?.Value ?? "worktree");

    public static WorkerRules RulesFor(Policy policy) => new(
        policy.Deny.Select(Convert).ToList(), policy.Escalate.Select(Convert).ToList());

    static EffectiveRule Convert(PolicyRule rule) => new() { Name = rule.Name!, Action = rule.Action!, Resources = rule.Resources! };

    public void CheckAllowed(TaskRun run)
    {
        var spec = run.Record.Spec;
        if (spec.Agent is { } agent)
        {
            if (!allow.AllowsAgent(agent)) throw new HarnessException($"agent {agent} left the allow-list");
        }
        else if (!allow.Allows(run.Record.Settings.Route?.Value ?? $"{backend.Name}:{spec.Route}"))
            throw new HarnessException($"route {spec.Route} left the allow-list");
    }

    public async Task StartAsync(TaskRun run, CancellationToken ct)
    {
        await Worktree.PrepareAsync(run, ledger, repo, ct, git);
        CheckAllowed(run);
        var record = run.Record;
        RouteRef? route;
        if (backend.Name == "codex") route = CodexBackend.ParseRoute(record.Spec.Route!);
        else RouteRef.TryParse(record.Spec.Route ?? "agent/placeholder", out route, out _);
        var (variables, removed) = WorkerEnvironment.Environment(run.Id, ledger.RunId);
        var created = await backend.CreateAsync(new(ledger.RunId, run.Id, record.Spec.Kind!, route!, record.Spec.Agent,
            record.Spec.Persona, ModesFor(record), record.Workspace.Path!, RulesFor(policy), variables,
            record.Spec.Kind == "structured" ? record.PromptInputs?.Schema as JsonObject : null,
            record.Settings.Profile?.Actual ?? record.Settings.Profile?.Value ?? "full", record.Settings.Profile?.Reason), ct);
        // Persist the reference before calling PromptAsync, even if that call fails or never returns.
        run.SaveWorker(created.Worker, ledger);
        if (backend.Name == "codex")
        {
            if (record.Settings.Access is { } access) access.Enforcement = created.Applied.Access;
            if (record.Settings.Network is { } network) network.Enforcement = created.Applied.Network;
        }
        if (backend.Name == "codex" && record.Settings.Profile is { } profile)
        {
            profile.Actual = created.Worker.State["profile"]?.GetValue<string>() ?? profile.Value;
            profile.Reason = created.Worker.State["profileReason"]?.GetValue<string>();
        }
        if (created.Details is { } details)
        {
            record.Settings.Route ??= new Setting<string> { Value = details.Route, From = "task" };
            record.Agent = details.Agent;
            record.Lineage = details.Lineage;
            ledger.SaveRecord(record);
            ledger.Write(run.Id, "route", new JsonObject { ["route"] = record.Settings.Route.Value, ["agent"] = record.Agent, ["lineage"] = record.Lineage });
            ledger.Write(run.Id, "session", new JsonObject { ["sessionId"] = created.Worker.Id, ["projectId"] = details.ProjectId, ["rules"] = Json.Array(details.Rules) });
        }
        ledger.Write(run.Id, "environment", new JsonObject { ["variables"] = variables.Count, ["removed"] = Json.Array(removed) });
        if (record.PromptInputs is null && record.Contracts is { Count: > 0 })
            throw new HarnessException("original contract text is unavailable; cannot start an unprompted task from the ledger");
        var text = policy.Render(new PlannedOrRecord(record.Spec, record.PromptText), record.Workspace.Path!, record.PromptInputs);
        run.PromptAttempted = true;
        var prompted = await backend.PromptAsync(created.Worker, new(text, false), ct);
        run.SaveWorker(prompted, ledger);
        var data = Prompt.Event(text, prompted.Turn!, record.PromptInputs, record.Spec.RawPrompt == true);
        data["promptId"] = prompted.Turn;
        ledger.Write(run.Id, "prompt", data);
        run.Deadline = (timeProvider ?? TimeProvider.System).GetUtcNow().AddMinutes(record.Spec.TimeoutMinutes ?? Constants.DefaultTimeoutMinutes);
        run.State = TaskState.Running;
    }
}
