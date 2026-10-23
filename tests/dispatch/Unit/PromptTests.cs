using System.Text;

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
