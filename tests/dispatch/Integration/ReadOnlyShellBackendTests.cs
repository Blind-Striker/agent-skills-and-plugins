using System.Text.Json.Nodes;
using Dispatch.Tests.Support;

namespace Dispatch.Tests.Integration;

[Category("Integration")]
[NotInParallel]
public sealed class ReadOnlyShellBackendTests
{
    [Test]
    [Arguments("codex", "decline", true, "shell")]
    [Arguments("opencode", "decline", true, "shell")]
    [Arguments("opencode", "escalate", false, "shell")]
    [Arguments("opencode", "decline", true, "bash")]
    [Arguments("opencode", "escalate", false, "bash")]
    [Timeout(180_000)]
    public async Task Read_only_shell_without_operator_authority_is_declined_on_the_wire(string name, string setting, bool matchingRule, string action, CancellationToken ct)
        => await Exercise(name, setting, matchingRule, false, action, ct);

    [Test]
    [Arguments("codex", "shell")]
    [Arguments("opencode", "shell")]
    [Arguments("opencode", "bash")]
    [Timeout(180_000)]
    public async Task Cli_shell_escalation_once_requires_a_new_ask_for_the_next_request(string name, string action, CancellationToken ct)
        => await Exercise(name, "escalate", true, true, action, ct);

    static async Task Exercise(string name, string setting, bool matchingRule, bool useCli, string action, CancellationToken ct)
    {
        var settings = new EffectiveSettings { Access = new() { Value = "read-only" }, ReadOnlyShell = new() { Value = setting } };
        if (useCli)
        {
            var file = Json.ReadTaskFile("""{"schemaVersion":2,"defaults":{"backend":"BACKEND","readOnlyShell":"decline"},"tasks":[{"id":"inspect","kind":"research","route":"ROUTE"}]}"""
                .Replace("BACKEND", name).Replace("ROUTE", name == "codex" ? "model-y#high" : "fixture/model"));
            settings = ConfigResolver.Resolve(file, cli: ConfigLayers.Preferences(Args.Parse(["run", "--read-only-shell", "escalate"]))).Tasks["inspect"].Settings;
            await Assert.That(settings.ReadOnlyShell!.Value).IsEqualTo("escalate");
            await Assert.That(settings.ReadOnlyShell.From).IsEqualTo("cli");
        }
        using var core = new CoreLab();
        using var codex = name == "codex" ? new CodexLab(core.Tree) : null;
        await using var opencode = name == "opencode" ? new OpenCodeLab(core.Tree) : null;
        await using var codexBackend = codex?.Backend();
        IWorkerBackend backend;
        if (codex is not null)
        {
            codex.Write("preflight", CodexLab.Handshake + CodexLab.Thread.Replace("workspaceWrite", "readOnly") + """
                {"request":"item/commandExecution/requestApproval","params":{"threadId":"$threadId","turnId":"$turnId","itemId":"call","command":"inspect files"}}

                """ + (useCli ? """
                {"request":"item/commandExecution/requestApproval","params":{"threadId":"$threadId","turnId":"$turnId","itemId":"call2","command":"inspect more files"}}

                """ : "{\"expect\":\"turn/steer\",\"reply\":{}}\n") + CodexLab.Final);
            backend = codexBackend!;
        }
        else
        {
            opencode!.Create("worker");
            opencode.Prompt("worker");
            opencode.Add("GET /api/session/active", new JsonObject { ["data"] = new JsonObject() }, repeat: true);
            foreach (var id in useCli ? new[] { "call", "call2" } : ["call"])
            {
                opencode.Add("GET /api/permission/request", new JsonObject
                {
                    ["location"] = new JsonObject { ["directory"] = "$directory" },
                    ["data"] = new JsonArray(new JsonObject { ["sessionID"] = "worker", ["id"] = id, ["action"] = action,
                        ["resources"] = new JsonArray(id == "call" ? "inspect files" : "inspect more files") }),
                });
                opencode.Add($"POST /api/session/worker/permission/{id}/reply", status: 204);
            }
            opencode.Add("GET /api/form", new JsonObject { ["location"] = new JsonObject { ["directory"] = "$directory" }, ["data"] = new JsonArray() }, repeat: true);
            opencode.Write();
            backend = opencode.Backend;
        }
        var created = await backend.CreateAsync(new("run", "inspect", "research",
            name == "codex" ? new("codex", "model-y", "high") : new("fixture", "model", null), null, null,
            TaskStarter.ModesFor(new() { Spec = new() { Kind = "research" }, Settings = settings }), core.Tree.Repository, new([], []),
            codex?.Environment ?? new Dictionary<string, string>(), null, "full", null), ct);
        if (opencode is not null)
        {
            var request = File.ReadLines(Path.Combine(core.Tree.Root, "requests.jsonl")).Select(x => JsonNode.Parse(x)!)
                .Single(x => x["method"]!.GetValue<string>() == "POST" && x["path"]!.GetValue<string>() == "/api/session");
            var shell = request["body"]!["permissions"]!.AsArray().LastOrDefault(x => x!["action"]!.GetValue<string>() == "shell");
            await Assert.That(shell?["resource"]?.GetValue<string>()).IsEqualTo("*");
            await Assert.That(shell?["effect"]?.GetValue<string>()).IsEqualTo(setting == "decline" ? "deny" : "ask");
        }
        var worker = await backend.PromptAsync(created.Worker, new("Inspect.", false), ct);
        if (matchingRule) core.Policy.Escalate.Add(new() { Name = "inspect", Action = action, Resources = ["inspect *"] });
        var run = new TaskRun { Record = new() { Spec = new() { Id = "inspect", Kind = "research" },
            Settings = settings }, State = TaskState.Running };
        var escalation = new Escalation(new Dictionary<string, IWorkerBackend> { [name] = backend }, core.Ledger, core.Policy);

        var count = useCli ? 2 : 1;
        string? previousAskId = null;
        for (var index = 0; index < count; index++)
        {
            if (opencode is not null) await backend.PollAsync([worker], ct);
            WorkerSignal.Ask? ask = null;
            while (ask is null) ask = await backend.Signals.ReadAsync(ct) as WorkerSignal.Ask;
            await Assert.That(ask.Action).IsEqualTo(action);
            if (opencode is not null) await Assert.That(ask.BlockerRef.AskId).IsEqualTo(index == 0 ? "call" : "call2");
            await escalation.AnswerAsync(run, ask, ct);
            if (useCli)
            {
                var pending = AskQueue.List(core.Ledger.RunId).Single();
                await Assert.That(pending.Action).IsEqualTo(action);
                await Assert.That(pending.AskId).IsNotEqualTo(previousAskId);
                await Assert.That(pending.Resources).IsEquivalentTo(index == 0 ? new[] { "inspect files" } : ["inspect more files"]);
                await Assert.That(core.Ledger.Read().Count(x => x.Type == "permission")).IsEqualTo(index);
                if (codex is not null) await Assert.That(codex.Input().Count(x => x.ContainsKey("result"))).IsEqualTo(index);
                else await Assert.That(File.ReadLines(Path.Combine(core.Tree.Root, "requests.jsonl")).Select(x => JsonNode.Parse(x)!)
                    .Count(x => x["path"]!.GetValue<string>().EndsWith("/reply", StringComparison.Ordinal))).IsEqualTo(index);
                AskQueue.WriteDecision(core.Ledger.RunId, new() { AskId = pending.AskId, Decision = "once", Time = Json.Timestamp() });
                await escalation.CheckDecisionsAsync(ct);
                previousAskId = pending.AskId;
            }
            await Assert.That(AskQueue.List(core.Ledger.RunId)).IsEmpty();
        }
        await Assert.That(core.Ledger.Read().Count(x => x.Type == "escalated")).IsEqualTo(useCli ? 2 : 0);
        if (codex is not null)
        {
            await backend.CollectAsync(worker, ct);
            var replies = codex.Input().Where(x => x.ContainsKey("result")).ToArray();
            await Assert.That(replies.Length).IsEqualTo(count);
            foreach (var reply in replies)
                await Assert.That(reply["result"]!["decision"]!.GetValue<string>()).IsEqualTo(useCli ? "accept" : "decline");
        }
        else
        {
            var replies = File.ReadLines(Path.Combine(core.Tree.Root, "requests.jsonl")).Select(x => JsonNode.Parse(x)!)
                .Where(x => x["path"]!.GetValue<string>().EndsWith("/reply", StringComparison.Ordinal)).ToArray();
            await Assert.That(replies.Length).IsEqualTo(count);
            foreach (var reply in replies)
                await Assert.That(reply["body"]!["decision"]!.GetValue<string>()).IsEqualTo(useCli ? "once" : "reject");
        }
    }
}
