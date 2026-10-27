using System.Text.Json.Nodes;
using Dispatch.Tests.Support;

namespace Dispatch.Tests.Integration;

[Category("Integration")]
[NotInParallel]
public sealed class ControlCommandTests
{
    const string Run = "recorded";
    const string ControlId = "abcdefghijklmnopqrstuvwxyz";

    [Test]
    [Arguments("collect", false)]
    [Arguments("resume", false)]
    [Arguments("clean", false)]
    [Arguments("clean", true)]
    [Timeout(60_000)]
    public async Task Codex_zombie_commands_keep_owned_work_and_home_until_stop_is_confirmed(string command, bool force, CancellationToken ct)
    {
        using var core = new CoreLab("zombie-commands");
        using var codex = new CodexLab(core.Tree);
        ConfigLayers.Initialize(Args.Parse(["status", "--ledger", Path.GetDirectoryName(core.Ledger.Dir)!]));
        var run = await core.TaskAsync("inspect", ct);
        run.Record.Settings.Backend = new() { Value = "codex" };
        run.Record.Workspace = new();
        run.Record.Spec.Base = "main";
        await Worktree.PrepareAsync(run, core.Ledger, core.Tree.Repository, ct, core.Git);
        var control = Locations.ControlDir(core.Ledger.RunId);
        File.WriteAllText(Path.Combine(control, "setup.done"), "");
        var info = core.Ledger.LoadRunInfo();
        info.Repo = core.Tree.Repository;
        Json.WriteFile(Path.Combine(core.Ledger.Dir, "run.json"), info, LedgerJson.Files.RunInfo);
        Json.WriteFile(Path.Combine(core.Ledger.Dir, "config.json"), new EffectiveConfig
        { Tasks = new() { [run.Id] = new() { Settings = run.Record.Settings } } }, LedgerJson.Files.EffectiveConfig);
        // A present PID without its original identity cannot authorize a kill, even of this test host.
        using var home = RecordedHome(core, codex, run, Environment.ProcessId);
        core.Ledger.Write(run.Id, "stop-unconfirmed", new() { ["detail"] = "failed creation stop uncertain" });
        if (command == "clean")
        {
            core.Ledger.SaveResult(new() { Run = core.Ledger.RunId, Task = run.Id, State = "failed", WorkerStopped = false, Failures = ["zombie"] });
            core.Ledger.Write(run.Id, "final", new() { ["state"] = "failed" });
        }
        var output = TestContext.Current!.Output;
        var start = output.GetStandardOutput().Length;
        var args = Args.Parse(force ? [command, core.Ledger.RunId, "--force"] : [command, core.Ledger.RunId, run.Id]);
        var exit = command == "collect" ? await Commands.CollectAsync(args, ct)
            : command == "resume" ? await Commands.ResumeAsync(args, ct) : await Commands.CleanAsync(args, ct);
        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(Directory.Exists(home.Home)).IsTrue();
        await Assert.That(Directory.Exists(run.Record.Workspace.Path)).IsEqualTo(!force);
        await Assert.That(core.Ledger.Read().Where(line => line.Type == "aborted")).IsEmpty();
        if (command == "clean")
            await Assert.That(output.GetStandardOutput()[start..]).Contains($"{run.Id}: retained Codex home: {home.Home}");
        else
        {
            var result = core.Ledger.LoadResult(run.Id)!;
            await Assert.That(result.State).IsEqualTo("failed");
            await Assert.That(result.WorkerStopped).IsFalse();
            await Assert.That(result.Failures).IsEquivalentTo(new[] { "zombie", "outcome:lost", "settle-error", "no-final-text" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        }
        // A later recorded check needs no turn, and can reclaim the home without a resume or retry run.
        run.Worker!.State["pid"] = int.MaxValue;
        run.SaveWorker(run.Worker, core.Ledger);
        await Assert.That(await Commands.CleanAsync(Args.Parse(["clean", core.Ledger.RunId]), ct)).IsEqualTo(0);
        await Assert.That(Directory.Exists(home.Home)).IsFalse();
        await Assert.That(Directory.Exists(run.Record.Workspace.Path)).IsFalse();
        await Assert.That(TaskRun.FromLedger(core.Ledger.LoadRecord(run.Id), core.Ledger).Zombie).IsFalse();
    }

    [Test]
    [Timeout(60_000)]
    public async Task Collect_rechecks_a_turnless_failed_start_and_deletes_its_confirmed_home(CancellationToken ct)
    {
        using var core = new CoreLab("turnless-collect");
        using var codex = new CodexLab(core.Tree);
        ConfigLayers.Initialize(Args.Parse(["status", "--ledger", Path.GetDirectoryName(core.Ledger.Dir)!]));
        var run = await core.TaskAsync("inspect", ct);
        run.Record.Settings.Backend = new() { Value = "codex" };
        using var home = RecordedHome(core, codex, run, int.MaxValue);
        core.Ledger.Write(run.Id, "stop-unconfirmed", new() { ["detail"] = "failed start" });
        var control = Locations.ControlDir(core.Ledger.RunId);
        File.WriteAllText(Path.Combine(control, "setup.done"), "");
        Json.WriteFile(Path.Combine(core.Ledger.Dir, "config.json"), new EffectiveConfig
        { Tasks = new() { [run.Id] = new() { Settings = run.Record.Settings } } }, LedgerJson.Files.EffectiveConfig);
        await Assert.That(await Commands.CollectAsync(Args.Parse(["collect", core.Ledger.RunId, run.Id]), ct)).IsEqualTo(1);
        var result = core.Ledger.LoadResult(run.Id)!;
        await Assert.That(result.Failures).IsEquivalentTo(new[] { "outcome:lost", "settle-error", "no-final-text" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(result.WorkerStopped).IsTrue();
        await Assert.That(Directory.Exists(home.Home)).IsFalse();
        await Assert.That(core.Ledger.Read().Where(line => line.Type == "aborted")).IsEmpty();
    }

    static CodexHome RecordedHome(CoreLab core, CodexLab codex, TaskRun run, int pid)
    {
        File.WriteAllText(Path.Combine(codex.Environment["CODEX_HOME"], "auth.json"), "{}");
        var home = CodexHome.Create(new(Path.Combine(Locations.ControlDir(core.Ledger.RunId), "profiles"), CodexLab.Executable, new Dictionary<string, string>()),
            new(core.Ledger.RunId, run.Id, "research", new("codex", "model-y", "high"), null, null,
                new("clean", "on", "workspace", "on", "worktree"), run.Record.Workspace.Path!, new([], []), codex.Environment, null, "clean", null),
            codex.Environment["CODEX_HOME"]);
        run.SaveWorker(new("codex", run.Id, "partial", null, new()
        {
            ["pid"] = pid, ["codexHome"] = home.Home, ["codexHomeTemporary"] = true,
            ["codexHomeMarkerName"] = home.MarkerName, ["codexHomeMarkerText"] = home.MarkerText,
        }), core.Ledger);
        return home;
    }

    static TaskRecord CleanRecord(TempTree tree, string task = "inspect")
    {
        var record = new TaskRecord { Spec = new() { Id = task, Kind = "research" }, Settings = new() { Backend = new() { Value = "codex" } },
            Workspace = new() { Path = Path.Combine(tree.WorktreeRoot, Run, task), Branch = $"dispatch/{Run}/{task}", BaseRef = "main" } };
        var ledger = Path.Combine(tree.DispatchHome, "state", "ledgers", Run);
        Json.WriteFile(Path.Combine(ledger, task, "task.json"), record, LedgerJson.Files.TaskRecord);
        if (task != "inspect") File.AppendAllText(Path.Combine(ledger, "events.jsonl"), $"{{\"run\":\"{Run}\",\"task\":\"{task}\",\"type\":\"queued\"}}\n");
        return record;
    }

    [Test]
    [Arguments("registration")]
    [Arguments("branch")] // Branch-only and temp-only are guards.
    [Arguments("temp")]
    [Timeout(60_000)]
    public async Task Clean_ReconcilesMissingArtifacts(string artifact, CancellationToken ct)
    {
        using var tree = new TempTree();
        var repo = await LabRepo.CreateAsync(tree, ct);
        var control = Seed(tree);
        await repo.BaselineAsync(control, ct);
        var record = CleanRecord(tree);
        var path = record.Workspace.Path!;
        var branch = record.Workspace.Branch!;
        var foreign = Path.Combine(tree.WorktreeRoot, "foreign");
        await repo.GitAsync(["worktree", "add", "-b", "foreign", foreign, "main"], ct);
        if (artifact == "registration")
        {
            await repo.GitAsync(["worktree", "add", "-b", branch, path, "main"], ct);
            Directory.Delete(path, true);
        }
        if (artifact == "branch") await repo.GitAsync(["branch", branch], ct);
        var temp = Path.Combine(tree.WorktreeRoot, Run, ".tmp", "inspect");
        Directory.CreateDirectory(temp);
        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var child = new HarnessProcess(tree);
            (await child.RunAsync(["clean", Run], ct)).RequireSuccess("reconcile artifacts");
            await Assert.That(Directory.Exists(path)).IsFalse();
            await Assert.That(Directory.Exists(temp)).IsFalse();
            await Assert.That(await repo.GitAsync(["branch", "--list", branch], ct)).IsEqualTo("");
            await Assert.That(await repo.GitAsync(["worktree", "list", "--porcelain"], ct)).DoesNotContain(path.Replace('\\', '/'));
            await Assert.That(File.ReadAllText(Path.Combine(foreign, "README.md"))).IsEqualTo("# Lab repository\n");
            await Assert.That(await repo.GitAsync(["branch", "--list", "foreign"], ct)).Contains("foreign");
        }
    }

    [Test]
    [Arguments("workspace")]
    [Arguments("index")]
    [Arguments("locked-file")]
    [Timeout(60_000)]
    public async Task Clean_FailureNamesTaskAndStep(string obstacle, CancellationToken ct)
    {
        if (obstacle != "workspace" && !OperatingSystem.IsWindows()) Skip.Test("Locked-file deletion refusal is Windows-only");
        var step = obstacle == "index" ? "index" : "workspace";
        using var tree = new TempTree();
        var repo = await LabRepo.CreateAsync(tree, ct);
        var control = Seed(tree);
        await repo.BaselineAsync(control, ct);
        var record = CleanRecord(tree);
        CleanRecord(tree, "later");
        await repo.GitAsync(["worktree", "add", "-b", record.Workspace.Branch!, record.Workspace.Path!, "main"], ct);
        var index = Path.Combine(control, "index-inspect");
        using var held = obstacle == "workspace" ? null : new FileStream(obstacle == "index" ? index : Path.Combine(record.Workspace.Path!, "README.md"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);
        if (obstacle == "workspace") await repo.GitAsync(["worktree", "lock", record.Workspace.Path!], ct);
        using (var child = new HarnessProcess(tree))
        {
            var result = await child.RunAsync(["clean", Run, "--force"], ct);
            await Assert.That(result.ExitCode).IsEqualTo(1);
            await Assert.That(result.Stderr).Contains($"inspect: {step}:");
        }
        var eventsPath = Path.Combine(tree.DispatchHome, "state", "ledgers", Run, "events.jsonl");
        var events = File.ReadAllLines(eventsPath).Select(x => JsonNode.Parse(x)!).Where(x => x["type"]!.GetValue<string>().StartsWith("clean", StringComparison.Ordinal)).ToArray();
        await Assert.That(string.Join(",", events.Select(x => x["task"] + ":" + x["type"]))).IsEqualTo("inspect:clean-failed,later:cleaned");
        await Assert.That(events[0]["data"]!["step"]!.GetValue<string>()).IsEqualTo(step);
        await Assert.That(events[0]["data"]!["force"]!.GetValue<bool>()).IsTrue();
        await Assert.That(events[0]["data"]!["error"]!.GetValue<string>()).Contains(obstacle == "workspace" ? "locked" : obstacle == "index" ? "index-inspect" : "worktree remove");
        held?.Dispose();
        if (obstacle == "workspace") await repo.GitAsync(["worktree", "unlock", record.Workspace.Path!], ct);
        using var retry = new HarnessProcess(tree);
        (await retry.RunAsync(["clean", Run, "--force"], ct)).RequireSuccess("retry failed cleanup");
        await Assert.That(Directory.Exists(record.Workspace.Path)).IsFalse();
        await Assert.That(File.Exists(index)).IsFalse();
        await Assert.That(await repo.GitAsync(["branch", "--list", record.Workspace.Branch!], ct)).IsEqualTo("");
        await Assert.That(string.Join(",", File.ReadAllLines(eventsPath).Select(x => JsonNode.Parse(x)!)
            .Where(x => x["type"]!.GetValue<string>().StartsWith("clean", StringComparison.Ordinal)).Select(x => x["task"] + ":" + x["type"])))
            .IsEqualTo("inspect:clean-failed,later:cleaned,inspect:cleaned,later:cleaned");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    [Timeout(60_000)]
    public async Task Clean_PrunesOnlyEmptyContainers(bool remnants, CancellationToken ct)
    {
        using var tree = new TempTree();
        await LabRepo.CreateAsync(tree, ct);
        Seed(tree);
        var record = CleanRecord(tree);
        var ledger = Path.Combine(tree.DispatchHome, "state", "ledgers", Run);
        var frozen = Path.Combine(tree.Root, "frozen-worktrees");
        var config = JsonNode.Parse(File.ReadAllText(Path.Combine(ledger, "config.json")))!;
        config["locations"] = new JsonObject { ["worktreeRoot"] = frozen };
        File.WriteAllText(Path.Combine(ledger, "config.json"), config.ToJsonString());
        var run = Path.Combine(frozen, Run);
        Directory.CreateDirectory(Path.Combine(run, ".tmp", "inspect"));
        Directory.CreateDirectory(Path.Combine(run, ".tmp", "empty", "nested"));
        var gitTemp = Path.Combine(frozen, ".dispatch-tmp", "git");
        Directory.CreateDirectory(gitTemp);
        var sibling = Path.Combine(frozen, "sibling", "keep");
        Directory.CreateDirectory(Path.GetDirectoryName(sibling)!);
        File.WriteAllText(sibling, "foreign");
        string? link = null;
        if (remnants)
        {
            File.WriteAllText(Path.Combine(run, "keep"), "retained");
            File.WriteAllText(Path.Combine(gitTemp, "keep"), "retained");
            link = Path.Combine(run, ".tmp", "linked");
            if (OperatingSystem.IsWindows())
            {
                using var command = new CapturedProcess();
                (await command.RunAsync("cmd", ["/c", "mklink", "/J", link, Path.GetDirectoryName(sibling)!], tree.Root, tree.ChildEnvironment(), ct)).RequireSuccess("junction fixture");
            }
            else Directory.CreateSymbolicLink(link, Path.GetDirectoryName(sibling)!);
        }
        // The link must go before TempTree deletes the tree, or deletion follows it.
        try
        {
            for (var attempt = 0; attempt < 2; attempt++)
            {
                using var child = new HarnessProcess(tree);
                var result = await child.RunAsync(["clean", Run], ct);
                result.RequireSuccess("prune empty containers");
                await Assert.That(Directory.Exists(Path.Combine(run, ".tmp"))).IsEqualTo(remnants);
                await Assert.That(Directory.Exists(run)).IsEqualTo(remnants);
                await Assert.That(Directory.Exists(gitTemp)).IsEqualTo(remnants);
                await Assert.That(Directory.Exists(frozen)).IsTrue();
                await Assert.That(File.ReadAllText(sibling)).IsEqualTo("foreign");
                if (remnants)
                {
                    await Assert.That(result.Stdout).Contains($"retained: {run}");
                    await Assert.That(result.Stdout).Contains($"retained: {gitTemp}");
                    await Assert.That(result.Stdout).Contains($"retained: {Path.Combine(run, ".tmp", "linked")}");
                }
            }
        }
        finally
        {
            if (link is not null && Path.Exists(link)) Directory.Delete(link);
        }
    }

    static string Seed(TempTree tree, bool complete = true, string? ledgerRoot = null, string controlId = ControlId)
    {
        var ledger = Directory.CreateDirectory(Path.Combine(ledgerRoot ?? Path.Combine(tree.DispatchHome, "state", "ledgers"), Run)).FullName;
        Json.WriteFile(Path.Combine(ledger, "run.json"), new RunInfo { Run = Run, ControlId = controlId, Repo = tree.Repository }, LedgerJson.Files.RunInfo);
        File.WriteAllText(Path.Combine(ledger, "events.jsonl"), "{\"schemaVersion\":2,\"run\":\"recorded\",\"type\":\"run\"}\n{\"run\":\"recorded\",\"task\":\"inspect\",\"type\":\"queued\"}\n");
        var task = Directory.CreateDirectory(Path.Combine(ledger, "inspect")).FullName;
        Json.WriteFile(Path.Combine(task, "task.json"), new TaskRecord { Spec = new() { Id = "inspect", Kind = "research" } }, LedgerJson.Files.TaskRecord);
        Json.WriteFile(Path.Combine(ledger, "config.json"), new EffectiveConfig
        {
            Tasks = new()
            {
                ["inspect"] = new()
                {
                    Settings = new()
                    { Backend = new() { Value = "opencode" }, Route = new() { Value = "opencode:fixture/model" }, RawPrompt = new() { Value = true } }
                }
            }
        }, LedgerJson.Files.EffectiveConfig);
        var control = Directory.CreateDirectory(Path.Combine(tree.DispatchHome, "state", "control", controlId)).FullName;
        Json.WriteFile(Path.Combine(control, "run.ref"), new RunReference { Run = Run, LedgerRoot = Path.GetDirectoryName(ledger)! }, ControlJson.Files.RunReference);
        if (complete) File.WriteAllText(Path.Combine(control, "setup.done"), "");
        return control;
    }

    [Test]
    [Timeout(45_000)]
    public async Task Disposed_host_releases_profile_handles_before_returning(CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows()) Skip.Test("Windows exit-code reporting can precede process-handle signaling");
        using var tree = new TempTree();
        var profile = Directory.CreateDirectory(Path.Combine(tree.Root, "profiles", "opencode")).FullName;
        var environment = FakeOpenCode.Environment(tree, "one-task.jsonl");
        environment["FAKE_OPENCODE_PROFILE_HANDLES"] = "32768";
        await using var host = await HarnessServer.StartAsync(false,
            new(tree.Root, FakeOpenCode.Executable, environment), profile,
            new("full", "on", "workspace", "on", "worktree"), ct);
        using var process = System.Diagnostics.Process.GetProcessById(host.Server.ProcessId);
        _ = process.Handle;
        var holder = ObservedProcess.Capture(process.Id);
        process.Kill();
        while (!process.HasExited) ct.ThrowIfCancellationRequested();

        await host.DisposeAsync();

        var signaled = process.WaitForExit(0);
        IOException? deletionError = null;
        try { Directory.Delete(profile, recursive: true); }
        catch (IOException error) { deletionError = error; }
        Console.WriteLine($"Profile holder pid {holder.Pid}: exit reported, signaled after disposal={signaled}, delete={deletionError?.Message ?? "succeeded"}");
        process.WaitForExit();
        await Assert.That(signaled).IsTrue();
        await Assert.That(deletionError).IsNull();
    }

    [Test]
    [Arguments("full")]
    [Arguments("clean")]
    [Timeout(45_000)]
    public async Task Resume_continue_preserves_the_run_profile_and_temp_folders(string profile, CancellationToken ct)
    {
        using var tree = new TempTree();
        var repo = await LabRepo.CreateAsync(tree, ct);
        var control = Seed(tree);
        await repo.BaselineAsync(control, ct);
        var ledger = Path.Combine(tree.DispatchHome, "state", "ledgers", Run);
        var configPath = Path.Combine(ledger, "config.json");
        var config = JsonNode.Parse(File.ReadAllText(configPath))!;
        config["tasks"]!["inspect"]!["settings"]!["profile"] = new JsonObject { ["value"] = profile, ["actual"] = profile };
        File.WriteAllText(configPath, config.ToJsonString());
        Json.WriteFile(Path.Combine(ledger, "inspect", "task.json"), new TaskRecord
        {
            Spec = new() { Id = "inspect", Kind = "research" },
            Session = new() { Id = "ses_fixture", Turn = "old-turn" },
            Workspace = new() { Path = repo.Root, BaseSha = await repo.GitAsync(["rev-parse", "HEAD"], ct) },
        }, LedgerJson.Files.TaskRecord);
        var steps = File.ReadAllLines(FakeOpenCode.Fixture("one-task.jsonl")).Select(x => JsonNode.Parse(x)!).ToList();
        var interrupted = steps[11].DeepClone();
        interrupted["repeat"] = false;
        interrupted["reply"]!["data"]!["outcome"] = "interrupted";
        steps.Insert(4, interrupted);
        var arrived = Path.Combine(tree.Root, "environment-arrived");
        var release = Path.Combine(tree.Root, "environment-release");
        var environmentStep = steps.Single(x => x["expect"]!.GetValue<string>() == "PUT /api/session/ses_fixture/environment");
        environmentStep["arrived"] = arrived;
        environmentStep["waitFor"] = release;
        var scenario = Path.Combine(tree.Root, "resume.jsonl");
        File.WriteAllLines(scenario, steps.Select(x => x.ToJsonString()));
        var environment = FakeOpenCode.Environment(tree, "one-task.jsonl").ToDictionary(x => x.Key, x => (string?)x.Value);
        environment["FAKE_OPENCODE_SCENARIO"] = scenario;
        environment["FAKE_OPENCODE_PROFILE_HANDLES"] = "1";
        using var child = new HarnessProcess(tree);

        var requests = FakeOpenCode.Record(tree);
        var running = child.RunAsync(["resume", Run, "--continue"], ct, environment);
        System.Diagnostics.Process? server = null;
        try
        {
            while (!File.Exists(arrived))
            {
                if (running.IsCompleted) throw new InvalidOperationException((await running).ToString());
                await Task.Delay(10, ct);
            }
            var observedHost = JsonNode.Parse(File.ReadAllText(Directory.GetFiles(Path.GetDirectoryName(FakeOpenCode.Record(tree))!, "requests.jsonl.*.host.json").Single()))!;
            server = System.Diagnostics.Process.GetProcessById(observedHost["pid"]!.GetValue<int>());
            var observedServer = ObservedProcess.Capture(server.Id);
            if (OperatingSystem.IsWindows()) _ = server.Handle;
            File.WriteAllText(release, "go");
            (await running).RequireSuccess("resume --continue");
            if (OperatingSystem.IsWindows()) await Assert.That(server.WaitForExit(0)).IsTrue();
            // A POSIX non-child handle has a polling wait state, not a Windows process-exit signal.
            else await Assert.That(observedServer.IsAlive()).IsFalse();
        }
        finally
        {
            try { child.Kill(); await running; }
            finally
            {
                if (server is not null)
                {
                    try
                    {
                        if (!server.HasExited) server.Kill(entireProcessTree: true);
                        await Task.Run(server.WaitForExit);
                    }
                    finally { server.Dispose(); }
                }
            }
        }

        var sent = File.ReadLines(requests).Where(x => x.EndsWith('}')).Select(x => JsonNode.Parse(x)!)
            .Where(x => x["method"]!.GetValue<string>() == "PUT").ToArray();
        await Assert.That(sent.Length).IsEqualTo(1);
        var variables = sent[0]["body"]!["variables"]!;
        var expected = Path.Combine(tree.DispatchHome, "state", "worktrees", Run, ".tmp", "inspect");
        foreach (var name in new[] { "TEMP", "TMP", "TMPDIR" })
            await Assert.That(variables[name]!.GetValue<string>()).IsEqualTo(expected);
        var host = JsonNode.Parse(File.ReadAllText(Directory.GetFiles(Path.GetDirectoryName(FakeOpenCode.Record(tree))!, "requests.jsonl.*.host.json").Single()))!;
        await Assert.That(host["directory"]!.GetValue<string>()).IsEqualTo(Path.Combine(control, "profiles", "opencode"));
    }

    [Test]
    [Arguments("resume")]
    [Arguments("collect")]
    [Timeout(45_000)]
    public async Task Recovery_ReappliesFrozenScrubBeforeBackendAndVerify(string command, CancellationToken ct)
    {
        using var tree = new TempTree();
        var repo = await LabRepo.CreateAsync(tree, ct);
        var control = Seed(tree);
        await repo.BaselineAsync(control, ct);
        var ledger = Path.Combine(tree.DispatchHome, "state", "ledgers", Run);
        var configPath = Path.Combine(ledger, "config.json");
        var config = JsonNode.Parse(File.ReadAllText(configPath))!;
        config["restrictions"] = new JsonObject { ["scrub"] = new JsonArray("^FAKE_EXTRA_") };
        File.WriteAllText(configPath, config.ToJsonString());
        Json.WriteFile(Path.Combine(ledger, "inspect", "task.json"), new TaskRecord
        {
            Spec = new() { Id = "inspect", Kind = "research", Verify = [OperatingSystem.IsWindows()
                ? "[Environment]::GetEnvironmentVariables().Keys | Sort-Object" : "env | cut -d= -f1 | sort"] },
            Session = new() { Id = "ses_fixture" },
            Workspace = new() { Path = repo.Root, BaseSha = await repo.GitAsync(["rev-parse", "HEAD"], ct) },
        }, LedgerJson.Files.TaskRecord);
        var environment = FakeOpenCode.Environment(tree, "one-task.jsonl").ToDictionary(x => x.Key, x => (string?)x.Value);
        var namesFile = Path.Combine(tree.Root, "names.json");
        environment["FAKE_ENV_NAMES"] = namesFile;
        environment["FAKE_EXTRA_VALUE"] = "synthetic";
        environment["FAKE_KEEP"] = "kept";
        using var child = new HarnessProcess(tree);
        var result = await child.RunAsync(command == "collect" ? [command, Run, "inspect"] : [command, Run], ct, environment);
        result.RequireSuccess("recovery");
        var names = JsonNode.Parse(File.ReadAllText(namesFile))!.AsArray().Select(x => x!.GetValue<string>()).ToArray();
        await Assert.That(names).DoesNotContain("FAKE_EXTRA_VALUE");
        await Assert.That(names).Contains("FAKE_KEEP");
        var log = File.ReadAllText(Path.Combine(ledger, "inspect", "verify.log"));
        await Assert.That(log).DoesNotContain("FAKE_EXTRA_VALUE");
        await Assert.That(log).Contains("FAKE_KEEP");
    }

    [Test]
    [Timeout(45_000)]
    public async Task Reply_UnconfirmedBackendAnswer_Exits1(CancellationToken ct)
    {
        await using var lab = new OpenCodeLab();
        var control = Seed(lab.Tree);
        var ledger = new Ledger(Run);
        var backend = new FakeBackend("opencode", new("policy", "policy", "text", true))
        { OnAnswer = (_, _) => Task.FromResult(new AnswerResult(false, "answer failed", 500)) };
        await using var owned = backend;
        var policy = new Policy { Path = "config.json", Deny = [], Instructions = [], Escalate = [new() { Name = "shell", Action = "shell", Resources = ["*"] }] };
        var escalation = new Escalation(new Dictionary<string, IWorkerBackend> { [backend.Name] = backend }, ledger, policy);
        var run = new TaskRun { Record = new() { Spec = new() { Id = "inspect" } }, State = TaskState.Running };
        await escalation.AnswerAsync(run, new("inspect", "turn", new("opencode", "worker", null, "backend-ask"), "shell", ["command"]), ct);
        using var held = SupervisorLock.AcquireFolder(control);
        var replying = Commands.ReplyAsync(Args.Parse(["reply", Run, "inspect-1", "once"]), ct);
        try
        {
            while (!File.Exists(Path.Combine(control, "decisions", "inspect-1.json")))
            { ct.ThrowIfCancellationRequested(); await Task.Yield(); }
            await escalation.CheckDecisionsAsync(ct);
        }
        finally { await replying; }
        await Assert.That(await replying).IsEqualTo(1);
    }

    [Test]
    [Arguments("reply")]
    [Arguments("cancel")]
    [Arguments("asks")]
    [Arguments("resume")]
    [Arguments("collect")]
    [Timeout(30_000)]
    public async Task ControlCommands_MissingControlFolder_Exit2(string command, CancellationToken ct)
    {
        using var tree = new TempTree();
        var control = Seed(tree);
        Directory.Delete(control, true);
        using var child = new HarnessProcess(tree);
        string[] args = command switch
        {
            "reply" => [command, Run, "ask", "once"],
            "cancel" or "collect" => [command, Run, "inspect"],
            _ => [command, Run],
        };
        var result = await child.RunAsync(args, ct);
        await Assert.That(result.ExitCode).IsEqualTo(2);
        await Assert.That(result.Stderr).Contains(control);
        await Assert.That(Directory.Exists(control)).IsFalse();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    [Timeout(30_000)]
    public async Task Clean_Twice_SameEndState(bool missingControl, CancellationToken ct)
    {
        using var tree = new TempTree();
        var repo = await LabRepo.CreateAsync(tree, ct);
        var control = Seed(tree);
        await repo.BaselineAsync(control, ct);
        File.WriteAllText(Path.Combine(control, "supervisor.lock"), "");
        File.WriteAllText(Path.Combine(control, "handover.json"), "unused");
        Directory.CreateDirectory(Path.Combine(control, "bin"));
        var worktree = Path.Combine(tree.WorktreeRoot, "inspect");
        await repo.GitAsync(["worktree", "add", "-b", "dispatch/test/inspect", worktree, "main"], ct);
        var ledger = Path.Combine(tree.DispatchHome, "state", "ledgers", Run);
        Json.WriteFile(Path.Combine(ledger, "inspect", "task.json"), new TaskRecord
        {
            Spec = new() { Id = "inspect", Kind = "research" },
            Workspace = new() { Path = worktree, Branch = "dispatch/test/inspect", BaseRef = "main" },
        }, LedgerJson.Files.TaskRecord);
        var temp = Directory.CreateDirectory(Path.Combine(tree.WorktreeRoot, Run, ".tmp", "inspect")).FullName;
        File.WriteAllText(Path.Combine(temp, "readonly"), "temporary");
        File.SetAttributes(Path.Combine(temp, "readonly"), FileAttributes.ReadOnly);
        var config = Directory.CreateDirectory(Path.Combine(tree.DispatchHome, "config")).FullName;
        File.WriteAllText(Path.Combine(config, "config.jsonc"), new JsonObject
        {
            ["schemaVersion"] = 2,
            ["locations"] = new JsonObject { ["worktreeRoot"] = tree.WorktreeRoot },
            ["backends"] = new JsonObject
            {
                ["opencode"] = new JsonObject { ["executable"] = Path.Combine(tree.Root, "missing-opencode") }
            }
        }.ToJsonString());
        if (missingControl) Directory.Delete(control, true);

        for (var i = 0; i < 2; i++)
        {
            using var child = new HarnessProcess(tree);
            var result = await child.RunAsync(["clean", Run], ct);
            result.RequireSuccess("clean");
            await Assert.That(Directory.Exists(worktree)).IsFalse();
            await Assert.That(Directory.Exists(temp)).IsFalse();
            await Assert.That(await repo.GitAsync(["branch", "--list", "dispatch/test/inspect"], ct)).IsEqualTo("");
            if (!missingControl)
                await Assert.That(Directory.GetFileSystemEntries(control).Select(x => Path.GetFileName(x)).Order().ToArray())
                    .IsEquivalentTo(new[] { "run.ref", "supervisor.lock" });
            else await Assert.That(Directory.Exists(control)).IsFalse();
        }
    }

    [Test]
    [Arguments("resume")]
    [Arguments("collect")]
    [Timeout(30_000)]
    public async Task Resume_SetupNeverFinished_Exits2(string command, CancellationToken ct)
    {
        using var tree = new TempTree();
        Seed(tree, complete: false);
        using var child = new HarnessProcess(tree);
        var result = await child.RunAsync(command == "resume" ? [command, Run] : [command, Run, "inspect"], ct);
        await Assert.That(result.ExitCode).IsEqualTo(2);
        await Assert.That(result.Stderr).Contains("setup never finished");
    }

    [Test]
    [Timeout(45_000)]
    public async Task ControlCommands_IgnoreRunRef(CancellationToken ct)
    {
        using var tree = new TempTree();
        var control = Seed(tree);
        Json.WriteFile(Path.Combine(control, "run.ref"), new RunReference { Run = "display-only", LedgerRoot = tree.Root }, ControlJson.Files.RunReference);
        var cmd = Directory.CreateDirectory(Path.Combine(tree.DispatchHome, "state", "control", "cmd-unreadable")).FullName;
        File.WriteAllText(Path.Combine(cmd, "run.ref"), "{");
        using (var child = new HarnessProcess(tree))
        {
            var result = await child.RunAsync(["asks"], ct);
            result.RequireSuccess("asks all");
            await Assert.That(result.Stdout).Contains("display-only");
            await Assert.That(result.Stdout.Contains("cmd-unreadable", StringComparison.Ordinal)).IsFalse();
        }
        using (var child = new HarnessProcess(tree)) (await child.RunAsync(["asks", Run], ct)).RequireSuccess("asks run");
        using var held = new FileStream(Path.Combine(control, "supervisor.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        using var cancel = new HarnessProcess(tree);
        var cancelling = cancel.RunAsync(["cancel", Run, "inspect"], ct);
        var file = Path.Combine(control, "cancel", "inspect.json");
        while (!File.Exists(file)) { if (cancelling.IsCompleted) throw new InvalidOperationException((await cancelling).ToString()); await Task.Delay(10, ct); }
        var request = JsonNode.Parse(File.ReadAllText(file))!.AsObject();
        await Assert.That(request.Select(x => x.Key).Order().ToArray()).IsEquivalentTo(new[] { "run", "schemaVersion", "task", "time" });
        await Assert.That(request["run"]!.GetValue<string>()).IsEqualTo(Run);
        await Assert.That(request["task"]!.GetValue<string>()).IsEqualTo("inspect");
        var ledger = Path.Combine(tree.DispatchHome, "state", "ledgers", Run, "events.jsonl");
        File.AppendAllText(ledger, new JsonObject
        {
            ["run"] = Run,
            ["task"] = "inspect",
            ["type"] = "cancel",
            ["data"] = new JsonObject { ["time"] = request["time"]!.DeepClone() }
        }.ToJsonString() + "\n");
        var cancelled = await cancelling;
        await Assert.That(cancelled.ExitCode).IsEqualTo(0);
        held.Dispose();
        File.Delete(Path.Combine(control, "run.ref"));
        using (var child = new HarnessProcess(tree))
        {
            var listed = await child.RunAsync(["asks"], ct);
            listed.RequireSuccess("asks without run.ref");
            await Assert.That(listed.Stdout).Contains("run.ref missing or invalid");
        }
    }

    [Test]
    [Arguments("resume")]
    [Arguments("collect")]
    [Timeout(45_000)]
    public async Task Collect_WhileResumeStarts_OneWins(string winner, CancellationToken ct)
    {
        using var tree = new TempTree();
        var repo = await LabRepo.CreateAsync(tree, ct);
        var control = Seed(tree);
        await repo.BaselineAsync(control, ct);
        File.WriteAllText(Path.Combine(control, "run.ref"), "{");
        var ledger = Path.Combine(tree.DispatchHome, "state", "ledgers", Run);
        var record = new TaskRecord
        {
            Spec = new() { Id = "inspect", Kind = "research", RawPrompt = true },
            PromptText = "Inspect.",
            Session = new() { Id = "ses_fixture" },
            Workspace = new() { Path = repo.Root, BaseSha = await repo.GitAsync(["rev-parse", "HEAD"], ct) }
        };
        Json.WriteFile(Path.Combine(ledger, "inspect", "task.json"), record, LedgerJson.Files.TaskRecord);
        var steps = File.ReadAllLines(FakeOpenCode.Fixture("one-task.jsonl")).Select(x => JsonNode.Parse(x)!).ToArray();
        var arrived = Path.Combine(tree.Root, "arrived");
        var release = Path.Combine(tree.Root, "release");
        steps[0]!["arrived"] = arrived;
        steps[0]!["waitFor"] = release;
        var scenario = Path.Combine(tree.Root, "scenario.jsonl");
        File.WriteAllLines(scenario, steps.Select(x => x.ToJsonString()));
        var env = FakeOpenCode.Environment(tree, "one-task.jsonl").ToDictionary(x => x.Key, x => (string?)x.Value);
        env["FAKE_OPENCODE_SCENARIO"] = scenario;
        using var first = new HarnessProcess(tree);
        var running = first.RunAsync(winner == "resume" ? [winner, Run] : [winner, Run, "inspect"], ct, env);
        while (!File.Exists(arrived)) { if (running.IsCompleted) throw new InvalidOperationException((await running).ToString()); await Task.Delay(10, ct); }
        using (var second = new HarnessProcess(tree))
        {
            var result = await second.RunAsync(winner == "resume" ? ["collect", Run, "inspect"] : ["resume", Run], ct, env);
            await Assert.That(result.ExitCode).IsEqualTo(2);
            await Assert.That(result.Stderr).Contains("supervised");
        }
        using (var status = new HarnessProcess(tree))
        {
            var result = await status.RunAsync(["status", Run], ct);
            result.RequireSuccess("status while locked");
            await Assert.That(result.Stdout).Contains("supervised now");
        }
        using (var clean = new HarnessProcess(tree))
        {
            var result = await clean.RunAsync(["clean", Run], ct);
            await Assert.That(result.ExitCode).IsEqualTo(2);
        }
        File.WriteAllText(release, "go");
        (await running).RequireSuccess(winner);
        await Assert.That(File.Exists(Path.Combine(control, "supervisor.lock"))).IsTrue();
        using (var reacquired = SupervisorLock.AcquireFolder(control)) { }
        await Assert.That(File.Exists(Path.Combine(control, "supervisor.lock"))).IsTrue();
    }

    [Test]
    [Timeout(45_000)]
    public async Task ControlFolder_SameRunIdTwoLedgerRoots_DoNotCollide(CancellationToken ct)
    {
        using var tree = new TempTree();
        var first = Seed(tree);
        var otherRoot = Path.Combine(tree.Root, "other-ledgers");
        var second = Seed(tree, ledgerRoot: otherRoot, controlId: "zzzzzzzzzzzzzzzzzzzzzzzzzz");
        foreach (var (folder, askId) in new[] { (first, "ask-a"), (second, "ask-b") })
            Json.WriteFile(Path.Combine(folder, "asks", askId + ".json"), new PendingAsk
            { Run = Run, Task = "inspect", AskId = askId, Action = "shell", Resources = [askId], Time = Json.Timestamp() }, ControlJson.Files.PendingAsk);
        var otherEvents = Path.Combine(otherRoot, Run, "events.jsonl");
        var before = File.ReadAllText(otherEvents);
        using (var asks = new HarnessProcess(tree))
        {
            var result = await asks.RunAsync(["asks", Run], ct);
            result.RequireSuccess("asks first ledger");
            await Assert.That(result.Stdout).Contains("ask-a");
            await Assert.That(result.Stdout.Contains("ask-b", StringComparison.Ordinal)).IsFalse();
        }
        using (var cancel = new HarnessProcess(tree)) (await cancel.RunAsync(["cancel", Run, "inspect"], ct)).RequireSuccess("cancel first ledger");
        using (var held = new FileStream(Path.Combine(first, "supervisor.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        using (var reply = new HarnessProcess(tree))
        {
            var replying = reply.RunAsync(["reply", Run, "ask-a", "once"], ct);
            while (!File.Exists(Path.Combine(first, "decisions", "ask-a.json")))
            { if (replying.IsCompleted) throw new InvalidOperationException((await replying).ToString()); await Task.Delay(10, ct); }
            File.AppendAllText(Path.Combine(tree.DispatchHome, "state", "ledgers", Run, "events.jsonl"),
                "{\"run\":\"recorded\",\"task\":\"inspect\",\"type\":\"permission\",\"data\":{\"id\":\"ask-a\",\"by\":\"operator\",\"answer\":\"once\"}}\n");
            (await replying).RequireSuccess("reply first ledger");
        }
        using (var clean = new HarnessProcess(tree)) (await clean.RunAsync(["clean", Run], ct)).RequireSuccess("clean first ledger");
        await Assert.That(File.Exists(Path.Combine(second, "asks", "ask-b.json"))).IsTrue();
        await Assert.That(Directory.Exists(Path.Combine(second, "decisions"))).IsFalse();
        await Assert.That(File.ReadAllText(otherEvents)).IsEqualTo(before);
        using (var asks = new HarnessProcess(tree))
        {
            var result = await asks.RunAsync(["asks", Run, "--ledger", otherRoot], ct);
            result.RequireSuccess("asks second ledger");
            await Assert.That(result.Stdout).Contains("ask-b");
        }
    }

    [Test]
    [Arguments("reply")]
    [Arguments("cancel")]
    [Timeout(45_000)]
    public async Task ControlCommand_QueuedWithoutLedgerConfirmation_Exits1(string command, CancellationToken ct)
    {
        using var tree = new TempTree();
        var control = Seed(tree);
        var askFile = Path.Combine(control, "asks", "ask.json");
        Json.WriteFile(askFile, new PendingAsk { Run = Run, Task = "inspect", AskId = "ask", Time = Json.Timestamp() }, ControlJson.Files.PendingAsk);
        using var held = new FileStream(Path.Combine(control, "supervisor.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        using var child = new HarnessProcess(tree);
        var running = child.RunAsync(command == "reply" ? [command, Run, "ask", "once"] : [command, Run, "inspect"], ct);
        var queuedFile = command == "reply" ? Path.Combine(control, "decisions", "ask.json") : Path.Combine(control, "cancel", "inspect.json");
        while (!File.Exists(queuedFile))
        { if (running.IsCompleted) throw new InvalidOperationException((await running).ToString()); await Task.Delay(10, ct); }
        // A disappearing display file is not a supervisor acknowledgement.
        File.Delete(askFile);
        var result = await running;
        await Assert.That(result.ExitCode).IsEqualTo(1);
        await Assert.That(File.Exists(queuedFile)).IsTrue();
    }
}
