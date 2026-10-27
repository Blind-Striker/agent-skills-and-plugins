static partial class Commands
{
    public static int Cancel(Args a)
    {
        var ledger = new Ledger(RunId(a));
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

        // Private backend sessions ended with the supervisor; a prepare command may have survived.
        state = ledger.TaskStates().GetValueOrDefault(task);
        if (state is "done" or "failed" or "cancelled")
        {
            Console.WriteLine($"{task} is already {state}");
            return 0;
        }

        var reason = "cancelled while no supervisor ran";
        // A task with no record never began prepare; cancel settles it as before.
        var record = File.Exists(Path.Combine(ledger.TaskDir(task), "task.json")) ? ledger.LoadRecord(task) : null;
        if (record?.PrepareRunning == true)
        {
            reason = "prepare-interrupted: a prepare command may still run on this host";
            ledger.SaveRecord(record);
            using (File.Open(Path.Combine(ledger.TaskDir(task), "prepare.log"), FileMode.Append)) { }
            ledger.Write(task, "prepared", new JsonObject { ["commands"] = record.Prepare?.Count ?? 0, ["failed"] = 0, ["cancelled"] = true });
            ledger.SaveResult(new TaskResult { Run = ledger.RunId, Task = task, Kind = record.Spec.Kind,
                Backend = record.Settings.Backend?.Value, State = "cancelled", Outcome = "cancelled",
                CancelRequested = true, Prepare = record.Prepare, Warnings = [reason] });
        }
        ledger.Write(task, "final", new JsonObject { ["state"] = "cancelled", ["reason"] = reason });
        Console.WriteLine($"{ledger.RunId}/{task} marked cancelled; resume skips it");
        return 0;
    }

    public static async Task<int> CollectAsync(Args a, CancellationToken ct)
    {
        var ledger = new Ledger(RunId(a));
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

        BackendScope? scope = null;
        var runs = new List<TaskRun>();
        try
        {
            await CleanupPreservingAsync(async () =>
            {
                var name = record.Settings.Backend?.Value ?? "opencode";
                await using var ownedScope = scope = new BackendScope([name], control, subscribe: false, runId: ledger.RunId);
                var backend = scope.Backends[name];
                var run = TaskRun.FromLedger(record, ledger);
                runs.Add(run);
                var result = await Settlement.SettleAsync(backend, ledger, run, outcomeOverride: null, ct);
                ledger.Write(task, "final", new JsonObject { ["state"] = result.State, ["outcome"] = result.Outcome, ["via"] = "collect" });
                return result.State == "done" ? 0 : 1;
            }, () => scope is null ? Task.CompletedTask : SettleCleanupAsync(ledger, runs, scope.Backends, scope.CleanupResults));
            return ledger.LoadResult(task)?.State == "done" ? 0 : 1;
        }
        catch (OpenCodeBackend.SessionReleasedException) when (ledger.TaskStates().GetValueOrDefault(task) == "done") { throw; }
        catch (Exception ex) { return Aborted(ledger, ex); }
    }

    public static async Task<int> CleanAsync(Args a, CancellationToken ct)
    {
        var ledger = new Ledger(RunId(a));
        var control = Locations.ControlDir(ledger.RunId);
        using var supervisorLock = Directory.Exists(control) ? SupervisorLock.AcquireFolder(control) : null;
        var info = ledger.LoadRunInfo();
        var force = a.Has("force");
        var states = ledger.TaskStates();
        var records = states.Keys.Where(task => File.Exists(Path.Combine(ledger.Dir, task, "task.json")))
            .Select(ledger.LoadRecord).ToList();
        var sessionFailures = await CleanSessionsAsync(ledger, records, states, control, ct);
        var refused = sessionFailures.Keys.Count(task => records.All(record => record.Spec.Id != task));
        foreach (var record in records)
        {
            var task = record.Spec.Id!;
            var step = "session";
            try
            {
                // A session that could not be released keeps the task from counting as cleaned,
                // but workspace cleanup is still attempted, subject to zombie retention.
                if (sessionFailures.TryGetValue(task, out var error))
                {
                    refused++;
                    ledger.Write(task, "clean-failed", new JsonObject { ["step"] = step, ["error"] = error, ["force"] = force });
                    Console.Error.WriteLine($"{task}: {step}: {error}");
                }
                step = "workspace";
                var git = Directory.Exists(control) ? Worktree.Runner(ledger, task) : Git.ForTask(task, ledger.RunId);
                if (!await Worktree.CleanAsync(ledger, record, info.Repo!, force, ct, git)) throw new HarnessException("workspace cleanup refused");
                if (error is null)
                    ledger.Write(task, "cleaned", new JsonObject { ["worktree"] = record.Workspace.Path, ["branch"] = record.Workspace.Branch, ["force"] = force });
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or HarnessException)
            {
                refused++;
                step = ex.Data["step"] as string ?? step;
                ledger.Write(task, "clean-failed", new JsonObject { ["step"] = step, ["error"] = ex.Message, ["force"] = force });
                Console.Error.WriteLine($"{task}: {step}: {ex.Message}");
            }
        }

        try
        {
            if (refused == 0)
                foreach (var path in Locations.CleanupCandidates(ledger, records))
                {
                    if (!Path.Exists(path)) continue;
                    if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0 || Directory.EnumerateFileSystemEntries(path).Any())
                        Console.WriteLine($"retained: {path}");
                    else Directory.Delete(path);
                }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"container cleanup failed: {ex.Message}");
            refused++;
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

    static async Task<Dictionary<string, string>> CleanSessionsAsync(Ledger ledger, IReadOnlyList<TaskRecord> records,
        IReadOnlyDictionary<string, string> states, string control, CancellationToken ct)
    {
        var failures = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var record in records.Where(record => record.Settings.Backend?.Value == "codex" && record.Session.Id is not null))
        {
            try
            {
                await using var scope = new BackendScope(["codex"], Directory.Exists(control) ? control : null, subscribe: false, runId: ledger.RunId);
                var run = TaskRun.FromLedger(record, ledger);
                var stop = await scope.Backends["codex"].ReleaseAsync(run.SavedWorker(), ct);
                Settlement.RecordStop(ledger, run, stop);
                if (stop is { Stopped: false })
                {
                    failures[run.Id] = $"Codex stop unconfirmed: {stop.Detail}";
                    Console.WriteLine($"{run.Id}: retained Codex home: {record.BackendState.CodexHome}");
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException and not LedgerWriteException)
            { failures[record.Spec.Id!] = ex.Message; }
        }
        var tasks = records.Where(x => (x.Settings.Backend?.Value ?? "opencode") == "opencode"
            // Code tasks record worktree progress before creating a session; structured tasks do not.
            && (x.Session.Id is not null || x.Workspace.Path is not null
                && (states[x.Spec.Id!] != "queued" || x.Spec.Kind == "structured"))).ToList();
        if (tasks.Count == 0) return failures;
        try
        {
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
                foreach (var task in tasks) failures[task.Spec.Id!] = ex.Message;
                ledger.Write(null, "error", new JsonObject { ["while"] = "find orphan sessions", ["message"] = ex.Message });
                Console.Error.WriteLine($"orphan session cleanup failed: {ex.Message}");
            }
            foreach (var worker in releases.Values)
            {
                try { await backend.ReleaseAsync(worker, ct); }
                catch (Exception ex) when (ex is not OperationCanceledException and not LedgerWriteException)
                {
                    failures[worker.TaskId] = ex.Message;
                    ledger.Write(worker.TaskId, "error", new JsonObject { ["while"] = "release session", ["message"] = ex.Message });
                    Console.Error.WriteLine($"{worker.TaskId}: session cleanup failed: {ex.Message}");
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not LedgerWriteException)
        {
            foreach (var task in tasks) failures[task.Spec.Id!] = ex.Message;
        }
        return failures;
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
        List<string> runs = [RunId(a)];
        Locations.RequireControlFolder(runs[0]);
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
        var run = RunId(a);
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
            if (events.FirstOrDefault(x => x.Type == "error" && x.Data?["while"]?.GetValue<string>() == "decision" && x.Data?["id"]?.GetValue<string>() == askId) is { } failure)
            {
                Console.WriteLine($"{askId}: decision {decision} was attempted but not confirmed: {failure.Data?["message"]?.GetValue<string>()}");
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
