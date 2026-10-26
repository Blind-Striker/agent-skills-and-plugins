using System.Security.Cryptography;
using System.Text;
using Dispatch.Tests.Support;
using TUnit.Assertions.Enums;

namespace Dispatch.Tests.Unit;

[Category("Unit")]
public sealed class PromptTests
{
    [Test]
    public async Task Contracts_cannot_silently_disappear_when_inputs_are_unavailable()
    {
        var policy = new Policy { Path = "config.json", Deny = [], Escalate = [], Instructions = [] };
        var spec = new TaskSpec { Kind = "code", Contracts = ["contract.md"] };
        await Assert.That(() => policy.Render(new(spec, "Inspect."), "<workspace>"))
            .Throws<HarnessException>();
    }

    [Test]
    public async Task Contract_paths_preserve_layer_order_and_first_occurrence()
    {
        using var tree = new TempTree();
        var paths = new[] { "zulu.md", "middle.md", "alpha.md" }.Select(name => Path.Combine(tree.Root, name)).ToArray();
        var texts = new[] { "Contract Z\nLast alphabetically.", "Contract M\nMiddle.", "Contract A\nFirst alphabetically." };
        for (var i = 0; i < paths.Length; i++) File.WriteAllText(paths[i], texts[i]);
        var cache = new Dictionary<string, Prompt.Contract>();
        var file = new TaskFile { Contracts = ["zulu.md"] };
        var task = new TaskSpec { Id = "inspect", Contracts = [paths[0], "middle.md"] };
        var args = Args.Parse(["plan", "--contract", paths[1], "--contract", paths[2], "--contract", paths[0]]);
        var inputs = Prompt.Load(file, task, Path.Combine(tree.Root, "tasks.jsonc"), args, [], cache);
        await Assert.That(inputs.Contracts.Select(c => c.Record.Path)).IsEquivalentTo(paths, CollectionOrdering.Matching);
        await Assert.That(inputs.Contracts.Select(c => c.Record.From)).IsEquivalentTo(new[] { "task-file", "task", "cli" }, CollectionOrdering.Matching);
        await Assert.That(inputs.Contracts.Select(c => c.Text)).IsEquivalentTo(texts, CollectionOrdering.Matching);
        File.WriteAllText(paths[0], "Changed after first load.");
        var cached = Prompt.Load(new(), new() { Contracts = paths.ToList() }, Path.Combine(tree.Root, "tasks.jsonc"), null, [], cache);
        await Assert.That(cached.Contracts.Select(c => c.Record.Path)).IsEquivalentTo(paths, CollectionOrdering.Matching);
        await Assert.That(cached.Contracts.Select(c => c.Record.From)).IsEquivalentTo(new[] { "task", "task", "task" }, CollectionOrdering.Matching);
        await Assert.That(cached.Contracts.Select(c => c.Text)).IsEquivalentTo(texts, CollectionOrdering.Matching);
    }

    [Test]
    [Arguments("\n")]
    [Arguments("\r\n")]
    public async Task Contract_prepends_the_whole_file_and_hashes_original_bytes(string newline)
    {
        using var tree = new TempTree();
        var text = "Coordinator preamble \t" + newline + "inline worker-model decoy" + newline + "worker-model zeta v1" + newline + "İyi günler 世界";
        var path = Path.Combine(tree.Root, "contract.md");
        byte[] bytes = [0xef, 0xbb, 0xbf, .. Encoding.UTF8.GetBytes(text)];
        File.WriteAllBytes(path, bytes);
        var task = new TaskSpec { Id = "inspect", Kind = "research", Contracts = [path] };
        var inputs = Prompt.Load(new(), task, Path.Combine(tree.Root, "tasks.jsonc"), null, []);
        await Assert.That(inputs.Contracts.Count).IsEqualTo(1);
        await Assert.That(inputs.Contracts[0].Text).IsEqualTo(text);
        await Assert.That(inputs.Contracts[0].Record.Version).IsEqualTo("Coordinator preamble");
        await Assert.That(inputs.Contracts[0].Record.Sha256).IsEqualTo(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
        var policy = new Policy { Path = "config.json", Deny = [], Escalate = [], Instructions = [] };
        await Assert.That(policy.Render(new(task, "Inspect."), "<workspace>", inputs)).StartsWith(text + "\n\nStanding instructions");
        File.WriteAllText(path, new string('V', 125) + " \t" + newline + "Body.");
        var capped = Prompt.Load(new(), task, Path.Combine(tree.Root, "tasks.jsonc"), null, []);
        await Assert.That(capped.Contracts[0].Record.Version).IsEqualTo(new string('V', 120));
    }

    [Test]
    public async Task At_contract_entries_refuse_before_file_or_cache_lookup()
    {
        using var tree = new TempTree();
        var errors = new List<string>();
        foreach (var layer in new[] { "task-file", "task", "cli" })
        {
            foreach (var entry in new[] { "@worker-contract", "@zeta-worker", "@unknown" })
            {
                var path = Path.Combine(tree.Root, entry);
                File.WriteAllText(path, "Must not fall back to this file.");
                var cache = new Dictionary<string, Prompt.Contract> { [path] = new(new() { Path = path }, "Cached content.") };
                var file = new TaskFile { Contracts = layer == "task-file" ? [entry] : [] };
                var task = new TaskSpec { Id = "inspect", Contracts = layer == "task" ? [entry] : [] };
                var args = Args.Parse(layer == "cli" ? ["plan", "--contract", entry] : ["plan"]);
                try { Prompt.Load(file, task, Path.Combine(tree.Root, "tasks.jsonc"), args, [], cache); }
                catch (HarnessException ex) { errors.Add(ex.Message); }
            }
        }
        await Assert.That(errors).IsEquivalentTo(Enumerable.Repeat(new[] {
            "contract aliases were removed; name the file path: @worker-contract",
            "contract aliases were removed; name the file path: @zeta-worker",
            "contract aliases were removed; name the file path: @unknown" }, 3).SelectMany(x => x), CollectionOrdering.Matching);
    }

    [Test]
    public async Task Prompt_ByteLayout()
    {
        var policy = new Policy { Path = "config.json", Deny = [], Escalate = [], Instructions = ["Keep the report short."] };
        var spec = new TaskSpec { Kind = "research", Access = "read-only" };
        var actual = policy.Render(new(spec, "Inspect."), "<workspace>");
        var expected = "Standing instructions from the dispatch harness (they apply to the whole task):\n"
            + "- Your working folder: <workspace>\n"
            + "- Declared scope: (none declared). Changes outside it are flagged for review.\n"
            + "- This is a read-only task: do not create, change or delete files. Report your findings instead.\n"
            + "- Nobody answers questions during this run: a question is cancelled, so continue without the answer.\n"
            + "- End your final message with exactly one status line: `Status: done` when the task is done, "
            + "`Status: stopped` when you stopped on a rule or a contradiction, "
            + "`Status: blocked` when the environment kept you from the work (a missing tool, access or input).\n"
            + "- Keep the report short.\n\n---\nTask:\n\nInspect.\n";
        await Assert.That(Encoding.UTF8.GetBytes(actual).SequenceEqual(Encoding.UTF8.GetBytes(expected))).IsTrue();
        var contracts = new Prompt.Inputs([new(new() { Path = "contract.md" }, "Contract v1\r\nKeep this.\r\n"), new(new() { Path = "other.md" }, "No final newline")]);
        var withContracts = policy.Render(new(spec, "Inspect."), "<workspace>", contracts);
        await Assert.That(Encoding.UTF8.GetBytes(withContracts).SequenceEqual(Encoding.UTF8.GetBytes("Contract v1\r\nKeep this.\r\n\nNo final newline\n\n" + expected))).IsTrue();
    }

    [Test]
    public async Task Structured_prompt_has_pretty_JSON_and_schema_with_safe_fences()
    {
        var policy = new Policy { Path = "config.json", Deny = [], Escalate = [], Instructions = [] };
        var spec = new TaskSpec { Kind = "structured", Input = new() { IdField = "key", Items = System.Text.Json.Nodes.JsonNode.Parse("[{\"key\":\"i1\",\"text\":\"café ```\"}]")!.AsArray() }, Result = new() { ItemsAt = "/answers" } };
        var inputs = new Prompt.Inputs([], spec.Input.Items, System.Text.Json.Nodes.JsonNode.Parse("{\"type\":\"object\"}"));
        var actual = policy.Render(new(spec, "Translate."), "<workspace>", inputs);
        var expected = "Standing instructions from the dispatch harness (they apply to the whole task):\n"
            + "- Your working folder: <workspace>\n"
            + "- Nobody answers questions during this run: a question is cancelled, so continue without the answer.\n\n---\nTask:\n\nTranslate.\n"
            + "\nInput items (JSON; the field `key` identifies each item):\n````json\n[\n  {\n    \"key\": \"i1\",\n    \"text\": \"café ```\"\n  }\n]\n````\n"
            + "\nResult schema (JSON Schema):\n```json\n{\n  \"type\": \"object\"\n}\n```\n"
            + "\nAnswer with exactly one JSON document that matches the result schema, with the answers in the array at `/answers`, and nothing else.\n";
        await Assert.That(Encoding.UTF8.GetBytes(actual).SequenceEqual(Encoding.UTF8.GetBytes(expected))).IsTrue();
    }

    [Test]
    public async Task Raw_prompt_refuses_instructions()
    {
        var policy = new Policy { Path = "config.json", Deny = [], Escalate = [], Instructions = ["Retain this restriction."], RawPrompt = true };
        await Assert.That(() => policy.Render(new(new TaskSpec { Kind = "code" }, "Task."), "<workspace>"))
            .Throws<HarnessException>();
        var raw = new Policy { Path = "config.json", Deny = [], Escalate = [], Instructions = [], RawPrompt = true };
        await Assert.That(() => raw.Render(new(new TaskSpec { Kind = "code", Contracts = ["contract.md"] }, "Task."), "<workspace>"))
            .Throws<HarnessException>();
        await Assert.That(raw.Render(new(new TaskSpec { Kind = "code" }, "Task."), "<workspace>")).IsEqualTo("Task.\n");
    }
}
