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
    public async Task Aliases_resolve_from_the_harness_for_every_layer()
    {
        using var tree = new TempTree();
        var references = ContractReferences(tree);
        var worker = Path.Combine(references, "worker-contract.md");
        var model = Path.Combine(references, "models", "zeta.md");
        File.WriteAllText(worker, "worker-contract v4\nRules.");
        File.WriteAllText(model, "Coordinator only.\nworker-model zeta v1\nWork.");
        var cache = new Dictionary<string, Prompt.Contract>();
        foreach (var layer in new[] { "task-file", "task", "cli" })
        {
            List<string> aliases = ["@zeta-worker", "@worker-contract", model, "@zeta-worker"];
            var file = new TaskFile { Contracts = layer == "task-file" ? aliases : [] };
            var task = new TaskSpec { Id = "inspect", Contracts = layer == "task" ? aliases : [] };
            var args = Args.Parse(layer == "cli" ? ["plan", .. aliases.SelectMany(a => new[] { "--contract", a })] : ["plan"]);
            var inputs = Prompt.Load(file, task, Path.Combine(tree.Root, "elsewhere", "tasks.jsonc"), args, [], cache, Path.Combine(tree.Root, "harness"));
            await Assert.That(inputs.Contracts.Select(c => c.Record.Alias)).IsEquivalentTo(new string?[] { "@zeta-worker", "@worker-contract" }, CollectionOrdering.Matching);
            await Assert.That(inputs.Contracts.Select(c => c.Record.Path)).IsEquivalentTo(new[] { model, worker }, CollectionOrdering.Matching);
            await Assert.That(inputs.Contracts.Select(c => c.Record.From)).IsEquivalentTo(new[] { layer, layer }, CollectionOrdering.Matching);
            await Assert.That(inputs.Contracts.Select(c => c.Text)).IsEquivalentTo(new[] { "worker-model zeta v1\nWork.", "worker-contract v4\nRules." }, CollectionOrdering.Matching);
        }
    }

    [Test]
    [Arguments("\n")]
    [Arguments("\r\n")]
    public async Task Model_alias_prepends_and_hashes_only_the_worker_block(string newline)
    {
        using var tree = new TempTree();
        var references = ContractReferences(tree);
        var selected = "worker-model zeta v1" + newline + "İyi günler 世界";
        var path = Path.Combine(references, "models", "zeta.md");
        File.WriteAllBytes(path, [0xef, 0xbb, 0xbf, .. Encoding.UTF8.GetBytes("Coordinator preamble" + newline + "inline worker-model decoy" + newline + selected)]);
        var task = new TaskSpec { Id = "inspect", Kind = "research", Contracts = ["@zeta-worker"] };
        var inputs = Prompt.Load(new(), task, Path.Combine(tree.Root, "tasks.jsonc"), null, [], harnessRoot: Path.Combine(tree.Root, "harness"));
        await Assert.That(inputs.Contracts.Count).IsEqualTo(1);
        await Assert.That(inputs.Contracts[0].Text).IsEqualTo(selected);
        await Assert.That(inputs.Contracts[0].Record.Version).IsEqualTo("worker-model zeta v1");
        await Assert.That(inputs.Contracts[0].Record.Sha256).IsEqualTo(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(selected))).ToLowerInvariant());
        var policy = new Policy { Path = "config.json", Deny = [], Escalate = [], Instructions = [] };
        await Assert.That(policy.Render(new(task, "Inspect."), "<workspace>", inputs)).StartsWith(selected + "\n\nStanding instructions");
    }

    [Test]
    public async Task Invalid_aliases_refuse_planning_with_alias_and_lookup_path()
    {
        using var tree = new TempTree();
        var references = ContractReferences(tree);
        var harness = Path.Combine(tree.Root, "harness");
        var absentHarness = Path.Combine(tree.Root, "absent", "harness");
        var absent = Path.Combine(tree.Root, "absent", "delegating-to-worker-models", "references", "worker-contract.md");
        File.WriteAllText(Path.Combine(tree.Root, "@unknown"), "Must not fall back to this file.");
        File.WriteAllText(Path.Combine(references, "models", "unmarked.md"), "inline worker-model decoy\n worker-model indented");
        var cases = new[]
        {
            ("@unknown", harness, $"unknown contract alias @unknown; known: @worker-contract, @<model>-worker (looked in {references})"),
            ("@../worker", harness, $"unknown contract alias @../worker; known: @worker-contract, @<model>-worker (looked in {references})"),
            ("@worker-contract", absentHarness, $"inspect: contract task @worker-contract {absent}: file not found"),
            ("@worker-contract", harness, $"inspect: contract task @worker-contract {Path.Combine(references, "worker-contract.md")}: file not found"),
            ("@missing-worker", harness, $"inspect: contract task @missing-worker {Path.Combine(references, "models", "missing.md")}: file not found"),
            ("@unmarked-worker", harness, $"inspect: contract task @unmarked-worker {Path.Combine(references, "models", "unmarked.md")}: missing column-one worker-model marker"),
        };
        var errors = new List<string>();
        foreach (var (alias, root, _) in cases)
        {
            try { Prompt.Load(new(), new() { Id = "inspect", Contracts = [alias] }, Path.Combine(tree.Root, "tasks.jsonc"), null, [], harnessRoot: root); }
            catch (HarnessException ex) { errors.Add(ex.Message); }
        }
        await Assert.That(errors).IsEquivalentTo(cases.Select(c => c.Item3), CollectionOrdering.Matching);
    }

    static string ContractReferences(TempTree tree)
    {
        var references = Path.Combine(tree.Root, "delegating-to-worker-models", "references");
        Directory.CreateDirectory(Path.Combine(references, "models"));
        return references;
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
