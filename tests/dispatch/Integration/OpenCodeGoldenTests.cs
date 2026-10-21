using System.Text.Json.Nodes;
using Dispatch.Tests.Support;

namespace Dispatch.Tests.Integration;

[Category("Integration")]
public sealed class OpenCodeGoldenTests
{
    [Test]
    [Timeout(180_000)]
    public async Task A_scripted_task_preserves_the_normalized_ledger(CancellationToken cancellationToken)
    {
        using var tree = new TempTree();
        var repo = await LabRepo.CreateAsync(tree, cancellationToken);
        var config = Directory.CreateDirectory(Path.Combine(tree.DispatchHome, "config")).FullName;
        await File.WriteAllTextAsync(Path.Combine(config, "routes.allow.jsonc"), """{"schemaVersion":2,"routes":["fixture/model"]}""", cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(config, "policy.jsonc"), """{"schemaVersion":2,"instructions":["Report findings only."],"deny":[{"name":"no-edits","action":"edit","resources":["*"]}]}""", cancellationToken);
        var tasks = Path.Combine(tree.Root, "tasks.jsonc");
        await File.WriteAllTextAsync(tasks, """{"schemaVersion":2,"run":"golden","tasks":[{"id":"inspect","kind":"research","base":"main","route":"fixture/model","prompt":"Inspect the lab repository."}]}""", cancellationToken);
        using var harness = new HarnessProcess(tree);

        var result = await harness.RunAsync(["run", tasks, "--repo", repo.Root, "--max", "1"], cancellationToken,
            FakeOpenCode.Environment(tree, "one-task.jsonl").ToDictionary(x => x.Key, x => (string?)x.Value));

        if (result.ExitCode != 0 && File.Exists(Path.Combine(tree.Root, "requests.jsonl")))
            TestContext.Current!.Output.WriteLine(await File.ReadAllTextAsync(Path.Combine(tree.Root, "requests.jsonl"), cancellationToken));
        result.RequireSuccess("scripted opencode run");
        var ledger = Directory.GetDirectories(Path.Combine(tree.DispatchHome, "state", "ledgers")).Single();
        var events = File.ReadAllLines(Path.Combine(ledger, "events.jsonl")).Select(line => JsonNode.Parse(line)!.AsObject()).ToArray();
        var requests = File.ReadAllLines(Path.Combine(tree.Root, "requests.jsonl")).Select(line => JsonNode.Parse(line)!.AsObject()).ToArray();
        await Assert.That(requests.All(request => request["authenticated"]!.GetValue<bool>())).IsTrue();
        await Assert.That(requests.Where(request => request["unexpected"]!.GetValue<bool>())).IsEmpty();
        var sentEnvironment = requests.Single(request => request["method"]!.GetValue<string>() == "PUT")["body"]!["variables"]!.AsObject();
        await Assert.That(events.Single(e => e["type"]!.GetValue<string>() == "environment")["data"]!["variables"]!.GetValue<int>()).IsEqualTo(sentEnvironment.Count);
        var normalized = LedgerSnapshot.Normalize(events, tree.Root, Path.GetFileName(ledger), await repo.GitAsync(["rev-parse", "HEAD"], cancellationToken));
        var expected = await File.ReadAllTextAsync(Path.Combine(TestPaths.Tests, "fixtures", "ledgers", "opencode-one-task.jsonl"), cancellationToken);
        await Assert.That(normalized).IsEqualTo(expected);
        var final = await File.ReadAllTextAsync(Path.Combine(ledger, "inspect", "final.md"), cancellationToken);
        await Assert.That(final).Contains("Outcome: succeeded");
        await Assert.That(final).Contains("The lab repository is ready.");
    }
}
