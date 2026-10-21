static partial class Commands
{
    public static string CreateRunId(string name, TimeProvider? time = null) =>
        $"{(time ?? TimeProvider.System).GetUtcNow().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}-{name}";

    public static async Task<int> RoutesAsync(Args a, CancellationToken ct)
    {
        var allow = AllowList.Load();
        var dir = Path.GetFullPath(a.Get("dir") ?? a.Get("repo") ?? throw new HarnessException("routes needs --dir or --repo"));
        await using var host = await HarnessServer.StartAsync(subscribe: false, ct);
        var catalog = await Catalog.LoadAsync(host.Client, dir, TimeSpan.FromSeconds(60), ct);

        Console.WriteLine($"location {dir}");
        Console.WriteLine($"server {host.Version}{(host.Version == Constants.PinnedUpstream ? "" : $" (SDK pin is {Constants.PinnedUpstream})")}");
        Console.WriteLine();
        Console.WriteLine($"agents ({catalog.Agents.Count}; manual-class {catalog.Manual.Count})");
        foreach (var agent in catalog.Agents.OrderBy(x => x.Id, StringComparer.Ordinal))
        {
            var model = agent.Model is { } m ? RouteRef.From(m).ToString() : "-";
            var flags = new List<string>();
            if (catalog.Manual.Contains(agent.Id)) flags.Add("MANUAL");
            if (allow.AllowsAgent(agent.Id)) flags.Add("allowed");
            if (agent.Hidden) flags.Add("hidden");
            Console.WriteLine($"  {agent.Id,-40} {agent.Mode.ToString().ToLowerInvariant(),-9} {model} {string.Join(' ', flags)}");
        }

        Console.WriteLine();
        var models = catalog.Models.Where(x => a.Has("all") || x.Enabled).OrderBy(x => x.ProviderId).ThenBy(x => x.Id).ToList();
        Console.WriteLine($"models ({models.Count} shown of {catalog.Models.Count})");
        foreach (var model in models)
        {
            var route = new RouteRef(model.ProviderId, model.Id, null);
            var variants = model.Variants.Count == 0 ? "-" : string.Join(',', model.Variants.Select(v => v.Id));
            var allowed = allow.Routes.Where(r => r.Provider == route.Provider && r.Model == route.Model).Select(r => r.VariantOrDefault).ToList();
            Console.WriteLine($"  {route.Provider}/{route.Model,-50} variants: {variants}{(model.Enabled ? "" : " disabled")}{(allowed.Count > 0 ? $" allowed: {string.Join(',', allowed)}" : "")}");
        }

        Console.WriteLine();
        Console.WriteLine($"allow-list {Locations.AllowListFile}: {allow.Count} entries");
        foreach (var route in allow.Routes)
        {
            var state = catalog.IsManualRoute(route) ? "refused: equals a manual-class agent's model" : catalog.CheckRoute(route) ?? "ok";
            Console.WriteLine($"  {route} {state}");
        }

        foreach (var agent in allow.Agents.Order(StringComparer.Ordinal))
        {
            var state = catalog.CheckAgent(agent) ?? "ok";
            Console.WriteLine($"  {agent} {state}");
        }

        return 0;
    }

    public static async Task<int> PlanAsync(Args a, CancellationToken ct)
    {
        var allow = AllowList.Load();
        var policy = Policy.Load(a.Get("policy"));
        Console.WriteLine(policy.Describe());
        var plan = await Planner.LoadAsync(a.Require(0, "task file"), a.Get("repo"), allow, ct);
        if (plan.Errors.Count == 0)
        {
            await using var host = await HarnessServer.StartAsync(subscribe: false, ct);
            var catalog = await Catalog.LoadAsync(host.Client, plan.Repo, TimeSpan.FromSeconds(60), ct);
            Planner.CheckLive(plan, catalog, allow);
        }

        return plan.Report();
    }

    public static async Task<int> RunAsync(Args a, CancellationToken ct)
    {
        var allow = AllowList.Load();
        if (allow.Count == 0)
        {
            Console.Error.WriteLine($"""
                dispatch: run refuses to start: the route allow-list is empty.
                  {Locations.AllowListFile}
                The allow-list owner fills it with "provider/model#variant" routes or roster agent names;
                until then only routes, plan, status and selfcheck are available.
                """);
            return 2;
        }

        var policy = Policy.Load(a.Get("policy"));
        Console.WriteLine(policy.Describe());
        var tasksPath = a.Require(0, "task file");
        var plan = await Planner.LoadAsync(tasksPath, a.Get("repo"), allow, ct);
        if (plan.Errors.Count > 0)
        {
            return plan.Report();
        }

        var name = a.Get("run") ?? plan.File.Run ?? Path.GetFileNameWithoutExtension(tasksPath);
        if (!Regex.IsMatch(name, "^[a-z0-9][a-z0-9-]{0,40}$"))
        {
            throw new HarnessException($"run name '{name}' must match ^[a-z0-9][a-z0-9-]{{0,40}}$");
        }

        var runId = CreateRunId(name);
        var ledger = new Ledger(runId);
        if (Directory.Exists(ledger.Dir))
        {
            throw new HarnessException($"run {runId} already exists; use `resume {runId}` or choose --run");
        }

        var max = int.TryParse(a.Get("max"), CultureInfo.InvariantCulture, out var m) ? m : plan.File.MaxParallel ?? Constants.DefaultMaxParallel;
        if (max is < 1 or > 8)
        {
            throw new HarnessException("--max must be between 1 and 8");
        }

        using var supervisorLock = SupervisorLock.Acquire(runId);
        AskQueue.Reset(runId);
        await using var host = await HarnessServer.StartAsync(subscribe: true, ct);
        var catalog = await Catalog.LoadAsync(host.Client, plan.Repo, TimeSpan.FromSeconds(60), ct);
        Planner.CheckLive(plan, catalog, allow);
        if (plan.Errors.Count > 0)
        {
            return plan.Report();
        }

        Directory.CreateDirectory(ledger.Dir);
        File.Copy(plan.TasksPath, Path.Combine(ledger.Dir, "tasks.jsonc"));
        File.Copy(policy.Path, Path.Combine(ledger.Dir, "policy.jsonc"));
        var info = new RunInfo
        {
            Run = runId,
            Repo = plan.Repo,
            TasksFile = plan.TasksPath,
            Created = Json.Timestamp(),
            Backends = new() { ["opencode"] = new BackendInfo { Version = host.Version } },
            MaxParallel = max,
        };
        Json.WriteFile(Path.Combine(ledger.Dir, "run.json"), info, LedgerJson.Files.RunInfo);
        var started = new JsonObject
        {
            ["repo"] = plan.Repo,
            ["max"] = max,
            ["server"] = host.Version,
            ["tasks"] = plan.Tasks.Count,
            ["policy"] = policy.Path,
            ["deny"] = Json.Array(policy.Deny.Select(x => x.Name!)),
            ["escalate"] = Json.Array(policy.Escalate.Select(x => x.Name!)),
        };
        if (policy.RawPrompt)
        {
            started["rawPrompt"] = true;
        }

        ledger.Write(null, "run", started);

        var runs = new List<TaskRun>();
        foreach (var task in plan.Tasks)
        {
            var record = new TaskRecord { Spec = task.Spec, PromptText = task.PromptText };
            ledger.SaveRecord(record);
            ledger.Write(task.Spec.Id, "queued", new JsonObject
            {
                ["kind"] = task.Spec.Kind,
                ["route"] = task.Route?.ToString(),
                ["agent"] = task.Spec.Agent,
                ["reviewOf"] = task.Spec.ReviewOf,
            });
            runs.Add(new TaskRun { Record = record });
        }

        var supervisor = new Supervisor(host, ledger, plan.Repo, allow, policy, max, ct);
        return await supervisor.RunAsync(runs);
    }

    public static int Status(Args a)
    {
        if (a.Positional.Count == 0)
        {
            if (!Directory.Exists(Locations.LedgerRoot))
            {
                Console.WriteLine("no runs");
                return 0;
            }

            foreach (var dir in Directory.GetDirectories(Locations.LedgerRoot).Order(StringComparer.Ordinal))
            {
                var ledger = new Ledger(Path.GetFileName(dir));
                var states = ledger.TaskStates();
                var summary = string.Join(", ", states.GroupBy(x => x.Value).Select(g => $"{g.Key} {g.Count()}"));
                Console.WriteLine($"{ledger.RunId,-40} {summary}{(SupervisorLock.IsHeld(ledger.RunId) ? " (supervised now)" : "")}");
            }

            return 0;
        }

        var run = new Ledger(a.Require(0, "run"));
        run.EnsureExists();
        Console.WriteLine($"run {run.RunId}{(SupervisorLock.IsHeld(run.RunId) ? " (supervised now)" : "")}");
        Console.WriteLine($"  {"task",-24} {"state",-10} {"outcome",-12} {"cost",8} {"in/out tokens",-16} {"files",5} {"scope!",6} {"asks",4} {"esc",3} verify");
        foreach (var (task, state) in run.TaskStates())
        {
            var result = run.LoadResult(task);
            var tokens = result?.Tokens is { } t ? $"{t.Input:0}/{t.Output:0}" : "-";
            var verify = result is null || result.Verify.Count == 0
                ? "-"
                : string.Join(' ', result.Verify.Select(v => v.ExitCode == 0 ? "ok" : $"exit {v.ExitCode}"));
            Console.WriteLine($"  {task,-24} {state,-10} {result?.Outcome ?? "-",-12} {result?.Cost?.ToString("0.0000", CultureInfo.InvariantCulture) ?? "-",8} {tokens,-16} {result?.FilesChanged.Count.ToString(CultureInfo.InvariantCulture) ?? "-",5} {result?.OutOfScope.Count.ToString(CultureInfo.InvariantCulture) ?? "-",6} {result?.Asks.ToString(CultureInfo.InvariantCulture) ?? "-",4} {result?.AsksEscalated.ToString(CultureInfo.InvariantCulture) ?? "-",3} {verify}");
        }

        var pending = AskQueue.List(run.RunId);
        if (pending.Count > 0)
        {
            Console.WriteLine($"  {pending.Count} escalated ask(s) wait for the operator; `asks {run.RunId}` lists them");
        }

        return 0;
    }

    public static async Task<int> ResumeAsync(Args a, CancellationToken ct)
    {
        var ledger = new Ledger(a.Require(0, "run"));
        ledger.EnsureExists();
        var allow = AllowList.Load();
        var proceed = a.Has("continue");
        if (proceed && allow.Count == 0)
        {
            throw new HarnessException("resume --continue prompts models, and the route allow-list is empty");
        }

        // The run keeps the policy it started with (its copy in the ledger) unless --policy names
        // another. Existing sessions keep the rules they were created with either way.
        var ledgerPolicy = Path.Combine(ledger.Dir, "policy.jsonc");
        var policy = Policy.Load(a.Get("policy") ?? (File.Exists(ledgerPolicy) ? ledgerPolicy : null));
        Console.WriteLine(policy.Describe());
        var info = ledger.LoadRunInfo();
        using var supervisorLock = SupervisorLock.Acquire(ledger.RunId);
        AskQueue.Reset(ledger.RunId);
        await using var host = await HarnessServer.StartAsync(subscribe: true, ct);
        var states = ledger.TaskStates();
        var runs = new List<TaskRun>();
        foreach (var (task, state) in states)
        {
            if (state is "done" or "failed" or "cancelled")
            {
                continue;
            }

            var record = ledger.LoadRecord(task);
            var run = TaskRun.FromLedger(record, ledger);
            if (record.Session.Id is null && !proceed)
            {
                Console.WriteLine($"{task}: never prompted; `resume --continue` dispatches it");
                continue;
            }

            runs.Add(run);
        }

        if (runs.Count == 0)
        {
            Console.WriteLine("nothing to resume");
            return 0;
        }

        ledger.Write(null, "resume", new JsonObject { ["continue"] = proceed, ["tasks"] = runs.Count, ["server"] = host.Version });
        var supervisor = new Supervisor(host, ledger, info.Repo!, allow, policy, info.MaxParallel, ct);
        return await supervisor.ResumeAsync(runs, proceed);
    }
}
