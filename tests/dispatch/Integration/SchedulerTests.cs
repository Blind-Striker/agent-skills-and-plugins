using Dispatch.Tests.Support;

namespace Dispatch.Tests.Integration;

[Category("Integration")]
[NotInParallel]
public sealed class SchedulerTests
{
    [Test]
    [Timeout(30_000)]
    public async Task Unprompted_task_with_recorded_contracts_refuses_missing_original_text(CancellationToken ct)
    {
        using var lab = new CoreLab();
        var run = await lab.TaskAsync("inspect", ct);
        run.Record.Spec.RawPrompt = false;
        run.Record.Contracts = [new() { Path = "contract.txt", Sha256 = "recorded-hash" }];
        lab.Backend.OnPoll = (live, _) => { if (live.Count > 0) lab.End(run); return Task.FromResult(new PollResult([])); };

        var exit = await lab.Scheduler(ct).RunAsync([run]);

        await Assert.That(lab.Backend.Calls.Any(x => x.Member == "PromptAsync")).IsFalse();
        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(run.State).IsEqualTo(TaskState.Failed);
        await Assert.That(lab.Ledger.Read().Single(x => x.Type == "final").Data!["reason"]!.GetValue<string>()).Contains("original contract text is unavailable");
        await Assert.That(lab.Backend.Calls.Any(x => x.Member == "ReleaseAsync")).IsTrue();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    [Timeout(30_000)]
    public async Task Resume_continue_records_the_exact_prompt_sent_to_the_backend(bool raw, CancellationToken ct)
    {
        using var lab = new CoreLab();
        var run = await lab.TaskAsync("inspect", ct);
        run.Record.Spec.RawPrompt = raw;
        run.SaveWorker(new("opencode", run.Id, "worker", "old-turn", new()), lab.Ledger);
        lab.Backend.OnReattach = (_, _) => Task.FromResult<ReattachResult>(new ReattachResult.Interrupted());
        WorkerPrompt? sent = null;
        lab.Backend.OnPrompt = (input, _) =>
        {
            sent = input.Prompt;
            return Task.FromResult(input.Worker with { Turn = "new-turn" });
        };
        lab.Backend.OnPoll = (live, _) => { if (live.Count > 0) lab.End(run); return Task.FromResult(new PollResult([])); };

        var exit = await lab.Scheduler(ct).ResumeAsync([run], true);

        var expected = raw ? "Continue the task from where you stopped."
            : "Continue the task from where you stopped. The standing instructions and the task above still apply.";
        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(sent!.Text).IsEqualTo(expected);
        await Assert.That(sent.IsContinue).IsTrue();
        var data = lab.Ledger.Read().Single(x => x.Type == "prompt").Data!;
        await Assert.That(data["turn"]?.GetValue<string>()).IsEqualTo("new-turn");
        await Assert.That(data["chars"]?.GetValue<int>()).IsEqualTo(expected.Length);
        await Assert.That(data["sha256"]?.GetValue<string>()).IsEqualTo(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(expected))).ToLowerInvariant());
        await Assert.That(data["continue"]?.GetValue<bool>()).IsTrue();
        await Assert.That(data["rawPrompt"]?.GetValue<bool>()).IsEqualTo(raw ? true : (bool?)null);
        await Assert.That(data["promptId"]?.GetValue<string>()).IsEqualTo("new-turn");
        await Assert.That(data["resume"]?.GetValue<bool>()).IsTrue();
    }

    [Test]
    [Timeout(30_000)]
    public async Task Allow_list_is_rechecked_before_worker_creation(CancellationToken ct)
    {
        using var lab = new CoreLab();
        var run = await lab.TaskAsync("inspect", ct);
        lab.Backend.OnPoll = (live, _) => { if (live.Count > 0) lab.End(run); return Task.FromResult(new PollResult([])); };
        var exit = await lab.Scheduler(ct, allow: AllowList.From(new RouteRestriction())).RunAsync([run]);

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(run.State).IsEqualTo(TaskState.Failed);
        await Assert.That(lab.Backend.Calls.Any(x => x.Member == "CreateAsync")).IsFalse();
        await Assert.That(lab.Ledger.Read().Single(x => x.Type == "final").Data!["reason"]!.GetValue<string>()).Contains("allow-list");
    }

    [Test]
    [Timeout(30_000)]
    public async Task Post_prompt_poll_error_keeps_the_worker_running_until_ended(CancellationToken ct)
    {
        using var lab = new CoreLab();
        var run = await lab.TaskAsync("inspect", ct);
        var polled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lab.Backend.OnPoll = (_, _) => { polled.TrySetResult(); throw new IOException("transient poll failure"); };
        var running = lab.Scheduler(ct).RunAsync([run]);
        await polled.Task.WaitAsync(ct);
        await run.Work!;
        var stateAfterError = run.State;
        var finalAfterError = lab.Ledger.Read().Any(x => x.Type == "final");
        lab.End(run);
        await running;

        await Assert.That(stateAfterError).IsEqualTo(TaskState.Running);
        await Assert.That(finalAfterError).IsFalse();
        await Assert.That(lab.Backend.Calls.Any(x => x.Member == "ReleaseAsync")).IsFalse();
        await Assert.That(run.State).IsEqualTo(TaskState.Done);
        await Assert.That(lab.Ledger.Read().Count(x => x.Type == "final")).IsEqualTo(1);
    }

    [Test]
    [Timeout(30_000)]
    public async Task Cancel_while_dispatching_waits_for_prompt_then_interrupts_before_final(CancellationToken ct)
    {
        using var lab = new CoreLab();
        var run = await lab.TaskAsync("inspect", ct);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var admit = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lab.Backend.OnCreate = async (_, _) =>
        {
            entered.TrySetResult();
            await admit.Task.WaitAsync(ct);
            return new(new("opencode", run.Id, "worker", null, new()), new("none", "none", "policy"));
        };
        var interruptedBeforeFinal = false;
        lab.Backend.OnInterrupt = (input, _) =>
        {
            interruptedBeforeFinal = !run.IsFinal && run.Worker?.Turn is not null;
            lab.End(run);
            return Task.FromResult(new InterruptResult(false, "interrupt"));
        };
        var running = lab.Scheduler(ct).RunAsync([run]);
        await entered.Task.WaitAsync(ct);
        await File.WriteAllTextAsync(Path.Combine(lab.Ledger.RunId, run.Id + ".cancel"), "cancel", ct);
        while (!lab.Ledger.Read().Any(x => x.Type == "cancel"))
        {
            ct.ThrowIfCancellationRequested();
            await Task.Yield();
        }
        var stateBeforeAdmission = run.State;
        var finalBeforeAdmission = lab.Ledger.Read().Any(x => x.Type == "final");
        admit.TrySetResult();
        var exit = await running;

        await Assert.That(stateBeforeAdmission).IsEqualTo(TaskState.Dispatching);
        await Assert.That(finalBeforeAdmission).IsFalse();
        await Assert.That(interruptedBeforeFinal).IsTrue();
        await Assert.That(run.State).IsEqualTo(TaskState.Cancelled);
        await Assert.That(lab.Ledger.LoadResult(run.Id)!.Outcome).IsEqualTo("cancelled");
        await Assert.That(exit).IsEqualTo(1);
    }

    [Test]
    [Timeout(30_000)]
    public async Task Unrequested_interruption_is_nonfinal_and_duplicate_signals_are_ignored(CancellationToken ct)
    {
        using var lab = new CoreLab();
        var run = await lab.TaskAsync("inspect", ct);
        lab.Backend.OnPoll = (live, _) =>
        {
            if (live.Count > 0)
            {
                var retry = new WorkerSignal.Retry(run.Id, run.Worker!.Turn!, 1, "retry", null, null, null);
                lab.Backend.Emit(retry);
                lab.Backend.Emit(retry);
                lab.End(run, "interrupted");
                lab.End(run, "interrupted");
            }
            return Task.FromResult(new PollResult([]));
        };
        var exit = await lab.Scheduler(ct).RunAsync([run]);

        await Assert.That(run.State).IsEqualTo(TaskState.Interrupted);
        await Assert.That(run.IsFinal).IsFalse();
        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(run.Retries).IsEqualTo(1);
        await Assert.That(lab.Ledger.Read().Count(x => x.Type == "interrupted")).IsEqualTo(1);
        await Assert.That(lab.Ledger.Read().Single(x => x.Type == "interrupted").Data!["reason"]!.GetValue<string>()).IsEqualTo("unknown");
        await Assert.That(lab.Backend.Calls.Any(x => x.Member == "CollectAsync")).IsFalse();
        await Assert.That(lab.Ledger.Read().Any(x => x.Type == "final")).IsFalse();
    }

    [Test]
    [Timeout(30_000)]
    public async Task Max_parallel_counts_starting_running_and_settling_tasks(CancellationToken ct)
    {
        using var lab = new CoreLab();
        var first = await lab.TaskAsync("first", ct);
        var second = await lab.TaskAsync("second", ct);
        var firstReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lab.Backend.OnPoll = (live, _) =>
        {
            foreach (var worker in live) lab.Backend.Emit(new WorkerSignal.Form(worker.TaskId, worker.Turn!, new("opencode", worker.Id, null, worker.TaskId), "Ready"));
            return Task.FromResult(new PollResult([]));
        };
        lab.Backend.OnAnswer = (input, _) =>
        {
            (input.Blocker.AskId == "first" ? firstReady : secondReady).TrySetResult();
            return Task.FromResult(new AnswerResult(true, null));
        };
        var collecting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishCollect = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lab.Backend.OnCollect = async (worker, _) =>
        {
            if (worker.TaskId == "first") { collecting.TrySetResult(); await finishCollect.Task.WaitAsync(ct); }
            return new("succeeded", "Report", new(), null, true, [], null, true);
        };
        var running = lab.Scheduler(ct).RunAsync([first, second]);
        await firstReady.Task.WaitAsync(ct);
        var startsWhileRunning = lab.Backend.Calls.Count(x => x.Member == "CreateAsync");
        lab.End(first);
        await collecting.Task.WaitAsync(ct);
        var startsWhileSettling = lab.Backend.Calls.Count(x => x.Member == "CreateAsync");
        finishCollect.TrySetResult();
        await secondReady.Task.WaitAsync(ct);
        lab.End(second);
        var exit = await running;

        await Assert.That(startsWhileRunning).IsEqualTo(1);
        await Assert.That(startsWhileSettling).IsEqualTo(1);
        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(lab.Ledger.Read().Count(x => x.Type == "final")).IsEqualTo(2);
    }

    [Test]
    [Timeout(30_000)]
    public async Task Shutdown_awaits_settlement_and_its_final_ledger_event(CancellationToken ct)
    {
        using var lab = new CoreLab();
        var run = await lab.TaskAsync("inspect", ct);
        using var shutdown = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var collecting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lab.Backend.OnPoll = (live, _) => { if (live.Count > 0) lab.End(run); return Task.FromResult(new PollResult([])); };
        CancellationToken collectToken = default;
        lab.Backend.OnCollect = async (_, token) =>
        {
            collectToken = token;
            collecting.TrySetResult();
            await release.Task.WaitAsync(ct);
            return new("succeeded", "Report", new(), null, true, [], null, true);
        };
        var running = lab.Scheduler(shutdown.Token).RunAsync([run]);
        await collecting.Task.WaitAsync(ct);
        shutdown.Cancel();
        // The aborted event proves the loop reached shutdown, rather than merely being unscheduled.
        while (!lab.Ledger.Read().Any(x => x.Type == "aborted"))
        {
            ct.ThrowIfCancellationRequested();
            await Task.Yield();
        }
        // A held backend read must prevent shutdown completion throughout this observation window.
        var returnedWhileCollecting = await Task.WhenAny(running, Task.Delay(100, ct)) == running;
        release.TrySetResult();
        await Assert.That(async () => await running).Throws<OperationCanceledException>();
        await run.Work!;

        await Assert.That(returnedWhileCollecting).IsFalse();
        await Assert.That(collectToken.CanBeCanceled).IsFalse();
        await Assert.That(lab.Ledger.Read().Last().Type).IsEqualTo("final");
        await Assert.That(run.State).IsEqualTo(TaskState.Done);
    }

    [Test]
    [Timeout(30_000)]
    public async Task Resume_continue_ignores_ended_and_status_from_an_earlier_turn(CancellationToken ct)
    {
        using var lab = new CoreLab();
        var run = await lab.TaskAsync("inspect", ct);
        run.SaveWorker(new("opencode", run.Id, "worker", "old-turn", new()), lab.Ledger);
        var marker = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lab.Backend.OnReattach = (_, _) => Task.FromResult<ReattachResult>(new ReattachResult.Interrupted());
        lab.Backend.OnPrompt = (input, _) =>
        {
            lab.Backend.Emit(new WorkerSignal.Status(run.Id, "old-turn", "succeeded", null));
            lab.End(run, turn: "old-turn");
            lab.Backend.Emit(new WorkerSignal.Form(run.Id, "new-turn", new("opencode", "worker", null, "marker"), "Question"));
            return Task.FromResult(input.Worker with { Turn = "new-turn" });
        };
        lab.Backend.OnAnswer = (_, _) => { marker.TrySetResult(); return Task.FromResult(new AnswerResult(true, null)); };
        var running = lab.Scheduler(ct).ResumeAsync([run], true);
        await marker.Task.WaitAsync(ct);
        var stateAfterStale = run.State;
        var staleStatuses = lab.Ledger.Read().Count(x => x.Type == "status");
        lab.End(run);
        await running;

        await Assert.That(stateAfterStale).IsEqualTo(TaskState.Running);
        await Assert.That(staleStatuses).IsEqualTo(0);
        await Assert.That(lab.Backend.Calls.Count(x => x.Member == "CollectAsync")).IsEqualTo(1);
        await Assert.That(run.Record.Session.Turn).IsEqualTo("new-turn");
    }

    [Test]
    [Timeout(30_000)]
    public async Task Prompt_failure_releases_the_saved_worker_before_final(CancellationToken ct)
    {
        using var lab = new CoreLab();
        var run = await lab.TaskAsync("inspect", ct);
        var savedBeforePrompt = false;
        var releasedBeforeFinal = false;
        lab.Backend.OnPrompt = (input, _) =>
        {
            savedBeforePrompt = lab.Ledger.LoadRecord(run.Id).Session.Id == input.Worker.Id;
            throw new IOException("prompt failed");
        };
        lab.Backend.OnRelease = (_, _) =>
        {
            releasedBeforeFinal = !run.IsFinal && !lab.Ledger.Read().Any(x => x.Type == "final");
            return Task.CompletedTask;
        };

        var exit = await lab.Scheduler(ct).RunAsync([run]);

        await Assert.That(savedBeforePrompt).IsTrue();
        await Assert.That(releasedBeforeFinal).IsTrue();
        await Assert.That(run.State).IsEqualTo(TaskState.Failed);
        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(lab.Backend.Calls.Select(x => x.Member)).IsEquivalentTo(["CreateAsync", "PromptAsync", "InterruptAsync", "ReleaseAsync"]);
    }
}
