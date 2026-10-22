using Dispatch.Tests.Support;

namespace Dispatch.Tests.Unit;

[Category("Unit")]
public sealed class EscalationTests
{
    [Test]
    public async Task Forms_are_cancelled_once_with_the_no_human_message()
    {
        using var tree = new TempTree();
        await using var backend = new FakeBackend("opencode", new("policy", "policy", "text", true));
        var answers = new List<BlockerAnswer>();
        backend.OnAnswer = (input, _) => { answers.Add(input.Answer); return Task.FromResult(new AnswerResult(true, null)); };
        var ledger = new Ledger(tree.Root);
        var escalation = new Escalation(new Dictionary<string, IWorkerBackend> { [backend.Name] = backend }, ledger,
            new Policy { Path = "config.json", Deny = [], Escalate = [], Instructions = [] });
        var run = new TaskRun { Record = new() { Spec = new() { Id = "inspect" } }, State = TaskState.Running };
        var form = new WorkerSignal.Form(run.Id, "turn", new("opencode", "worker", "child", "form"), "Question");
        await escalation.CancelFormAsync(run, form, CancellationToken.None);
        await escalation.CancelFormAsync(run, form, CancellationToken.None);

        await Assert.That(answers.Count).IsEqualTo(1);
        await Assert.That(answers.Single()).IsTypeOf<BlockerAnswer.Cancel>();
        await Assert.That(((BlockerAnswer.Cancel)answers.Single()).Message).IsEqualTo(Policy.NoHuman);
        await Assert.That(ledger.Read().Single().Data!["session"]!.GetValue<string>()).IsEqualTo("child");
    }

    [Test]
    [Arguments("opencode", true)]
    [Arguments("codex", false)]
    public async Task Unescalated_ask_uses_the_backend_default_once_per_id(string name, bool once)
    {
        using var tree = new TempTree();
        await using var backend = new FakeBackend(name, new("policy", "policy", "text", true));
        var answers = new List<BlockerAnswer>();
        backend.OnAnswer = (input, _) => { answers.Add(input.Answer); return Task.FromResult(new AnswerResult(true, null)); };
        var escalation = new Escalation(new Dictionary<string, IWorkerBackend> { [name] = backend }, new Ledger(tree.Root),
            new Policy { Path = "config.json", Deny = [], Escalate = [], Instructions = [] });
        var run = new TaskRun { Record = new() { Spec = new() { Id = "inspect" } }, State = TaskState.Running };
        var ask = new WorkerSignal.Ask(run.Id, "turn", new(name, "worker", null, "ask"), "shell", ["tool"]);
        await escalation.AnswerAsync(run, ask, CancellationToken.None);
        await escalation.AnswerAsync(run, ask, CancellationToken.None);

        await Assert.That(answers.Count).IsEqualTo(1);
        await Assert.That(answers.Single() is BlockerAnswer.Once).IsEqualTo(once);
        if (!once) await Assert.That(((BlockerAnswer.Reject)answers.Single()).Message).IsNotEmpty();
        await Assert.That(run.Asks).IsEqualTo(1);
    }

    [Test]
    [Arguments("Done")]
    [Arguments("Failed")]
    [Arguments("Cancelled")]
    [Arguments("Settling")]
    public async Task Ask_after_task_end_is_rejected_even_when_the_policy_escalates(string state)
    {
        using var tree = new TempTree();
        var ledger = new Ledger(tree.Root);
        await using var backend = new FakeBackend("opencode", new("policy", "policy", "text", true));
        BlockerAnswer? answered = null;
        backend.OnAnswer = (input, _) => { answered = input.Answer; return Task.FromResult(new AnswerResult(true, null)); };
        var policy = new Policy { Path = "config.json", Deny = [], Instructions = [],
            Escalate = [new() { Name = "all", Action = "*", Resources = ["*"] }] };
        var escalation = new Escalation(new Dictionary<string, IWorkerBackend> { [backend.Name] = backend }, ledger, policy);
        var run = new TaskRun { Record = new() { Spec = new() { Id = "inspect" } }, State = Enum.Parse<TaskState>(state) };
        await escalation.AnswerAsync(run, new(run.Id, "turn", new("opencode", "worker", null, "ask"), "shell", ["tool"]), CancellationToken.None);

        await Assert.That(answered).IsTypeOf<BlockerAnswer.Reject>();
        await Assert.That(((BlockerAnswer.Reject)answered!).Message).IsNotEmpty();
        await Assert.That(ledger.Read().Any(x => x.Type == "escalated")).IsFalse();
    }
}
