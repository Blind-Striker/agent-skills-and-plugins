using System.Text.Json.Nodes;
using Dispatch.Tests.Support;

namespace Dispatch.Tests.Integration;

[Category("Integration")]
[NotInParallel]
public sealed class CodexApprovalPolicyTests
{
    [Test]
    [Timeout(180_000)]
    public async Task File_change_approval_without_grant_root_matches_the_preceding_item_path(CancellationToken cancellationToken)
    {
        using var core = new CoreLab();
        using var lab = new CodexLab(core.Tree);
        lab.Write("preflight", CodexLab.Handshake + CodexLab.Thread + """
            {"notify":"item/started","params":{"threadId":"$threadId","turnId":"$turnId","item":{"id":"call","type":"fileChange","changes":[{"path":"$cwd/a.txt"}]}}}
            {"request":"item/fileChange/requestApproval","params":{"threadId":"$threadId","turnId":"$turnId","itemId":"call"},"result":{"decision":"accept"}}

            """ + CodexLab.Final);
        await using var backend = lab.Backend();
        var created = await backend.CreateAsync(new("run", "inspect", "code", new("codex", "model-y", "high"), null, null,
            new("full", "on", "workspace", "on", "worktree"), lab.Tree.Repository, new([], []), lab.Environment, null, "full", null), cancellationToken);
        using var paused = PauseNotifications(backend);
        var worker = await backend.PromptAsync(created.Worker, new("Inspect.", false), cancellationToken);
        WorkerSignal.Ask? ask = null;
        while (ask is null) ask = await backend.Signals.ReadAsync(cancellationToken) as WorkerSignal.Ask;
        var path = lab.Tree.Repository + "/a.txt";
        await Assert.That(ask.Resources).IsEquivalentTo(new[] { path });
        core.Policy.Escalate.Add(new() { Name = "path-only", Action = "edit", Resources = [path] });
        var escalation = new Escalation(new Dictionary<string, IWorkerBackend> { ["codex"] = backend }, core.Ledger, core.Policy);
        var run = new TaskRun { Record = new() { Spec = new() { Id = "inspect" } }, State = TaskState.Running };
        await escalation.AnswerAsync(run, ask, cancellationToken);
        var pending = AskQueue.List(core.Ledger.RunId).Single();
        await Assert.That(pending.Rule).IsEqualTo("path-only");
        AskQueue.WriteDecision(core.Ledger.RunId, new() { AskId = pending.AskId, Decision = "once", Time = Json.Timestamp() });
        await escalation.CheckDecisionsAsync(cancellationToken);
        paused.Dispose();
        await Assert.That((await backend.CollectAsync(worker, cancellationToken)).Outcome).IsEqualTo("succeeded");
        await Assert.That(lab.Input().Single(x => x.ContainsKey("result"))["result"]!["decision"]!.GetValue<string>()).IsEqualTo("accept");
    }

    static PausedNotifications PauseNotifications(CodexBackend backend)
    {
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var workers = (System.Collections.IEnumerable)typeof(CodexBackend).GetField("workers", flags)!.GetValue(backend)!;
        var entry = workers.Cast<object>().Single();
        var connection = entry.GetType().GetProperty("Value")!.GetValue(entry)!;
        var rpc = (CodexRpc)connection.GetType().GetProperty("Rpc")!.GetValue(connection)!;
        var field = typeof(CodexRpc).GetField("notifications", flags)!;
        var paused = new PausedNotifications((System.Threading.Channels.Channel<CodexNotification>)field.GetValue(rpc)!);
        field.SetValue(rpc, paused);
        return paused;
    }

    sealed class PausedNotifications : System.Threading.Channels.Channel<CodexNotification>, IDisposable
    {
        readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public PausedNotifications(System.Threading.Channels.Channel<CodexNotification> channel)
        {
            Writer = channel.Writer;
            Reader = new PausedReader(channel.Reader, ready.Task);
        }
        public void Dispose() => ready.TrySetResult();

        sealed class PausedReader(System.Threading.Channels.ChannelReader<CodexNotification> reader, Task ready)
            : System.Threading.Channels.ChannelReader<CodexNotification>
        {
            public override bool TryRead(out CodexNotification item)
            {
                if (ready.IsCompleted) return reader.TryRead(out item!);
                item = null!;
                return false;
            }
            public override async ValueTask<bool> WaitToReadAsync(CancellationToken cancellationToken = default)
            {
                await ready.WaitAsync(cancellationToken);
                return await reader.WaitToReadAsync(cancellationToken);
            }
        }
    }

    [Test]
    [Timeout(180_000)]
    public async Task Deny_overrides_wildcard_escalation_at_the_Codex_approval_gate(CancellationToken cancellationToken)
    {
        using var core = new CoreLab();
        using var lab = new CodexLab(core.Tree);
        lab.Write("preflight", CodexLab.Handshake + CodexLab.Thread + """
            {"request":"item/commandExecution/requestApproval","params":{"threadId":"$threadId","turnId":"$turnId","itemId":"call","command":"publish package"},"result":{"decision":"decline"}}
            {"expect":"turn/steer","reply":{}}

            """ + CodexLab.Final);
        await using var backend = lab.Backend();
        var created = await backend.CreateAsync(new("run", "inspect", "code", new("codex", "model-y", "high"), null, null,
            new("full", "on", "workspace", "on", "worktree"), lab.Tree.Repository, new([], []), lab.Environment, null, "full", null), cancellationToken);
        var worker = await backend.PromptAsync(created.Worker, new("Inspect.", false), cancellationToken);
        WorkerSignal.Ask? ask = null;
        while (ask is null) ask = await backend.Signals.ReadAsync(cancellationToken) as WorkerSignal.Ask;
        core.Policy.Deny.Add(new() { Name = "no-publish", Action = "shell", Resources = ["publish *"] });
        core.Policy.Escalate.Add(new() { Name = "all", Action = "*", Resources = ["*"] });
        var escalation = new Escalation(new Dictionary<string, IWorkerBackend> { ["codex"] = backend }, core.Ledger, core.Policy);
        var run = new TaskRun { Record = new() { Spec = new() { Id = "inspect" } }, State = TaskState.Running };

        await escalation.AnswerAsync(run, ask, cancellationToken);

        await Assert.That(AskQueue.List(core.Ledger.RunId)).IsEmpty();
        await Assert.That(core.Ledger.Read().Where(x => x.Type == "escalated")).IsEmpty();
        await backend.CollectAsync(worker, cancellationToken);
        await Assert.That(lab.Input().Single(x => x.ContainsKey("result"))["result"]!["decision"]!.GetValue<string>()).IsEqualTo("decline");
    }

    [Test]
    [Arguments("fileChange")]
    [Arguments("permissions")]
    [Arguments("permissions-entries")]
    [Timeout(180_000)]
    public async Task Read_only_write_approval_is_declined_without_an_operator_ask(string tool, CancellationToken cancellationToken)
    {
        using var core = new CoreLab();
        using var lab = new CodexLab(core.Tree);
        var permissions = tool == "permissions-entries"
            ? """{"fileSystem":{"entries":[{"access":"write","path":{"type":"path","path":"$cwd/a.txt"}}]}}"""
            : """{"fileSystem":{"write":["$cwd/a.txt"]}}""";
        lab.Write("preflight", CodexLab.Handshake + CodexLab.Thread.Replace("workspaceWrite", "readOnly") + """
            {"request":"item/TOOL/requestApproval","params":{"threadId":"$threadId","turnId":"$turnId","itemId":"call","grantRoot":"$cwd/a.txt","permissions":PERMISSIONS}}
            {"expect":"turn/steer","reply":{}}

            """.Replace("TOOL", tool == "permissions-entries" ? "permissions" : tool).Replace("PERMISSIONS", permissions) + CodexLab.Final);
        await using var backend = lab.Backend();
        var created = await backend.CreateAsync(new("run", "inspect", "review", new("codex", "model-y", "high"), null, null,
            new("full", "on", "read-only", "on", "worktree"), lab.Tree.Repository, new([], []), lab.Environment, null, "full", null), cancellationToken);
        var worker = await backend.PromptAsync(created.Worker, new("Inspect.", false), cancellationToken);
        WorkerSignal.Ask? ask = null;
        while (ask is null) ask = await backend.Signals.ReadAsync(cancellationToken) as WorkerSignal.Ask;
        core.Policy.Escalate.Add(new() { Name = "all", Action = "*", Resources = ["*"] });
        var escalation = new Escalation(new Dictionary<string, IWorkerBackend> { ["codex"] = backend }, core.Ledger, core.Policy);
        var run = new TaskRun { Record = new() { Spec = new() { Id = "inspect", Access = "read-only" },
            Settings = new() { Access = new() { Value = "read-only" } } }, State = TaskState.Running };

        await escalation.AnswerAsync(run, ask, cancellationToken);

        await Assert.That(AskQueue.List(core.Ledger.RunId)).IsEmpty();
        await Assert.That(core.Ledger.Read().Where(x => x.Type == "escalated")).IsEmpty();
        await backend.CollectAsync(worker, cancellationToken);
        var answer = lab.Input().Single(x => x.ContainsKey("result"))["result"]!;
        if (tool.StartsWith("permissions", StringComparison.Ordinal)) await Assert.That(answer["permissions"]!.AsObject()).IsEmpty();
        else await Assert.That(answer["decision"]!.GetValue<string>()).IsEqualTo("decline");
        await Assert.That(lab.Input().Single(x => x["method"]?.GetValue<string>() == "turn/steer")["params"]!["input"]![0]!["text"]!.GetValue<string>()).Contains("read-only");
    }
}
