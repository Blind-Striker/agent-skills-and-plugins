using System.Text.Json.Nodes;

namespace Dispatch.Tests.Support;

internal sealed class StructuredLab : IDisposable
{
    public TempTree Tree { get; } = new();
    readonly CodexLab? codex;
    public Dictionary<string, string?> Environment { get; }
    public string Tasks => Path.Combine(Tree.Root, "tasks.jsonc");
    public string RunFolder => Directory.GetDirectories(Path.Combine(Tree.DispatchHome, "state", "ledgers")).Single();
    public string TaskFolder => Path.Combine(RunFolder, "answer");
    public JsonObject Result => JsonNode.Parse(File.ReadAllText(Path.Combine(TaskFolder, "result.json")))!.AsObject();
    public const string Text = "İyi günler, ışık 世界 😀";
    public const string Valid = """{"items":[{"id":"i1","key":"title","text":"İyi günler, ışık 世界 😀","details":{"value":null}}]}""";
    public const string Schema = """
        {"type":"object","required":["items"],"additionalProperties":false,"properties":{"items":{"type":"array","items":{"type":"object","required":["id","key","text","details"],"additionalProperties":false,"properties":{"id":{"type":"string"},"key":{"type":"string"},"text":{"type":"string"},"details":{"type":"object","additionalProperties":{"type":["string","null"]}}}}}}}
        """;

    public StructuredLab(string backend, string? final = Valid, string outcome = "succeeded")
    {
        if (backend == "codex")
        {
            codex = new(Tree);
            Environment = codex.Environment.ToDictionary(x => x.Key, x => (string?)x.Value);
            var end = final is null ? "" : new JsonObject
            {
                ["notify"] = "item/completed", ["params"] = new JsonObject
                {
                    ["threadId"] = "$threadId", ["turnId"] = "$turnId", ["item"] = new JsonObject
                    { ["id"] = "answer", ["type"] = "agentMessage", ["phase"] = "final_answer", ["text"] = final },
                },
            }.ToJsonString() + "\n";
            end += outcome == "lost" ? "{\"exit\":17}" : outcome == "timeout" ? """
                {"expect":"turn/interrupt","reply":{}}
                {"notify":"turn/completed","params":{"threadId":"$threadId","turn":{"id":"$turnId","status":"interrupted"}}}
                """ : """
                {"notify":"turn/completed","params":{"threadId":"$threadId","turn":{"id":"$turnId","status":"completed"}}}
                """;
            codex.Write("task", CodexLab.Handshake + CodexLab.Thread.Replace("workspaceWrite", "readOnly") + end);
        }
        else
        {
            Environment = FakeOpenCode.Environment(Tree, "one-task.jsonl").ToDictionary(x => x.Key, x => (string?)x.Value);
            var steps = File.ReadLines(FakeOpenCode.Fixture("one-task.jsonl")).Select(x => JsonNode.Parse(x)!.AsObject()).ToList();
            var messages = steps.Single(x => x["expect"]!.GetValue<string>().EndsWith("/message", StringComparison.Ordinal));
            messages["repeat"] = true;
            var answer = messages["reply"]!["data"]!.AsArray();
            if (final is null) answer.RemoveAt(1);
            else answer[1]!["content"]![0]!["text"] = final;
            if (outcome == "timeout")
            {
                steps.Single(x => x["expect"]!.GetValue<string>() == "GET /api/form").Remove("events");
                var session = steps.Single(x => x["expect"]!.GetValue<string>() == "GET /api/session/ses_fixture");
                var index = steps.IndexOf(session);
                for (var i = 0; i < 2; i++)
                {
                    var busy = session.DeepClone().AsObject();
                    busy.Remove("repeat");
                    busy["reply"]!["data"]!.AsObject().Remove("outcome");
                    busy["reply"]!["data"]!["time"]!["idle"] = 0;
                    steps.Insert(index + i, busy);
                }
                session["reply"]!["data"]!["outcome"] = "interrupted";
                steps.Add(new JsonObject
                {
                    ["expect"] = "POST /api/session/ses_fixture/interrupt", ["repeat"] = true,
                    ["reply"] = new JsonObject { ["interrupted"] = true },
                    ["events"] = new JsonArray(OpenCodeLab.Event("session.execution.interrupted", new() { ["sessionID"] = "ses_fixture" })),
                });
            }
            var scenario = Path.Combine(Tree.Root, "structured.jsonl");
            File.WriteAllLines(scenario, steps.Select(x => x.ToJsonString()));
            Environment["FAKE_OPENCODE_SCENARIO"] = scenario;
        }
        File.WriteAllText(Path.Combine(Tree.Root, "schema.json"), Schema);
        File.WriteAllText(Tasks, new JsonObject
        {
            ["schemaVersion"] = 2, ["run"] = "structured-run", ["tasks"] = new JsonArray(new JsonObject
            {
                ["id"] = "answer", ["kind"] = "structured", ["backend"] = backend,
                ["route"] = backend == "codex" ? "model-y#high" : "fixture/model", ["project"] = "on", ["base"] = "main",
                ["prompt"] = "Return each item's text unchanged.",
                ["input"] = new JsonObject { ["items"] = JsonNode.Parse(Valid)!["items"]!.DeepClone() },
                ["result"] = new JsonObject { ["schema"] = "schema.json", ["itemsAt"] = "/items", ["immutable"] = new JsonArray("key") },
            }),
        }.ToJsonString());
    }

    public JsonObject[] CodexInput() => codex!.Input();
    public void Dispose() { codex?.Dispose(); Tree.Dispose(); }
}
