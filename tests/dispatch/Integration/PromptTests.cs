using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Dispatch.Tests.Support;

namespace Dispatch.Tests.Integration;

[Category("Integration")]
public sealed class PromptTests
{
    [Test]
    [Timeout(180_000)]
    public async Task Contracts_are_ordered_deduplicated_and_recorded_with_prompt_hash(CancellationToken cancellationToken)
    {
        using var tree = new TempTree();
        var repo = await LabRepo.CreateAsync(tree, cancellationToken);
        var directory = Directory.CreateDirectory(Path.Combine(tree.Root, "tasks")).FullName;
        var first = Path.Combine(directory, "first.txt");
        var second = Path.Combine(directory, "second.txt");
        var third = Path.Combine(directory, "third.txt");
        await File.WriteAllBytesAsync(first, [0xef, 0xbb, 0xbf, .. Encoding.UTF8.GetBytes("Contract one\r\nKeep this.\r\n")], cancellationToken);
        await File.WriteAllTextAsync(second, "Contract two", cancellationToken);
        await File.WriteAllTextAsync(third, "Contract three\n", cancellationToken);
        var tasks = Path.Combine(directory, "tasks.jsonc");
        await File.WriteAllTextAsync(tasks, """{"schemaVersion":2,"run":"contracts","contracts":["first.txt"],"policy":{"deny":[{"name":"read-only","action":"edit","resources":["*"]}]},"defaults":{"backend":"opencode"},"tasks":[{"id":"inspect","kind":"research","base":"main","route":"fixture/model","contracts":["first.txt","second.txt"],"prompt":"Inspect."}]}""", cancellationToken);
        var environment = FakeOpenCode.Environment(tree, "one-task.jsonl").ToDictionary(p => p.Key, p => (string?)p.Value);
        using var harness = new HarnessProcess(tree);
        var result = await harness.RunAsync(["run", tasks, "--repo", repo.Root, "--contract", third, "--contract", second], cancellationToken, environment);
        result.RequireSuccess("contract run");
        var requests = File.ReadLines(Path.Combine(tree.Root, "requests.jsonl")).Select(line => JsonNode.Parse(line)!).ToArray();
        var prompt = requests.Single(r => r["path"]!.GetValue<string>().EndsWith("/prompt", StringComparison.Ordinal))["body"]!["text"]!.GetValue<string>();
        await Assert.That(prompt).StartsWith("Contract one\r\nKeep this.\r\n\nContract two\n\nContract three\n\nStanding instructions");
        var run = Directory.GetDirectories(Path.Combine(tree.DispatchHome, "state", "ledgers")).Single();
        var record = JsonNode.Parse(File.ReadAllText(Path.Combine(run, "inspect", "task.json")))!;
        await Assert.That(record["contracts"]!.AsArray().Count).IsEqualTo(3);
        var data = File.ReadLines(Path.Combine(run, "events.jsonl")).Select(line => JsonNode.Parse(line)!).Single(e => e["type"]!.GetValue<string>() == "prompt")["data"]!;
        await Assert.That(data["chars"]!.GetValue<int>()).IsEqualTo(prompt.Length);
        await Assert.That(data["turn"]!.GetValue<string>()).IsNotEmpty();
        await Assert.That(data["sha256"]!.GetValue<string>()).IsEqualTo(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(prompt))).ToLowerInvariant());
        var paths = new[] { first, second, third };
        for (var i = 0; i < paths.Length; i++)
            await Assert.That(data["contracts"]![i]!.GetValue<string>()).IsEqualTo(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(paths[i]))).ToLowerInvariant());
    }
}
