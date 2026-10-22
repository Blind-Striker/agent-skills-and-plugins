static partial class Commands
{
    public static int Cancel(Args a)
    {
        var ledger = new Ledger(a.Require(0, "run"));
        var task = a.Require(1, "task");
        var control = Locations.RequireControlFolder(ledger.RunId);
        var states = ledger.TaskStates();
        if (!states.TryGetValue(task, out var state)) throw new HarnessException($"no task {task} in run {ledger.RunId}");
        if (state is "done" or "failed" or "cancelled") return 0;
        using var supervisorLock = SupervisorLock.TryAcquireFolder(control);
        if (supervisorLock is null)
        {
            var time = Json.Timestamp();
            Json.WriteFile(Path.Combine(control, "cancel", $"{task}.json"),
                new CancelRequest { Run = ledger.RunId, Task = task, Time = time }, ControlJson.Files.CancelRequest);
            Console.WriteLine($"cancel requested for {ledger.RunId}/{task}; the supervisor interrupts it within seconds");
            var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
            while (DateTimeOffset.UtcNow < deadline)
            {
                if (ledger.Read().Any(x => x.Task == task && x.Type == "cancel" && x.Data?["time"]?.GetValue<string>() == time)) return 0;
                Thread.Sleep(100);
            }
            return 1;
        }

        // Without a supervisor nothing runs: the private server and its sessions ended with it.
        state = ledger.TaskStates().GetValueOrDefault(task);
        if (state is "done" or "failed" or "cancelled")
        {
            Console.WriteLine($"{task} is already {state}");
            return 0;
        }

        ledger.Write(task, "final", new JsonObject { ["state"] = "cancelled", ["reason"] = "cancelled while no supervisor ran" });
        Console.WriteLine($"{ledger.RunId}/{task} marked cancelled; resume skips it");
        return 0;
    }

    public static async Task<int> CollectAsync(Args a, CancellationToken ct)
    {
        var ledger = new Ledger(a.Require(0, "run"));
        var task = a.Require(1, "task");
        var control = Locations.RequireControlFolder(ledger.RunId);
        using var supervisorLock = SupervisorLock.AcquireFolder(control);
        Locations.RequireControlFolder(ledger.RunId, setup: true);

        var record = ledger.LoadRecord(task);
        var frozen = ConfigLayers.Frozen(ledger.Dir);
        if (record.Spec.Kind == "structured" || frozen.Tasks[task].InputSha256 is not null)
        {
            record.Spec = ledger.FrozenSpec(task);
            StructuredOutput.Load(ledger, task);
        }
        Cli.RestrictEnvironment(frozen.Restrictions?.Scrub);
        record.Settings = frozen.Tasks[task].Settings;
        ConfigLayers.Apply(record.Spec, record.Settings);
        if (record.Session.Id is null || record.Workspace.Path is null)
        {
            throw new HarnessException($"{task} has no session or worktree to collect");
        }

        try
        {
            var name = record.Settings.Backend?.Value ?? "opencode";
            await using var scope = new BackendScope([name], control, subscribe: false, runId: ledger.RunId);
            var backend = scope.Backends[name];
            var run = TaskRun.FromLedger(record, ledger);
            var result = await Settlement.SettleAsync(backend, ledger, run, outcomeOverride: null, ct);
            ledger.Write(task, "final", new JsonObject { ["state"] = result.State, ["outcome"] = result.Outcome, ["via"] = "collect" });
            return result.State == "done" ? 0 : 1;
        }
        catch (OpenCodeBackend.SessionReleasedException) when (ledger.TaskStates().GetValueOrDefault(task) == "done") { throw; }
        catch (Exception ex) { return Aborted(ledger, ex); }
    }

    public static async Task<int> CleanAsync(Args a, CancellationToken ct)
    {
        var ledger = new Ledger(a.Require(0, "run"));
        var control = Locations.ControlDir(ledger.RunId);
        using var supervisorLock = Directory.Exists(control) ? SupervisorLock.AcquireFolder(control) : null;
        var info = ledger.LoadRunInfo();
        var force = a.Has("force");
        var states = ledger.TaskStates();
        var records = states.Keys.Where(task => File.Exists(Path.Combine(ledger.Dir, task, "task.json")))
            .Select(ledger.LoadRecord).ToList();
        var refused = await CleanSessionsAsync(ledger, records, states, control, ct);
        foreach (var record in records)
        {
            var task = record.Spec.Id!;
            try
            {
                var git = Directory.Exists(control) ? Worktree.Runner(ledger, task) : Git.ForTask(task, ledger.RunId);
                if (!await Worktree.CleanAsync(ledger, record, info.Repo!, force, ct, git)) refused++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or HarnessException)
            {
                refused++;
                ledger.Write(task, "cleaned", new JsonObject { ["error"] = ex.Message, ["force"] = force });
            }
        }

        if (refused == 0 && Directory.Exists(control))
        {
            try
            {
                foreach (var entry in Directory.GetFileSystemEntries(control))
                {
                    if (Path.GetFileName(entry) is "supervisor.lock" or "run.ref") continue;
                    RemoveControlEntry(entry);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine($"control cleanup failed: {ex.Message}");
                refused++;
            }
        }

        return refused == 0 ? 0 : 1;
    }

    static async Task<int> CleanSessionsAsync(Ledger ledger, IReadOnlyList<TaskRecord> records,
        IReadOnlyDictionary<string, string> states, string control, CancellationToken ct)
    {
        var tasks = records.Where(x => (x.Settings.Backend?.Value ?? "opencode") == "opencode"
            && (x.Session.Id is not null || x.Workspace.Path is not null)).ToList();
        if (tasks.Count == 0) return 0;
        var refused = 0;
        await using var scope = new BackendScope(["opencode"], Directory.Exists(control) ? control : null, subscribe: false, runId: ledger.RunId);
        var backend = scope.Backends["opencode"];
        var recorded = tasks.Where(x => x.Session.Id is not null).Select(x => TaskRun.FromLedger(x, ledger).SavedWorker()).ToList();
        var releases = recorded.ToDictionary(x => x.Id, StringComparer.Ordinal);
        try
        {
            var known = recorded.Where(x => states[x.TaskId] is not ("done" or "failed" or "cancelled")).ToList();
            var directories = tasks.Select(x => x.Workspace.Path).OfType<string>().ToList();
            foreach (var orphan in await backend.FindOrphansAsync(new(ledger.RunId, known, directories), ct))
                releases.TryAdd(orphan.Id, orphan);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not LedgerWriteException)
        {
            refused++;
            ledger.Write(null, "error", new JsonObject { ["while"] = "find orphan sessions", ["message"] = ex.Message });
            Console.Error.WriteLine($"orphan session cleanup failed: {ex.Message}");
        }
        foreach (var worker in releases.Values)
        {
            try { await backend.ReleaseAsync(worker, ct); }
            catch (Exception ex) when (ex is not OperationCanceledException and not LedgerWriteException)
            {
                refused++;
                ledger.Write(worker.TaskId, "error", new JsonObject { ["while"] = "release session", ["message"] = ex.Message });
                Console.Error.WriteLine($"{worker.TaskId}: session cleanup failed: {ex.Message}");
            }
        }
        return refused;
    }

    static void RemoveControlEntry(string path)
    {
        if (!Path.Exists(path)) return;
        var attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.Directory) != 0 && (attributes & FileAttributes.ReparsePoint) == 0)
            foreach (var child in Directory.GetFileSystemEntries(path)) RemoveControlEntry(child);
        File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
        if ((attributes & FileAttributes.Directory) != 0) Directory.Delete(path);
        else File.Delete(path);
    }

    // Escalated asks that wait for the operator, for one run or for every run with a control folder.
    public static int Asks(Args a)
    {
        if (a.Positional.Count == 0) return ListControlFolders();
        Locations.RequireControlFolder(a.Require(0, "run"));
        List<string> runs = [a.Positional[0]];
        var shown = 0;
        foreach (var run in runs)
        {
            var pending = AskQueue.List(run);
            if (pending.Count == 0)
            {
                continue;
            }

            var supervised = SupervisorLock.IsHeld(run);
            Console.WriteLine($"run {run}{(supervised ? "" : " (no supervisor: these asks ended with its server)")}");
            foreach (var ask in pending)
            {
                var age = DateTimeOffset.TryParse(ask.Time, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at)
                    ? $"{(DateTimeOffset.UtcNow - at).TotalMinutes:0.0} min"
                    : "?";
                Console.WriteLine($"  {ask.AskId}  task {ask.Task}  {ask.Action} {string.Join(", ", ask.Resources)}  (rule {ask.Rule}, waiting {age})");
                shown++;
            }
        }

        Console.WriteLine(shown == 0
            ? "no escalated asks are waiting"
            : "answer with: dispatch reply <run> <askId> once|reject [--message <text>]");
        return 0;
    }

    static int ListControlFolders()
    {
        if (!Directory.Exists(Locations.ControlRoot)) return 0;
        foreach (var folder in Directory.GetDirectories(Locations.ControlRoot).Order(StringComparer.Ordinal))
        {
            if (!Locations.IsControlId(Path.GetFileName(folder))) continue;
            try
            {
                var reference = JsonSerializer.Deserialize(File.ReadAllText(Path.Combine(folder, "run.ref")), ControlJson.Files.RunReference)
                    ?? throw new JsonException("expected run.ref");
                Console.WriteLine($"control {Path.GetFileName(folder)}: run {reference.Run}, ledger {reference.LedgerRoot}");
            }
            catch (Exception ex) when (ex is IOException or JsonException)
            {
                Console.WriteLine($"control {Path.GetFileName(folder)}: run.ref missing or invalid");
            }
            foreach (var ask in AskQueue.ListFolder(folder))
                Console.WriteLine($"  {ask.AskId}  task {ask.Task}  {ask.Action} {string.Join(", ", ask.Resources)}");
        }
        return 0;
    }

    // The operator's answer to one escalated ask. The running supervisor picks the decision file up.
    public static async Task<int> ReplyAsync(Args a, CancellationToken ct)
    {
        var run = a.Require(0, "run");
        var askId = a.Require(1, "ask id");
        var decision = a.Require(2, "decision (once or reject)");
        if (decision is not ("once" or "reject"))
        {
            throw new HarnessException("the decision is once or reject; the harness never replies always");
        }

        var message = a.Get("message");
        if (message is not null && decision != "reject")
        {
            throw new HarnessException("--message goes with reject: it is the feedback the model sees");
        }

        var ledger = new Ledger(run);
        Locations.RequireControlFolder(run);
        if (AskQueue.List(run).All(x => x.AskId != askId))
        {
            throw new HarnessException($"no escalated ask {askId} waits in run {run}; `asks {run}` lists them");
        }

        if (!SupervisorLock.IsHeld(run))
        {
            throw new HarnessException($"run {run} has no supervisor: its server ended, and the ask with it. `resume {run} --continue` re-prompts the task");
        }

        var previousEvents = ledger.Read().Count;
        AskQueue.WriteDecision(run, new AskDecision
        {
            AskId = askId,
            Decision = decision,
            Message = message,
            Time = Json.Timestamp(),
        });

        // The supervisor reads decisions every few seconds; wait briefly to confirm.
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        while (DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromSeconds(1), ct);
            var events = ledger.Read().Skip(previousEvents).ToList();
            if (events.Any(x => x.Type == "error" && x.Data?["while"]?.GetValue<string>() == "decision" && x.Data?["id"]?.GetValue<string>() == askId))
            {
                Console.WriteLine($"{askId}: decision {decision} was attempted but not confirmed; the ledger records the error");
                return 1;
            }
            if (events.Any(x => x.Type == "permission" && x.Data?["id"]?.GetValue<string>() == askId
                && x.Data?["by"]?.GetValue<string>() == "operator" && x.Data?["answer"]?.GetValue<string>() == decision))
            {
                Console.WriteLine($"{askId}: answered {decision}; the ledger records it");
                return 0;
            }
        }

        Console.WriteLine($"{askId}: decision {decision} is queued; the supervisor has not picked it up yet");
        return 1;
    }
}
