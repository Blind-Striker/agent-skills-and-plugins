using System.Text.Json.Nodes;
using Dispatch.Tests.Support;

namespace Dispatch.Tests.Integration;

[Category("Integration")]
[NotInParallel]
public sealed class OpenCodeOwnerTests
{
    [Test]
    [Arguments(10)]
    [Arguments(64)]
    [Timeout(30_000)]
    public async Task Owner_DepthTen_ResolvesToTask(int depth, CancellationToken ct)
    {
        await using var lab = new OpenCodeLab();
        for (var i = 1; i <= depth; i++)
            Parent(lab, $"child-{i}", i == depth ? "worker" : $"child-{i + 1}");
        Trigger(lab, Ask("child-1", "first"), Ask("child-1", "cached"), Ask("child-5", "ancestor"));
        lab.Write();

        var signals = await RunAsync(lab, ct);

        var asks = signals.OfType<WorkerSignal.Ask>().ToArray();
        await Assert.That(asks.Select(x => x.BlockerRef.AskId)).IsEquivalentTo(["first", "cached", "ancestor"]);
        foreach (var ask in asks)
        {
            await Assert.That(ask.TaskId).IsEqualTo("task");
            await Assert.That(ask.Turn).IsEqualTo("turn");
            await Assert.That(ask.BlockerRef.WorkerId).IsEqualTo("worker");
            await Assert.That(ask.BlockerRef.OriginChildId).IsEqualTo(ask.BlockerRef.AskId == "ancestor" ? "child-5" : "child-1");
        }
        await Assert.That(Requests(lab).Count(x => x["method"]!.GetValue<string>() == "GET"
            && x["path"]!.GetValue<string>().StartsWith("/api/session/child-", StringComparison.Ordinal))).IsEqualTo(depth);
        await Assert.That(Requests(lab).Count(x => x["unexpected"]!.GetValue<bool>())).IsEqualTo(0);
    }

    [Test]
    [Arguments(1)]
    [Arguments(2)]
    [Timeout(30_000)]
    public async Task Owner_Cycle_Stops(int length, CancellationToken ct)
    {
        await using var lab = new OpenCodeLab();
        Parent(lab, "child", length == 1 ? "child" : "parent", repeat: true);
        if (length == 2) Parent(lab, "parent", "child", repeat: true);
        Reject(lab, "child", "unowned");
        Trigger(lab, Ask("child", "unowned"));
        lab.Write();

        var signals = await RunAsync(lab, ct);

        await CutAsync(signals, "child", length);
        await Assert.That(signals.OfType<WorkerSignal.Ask>().Count()).IsEqualTo(0);
        await Assert.That(Requests(lab).Count(x => x["method"]!.GetValue<string>() == "GET"
            && x["path"]!.GetValue<string>() is "/api/session/child" or "/api/session/parent")).IsEqualTo(length);
        await RejectedAsync(lab, "child", "unowned");
    }

    [Test]
    [Arguments("cycle", 2)]
    [Arguments("missing", 2)]
    [Arguments("cap", 64)]
    [Arguments("root", 0)]
    [Timeout(30_000)]
    public async Task Owner_CutChain_NotCachedUnowned(string failure, int depth, CancellationToken ct)
    {
        await using var lab = new OpenCodeLab();
        switch (failure)
        {
            case "cycle":
                Parent(lab, "child", "parent");
                Parent(lab, "parent", "child");
                break;
            case "missing":
                Parent(lab, "child", "missing");
                lab.Add("GET /api/session/missing", new JsonObject { ["_tag"] = "SessionNotFoundError", ["message"] = "missing" }, 404);
                break;
            case "cap":
                Parent(lab, "child", "child-2");
                for (var i = 2; i <= 65; i++) Parent(lab, $"child-{i}", i == 65 ? "worker" : $"child-{i + 1}");
                break;
            case "root":
                Parent(lab, "child", null);
                break;
        }
        Parent(lab, "child", "worker");
        Reject(lab, "child", "unowned");
        Trigger(lab, Ask("child", "unowned"), Ask("child", "recovered"));
        lab.Write();

        var signals = await RunAsync(lab, ct);

        if (failure != "root") await CutAsync(signals, "child", depth);
        else await Assert.That(signals.OfType<WorkerSignal.Fault>().Count()).IsEqualTo(0);
        var asks = signals.OfType<WorkerSignal.Ask>().ToArray();
        await Assert.That(asks.Select(x => x.BlockerRef.AskId)).IsEquivalentTo(["recovered"]);
        await Assert.That(asks.Single().TaskId).IsEqualTo("task");
        await Assert.That(asks.Single().BlockerRef.OriginChildId).IsEqualTo("child");
        await RejectedAsync(lab, "child", "unowned");
        await Assert.That(Requests(lab).Count(x => x["unexpected"]!.GetValue<bool>())).IsEqualTo(0);
    }

    static async Task CutAsync(List<WorkerSignal> signals, string session, int depth)
    {
        var cuts = signals.OfType<WorkerSignal.Fault>().Where(x => x.Operation == "owner-chain-cut").ToArray();
        await Assert.That(cuts.Length).IsEqualTo(1);
        var data = JsonNode.Parse(cuts.Single().Message)!;
        await Assert.That(data["session"]!.GetValue<string>()).IsEqualTo(session);
        await Assert.That(data["depth"]!.GetValue<int>()).IsEqualTo(depth);
    }

    static void Reject(OpenCodeLab lab, string session, string ask) =>
        lab.Add($"POST /api/session/{session}/permission/{ask}/reply", status: 204)["body"] = new JsonObject { ["decision"] = "reject" };

    static async Task RejectedAsync(OpenCodeLab lab, string session, string ask)
    {
        var replies = Requests(lab).Where(x => x["method"]!.GetValue<string>() == "POST"
            && x["path"]!.GetValue<string>() == $"/api/session/{session}/permission/{ask}/reply").ToArray();
        await Assert.That(replies.Length).IsEqualTo(1);
        await Assert.That(replies.Single()["body"]!["decision"]!.GetValue<string>()).IsEqualTo("reject");
        await Assert.That(replies.Single()["body"]!["message"]!.GetValue<string>()).IsNotEmpty();
    }

    static void Parent(OpenCodeLab lab, string child, string? parent, bool repeat = false)
    {
        var session = OpenCodeLab.Session(child);
        session["data"]!["parentID"] = parent;
        lab.Add($"GET /api/session/{child}", session, repeat: repeat);
    }

    static JsonObject Ask(string session, string id) => OpenCodeLab.Event("permission.asked", new()
    {
        ["sessionID"] = session, ["id"] = id, ["action"] = "read", ["resources"] = new JsonArray("README.md"),
    });

    static void Trigger(OpenCodeLab lab, params JsonObject[] events)
    {
        lab.Add("GET /api/session/worker", OpenCodeLab.Session("worker"));
        lab.Add("POST /api/session/worker/interrupt", new JsonObject { ["interrupted"] = false })["events"] =
            new JsonArray(events.Append(Ask("worker", "marker")).Cast<JsonNode>().ToArray());
    }

    static async Task<List<WorkerSignal>> RunAsync(OpenCodeLab lab, CancellationToken ct)
    {
        var worker = new WorkerRef("opencode", "task", "worker", "turn", new());
        await lab.Backend.ReattachAsync(worker, ct);
        await lab.Backend.InterruptAsync(worker, InterruptKind.Cancel, ct);
        var signals = new List<WorkerSignal>();
        while (await lab.Backend.Signals.ReadAsync(ct) is { } signal)
        {
            if (signal is WorkerSignal.Ask { BlockerRef.AskId: "marker" }) break;
            signals.Add(signal);
        }
        return signals;
    }

    static JsonNode[] Requests(OpenCodeLab lab) => File.ReadLines(Path.Combine(lab.Tree.Root, "requests.jsonl"))
        .Select(x => JsonNode.Parse(x)!).ToArray();
}
