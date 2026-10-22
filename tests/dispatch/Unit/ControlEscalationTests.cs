using System.Text.Json.Nodes;
using Dispatch.Tests.Support;

namespace Dispatch.Tests.Unit;

[Category("Unit")]
[NotInParallel]
public sealed class ControlEscalationTests
{
    [Test]
    public async Task Cancel_UnpublishedTemporaryFile_IsNotRead()
    {
        using var lab = new CoreLab();
        var escalation = new Escalation(new Dictionary<string, IWorkerBackend> { [lab.Backend.Name] = lab.Backend }, lab.Ledger, lab.Policy);
        var run = new TaskRun { Record = new() { Spec = new() { Id = "inspect" } }, State = TaskState.Running };
        var dir = Directory.CreateDirectory(Locations.CancelDir(lab.Ledger.RunId)).FullName;
        var file = Path.Combine(dir, "inspect.json.tmp-pending");
        File.WriteAllText(file, "{");

        escalation.CheckCancelRequests([run]);

        await Assert.That(File.Exists(file)).IsTrue();
        await Assert.That(run.CancelRequested).IsFalse();
        await Assert.That(lab.Ledger.Read()).IsEmpty();
    }

    [Test]
    public Task Decision_ActsOnHeldAsk_NotOnRewrittenFile() => HeldDecisionAsync(replay: false);

    [Test]
    public Task Decision_IsSingleUse() => HeldDecisionAsync(replay: true);

    static async Task HeldDecisionAsync(bool replay)
    {
        using var lab = new CoreLab();
        lab.Policy.Escalate.Add(new() { Name = "all", Action = "*", Resources = ["*"] });
        var answers = new List<BlockerRef>();
        lab.Backend.OnAnswer = (input, _) => { answers.Add(input.Blocker); return Task.FromResult(new AnswerResult(true, null)); };
        var escalation = new Escalation(new Dictionary<string, IWorkerBackend> { [lab.Backend.Name] = lab.Backend }, lab.Ledger, lab.Policy);
        var run = new TaskRun { Record = new() { Spec = new() { Id = "inspect" } }, State = TaskState.Running };
        var blocker = new BlockerRef("opencode", "worker", "child", "backend-ask");
        await escalation.AnswerAsync(run, new(run.Id, "turn", blocker, "shell", ["original"]), CancellationToken.None);
        var ask = AskQueue.List(lab.Ledger.RunId).Single();
        await Assert.That(ask.AskId).IsEqualTo("inspect-1");
        ask.Action = "changed";
        ask.Resources = ["rewritten"];
        ask.Session = "other-worker";
        AskQueue.Write(ask);
        var decision = new AskDecision { AskId = ask.AskId, Decision = "once", Time = Json.Timestamp() };
        AskQueue.WriteDecision(lab.Ledger.RunId, decision);
        await escalation.CheckDecisionsAsync(CancellationToken.None);
        if (replay)
        {
            AskQueue.WriteDecision(lab.Ledger.RunId, decision);
            await escalation.CheckDecisionsAsync(CancellationToken.None);
            await Assert.That(lab.Ledger.Read().Single(x => x.Type == "decision-refused").Data!["reason"]!.GetValue<string>()).IsEqualTo("unknown-ask");
        }
        await Assert.That(answers.Count).IsEqualTo(1);
        await Assert.That(answers.Single()).IsEqualTo(blocker);
        var permission = lab.Ledger.Read().Single(x => x.Type == "permission");
        await Assert.That(permission.Data!["action"]!.GetValue<string>()).IsEqualTo("shell");
        await Assert.That(permission.Data["resources"]![0]!.GetValue<string>()).IsEqualTo("original");
        await Assert.That(AskQueue.List(lab.Ledger.RunId)).IsEmpty();
    }

    [Test]
    [Arguments("{")]
    [Arguments("{\"schemaVersion\":2,\"askId\":\"ask\",\"decision\":\"always\"}")]
    [Arguments("{\"schemaVersion\":2,\"askId\":\"ask\",\"decision\":\"once\",\"proof\":\"extra\"}")]
    [Arguments("{\"schemaVersion\":2,\"askId\":null,\"decision\":\"once\",\"time\":\"2030-01-01T00:00:00Z\"}")]
    public async Task Decision_Unparsable_DeletedAsError(string text)
    {
        using var lab = new CoreLab();
        lab.Policy.Escalate.Add(new() { Name = "all", Action = "*", Resources = ["*"] });
        var answers = 0;
        lab.Backend.OnAnswer = (_, _) => { answers++; return Task.FromResult(new AnswerResult(true, null)); };
        var escalation = new Escalation(new Dictionary<string, IWorkerBackend> { [lab.Backend.Name] = lab.Backend }, lab.Ledger, lab.Policy);
        var run = new TaskRun { Record = new() { Spec = new() { Id = "inspect" } }, State = TaskState.Running };
        await escalation.AnswerAsync(run, new(run.Id, "turn", new("opencode", "worker", null, "ask"), "shell", ["tool"]), CancellationToken.None);
        var dir = Directory.CreateDirectory(Locations.DecisionsDir(lab.Ledger.RunId)).FullName;
        var file = Path.Combine(dir, "ask.json");
        File.WriteAllText(file, text);

        await escalation.CheckDecisionsAsync(CancellationToken.None);
        await escalation.CheckDecisionsAsync(CancellationToken.None);

        await Assert.That(File.Exists(file)).IsFalse();
        await Assert.That(answers).IsEqualTo(0);
        await Assert.That(AskQueue.List(lab.Ledger.RunId).Count).IsEqualTo(1);
        await Assert.That(lab.Ledger.Read().Count(x => x.Type == "error")).IsEqualTo(1);
        await Assert.That(lab.Ledger.Read().Any(x => x.Type == "decision-refused")).IsFalse();
    }

    [Test]
    public async Task Decision_ForUnknownAsk_IsRefusedAndLogged()
    {
        using var lab = new CoreLab();
        var answers = 0;
        lab.Backend.OnAnswer = (_, _) => { answers++; return Task.FromResult(new AnswerResult(true, null)); };
        var escalation = new Escalation(new Dictionary<string, IWorkerBackend> { [lab.Backend.Name] = lab.Backend }, lab.Ledger, lab.Policy);
        AskQueue.WriteDecision(lab.Ledger.RunId, new AskDecision { AskId = "unknown", Decision = "once", Time = Json.Timestamp() });

        await escalation.CheckDecisionsAsync(CancellationToken.None);

        await Assert.That(answers).IsEqualTo(0);
        var refused = lab.Ledger.Read().Single(x => x.Type == "decision-refused");
        await Assert.That(refused.Data!["askId"]!.GetValue<string>()).IsEqualTo("unknown");
        await Assert.That(refused.Data["reason"]!.GetValue<string>()).IsEqualTo("unknown-ask");
        await Assert.That(Directory.GetFiles(Locations.DecisionsDir(lab.Ledger.RunId))).IsEmpty();
    }
}
