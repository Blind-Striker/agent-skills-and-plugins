using System.Text.Json.Nodes;
using Dispatch.Tests.Support;

namespace Dispatch.Tests.Integration;

[Category("Integration")]
[NotInParallel("processes")]
public sealed class ConfigLayerTests
{
    [Test]
    [Timeout(180_000)]
    public async Task Missing_policy_is_a_named_refusal_not_a_crash(CancellationToken cancellationToken)
    {
        using var tree = new TempTree();
        var repo = await LabRepo.CreateAsync(tree, cancellationToken);
        var tasks = Path.Combine(tree.Root, "tasks.jsonc");
        await File.WriteAllTextAsync(tasks, """{"schemaVersion":2,"defaults":{"backend":"opencode"},"tasks":[{"id":"inspect","kind":"research","base":"main","route":"fixture/model","prompt":"Inspect."}]}""", cancellationToken);
        var missing = Path.Combine(tree.Root, "missing.jsonc");
        using var harness = new HarnessProcess(tree);
        var result = await harness.RunAsync(["run", tasks, "--repo", repo.Root, "--policy", missing], cancellationToken);
        await Assert.That(result.ExitCode).IsEqualTo(2);
        await Assert.That(result.Stderr).Contains("--policy");
        await Assert.That(result.Stderr).Contains(missing);
    }

    [Test]
    [Timeout(180_000)]
    public async Task Committed_policy_is_frozen_with_provenance_and_cli_ledger_override(CancellationToken cancellationToken)
    {
        using var tree = new TempTree();
        var repo = await LabRepo.CreateAsync(tree, cancellationToken);
        var repoPath = Path.Combine(Directory.CreateDirectory(Path.Combine(repo.Root, ".dispatch")).FullName, "config.jsonc");
        var committed = """{"schemaVersion":2,"preferences":{"timeoutMinutes":17},"restrictions":{"deny":[{"name":"no-push","action":"shell","resources":["git push*"]}]},"instructions":["Committed instruction."]}""";
        await File.WriteAllTextAsync(repoPath, committed, cancellationToken);
        await repo.GitAsync(["add", ".dispatch/config.jsonc"], cancellationToken);
        await repo.GitAsync(["commit", "-m", "Configure policy"], cancellationToken);
        var head = (await repo.GitAsync(["rev-parse", "HEAD"], cancellationToken)).Trim();
        await File.WriteAllTextAsync(repoPath, """{"schemaVersion":2,"restrictions":{"deny":[]}}""", cancellationToken);
        var userPath = Path.Combine(Directory.CreateDirectory(Path.Combine(tree.DispatchHome, "config")).FullName, "config.jsonc");
        var ignoredLedger = Path.Combine(tree.Root, "user-ledgers");
        var user = new JsonObject
        {
            ["schemaVersion"] = 2,
            ["preferences"] = new JsonObject { ["backend"] = "opencode", ["maxParallel"] = 2, ["profile"] = "full" },
            ["locations"] = new JsonObject { ["ledgerRoot"] = ignoredLedger }
        };
        await File.WriteAllTextAsync(userPath, user.ToJsonString(), cancellationToken);
        var tasks = Path.Combine(tree.Root, "tasks.jsonc");
        await File.WriteAllTextAsync(tasks, """{"schemaVersion":2,"run":"layered","sharedPaths":["shared.txt"],"defaults":{"timeoutMinutes":19},"tasks":[{"id":"inspect","kind":"research","base":"main","route":"fixture/model","prompt":"Inspect the lab repository."}]}""", cancellationToken);
        var ledgerRoot = Path.Combine(tree.Root, "override-ledgers");
        var environment = FakeOpenCode.Environment(tree, "one-task.jsonl").ToDictionary(p => p.Key, p => (string?)p.Value);
        var scenario = Path.Combine(tree.Root, "layered.jsonl");
        var steps = File.ReadAllLines(FakeOpenCode.Fixture("one-task.jsonl")).Select(line => JsonNode.Parse(line)!).ToArray();
        steps.Single(s => s["expect"]!.GetValue<string>() == "POST /api/session")["body"]!["permissions"] =
            new JsonArray(new JsonObject { ["action"] = "shell", ["resource"] = "git push*", ["effect"] = "deny" },
                new JsonObject { ["action"] = "edit", ["resource"] = "*", ["effect"] = "deny" },
                new JsonObject { ["action"] = "shell", ["resource"] = "*", ["effect"] = "deny" },
                new JsonObject { ["action"] = "edit", ["resource"] = Path.Combine(tree.DispatchHome, "state", "control", "**").Replace('\\', '/'), ["effect"] = "deny" },
                new JsonObject { ["action"] = "edit", ["resource"] = Path.Combine(ledgerRoot, Commands.CreateRunId("layered"), "**").Replace('\\', '/'), ["effect"] = "deny" });
        await File.WriteAllLinesAsync(scenario, steps.Select(s => s.ToJsonString()), cancellationToken);
        environment["FAKE_OPENCODE_SCENARIO"] = scenario;
        using var harness = new HarnessProcess(tree);
        var result = await harness.RunAsync(["run", tasks, "--repo", repo.Root, "--ledger", ledgerRoot, "--max", "1", "--timeout-minutes", "23"],
            cancellationToken, environment);
        result.RequireSuccess("layered run");
        await Assert.That(result.Stdout).Contains("working copy differs from HEAD");
        await Assert.That(Directory.Exists(ignoredLedger)).IsFalse();
        var ledger = Directory.GetDirectories(ledgerRoot).Single();
        await Assert.That(await File.ReadAllTextAsync(Path.Combine(ledger, ".sources", "repo.jsonc"), cancellationToken)).IsEqualTo(committed);
        var effective = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(ledger, "config.json"), cancellationToken))!;
        var repoSource = effective["sources"]!.AsArray().Single(s => s!["layer"]!.GetValue<string>() == "repo")!;
        await Assert.That(repoSource["head"]!.GetValue<string>()).IsEqualTo(head);
        await Assert.That(repoSource["dirty"]!.GetValue<bool>()).IsTrue();
        await Assert.That(effective["maxParallel"]!["from"]!.GetValue<string>()).IsEqualTo("cli");
        var settings = effective["tasks"]!["inspect"]!["settings"]!;
        await Assert.That(settings["backend"]!["from"]!.GetValue<string>()).IsEqualTo("user");
        await Assert.That(settings["timeoutMinutes"]!["value"]!.GetValue<int>()).IsEqualTo(23);
        await Assert.That(settings["timeoutMinutes"]!["from"]!.GetValue<string>()).IsEqualTo("cli");
        await Assert.That(settings["route"]!["from"]!.GetValue<string>()).IsEqualTo("task");
        var record = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(ledger, "inspect", "task.json"), cancellationToken))!;
        await Assert.That(JsonNode.DeepEquals(record["settings"], settings)).IsTrue();
        var run = JsonNode.Parse(File.ReadLines(Path.Combine(ledger, "events.jsonl")).First())!;
        await Assert.That(run["data"]!["sharedPaths"]!["paths"]![0]!.GetValue<string>()).IsEqualTo("shared.txt");
        await Assert.That(run["data"]!["sharedPaths"]!["mergedBy"]!.GetValue<string>()).IsEqualTo("coordinator");
        var requests = File.ReadLines(Path.Combine(tree.Root, "requests.jsonl")).Select(line => JsonNode.Parse(line)!).ToArray();
        await Assert.That(requests.Any(r => r["body"]?.ToJsonString().Contains("Committed instruction.") == true)).IsTrue();
        await Assert.That(requests.Any(r => r["body"]?.ToJsonString().Contains("git push*") == true)).IsTrue();
    }

    [Test]
    [Arguments("repo")]
    [Arguments("task-file")]
    [Timeout(180_000)]
    public async Task Non_user_backend_config_is_refused_before_any_run_is_created(string layer, CancellationToken cancellationToken)
    {
        using var tree = new TempTree();
        var repo = await LabRepo.CreateAsync(tree, cancellationToken);
        var tasks = Path.Combine(tree.Root, "tasks.jsonc");
        var document = JsonNode.Parse("""{"schemaVersion":2,"defaults":{"backend":"opencode"},"tasks":[{"id":"inspect","kind":"research","base":"main","route":"fixture/model","prompt":"Inspect."}]}""")!;
        var path = tasks;
        if (layer == "repo")
        {
            path = Path.Combine(Directory.CreateDirectory(Path.Combine(repo.Root, ".dispatch")).FullName, "config.jsonc");
            await File.WriteAllTextAsync(path, """{"schemaVersion":2,"backends":{"opencode":{"env":["EXAMPLE_KEY"]}}}""", cancellationToken);
            await repo.GitAsync(["add", ".dispatch"], cancellationToken);
            await repo.GitAsync(["commit", "-m", "Add restricted member"], cancellationToken);
        }
        else document["backends"] = new JsonObject { ["codex"] = new JsonObject { ["env"] = new JsonArray() } };
        await File.WriteAllTextAsync(tasks, document.ToJsonString(), cancellationToken);
        using var harness = new HarnessProcess(tree);
        var result = await harness.RunAsync(["run", tasks, "--repo", repo.Root], cancellationToken);
        await Assert.That(result.ExitCode).IsEqualTo(2);
        await Assert.That(result.Stderr).Contains(path);
        await Assert.That(result.Stderr).Contains(layer);
        await Assert.That(result.Stderr).Contains("backends");
        await Assert.That(Directory.Exists(Path.Combine(tree.DispatchHome, "state", "ledgers"))).IsFalse();
    }
}
