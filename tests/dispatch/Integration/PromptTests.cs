using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Dispatch.Tests.Support;
using TUnit.Assertions.Enums;

namespace Dispatch.Tests.Integration;

[Category("Integration")]
public sealed class PromptTests
{
    [Test]
    [Timeout(180_000)]
    public async Task Explicit_contract_files_are_prepended_whole_and_recorded(CancellationToken cancellationToken)
    {
        using var tree = new TempTree();
        var repo = await LabRepo.CreateAsync(tree, cancellationToken);
        var tasks = Path.Combine(tree.Root, "tasks.jsonc");
        var paths = new[] { Path.Combine(tree.Root, "zulu.md"), Path.Combine(tree.Root, "alpha.md") };
        var texts = new[] { "Contract Z v1\r\nKeep this.\r\n", "Coordinator preamble\nworker-model example v1\nİyi günler 世界" };
        await File.WriteAllBytesAsync(paths[0], [0xef, 0xbb, 0xbf, .. Encoding.UTF8.GetBytes(texts[0])], cancellationToken);
        await File.WriteAllTextAsync(paths[1], texts[1], cancellationToken);
        await File.WriteAllTextAsync(tasks, """{"schemaVersion":2,"contracts":["zulu.md"],"defaults":{"backend":"opencode"},"tasks":[{"id":"inspect","kind":"research","base":"main","route":"fixture/model","contracts":["zulu.md","alpha.md"],"prompt":"Inspect."}]}""", cancellationToken);
        var environment = FakeOpenCode.Environment(tree, "one-task.jsonl").ToDictionary(p => p.Key, p => (string?)p.Value);
        using (var planner = new HarnessProcess(tree))
            (await planner.RunAsync(["plan", tasks, "--repo", repo.Root], cancellationToken, environment)).RequireSuccess("contract plan");
        using var harness = new HarnessProcess(tree);
        (await harness.RunAsync(["run", tasks, "--repo", repo.Root], cancellationToken, environment)).RequireSuccess("contract run");
        var run = Directory.GetDirectories(Path.Combine(tree.DispatchHome, "state", "ledgers")).Single();
        var record = JsonNode.Parse(File.ReadAllText(Path.Combine(run, "inspect", "task.json")))!;
        var frozen = JsonNode.Parse(File.ReadAllText(Path.Combine(run, "config.json")))!;
        foreach (var contracts in new[] { record["contracts"]!, frozen["tasks"]!["inspect"]!["contracts"]! })
        {
            await Assert.That(contracts.AsArray().Count).IsEqualTo(2);
            await Assert.That(contracts.AsArray().Select(c => c!["path"]!.GetValue<string>())).IsEquivalentTo(paths, CollectionOrdering.Matching);
            await Assert.That(contracts.AsArray().Select(c => c!["from"]!.GetValue<string>())).IsEquivalentTo(new[] { "task-file", "task" }, CollectionOrdering.Matching);
            await Assert.That(contracts.AsArray().Select(c => c!["version"]!.GetValue<string>())).IsEquivalentTo(new[] { "Contract Z v1", "Coordinator preamble" }, CollectionOrdering.Matching);
            for (var i = 0; i < 2; i++)
            {
                await Assert.That(contracts[i]!.AsObject().ContainsKey("alias")).IsFalse();
                await Assert.That(contracts[i]!["sha256"]!.GetValue<string>()).IsEqualTo(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(paths[i]))).ToLowerInvariant());
            }
        }
        var requests = File.ReadLines(FakeOpenCode.Record(tree)).Select(line => JsonNode.Parse(line)!).ToArray();
        var prompt = requests.Single(r => r["path"]!.GetValue<string>().EndsWith("/prompt", StringComparison.Ordinal))["body"]!["text"]!.GetValue<string>();
        var workspace = record["workspace"]!["path"]!.GetValue<string>();
        var expected = texts[0] + "\n" + texts[1] + "\n\nStanding instructions from the dispatch harness (they apply to the whole task):\n"
            + $"- Your working folder: {workspace}\n"
            + "- Declared scope: (none declared). Changes outside it are flagged for review.\n"
            + "- This is a read-only task: do not create, change or delete files. Report your findings instead.\n"
            + "- Nobody answers questions during this run: a question is cancelled, so continue without the answer.\n"
            + "- End your final message with exactly one status line: `Status: done` when the task is done, "
            + "`Status: stopped` when you stopped on a rule or a contradiction, "
            + "`Status: blocked` when the environment kept you from the work (a missing tool, access or input).\n\n---\nTask:\n\nInspect.\n";
        await Assert.That(prompt).IsEqualTo(expected);
    }

    [Test]
    [Arguments("task-file")]
    [Arguments("task")]
    [Arguments("cli")]
    [Timeout(180_000)]
    public async Task At_contract_entries_refuse_planning(string layer, CancellationToken ct)
    {
        using var tree = new TempTree();
        var repo = await LabRepo.CreateAsync(tree, ct);
        var tasks = Path.Combine(tree.Root, "tasks.jsonc");
        var document = JsonNode.Parse("""{"schemaVersion":2,"defaults":{"backend":"opencode"},"tasks":[{"id":"zulu","kind":"research","base":"main","route":"fixture/model","prompt":"Inspect."},{"id":"alpha","kind":"research","base":"main","route":"fixture/model","prompt":"Inspect."}]}""")!;
        const string entry = "@worker-contract";
        File.WriteAllText(Path.Combine(tree.Root, entry), "Must not fall back to this file.");
        if (layer == "task-file") document["contracts"] = new JsonArray(entry);
        if (layer == "task")
            foreach (var task in document["tasks"]!.AsArray()) task!["contracts"] = new JsonArray(entry);
        File.WriteAllText(tasks, document.ToJsonString());
        using var process = new HarnessProcess(tree);
        var result = await process.RunAsync(["plan", tasks, "--repo", repo.Root, .. layer == "cli" ? new[] { "--contract", entry } : []], ct,
            FakeOpenCode.Environment(tree, "one-task.jsonl").ToDictionary(x => x.Key, x => (string?)x.Value));
        await Assert.That(result.ExitCode).IsEqualTo(2);
        var errors = result.Stdout.Split('\n').Select(line => line.TrimEnd('\r')).Where(line => line.StartsWith("ERROR ", StringComparison.Ordinal));
        await Assert.That(errors).IsEquivalentTo(layer == "cli" ? [] : new[] {
            "ERROR zulu: contract aliases were removed; name the file path: @worker-contract",
            "ERROR alpha: contract aliases were removed; name the file path: @worker-contract" }, CollectionOrdering.Matching);
        await Assert.That(result.Stderr).IsEqualTo(layer == "cli" ? "dispatch: contract aliases were removed; name the file path: @worker-contract" + Environment.NewLine : "");
        var requests = File.Exists(FakeOpenCode.Record(tree)) ? File.ReadLines(FakeOpenCode.Record(tree)).Select(line => JsonNode.Parse(line)!).ToArray() : [];
        await Assert.That(requests.Where(r => r["path"]!.GetValue<string>().EndsWith("/prompt", StringComparison.Ordinal))).IsEmpty();
    }

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
        var requests = File.ReadLines(FakeOpenCode.Record(tree)).Select(line => JsonNode.Parse(line)!).ToArray();
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
