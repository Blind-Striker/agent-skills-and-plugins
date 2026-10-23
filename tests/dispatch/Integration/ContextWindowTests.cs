using System.Text.Json.Nodes;
using Dispatch.Tests.Support;

namespace Dispatch.Tests.Integration;

[Category("Integration")]
public sealed class ContextWindowTests
{
    [Test]
    [Arguments("model", true)]
    [Arguments("ran-model", true)]
    [Arguments("ran-model", false)]
    [Timeout(180_000)]
    public async Task OpenCode_normalizes_prompt_tokens_and_records_the_ran_models_window(string ranModel, bool listed, CancellationToken cancellationToken)
    {
        using var tree = new TempTree();
        var repo = await LabRepo.CreateAsync(tree, cancellationToken);
        var config = Directory.CreateDirectory(Path.Combine(tree.DispatchHome, "config")).FullName;
        await File.WriteAllTextAsync(Path.Combine(config, "config.jsonc"), """{"schemaVersion":2,"preferences":{"backend":"opencode"}}""", cancellationToken);
        var tasks = Path.Combine(tree.Root, "tasks.jsonc");
        await File.WriteAllTextAsync(tasks, """{"schemaVersion":2,"run":"usage-window","tasks":[{"id":"inspect","kind":"research","base":"main","route":"fixture/model","prompt":"Inspect the lab repository."}]}""", cancellationToken);
        var steps = File.ReadAllLines(FakeOpenCode.Fixture("one-task.jsonl")).Select(line => JsonNode.Parse(line)!).ToArray();
        var models = steps.Single(step => step["expect"]!.GetValue<string>() == "GET /api/model")["reply"]!["data"]!.AsArray();
        if (ranModel != "model" && listed)
        {
            var ran = models[0]!.DeepClone();
            ran["id"] = ranModel;
            ran["modelID"] = ranModel;
            ran["limit"]!["context"] = 128000;
            models.Add(ran);
        }
        var messages = steps.Single(step => step["expect"]!.GetValue<string>() == "GET /api/session/ses_fixture/message")["reply"]!["data"]!.AsArray();
        messages.Single(message => message!["type"]!.GetValue<string>() == "assistant")!["model"]!["id"] = ranModel;
        steps.Single(step => step["expect"]!.GetValue<string>() == "POST /api/session/ses_fixture/permission/per_fixture/reply")["body"]
            = new JsonObject { ["decision"] = "reject", ["message"] = Policy.LeadDeclined };
        var scenario = Path.Combine(tree.Root, "usage-window.jsonl");
        await File.WriteAllLinesAsync(scenario, steps.Select(step => step.ToJsonString()), cancellationToken);
        var environment = FakeOpenCode.Environment(tree, "one-task.jsonl").ToDictionary(x => x.Key, x => (string?)x.Value);
        environment["FAKE_OPENCODE_SCENARIO"] = scenario;
        using var harness = new HarnessProcess(tree);
        var execution = await harness.RunAsync(["run", tasks, "--repo", repo.Root], cancellationToken, environment);
        await Assert.That(execution.ExitCode).IsEqualTo(ranModel == "model" ? 0 : 1);
        var ledger = Directory.GetDirectories(Path.Combine(tree.DispatchHome, "state", "ledgers")).Single();
        var result = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(ledger, "inspect", "result.json"), cancellationToken))!;
        await Assert.That(result["tokens"]!["input"]!.GetValue<double>()).IsEqualTo(12 + 3 + 1);
        await Assert.That(result["tokens"]!["cacheRead"]!.GetValue<double>()).IsEqualTo(3);
        await Assert.That(result["tokens"]!["cacheWrite"]!.GetValue<double>()).IsEqualTo(1);
        await Assert.That(result["routesUsed"]!.AsArray().Select(x => x!.GetValue<string>()).ToArray())
            .IsEquivalentTo(new[] { $"fixture/{ranModel}#default" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(result["failures"]!.AsArray().Select(x => x!.GetValue<string>()).ToArray())
            .IsEquivalentTo(ranModel == "model" ? Array.Empty<string>() : ["route-drift"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(result["contextWindow"]?.GetValue<long>()).IsEqualTo(listed ? ranModel == "model" ? 8192L : 128000L : (long?)null);
        var usage = File.ReadAllLines(Path.Combine(ledger, "events.jsonl")).Select(line => JsonNode.Parse(line)!)
            .Where(line => line["type"]!.GetValue<string>() == "usage").ToArray();
        await Assert.That(usage.Select(line => line["data"]!.ToJsonString()).ToArray()).IsEquivalentTo(new[]
        {
            """{"input":16,"cacheRead":3,"output":7,"reasoning":2}""",
        }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    [Timeout(180_000)]
    public async Task Codex_records_the_last_non_null_task_context_window(CancellationToken cancellationToken)
    {
        var result = await RunCodexAsync("""
            {"notify":"thread/tokenUsage/updated","params":{"threadId":"$threadId","turnId":"$turnId","tokenUsage":{"total":{"inputTokens":10},"modelContextWindow":128000}}}
            {"notify":"thread/tokenUsage/updated","params":{"threadId":"$threadId","turnId":"$turnId","tokenUsage":{"total":{"inputTokens":20},"modelContextWindow":272000}}}
            {"notify":"thread/tokenUsage/updated","params":{"threadId":"$threadId","turnId":"$turnId","tokenUsage":{"total":{"inputTokens":30}}}}
            {"notify":"thread/tokenUsage/updated","params":{"threadId":"$otherThreadId","turnId":"child-turn","tokenUsage":{"total":{"inputTokens":5},"modelContextWindow":64000}}}
            {"notify":"thread/tokenUsage/updated","params":{"threadId":"$threadId","turnId":"old-turn","tokenUsage":{"total":{"inputTokens":99},"modelContextWindow":32000}}}
            """, cancellationToken);
        await Assert.That(result["contextWindow"]?.GetValue<long>()).IsEqualTo(272000);
    }

    [Test]
    [Timeout(180_000)]
    public async Task Codex_omits_context_window_when_token_usage_reports_none(CancellationToken cancellationToken)
    {
        var result = await RunCodexAsync("""
            {"notify":"thread/tokenUsage/updated","params":{"threadId":"$threadId","turnId":"$turnId","tokenUsage":{"total":{"inputTokens":10,"outputTokens":2}}}}
            """, cancellationToken);
        await Assert.That(result.ContainsKey("contextWindow")).IsFalse();
    }

    static async Task<JsonObject> RunCodexAsync(string usage, CancellationToken cancellationToken)
    {
        using var lab = new CodexLab();
        var repo = await LabRepo.CreateAsync(lab.Tree, cancellationToken);
        lab.CodeTask();
        lab.Write("task", CodexLab.Handshake + CodexLab.Thread + usage + "\n" + CodexLab.Final);
        using var harness = new HarnessProcess(lab.Tree);
        (await harness.RunAsync(["run", lab.Tasks, "--repo", repo.Root], cancellationToken,
            lab.Environment.ToDictionary(x => x.Key, x => (string?)x.Value))).RequireSuccess("Codex context-window run");
        await Assert.That(lab.Result["state"]!.GetValue<string>()).IsEqualTo("done");
        return lab.Result;
    }

    [Test]
    [Timeout(180_000)]
    public async Task OpenCode_records_the_catalog_context_window(CancellationToken cancellationToken)
    {
        using var tree = new TempTree();
        var repo = await LabRepo.CreateAsync(tree, cancellationToken);
        var config = Directory.CreateDirectory(Path.Combine(tree.DispatchHome, "config")).FullName;
        await File.WriteAllTextAsync(Path.Combine(config, "config.jsonc"), """{"schemaVersion":2,"preferences":{"backend":"opencode"}}""", cancellationToken);
        var tasks = Path.Combine(tree.Root, "tasks.jsonc");
        await File.WriteAllTextAsync(tasks, """{"schemaVersion":2,"run":"context-window","tasks":[{"id":"inspect","kind":"research","base":"main","route":"fixture/model","prompt":"Inspect the lab repository."}]}""", cancellationToken);
        var steps = File.ReadAllLines(FakeOpenCode.Fixture("one-task.jsonl")).Select(line => JsonNode.Parse(line)!).ToArray();
        steps.Single(step => step["expect"]!.GetValue<string>() == "GET /api/model")["reply"]!["data"]![0]!["limit"]!["context"] = 1050000;
        steps.Single(step => step["expect"]!.GetValue<string>() == "POST /api/session/ses_fixture/permission/per_fixture/reply")["body"]
            = new JsonObject { ["decision"] = "reject", ["message"] = Policy.LeadDeclined };
        var scenario = Path.Combine(tree.Root, "context-window.jsonl");
        await File.WriteAllLinesAsync(scenario, steps.Select(step => step.ToJsonString()), cancellationToken);
        var environment = FakeOpenCode.Environment(tree, "one-task.jsonl").ToDictionary(x => x.Key, x => (string?)x.Value);
        environment["FAKE_OPENCODE_SCENARIO"] = scenario;
        using var harness = new HarnessProcess(tree);
        (await harness.RunAsync(["run", tasks, "--repo", repo.Root], cancellationToken, environment)).RequireSuccess("OpenCode context-window run");
        var ledger = Directory.GetDirectories(Path.Combine(tree.DispatchHome, "state", "ledgers")).Single();
        var result = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(ledger, "inspect", "result.json"), cancellationToken))!.AsObject();
        await Assert.That(result["state"]!.GetValue<string>()).IsEqualTo("done");
        await Assert.That(result["contextWindow"]?.GetValue<long>()).IsEqualTo(1050000);
    }
}
