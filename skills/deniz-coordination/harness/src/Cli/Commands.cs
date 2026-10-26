static partial class Commands
{
    public static string CreateRunId(string name, TimeProvider? time = null) =>
        $"{(time ?? TimeProvider.System).GetUtcNow().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}-{name}";

    // The usual x.tasks.jsonc names run x.
    public static string RunName(Args? a, TaskFile file, string tasksPath) =>
        a?.Get("run") ?? file.Run ?? Regex.Replace(Path.GetFileNameWithoutExtension(tasksPath), @"\.tasks\z", "");

    static string RunId(Args a) => Ledger.Resolve(a.Require(0, "run"));

    public static async Task<int> RoutesAsync(Args a, CancellationToken ct)
    {
        var dir = Path.GetFullPath(a.Get("repo") ?? throw new HarnessException("routes needs --repo"));
        var layers = await ConfigLayers.LoadAsync(dir, new TaskFile(), null, a, ct);
        layers.PrintWarnings();
        var allow = layers.Allow;
        var selectedBackends = BackendScope.Selected(a, layers);
        await using var scope = new BackendScope(selectedBackends, subscribe: false);
        foreach (var selected in selectedBackends)
        {
            Console.WriteLine($"backend {selected}");
            var backend = scope.Backends[selected];
            var probe = await backend.ProbeAsync(new(dir), ct);
            if (!probe.Ready) throw new HarnessException($"{selected} is not ready: {string.Join("; ", probe.Errors)}");
            var catalog = await backend.ListRoutesAsync(dir, ct);

            if (selected == "codex")
            {
                Console.WriteLine($"location {dir}\nCodex {probe.Version}");
                foreach (var entry in catalog.Routes)
                    Console.WriteLine($"  codex:{entry.Route.Model} efforts: {string.Join(',', entry.Variants)}");
                Console.WriteLine(allow.IsRestricted ? "route allow-list applies" : "no route restriction");
                continue;
            }

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
                Console.WriteLine($"  opencode:{agent.Name,-40} {agent.Mode,-9} {model} {string.Join(' ', flags)}");
            }

            Console.WriteLine();
            var models = catalog.Routes.Where(x => a.Has("all") || x.Enabled).OrderBy(x => x.Route.Provider).ThenBy(x => x.Route.Model).ToList();
            Console.WriteLine($"models ({models.Count} shown of {catalog.Routes.Count})");
            foreach (var model in models)
            {
                var route = model.Route;
                var variants = model.Variants.Count == 0 ? "-" : string.Join(',', model.Variants);
                var allowed = allow.Routes.Where(r => r.Provider == route.Provider && r.Model == route.Model).Select(r => r.VariantOrDefault).ToList();
                Console.WriteLine($"  opencode:{route.Provider}/{route.Model,-50} variants: {variants}{(model.Enabled ? "" : " disabled")}{(allowed.Count > 0 ? $" allowed: {string.Join(',', allowed)}" : "")}");
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
                Console.WriteLine($"  opencode:{route} {state}");
            }

            foreach (var agent in allow.Agents.Order(StringComparer.Ordinal))
            {
                var state = catalog.Agents.FirstOrDefault(x => x.Name == agent)?.Reason ?? (catalog.Agents.Any(x => x.Name == agent) ? "ok" : "agent not found at this location");
                Console.WriteLine($"  opencode:{agent} {state}");
            }
        }

        return 0;
    }

    public static async Task<int> PlanAsync(Args a, CancellationToken ct)
    {
        var plan = await Planner.LoadAsync(a.Require(0, "task file"), a.Get("repo"), AllowList.From(null), ct, args: a);
        var runId = CreateRunId(RunName(a, plan.File, plan.TasksPath));
        foreach (var spec in plan.File.Tasks ?? [])
        {
            if (spec.Kind == "structured" || plan.Config is null) continue;
            var root = plan.Config.Effective.Tasks[spec.Id!].Settings.TempRoot;
            var temp = Locations.TaskTemp(spec.Id!, runId, root?.Value);
            var source = root?.Value is null ? "(default)" : $"(tempRoot from {root.From})";
            Console.WriteLine($"INFO {spec.Id}: temp {temp} {source}");
        }
        foreach (var (id, task) in plan.Config?.Effective.Tasks ?? [])
            if (task.Settings.Access?.Value == "read-only" && task.Settings.ReadOnlyShell is { } shell)
                Console.WriteLine($"INFO {id}: {ReadOnlyShellNote(task.Settings.Backend?.Value, shell.Value)} (readOnlyShell {shell.Value}, from {shell.From})");
        var allow = plan.Config?.Allow ?? AllowList.From(null);
        if (plan.Errors.Count == 0)
        {
            await using var scope = new BackendScope(plan.Tasks.Select(t => t.Spec.Backend ?? "opencode"), subscribe: false);
            foreach (var backend in scope.Backends.Values) await Planner.CheckLiveAsync(plan, backend, allow, ct);
        }

        return plan.Report();
    }

    static string ReadOnlyShellNote(string? backend, string? shell) => (backend, shell) switch
    {
        ("codex", "decline") or ("opencode", "decline") when backend == "codex" || Ready.FenceKind is not null => "sandboxed shell; escalations declined",
        ("codex", _) or ("opencode", _) when backend == "codex" || Ready.FenceKind is not null => "sandboxed shell; escalations reach the operator through a matching rule",
        (_, "decline") => "no shell for a read-only task",
        _ => "shell asks reach the operator through a matching rule",
    };

    public static async Task<int> RunAsync(Args a, CancellationToken ct, Func<RunInfo, string, Task>? setupCheckpoint = null,
        Func<IWorkerBackend>? backendFactory = null, Func<string?>? userPathReader = null,
        Action<string>? beforeFrozenConfigMove = null)
    {
        var userPath = new UserPathGuard(userPathReader);
        if (userPath.RunRefusal is { } refusal)
        {
            Console.Error.WriteLine($"dispatch: {refusal}");
            return 2;
        }
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

        var name = RunName(a, plan.File, tasksPath);
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
        SupervisorLock? supervisorLock = null;
        string? control = null;
        var claimed = false;
        try
        {
            await using var injectedBackend = backendFactory?.Invoke();
            var versions = new Dictionary<string, BackendInfo>();
            if (injectedBackend is not null)
            {
                var probe = await injectedBackend.ProbeAsync(new(plan.Repo), ct);
                versions[injectedBackend.Name] = new() { Version = probe.Version! };
                await Planner.CheckLiveAsync(plan, injectedBackend, allow, ct);
            }
            else
            {
                await using var preflight = new BackendScope(plan.Tasks.Select(t => t.Spec.Backend ?? "opencode"));
                foreach (var backend in preflight.Backends.Values)
                {
                    var probe = await backend.ProbeAsync(new(plan.Repo), ct);
                    versions[backend.Name] = new() { Version = probe.Version! };
                    await Planner.CheckLiveAsync(plan, backend, allow, ct);
                }
            }
            if (plan.Errors.Count > 0)
            {
                return plan.Report();
            }
            plan.PrintWarnings();

            var info = new RunInfo
            {
                Run = runId,
                ControlId = Locations.NewControlId(),
                Repo = plan.Repo,
                TasksFile = plan.TasksPath,
                Created = Json.Timestamp(),
                Backends = versions,
                MaxParallel = max,
            };
            control = Path.Combine(Locations.ControlRoot, info.ControlId);
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
            Console.WriteLine($"run {runId}: ledger {ledger.Dir}");
            if (setupCheckpoint is not null) await setupCheckpoint(info, "claimed");
            File.Copy(plan.TasksPath, Path.Combine(ledger.Dir, "tasks.jsonc"));
            layers.Freeze(ledger.Dir);
            var started = new JsonObject
            {
                ["repo"] = plan.Repo,
                ["max"] = max,
                ["server"] = versions.Values.First().Version,
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
            StructuredOutput.Freeze(ledger, runs, beforeFrozenConfigMove);
            using (var done = new FileStream(Path.Combine(control, "setup.done"), FileMode.CreateNew, FileAccess.Write, FileShare.Read))
                done.Flush(flushToDisk: true);
            await using var scope = injectedBackend is null ? new BackendScope(versions.Keys, control, runId: runId) : null;
            var backends = scope?.Backends ?? new Dictionary<string, IWorkerBackend> { [injectedBackend!.Name] = injectedBackend };
            var supervisor = new Supervisor(backends, ledger, plan.Repo, allow, policy, max, ct);
            return await supervisor.RunAsync(runs);
        }
        catch (Exception ex) when (claimed)
        {
            return Aborted(ledger, ex);
        }
        catch (Exception ex) when (ex is not HarnessException)
        {
            throw new HarnessException($"{ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            try
            {
                if (claimed) userPath.ReportChanges(ledger);
            }
            finally
            {
                supervisorLock?.Dispose();
                if (!claimed && control is not null && Directory.Exists(control)) Directory.Delete(control, recursive: true);
            }
        }
    }

    static HarnessException RecordedRun(string run) => new($"run {run} is already recorded; a rerun takes a new run id; `collect` or `resume` recovers it");

    static int Aborted(Ledger ledger, Exception error)
    {
        var message = $"{error.GetType().Name}: {error.Message}";
        try
        {
            ledger.Write(null, "aborted", new JsonObject { ["message"] = error.Message });
        }
        catch (Exception writeError)
        {
            message += $"; could not record aborted event: {writeError.GetType().Name}: {writeError.Message}";
        }
        Console.Error.WriteLine($"dispatch: {message.Replace('\r', ' ').Replace('\n', ' ')}");
        return 1;
    }

    // A run with unfinished tasks and no supervisor waits for resume; say so, so a watcher stops waiting.
    static string Driver(string run, IEnumerable<string> states) => SupervisorLock.IsHeld(run) ? " (supervised now)"
        : !states.Any() || states.Any(x => x is not ("done" or "failed" or "cancelled")) ? $" (no supervisor: `resume {run}` settles it)" : "";

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
                Console.WriteLine($"{ledger.RunId,-40} {summary}{Driver(ledger.RunId, states.Values)}");
            }

            return 0;
        }

        return PrintStatus(StatusRun(a));
    }

    static Ledger StatusRun(Args a)
    {
        var run = new Ledger(RunId(a));
        if (File.Exists(Path.Combine(run.Dir, "run.json"))) Locations.ControlDir(run.RunId);
        else run.EnsureExists();
        return run;
    }

    public static async Task<int> WaitAsync(Args a, CancellationToken ct, TimeProvider? timeProvider = null)
    {
        a.ValidatePaths();
        var run = StatusRun(a);
        var time = timeProvider ?? TimeProvider.System;
        var deadline = time.GetUtcNow().AddMinutes(int.Parse(a.Get("timeout") ?? "60", CultureInfo.InvariantCulture));
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var states = run.TaskStates();
            var unfinished = states.Where(x => x.Value is not ("done" or "failed" or "cancelled")).Select(x => x.Key).ToList();
            if (states.Count > 0 && unfinished.Count == 0 || !SupervisorLock.IsHeld(run.RunId))
            {
                PrintStatus(run);
                return states.Count > 0 && states.Values.All(x => x == "done") ? 0 : 1;
            }
            var remaining = deadline - time.GetUtcNow();
            if (remaining <= TimeSpan.Zero)
            {
                Console.Error.WriteLine($"run {run.RunId}: wait timed out; unfinished tasks: {string.Join(", ", unfinished)}");
                return 3;
            }
            await Task.Delay(remaining < TimeSpan.FromSeconds(5) ? remaining : TimeSpan.FromSeconds(5), time, ct);
        }
    }

    static int PrintStatus(Ledger run)
    {
        Console.WriteLine($"run {run.RunId}{Driver(run.RunId, run.TaskStates().Values)}");
        Console.WriteLine($"  {"task",-24} {"state",-10} {"outcome",-12} {"cost",8} {"in/out tokens",-16} {"files",5} {"scope!",6} {"asks",4} {"esc",3} verify");
        var events = run.Read();
        var toolCalls = events.Where(x => x.Type == "tool" && x.Task is not null).ToLookup(x => x.Task!);
        // An escalated ask that closed unanswered declined the worker's call: a coordinator who did not
        // poll `asks` learns it here.
        var unanswered = events.Where(x => x.Type == "escalation-dropped" && x.Task is not null).ToLookup(x => x.Task!);
        foreach (var (task, state) in run.TaskStates())
        {
            var result = run.LoadResult(task);
            var tokens = result?.Tokens is { } t ? $"{t.Input:0}/{t.Output:0}" : "-";
            var verify = result is null || result.Verify.Count == 0
                ? "-"
                : string.Join(' ', result.Verify.Select(v => v.NotRun is not null ? "not run" : v.ExitCode == 0 ? "ok" : $"exit {v.ExitCode}"));
            Console.WriteLine($"  {task,-24} {state,-10} {result?.Outcome ?? "-",-12} {result?.Cost?.ToString("0.0000", CultureInfo.InvariantCulture) ?? "-",8} {tokens,-16} {result?.FilesChanged.Count.ToString(CultureInfo.InvariantCulture) ?? "-",5} {result?.OutOfScope.Count.ToString(CultureInfo.InvariantCulture) ?? "-",6} {result?.Asks.ToString(CultureInfo.InvariantCulture) ?? "-",4} {result?.AsksEscalated.ToString(CultureInfo.InvariantCulture) ?? "-",3} {verify}");
            if (unanswered[task].ToList() is { Count: > 0 } dropped)
                Console.WriteLine($"    asks closed without an answer: {dropped.Count} ({string.Join("; ", dropped.Select(x => $"{x.Data?["action"]}: {x.Data?["reason"]}").Distinct())})");
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
        var ledger = new Ledger(RunId(a));
        var control = Locations.RequireControlFolder(ledger.RunId);
        using var supervisorLock = SupervisorLock.AcquireFolder(control);
        Locations.RequireControlFolder(ledger.RunId, setup: true);
        var info = ledger.LoadRunInfo();
        var proceed = a.Has("continue");
        var frozen = ConfigLayers.Frozen(ledger.Dir);
        foreach (var (task, config) in frozen.Tasks)
            if (config.InputSha256 is not null || config.ResultSchemaSha256 is not null)
                StructuredOutput.Load(ledger, task);
        Cli.RestrictEnvironment(frozen.Restrictions?.Scrub);
        var current = proceed ? await ConfigLayers.LoadAsync(info.Repo, new TaskFile(), null, a, ct) : null;
        current?.PrintWarnings();
        var allow = current?.Allow ?? AllowList.From(frozen.Restrictions?.Routes);
        var policy = Policy.From(frozen);
        Console.WriteLine(policy.Describe());
        AskQueue.Reset(ledger.RunId);
        try
        {
            await using var scope = new BackendScope(frozen.Tasks.Values.Select(t => t.Settings.Backend?.Value ?? "opencode"), control, runId: ledger.RunId);
            var states = ledger.TaskStates();
            var runs = new List<TaskRun>();
            foreach (var (task, state) in states)
            {
                if (state is "done" or "failed" or "cancelled")
                {
                    continue;
                }

                var record = ledger.LoadRecord(task);
                if (record.Spec.Kind == "structured" || frozen.Tasks[task].InputSha256 is not null)
                    record.Spec = ledger.FrozenSpec(task);
                record.Settings = frozen.Tasks[task].Settings;
                ConfigLayers.Apply(record.Spec, record.Settings);
                var run = TaskRun.FromLedger(record, ledger);
                if (record.PrepareRunning == true)
                {
                    ledger.Write(task, "queued", new JsonObject { ["kind"] = record.Spec.Kind });
                    ledger.Write(task, "error", new JsonObject { ["message"] = $"{task}: prepare was interrupted by a supervisor exit; a command may still run on this host. Check for it, then clean the task and start it under a new run id." });
                    continue;
                }
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
                return states.Values.All(x => x == "done") ? 0 : 1;
            }

            ledger.Write(null, "resume", new JsonObject { ["continue"] = proceed, ["tasks"] = runs.Count });
            var supervisor = new Supervisor(scope.Backends, ledger, info.Repo!, allow, policy, info.MaxParallel, ct);
            var exit = await supervisor.ResumeAsync(runs, proceed);
            return exit == 0 && ledger.TaskStates().Values.All(x => x == "done") ? 0 : 1;
        }
        catch (Exception ex)
        {
            return Aborted(ledger, ex);
        }
    }
}
