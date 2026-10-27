using Dispatch.Tests.Support;
using Microsoft.Extensions.Time.Testing;
using System.Text.Json;
using System.Text.Json.Nodes;
using TUnit.Assertions.Enums;

namespace Dispatch.Tests.Integration;

[Category("Integration")]
[NotInParallel]
public sealed class SchedulerTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    [Timeout(30_000)]
    public async Task Zombie_admission_records_stop_evidence_under_the_run_lock(bool deadline, CancellationToken ct)
    {
        using var lab = new CoreLab();
        var run = await lab.TaskAsync("inspect", ct);
        run.Record.Settings.Backend = new() { Value = "codex" };
        run.SaveWorker(new("codex", run.Id, "worker", "turn", new()), lab.Ledger);
        run.State = TaskState.Running;
        var time = new FakeTimeProvider();
        await using var backend = new FakeBackend("codex", new("sandbox", "approval-gate", "native", false));
        backend.OnPoll = (_, _) => Task.FromResult(new PollResult([]));
        backend.OnCollect = (_, _) => Task.FromResult(new WorkerReport("lost", null, new(), null, false, [], null, false));
        if (deadline)
        {
            run.CancelRequested = true;
            run.StopKind = InterruptKind.Cancel;
            run.StopStarted = time.GetUtcNow().AddMinutes(-4);
            run.HardStopSent = true;
        }
        else backend.Emit(new WorkerSignal.StopUnconfirmed(run.Id, "turn", "lost", "process survives"));
        var scheduler = new Scheduler(new Dictionary<string, IWorkerBackend> { [backend.Name] = backend }, lab.Ledger,
            lab.Tree.Repository, AllowList.From(null), lab.Policy, 1, ct, lab.Git, time);
        var started = new TaskCompletionSource<Task<int>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { started.SetResult(scheduler.RunAsync([run])); }
            catch (Exception error) { started.SetException(error); }
        });
        string[] before;
        bool blockedWhileAlive;
        lock (run)
        {
            thread.Start();
            // Both versions block on this monitor: the base writes the contradictory evidence first.
            while (thread.IsAlive && (thread.ThreadState & System.Threading.ThreadState.WaitSleepJoin) == 0) Thread.Yield();
            blockedWhileAlive = thread.IsAlive && (thread.ThreadState & System.Threading.ThreadState.WaitSleepJoin) != 0;
            before = lab.Ledger.Read().Select(line => line.Type).ToArray();
        }
        thread.Join();
        var running = await started.Task;
        while (!running.IsCompleted)
        {
            time.Advance(TimeSpan.FromMilliseconds(20));
            await Task.Yield();
        }
        await running;
        await Assert.That(blockedWhileAlive).IsTrue();
        await Assert.That(before).IsEmpty();
        var result = lab.Ledger.LoadResult(run.Id)!;
        await Assert.That(result.Failures).IsEquivalentTo(deadline ? new[] { "zombie" } : new[] { "zombie", "outcome:lost", "no-final-text" }, CollectionOrdering.Matching);
        await Assert.That(result.WorkerStopped).IsFalse();
        await Assert.That(lab.Ledger.Read().Select(line => line.Type)).IsEquivalentTo(new[] { "stop-unconfirmed", "outcome", "final" }, CollectionOrdering.Matching);
    }

    [Test]
    [Timeout(30_000)]
    public async Task Late_hard_stop_cannot_change_a_settlement_already_in_progress(CancellationToken ct)
    {
        using var lab = new CoreLab();
        var run = await lab.TaskAsync("inspect", ct);
        run.Record.Settings.Backend = new() { Value = "codex" };
        run.SaveWorker(new("codex", run.Id, "worker", "turn", new()), lab.Ledger);
        run.State = TaskState.Running;
        run.CancelRequested = true;
        run.StopKind = InterruptKind.Cancel;
        var time = new FakeTimeProvider();
        run.StopStarted = time.GetUtcNow().AddMinutes(-2);
        var hardStop = new TaskCompletionSource<InterruptResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var collecting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var report = new TaskCompletionSource<WorkerReport>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var backend = new FakeBackend("codex", new("sandbox", "approval-gate", "native", false));
        backend.OnPoll = (_, _) => Task.FromResult(new PollResult([]));
        backend.OnInterrupt = (_, _) =>
        {
            backend.Emit(new WorkerSignal.StopUnconfirmed(run.Id, "turn", "interrupted", "turn ended; process survives"));
            return hardStop.Task;
        };
        backend.OnCollect = (_, _) => { collecting.SetResult(); return report.Task; };
        var scheduler = new Scheduler(new Dictionary<string, IWorkerBackend> { [backend.Name] = backend }, lab.Ledger,
            lab.Tree.Repository, AllowList.From(null), lab.Policy, 1, ct, lab.Git, time);
        var running = scheduler.RunAsync([run]);
        try
        {
            while (!collecting.Task.IsCompleted)
            {
                time.Advance(TimeSpan.FromMilliseconds(20));
                await Task.Yield();
            }
            await Assert.That(run.State).IsEqualTo(TaskState.Settling);
            hardStop.SetResult(new(true, "late hard stop confirmed"));
            await run.InterruptWork!;
            await Assert.That(run.WorkerStopped).IsFalse();
        }
        finally
        {
            hardStop.TrySetResult(new(true, "late hard stop confirmed"));
            report.SetResult(new("interrupted", "Partial report", new(), null, true, [], null, true, StopResult: new(true, "late stop reached the report")));
            while (!running.IsCompleted)
            {
                time.Advance(TimeSpan.FromMilliseconds(20));
                await Task.Yield();
            }
            await running;
        }
        var result = lab.Ledger.LoadResult(run.Id)!;
        await Assert.That(result.Failures).IsEquivalentTo(new[] { "zombie" }, CollectionOrdering.Matching);
        await Assert.That(result.WorkerStopped).IsFalse();
        await Assert.That(lab.Ledger.Read().Select(line => line.Type)).IsEquivalentTo(new[] { "stop-unconfirmed", "outcome", "final" }, CollectionOrdering.Matching);
    }

    [Test]
    [Timeout(30_000)]
    public async Task Unconfirmed_normal_release_prevents_commit_and_verify_before_final_success(CancellationToken ct)
    {
        using var lab = new CoreLab("release-before-settle");
        var run = await lab.TaskAsync("inspect", ct);
        run.Record.Spec.Kind = "code";
        run.Record.Spec.Base = "main";
        run.Record.Spec.Scope = ["a.txt"];
        run.Record.Settings.Backend = new() { Value = "codex" };
        run.Record.Workspace = new();
        await Worktree.PrepareAsync(run, lab.Ledger, lab.Tree.Repository, ct, lab.Git);
        run.SaveWorker(new("codex", run.Id, "worker", "turn", new()), lab.Ledger);
        run.State = TaskState.Running;
        var before = await lab.Git.CheckAsync(run.Record.Workspace.Path!, ct, "rev-parse", "HEAD");
        File.WriteAllText(Path.Combine(run.Record.Workspace.Path!, "a.txt"), "worker bytes");
        var marker = Path.Combine(lab.Tree.Root, "verify-ran");
        run.Record.Spec.Verify = [OperatingSystem.IsWindows() ? $"Set-Content -LiteralPath '{marker}' -Value ran" : $"touch '{marker}'"];
        await using var backend = new FakeBackend("codex", new("sandbox", "approval-gate", "native", false));
        backend.OnCollect = (_, _) => Task.FromResult(new WorkerReport("succeeded", "Completed.", new(), null, true, [], null, true));
        backend.OnRelease = (_, _) => Task.FromResult<InterruptResult?>(new(false, "pid=1972 still alive"));
        backend.Emit(new WorkerSignal.Ended(run.Id, "turn", "succeeded", null));
        var scheduler = new Scheduler(new Dictionary<string, IWorkerBackend> { ["codex"] = backend }, lab.Ledger,
            lab.Tree.Repository, AllowList.From(null), lab.Policy, 1, ct, lab.Git);
        await Assert.That(await scheduler.RunAsync([run])).IsEqualTo(1);
        var result = lab.Ledger.LoadResult(run.Id)!;
        await Assert.That(result.Failures).IsEquivalentTo(new[] { "zombie" }, CollectionOrdering.Matching);
        await Assert.That(result.State).IsEqualTo("failed");
        await Assert.That(result.Verify).IsEmpty();
        await Assert.That(result.Commit).IsNull();
        await Assert.That(File.Exists(marker)).IsFalse();
        await Assert.That(File.ReadAllText(Path.Combine(run.Record.Workspace.Path!, "a.txt"))).IsEqualTo("worker bytes");
        await Assert.That(await lab.Git.CheckAsync(run.Record.Workspace.Path!, ct, "rev-parse", "HEAD")).IsEqualTo(before);
        await Assert.That(lab.Ledger.Read().Select(x => x.Type)).IsEquivalentTo(new[] { "worktree", "stop-unconfirmed", "outcome", "final" }, CollectionOrdering.Matching);
        await Assert.That(backend.Calls.Select(x => x.Member)).IsEquivalentTo(new[] { "CollectAsync", "ReleaseAsync" }, CollectionOrdering.Matching);
    }

    [Test]
    [Timeout(30_000)]
    public async Task Unconfirmed_failed_start_release_settles_zombie_and_sibling_done(CancellationToken ct)
    {
        using var lab = new CoreLab();
        var bad = await lab.TaskAsync("bad", ct);
        var good = await lab.TaskAsync("good", ct);
        foreach (var run in new[] { bad, good }) run.Record.Settings.Backend = new() { Value = "codex" };
        await using var backend = new FakeBackend("codex", new("sandbox", "approval-gate", "native", false));
        backend.OnCreate = (launch, _) => launch.TaskId == bad.Id
            ? throw new WorkerCreationException(new("codex", bad.Id, "partial", null, new() { ["pid"] = 1972 }), "initialize failed")
            : Task.FromResult(new CreateResult(new("codex", good.Id, "worker-good", null, new()), new("sandbox", "sandbox", "approval-gate")));
        backend.OnPrompt = (input, _) => Task.FromResult(input.Worker with { Turn = "turn" });
        backend.OnPoll = (live, _) =>
        {
            foreach (var worker in live) backend.Emit(new WorkerSignal.Ended(worker.TaskId, worker.Turn!, "succeeded", null));
            return Task.FromResult(new PollResult([]));
        };
        backend.OnInterrupt = (_, _) => Task.FromResult(new InterruptResult(false, "interrupt sent"));
        backend.OnRelease = (worker, _) => Task.FromResult<InterruptResult?>(new(worker.TaskId == good.Id, "pid=1972; still alive"));
        backend.OnCollect = (worker, _) => Task.FromResult(worker.TaskId == good.Id
            ? new WorkerReport("succeeded", "Completed.", new(), null, true, [], null, true)
            : new WorkerReport("lost", null, new(), null, false, [], null, false, StopResult: new(false, "pid=1972; still alive")));
        var scheduler = new Scheduler(new Dictionary<string, IWorkerBackend> { ["codex"] = backend }, lab.Ledger,
            lab.Tree.Repository, AllowList.From(null), lab.Policy, 2, ct, lab.Git);
        await Assert.That(await scheduler.RunAsync([bad, good])).IsEqualTo(1);
        var result = lab.Ledger.LoadResult(bad.Id)!;
        await Assert.That(result).IsNotNull();
        await Assert.That(result.State).IsEqualTo("failed");
        await Assert.That(result.Failures).IsEquivalentTo(new[] { "zombie", "outcome:unknown", "settle-error", "no-final-text" }, CollectionOrdering.Matching);
        await Assert.That(result.WorkerStopped).IsFalse();
        await Assert.That(lab.Ledger.LoadRecord(bad.Id).BackendState.Pid).IsEqualTo(1972);
        await Assert.That(lab.Ledger.Read().Where(x => x.Task == bad.Id).Select(x => x.Type))
            .IsEquivalentTo(new[] { "session", "stop-unconfirmed", "outcome", "final" }, CollectionOrdering.Matching);
        await Assert.That(lab.Ledger.Read().Single(x => x.Task == bad.Id && x.Type == "stop-unconfirmed").Data!["detail"]!.GetValue<string>()).IsEqualTo("pid=1972; still alive");
        await Assert.That(lab.Ledger.LoadResult(good.Id)!.Failures).IsEmpty();
        await Assert.That(lab.Ledger.LoadResult(good.Id)!.State).IsEqualTo("done");
        await Assert.That(lab.Ledger.Read().Where(x => x.Type == "aborted")).IsEmpty();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    [Timeout(30_000)]
    public async Task Unconfirmed_Codex_cancel_and_recovery_fail_only_their_task(bool recovery, CancellationToken ct)
    {
        using var lab = new CoreLab();
        var bad = await lab.TaskAsync("bad", ct);
        var good = await lab.TaskAsync("good", ct);
        foreach (var run in new[] { bad, good })
        {
            run.Record.Settings.Backend = new() { Value = "codex" };
            run.SaveWorker(new("codex", run.Id, "worker-" + run.Id, "turn", new()), lab.Ledger);
            run.State = TaskState.Running;
        }
        lab.Ledger.Write(bad.Id, "cancel", new());
        lab.Ledger.Write(bad.Id, "stop-unconfirmed", new() { ["outcome"] = "interrupted", ["detail"] = "pid=1972; still alive" });
        var restored = TaskRun.FromLedger(lab.Ledger.LoadRecord(bad.Id), lab.Ledger);
        await Assert.That(restored.Zombie).IsTrue();
        await Assert.That(restored.WorkerStopped).IsFalse();
        if (recovery) bad = restored;
        else
        {
            bad.CancelRequested = true;
            bad.StopKind = InterruptKind.Cancel;
            bad.StopStarted = DateTimeOffset.UtcNow;
        }
        await using var backend = new FakeBackend("codex", new("sandbox", "approval-gate", "native", false));
        backend.OnReattach = (worker, _) => Task.FromResult<ReattachResult>(new ReattachResult.Ended(worker.TaskId == good.Id ? "succeeded" : "lost",
            new(worker.TaskId == good.Id, "pid=1972; still alive")));
        backend.OnCollect = (worker, _) => Task.FromResult(new WorkerReport(worker.TaskId == good.Id ? "succeeded" : "lost",
            "Completed.", new(), null, true, [], null, true, StopResult: new(worker.TaskId == good.Id, "pid=1972; still alive")));
        backend.OnRelease = (_, _) => Task.FromResult<InterruptResult?>(new(true, "confirmed exit"));
        backend.OnPoll = (_, _) => Task.FromResult(new PollResult([]));
        if (!recovery)
        {
            backend.Emit(new WorkerSignal.StopUnconfirmed(bad.Id, "turn", "interrupted", "pid=1972; still alive"));
            backend.Emit(new WorkerSignal.Ended(good.Id, "turn", "succeeded", null));
        }
        var scheduler = new Scheduler(new Dictionary<string, IWorkerBackend> { ["codex"] = backend }, lab.Ledger,
            lab.Tree.Repository, AllowList.From(null), lab.Policy, 2, ct, lab.Git);
        await Assert.That(recovery ? await scheduler.ResumeAsync([bad, good], false) : await scheduler.RunAsync([bad, good])).IsEqualTo(1);
        var result = lab.Ledger.LoadResult(bad.Id)!;
        await Assert.That(result.Failures).IsEquivalentTo(new[] { "zombie" }, CollectionOrdering.Matching);
        await Assert.That(result.Outcome).IsEqualTo("cancelled");
        await Assert.That(result.State).IsEqualTo("failed");
        await Assert.That(bad.ReportReady).IsEqualTo(!recovery);
        await Assert.That(lab.Ledger.Read().Where(x => x.Task == bad.Id).Select(x => x.Type)).IsEquivalentTo(recovery
            ? new[] { "cancel", "stop-unconfirmed", "outcome", "final" }
            : new[] { "cancel", "stop-unconfirmed", "stop-unconfirmed", "outcome", "final" }, CollectionOrdering.Matching);
        await Assert.That(lab.Ledger.LoadResult(good.Id)!.State).IsEqualTo("done");
        await Assert.That(lab.Ledger.LoadResult(good.Id)!.Failures).IsEmpty();
        await Assert.That(lab.Ledger.Read().Where(x => x.Type == "aborted")).IsEmpty();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    [Timeout(30_000)]
    public async Task Codex_stop_evidence_is_applied_before_collection_and_can_clear_restored_uncertainty(bool confirmed, CancellationToken ct)
    {
        using var lab = new CoreLab();
        var original = await lab.TaskAsync("inspect", ct);
        original.Record.Settings.Backend = new() { Value = "codex" };
        original.SaveWorker(new("codex", original.Id, "worker", "turn", new()), lab.Ledger);
        lab.Ledger.Write(original.Id, "stop-unconfirmed", new() { ["outcome"] = "succeeded", ["detail"] = "old uncertainty" });
        var run = TaskRun.FromLedger(lab.Ledger.LoadRecord(original.Id), lab.Ledger);
        await using var backend = new FakeBackend("codex", new("sandbox", "approval-gate", "native", false));
        backend.OnCollect = (_, _) => Task.FromResult(new WorkerReport("lost", null, new(), null, false, [], "current identity check", confirmed,
            StopResult: new(confirmed, "current identity check")));
        backend.OnRelease = (_, _) => Task.FromResult<InterruptResult?>(new(true, "confirmed exit"));
        var result = await Settlement.SettleAsync(backend, lab.Ledger, run, null, ct, lab.Git);
        await Assert.That(result.State).IsEqualTo("failed");
        await Assert.That(result.Failures).IsEquivalentTo(confirmed ? new[] { "outcome:lost", "settle-error", "no-final-text" }
            : new[] { "zombie", "outcome:lost", "settle-error", "no-final-text" }, CollectionOrdering.Matching);
        await Assert.That(result.WorkerStopped).IsEqualTo(confirmed);
        await Assert.That(TaskRun.FromLedger(lab.Ledger.LoadRecord(run.Id), lab.Ledger).Zombie).IsEqualTo(!confirmed);
        await Assert.That(lab.Ledger.Read().Select(x => x.Type)).IsEquivalentTo(confirmed
            ? new[] { "stop-unconfirmed", "hard-stop", "collected", "verified", "outcome" }
            : new[] { "stop-unconfirmed", "outcome" }, CollectionOrdering.Matching);
    }

    [Test]
    [Timeout(30_000)]
    public async Task Configured_environment_reaches_prepare_and_worker(CancellationToken ct)
    {
        using var lab = new CoreLab("configured-environment");
        var run = await PrepareTaskAsync(lab, ct);
        run.Record.Settings.Environment = new()
        {
            ["DISPATCH_SAMPLE"] = new() { Value = "configured-value", From = "repo" },
            ["DISPATCH_SAMPLE_TOKEN"] = new() { Value = "synthetic-override", From = "task" },
            ["GIT_CONFIG_COUNT"] = new() { Value = "not-a-number", From = "task" },
        };
        var privateFolder = Path.Combine(lab.Tree.Root, "private-cache");
        run.Record.Settings.PrivateFolders = new() { Value = [privateFolder], From = "task" };
        run.Record.Spec.Prepare = [OperatingSystem.IsWindows() ? "$env:DISPATCH_SAMPLE; $env:DISPATCH_SAMPLE_TOKEN"
            : "printf '%s\\n' \"$DISPATCH_SAMPLE\" \"$DISPATCH_SAMPLE_TOKEN\""];
        WorkerLaunch? launch = null;
        var create = lab.Backend.OnCreate!;
        lab.Backend.OnCreate = (input, token) => { launch = input; return create(input, token); };
        EndOnPoll(lab, run);
        await Assert.That(await lab.Scheduler(ct).RunAsync([run])).IsEqualTo(0);
        var log = File.ReadAllText(Path.Combine(lab.Ledger.TaskDir(run.Id), "prepare.log")).ReplaceLineEndings("\n");
        await Assert.That(log.Split("[stdout]\n")[1].Split("[stderr]")[0].TrimEnd()).IsEqualTo("configured-value\nsynthetic-override");
        await Assert.That(launch!.Environment.Keys).Contains("DISPATCH_SAMPLE");
        await Assert.That(launch.Environment.Keys).Contains("DISPATCH_SAMPLE_TOKEN");
        await Assert.That(launch!.Environment["DISPATCH_SAMPLE"]).IsEqualTo("configured-value");
        await Assert.That(launch.Environment["DISPATCH_SAMPLE_TOKEN"]).IsEqualTo("synthetic-override");
        await Assert.That(launch.Environment["GIT_CONFIG_COUNT"]).IsEqualTo("not-a-number");
        await Assert.That(launch.ConfiguredEnvironment!["DISPATCH_SAMPLE_TOKEN"]).IsEqualTo("synthetic-override");
        await Assert.That(launch.PrivateFolders).IsEquivalentTo(new[] { privateFolder }, CollectionOrdering.Matching);
        await Assert.That(Environment.GetEnvironmentVariable("DISPATCH_SAMPLE")).IsNull();
        await Assert.That(Environment.GetEnvironmentVariable("DISPATCH_SAMPLE_TOKEN")).IsNull();
    }

    [Test]
    [Timeout(30_000)]
    public async Task Prepare_precedes_worker_creation(CancellationToken ct)
    {
        using var lab = new CoreLab("prepare-order");
        var run = await PrepareTaskAsync(lab, ct);
        var second = await lab.TaskAsync("second", ct);
        var entered = Path.Combine(lab.Tree.Root, "entered");
        var release = Path.Combine(lab.Tree.Root, "release");
        var marker = Path.Combine(Locations.TaskTemp(run.Id, lab.Ledger.RunId), "marker");
        run.Record.Spec.Prepare = [GateCommand(entered, release) + "; " + WriteCommand(marker, "final line")];
        var contents = new List<string>();
        var create = lab.Backend.OnCreate!;
        lab.Backend.OnCreate = (launch, token) => { contents.Add(File.Exists(marker) ? File.ReadAllText(marker).Trim() : "missing"); return create(launch, token); };
        lab.Backend.OnPoll = (live, _) => { foreach (var worker in live) lab.End(worker.TaskId == run.Id ? run : second); return Task.FromResult(new PollResult([])); };
        var running = lab.Scheduler(ct).RunAsync([run, second]);
        var reached = await WaitFileAsync(entered, running, ct);
        var state = run.State;
        var pending = second.State;
        var earlyCalls = lab.Backend.Calls.Select(x => x.Member).ToArray();
        File.WriteAllText(release, "go");
        await Assert.That(await running).IsEqualTo(0);
        await Assert.That(reached).IsTrue();
        await Assert.That(state).IsEqualTo(TaskState.Dispatching);
        await Assert.That(pending).IsEqualTo(TaskState.Pending);
        await Assert.That(earlyCalls).IsEmpty();
        await Assert.That(contents).IsEquivalentTo(new[] { "final line", "final line" }, CollectionOrdering.Matching);
        await Assert.That(lab.Backend.Calls.Where(x => x.Member is "CreateAsync" or "PromptAsync").Select(x => x.Member))
            .IsEquivalentTo(new[] { "CreateAsync", "PromptAsync", "CreateAsync", "PromptAsync" }, CollectionOrdering.Matching);
        await Assert.That(run.Record.Prepare!.Select(x => x.Command)).IsEquivalentTo(run.Record.Spec.Prepare, CollectionOrdering.Matching);
        await Assert.That(run.Record.Prepare![0].ExitCode).IsEqualTo(0);
        await Assert.That(run.PrepareCancellation).IsNull();
    }

    [Test]
    [Timeout(30_000)]
    public async Task Prepare_failure_stops_before_worker(CancellationToken ct)
    {
        foreach (var spawn in new[] { false, true })
        {
            using var lab = new CoreLab();
            var run = await PrepareTaskAsync(lab, ct);
            var marker = Path.Combine(lab.Tree.Root, "second");
            var first = OperatingSystem.IsWindows() ? "Write-Output first-output; exit 3" : "printf 'first-output\\n'; exit 3";
            run.Record.Spec.Prepare = [first, WriteCommand(marker, "second")];
            var scheduler = spawn ? lab.Scheduler(ct, prepareCommand: (task, command, environment, token) =>
                Verifier.RunCommandAsync(task.Record.Spec, null, Path.Combine(lab.Tree.Root, "missing-workspace"), environment, command, false, token)) : lab.Scheduler(ct);
            await Assert.That(await scheduler.RunAsync([run])).IsEqualTo(1);
            var result = lab.Ledger.LoadResult(run.Id)!;
            await Assert.That(result.State).IsEqualTo("failed");
            await Assert.That(result.Failures).IsEquivalentTo(new[] { "prepare-failed" }, CollectionOrdering.Matching);
            await Assert.That(result.Prepare!.Select(x => x.Command)).IsEquivalentTo(new[] { first }, CollectionOrdering.Matching);
            await Assert.That(result.Prepare![0].ExitCode).IsEqualTo(spawn ? -1 : 3);
            await Assert.That(result.Prepare[0].TimedOut).IsFalse();
            if (spawn) await Assert.That(result.Error).Contains("missing-workspace");
            else await Assert.That(result.Error).IsEqualTo("prepare command 1 exited 3");
            var log = File.ReadAllText(Path.Combine(lab.Ledger.TaskDir(run.Id), "prepare.log"));
            await Assert.That(log).Contains(spawn ? "missing-workspace" : "first-output");
            await Assert.That(File.Exists(marker)).IsFalse();
            await AssertPrepareTerminalAsync(lab, run, "{\"commands\":1,\"failed\":1}");
        }
    }

    [Test]
    [Timeout(60_000)]
    public async Task Prepare_rejects_workspace_changes(CancellationToken ct)
    {
        foreach (var arrangement in new[] { "tracked", "untracked", "dirty", "ignored", "many", "unicode", "leading-space" })
        {
            using var lab = new CoreLab();
            var run = await PrepareTaskAsync(lab, ct);
            var repo = new LabRepo(lab.Tree);
            File.WriteAllText(Path.Combine(repo.Root, ".gitignore"), "obj/\nbin/\nnode_modules/\n.pkgs/\n");
            await repo.GitAsync(["add", ".gitignore"], ct);
            await repo.GitAsync(["commit", "-m", "Ignore dependency output"], ct);
            if (arrangement == "dirty") File.WriteAllText(Path.Combine(repo.Root, "README.md"), "already dirty");
            var paths = arrangement == "many" ? Enumerable.Range(1, 25).Reverse().Select(n => $"path-{n}.txt").ToArray()
                : arrangement == "ignored" ? new[] { "obj/marker", "bin/marker", "node_modules/marker", ".pkgs/marker" }
                : new[] { arrangement == "leading-space" ? " leading.txt" : arrangement == "unicode" ? "ışık-世界.txt" : arrangement == "untracked" ? "new.txt" : "README.md" };
            foreach (var path in paths) Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(repo.Root, path))!);
            run.Record.Spec.Prepare = [string.Join("; ", paths.Select(path => WriteCommand(path, "changed")))];
            EndOnPoll(lab, run);
            await Assert.That(await lab.Scheduler(ct).RunAsync([run])).IsEqualTo(arrangement == "ignored" ? 0 : 1);
            var result = lab.Ledger.LoadResult(run.Id)!;
            await Assert.That(result.Prepare!.Select(x => x.ExitCode)).IsEquivalentTo(new[] { 0 }, CollectionOrdering.Matching);
            if (arrangement == "ignored")
            {
                await Assert.That(result.State).IsEqualTo("done");
                await Assert.That(run.Record.PrepareDone).IsTrue();
                await Assert.That(File.ReadAllText(Path.Combine(repo.Root, "obj", "marker")).Trim()).IsEqualTo("changed");
            }
            else
            {
                await Assert.That(result.Failures).IsEquivalentTo(new[] { "prepare-changed-workspace" }, CollectionOrdering.Matching);
                var expected = arrangement == "many"
                    ? "path-1.txt, path-10.txt, path-11.txt, path-12.txt, path-13.txt, path-14.txt, path-15.txt, path-16.txt, path-17.txt, path-18.txt, path-19.txt, path-2.txt, path-20.txt, path-21.txt, path-22.txt, path-23.txt, path-24.txt, path-25.txt, path-3.txt, path-4.txt, and 5 more"
                    : paths[0];
                await Assert.That(result.Error).IsEqualTo(expected);
                await AssertPrepareTerminalAsync(lab, run, "{\"commands\":1,\"failed\":0,\"changed\":true}");
            }
        }
    }

    [Test]
    [Timeout(60_000)]
    public async Task Prepare_snapshot_failures_are_terminal(CancellationToken ct)
    {
        foreach (var baseline in new[] { true, false })
        {
            using var lab = new CoreLab();
            var run = await PrepareTaskAsync(lab, ct);
            var marker = Path.Combine(lab.Tree.Root, "command");
            run.Record.Spec.Prepare = [WriteCommand(marker, "ran")];
            var scheduler = lab.Scheduler(ct, prepareSnapshot: (git, cwd, task, start, token) => start == baseline
                ? throw new HarnessException("git fixture capture failed") : Snapshot.CaptureAsync(git, cwd, task, start, token));
            await Assert.That(await scheduler.RunAsync([run])).IsEqualTo(1);
            var result = lab.Ledger.LoadResult(run.Id)!;
            await Assert.That(result.Error).IsEqualTo("git fixture capture failed");
            await Assert.That(result.Failures).IsEquivalentTo(new[] { "prepare-failed" }, CollectionOrdering.Matching);
            await Assert.That(result.Prepare!.Select(x => x.Command)).IsEquivalentTo(baseline ? [] : run.Record.Spec.Prepare, CollectionOrdering.Matching);
            await Assert.That(File.Exists(marker)).IsEqualTo(!baseline);
            await AssertPrepareTerminalAsync(lab, run, baseline ? "{\"commands\":0,\"failed\":0,\"checkFailed\":true}" : "{\"commands\":1,\"failed\":0,\"checkFailed\":true}");
        }
    }

    [Test]
    [Timeout(30_000)]
    public async Task Prepare_short_command_succeeds(CancellationToken ct)
    {
        using var lab = new CoreLab();
        var run = await PrepareTaskAsync(lab, ct);
        run.Record.Spec.Prepare = ["exit 0"];
        EndOnPoll(lab, run);
        await Assert.That(await lab.Scheduler(ct).ResumeAsync([run], true)).IsEqualTo(0);
        await Assert.That(run.Record.PrepareDone).IsTrue();
        await Assert.That(run.Record.PrepareRunning).IsFalse();
        await Assert.That(run.Record.Prepare!.Select(x => x.Command)).IsEquivalentTo(new[] { "exit 0" }, CollectionOrdering.Matching);
        await Assert.That(run.Record.Prepare![0].ExitCode).IsEqualTo(0);
        await Assert.That(run.Record.Prepare[0].Advisory).IsNull();
        await Assert.That(run.Record.Prepare[0].NotRun).IsNull();
        await Assert.That(lab.Ledger.Read().Single(x => x.Type == "prepared").Data!.ToJsonString()).IsEqualTo("{\"commands\":1,\"failed\":0,\"changed\":false}");
        await Assert.That(lab.Backend.Calls.Count(x => x.Member == "CreateAsync")).IsEqualTo(1);
        // A completed, sessionless preparation is not replayed when startup resumes.
        var saved = lab.Ledger.LoadRecord(run.Id);
        saved.Session = new();
        var resumed = new TaskRun { Record = saved };
        EndOnPoll(lab, resumed);
        await Assert.That(await lab.Scheduler(ct, prepareCommand: (_, _, _, _) => throw new InvalidOperationException("replayed")).ResumeAsync([resumed], true)).IsEqualTo(0);
        await Assert.That(lab.Ledger.Read().Count(x => x.Type == "prepared")).IsEqualTo(1);
        foreach (var commands in new List<string>?[] { null, [] })
        {
            using var emptyLab = new CoreLab();
            var emptyRun = await PrepareTaskAsync(emptyLab, ct);
            emptyRun.Record.Spec.Prepare = commands;
            EndOnPoll(emptyLab, emptyRun);
            await Assert.That(await emptyLab.Scheduler(ct).RunAsync([emptyRun])).IsEqualTo(0);
            var emptyRecord = emptyLab.Ledger.LoadRecord(emptyRun.Id);
            await Assert.That(emptyRecord.Prepare).IsNull();
            await Assert.That(emptyRecord.PrepareDone).IsNull();
            await Assert.That(emptyRecord.PrepareRunning).IsNull();
            await Assert.That(File.Exists(Path.Combine(emptyLab.Ledger.TaskDir(emptyRun.Id), "prepare.log"))).IsFalse();
            await Assert.That(emptyLab.Ledger.Read().Select(x => x.Type)).DoesNotContain("prepared");
        }
    }

    [Test]
    [Timeout(60_000)]
    public async Task Prepare_obeys_precedence_and_boundaries(CancellationToken ct)
    {
        foreach (var boundary in new[] { "between", "last", "failure", "baseline-error", "snapshot", "git-error", "changed" })
        {
            using var lab = new CoreLab();
            var run = await PrepareTaskAsync(lab, ct);
            run.Record.Spec.Prepare = boundary == "between" ? ["exit 0", "exit 0"] : boundary == "failure" ? ["exit 3"] : ["exit 0"];
            EndOnPoll(lab, run);
            var commands = new List<string>();
            var snapshots = new List<bool>();
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var scheduler = lab.Scheduler(ct, prepareCommand: async (task, command, environment, token) =>
            {
                var executed = await Verifier.RunCommandAsync(task.Record.Spec, null, task.Record.Workspace.Path!, environment, command, false, token, task.PrepareCancellation!.Token);
                commands.Add(command);
                if (boundary is "between" or "last" or "failure") { entered.SetResult(); await release.Task.WaitAsync(ct); }
                return executed;
            }, prepareSnapshot: async (git, cwd, task, start, token) =>
            {
                snapshots.Add(start);
                if (boundary == "baseline-error")
                {
                    entered.SetResult();
                    await release.Task.WaitAsync(ct);
                    throw new HarnessException("git baseline failed");
                }
                var tree = await Snapshot.CaptureAsync(git, cwd, task, start, token);
                if (!start)
                {
                    entered.SetResult();
                    await release.Task.WaitAsync(ct);
                    if (boundary == "git-error") throw new HarnessException("git comparison failed");
                    if (boundary == "changed") return new string('0', 40);
                }
                return tree;
            });
            var running = scheduler.RunAsync([run]);
            var reached = await Task.WhenAny(entered.Task, running) == entered.Task;
            run.CancelRequested = true;
            release.SetResult();
            await Assert.That(await running).IsEqualTo(1);
            await Assert.That(reached).IsTrue();
            var result = lab.Ledger.LoadResult(run.Id)!;
            await Assert.That(result.State).IsEqualTo("cancelled");
            await Assert.That(result.Failures).IsEmpty();
            await Assert.That(result.Error).IsNull();
            await Assert.That(commands).IsEquivalentTo(boundary == "baseline-error" ? [] : new[] { boundary == "failure" ? "exit 3" : "exit 0" }, CollectionOrdering.Matching);
            await Assert.That(snapshots).IsEquivalentTo(boundary is "between" or "last" or "failure" or "baseline-error" ? new[] { true } : new[] { true, false }, CollectionOrdering.Matching);
            await AssertPrepareTerminalAsync(lab, run, boundary == "baseline-error" ? "{\"commands\":0,\"failed\":0,\"cancelled\":true}" : "{\"commands\":1,\"failed\":0,\"cancelled\":true}");
        }
        foreach (var arrangement in new[] { "edit-fail", "head", "unborn" })
        {
            using var lab = new CoreLab();
            var run = await PrepareTaskAsync(lab, ct);
            var repo = new LabRepo(lab.Tree);
            var before = await repo.GitAsync(["rev-parse", "HEAD"], ct);
            string? after = null;
            if (arrangement == "head") after = await repo.GitAsync(["commit-tree", "HEAD^{tree}", "-p", "HEAD", "-m", "Same tree"], ct);
            if (arrangement == "unborn") await repo.GitAsync(["symbolic-ref", "HEAD", "refs/heads/unborn"], ct);
            run.Record.Spec.Prepare = [arrangement == "edit-fail" ? WriteCommand("README.md", "edited") + "; exit 1"
                : arrangement == "head" ? $"git update-ref HEAD {after}" : "exit 0"];
            EndOnPoll(lab, run);
            await Assert.That(await lab.Scheduler(ct).RunAsync([run])).IsEqualTo(arrangement == "unborn" ? 0 : 1);
            var result = lab.Ledger.LoadResult(run.Id)!;
            await Assert.That(result.Prepare!.Count).IsEqualTo(1);
            if (arrangement == "unborn") await Assert.That(run.Record.PrepareDone).IsTrue();
            else
            {
                await Assert.That(result.Failures).IsEquivalentTo(new[] { arrangement == "head" ? "prepare-changed-workspace" : "prepare-failed" }, CollectionOrdering.Matching);
                await Assert.That(result.Error).IsEqualTo(arrangement == "head" ? $"prepare moved HEAD from {before} to {after}" : "prepare command 1 exited 1");
            }
        }
    }

    [Test]
    [Timeout(30_000)]
    public async Task Prepare_cancel_kills_running_command(CancellationToken ct)
    {
        using var lab = new CoreLab();
        var run = await PrepareTaskAsync(lab, ct);
        var entered = Path.Combine(lab.Tree.Root, "pid");
        var release = Path.Combine(lab.Tree.Root, "release");
        run.Record.Spec.Prepare = [GateCommand(entered, release), "exit 0"];
        var running = lab.Scheduler(ct).RunAsync([run]);
        var reached = await WaitFileAsync(entered, running, ct);
        Json.WriteFile(Path.Combine(Locations.CancelDir(lab.Ledger.RunId), run.Id + ".json"),
            new CancelRequest { Run = lab.Ledger.RunId, Task = run.Id, Time = Json.Timestamp() }, ControlJson.Files.CancelRequest);
        var stoppedOnCancel = await Task.WhenAny(running, Task.Delay(TimeSpan.FromSeconds(5), ct)) == running;
        File.WriteAllText(release, "go");
        var exit = await running;
        await Assert.That(reached).IsTrue();
        await Assert.That(stoppedOnCancel).IsTrue();
        await Assert.That(exit).IsEqualTo(1);
        var result = lab.Ledger.LoadResult(run.Id)!;
        await Assert.That(result.State).IsEqualTo("cancelled");
        await Assert.That(result.Failures).IsEmpty();
        await Assert.That(result.Prepare!.Select(x => x.Command)).IsEquivalentTo(new[] { run.Record.Spec.Prepare[0] }, CollectionOrdering.Matching);
        await Assert.That(result.Prepare![0].ExitCode).IsEqualTo(-1);
        await Assert.That(result.Prepare[0].TimedOut).IsFalse();
        await Assert.That(File.ReadAllText(Path.Combine(lab.Ledger.TaskDir(run.Id), "prepare.log"))).DoesNotContain("[harness] timed out");
        await Assert.That(ProcessStopped(int.Parse(File.ReadAllText(entered).Trim()))).IsTrue();
        await AssertPrepareTerminalAsync(lab, run, "{\"commands\":1,\"failed\":0,\"cancelled\":true}");
    }

    [Test]
    [Timeout(30_000)]
    public async Task Prepare_command_timeout_fails_task(CancellationToken ct)
    {
        using var lab = new CoreLab();
        var run = await PrepareTaskAsync(lab, ct);
        var entered = Path.Combine(lab.Tree.Root, "pid");
        var starting = Path.Combine(lab.Tree.Root, "starting");
        var start = Path.Combine(lab.Tree.Root, "start");
        run.Record.Spec.Prepare = [GateCommand(starting, start) + "; " + GateCommand(entered, Path.Combine(lab.Tree.Root, "release"))];
        var scheduler = lab.Scheduler(ct, prepareCommand: async (task, command, environment, token) =>
        {
            Proc.BeforeTimeout.Value = async (process, cancellation) =>
                await Assert.That(await WaitFileAsync(entered, process.WaitForExitAsync(CancellationToken.None), cancellation)).IsTrue();
            try
            {
                return await Verifier.RunCommandAsync(task.Record.Spec, null, task.Record.Workspace.Path!, environment, command, false, token,
                    task.PrepareCancellation!.Token, TimeSpan.FromSeconds(2));
            }
            finally { Proc.BeforeTimeout.Value = null; }
        });
        var running = scheduler.RunAsync([run]);
        var reached = await WaitFileAsync(starting, running, ct);
        // Deterministically hold startup beyond the real two-second command timeout.
        await Task.WhenAny(running, Task.Delay(TimeSpan.FromSeconds(3), ct));
        var returnedBeforeSignal = running.IsCompleted;
        File.WriteAllText(start, "go");
        var exit = await running;
        await Assert.That(reached).IsTrue();
        await Assert.That(returnedBeforeSignal).IsFalse();
        await Assert.That(exit).IsEqualTo(1);
        var result = lab.Ledger.LoadResult(run.Id)!;
        await Assert.That(result.Failures).IsEquivalentTo(new[] { "prepare-failed" }, CollectionOrdering.Matching);
        await Assert.That(result.Prepare!.Count).IsEqualTo(1);
        await Assert.That(result.Prepare[0].Command).IsEqualTo(run.Record.Spec.Prepare[0]);
        await Assert.That(result.Prepare[0].TimedOut).IsTrue();
        await Assert.That(result.Prepare[0].ExitCode).IsEqualTo(-1);
        await Assert.That(result.TimedOut).IsFalse();
        await Assert.That(result.Error).IsEqualTo("prepare command 1 timed out after 2 seconds");
        await Assert.That(ProcessStopped(int.Parse(File.ReadAllText(entered).Trim()))).IsTrue();
        await AssertPrepareTerminalAsync(lab, run, "{\"commands\":1,\"failed\":1}");
    }

    [Test]
    [Timeout(60_000)]
    public async Task Prepare_interruption_is_not_replayed(CancellationToken ct)
    {
        foreach (var recovery in new[] { "resume", "continue", "cancel" })
        {
            using var tree = new TempTree();
            var repo = await LabRepo.CreateAsync(tree, ct);
            var entered = Path.Combine(tree.Root, "pid");
            var release = Path.Combine(tree.Root, "release");
            var completed = Path.Combine(tree.Root, "orphan-done");
            var second = Path.Combine(tree.Root, "second");
            var tasks = WritePrepareTasks(tree, [GateCommand(entered, release) + "; " + WriteCommand(completed, "done"), WriteCommand(second, "wrong")]);
            var environment = FakeOpenCode.Environment(tree, "one-task.jsonl").ToDictionary(x => x.Key, x => (string?)x.Value);
            using var supervisor = new HarnessProcess(tree);
            var running = supervisor.RunAsync(["run", tasks, "--repo", repo.Root], ct, environment);
            var reached = await WaitFileAsync(entered, running, ct);
            using var supervisorProcess = System.Diagnostics.Process.GetProcessById(supervisor.ProcessId);
            supervisor.KillRoot();
            CapturedResult recovered;
            string folder;
            try
            {
                folder = Directory.GetDirectories(Path.Combine(tree.DispatchHome, "state", "ledgers")).Single();
                // The supervisor process has exited, even though the orphan still holds its pipes.
                await supervisorProcess.WaitForExitAsync(ct);
                using var recoveryProcess = new HarnessProcess(tree);
                recovered = await recoveryProcess.RunAsync(recovery == "cancel" ? ["cancel", Path.GetFileName(folder), "inspect"]
                    : recovery == "continue" ? ["resume", Path.GetFileName(folder), "--continue"] : ["resume", Path.GetFileName(folder)], ct, environment);
            }
            finally
            {
                File.WriteAllText(release, "go");
                await running;
            }
            await Assert.That(reached).IsTrue();
            var pid = int.Parse(File.ReadAllText(entered).Trim());
            // The orphan's exit, not a one-second grace period, proves its final command finished.
            if (!ProcessStopped(pid))
            {
                using var process = System.Diagnostics.Process.GetProcessById(pid);
                await process.WaitForExitAsync(ct);
            }
            await Assert.That(File.Exists(completed)).IsTrue();
            await Assert.That(File.Exists(second)).IsFalse();
            var lines = File.ReadAllLines(Path.Combine(folder, "events.jsonl")).Select(x => JsonSerializer.Deserialize(x, LedgerJson.Events.LedgerLine)!).ToList();
            var record = JsonSerializer.Deserialize(File.ReadAllText(Path.Combine(folder, "inspect", "task.json")), LedgerJson.Files.TaskRecord)!;
            await Assert.That(record.PrepareRunning).IsTrue();
            await Assert.That(record.Prepare).IsEmpty();
            await Assert.That(record.Session.Id).IsNull();
            if (recovery == "cancel")
            {
                await Assert.That(recovered.ExitCode).IsEqualTo(0);
                var result = JsonSerializer.Deserialize(File.ReadAllText(Path.Combine(folder, "inspect", "result.json")), LedgerJson.Files.TaskResult)!;
                await Assert.That(result.State).IsEqualTo("cancelled");
                await Assert.That(result.Failures).IsEmpty();
                await Assert.That(result.Prepare).IsEmpty();
                await Assert.That(result.Warnings).IsEquivalentTo(new[] { "prepare-interrupted: a prepare command may still run on this host" }, CollectionOrdering.Matching);
                await Assert.That(lines.Single(x => x.Type == "prepared").Data!.ToJsonString()).IsEqualTo("{\"commands\":0,\"failed\":0,\"cancelled\":true}");
                await Assert.That(lines.Last().Type).IsEqualTo("final");
                await Assert.That(lines.Last().Data!["reason"]!.GetValue<string>()).IsEqualTo("prepare-interrupted: a prepare command may still run on this host");
            }
            else
            {
                await Assert.That(recovered.ExitCode).IsEqualTo(1);
                await Assert.That(lines.Where(x => x.Type == "error").Select(x => x.Data!["message"]!.GetValue<string>())).IsEquivalentTo(new[] {
                    "inspect: prepare was interrupted by a supervisor exit; a command may still run on this host. Check for it, then clean the task and start it under a new run id." }, CollectionOrdering.Matching);
                await Assert.That(lines.TakeLast(2).Select(x => x.Type)).IsEquivalentTo(new[] { "queued", "error" }, CollectionOrdering.Matching);
                await Assert.That(lines.Select(x => x.Type)).DoesNotContain("final");
            }
        }
    }

    [Test]
    [Timeout(60_000)]
    public async Task Prepare_failed_task_stays_terminal(CancellationToken ct)
    {
        using var tree = new TempTree();
        var repo = await LabRepo.CreateAsync(tree, ct);
        var counter = Path.Combine(tree.Root, "counter");
        var command = OperatingSystem.IsWindows() ? $"Add-Content -LiteralPath {Quote(counter)} -Value once; exit 3" : $"printf 'once\\n' >> {Quote(counter)}; exit 3";
        var tasks = WritePrepareTasks(tree, [command]);
        var environment = FakeOpenCode.Environment(tree, "one-task.jsonl").ToDictionary(x => x.Key, x => (string?)x.Value);
        using var supervisor = new HarnessProcess(tree);
        var outcome = await supervisor.RunAsync(["run", tasks, "--repo", repo.Root], ct, environment);
        await Assert.That(outcome.ExitCode).IsEqualTo(1);
        var folder = Directory.GetDirectories(Path.Combine(tree.DispatchHome, "state", "ledgers")).Single();
        var resultPath = Path.Combine(folder, "inspect", "result.json");
        var resultBefore = File.ReadAllText(resultPath);
        using var recovery = new HarnessProcess(tree);
        await Assert.That((await recovery.RunAsync(["resume", Path.GetFileName(folder), "--continue"], ct, environment)).ExitCode).IsEqualTo(1);
        await Assert.That(File.ReadAllText(resultPath)).IsEqualTo(resultBefore);
        var result = JsonSerializer.Deserialize(resultBefore, LedgerJson.Files.TaskResult)!;
        await Assert.That(result.Failures).IsEquivalentTo(new[] { "prepare-failed" }, CollectionOrdering.Matching);
        await Assert.That(result.Error).IsEqualTo("prepare command 1 exited 3");
        await Assert.That(result.Prepare!.Select(x => x.Command)).IsEquivalentTo(new[] { command }, CollectionOrdering.Matching);
        await Assert.That(File.ReadAllLines(counter)).IsEquivalentTo(new[] { "once" }, CollectionOrdering.Matching);
        await Assert.That(File.ReadAllLines(Path.Combine(folder, "events.jsonl")).Select(x => JsonNode.Parse(x)!["type"]!.GetValue<string>()).Last()).IsEqualTo("final");
    }

    [Test]
    [Timeout(60_000)]
    public async Task Prepare_evidence_survives_collect(CancellationToken ct)
    {
        using var tree = new TempTree();
        var repo = await LabRepo.CreateAsync(tree, ct);
        var tasks = WritePrepareTasks(tree, ["exit 0", OperatingSystem.IsWindows() ? "Write-Output evidence" : "printf evidence"]);
        var document = JsonNode.Parse(File.ReadAllText(tasks))!;
        document["tasks"]![0]!["access"] = "full";
        File.WriteAllText(tasks, document.ToJsonString());
        var environment = FakeOpenCode.Environment(tree, "one-task.jsonl").ToDictionary(x => x.Key, x => (string?)x.Value);
        var steps = File.ReadAllLines(FakeOpenCode.Fixture("one-task.jsonl")).Select(x => JsonNode.Parse(x)!.AsObject()).ToList();
        // Keep this fake session collectible instead of simulating the release marker of a fenced server.
        foreach (var step in steps.Where(x => x["expect"]!.GetValue<string>() == "GET /api/session/ses_fixture"))
            step["reply"]!["data"]!["outcome"] = "failed";
        var message = steps.Single(x => x["expect"]!.GetValue<string>() == "GET /api/session/ses_fixture/message");
        message["reply"]!["data"]![1]!["content"]![0]!["text"] = "Report\nStatus: stopped";
        var scenario = Path.Combine(tree.Root, "collect.jsonl");
        File.WriteAllLines(scenario, steps.Select(x => x.ToJsonString()));
        environment["FAKE_OPENCODE_SCENARIO"] = scenario;
        using var supervisor = new HarnessProcess(tree);
        await supervisor.RunAsync(["run", tasks, "--repo", repo.Root], ct, environment);
        var folder = Directory.GetDirectories(Path.Combine(tree.DispatchHome, "state", "ledgers")).Single();
        var record = JsonNode.Parse(File.ReadAllText(Path.Combine(folder, "inspect", "task.json")))!;
        var expected = record["prepare"]!.ToJsonString();
        await Assert.That(record["prepare"]!.AsArray().Select(x => x!["command"]!.GetValue<string>())).IsEquivalentTo(new[] { "exit 0", OperatingSystem.IsWindows() ? "Write-Output evidence" : "printf evidence" }, CollectionOrdering.Matching);
        await Assert.That(record["prepare"]!.AsArray().Select(x => x!["exitCode"]!.GetValue<int>())).IsEquivalentTo(new[] { 0, 0 }, CollectionOrdering.Matching);
        for (var i = 0; i < 2; i++)
        {
            var resultPath = Path.Combine(folder, "inspect", "result.json");
            var previous = JsonNode.Parse(File.ReadAllText(resultPath))!;
            previous.AsObject().Remove("prepare");
            File.WriteAllText(resultPath, previous.ToJsonString());
            using var collect = new HarnessProcess(tree);
            await collect.RunAsync(["collect", Path.GetFileName(folder), "inspect"], ct, environment);
            var result = JsonNode.Parse(File.ReadAllText(Path.Combine(folder, "inspect", "result.json")))!;
            await Assert.That(result["prepare"]!.ToJsonString()).IsEqualTo(expected);
        }
        var events = File.ReadAllLines(Path.Combine(folder, "events.jsonl")).Select(x => JsonNode.Parse(x)!);
        await Assert.That(events.Count(x => x["type"]!.GetValue<string>() == "collected")).IsEqualTo(3);
        await Assert.That(events.Count(x => x["type"]!.GetValue<string>() == "final" && x["data"]?["via"]?.GetValue<string>() == "collect")).IsEqualTo(2);
        var calls = File.ReadLines(FakeOpenCode.Record(tree)).Select(x => JsonNode.Parse(x)!);
        await Assert.That(calls.Count(x => x["method"]!.GetValue<string>() == "DELETE")).IsEqualTo(0);
    }

    [Test]
    [Timeout(30_000)]
    public async Task Prepare_supervisor_cancel_is_not_a_timeout(CancellationToken ct)
    {
        foreach (var injected in new[] { false, true })
        {
            using var lab = new CoreLab();
            using var supervisor = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var run = await PrepareTaskAsync(lab, ct);
            var entered = Path.Combine(lab.Tree.Root, "pid");
            run.Record.Spec.Prepare = [GateCommand(entered, Path.Combine(lab.Tree.Root, "release")), "exit 0"];
            var scheduler = lab.Scheduler(supervisor.Token, prepareCommand: injected ? (task, command, environment, token) =>
                Verifier.RunCommandAsync(task.Record.Spec, null, task.Record.Workspace.Path!, environment, command, false, token) : null);
            var running = scheduler.RunAsync([run]);
            var reached = await WaitFileAsync(entered, running, ct);
            supervisor.Cancel();
            await Assert.That(async () => await running).Throws<OperationCanceledException>();
            await Assert.That(reached).IsTrue();
            var saved = lab.Ledger.LoadRecord(run.Id);
            await Assert.That(saved.Prepare!.Select(x => x.Command)).IsEquivalentTo(new[] { run.Record.Spec.Prepare[0] }, CollectionOrdering.Matching);
            await Assert.That(saved.Prepare![0].ExitCode).IsEqualTo(-1);
            await Assert.That(saved.Prepare[0].TimedOut).IsFalse();
            await Assert.That(saved.PrepareRunning).IsTrue();
            await Assert.That(saved.PrepareDone).IsNull();
            await Assert.That(lab.Backend.Calls).IsEmpty();
            await Assert.That(File.ReadAllText(Path.Combine(lab.Ledger.TaskDir(run.Id), "prepare.log"))).DoesNotContain("[harness] timed out");
            await Assert.That(ProcessStopped(int.Parse(File.ReadAllText(entered).Trim()))).IsTrue();
        }
    }

    [Test]
    [Timeout(30_000)]
    public async Task Prepare_final_head_cancel_prevents_worker(CancellationToken ct)
    {
        using var lab = new CoreLab();
        var run = await PrepareTaskAsync(lab, ct);
        run.Record.Spec.Prepare = ["exit 0"];
        EndOnPoll(lab, run);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var scheduler = lab.Scheduler(ct, prepareHead: async (git, cwd, task, start, token) =>
        {
            var head = (await git.CheckAsync(cwd, token, "rev-parse", "--verify", "HEAD^{commit}")).Trim();
            if (!start) { entered.SetResult(); await release.Task.WaitAsync(ct); }
            return head;
        });
        var running = scheduler.RunAsync([run]);
        var reached = await Task.WhenAny(entered.Task, running) == entered.Task;
        var cancellation = run.PrepareCancellation!;
        run.CancelRequested = true;
        release.SetResult();
        await Assert.That(await running).IsEqualTo(1);
        await Assert.That(reached).IsTrue();
        await Assert.That(lab.Ledger.LoadResult(run.Id)!.State).IsEqualTo("cancelled");
        await Assert.That(lab.Ledger.LoadResult(run.Id)!.Failures).IsEmpty();
        await AssertPrepareTerminalAsync(lab, run, "{\"commands\":1,\"failed\":0,\"cancelled\":true}");
        cancellation.Cancel(); // A concurrent canceller may still own this reference after startup clears it.
        await Assert.That(run.PrepareCancellation).IsNull();
    }

    [Test]
    [Timeout(30_000)]
    public async Task Prepare_non_git_failures_are_terminal(CancellationToken ct)
    {
        foreach (var stage in new[] { "log", "environment", "command" })
        {
            using var lab = new CoreLab("prepare-non-git");
            var run = await PrepareTaskAsync(lab, ct);
            run.Record.Spec.Prepare = ["exit 0"];
            var log = Path.Combine(lab.Ledger.TaskDir(run.Id), "prepare.log");
            if (stage == "log") Directory.CreateDirectory(log);
            var blocked = Path.Combine(lab.Tree.Root, "blocked");
            File.WriteAllText(blocked, "not a directory");
            var scheduler = lab.Scheduler(ct, prepareCommand: (_, _, _, _) => throw new InvalidOperationException("prepare fixture failed"),
                prepareHead: async (git, cwd, task, start, token) =>
                {
                    var head = (await git.CheckAsync(cwd, token, "rev-parse", "--verify", "HEAD^{commit}")).Trim();
                    if (stage == "environment") run.Record.Settings.TempRoot = new() { Value = blocked };
                    return head;
                });
            await Assert.That(await scheduler.RunAsync([run])).IsEqualTo(1);
            var result = lab.Ledger.LoadResult(run.Id)!;
            await Assert.That(result.State).IsEqualTo("failed");
            await Assert.That(result.Failures).IsEquivalentTo(new[] { "prepare-failed" }, CollectionOrdering.Matching);
            await Assert.That(result.Prepare).IsEmpty();
            if (stage == "command") await Assert.That(result.Error).IsEqualTo("prepare fixture failed");
            else await Assert.That(result.Error).Contains(stage == "log" ? log : blocked);
            await Assert.That(lab.Ledger.Read().Single(x => x.Type == "prepared").Data!.ToJsonString()).IsEqualTo(stage == "command"
                ? "{\"commands\":0,\"failed\":1}" : "{\"commands\":0,\"failed\":0,\"checkFailed\":false}");
            await Assert.That(lab.Backend.Calls).IsEmpty();
            await Assert.That(run.PrepareCancellation).IsNull();
        }
    }

    [Test]
    [Timeout(30_000)]
    public async Task Prepare_completed_evidence_survives_log_failure(CancellationToken ct)
    {
        using var lab = new CoreLab();
        var run = await PrepareTaskAsync(lab, ct);
        run.Record.Spec.Prepare = ["exit 0", "exit 3"];
        var log = Path.Combine(lab.Ledger.TaskDir(run.Id), "prepare.log");
        var scheduler = lab.Scheduler(ct, prepareCommand: async (task, command, environment, token) =>
        {
            var executed = await Verifier.RunCommandAsync(task.Record.Spec, null, task.Record.Workspace.Path!, environment, command, false, token);
            File.Move(log, log + ".saved");
            Directory.CreateDirectory(log);
            return executed;
        });
        await Assert.That(await scheduler.RunAsync([run])).IsEqualTo(1);
        var result = lab.Ledger.LoadResult(run.Id)!;
        await Assert.That(result.Prepare!.Select(x => x.Command)).IsEquivalentTo(new[] { "exit 0" }, CollectionOrdering.Matching);
        await Assert.That(result.Prepare![0].ExitCode).IsEqualTo(0);
        await Assert.That(result.Failures).IsEquivalentTo(new[] { "prepare-failed" }, CollectionOrdering.Matching);
        await Assert.That(result.Error).Contains(log);
        await Assert.That(lab.Ledger.LoadRecord(run.Id).Prepare!.Select(x => x.ExitCode)).IsEquivalentTo(new[] { 0 }, CollectionOrdering.Matching);
        await Assert.That(lab.Ledger.Read().Single(x => x.Type == "prepared").Data!.ToJsonString()).IsEqualTo("{\"commands\":1,\"failed\":0,\"checkFailed\":false}");
        await Assert.That(lab.Backend.Calls).IsEmpty();
    }

    static string WritePrepareTasks(TempTree tree, IReadOnlyList<string> commands)
    {
        var path = Path.Combine(tree.Root, "tasks.jsonc");
        File.WriteAllText(path, new JsonObject { ["schemaVersion"] = 2, ["run"] = "prepare",
            ["tasks"] = new JsonArray(new JsonObject { ["id"] = "inspect", ["kind"] = "research", ["backend"] = "opencode", ["base"] = "main",
                ["route"] = "fixture/model", ["prompt"] = "Inspect.", ["prepare"] = new JsonArray(commands.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray()) }) }.ToJsonString());
        return path;
    }

    static async Task<TaskRun> PrepareTaskAsync(CoreLab lab, CancellationToken ct)
    {
        var run = await lab.TaskAsync("inspect", ct);
        run.Record.Spec.Workspace = "in-place";
        run.Record.Spec.Commit = "none";
        run.Record.Workspace = new();
        lab.Ledger.SaveRecord(run.Record);
        return run;
    }

    static void EndOnPoll(CoreLab lab, TaskRun run) => lab.Backend.OnPoll = (live, _) =>
    {
        if (live.Count > 0) lab.End(run);
        return Task.FromResult(new PollResult([]));
    };

    static string Quote(string value) => "'" + value.Replace("'", OperatingSystem.IsWindows() ? "''" : "'\\''", StringComparison.Ordinal) + "'";
    static string WriteCommand(string path, string text) => OperatingSystem.IsWindows()
        ? $"Set-Content -LiteralPath {Quote(path)} -Value {Quote(text)}" : $"printf '%s\\n' {Quote(text)} > {Quote(path)}";
    static string GateCommand(string entered, string release) => OperatingSystem.IsWindows()
        ? $"Set-Content -LiteralPath {Quote(entered)} -Value $PID; while (!(Test-Path -LiteralPath {Quote(release)})) {{ Start-Sleep -Milliseconds 20 }}"
        : $"printf '%s\\n' $$ > {Quote(entered)}; while [ ! -f {Quote(release)} ]; do sleep 0.02; done";

    [Test]
    [Timeout(30_000)]
    public async Task Prepare_readiness_waits_for_the_fixture_signal_not_a_startup_budget(CancellationToken ct)
    {
        using var tree = new TempTree();
        var entered = Path.Combine(tree.Root, "entered");
        var running = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var waiting = WaitFileAsync(entered, running.Task, ct);
        // Hold readiness beyond the old five-second cutoff, without allowing the producer to exit.
        await Task.WhenAny(waiting, Task.Delay(TimeSpan.FromSeconds(6), ct));
        var returnedBeforeSignal = waiting.IsCompleted;
        File.WriteAllText(entered, "ready");
        var reached = await waiting;
        await Assert.That(returnedBeforeSignal).IsFalse();
        await Assert.That(reached).IsTrue();
        await Assert.That(await WaitFileAsync(entered, Task.CompletedTask, ct)).IsTrue();
        await Assert.That(await WaitFileAsync(Path.Combine(tree.Root, "missing"), Task.CompletedTask, ct)).IsFalse();
    }

    static async Task<bool> WaitFileAsync(string path, Task running, CancellationToken ct)
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var watcher = new FileSystemWatcher(Path.GetDirectoryName(path)!, Path.GetFileName(path));
        watcher.Created += (_, _) => entered.TrySetResult();
        watcher.Error += (_, error) => entered.TrySetException(error.GetException());
        watcher.EnableRaisingEvents = true;
        // Subscribe before checking, so creation between the check and the wait cannot be missed.
        if (!File.Exists(path))
        {
            if (await Task.WhenAny(entered.Task, running).WaitAsync(ct) == entered.Task) await entered.Task;
        }
        return File.Exists(path);
    }

    static bool ProcessStopped(int pid)
    {
        try { using var process = System.Diagnostics.Process.GetProcessById(pid); return process.HasExited; }
        catch (ArgumentException) { return true; }
    }

    static async Task AssertPrepareTerminalAsync(CoreLab lab, TaskRun run, string summary)
    {
        var saved = lab.Ledger.LoadRecord(run.Id);
        await Assert.That(saved.PrepareRunning).IsTrue();
        await Assert.That(saved.PrepareDone).IsNull();
        await Assert.That(saved.Session.Id).IsNull();
        await Assert.That(lab.Ledger.LoadResult(run.Id)!.TimedOut).IsFalse();
        await Assert.That(lab.Backend.Calls.Select(x => x.Member)).IsEmpty();
        var events = lab.Ledger.Read();
        await Assert.That(events.Single(x => x.Type == "prepared").Data!.ToJsonString()).IsEqualTo(summary);
        await Assert.That(events.FindIndex(x => x.Type == "final")).IsGreaterThan(events.FindIndex(x => x.Type == "prepared"));
        await Assert.That(File.Exists(Path.Combine(lab.Ledger.TaskDir(run.Id), "prepare.log"))).IsTrue();
    }

    [Test]
    [Timeout(30_000)]
    public async Task Workers_receive_configured_temp(CancellationToken ct)
    {
        using var lab = new CoreLab("worker-temp");
        var run = await lab.TaskAsync("inspect", ct);
        var root = Path.Combine(lab.Tree.Root, "private-temp");
        run.Record.Settings.TempRoot = new() { Value = root, From = "task" };
        WorkerLaunch? launch = null;
        var create = lab.Backend.OnCreate!;
        lab.Backend.OnCreate = (input, token) => { launch = input; return create(input, token); };
        lab.Backend.OnPoll = (_, _) => { lab.End(run); return Task.FromResult(new PollResult([])); };
        await Assert.That(await lab.Scheduler(ct).RunAsync([run])).IsEqualTo(0);
        await Assert.That(launch).IsNotNull();
        var expected = Path.Combine(root, "worker-temp", "inspect");
        foreach (var name in new[] { "TEMP", "TMP", "TMPDIR" })
            await Assert.That(launch!.Environment[name]).IsEqualTo(expected);
        await Assert.That(Directory.Exists(expected)).IsTrue();
    }

    [Test]
    [Timeout(60_000)]
    public async Task Reattached_continue_receives_recorded_tempRoot(CancellationToken ct)
    {
        using var tree = new TempTree();
        var repo = await LabRepo.CreateAsync(tree, ct);
        const string run = "resume-temp";
        const string controlId = "abcdefghijklmnopqrstuvwxyz";
        var ledger = Path.Combine(tree.DispatchHome, "state", "ledgers", run);
        var control = Directory.CreateDirectory(Path.Combine(tree.DispatchHome, "state", "control", controlId)).FullName;
        await repo.BaselineAsync(control, ct);
        File.WriteAllText(Path.Combine(control, "setup.done"), "");
        Json.WriteFile(Path.Combine(ledger, "run.json"), new RunInfo { Run = run, Repo = repo.Root, ControlId = controlId }, LedgerJson.Files.RunInfo);
        File.WriteAllText(Path.Combine(ledger, "events.jsonl"), $$"""{"schemaVersion":2,"run":"{{run}}","task":"inspect","type":"queued"}""" + "\n");
        var root = Path.Combine(tree.Root, "recorded-temp");
        var settings = new EffectiveSettings
        {
            Backend = new() { Value = "opencode" }, Route = new() { Value = "opencode:fixture/model" },
            RawPrompt = new() { Value = true }, TempRoot = new() { Value = root, From = "task" },
        };
        Json.WriteFile(Path.Combine(ledger, "config.json"), new EffectiveConfig
        { Tasks = new() { ["inspect"] = new() { Settings = settings } } }, LedgerJson.Files.EffectiveConfig);
        Json.WriteFile(Path.Combine(ledger, "inspect", "task.json"), new TaskRecord
        {
            Spec = new() { Id = "inspect", Kind = "research" }, Settings = settings,
            Session = new() { Id = "ses_fixture", Turn = "old-turn" },
            Workspace = new() { Path = repo.Root, BaseSha = await repo.GitAsync(["rev-parse", "HEAD"], ct) },
        }, LedgerJson.Files.TaskRecord);
        var steps = File.ReadAllLines(FakeOpenCode.Fixture("one-task.jsonl")).Select(x => System.Text.Json.Nodes.JsonNode.Parse(x)!).ToList();
        var interrupted = steps.Single(x => x["expect"]!.GetValue<string>() == "GET /api/session/ses_fixture").DeepClone();
        interrupted["repeat"] = false;
        interrupted["reply"]!["data"]!["outcome"] = "interrupted";
        steps.Insert(4, interrupted);
        var scenario = Path.Combine(tree.Root, "resume.jsonl");
        File.WriteAllLines(scenario, steps.Select(x => x.ToJsonString()));
        var environment = FakeOpenCode.Environment(tree, "one-task.jsonl").ToDictionary(x => x.Key, x => (string?)x.Value);
        environment["FAKE_OPENCODE_SCENARIO"] = scenario;
        using var child = new HarnessProcess(tree);
        (await child.RunAsync(["resume", run, "--continue"], ct, environment)).RequireSuccess("resume --continue");
        var sent = File.ReadLines(FakeOpenCode.Record(tree)).Select(x => System.Text.Json.Nodes.JsonNode.Parse(x)!)
            .Single(x => x["method"]!.GetValue<string>() == "PUT");
        foreach (var name in new[] { "TEMP", "TMP", "TMPDIR" })
            await Assert.That(sent["body"]!["variables"]![name]!.GetValue<string>()).IsEqualTo(Path.Combine(root, run, "inspect"));
    }

    [Test]
    [Arguments("codex", "codex app-server")]
    [Arguments("opencode", "opencode server")]
    [Timeout(30_000)]
    public async Task Watchdog_failure_names_the_backend_that_stopped_answering(string name, string server, CancellationToken ct)
    {
        using var lab = new CoreLab();
        var run = await lab.TaskAsync("inspect", ct);
        run.Record.Settings.Backend = new() { Value = name };
        run.SaveWorker(new(name, run.Id, "worker", "turn", new()), lab.Ledger);
        run.State = TaskState.Running;
        var failing = new FakeBackend(name, lab.Backend.Capabilities)
        {
            OnPoll = (_, _) => throw new IOException("poll failed"),
        };
        var other = new FakeBackend(name == "codex" ? "opencode" : "codex", lab.Backend.Capabilities)
        {
            OnPoll = (_, _) => Task.FromResult(new PollResult([])),
        };
        var time = new FakeTimeProvider();
        var scheduler = new Scheduler(new Dictionary<string, IWorkerBackend> { [other.Name] = other, [name] = failing },
            lab.Ledger, lab.Tree.Repository, AllowList.From(null), lab.Policy, 1, ct, lab.Git, time);
        var running = scheduler.RunAsync([run]);
        while (!running.IsCompleted)
        {
            time.Advance(TimeSpan.FromSeconds(30));
            await Task.Delay(20, ct);
        }
        var failure = await Assert.ThrowsAsync<HarnessException>(() => running);
        await Assert.That(failure!.Message).Contains($"the {server} stopped answering");
        await Assert.That(failure.Message).Contains($"resume {lab.Ledger.RunId}");
        await Assert.That(lab.Ledger.Read().Single(item => item.Type == "aborted").Data!["reason"]!.GetValue<string>()).Contains(server);
        await Assert.That(lab.Ledger.Read().Count(item => item.Type == "error" && item.Data?["while"]?.GetValue<string>() == "watchdog")).IsEqualTo(3);
    }

    [Test]
    [Timeout(30_000)]
    public async Task Run_ControlPathNeverInWorkerEnvOrPrompt(CancellationToken ct)
    {
        using var lab = new CoreLab();
        var run = await lab.TaskAsync("inspect", ct);
        WorkerLaunch? launch = null;
        WorkerPrompt? prompt = null;
        var create = lab.Backend.OnCreate!;
        var send = lab.Backend.OnPrompt!;
        lab.Backend.OnCreate = (input, token) => { launch = input; return create(input, token); };
        lab.Backend.OnPrompt = (input, token) => { prompt = input.Prompt; return send(input, token); };
        lab.Backend.OnPoll = (_, _) => { lab.End(run); return Task.FromResult(new PollResult([])); };

        await lab.Scheduler(ct).RunAsync([run]);

        await Assert.That(launch).IsNotNull();
        await Assert.That(prompt).IsNotNull();
        var control = Locations.ControlDir(lab.Ledger.RunId);
        await Assert.That(control.StartsWith(lab.Tree.WorktreeRoot, StringComparison.OrdinalIgnoreCase)).IsFalse();
        await Assert.That(control.StartsWith(lab.Ledger.Dir, StringComparison.OrdinalIgnoreCase)).IsFalse();
        await Assert.That(launch!.Environment.Values.Any(x => x.Contains(control, StringComparison.OrdinalIgnoreCase))).IsFalse();
        await Assert.That(prompt!.Text.Contains(control, StringComparison.OrdinalIgnoreCase)).IsFalse();
    }

    [Test]
    [Arguments("valid")]
    [Arguments("other-run")]
    [Arguments("unknown")]
    [Arguments("extra")]
    [Arguments("malformed")]
    [Arguments("final")]
    [Timeout(30_000)]
    public async Task Cancel_ForOtherRunUnknownOrFinalTask_IsIgnoredAndLogged(string kind, CancellationToken ct)
    {
        using var lab = new CoreLab();
        var run = await lab.TaskAsync("inspect", ct);
        var final = await lab.TaskAsync("finished", ct);
        final.State = TaskState.Done;
        var request = new System.Text.Json.Nodes.JsonObject
        {
            ["schemaVersion"] = 2,
            ["run"] = kind == "other-run" ? "another-run" : lab.Ledger.RunId,
            ["task"] = kind == "unknown" ? "missing" : kind == "final" ? "finished" : "inspect",
            ["time"] = Json.Timestamp()
        };
        if (kind == "extra") request["by"] = "operator";
        var dir = Directory.CreateDirectory(Path.Combine(Locations.ControlDir(lab.Ledger.RunId), "cancel")).FullName;
        File.WriteAllText(Path.Combine(dir, "inspect.json"), kind == "malformed" ? "{" : request.ToJsonString());
        lab.Backend.OnPoll = (_, _) => { lab.End(run); return Task.FromResult(new PollResult([])); };
        lab.Backend.OnInterrupt = (_, _) => { lab.End(run); return Task.FromResult(new InterruptResult(true, "interrupt")); };

        await lab.Scheduler(ct).RunAsync([run, final]);

        await Assert.That(Directory.GetFiles(dir)).IsEmpty();
        await Assert.That(run.CancelRequested).IsEqualTo(kind == "valid");
        await Assert.That(final.State).IsEqualTo(TaskState.Done);
        await Assert.That(lab.Ledger.Read().Any(x => x.Type == "error" && x.Data?["while"]?.GetValue<string>() == "cancel")).IsEqualTo(kind != "valid");
    }

    [Test]
    [Timeout(30_000)]
    public async Task Cancel_ledger_does_not_substitute_confirmed_stop_for_interrupt_response(CancellationToken ct)
    {
        using var lab = new CoreLab();
        var run = await lab.TaskAsync("inspect", ct);
        lab.Backend.OnPoll = (_, _) => { run.CancelRequested = true; return Task.FromResult(new PollResult([])); };
        lab.Backend.OnInterrupt = (_, _) => { lab.End(run); return Task.FromResult(new InterruptResult(true, "tree-kill")); };

        await lab.Scheduler(ct).RunAsync([run]);

        var data = lab.Ledger.Read().Single(x => x.Type == "cancel").Data!;
        await Assert.That(data["interrupted"]).IsNull();
        await Assert.That(data.ContainsKey("status")).IsTrue();
        await Assert.That(data.ContainsKey("detail")).IsFalse();
    }

    [Test]
    [Timeout(30_000)]
    public async Task Interrupt_failure_does_not_skip_other_workers(CancellationToken ct)
    {
        using var lab = new CoreLab();
        var bad = await lab.TaskAsync("bad", ct);
        var good = await lab.TaskAsync("good", ct);
        lab.Backend.OnPoll = (live, _) =>
        {
            foreach (var worker in live) (worker.TaskId == bad.Id ? bad : good).CancelRequested = true;
            return Task.FromResult(new PollResult([]));
        };
        lab.Backend.OnInterrupt = (input, _) =>
        {
            if (input.Worker.TaskId == bad.Id) throw new IOException("session interrupt failed");
            lab.End(bad);
            lab.End(good);
            return Task.FromResult(new InterruptResult(false, "interrupt"));
        };

        var exit = await lab.Scheduler(ct, max: 2).RunAsync([bad, good]);

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(good.State).IsEqualTo(TaskState.Cancelled);
        await Assert.That(lab.Ledger.Read().Any(x => x.Task == bad.Id && x.Type == "error" && x.Data?["while"]?.GetValue<string>() == "interrupt")).IsTrue();
    }

    [Test]
    [Arguments("reattach")]
    [Arguments("prompt")]
    [Arguments("unreadable")]
    [Arguments("release")]
    [Timeout(30_000)]
    public async Task Resume_failure_is_isolated_and_continue_failure_is_released(string failure, CancellationToken ct)
    {
        using var lab = new CoreLab();
        var bad = await lab.TaskAsync("bad", ct);
        var good = await lab.TaskAsync("good", ct);
        foreach (var run in new[] { bad, good }) run.SaveWorker(new("opencode", run.Id, run.Id, "old", new()), lab.Ledger);
        lab.Backend.OnReattach = (worker, _) => worker.TaskId == good.Id
            ? Task.FromResult<ReattachResult>(new ReattachResult.Ended("succeeded"))
            : failure == "reattach" ? throw new IOException("read failed")
            : Task.FromResult<ReattachResult>(failure == "unreadable" ? new ReattachResult.Unreadable("missing") : new ReattachResult.Interrupted());
        lab.Backend.OnPrompt = (_, _) => throw new IOException("continue failed");
        lab.Backend.OnRelease = (worker, _) => failure == "release" && worker.TaskId == bad.Id
            ? throw new IOException("release failed") : Task.FromResult<InterruptResult?>(null);
        lab.Backend.OnInterrupt = (input, _) =>
        {
            if (failure == "prompt") lab.End(bad, "interrupted");
            return Task.FromResult(new InterruptResult(false, "interrupt"));
        };

        var exit = await lab.Scheduler(ct, allow: failure == "release" ? AllowList.From(new RouteRestriction()) : null).ResumeAsync([bad, good], true);

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(good.State).IsEqualTo(TaskState.Done);
        await Assert.That(bad.State).IsEqualTo(failure == "release" ? TaskState.Dispatching : TaskState.Failed);
        if (failure is "prompt" or "release")
        {
            await Assert.That(lab.Backend.Calls.Any(x => x.Member == "InterruptAsync")).IsTrue();
            await Assert.That(lab.Backend.Calls.Any(x => x.Member == "ReleaseAsync" && x.Input is WorkerRef worker && worker.TaskId == bad.Id)).IsEqualTo(failure == "release");
        }
        await Assert.That(lab.Ledger.Read().Any(x => x.Task == bad.Id && x.Type == "final")).IsEqualTo(failure != "release");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    [Timeout(30_000)]
    public async Task Pre_admission_signal_does_not_block_another_workers_cancel(bool resume, CancellationToken ct)
    {
        using var lab = new CoreLab();
        var slow = await lab.TaskAsync("slow", ct);
        var fast = await lab.TaskAsync("fast", ct);
        if (resume)
        {
            foreach (var run in new[] { slow, fast }) run.SaveWorker(new("opencode", run.Id, "worker-" + run.Id, "old-turn", new()), lab.Ledger);
            lab.Backend.OnReattach = (_, _) => Task.FromResult<ReattachResult>(new ReattachResult.Interrupted());
        }
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var interrupted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lab.Backend.OnPrompt = async (input, _) =>
        {
            if (input.Worker.TaskId == slow.Id)
            {
                lab.Backend.Emit(new WorkerSignal.Ask(slow.Id, "turn-slow", new("opencode", input.Worker.Id, null, "early"), "read", ["README.md"]));
                await release.Task.WaitAsync(ct);
            }
            return input.Worker with { Turn = "turn-" + input.Worker.TaskId };
        };
        lab.Backend.OnPoll = (live, _) =>
        {
            if (live.Any(x => x.TaskId == fast.Id))
                Json.WriteFile(Path.Combine(Locations.CancelDir(lab.Ledger.RunId), fast.Id + ".json"),
                    new CancelRequest { Run = lab.Ledger.RunId, Task = fast.Id, Time = Json.Timestamp() }, ControlJson.Files.CancelRequest);
            return Task.FromResult(new PollResult([]));
        };
        lab.Backend.OnInterrupt = (input, _) =>
        {
            interrupted.TrySetResult();
            lab.End(fast);
            return Task.FromResult(new InterruptResult(false, "interrupt"));
        };
        lab.Backend.OnAnswer = (_, _) => { lab.End(slow); return Task.FromResult(new AnswerResult(true, null)); };
        var scheduler = lab.Scheduler(ct, max: 2);
        var running = resume ? scheduler.ResumeAsync([slow, fast], true) : scheduler.RunAsync([slow, fast]);
        var progressed = await Task.WhenAny(interrupted.Task, Task.Delay(TimeSpan.FromSeconds(2), ct)) == interrupted.Task;
        release.TrySetResult();
        await running;

        await Assert.That(progressed).IsTrue();
        var events = lab.Ledger.Read().Where(x => x.Task == slow.Id).Select(x => x.Type).ToList();
        await Assert.That(events.IndexOf("permission")).IsGreaterThan(events.IndexOf("prompt"));
    }

    [Test]
    [Timeout(30_000)]
    public async Task Interrupted_author_is_persisted_and_leaves_review_pending(CancellationToken ct)
    {
        using var lab = new CoreLab();
        var author = await lab.TaskAsync("author", ct);
        var review = await lab.TaskAsync("review", ct);
        var healthy = await lab.TaskAsync("healthy", ct);
        review.Record.Spec.ReviewOf = author.Id;
        lab.Backend.OnPoll = (live, _) =>
        {
            if (live.Any(x => x.TaskId == author.Id))
            {
                lab.End(author, "interrupted");
                lab.Backend.Emit(new WorkerSignal.Ask(author.Id, author.Worker!.Turn!, new("opencode", author.Worker.Id, null, "late"), "read", ["README.md"]));
            }
            return Task.FromResult(new PollResult([]));
        };
        lab.Backend.OnAnswer = (_, _) => { lab.End(healthy); return Task.FromResult(new AnswerResult(true, null)); };

        var exit = await lab.Scheduler(ct, max: 2).RunAsync([author, review, healthy]);

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(lab.Ledger.TaskStates()[author.Id]).IsEqualTo("interrupted");
        await Assert.That(review.State).IsEqualTo(TaskState.Pending);
        await Assert.That(lab.Ledger.Read().Any(x => x.Task == review.Id && x.Type == "final")).IsFalse();
    }

    [Test]
    [Arguments("cancel", "succeeded")]
    [Arguments("cancel", "failed")]
    [Arguments("cancel", "interrupted")]
    [Arguments("timeout", "succeeded")]
    [Arguments("timeout", "failed")]
    [Arguments("timeout", "interrupted")]
    [Timeout(30_000)]
    public async Task Resume_preserves_persisted_interrupt_outcome(string interrupt, string outcome, CancellationToken ct)
    {
        using var lab = new CoreLab();
        var original = await lab.TaskAsync("inspect", ct);
        original.SaveWorker(new("opencode", original.Id, "worker", "turn", new()), lab.Ledger);
        lab.Ledger.Write(original.Id, interrupt, new());
        var run = TaskRun.FromLedger(lab.Ledger.LoadRecord(original.Id), lab.Ledger);
        lab.Backend.OnReattach = (_, _) => Task.FromResult<ReattachResult>(outcome == "interrupted"
            ? new ReattachResult.Interrupted() : new ReattachResult.Ended(outcome));

        var exit = await lab.Scheduler(ct).ResumeAsync([run], false);

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(lab.Ledger.LoadResult(run.Id)!.Outcome).IsEqualTo(interrupt == "cancel" ? "cancelled" : "timeout");
        await Assert.That(run.State).IsEqualTo(interrupt == "cancel" ? TaskState.Cancelled : TaskState.Failed);
    }

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
        var releasedAfterError = lab.Backend.Calls.Any(x => x.Member == "ReleaseAsync");
        lab.End(run);
        await running;

        await Assert.That(stateAfterError).IsEqualTo(TaskState.Running);
        await Assert.That(finalAfterError).IsFalse();
        await Assert.That(releasedAfterError).IsFalse();
        await Assert.That(lab.Backend.Calls.Count(x => x.Member == "ReleaseAsync")).IsEqualTo(1);
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
        Json.WriteFile(Path.Combine(Locations.CancelDir(lab.Ledger.RunId), run.Id + ".json"),
            new CancelRequest { Run = lab.Ledger.RunId, Task = run.Id, Time = Json.Timestamp() }, ControlJson.Files.CancelRequest);
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

    // A Codex turn completed but two descendants could not be observed, so the stop stayed unconfirmed
    // and the supervisor waited 58 minutes for an Ended signal that never came.
    [Test]
    [Timeout(30_000)]
    public async Task An_ended_turn_whose_stop_is_unconfirmed_settles_as_a_zombie_at_once(CancellationToken ct)
    {
        using var lab = new CoreLab();
        var run = await lab.TaskAsync("inspect", ct);
        run.Record.Spec.Verify = ["exit 0"];
        var sent = false;
        lab.Backend.OnPoll = (live, _) =>
        {
            if (live.Count > 0 && !sent)
            {
                sent = true;
                lab.Backend.Emit(new WorkerSignal.Status(run.Id, run.Worker!.Turn!, "succeeded", null));
                var unconfirmed = new WorkerSignal.StopUnconfirmed(run.Id, run.Worker.Turn!, "succeeded", "kill descendant pid=1972: Access is denied");
                lab.Backend.Emit(unconfirmed);
                lab.Backend.Emit(unconfirmed);
            }
            return Task.FromResult(new PollResult([]));
        };
        lab.Backend.OnCollect = (_, _) => Task.FromResult(new WorkerReport("succeeded", "Report\n\nStatus: done", new(), null, true, ["fixture/model#default"], null, true));

        var exit = await lab.Scheduler(ct).RunAsync([run]);

        await Assert.That(exit).IsEqualTo(1);
        var result = lab.Ledger.LoadResult(run.Id)!;
        await Assert.That(result.State).IsEqualTo("failed");
        await Assert.That(result.Failures).IsEquivalentTo(new[] { "zombie" });
        await Assert.That(result.Verify).IsEmpty();
        await Assert.That(result.WorkerStatus).IsEqualTo("done");
        await Assert.That(lab.Ledger.Read().Count(x => x.Type == "stop-unconfirmed")).IsEqualTo(1);
        await Assert.That(lab.Backend.Calls.Count(x => x.Member == "CollectAsync")).IsEqualTo(1);
    }

    [Test]
    [Timeout(30_000)]
    public async Task Settlement_between_admission_and_exit_does_not_strand_a_pending_task(CancellationToken ct)
    {
        using var lab = new CoreLab();
        var first = await lab.TaskAsync("first", ct);
        var second = await lab.TaskAsync("second", ct);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lab.Backend.OnCollect = async (worker, _) =>
        {
            if (worker.TaskId == first.Id) await release.Task.WaitAsync(ct);
            return new("succeeded", "Report", new(), null, true, [], null, true);
        };
        lab.Backend.OnPoll = (live, _) =>
        {
            foreach (var worker in live) lab.Backend.Emit(new WorkerSignal.Ended(worker.TaskId, worker.Turn!, "succeeded", null));
            return Task.FromResult(new PollResult([]));
        };
        var scheduler = lab.Scheduler(ct);
        TaskState[]? before = null;
        TaskState[]? after = null;
        scheduler.AfterAdmission = async () =>
        {
            if (first.State != TaskState.Settling) return;
            before = [first.State, second.State];
            release.SetResult();
            await first.Work!;
            after = [first.State, second.State];
        };
        var exit = await scheduler.RunAsync([first, second]);
        await Assert.That(before).IsEquivalentTo(new[] { TaskState.Settling, TaskState.Pending }, CollectionOrdering.Matching);
        await Assert.That(after).IsEquivalentTo(new[] { TaskState.Done, TaskState.Pending }, CollectionOrdering.Matching);
        await Assert.That(lab.Backend.Calls.Where(x => x.Member == "CreateAsync").Select(x => ((WorkerLaunch)x.Input!).TaskId))
            .IsEquivalentTo(new[] { "first", "second" }, CollectionOrdering.Matching);
        await Assert.That(new[] { first.State, second.State }).IsEquivalentTo(new[] { TaskState.Done, TaskState.Done }, CollectionOrdering.Matching);
        await Assert.That(lab.Ledger.Read().Where(x => x.Type == "final").Select(x => x.Task!))
            .IsEquivalentTo(new[] { "first", "second" }, CollectionOrdering.Matching);
        await Assert.That(exit).IsEqualTo(0);
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
    public async Task Prompt_failure_waits_for_confirmed_stop_before_final(CancellationToken ct)
    {
        using var lab = new CoreLab();
        var run = await lab.TaskAsync("inspect", ct);
        var savedBeforePrompt = false;
        var interruptedBeforeFinal = false;
        lab.Backend.OnPrompt = (input, _) =>
        {
            savedBeforePrompt = lab.Ledger.LoadRecord(run.Id).Session.Id == input.Worker.Id;
            throw new IOException("prompt failed");
        };
        lab.Backend.OnInterrupt = (_, _) =>
        {
            interruptedBeforeFinal = !run.IsFinal && !lab.Ledger.Read().Any(x => x.Type == "final");
            return Task.FromResult(new InterruptResult(true, "worker stopped"));
        };

        var exit = await lab.Scheduler(ct).RunAsync([run]);

        await Assert.That(savedBeforePrompt).IsTrue();
        await Assert.That(interruptedBeforeFinal).IsTrue();
        await Assert.That(run.State).IsEqualTo(TaskState.Failed);
        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(lab.Backend.Calls.Select(x => x.Member)).IsEquivalentTo(["CreateAsync", "PromptAsync", "InterruptAsync", "CollectAsync"]);
        await Assert.That(lab.Ledger.LoadResult(run.Id)!.Outcome).IsEqualTo("unknown");
    }
}
