using Dispatch.Tests.Support;
using System.Text.Json.Nodes;

namespace Dispatch.Tests.Integration;

[Category("Integration")]
[NotInParallel("processes")]
public sealed class ExitCodeTests
{
    [Test]
    [Arguments("plan", false, 0)]
    [Arguments("plan", true, 2)]
    [Arguments("selfcheck", false, 0)]
    [Arguments("selfcheck", true, 1)]
    [Timeout(45_000)]
    public async Task Checks_distinguish_a_refusal_from_a_failed_selfcheck(string command, bool missingTool, int expected, CancellationToken ct)
    {
        using var tree = new TempTree();
        var repo = await LabRepo.CreateAsync(tree, ct);
        var tasks = Path.Combine(tree.Root, "tasks.jsonc");
        File.WriteAllText(tasks, """{"schemaVersion":2,"defaults":{"backend":"opencode"},"tasks":[{"id":"inspect","kind":"research","base":"main","route":"fixture/model","prompt":"Inspect."}]}""");
        var env = FakeOpenCode.Environment(tree, "one-task.jsonl").ToDictionary(x => x.Key, x => (string?)x.Value);
        if (missingTool)
        {
            var config = Path.Combine(tree.DispatchHome, "config", "config.jsonc");
            var document = JsonNode.Parse(File.ReadAllText(config))!;
            document["backends"]!["opencode"]!["executable"] = Path.Combine(tree.Root, "missing-executable");
            File.WriteAllText(config, document.ToJsonString());
        }
        else if (command == "selfcheck")
        {
            var fixture = File.ReadAllLines(FakeOpenCode.Fixture("one-task.jsonl")).Select(x => JsonNode.Parse(x)!).ToArray();
            var session = fixture[11]["reply"]!.DeepClone();
            session["data"]!["permissions"] = new JsonArray(new JsonObject
            {
                ["action"] = "edit", ["resource"] = Path.Combine(tree.DispatchHome, "state", "control", "**").Replace('\\', '/'), ["effect"] = "deny",
            });
            var steps = fixture.Take(4).ToList();
            var empty = JsonNode.Parse("""{"location":{"directory":"$directory"},"data":[]}""")!;
            steps.Add(new JsonObject { ["expect"] = "GET /api/mcp", ["reply"] = empty.DeepClone() });
            steps.Add(new JsonObject { ["expect"] = "POST /api/session", ["reply"] = session.DeepClone() });
            steps.Add(new JsonObject { ["expect"] = "GET /api/session/ses_fixture", ["reply"] = session.DeepClone() });
            steps.Add(fixture[10].DeepClone());
            foreach (var endpoint in new[] { "permission", "form" })
                steps.Add(new JsonObject { ["expect"] = $"GET /api/session/ses_fixture/{endpoint}", ["reply"] = empty.DeepClone() });
            steps.Add(new JsonObject { ["expect"] = "DELETE /api/session/ses_fixture", ["status"] = 204 });
            steps.Add(new JsonObject { ["expect"] = "GET /api/session/ses_fixture", ["status"] = 404,
                ["reply"] = new JsonObject { ["_tag"] = "SessionNotFoundError", ["sessionID"] = "ses_fixture", ["message"] = "removed" } });
            var scenario = Path.Combine(tree.Root, "selfcheck.jsonl");
            File.WriteAllLines(scenario, steps.Select(x => x.ToJsonString()));
            env["FAKE_OPENCODE_SCENARIO"] = scenario;
        }
        using var child = new HarnessProcess(tree);

        var result = await child.RunAsync(command == "plan" ? [command, tasks, "--repo", repo.Root] : [command, "--repo", repo.Root], ct, env);

        TestContext.Current!.Output.WriteLine(result.ToString());
        await Assert.That(result.ExitCode).IsEqualTo(expected);
        await Assert.That(Directory.GetFiles(tree.DispatchHome, "run.json", SearchOption.AllDirectories)).IsEmpty();
    }

    [Test]
    [Arguments("queued")]
    [Arguments("failed")]
    [Arguments("cancelled")]
    [Timeout(30_000)]
    public async Task Status_reports_states_without_judging_them(string state, CancellationToken ct)
    {
        using var tree = new TempTree();
        Seed(tree, state);
        using var child = new HarnessProcess(tree);

        var result = await child.RunAsync(["status", "recorded"], ct);

        await Assert.That(result.ExitCode).IsEqualTo(0);
        await Assert.That(result.Stdout).Contains(state);
    }

    [Test]
    [Arguments("success", 0, "")]
    [Arguments("verify", 1, "verify-failed")]
    [Arguments("scope", 1, "out-of-scope")]
    [Timeout(45_000)]
    public async Task Run_exit_reflects_settlement(string condition, int expected, string failure, CancellationToken ct)
    {
        using var tree = new TempTree();
        var repo = await LabRepo.CreateAsync(tree, ct);
        var tasks = Path.Combine(tree.Root, "tasks.jsonc");
        var document = JsonNode.Parse("""{"schemaVersion":2,"run":"exit-check","defaults":{"backend":"opencode"},"tasks":[{"id":"inspect","kind":"code","base":"main","route":"fixture/model","prompt":"Inspect.","scope":["allowed/**"]}]}""")!;
        if (condition == "verify") document["tasks"]![0]!["verify"] = new JsonArray("exit 3");
        File.WriteAllText(tasks, document.ToJsonString());
        var env = FakeOpenCode.Environment(tree, "one-task.jsonl").ToDictionary(x => x.Key, x => (string?)x.Value);
        var steps = File.ReadAllLines(FakeOpenCode.Fixture("one-task.jsonl")).Select(x => JsonNode.Parse(x)!).ToArray();
        var arrived = Path.Combine(tree.Root, "prompt-arrived");
        var release = Path.Combine(tree.Root, "release-prompt");
        if (condition == "scope")
        {
            steps[6]["arrived"] = arrived;
            steps[6]["waitFor"] = release;
        }
        var scenario = Path.Combine(tree.Root, "scenario.jsonl");
        File.WriteAllLines(scenario, steps.Select(x => x.ToJsonString()));
        env["FAKE_OPENCODE_SCENARIO"] = scenario;
        var promptArrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var watcher = new FileSystemWatcher(tree.Root, Path.GetFileName(arrived));
        watcher.Created += (_, _) => promptArrived.TrySetResult();
        watcher.Error += (_, error) => promptArrived.TrySetException(error.GetException());
        watcher.EnableRaisingEvents = true;
        using var child = new HarnessProcess(tree);
        var running = child.RunAsync(["run", tasks, "--repo", repo.Root], ct, env);
        if (condition == "scope")
        {
            if (await Task.WhenAny(promptArrived.Task, running).WaitAsync(ct) == running)
                throw new InvalidOperationException((await running).ToString());
            await promptArrived.Task.WaitAsync(ct);
            var worktree = Directory.GetDirectories(Path.Combine(tree.DispatchHome, "state", "worktrees"))
                .Single(path => Directory.Exists(Path.Combine(path, "inspect")));
            File.WriteAllText(Path.Combine(worktree, "inspect", "outside.txt"), "worker edit");
            File.WriteAllText(release, "go");
        }

        var result = await running;

        await Assert.That(result.ExitCode).IsEqualTo(expected);
        var ledger = Directory.GetDirectories(Path.Combine(tree.DispatchHome, "state", "ledgers")).Single();
        var saved = JsonNode.Parse(File.ReadAllText(Path.Combine(ledger, "inspect", "result.json")))!;
        await Assert.That(saved["state"]!.GetValue<string>()).IsEqualTo(expected == 0 ? "done" : "failed");
        await Assert.That(saved["failures"]!.AsArray().Select(x => x!.GetValue<string>()).ToArray())
            .IsEquivalentTo(failure.Length == 0 ? [] : new[] { failure });
    }

    [Test]
    [Timeout(45_000)]
    public async Task Collect_FailedTask_Exits1(CancellationToken ct)
    {
        using var tree = new TempTree();
        var repo = await LabRepo.CreateAsync(tree, ct);
        var ledger = Seed(tree, "done");
        await repo.BaselineAsync(Path.Combine(tree.DispatchHome, "state", "control", "abcdefghijklmnopqrstuvwxyz"), ct);
        Json.WriteFile(Path.Combine(ledger, "inspect", "task.json"), new TaskRecord
        {
            Spec = new() { Id = "inspect", Kind = "code", Scope = ["**"], Verify = ["exit 3"] },
            Session = new() { Id = "ses_fixture" },
            Workspace = new() { Path = repo.Root, BaseSha = await repo.GitAsync(["rev-parse", "HEAD"], ct) },
        }, LedgerJson.Files.TaskRecord);
        using var child = new HarnessProcess(tree);

        var result = await child.RunAsync(["collect", "recorded", "inspect"], ct,
            FakeOpenCode.Environment(tree, "one-task.jsonl").ToDictionary(x => x.Key, x => (string?)x.Value));

        await Assert.That(result.ExitCode).IsEqualTo(1);
        await Assert.That(File.ReadAllText(Path.Combine(ledger, "inspect", "result.json"))).Contains("verify-failed");
    }

    [Test]
    [Arguments("usage")]
    [Arguments("unknown")]
    [Arguments("missing-value")]
    [Arguments("plan-errors")]
    [Arguments("run-errors")]
    [Timeout(30_000)]
    public async Task Refusals_exit_2_without_starting_a_run(string kind, CancellationToken ct)
    {
        using var tree = new TempTree();
        var tasks = Path.Combine(tree.Root, "tasks.jsonc");
        File.WriteAllText(tasks, """{"schemaVersion":2,"tasks":[]}""");
        string[] args = kind switch
        {
            "usage" => [], "unknown" => ["not-a-command"], "missing-value" => ["run", "--max"],
            "plan-errors" => ["plan", tasks], _ => ["run", tasks],
        };
        using var child = new HarnessProcess(tree);

        var result = await child.RunAsync(args, ct);

        await Assert.That(result.ExitCode).IsEqualTo(2);
        await Assert.That(Directory.GetFiles(tree.DispatchHome, "run.json", SearchOption.AllDirectories)).IsEmpty();
        await Assert.That(result.Stderr).DoesNotContain("Unhandled exception");
    }

    [Test]
    [Timeout(30_000)]
    public async Task Collect_CorruptMiddleLedgerLine_Exits1(CancellationToken ct)
    {
        using var tree = new TempTree();
        var ledger = Seed(tree, "done");
        Json.WriteFile(Path.Combine(ledger, "inspect", "task.json"), new TaskRecord
        {
            Spec = new() { Id = "inspect", Kind = "research" },
            Session = new() { Id = "ses_fixture" }, Workspace = new() { Path = tree.Repository },
        }, LedgerJson.Files.TaskRecord);
        File.AppendAllText(Path.Combine(ledger, "events.jsonl"), "not-json\n{\"run\":\"recorded\",\"type\":\"tail\"}\n");
        using var child = new HarnessProcess(tree);
        var result = await child.RunAsync(["collect", "recorded", "inspect"], ct,
            FakeOpenCode.Environment(tree, "one-task.jsonl").ToDictionary(x => x.Key, x => (string?)x.Value));
        await Assert.That(result.ExitCode).IsEqualTo(1);
        await Assert.That(result.Stderr).Contains("events.jsonl");
        await Assert.That(result.Stderr).Contains("line 3");
        await Assert.That(File.Exists(Path.Combine(tree.Root, "requests.jsonl"))).IsFalse();
    }

    [Test]
    [Arguments("status", 2)]
    [Arguments("resume", 1)]
    [Timeout(30_000)]
    public async Task Corrupt_middle_line_uses_the_command_exit_rule(string command, int expected, CancellationToken ct)
    {
        using var tree = new TempTree();
        var ledger = Seed(tree, "done");
        File.AppendAllText(Path.Combine(ledger, "events.jsonl"), "not-json\n{\"run\":\"recorded\",\"type\":\"tail\"}\n");
        using var child = new HarnessProcess(tree);

        var result = await child.RunAsync([command, "recorded"], ct,
            FakeOpenCode.Environment(tree, "one-task.jsonl").ToDictionary(x => x.Key, x => (string?)x.Value));

        await Assert.That(result.ExitCode).IsEqualTo(expected);
        await Assert.That(result.Stderr).Contains("events.jsonl");
        await Assert.That(result.Stderr).Contains("line 3");
    }

    [Test]
    [Timeout(45_000)]
    public async Task Run_UnexpectedIOException_ExitsCleanly(CancellationToken ct)
    {
        using var tree = new TempTree();
        var repo = await LabRepo.CreateAsync(tree, ct);
        var tasks = Path.Combine(tree.Root, "tasks.jsonc");
        File.WriteAllText(tasks, """{"schemaVersion":2,"run":"blocked-setup","defaults":{"backend":"opencode"},"tasks":[{"id":"inspect","kind":"research","base":"main","route":"fixture/model","prompt":"Inspect."}]}""");
        var ledger = Path.Combine(tree.DispatchHome, "state", "ledgers", Commands.CreateRunId("blocked-setup"));
        // The run claim succeeds, then copying the frozen task file encounters this directory.
        Directory.CreateDirectory(Path.Combine(ledger, "tasks.jsonc"));
        using var child = new HarnessProcess(tree);

        var result = await child.RunAsync(["run", tasks, "--repo", repo.Root], ct,
            FakeOpenCode.Environment(tree, "one-task.jsonl").ToDictionary(x => x.Key, x => (string?)x.Value));

        await Assert.That(result.ExitCode).IsEqualTo(1);
        await Assert.That(File.Exists(Path.Combine(ledger, "run.json"))).IsTrue();
        var events = File.ReadAllLines(Path.Combine(ledger, "events.jsonl")).Select(x => JsonNode.Parse(x)!);
        await Assert.That(events.Any(x => x["type"]!.GetValue<string>() == "aborted")).IsTrue();
        await Assert.That(result.Stderr.Trim().Split('\n').Length).IsEqualTo(1);
    }

    [Test]
    [Arguments("done", false, 0)]
    [Arguments("failed", false, 1)]
    [Arguments("cancelled", false, 1)]
    [Arguments("queued", false, 1)]
    [Arguments("failed", true, 1)]
    [Timeout(45_000)]
    public async Task Resume_judges_every_task_in_the_run(string state, bool recoverAnother, int expected, CancellationToken ct)
    {
        using var tree = new TempTree();
        var repo = await LabRepo.CreateAsync(tree, ct);
        var ledger = Seed(tree, state);
        await repo.BaselineAsync(Path.Combine(tree.DispatchHome, "state", "control", "abcdefghijklmnopqrstuvwxyz"), ct);
        if (recoverAnother)
        {
            var record = new TaskRecord
            {
                Spec = new() { Id = "recover", Kind = "research", RawPrompt = true },
                Session = new() { Id = "ses_fixture" },
                Workspace = new() { Path = repo.Root, BaseSha = await repo.GitAsync(["rev-parse", "HEAD"], ct) },
            };
            Json.WriteFile(Path.Combine(ledger, "recover", "task.json"), record, LedgerJson.Files.TaskRecord);
            var frozen = System.Text.Json.JsonSerializer.Deserialize(File.ReadAllText(Path.Combine(ledger, "config.json")), LedgerJson.Files.EffectiveConfig)!;
            frozen.Tasks["recover"] = frozen.Tasks["inspect"];
            Json.WriteFile(Path.Combine(ledger, "config.json"), frozen, LedgerJson.Files.EffectiveConfig);
            File.AppendAllText(Path.Combine(ledger, "events.jsonl"), "{\"run\":\"recorded\",\"task\":\"recover\",\"type\":\"queued\"}\n");
        }
        using var child = new HarnessProcess(tree);

        var result = await child.RunAsync(["resume", "recorded"], ct,
            FakeOpenCode.Environment(tree, "one-task.jsonl").ToDictionary(x => x.Key, x => (string?)x.Value));

        await Assert.That(result.ExitCode).IsEqualTo(expected);
        if (recoverAnother)
            await Assert.That(JsonNode.Parse(File.ReadAllText(Path.Combine(ledger, "recover", "result.json")))!["state"]!.GetValue<string>()).IsEqualTo("done");
    }

    static string Seed(TempTree tree, string state)
    {
        var ledger = Directory.CreateDirectory(Path.Combine(tree.DispatchHome, "state", "ledgers", "recorded")).FullName;
        const string controlId = "abcdefghijklmnopqrstuvwxyz";
        Json.WriteFile(Path.Combine(ledger, "run.json"), new RunInfo { Run = "recorded", ControlId = controlId, Repo = tree.Repository }, LedgerJson.Files.RunInfo);
        File.WriteAllText(Path.Combine(ledger, "events.jsonl"), "{\"schemaVersion\":2,\"run\":\"recorded\",\"type\":\"run\"}\n" + new JsonObject
        {
            ["run"] = "recorded", ["task"] = "inspect", ["type"] = state == "queued" ? "queued" : "final",
            ["data"] = new JsonObject { ["state"] = state },
        }.ToJsonString() + "\n");
        Json.WriteFile(Path.Combine(ledger, "inspect", "task.json"), new TaskRecord { Spec = new() { Id = "inspect", Kind = "research" } }, LedgerJson.Files.TaskRecord);
        Json.WriteFile(Path.Combine(ledger, "config.json"), new EffectiveConfig
        {
            Tasks = new() { ["inspect"] = new() { Settings = new()
            {
                Backend = new() { Value = "opencode" }, Route = new() { Value = "opencode:fixture/model" }, RawPrompt = new() { Value = true },
            } } },
        }, LedgerJson.Files.EffectiveConfig);
        var control = Directory.CreateDirectory(Path.Combine(tree.DispatchHome, "state", "control", controlId)).FullName;
        File.WriteAllText(Path.Combine(control, "setup.done"), "");
        return ledger;
    }

    [Test]
    [Timeout(30_000)]
    public async Task Unexpected_exception_before_start_exits_2_with_one_line(CancellationToken ct)
    {
        using var tree = new TempTree();
        var blocked = Path.Combine(tree.Root, "blocked-home");
        File.WriteAllText(blocked, "not a directory");
        using var child = new HarnessProcess(tree);

        var result = await child.RunAsync(["status"], ct, new Dictionary<string, string?> { ["DISPATCH_HOME"] = blocked });

        await Assert.That(result.ExitCode).IsEqualTo(2);
        await Assert.That(result.Stderr.Trim().Split('\n').Length).IsEqualTo(1);
        await Assert.That(result.Stderr).StartsWith("dispatch: ");
        await Assert.That(result.Stderr).Contains("Exception:");
    }
}
