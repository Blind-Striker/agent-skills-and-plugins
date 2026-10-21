static partial class Commands
{
    public static string CreateRunId(string name, TimeProvider? time = null) =>
        $"{(time ?? TimeProvider.System).GetUtcNow().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}-{name}";

    public static async Task<int> RoutesAsync(Args a, CancellationToken ct)
    {
        var dir = Path.GetFullPath(a.Get("dir") ?? a.Get("repo") ?? throw new HarnessException("routes needs --dir or --repo"));
        var layers = await ConfigLayers.LoadAsync(dir, new TaskFile(), null, a, ct);
        var allow = layers.Allow;
        await using IWorkerBackend backend = new OpenCodeBackend(subscribe: false);
        var probe = await backend.ProbeAsync(new(dir), ct);
        var catalog = await backend.ListRoutesAsync(dir, ct);

        Console.WriteLine($"location {dir}");
        Console.WriteLine($"server {probe.Version}{(probe.Version == Constants.PinnedUpstream ? "" : $" (SDK pin is {Constants.PinnedUpstream})")}");
        Console.WriteLine();
        Console.WriteLine($"agents ({catalog.Agents.Count}; manual-class {catalog.Agents.Count(x => x.Manual)})");
        foreach (var agent in catalog.Agents.OrderBy(x => x.Name, StringComparer.Ordinal))
        {
            var model = agent.Model ?? "-";
            var flags = new List<string>();
            if (agent.Manual) flags.Add("MANUAL");
            if (allow.AllowsAgent(agent.Name)) flags.Add("allowed");
            if (agent.Hidden) flags.Add("hidden");
            Console.WriteLine($"  {agent.Name,-40} {agent.Mode,-9} {model} {string.Join(' ', flags)}");
        }

        Console.WriteLine();
        var models = catalog.Routes.Where(x => a.Has("all") || x.Enabled).OrderBy(x => x.Route.Provider).ThenBy(x => x.Route.Model).ToList();
        Console.WriteLine($"models ({models.Count} shown of {catalog.Routes.Count})");
        foreach (var model in models)
        {
            var route = model.Route;
            var variants = model.Variants.Count == 0 ? "-" : string.Join(',', model.Variants);
            var allowed = allow.Routes.Where(r => r.Provider == route.Provider && r.Model == route.Model).Select(r => r.VariantOrDefault).ToList();
            Console.WriteLine($"  {route.Provider}/{route.Model,-50} variants: {variants}{(model.Enabled ? "" : " disabled")}{(allowed.Count > 0 ? $" allowed: {string.Join(',', allowed)}" : "")}");
        }

        Console.WriteLine();
        Console.WriteLine(allow.IsRestricted ? $"allow-list: {allow.Count} opencode entries" : "no route restriction");
        foreach (var route in allow.Routes)
        {
            var entry = catalog.Routes.FirstOrDefault(x => x.Route.Provider == route.Provider && x.Route.Model == route.Model);
            var manual = catalog.Agents.Any(x => x.Manual && RouteRef.TryParse(x.Model ?? "", out var pinned, out _) && pinned!.Matches(route));
            var state = manual ? "refused: equals a manual-class agent's model" : entry is null ? "not listed at this location"
                : !entry.Enabled ? "listed but disabled" : route.VariantOrDefault != "default" && !entry.Variants.Contains(route.Variant!)
                    ? $"variant {route.Variant} not listed (has: {string.Join(',', entry.Variants)})" : "ok";
            Console.WriteLine($"  {route} {state}");
        }

        foreach (var agent in allow.Agents.Order(StringComparer.Ordinal))
        {
            var state = catalog.Agents.FirstOrDefault(x => x.Name == agent)?.Reason ?? (catalog.Agents.Any(x => x.Name == agent) ? "ok" : "agent not found at this location");
            Console.WriteLine($"  {agent} {state}");
        }

        return 0;
    }

    public static async Task<int> PlanAsync(Args a, CancellationToken ct)
    {
        var plan = await Planner.LoadAsync(a.Require(0, "task file"), a.Get("repo"), AllowList.From(null), ct, args: a);
        var allow = plan.Config?.Allow ?? AllowList.From(null);
        if (plan.Errors.Count == 0)
        {
            await using IWorkerBackend backend = new OpenCodeBackend(subscribe: false);
            await Planner.CheckLiveAsync(plan, backend, allow, ct);
        }

        return plan.Report();
    }

    public static async Task<int> RunAsync(Args a, CancellationToken ct, Func<RunInfo, string, Task>? setupCheckpoint = null)
    {
        var tasksPath = a.Require(0, "task file");
        var plan = await Planner.LoadAsync(tasksPath, a.Get("repo"), AllowList.From(null), ct, args: a);
        if (plan.Errors.Count > 0)
        {
            return plan.Report();
        }

        var layers = plan.Config!;
        foreach (var task in plan.Tasks)
            layers.Effective.Tasks[task.Spec.Id!].Contracts = task.PromptInputs?.Contracts is { Count: > 0 } contracts ? contracts.Select(c => c.Record).ToList() : null;
        var allow = layers.Allow;
        var policy = layers.Policy;
        Console.WriteLine(policy.Describe());

        var name = a.Get("run") ?? plan.File.Run ?? Path.GetFileNameWithoutExtension(tasksPath);
        if (!Regex.IsMatch(name, "^[a-z0-9][a-z0-9-]{0,40}$"))
        {
            throw new HarnessException($"run name '{name}' must match ^[a-z0-9][a-z0-9-]{{0,40}}$");
        }

        var runId = CreateRunId(name);
        var ledger = new Ledger(runId);
        if (new[] { "run.json", "config.json", "tasks.jsonc", "events.jsonl" }.Any(file => File.Exists(Path.Combine(ledger.Dir, file))))
        {
            throw RecordedRun(runId);
        }

        var max = layers.Effective.MaxParallel!.Value;
        if (max is < 1 or > 8)
        {
            throw new HarnessException("--max must be between 1 and 8");
        }

        Locations.ValidateControlLocation(plan.Repo);
        await using IWorkerBackend backend = new OpenCodeBackend();
        var probe = await backend.ProbeAsync(new(plan.Repo), ct);
        await Planner.CheckLiveAsync(plan, backend, allow, ct);
        if (plan.Errors.Count > 0)
        {
            return plan.Report();
        }

        var info = new RunInfo
        {
            Run = runId,
            ControlId = Locations.NewControlId(),
            Repo = plan.Repo,
            TasksFile = plan.TasksPath,
            Created = Json.Timestamp(),
            Backends = new() { ["opencode"] = new BackendInfo { Version = probe.Version! } },
            MaxParallel = max,
        };
        var control = Path.Combine(Locations.ControlRoot, info.ControlId);
        SupervisorLock? supervisorLock = null;
        var claimed = false;
        try
        {
            Locations.CreateControlFolder(info.ControlId);
            supervisorLock = SupervisorLock.AcquireFolder(control);
            Json.WriteFile(Path.Combine(control, "run.ref"), new RunReference { Run = runId, LedgerRoot = Locations.LedgerRoot }, ControlJson.Files.RunReference);
            var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(info, LedgerJson.Files.RunInfo) + "\n");
            Directory.CreateDirectory(ledger.Dir);
            try
            {
                using var claim = new FileStream(Path.Combine(ledger.Dir, "run.json"), FileMode.CreateNew, FileAccess.Write, FileShare.Read);
                claimed = true;
                claim.Write(bytes);
                claim.Flush(flushToDisk: true);
            }
            catch (IOException) when (!claimed && File.Exists(Path.Combine(ledger.Dir, "run.json"))) { throw RecordedRun(runId); }
            if (setupCheckpoint is not null) await setupCheckpoint(info, "claimed");
            File.Copy(plan.TasksPath, Path.Combine(ledger.Dir, "tasks.jsonc"));
            layers.Freeze(ledger.Dir);
            var started = new JsonObject
            {
                ["repo"] = plan.Repo,
                ["max"] = max,
                ["server"] = probe.Version,
                ["tasks"] = plan.Tasks.Count,
                ["policy"] = policy.Path,
                ["deny"] = Json.Array(policy.Deny.Select(x => x.Name!)),
                ["escalate"] = Json.Array(policy.Escalate.Select(x => x.Name!)),
                ["removed"] = Json.Array(Cli.RootEnvironment?.Removed ?? []),
            };
            if (plan.File.SharedPaths is { Count: > 0 } shared)
                started["sharedPaths"] = new JsonObject { ["paths"] = Json.Array(shared), ["mergedBy"] = "coordinator" };
            if (policy.RawPrompt)
            {
                started["rawPrompt"] = true;
            }

            ledger.Write(null, "run", started);

            var runs = new List<TaskRun>();
            foreach (var task in plan.Tasks)
            {
                var record = new TaskRecord
                {
                    Spec = task.Spec,
                    PromptText = task.PromptText,
                    Settings = layers.Effective.Tasks[task.Spec.Id!].Settings,
                    PromptInputs = task.PromptInputs,
                    Contracts = layers.Effective.Tasks[task.Spec.Id!].Contracts
                };
                ledger.SaveRecord(record);
                ledger.Write(task.Spec.Id, "queued", new JsonObject
                {
                    ["kind"] = task.Spec.Kind,
                    ["route"] = layers.Effective.Tasks[task.Spec.Id!].Settings.Route?.Value,
                    ["agent"] = task.Spec.Agent,
                    ["reviewOf"] = task.Spec.ReviewOf,
                });
                runs.Add(new TaskRun { Record = record });
            }

            if (setupCheckpoint is not null) await setupCheckpoint(info, "queued");
            using (var done = new FileStream(Path.Combine(control, "setup.done"), FileMode.CreateNew, FileAccess.Write, FileShare.Read))
                done.Flush(flushToDisk: true);
            var supervisor = new Supervisor(backend, ledger, plan.Repo, allow, policy, max, ct);
            return await supervisor.RunAsync(runs);
        }
        catch (Exception ex) when (claimed)
        {
            ledger.Write(null, "aborted", new JsonObject { ["message"] = ex.Message });
            Console.Error.WriteLine($"dispatch: {ex.GetType().Name}: {ex.Message}");
            return 1;
        }
        catch (Exception ex) when (ex is not HarnessException)
        {
            throw new HarnessException($"{ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            supervisorLock?.Dispose();
            if (!claimed && Directory.Exists(control)) Directory.Delete(control, recursive: true);
        }
    }

    static HarnessException RecordedRun(string run) => new($"run {run} is already recorded; a rerun takes a new run id; `collect` or `resume` recovers it");

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
        if (File.Exists(Path.Combine(run.Dir, "run.json"))) Locations.ControlDir(run.RunId);
        else run.EnsureExists();
        Console.WriteLine($"run {run.RunId}{(SupervisorLock.IsHeld(run.RunId) ? " (supervised now)" : "")}");
        Console.WriteLine($"  {"task",-24} {"state",-10} {"outcome",-12} {"cost",8} {"in/out tokens",-16} {"files",5} {"scope!",6} {"asks",4} {"esc",3} verify");
        var toolCalls = run.Read().Where(x => x.Type == "tool" && x.Task is not null).ToLookup(x => x.Task!);
        foreach (var (task, state) in run.TaskStates())
        {
            var result = run.LoadResult(task);
            var tokens = result?.Tokens is { } t ? $"{t.Input:0}/{t.Output:0}" : "-";
            var verify = result is null || result.Verify.Count == 0
                ? "-"
                : string.Join(' ', result.Verify.Select(v => v.ExitCode == 0 ? "ok" : $"exit {v.ExitCode}"));
            Console.WriteLine($"  {task,-24} {state,-10} {result?.Outcome ?? "-",-12} {result?.Cost?.ToString("0.0000", CultureInfo.InvariantCulture) ?? "-",8} {tokens,-16} {result?.FilesChanged.Count.ToString(CultureInfo.InvariantCulture) ?? "-",5} {result?.OutOfScope.Count.ToString(CultureInfo.InvariantCulture) ?? "-",6} {result?.Asks.ToString(CultureInfo.InvariantCulture) ?? "-",4} {result?.AsksEscalated.ToString(CultureInfo.InvariantCulture) ?? "-",3} {verify}");
            if (state is "running" or "waiting" or "dispatching" or "settling")
            {
                var calls = toolCalls[task].ToList();
                var last = calls.LastOrDefault();
                Console.WriteLine(last is null ? "    tools: 0; last: -"
                    : $"    tools: {calls.Count}; last: {last.Data?["tool"]} {last.Data?["resource"]} ({last.Data?["outcome"]}) at {last.Ts}");
            }
        }

        var pending = File.Exists(Path.Combine(run.Dir, "run.json")) ? AskQueue.List(run.RunId) : [];
        if (pending.Count > 0)
        {
            Console.WriteLine($"  {pending.Count} escalated ask(s) wait for the operator; `asks {run.RunId}` lists them");
        }

        return 0;
    }

    public static async Task<int> ResumeAsync(Args a, CancellationToken ct)
    {
        var ledger = new Ledger(a.Require(0, "run"));
        var control = Locations.RequireControlFolder(ledger.RunId);
        using var supervisorLock = SupervisorLock.AcquireFolder(control);
        Locations.RequireControlFolder(ledger.RunId, setup: true);
        var info = ledger.LoadRunInfo();
        var proceed = a.Has("continue");
        var frozen = ConfigLayers.Frozen(ledger.Dir);
        var allow = proceed ? (await ConfigLayers.LoadAsync(info.Repo, new TaskFile(), null, a, ct)).Allow
            : AllowList.From(frozen.Restrictions?.Routes);
        var policy = Policy.From(frozen);
        Console.WriteLine(policy.Describe());
        AskQueue.Reset(ledger.RunId);
        await using IWorkerBackend backend = new OpenCodeBackend();
        var probe = await backend.ProbeAsync(new(info.Repo!), ct);
        var states = ledger.TaskStates();
        var runs = new List<TaskRun>();
        foreach (var (task, state) in states)
        {
            if (state is "done" or "failed" or "cancelled")
            {
                continue;
            }

            var record = ledger.LoadRecord(task);
            record.Settings = frozen.Tasks[task].Settings;
            ConfigLayers.Apply(record.Spec, record.Settings);
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

        ledger.Write(null, "resume", new JsonObject { ["continue"] = proceed, ["tasks"] = runs.Count, ["server"] = probe.Version });
        var supervisor = new Supervisor(backend, ledger, info.Repo!, allow, policy, info.MaxParallel, ct);
        return await supervisor.ResumeAsync(runs, proceed);
    }
}
