using System.Text.Json.Nodes;
using Dispatch.Tests.Support;

namespace Dispatch.Tests.Integration;

[Category("Integration")]
[NotInParallel("processes")]
public sealed class OpenCodeGoldenTests
{
    [Test]
    [Timeout(180_000)]
    public async Task A_scripted_task_preserves_the_normalized_ledger(CancellationToken cancellationToken)
    {
        using var tree = new TempTree();
        var repo = await LabRepo.CreateAsync(tree, cancellationToken);
        var config = Directory.CreateDirectory(Path.Combine(tree.DispatchHome, "config")).FullName;
        await File.WriteAllTextAsync(Path.Combine(config, "config.jsonc"), """{"schemaVersion":2,"preferences":{"backend":"opencode"},"restrictions":{"routes":["opencode:fixture/model"],"deny":[{"name":"no-edits","action":"edit","resources":["*"]}]},"instructions":["Report findings only."]}""", cancellationToken);
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
        var permissions = requests.Single(request => request["method"]!.GetValue<string>() == "POST" && request["path"]!.GetValue<string>() == "/api/session")["body"]!["permissions"]!.AsArray();
        await Assert.That(permissions.Count).IsEqualTo(4);
        await Assert.That(permissions[0]!["resource"]!.GetValue<string>()).IsEqualTo("*");
        await Assert.That(permissions[^2]!["resource"]!.GetValue<string>()).IsEqualTo(Path.Combine(tree.DispatchHome, "state", "control", "**").Replace('\\', '/'));
        await Assert.That(permissions[^1]!["resource"]!.GetValue<string>()).IsEqualTo(Path.Combine(ledger, "**").Replace('\\', '/'));
        var sentEnvironment = requests.Single(request => request["method"]!.GetValue<string>() == "PUT")["body"]!["variables"]!.AsObject();
        await Assert.That(events.Single(e => e["type"]!.GetValue<string>() == "environment")["data"]!["variables"]!.GetValue<int>()).IsEqualTo(sentEnvironment.Count);
        var promptText = requests.Single(request => request["path"]!.GetValue<string>().EndsWith("/prompt", StringComparison.Ordinal))["body"]!["text"]!.GetValue<string>();
        var promptEvent = events.Single(e => e["type"]!.GetValue<string>() == "prompt")["data"]!;
        await Assert.That(promptEvent["chars"]!.GetValue<int>()).IsEqualTo(promptText.Length);
        await Assert.That(promptEvent["sha256"]!.GetValue<string>()).IsEqualTo(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(promptText))).ToLowerInvariant());
        var normalized = LedgerSnapshot.Normalize(events, tree.Root, Path.GetFileName(ledger), await repo.GitAsync(["rev-parse", "HEAD"], cancellationToken));
        var expected = await File.ReadAllTextAsync(Path.Combine(TestPaths.Tests, "fixtures", "ledgers", "opencode-one-task.jsonl"), cancellationToken);
        await Assert.That(normalized).IsEqualTo(expected);
        var final = await File.ReadAllTextAsync(Path.Combine(ledger, "inspect", "final.md"), cancellationToken);
        await Assert.That(final).Contains("Outcome: succeeded");
        await Assert.That(final).Contains("The lab repository is ready.");
    }
}
