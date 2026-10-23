using System.Text.Json.Nodes;
using Dispatch.Tests.Support;
using TUnit.Assertions.Enums;

namespace Dispatch.Tests.Integration;

[Category("Integration")]
[NotInParallel]
public sealed class ToolProgressTests
{
    [Test]
    [Timeout(30_000)]
    public async Task Status_shows_each_running_tasks_tool_count_and_last_completion_time(CancellationToken ct)
    {
        using var core = new CoreLab();
        core.Ledger.Write("inspect", "prompt");
        core.Ledger.Write("inspect", "tool", new() { ["tool"] = "shell", ["resource"] = "git status", ["outcome"] = "succeeded", ["duration"] = 1 });
        core.Ledger.Write("inspect", "tool", new() { ["tool"] = "read", ["resource"] = "README.md", ["outcome"] = "failed", ["duration"] = 2 });
        core.Ledger.Write("starting", "prompt");
        var last = core.Ledger.Read().Last(x => x.Type == "tool").Ts;
        using var harness = new HarnessProcess(core.Tree);

        var status = await harness.RunAsync(["status", core.Ledger.RunId], ct);

        status.RequireSuccess("progress status");
        await Assert.That(status.Stdout).Contains($"tools: 2; last: read README.md (failed) at {last}");
        await Assert.That(status.Stdout).Contains("tools: 0; last: -");
    }

    [Test]
    [Timeout(30_000)]
    public async Task Tool_calls_are_recorded_once_in_completion_order_with_safe_resources(CancellationToken ct)
    {
        using var core = new CoreLab();
        await using var lab = new OpenCodeLab(core.Tree);
        var run = await core.TaskAsync("inspect", ct);
        lab.Create("worker");
        lab.Prompt("worker")["events"] = new JsonArray(
            Tool("input.started", "read", 2100, new() { ["name"] = "read" }),
            Tool("called", "read", 2200, new() { ["input"] = new JsonObject { ["filePath"] = "README.md" }, ["executed"] = false }),
            Tool("input.started", "shell", 2300, new() { ["name"] = "shell" }),
            Tool("called", "shell", 2400, new() { ["input"] = new JsonObject { ["command"] = "fetch https://user:synthetic-password@example.invalid/data\nignored second line" }, ["executed"] = false }),
            Tool("success", "shell", 3400, new() { ["content"] = new JsonArray(), ["executed"] = false }),
            Tool("success", "shell", 3400, new() { ["content"] = new JsonArray(), ["executed"] = false }),
            Tool("failed", "read", 4200, new() { ["error"] = new JsonObject { ["type"] = "Error", ["message"] = "not found" }, ["executed"] = false }),
            Tool("input.started", "pending", 4300, new() { ["name"] = "read" }),
            Tool("called", "pending", 4400, new() { ["input"] = new JsonObject { ["path"] = "unfinished.txt" }, ["executed"] = false }),
            OpenCodeLab.Event("session.execution.succeeded", new() { ["sessionID"] = "worker" }, 5000));
        lab.Add("GET /api/permission/request", new JsonObject { ["location"] = new JsonObject { ["directory"] = "$directory" }, ["data"] = new JsonArray() }, repeat: true);
        lab.Add("GET /api/form", new JsonObject { ["location"] = new JsonObject { ["directory"] = "$directory" }, ["data"] = new JsonArray() }, repeat: true);
        lab.Add("GET /api/session/active", new JsonObject { ["data"] = new JsonObject() }, repeat: true);
        lab.Add("GET /api/session/worker", OpenCodeLab.Session("worker", "succeeded", 5000), repeat: true);
        lab.Add("GET /api/session/worker/message", JsonNode.Parse("""{"data":[{"id":"answer","type":"assistant","time":{"created":2,"completed":5},"agent":"build","model":{"id":"model","providerID":"fixture"},"content":[{"type":"text","text":"Report"}],"tokens":{"input":0,"output":0,"reasoning":0,"cache":{"read":0,"write":0}}}],"cursor":{}}"""));
        lab.Write();
        var scheduler = new Scheduler(new Dictionary<string, IWorkerBackend> { ["opencode"] = lab.Backend },
            core.Ledger, core.Tree.Repository, AllowList.From(null), core.Policy, 1, ct, core.Git);

        await Assert.That(await scheduler.RunAsync([run])).IsEqualTo(0);

        var events = core.Ledger.Read();
        var tools = events.Where(x => x.Type == "tool").ToArray();
        await Assert.That(tools.Length).IsEqualTo(2);
        await Assert.That(tools.Select(x => x.Task ?? "")).IsEquivalentTo(["inspect", "inspect"]);
        await Assert.That(tools.Select(x => x.Data!["tool"]!.GetValue<string>())).IsEquivalentTo(["shell", "read"], CollectionOrdering.Matching);
        await Assert.That(tools[0].Data!["resource"]!.GetValue<string>()).IsEqualTo("[REDACTED]");
        await Assert.That(tools[0].Data!["outcome"]!.GetValue<string>()).IsEqualTo("succeeded");
        await Assert.That(tools[0].Data!["duration"]!.GetValue<double>()).IsEqualTo(1);
        await Assert.That(tools[1].Data!["resource"]!.GetValue<string>()).IsEqualTo("README.md");
        await Assert.That(tools[1].Data!["outcome"]!.GetValue<string>()).IsEqualTo("failed");
        await Assert.That(tools[1].Data!["duration"]!.GetValue<double>()).IsEqualTo(2);
        await Assert.That(events.IndexOf(tools[0])).IsGreaterThan(events.FindIndex(x => x.Type == "prompt"));
        await Assert.That(events.IndexOf(tools[1])).IsLessThan(events.FindIndex(x => x.Type == "status"));
        await Assert.That(File.ReadAllText(Path.Combine(core.Ledger.Dir, "events.jsonl"))).DoesNotContain("synthetic-password");
    }

    [Test]
    [Timeout(30_000)]
    public async Task A_skill_tool_call_records_the_skill_id_as_its_resource(CancellationToken ct)
    {
        using var core = new CoreLab();
        await using var lab = new OpenCodeLab(core.Tree);
        var run = await core.TaskAsync("inspect", ct);
        lab.Create("worker");
        lab.Prompt("worker")["events"] = new JsonArray(
            Tool("input.started", "load", 2100, new() { ["name"] = "skill" }),
            Tool("called", "load", 2200, new() { ["input"] = new JsonObject { ["id"] = "brainstorming" }, ["executed"] = false }),
            Tool("success", "load", 3200, new() { ["content"] = new JsonArray(), ["executed"] = false }),
            OpenCodeLab.Event("session.execution.succeeded", new() { ["sessionID"] = "worker" }, 5000));
        lab.Add("GET /api/permission/request", new JsonObject { ["location"] = new JsonObject { ["directory"] = "$directory" }, ["data"] = new JsonArray() }, repeat: true);
        lab.Add("GET /api/form", new JsonObject { ["location"] = new JsonObject { ["directory"] = "$directory" }, ["data"] = new JsonArray() }, repeat: true);
        lab.Add("GET /api/session/active", new JsonObject { ["data"] = new JsonObject() }, repeat: true);
        lab.Add("GET /api/session/worker", OpenCodeLab.Session("worker", "succeeded", 5000), repeat: true);
        lab.Add("GET /api/session/worker/message", JsonNode.Parse("""{"data":[{"id":"answer","type":"assistant","time":{"created":2,"completed":5},"agent":"build","model":{"id":"model","providerID":"fixture"},"content":[{"type":"text","text":"Report"}],"tokens":{"input":0,"output":0,"reasoning":0,"cache":{"read":0,"write":0}}}],"cursor":{}}"""));
        lab.Write();
        var scheduler = new Scheduler(new Dictionary<string, IWorkerBackend> { ["opencode"] = lab.Backend },
            core.Ledger, core.Tree.Repository, AllowList.From(null), core.Policy, 1, ct, core.Git);

        await Assert.That(await scheduler.RunAsync([run])).IsEqualTo(0);

        var tool = core.Ledger.Read().Single(x => x.Type == "tool");
        await Assert.That(tool.Data!["tool"]!.GetValue<string>()).IsEqualTo("skill");
        await Assert.That(tool.Data!["resource"]!.GetValue<string>()).IsEqualTo("brainstorming");
    }

    static JsonObject Tool(string phase, string id, double created, JsonObject fields)
    {
        fields["sessionID"] = "worker";
        fields["assistantMessageID"] = "assistant";
        fields["id"] = id;
        return OpenCodeLab.Event("session.tool." + phase, fields, created);
    }
}
