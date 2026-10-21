using System.Text.Json.Nodes;
using Dispatch.Tests.Support;

namespace Dispatch.Tests.Integration;

[Category("Integration")]
[NotInParallel]
public sealed class ControlCommandTests
{
    const string Run = "recorded";
    const string ControlId = "abcdefghijklmnopqrstuvwxyz";

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
        var scenario = Path.Combine(tree.Root, "resume.jsonl");
        File.WriteAllLines(scenario, steps.Select(x => x.ToJsonString()));
        var environment = FakeOpenCode.Environment(tree, "one-task.jsonl").ToDictionary(x => x.Key, x => (string?)x.Value);
        environment["FAKE_OPENCODE_SCENARIO"] = scenario;
        using var child = new HarnessProcess(tree);

        var requests = Path.Combine(tree.Root, "requests.jsonl");
        var events = Path.Combine(ledger, "events.jsonl");
        static string SharedText(string path)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        var running = child.RunAsync(["resume", Run, "--continue"], ct, environment);
        try
        {
            while (!running.IsCompleted)
            {
                if (File.Exists(requests) && SharedText(requests).Split('\n').Any(x => x.Contains("\"method\":\"PUT\"", StringComparison.Ordinal) && x.EndsWith('}'))) break;
                if (SharedText(events).Contains("\"while\":\"resume\"", StringComparison.Ordinal)) break;
                await Task.Delay(10, ct);
            }
        }
        finally { child.Kill(); await running; }

        var sent = File.ReadLines(requests).Where(x => x.EndsWith('}')).Select(x => JsonNode.Parse(x)!)
            .Where(x => x["method"]!.GetValue<string>() == "PUT").ToArray();
        await Assert.That(sent.Length).IsEqualTo(1);
        var variables = sent[0]["body"]!["variables"]!;
        var expected = Path.Combine(tree.DispatchHome, "state", "worktrees", Run, ".tmp", "inspect");
        foreach (var name in new[] { "TEMP", "TMP", "TMPDIR" })
            await Assert.That(variables[name]!.GetValue<string>()).IsEqualTo(expected);
        var host = JsonNode.Parse(File.ReadAllText(Directory.GetFiles(tree.Root, "requests.jsonl.*.host.json").Single()))!;
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
            ["locations"] = new JsonObject { ["worktreeRoot"] = tree.WorktreeRoot }
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
