using Dispatch.Tests.Support;
using Microsoft.Extensions.Time.Testing;
using System.Threading.Channels;

namespace Dispatch.Tests.Integration;

[Category("Integration")]
[NotInParallel]
public sealed class LifecycleTests
{
    [Test]
    [Timeout(30_000)]
    public async Task Resume_PersistedTimeout_ContinuesStopLadder(CancellationToken ct)
    {
        using var lab = new CoreLab();
        var original = await lab.TaskAsync("inspect", ct);
        original.SaveWorker(new("opencode", original.Id, "worker", "turn", new()), lab.Ledger);
        lab.Ledger.Write(original.Id, "timeout");
        var time = new FakeTimeProvider(DateTimeOffset.Parse(lab.Ledger.Read().Single().Ts));
        time.Advance(TimeSpan.FromMinutes(3));
        var clock = new ObservedClock(time);
        var run = TaskRun.FromLedger(lab.Ledger.LoadRecord(original.Id), lab.Ledger);
        lab.Backend.OnReattach = (_, _) => Task.FromResult<ReattachResult>(new ReattachResult.Live());
        var hard = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lab.Backend.OnInterrupt = (input, _) =>
        {
            if (input.Kind == InterruptKind.Hard) hard.TrySetResult();
            return Task.FromResult(new InterruptResult(false, "worker still live"));
        };
        var running = lab.Scheduler(ct, time: clock).ResumeAsync([run], false);
        await hard.Task.WaitAsync(TimeSpan.FromSeconds(2), ct);
        await clock.AdvanceWhenIdle(TimeSpan.FromMinutes(1), ct);
        await DriveUntilDone(running, time, ct);
        await Assert.That(await running).IsEqualTo(1);
        await Assert.That(lab.Ledger.LoadResult(run.Id)!.Outcome).IsEqualTo("timeout");
        await Assert.That(lab.Ledger.LoadResult(run.Id)!.Failures).Contains("zombie");
    }

    [Test]
    [Timeout(30_000)]
    public async Task StopLadder_UnansweredHardStop_GivesUpAtFinalMark(CancellationToken ct)
    {
        using var lab = new CoreLab();
        var time = new FakeTimeProvider();
        var clock = new ObservedClock(time);
        var run = await lab.TaskAsync("inspect", ct);
        run.Record.Spec.TimeoutMinutes = 1;
        var hard = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lab.Backend.OnInterrupt = async (input, token) =>
        {
            if (input.Kind == InterruptKind.Hard)
            {
                hard.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            }
            return new(false, "interrupt sent");
        };
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var running = lab.Scheduler(stop.Token, time: clock).RunAsync([run]);
        try
        {
            await clock.AdvanceWhenIdle(TimeSpan.FromMinutes(1), ct);
            await clock.AdvanceWhenIdle(TimeSpan.FromMinutes(2), ct);
            await hard.Task.WaitAsync(ct);
            time.Advance(TimeSpan.FromMinutes(2));
            await DriveUntilDone(running, time, ct);
            await Assert.That(await running).IsEqualTo(1);
            await Assert.That(lab.Ledger.LoadResult(run.Id)!.Failures).Contains("zombie");
            await Assert.That(lab.Ledger.Read().Count(x => x.Type == "hard-stop")).IsEqualTo(1);
        }
        finally
        {
            stop.Cancel();
            try { await running; }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { /* The failure guard owns cancellation. */ }
        }
    }

    [Test]
    [Timeout(30_000)]
    public async Task Resume_StaleOutcomeBeforeAdmission_DoesNotSettle(CancellationToken ct)
    {
        using var lab = new CoreLab();
        var time = new FakeTimeProvider();
        var run = await lab.TaskAsync("inspect", ct);
        run.SaveWorker(new("opencode", run.Id, "worker", "old-turn", new()), lab.Ledger);
        lab.Backend.OnReattach = (_, _) => Task.FromResult<ReattachResult>(new ReattachResult.Interrupted());
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var polled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lab.Backend.OnPrompt = async (input, _) =>
        {
            lab.End(run, turn: "old-turn");
            lab.Backend.Emit(new WorkerSignal.Status(run.Id, "old-turn", "succeeded", null));
            var admission = Task.Delay(TimeSpan.FromSeconds(10), time, ct);
            entered.TrySetResult();
            await admission;
            return input.Worker with { Turn = "new-turn" };
        };
        lab.Backend.OnPoll = (_, _) => { polled.TrySetResult(); return Task.FromResult(new PollResult([])); };
        var running = lab.Scheduler(ct, time: time).ResumeAsync([run], true);
        await entered.Task.WaitAsync(ct);
        await Assert.That(run.State).IsEqualTo(TaskState.Dispatching);
        await Assert.That(lab.Backend.Calls.Any(x => x.Member == "CollectAsync")).IsFalse();
        time.Advance(TimeSpan.FromSeconds(10));
        await polled.Task.WaitAsync(ct);
        // A same-turn form acts as a barrier after both buffered stale signals were processed.
        var answered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lab.Backend.OnAnswer = (_, _) => { answered.TrySetResult(); return Task.FromResult(new AnswerResult(true, null)); };
        lab.Backend.Emit(new WorkerSignal.Form(run.Id, "new-turn", new("opencode", "worker", null, "barrier"), "Question"));
        await DriveUntilDone(answered.Task, time, ct);
        var beforeEnd = run.State;
        lab.End(run);
        await DriveUntilDone(running, time, ct);
        await Assert.That(await running).IsEqualTo(0);
        await Assert.That(beforeEnd).IsEqualTo(TaskState.Running);
        await Assert.That(lab.Ledger.Read().Count(x => x.Type == "status")).IsEqualTo(0);
        await Assert.That(lab.Backend.Calls.Count(x => x.Member == "CollectAsync")).IsEqualTo(1);
    }

    [Test]
    [Timeout(30_000)]
    public async Task PromptThrows_TaskInterruptedBeforeFailed(CancellationToken ct)
    {
        using var lab = new CoreLab();
        var run = await lab.TaskAsync("inspect", ct);
        lab.Backend.OnCreate = (_, _) => Task.FromResult(new CreateResult(new("opencode", run.Id, "worker", "turn", new()), new("none", "none", "policy")));
        lab.Backend.OnPrompt = (_, _) => throw new IOException("prompt admission response was lost");
        var interrupted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lab.Backend.OnInterrupt = (_, _) => { interrupted.TrySetResult(); return Task.FromResult(new InterruptResult(false, "interrupt sent")); };
        var running = lab.Scheduler(ct).RunAsync([run]);
        await interrupted.Task.WaitAsync(ct);
        await run.Work!;
        var stateBeforeEnd = run.State;
        var finalBeforeEnd = lab.Ledger.Read().Any(x => x.Type == "final");
        lab.End(run, "interrupted");
        var exit = await running;

        await Assert.That(stateBeforeEnd).IsEqualTo(TaskState.Running);
        await Assert.That(finalBeforeEnd).IsFalse();
        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(run.State).IsEqualTo(TaskState.Failed);
        await Assert.That(lab.Backend.Calls.Any(x => x.Member == "ReleaseAsync")).IsFalse();
    }

    [Test]
    [Timeout(30_000)]
    public async Task AskAfterTaskEnded_IsRejected(CancellationToken ct)
    {
        using var lab = new CoreLab();
        var ended = await lab.TaskAsync("ended", ct);
        var live = await lab.TaskAsync("live", ct);
        var collecting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishCollect = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lab.Backend.OnPoll = (_, _) => { if (ended.Worker?.Turn is not null) lab.End(ended); return Task.FromResult(new PollResult([])); };
        lab.Backend.OnCollect = async (worker, _) =>
        {
            if (worker.TaskId == ended.Id) { collecting.TrySetResult(); await finishCollect.Task.WaitAsync(ct); }
            return new("succeeded", "Report", new(), null, true, [], null, true);
        };
        var answers = new List<BlockerAnswer>();
        lab.Backend.OnAnswer = (input, _) =>
        {
            answers.Add(input.Answer);
            if (answers.Count == 2) { finishCollect.TrySetResult(); lab.End(live); }
            return Task.FromResult(new AnswerResult(true, null));
        };
        var running = lab.Scheduler(ct, max: 2).RunAsync([ended, live]);
        await collecting.Task.WaitAsync(ct);
        lab.Backend.Emit(new WorkerSignal.Ask(ended.Id, ended.Worker!.Turn!, new("opencode", ended.Worker.Id, null, "ask"), "read", ["README.md"]));
        lab.Backend.Emit(new WorkerSignal.Form(ended.Id, ended.Worker.Turn!, new("opencode", ended.Worker.Id, null, "form"), "Question"));
        await running;

        await Assert.That(answers.Count).IsEqualTo(2);
        foreach (var answer in answers)
        {
            await Assert.That(answer is BlockerAnswer.Reject).IsTrue();
            await Assert.That(((BlockerAnswer.Reject)answer).Message.ToLowerInvariant()).Contains("task has ended");
        }
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    [Timeout(30_000)]
    public async Task StopLadder_InterruptsHardStopsAndSettles(bool cancel, bool stopped, CancellationToken ct)
    {
        using var lab = new CoreLab();
        var time = new FakeTimeProvider();
        var clock = new ObservedClock(time);
        var run = await lab.TaskAsync("inspect", ct);
        run.Record.Spec.TimeoutMinutes = 1;
        var keeper = stopped ? null : await lab.TaskAsync("keeper", ct);
        if (keeper is not null) keeper.Record.Spec.TimeoutMinutes = 720;
        var prompted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var soft = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var hard = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var interruptions = new List<(InterruptKind Kind, DateTimeOffset At)>();
        lab.Backend.OnPoll = (_, _) => { prompted.TrySetResult(); return Task.FromResult(new PollResult([])); };
        lab.Backend.OnInterrupt = (input, _) =>
        {
            interruptions.Add((input.Kind, time.GetUtcNow()));
            (input.Kind == InterruptKind.Hard ? hard : soft).TrySetResult();
            return Task.FromResult(new InterruptResult(input.Kind == InterruptKind.Hard && stopped, "tree stop result"));
        };
        var running = lab.Scheduler(ct, max: 2, time: clock).RunAsync(keeper is null ? [run] : [run, keeper]);
        await prompted.Task.WaitAsync(ct);
        await File.WriteAllTextAsync(Path.Combine(run.Record.Workspace.Path!, "worker.txt"), "worker edit", ct);
        // The first minute reaches the deadline, not either hard-stop rung.
        run.CancelRequested = cancel;
        await clock.AdvanceWhenIdle(TimeSpan.FromMinutes(1), ct);
        await soft.Task.WaitAsync(TimeSpan.FromSeconds(2), ct);
        await Assert.That(interruptions[0].Kind).IsEqualTo(cancel ? InterruptKind.Cancel : InterruptKind.Timeout);
        await Assert.That(run.IsFinal).IsFalse();
        await clock.AdvanceWhenIdle(TimeSpan.FromSeconds(30), ct);
        await clock.WaitForIdle(ct);
        await Assert.That(interruptions.Count).IsEqualTo(2);
        await Assert.That(interruptions[1].Kind).IsEqualTo(interruptions[0].Kind);
        time.Advance(TimeSpan.FromSeconds(90) - TimeSpan.FromMilliseconds(20));
        await clock.WaitForIdle(ct);
        await Assert.That(hard.Task.IsCompleted).IsFalse();
        time.Advance(TimeSpan.FromMilliseconds(20));
        await hard.Task.WaitAsync(TimeSpan.FromSeconds(2), ct);
        await Assert.That(interruptions.Single(x => x.Kind == InterruptKind.Hard).At - interruptions[0].At).IsEqualTo(TimeSpan.FromMinutes(2));
        if (!stopped)
        {
            await Assert.That(run.IsFinal).IsFalse();
            await clock.AdvanceWhenIdle(TimeSpan.FromMinutes(2) - TimeSpan.FromMilliseconds(20), ct);
            await clock.WaitForIdle(ct);
            await Assert.That(run.IsFinal).IsFalse();
            time.Advance(TimeSpan.FromMilliseconds(20));
            async Task WaitForFinal()
            {
                while (!run.IsFinal) { ct.ThrowIfCancellationRequested(); await Task.Yield(); }
            }
            await DriveUntilDone(WaitForFinal(), time, ct);
            var answers = new List<BlockerAnswer>();
            lab.Backend.OnAnswer = (input, _) =>
            {
                answers.Add(input.Answer);
                if (answers.Count == 2) lab.End(keeper!, "interrupted");
                return Task.FromResult(new AnswerResult(true, null));
            };
            lab.End(run);
            lab.Backend.Emit(new WorkerSignal.Retry(run.Id, run.Worker!.Turn!, 1, "late retry", null, null, null));
            lab.Backend.Emit(new WorkerSignal.Ask(run.Id, run.Worker.Turn!, new("opencode", run.Worker.Id, null, "late-ask"), "read", ["README.md"]));
            lab.Backend.Emit(new WorkerSignal.Form(run.Id, run.Worker.Turn!, new("opencode", run.Worker.Id, null, "late-form"), "Question"));
            await DriveUntilDone(running, time, ct);
            await Assert.That(answers.Count).IsEqualTo(2);
            await Assert.That(answers.All(x => x is BlockerAnswer.Reject reject && reject.Message.ToLowerInvariant().Contains("task has ended"))).IsTrue();
            await Assert.That(run.Retries).IsEqualTo(0);
            await Assert.That(lab.Backend.Calls.Any(x => x.Member == "CollectAsync" && ((WorkerRef)x.Input!).TaskId == run.Id)).IsFalse();
        }
        // Wake the loop if the previous continuation had not installed its timer before Advance.
        await DriveUntilDone(running, time, ct);
        await Assert.That(await running).IsEqualTo(1);
        var result = lab.Ledger.LoadResult(run.Id)!;
        await Assert.That(result.State).IsEqualTo(cancel ? "cancelled" : "failed");
        await Assert.That(result.Outcome).IsEqualTo(cancel ? "cancelled" : "timeout");
        await Assert.That(result.WorkerStopped).IsEqualTo(stopped);
        await Assert.That(result.Failures.Contains("zombie")).IsEqualTo(!stopped);
        await Assert.That(result.Failures.Contains("outcome:timeout")).IsEqualTo(!cancel);
        await Assert.That(result.Commit is not null).IsEqualTo(stopped);
        var data = lab.Ledger.Read().Single(x => x.Type == "hard-stop").Data!;
        await Assert.That(data.Select(x => x.Key).Order()).IsEquivalentTo(["detail", "kind", "stopped"]);
        await Assert.That(data["kind"]!.GetValue<string>()).IsEqualTo(cancel ? "cancel" : "timeout");
        await Assert.That(data["stopped"]!.GetValue<bool>()).IsEqualTo(stopped);
        await Assert.That(data["detail"]!.GetValue<string>()).IsEqualTo("tree stop result");
    }

    static async Task DriveUntilDone(Task running, FakeTimeProvider time, CancellationToken ct)
    {
        using var guard = CancellationTokenSource.CreateLinkedTokenSource(ct);
        guard.CancelAfter(TimeSpan.FromSeconds(5));
        while (!running.IsCompleted)
        {
            guard.Token.ThrowIfCancellationRequested();
            time.Advance(TimeSpan.FromMilliseconds(20));
            await Task.Yield();
        }
    }

    sealed class ObservedClock(FakeTimeProvider inner) : TimeProvider
    {
        readonly Channel<bool> idle = Channel.CreateUnbounded<bool>();
        public override DateTimeOffset GetUtcNow() => inner.GetUtcNow();
        public override long GetTimestamp() => inner.GetTimestamp();
        public override long TimestampFrequency => inner.TimestampFrequency;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = inner.CreateTimer(callback, state, dueTime, period);
            idle.Writer.TryWrite(true);
            return timer;
        }
        public async Task WaitForIdle(CancellationToken ct) => _ = await idle.Reader.ReadAsync(ct);
        public async Task AdvanceWhenIdle(TimeSpan amount, CancellationToken ct)
        {
            await WaitForIdle(ct);
            inner.Advance(amount);
        }
    }

    [Test]
    [Arguments("dispatch")]
    [Arguments("settle")]
    [Arguments("signal")]
    [Arguments("interrupt")]
    [Timeout(30_000)]
    public async Task LedgerWriteFails_SupervisorAbortsCleanly(string stage, CancellationToken ct)
    {
        using var lab = new CoreLab();
        var run = await lab.TaskAsync("inspect", ct);
        lab.Ledger.Write(run.Id, "queued");
        FileStream? held = null;
        void BlockLedger() => held = new FileStream(Path.Combine(lab.Ledger.Dir, "events.jsonl"), FileMode.Open, FileAccess.Read, FileShare.None);
        lab.Backend.OnPrompt = (input, _) =>
        {
            if (stage == "dispatch") BlockLedger();
            return Task.FromResult(input.Worker with { Turn = "turn" });
        };
        lab.Backend.OnPoll = (_, _) =>
        {
            if (stage == "interrupt") run.CancelRequested = true;
            else if (stage == "signal")
            {
                BlockLedger();
                lab.Backend.Emit(new WorkerSignal.Status(run.Id, "turn", "succeeded", null));
            }
            else lab.End(run);
            return Task.FromResult(new PollResult([]));
        };
        lab.Backend.OnCollect = (_, _) =>
        {
            if (stage == "settle") BlockLedger();
            return Task.FromResult(new WorkerReport("succeeded", "Report", new(), null, true, [], null, true));
        };
        lab.Backend.OnInterrupt = (_, _) =>
        {
            if (stage == "interrupt") BlockLedger();
            return Task.FromResult(new InterruptResult(false, "interrupt"));
        };
        try
        {
            var exit = await lab.Scheduler(ct).RunAsync([run]);
            await Assert.That(exit).IsEqualTo(1);
            await Assert.That(run.State is TaskState.Settling or TaskState.Dispatching).IsFalse();
        }
        finally { held?.Dispose(); }
    }
}
