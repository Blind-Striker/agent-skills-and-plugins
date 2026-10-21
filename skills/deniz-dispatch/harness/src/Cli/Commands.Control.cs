static partial class Commands
{
    public static int Cancel(Args a)
    {
        var task = a.Require(0, "task");
        var ledger = Ledger.FindRunFor(task, a.Get("run"));
        if (SupervisorLock.IsHeld(ledger.RunId))
        {
            var control = Locations.ControlDir(ledger.RunId);
            Directory.CreateDirectory(control);
            File.WriteAllText(Path.Combine(control, $"{task}.cancel"), Json.Timestamp());
            Console.WriteLine($"cancel requested for {ledger.RunId}/{task}; the supervisor interrupts it within seconds");
            return 0;
        }

        // Without a supervisor nothing runs: the private server and its sessions ended with it.
        var state = ledger.TaskStates().GetValueOrDefault(task);
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
        var task = a.Require(0, "task");
        var ledger = Ledger.FindRunFor(task, a.Get("run"));
        if (SupervisorLock.IsHeld(ledger.RunId))
        {
            throw new HarnessException($"run {ledger.RunId} is supervised now; wait for it or cancel the task first");
        }

        var record = ledger.LoadRecord(task);
        if (record.Session.Id is null || record.Workspace.Path is null)
        {
            throw new HarnessException($"{task} has no session or worktree to collect");
        }

        await using var host = await HarnessServer.StartAsync(subscribe: false, ct);
        var run = TaskRun.FromLedger(record, ledger);
        var result = await Settlement.SettleAsync(host.Client, ledger, run, outcomeOverride: null, ct);
        ledger.Write(task, "final", new JsonObject { ["state"] = result.State, ["outcome"] = result.Outcome, ["via"] = "collect" });
        return 0;
    }

    public static async Task<int> CleanAsync(Args a, CancellationToken ct)
    {
        var ledger = new Ledger(a.Require(0, "run"));
        ledger.EnsureExists();
        if (SupervisorLock.IsHeld(ledger.RunId))
        {
            throw new HarnessException($"run {ledger.RunId} is supervised now");
        }

        var info = ledger.LoadRunInfo();
        var force = a.Has("force");
        var refused = 0;
        foreach (var task in ledger.TaskStates().Keys)
        {
            var record = ledger.LoadRecord(task);
            if (record.Workspace.Path is null || record.Workspace.Branch is null)
            {
                continue;
            }

            var exists = Directory.Exists(record.Workspace.Path);
            var dirty = exists && (await Git.RunAsync(record.Workspace.Path, ct, "status", "--porcelain")).Output.Trim().Length > 0;
            var merged = record.Workspace.BaseRef is not null
                && (await Git.RunAsync(info.Repo!, ct, "merge-base", "--is-ancestor", record.Workspace.Branch, record.Workspace.BaseRef)).ExitCode == 0;
            if (!force && (dirty || !merged))
            {
                Console.WriteLine($"{task}: refused ({(dirty ? "dirty worktree" : "")}{(dirty && !merged ? ", " : "")}{(!merged ? $"{record.Workspace.Branch} is not merged into {record.Workspace.BaseRef}" : "")}); --force removes it anyway");
                refused++;
                continue;
            }

            if (exists)
            {
                var remove = force
                    ? await Git.RunAsync(info.Repo!, ct, "worktree", "remove", "--force", record.Workspace.Path)
                    : await Git.RunAsync(info.Repo!, ct, "worktree", "remove", record.Workspace.Path);
                if (remove.ExitCode != 0)
                {
                    Console.WriteLine($"{task}: worktree remove failed: {remove.Output.Trim()}");
                    refused++;
                    continue;
                }
            }

            var branch = await Git.RunAsync(info.Repo!, ct, "branch", force ? "-D" : "-d", record.Workspace.Branch);
            var temp = Locations.TaskTemp(task);
            if (Directory.Exists(temp))
            {
                Directory.Delete(temp, recursive: true);
            }

            ledger.Write(task, "cleaned", new JsonObject { ["worktree"] = record.Workspace.Path, ["branch"] = record.Workspace.Branch, ["branchDeleted"] = branch.ExitCode == 0, ["force"] = force });
        }

        if (refused == 0 && Directory.Exists(Locations.ControlDir(ledger.RunId)))
        {
            Directory.Delete(Locations.ControlDir(ledger.RunId), recursive: true);
        }

        return refused == 0 ? 0 : 1;
    }

    // Escalated asks that wait for the operator, for one run or for every run with a control folder.
    public static int Asks(Args a)
    {
        List<string> runs = a.Positional.Count > 0
            ? [a.Positional[0]]
            : Directory.Exists(Locations.ControlRoot)
                ? Directory.GetDirectories(Locations.ControlRoot).Select(Path.GetFileName).OfType<string>().Order(StringComparer.Ordinal).ToList()
                : [];
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
        ledger.EnsureExists();
        if (AskQueue.List(run).All(x => x.AskId != askId))
        {
            throw new HarnessException($"no escalated ask {askId} waits in run {run}; `asks {run}` lists them");
        }

        if (!SupervisorLock.IsHeld(run))
        {
            throw new HarnessException($"run {run} has no supervisor: its server ended, and the ask with it. `resume {run} --continue` re-prompts the task");
        }

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
            if (AskQueue.List(run).All(x => x.AskId != askId))
            {
                Console.WriteLine($"{askId}: answered {decision}; the ledger records it");
                return 0;
            }
        }

        Console.WriteLine($"{askId}: decision {decision} is queued; the supervisor has not picked it up yet");
        return 0;
    }
}
