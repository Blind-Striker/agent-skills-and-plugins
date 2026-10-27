using Dispatch.Tests.Support;

namespace Dispatch.Tests.Unit;

[Category("Unit")]
public sealed class FakeBackendTests
{
    [Test]
    public async Task FakeBackend_scripts_the_two_phase_start_and_emits_turn_scoped_signals()
    {
        var worker = new WorkerRef("codex", "task", "worker", null, new JsonObject());
        var fake = new FakeBackend("codex", new BackendCapabilities("sandbox", "approval-gate", "native", false))
        {
            OnCreate = (launch, _) => Task.FromResult(new CreateResult(worker with { TaskId = launch.TaskId }, new AppliedEnforcement("sandbox", "sandbox", "approval-gate"))),
        };
        fake.OnPrompt = (request, _) =>
        {
            var prompted = request.Worker with { Turn = request.Prompt.IsContinue ? "continued" : "first" };
            fake.Emit(new WorkerSignal.Ended(prompted.TaskId, prompted.Turn!, "succeeded", null));
            return Task.FromResult(prompted);
        };
        await using IWorkerBackend backend = fake;
        var launch = new WorkerLaunch("run", "task", "code", new RouteRef("provider", "model", null), null, null,
            new Modes("full", "on", "workspace", "on", "worktree"), "workspace", new WorkerRules([], []),
            new Dictionary<string, string>(), null, "full", null);

        var created = await backend.CreateAsync(launch, CancellationToken.None);
        await Assert.That(created.Worker.Turn).IsNull();
        await Assert.That(backend.Signals.TryRead(out _)).IsFalse();
        var first = await backend.PromptAsync(created.Worker, new WorkerPrompt("Task\n", false), CancellationToken.None);
        var continued = await backend.PromptAsync(first, new WorkerPrompt("Continue\n", true), CancellationToken.None);

        await Assert.That(first.Turn).IsEqualTo("first");
        await Assert.That(continued.Turn).IsEqualTo("continued");
        await Assert.That(backend.Signals.TryRead(out var firstSignal)).IsTrue();
        await Assert.That(((WorkerSignal.Ended)firstSignal!).Turn).IsEqualTo("first");
        await Assert.That(backend.Signals.TryRead(out var secondSignal)).IsTrue();
        await Assert.That(((WorkerSignal.Ended)secondSignal!).Turn).IsEqualTo("continued");
        await Assert.That(fake.Calls.Select(c => c.Member)).IsEquivalentTo(["CreateAsync", "PromptAsync", "PromptAsync"]);
        var promptCall = ((WorkerRef Worker, WorkerPrompt Prompt))fake.Calls.Last().Input!;
        await Assert.That(promptCall.Prompt.Text).IsEqualTo("Continue\n");
    }

    [Test]
    public async Task FakeBackend_exposes_scripted_results_for_every_backend_operation()
    {
        var worker = new WorkerRef("opencode", "task", "worker", "turn", new JsonObject());
        var blocker = new BlockerRef("opencode", "worker", "child", "ask");
        var report = new WorkerReport("failed", "partial answer", new TokenTotals { Input = 10, Output = 5 }, null, false,
            ["opencode:provider/model"], "execution failed", false);
        var fake = new FakeBackend("opencode", new BackendCapabilities("policy", "policy", "text", true))
        {
            OnProbe = (_, _) => Task.FromResult(new BackendProbe(true, "test-version", [], ["probe warning"])),
            OnValidate = (plans, _) => Task.FromResult<IReadOnlyList<LiveCheck>>([new(plans.Single().TaskId, [], [], "unknown", "full", null)]),
            OnAnswer = (request, _) => Task.FromResult(new AnswerResult(request.Blocker.OriginChildId == "child" && request.Answer is BlockerAnswer.Once, "delivered")),
            OnInterrupt = (request, _) => Task.FromResult(new InterruptResult(request.Kind == InterruptKind.Hard, "scripted stop")),
            OnPoll = (_, _) => Task.FromResult(new PollResult([new WorkerSignal.Host("profile=full;project=on", "connected", "ready", 1)])),
            OnCollect = (_, _) => Task.FromResult(report),
            OnReattach = (_, _) => Task.FromResult<ReattachResult>(new ReattachResult.Interrupted()),
            OnRelease = (_, _) => Task.FromResult<InterruptResult?>(null),
            OnFindOrphans = (_, _) => Task.FromResult<IReadOnlyList<WorkerRef>>([worker with { Id = "orphan" }]),
            OnListRoutes = (_, _) => Task.FromResult(new RouteListing([], [], [new RouteAllowance("provider/model", "enabled", null)])),
        };
        await using IWorkerBackend backend = fake;

        var probe = await backend.ProbeAsync(new ProbeRequest("workspace"), CancellationToken.None);
        var checks = await backend.ValidateAsync([new WorkerPlan("task", "code", new RouteRef("provider", "model", null), null, null,
            new Modes("full", "on", "workspace", "on", "worktree"), "workspace")], CancellationToken.None);
        var answer = await backend.AnswerAsync(blocker, new BlockerAnswer.Once(), CancellationToken.None);
        var interrupt = await backend.InterruptAsync(worker, InterruptKind.Hard, CancellationToken.None);
        var poll = await backend.PollAsync([worker], CancellationToken.None);
        var collected = await backend.CollectAsync(worker, CancellationToken.None);
        var reattach = await backend.ReattachAsync(worker, CancellationToken.None);
        await backend.ReleaseAsync(worker, CancellationToken.None);
        var orphans = await backend.FindOrphansAsync(new OrphanQuery("run", [worker], ["workspace"]), CancellationToken.None);
        var routes = await backend.ListRoutesAsync("workspace", CancellationToken.None);

        await Assert.That(probe.Ready).IsTrue();
        await Assert.That(checks.Single().TaskId).IsEqualTo("task");
        await Assert.That(answer.Confirmed).IsTrue();
        await Assert.That(interrupt.Stopped).IsTrue();
        await Assert.That(poll.Hosts.Single().State).IsEqualTo("connected");
        await Assert.That(collected).IsSameReferenceAs(report);
        await Assert.That(reattach is ReattachResult.Interrupted).IsTrue();
        await Assert.That(orphans.Single().Id).IsEqualTo("orphan");
        await Assert.That(routes.AllowList.Single().State).IsEqualTo("enabled");
        await Assert.That(fake.Calls.Select(c => c.Member)).IsEquivalentTo([
            "ProbeAsync", "ValidateAsync", "AnswerAsync", "InterruptAsync", "PollAsync", "CollectAsync", "ReattachAsync", "ReleaseAsync", "FindOrphansAsync", "ListRoutesAsync"]);
    }

    [Test]
    public async Task FakeBackend_propagates_scripted_faults_and_rejects_unscripted_calls()
    {
        await using var fake = new FakeBackend("codex", new BackendCapabilities("sandbox", "approval-gate", "native", false));
        var worker = new WorkerRef("codex", "task", "worker", "turn", new JsonObject());
        await Assert.That(async () => await fake.CollectAsync(worker, CancellationToken.None)).Throws<InvalidOperationException>();
        fake.OnCollect = (_, _) => Task.FromException<WorkerReport>(new IOException("scripted read failure"));
        await Assert.That(async () => await fake.CollectAsync(worker, CancellationToken.None)).Throws<IOException>();
    }

    [Test]
    public async Task FakeBackend_honours_cancellation_and_completes_its_signal_channel_on_disposal()
    {
        var fake = new FakeBackend("codex", new BackendCapabilities("sandbox", "approval-gate", "native", false));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.That(async () => await fake.ProbeAsync(new ProbeRequest("workspace"), cancellation.Token)).Throws<OperationCanceledException>();
        await Assert.That(fake.Calls).IsEmpty();
        await fake.DisposeAsync();
        await fake.Signals.Completion;
        await Assert.That(async () => await fake.ProbeAsync(new ProbeRequest("workspace"), CancellationToken.None)).Throws<ObjectDisposedException>();
        await Assert.That(() => fake.Emit(new WorkerSignal.Host("host", "connected", "ready", 1))).Throws<ObjectDisposedException>();
    }
}
