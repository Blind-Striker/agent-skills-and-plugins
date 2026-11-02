using System.Reflection;
using System.Text.Json.Nodes;
using Dispatch.Tests.Support;
using TUnit.Assertions.Enums;

namespace Dispatch.Tests.Integration;

[Category("Integration")]
[NotInParallel]
public sealed class CodexHomeRetainedTests
{
    [Test]
    [Arguments(false, false)]
    [Arguments(true, false)]
    [Arguments(false, true)]
    [Timeout(180_000)]
    public async Task Live_settlement_preserves_success_and_clean_retries_only_the_retained_home(bool missingMarker, bool changedMarker, CancellationToken ct)
    {
        var locked = !missingMarker && !changedMarker;
        if (locked && !OperatingSystem.IsWindows()) Skip.Test("Windows file handles without delete sharing model the SQLite lock.");
        using var core = new CoreLab("retained-live");
        using var codex = new CodexLab(core.Tree);
        var run = await PrepareAsync(core, ct);
        File.WriteAllText(Path.Combine(codex.Environment["CODEX_HOME"], "auth.json"), "{}");
        codex.Write("task-edit", CodexLab.Handshake + CodexLab.Thread + CodexLab.Final);
        var context = new BackendContext(Path.Combine(Locations.ControlDir(core.Ledger.RunId), "profiles"), CodexLab.Executable,
            codex.Environment.Where(x => x.Key.StartsWith("FAKE_CODEX_", StringComparison.Ordinal)).ToDictionary(x => x.Key, x => x.Value));
        await using var backend = new CodexBackend(context, codex.Environment) { RunId = core.Ledger.RunId };
        var worker = (await backend.CreateAsync(new(core.Ledger.RunId, run.Id, "code", new("codex", "model-y", "high"), null, null,
            new("clean", "on", "workspace", "on", "worktree"), run.Record.Workspace.Path!, new([], []), codex.Environment, null, "clean", null), ct)).Worker;
        var process = backend.WorkerProcesses.Single().Process;
        var home = worker.State["codexHome"]!.GetValue<string>();
        var marker = Path.Combine(home, worker.State["codexHomeMarkerName"]!.GetValue<string>());
        using var held = locked ? new FileStream(Path.Combine(home, "goals_1.sqlite"), FileMode.Create, FileAccess.ReadWrite, FileShare.Read) : null;
        if (missingMarker) File.Delete(marker);
        if (changedMarker) File.WriteAllText(marker, "not ours");
        try
        {
            run.SaveWorker(await backend.PromptAsync(worker, new("Inspect.", false), ct), core.Ledger);
            TaskResult? result = null;
            Exception? error = null;
            try { result = await Settlement.SettleAsync(backend, core.Ledger, run, null, ct, core.Git); }
            catch (Exception failure) { error = failure; }
            await Assert.That(error).IsNull();
            await Assert.That(File.ReadAllText(Path.Combine(core.Ledger.TaskDir(run.Id), "final.md"))).Contains("Completed.");
            await Assert.That(result!.Outcome).IsEqualTo("succeeded");
            await Assert.That(result.State).IsEqualTo("done");
            await Assert.That(result.Failures).IsEmpty();
            await Assert.That(result.Warnings!).IsEquivalentTo(new[] { "codex-home-retained" }, CollectionOrdering.Matching);
            await Assert.That(core.Ledger.LoadResult(run.Id)!.Outcome).IsEqualTo("succeeded");
            await Assert.That(core.Ledger.LoadResult(run.Id)!.Warnings!).Contains("codex-home-retained");
            await Assert.That(result.WorkerStopped).IsTrue();
            await Assert.That(backend.HasResources).IsFalse();
            await Assert.That((bool)typeof(CodexProcess).GetField("disposed", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(process)!).IsTrue();
            await Assert.That((await backend.ReleaseAsync(run.SavedWorker(), ct))!.HomeRetained).IsNotNull();
            var errors = core.Ledger.Read().Where(line => line.Type == "error").ToArray();
            await Assert.That(errors.Select(line => line.Data!["while"]!.GetValue<string>())).IsEquivalentTo(new[] { "release" }, CollectionOrdering.Matching);
            await Assert.That(errors.Single().Data!["message"]!.GetValue<string>()).Contains(missingMarker ? "FileNotFoundException"
                : changedMarker ? "ownership marker does not match" : "goals_1.sqlite");
            await Assert.That(Directory.Exists(home)).IsTrue();
            if (!missingMarker) await Assert.That(File.ReadAllText(marker)).IsEqualTo(changedMarker ? "not ours" : worker.State["codexHomeMarkerText"]!.GetValue<string>());
            core.Ledger.Write(run.Id, "final", new() { ["state"] = result.State });
            // A process check here would be unconfirmed. Final warning cleanup must only delete the home.
            run.Worker!.State["pid"] = Environment.ProcessId;
            run.Worker.State["processStartTime"] = null;
            run.Worker.State["linuxBootId"] = null;
            run.Worker.State["linuxStartTicks"] = null;
            run.SaveWorker(run.Worker, core.Ledger);
            var control = Locations.ControlDir(core.Ledger.RunId);
            var output = TestContext.Current!.Output;
            var start = output.GetStandardOutput().Length;
            var exit = await Commands.CleanAsync(Args.Parse(["clean", core.Ledger.RunId]), ct);
            await Assert.That(exit).IsEqualTo(locked ? 1 : 0);
            await Assert.That(Directory.Exists(run.Record.Workspace.Path)).IsFalse();
            await Assert.That(Directory.Exists(home)).IsTrue();
            if (!locked)
            {
                await Assert.That(output.GetStandardOutput()[start..]).Contains($"edit: codex home has no valid ownership marker; delete it by hand: {home}");
                await Assert.That(core.Ledger.Read().Where(line => line.Type == "clean-failed")).IsEmpty();
            }
            else
            {
                await Assert.That(output.GetStandardOutput()[start..]).Contains("edit: codex home retained (");
                await Assert.That(File.Exists(Path.Combine(control, "setup.done"))).IsTrue();
                await Assert.That(core.Ledger.Read().Where(line => line.Type == "clean-failed").Select(line => line.Data!["step"]!.GetValue<string>()))
                    .IsEquivalentTo(new[] { "session" }, CollectionOrdering.Matching);
                held!.Dispose();
                start = output.GetStandardOutput().Length;
                await Assert.That(await Commands.CleanAsync(Args.Parse(["clean", core.Ledger.RunId]), ct)).IsEqualTo(0);
                await Assert.That(output.GetStandardOutput()[start..]).Contains("edit: codex home removed");
                await Assert.That(Directory.Exists(home)).IsFalse();
            }
        }
        finally
        {
            held?.Dispose();
            if (Directory.Exists(home))
            {
                File.WriteAllText(marker, worker.State["codexHomeMarkerText"]!.GetValue<string>());
                CodexHome.DeleteRecorded(context, worker, core.Ledger.RunId);
            }
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)] // Preservation: a changed marker still refuses at CollectAsync, before settlement artifacts.
    [Timeout(60_000)]
    public async Task Recorded_collect_keeps_a_missing_marker_home_but_refuses_a_changed_marker(bool changedMarker, CancellationToken ct)
    {
        using var core = new CoreLab("retained-collect");
        using var codex = new CodexLab(core.Tree);
        var run = await PrepareAsync(core, ct);
        File.WriteAllText(Path.Combine(codex.Environment["CODEX_HOME"], "auth.json"), "{}");
        var context = new BackendContext(Path.Combine(Locations.ControlDir(core.Ledger.RunId), "profiles"), null, new Dictionary<string, string>());
        using var home = CodexHome.Create(context, new(core.Ledger.RunId, run.Id, "code", new("codex", "model-y", "high"), null, null,
            new("clean", "on", "workspace", "on", "worktree"), run.Record.Workspace.Path!, new([], []), codex.Environment, null, "clean", null), codex.Environment["CODEX_HOME"]);
        run.SaveWorker(new("codex", run.Id, "recorded", null, new()
        {
            ["pid"] = int.MaxValue, ["codexHome"] = home.Home, ["codexHomeTemporary"] = true,
            ["codexHomeMarkerName"] = home.MarkerName, ["codexHomeMarkerText"] = home.MarkerText,
        }), core.Ledger);
        core.Ledger.Write(run.Id, "queued", new());
        var marker = Path.Combine(home.Home, home.MarkerName!);
        if (changedMarker) File.WriteAllText(marker, "not ours");
        else File.Delete(marker);
        try
        {
            await Assert.That(await Commands.CollectAsync(Args.Parse(["collect", core.Ledger.RunId, run.Id]), ct)).IsEqualTo(1);
            await Assert.That(Directory.Exists(home.Home)).IsTrue();
            if (changedMarker)
            {
                await Assert.That(core.Ledger.Read().Where(line => line.Type == "aborted").Select(line => line.Data!["message"]!.GetValue<string>()))
                    .IsEquivalentTo(new[] { "temporary Codex home ownership marker does not match" }, CollectionOrdering.Matching);
                await Assert.That(File.ReadAllText(marker)).IsEqualTo("not ours");
                await Assert.That(File.Exists(Path.Combine(core.Ledger.TaskDir(run.Id), "final.md"))).IsFalse();
            }
            else
            {
                var result = core.Ledger.LoadResult(run.Id)!;
                await Assert.That(result).IsNotNull();
                await Assert.That(result.Outcome).IsEqualTo("lost");
                await Assert.That(result.WorkerStopped).IsTrue();
                await Assert.That(result.Warnings!).Contains("codex-home-retained");
                await Assert.That(result.Failures).IsEquivalentTo(new[] { "outcome:lost", "settle-error", "no-final-text" }, CollectionOrdering.Matching);
                await Assert.That(File.ReadAllText(Path.Combine(core.Ledger.TaskDir(run.Id), "final.md"))).Contains("Outcome: lost");
                await Assert.That(core.Ledger.Read().Where(line => line.Type == "aborted")).IsEmpty();
                var output = TestContext.Current!.Output;
                var start = output.GetStandardOutput().Length;
                await Assert.That(await Commands.CleanAsync(Args.Parse(["clean", core.Ledger.RunId]), ct)).IsEqualTo(0);
                await Assert.That(output.GetStandardOutput()[start..]).Contains($"edit: codex home has no valid ownership marker; delete it by hand: {home.Home}");
                await Assert.That(File.Exists(marker)).IsFalse();
            }
        }
        finally { File.WriteAllText(marker, home.MarkerText); }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    [Timeout(60_000)]
    public async Task Release_exception_keeps_the_report_and_prior_error_without_collecting_or_verifying(bool cancelled, CancellationToken ct)
    {
        using var core = new CoreLab("release-failed");
        var run = await PrepareAsync(core, ct);
        var verify = Path.Combine(core.Tree.Root, "verify-ran");
        run.Record.Spec.Verify = [OperatingSystem.IsWindows() ? $"Set-Content -LiteralPath '{verify}' -Value ran" : $"touch '{verify}'"];
        File.WriteAllText(Path.Combine(run.Record.Workspace.Path!, "a.txt"), "worker edit");
        run.StartFailure = "earlier error";
        run.CancelRequested = cancelled;
        run.SaveWorker(new("codex", run.Id, "worker", "turn", new()), core.Ledger);
        await using var backend = new FakeBackend("codex", new("sandbox", "approval-gate", "native", false))
        {
            OnCollect = (_, _) => Task.FromResult(new WorkerReport("succeeded", "Worker report", new(), null, true, ["codex:model-y#high"], null, true)),
            OnRelease = (_, _) => throw new InvalidOperationException("release broke"),
        };
        TaskResult? result = null;
        Exception? failure = null;
        try { result = await Settlement.SettleAsync(backend, core.Ledger, run, null, ct, core.Git); }
        catch (Exception error) { failure = error; }
        await Assert.That(failure).IsNull();
        await Assert.That(File.ReadAllText(Path.Combine(core.Ledger.TaskDir(run.Id), "final.md"))).Contains("Worker report");
        await Assert.That(result!.Error).IsEqualTo("earlier error\nrelease broke");
        await Assert.That(result.Failures).IsEquivalentTo(cancelled ? new[] { "release-failed" } : ["release-failed", "settle-error"], CollectionOrdering.Matching);
        await Assert.That(core.Ledger.LoadResult(run.Id)!.State).IsEqualTo("failed");
        await Assert.That(result.Verify).IsEmpty();
        await Assert.That(result.FilesChanged).IsEmpty();
        await Assert.That(result.Commit).IsNull();
        await Assert.That(File.Exists(verify)).IsFalse();
        await Assert.That(File.Exists(Path.Combine(core.Ledger.TaskDir(run.Id), "diff.patch"))).IsFalse();
        await Assert.That(core.Ledger.Read().Where(line => line.Type is "collected" or "verified")).IsEmpty();
        await Assert.That(File.ReadAllText(Path.Combine(run.Record.Workspace.Path!, "a.txt"))).IsEqualTo("worker edit");
    }

    [Test]
    [Timeout(60_000)]
    public async Task Structured_validation_appends_its_error_instead_of_replacing_an_earlier_error(CancellationToken ct)
    {
        using var core = new CoreLab();
        var run = await core.TaskAsync("inspect", ct);
        core.Structured(run);
        File.WriteAllText(Path.Combine(core.Ledger.TaskDir(run.Id), "input.json"), "[]");
        var result = new TaskResult { Error = "earlier error" };
        await StructuredOutput.ValidateAsync(core.Ledger, run, result, StructuredLab.Valid, ct);
        await Assert.That(result.Error).IsEqualTo("earlier error\nstructured validation failed: inspect: input.json: frozen SHA-256 mismatch");
    }

    [Test]
    [Timeout(60_000)]
    public async Task Commit_failure_appends_its_message_and_keeps_the_commit_failure_classification(CancellationToken ct)
    {
        using var core = new CoreLab("commit-errors");
        var run = await PrepareAsync(core, ct);
        run.StartFailure = "earlier error";
        run.SaveWorker(new("codex", run.Id, "worker", "turn", new()), core.Ledger);
        File.WriteAllText(Path.Combine(run.Record.Workspace.Path!, "a.txt"), "worker edit");
        var lockPath = Path.GetFullPath((await core.Git.CheckAsync(run.Record.Workspace.Path!, ct, "rev-parse", "--git-path",
            $"refs/heads/{run.Record.Workspace.Branch}.lock")).Trim(), run.Record.Workspace.Path!);
        File.WriteAllText(lockPath, "test-owned ref lock");
        try
        {
            await using var backend = new FakeBackend("codex", new("sandbox", "approval-gate", "native", false))
            {
                OnCollect = (_, _) => Task.FromResult(new WorkerReport("succeeded", "Worker report", new(), null, true, ["codex:model-y#high"], null, true)),
                OnRelease = (_, _) => Task.FromResult<InterruptResult?>(new(true, "stopped")),
            };
            var result = await Settlement.SettleAsync(backend, core.Ledger, run, null, ct, core.Git);
            await Assert.That(result.Error).StartsWith("earlier error\nharness commit failed: ");
            await Assert.That(result.Error).Contains(".lock");
            await Assert.That(result.Failures).IsEquivalentTo(new[] { "commit-failed", "out-of-scope" }, CollectionOrdering.Matching);
        }
        finally { File.Delete(lockPath); }
    }

    [Test]
    [Timeout(60_000)]
    public async Task Scratch_cleanup_appends_its_error_without_replacing_the_start_failure(CancellationToken ct)
    {
        using var core = new CoreLab();
        var run = await core.TaskAsync("inspect", ct);
        core.Structured(run);
        run.Record.Spec.Project = "off";
        run.Record.Workspace = new();
        Scratch.Prepare(run, core.Ledger);
        var marker = Path.Combine(run.Record.Workspace.Path!, ".dispatch-owner.json");
        var owner = File.ReadAllText(marker);
        File.Delete(marker);
        run.StartFailure = "earlier error";
        run.SaveWorker(new("opencode", run.Id, "worker", "turn", new()), core.Ledger);
        core.Backend.OnCollect = (_, _) => Task.FromResult(new WorkerReport("succeeded", StructuredLab.Valid, new(), null, true, [], null, true));
        try
        {
            var result = await Settlement.SettleAsync(core.Backend, core.Ledger, run, null, ct, core.Git);
            await Assert.That(result.Error).IsEqualTo("earlier error\nscratch cleanup failed: structured scratch ownership marker is missing or unsafe");
        }
        finally
        {
            File.WriteAllText(marker, owner);
            Scratch.Delete(run.Record, core.Ledger);
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    [Timeout(60_000)]
    public async Task Recorded_release_or_collection_keeps_stop_confirmation_when_the_home_is_locked(bool collect, CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows()) Skip.Test("Windows file handles without delete sharing model the SQLite lock.");
        using var core = new CoreLab("recorded-lock");
        using var codex = new CodexLab(core.Tree);
        var run = await PrepareAsync(core, ct);
        File.WriteAllText(Path.Combine(codex.Environment["CODEX_HOME"], "auth.json"), "{}");
        var context = new BackendContext(Path.Combine(Locations.ControlDir(core.Ledger.RunId), "profiles"), null, new Dictionary<string, string>());
        using var home = CodexHome.Create(context, new(core.Ledger.RunId, run.Id, "code", new("codex", "model-y", "high"), null, null,
            new("clean", "on", "workspace", "on", "worktree"), run.Record.Workspace.Path!, new([], []), codex.Environment, null, "clean", null), codex.Environment["CODEX_HOME"]);
        var worker = new WorkerRef("codex", run.Id, "recorded", null, new()
        {
            ["pid"] = int.MaxValue, ["codexHome"] = home.Home, ["codexHomeTemporary"] = true,
            ["codexHomeMarkerName"] = home.MarkerName, ["codexHomeMarkerText"] = home.MarkerText,
        });
        using var held = new FileStream(Path.Combine(home.Home, "goals_1.sqlite"), FileMode.Create, FileAccess.ReadWrite, FileShare.Read);
        await using var backend = new CodexBackend(context) { RunId = core.Ledger.RunId };
        InterruptResult? stop = null;
        WorkerReport? report = null;
        Exception? failure = null;
        try
        {
            if (collect)
            {
                report = await backend.CollectAsync(worker, ct);
                stop = report.StopResult;
            }
            else stop = await backend.ReleaseAsync(worker, ct);
        }
        catch (Exception error) { failure = error; }
        await Assert.That(failure).IsNull();
        if (collect) await Assert.That(report!.Outcome).IsEqualTo("lost");
        await Assert.That(stop!.Stopped).IsTrue();
        await Assert.That(stop.HomeRetained).Contains("goals_1.sqlite");
        await Assert.That(Directory.Exists(home.Home)).IsTrue();
        await Assert.That(File.ReadAllText(Path.Combine(home.Home, home.MarkerName!))).IsEqualTo(home.MarkerText);
        await Assert.That(await backend.ReleaseAsync(worker, ct)).IsEqualTo(stop);
        await Assert.That(((ReattachResult.Ended)await backend.ReattachAsync(worker, ct)).StopResult).IsEqualTo(stop);
        held.Dispose();
        await Assert.That(backend.DeleteRetainedHome(worker)).IsNull();
        await Assert.That(Directory.Exists(home.Home)).IsFalse();
    }

    [Test]
    [Timeout(60_000)]
    public async Task Release_failure_without_stop_evidence_keeps_structured_scratch_after_Finish(CancellationToken ct)
    {
        using var core = new CoreLab("release-scratch");
        var run = await core.TaskAsync("inspect", ct);
        run.Record.Settings.Backend = new() { Value = "codex" };
        core.Structured(run);
        run.Record.Spec.Project = "off";
        run.Record.Workspace = new();
        Scratch.Prepare(run, core.Ledger);
        run.SaveWorker(new("codex", run.Id, "worker", "turn", new()), core.Ledger);
        run.WorkerEnded = true;
        await using var backend = new FakeBackend("codex", new("sandbox", "approval-gate", "native", false))
        {
            OnCollect = (_, _) => Task.FromResult(new WorkerReport("succeeded", StructuredLab.Valid, new(), null, true, [], null, true)),
            OnRelease = (_, _) => throw new InvalidOperationException("release broke"),
        };
        try
        {
            BeginSettle(Scheduler(core, backend, ct), run);
            await run.Work!;
            await Assert.That(core.Ledger.TaskStates()[run.Id]).IsEqualTo("failed");
            await Assert.That(core.Ledger.LoadResult(run.Id)!.Failures).IsEquivalentTo(new[] { "release-failed", "settle-error" }, CollectionOrdering.Matching);
            await Assert.That(Directory.Exists(run.Record.Workspace.Path)).IsTrue();
            await Assert.That(File.Exists(Path.Combine(run.Record.Workspace.Path!, ".dispatch-owner.json"))).IsTrue();
        }
        finally { Scratch.Delete(run.Record, core.Ledger); }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    [Timeout(60_000)]
    public async Task Release_failed_clean_retries_stop_and_force_removes_only_worktree_and_branch(bool force, CancellationToken ct)
    {
        using var core = new CoreLab("release-clean");
        using var codex = new CodexLab(core.Tree);
        var run = await PrepareAsync(core, ct);
        using var home = RecordedHome(core, codex, run, Environment.ProcessId);
        using var current = System.Diagnostics.Process.GetCurrentProcess();
        var detail = OperatingSystem.IsWindows()
            ? $"pid={Environment.ProcessId}; recordedStartTime=unknown; currentStartTime={current.StartTime.ToUniversalTime():O}; handle=uncertain; process identity unavailable; cannot safely terminate by PID"
            : $"kill descendant pid={Environment.ProcessId}: process identity unavailable; cannot safely terminate by PID";
        var stopError = $"Codex stop unconfirmed: recorded process recovery; {detail}";
        core.Ledger.SaveResult(new() { Run = core.Ledger.RunId, Task = run.Id, State = "failed", Failures = ["release-failed", "settle-error"] });
        core.Ledger.Write(run.Id, "final", new() { ["state"] = "failed" });
        if (!force)
        {
            await Assert.That(await Worktree.CleanAsync(core.Ledger, run.Record, core.Tree.Repository, false, ct, core.Git)).IsFalse();
            await Assert.That(Directory.Exists(run.Record.Workspace.Path)).IsTrue();
        }
        var output = TestContext.Current!.Output;
        var start = output.GetStandardOutput().Length;
        await Assert.That(await Commands.CleanAsync(Args.Parse(force ? ["clean", core.Ledger.RunId, "--force"] : ["clean", core.Ledger.RunId]), ct)).IsEqualTo(1);
        await Assert.That(output.GetStandardOutput()[start..]).Contains($"edit: retained Codex home: {home.Home}");
        await Assert.That(Directory.Exists(home.Home)).IsTrue();
        await Assert.That(Directory.Exists(run.Record.Workspace.Path)).IsEqualTo(!force);
        await Assert.That((await core.Git.CheckAsync(core.Tree.Repository, ct, "branch", "--format=%(refname:short)", "--list", run.Record.Workspace.Branch!)).Trim())
            .IsEqualTo(force ? "" : run.Record.Workspace.Branch!);
        await Assert.That(core.Ledger.Read().Where(line => line.Type == "stop-unconfirmed").Select(line => line.Task!))
            .IsEquivalentTo(new[] { run.Id }, CollectionOrdering.Matching);
        await Assert.That(core.Ledger.Read().Where(line => line.Type == "clean-failed").Select(line => line.Data!["step"]!.GetValue<string>()))
            .IsEquivalentTo(force ? new[] { "session" } : ["session", "workspace"], CollectionOrdering.Matching);
        await Assert.That(core.Ledger.Read().Where(line => line.Type == "clean-failed").Select(line => line.Data!["error"]!.GetValue<string>()))
            .IsEquivalentTo(force ? new[] { stopError } : [stopError, "workspace cleanup refused"], CollectionOrdering.Matching);
        await Assert.That(File.Exists(Path.Combine(Locations.ControlDir(core.Ledger.RunId), "setup.done"))).IsTrue();
        run.Worker!.State["pid"] = int.MaxValue;
        run.SaveWorker(run.Worker, core.Ledger);
        await Assert.That(await Commands.CleanAsync(Args.Parse(["clean", core.Ledger.RunId]), ct)).IsEqualTo(0);
        await Assert.That(Directory.Exists(home.Home)).IsFalse();
        await Assert.That(Directory.Exists(run.Record.Workspace.Path)).IsFalse();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    [Timeout(60_000)]
    public async Task Failure_path_with_only_a_retained_home_event_is_cleaned_without_rechecking_pid(bool reusedPid, CancellationToken ct)
    {
        using var core = new CoreLab("event-only-home");
        using var codex = new CodexLab(core.Tree);
        var run = await PrepareAsync(core, ct);
        using var home = RecordedHome(core, codex, run, int.MaxValue);
        await using var backend = new FakeBackend("codex", new("sandbox", "approval-gate", "native", false))
        {
            OnInterrupt = (_, _) => Task.FromResult(new InterruptResult(true, "stopped")),
            OnRelease = (_, _) => Task.FromResult<InterruptResult?>(new(true, "stopped", HomeRetained: "locked during failed creation")),
        };
        await (Task)typeof(Scheduler).GetMethod("FailStartCoreAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(Scheduler(core, backend, ct), [run, new HarnessException("creation failed"), "dispatch", null])!;
        await Assert.That(core.Ledger.LoadResult(run.Id)).IsNull();
        await Assert.That(core.Ledger.Read().Where(line => line.Type == "error" && line.Data?["while"]?.GetValue<string>() == "release")
            .Select(line => line.Data!["message"]!.GetValue<string>())).IsEquivalentTo(new[] { "locked during failed creation" }, CollectionOrdering.Matching);
        // A reused/live unidentifiable PID makes any process recheck refuse; deletion-only clean must ignore it.
        if (reusedPid) run.Worker!.State["pid"] = Environment.ProcessId;
        run.SaveWorker(run.SavedWorker(), core.Ledger);
        await Assert.That(await Commands.CleanAsync(Args.Parse(["clean", core.Ledger.RunId]), ct)).IsEqualTo(0);
        await Assert.That(Directory.Exists(home.Home)).IsFalse();
        await Assert.That(core.Ledger.Read().Where(line => line.Type is "stop-unconfirmed" or "hard-stop")).IsEmpty();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    [Timeout(60_000)]
    public async Task Locked_home_on_zombie_or_non_final_route_fails_clean_and_keeps_control(bool zombie, CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows()) Skip.Test("Windows file handles without delete sharing model the SQLite lock.");
        using var core = new CoreLab("route-lock");
        using var codex = new CodexLab(core.Tree);
        var run = await PrepareAsync(core, ct);
        using var home = RecordedHome(core, codex, run, int.MaxValue);
        core.Ledger.Write(run.Id, "queued", new());
        if (zombie)
        {
            core.Ledger.Write(run.Id, "stop-unconfirmed", new() { ["detail"] = "old stop" });
            core.Ledger.SaveResult(new() { Run = core.Ledger.RunId, Task = run.Id, State = "failed", WorkerStopped = false, Failures = ["zombie"] });
            core.Ledger.Write(run.Id, "final", new() { ["state"] = "failed" });
        }
        using var held = new FileStream(Path.Combine(home.Home, "goals_1.sqlite"), FileMode.Create, FileAccess.ReadWrite, FileShare.Read);
        var expectedError = DeleteFailure(held.Name);
        var output = TestContext.Current!.Output;
        var start = output.GetStandardOutput().Length;
        await Assert.That(await Commands.CleanAsync(Args.Parse(["clean", core.Ledger.RunId]), ct)).IsEqualTo(1);
        await Assert.That(output.GetStandardOutput()[start..]).Contains("edit: codex home retained (");
        await Assert.That(Directory.Exists(home.Home)).IsTrue();
        await Assert.That(File.Exists(Path.Combine(Locations.ControlDir(core.Ledger.RunId), "setup.done"))).IsTrue();
        await Assert.That(core.Ledger.Read().Where(line => line.Type == "clean-failed").Select(line => line.Data!["step"]!.GetValue<string>()))
            .IsEquivalentTo(new[] { "session" }, CollectionOrdering.Matching);
        await Assert.That(core.Ledger.Read().Where(line => line.Type == "clean-failed").Select(line => line.Data!["error"]!.GetValue<string>()))
            .IsEquivalentTo(new[] { expectedError }, CollectionOrdering.Matching);
        held.Dispose();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    [Timeout(60_000)]
    public async Task Read_only_nested_file_is_removed_without_changing_auth_source_attributes(bool readOnlyAuth, CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows()) Skip.Test("Windows read-only attributes refuse file deletion.");
        using var core = new CoreLab("readonly-home");
        using var codex = new CodexLab(core.Tree);
        var run = await PrepareAsync(core, ct);
        using var home = RecordedHome(core, codex, run, int.MaxValue);
        var source = Path.Combine(codex.Environment["CODEX_HOME"], "auth.json");
        var attributes = File.GetAttributes(source);
        if (readOnlyAuth) File.SetAttributes(source, attributes | FileAttributes.ReadOnly);
        var expected = File.GetAttributes(source);
        var nested = Path.Combine(Directory.CreateDirectory(Path.Combine(home.Home, "nested")).FullName, "readonly.sqlite");
        File.WriteAllText(nested, "state");
        File.SetAttributes(nested, File.GetAttributes(nested) | FileAttributes.ReadOnly);
        try
        {
            await using var backend = new CodexBackend(HomeContext(core)) { RunId = core.Ledger.RunId };
            var error = await Task.Run(() => backend.DeleteRetainedHome(run.SavedWorker()), ct);
            await Assert.That(File.Exists(nested)).IsFalse();
            await Assert.That(File.GetAttributes(source)).IsEqualTo(expected);
            await Assert.That(File.ReadAllText(source)).IsEqualTo("{}");
            if (readOnlyAuth)
            {
                await Assert.That(error).IsNotNull();
                await Assert.That(error!.Message).Contains("auth.json");
                await Assert.That(File.Exists(Path.Combine(home.Home, "auth.json"))).IsTrue();
            }
            else
            {
                await Assert.That(error).IsNull();
                await Assert.That(Directory.Exists(home.Home)).IsFalse();
            }
        }
        finally
        {
            File.SetAttributes(source, attributes);
            if (File.Exists(nested)) File.SetAttributes(nested, File.GetAttributes(nested) & ~FileAttributes.ReadOnly);
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    [Timeout(60_000)]
    public async Task Locked_child_does_not_prevent_sweeping_later_children_including_auth(bool secondFailure, CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows()) Skip.Test("Windows file handles without delete sharing model the SQLite lock.");
        using var core = new CoreLab("sweep-home");
        using var codex = new CodexLab(core.Tree);
        var run = await PrepareAsync(core, ct);
        using var home = RecordedHome(core, codex, run, int.MaxValue);
        var nested = Directory.CreateDirectory(Path.Combine(home.Home, "nested")).FullName;
        var auth = Path.Combine(nested, "auth.json");
        File.Move(Path.Combine(home.Home, "auth.json"), auth);
        // Lock the first enumerated non-marker child, not an assumed filesystem ordering.
        var first = Directory.GetFileSystemEntries(home.Home).First(path => path != Path.Combine(home.Home, home.MarkerName!));
        await Assert.That(Path.GetFileName(first)).IsEqualTo("config.toml");
        var later = Path.Combine(nested, "later.sqlite");
        File.WriteAllText(later, "state");
        using var held = new FileStream(first, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
        using var second = secondFailure ? new FileStream(Path.Combine(home.Home, "second.sqlite"), FileMode.Create, FileAccess.ReadWrite, FileShare.Read) : null;
        var last = Directory.GetFileSystemEntries(home.Home).Last(path => path == first || path == second?.Name);
        var expectedError = DeleteFailure(last);
        await using var backend = new CodexBackend(HomeContext(core)) { RunId = core.Ledger.RunId };
        var error = await Task.Run(() => backend.DeleteRetainedHome(run.SavedWorker()), ct);
        await Assert.That(error).IsNotNull();
        await Assert.That(error!.Message).IsEqualTo(expectedError);
        await Assert.That(File.Exists(later)).IsFalse();
        await Assert.That(File.Exists(auth)).IsFalse();
        await Assert.That(File.ReadAllText(Path.Combine(codex.Environment["CODEX_HOME"], "auth.json"))).IsEqualTo("{}");
        await Assert.That(File.ReadAllText(Path.Combine(home.Home, home.MarkerName!))).IsEqualTo(home.MarkerText);
        held.Dispose();
        second?.Dispose();
    }

    [Test]
    [Timeout(60_000)]
    public async Task Throwing_pump_still_deletes_home_cleans_process_and_returns_stop(CancellationToken ct)
    {
        using var lab = new CodexLab();
        File.WriteAllText(Path.Combine(lab.Environment["CODEX_HOME"], "auth.json"), "{}");
        lab.Write("task-edit", CodexLab.Handshake + CodexLab.Thread.Split("{\"expect\":\"turn/start\"")[0]);
        await using var backend = lab.Backend();
        var worker = (await backend.CreateAsync(new("run", "edit", "code", new("codex", "model-y", "high"), null, null,
            new("clean", "on", "workspace", "on", "worktree"), lab.Tree.Repository, new([], []), lab.Environment, null, "clean", null), ct)).Worker;
        var connection = Connections(backend).Single();
        var process = backend.WorkerProcesses.Single().Process;
        connection.GetType().GetProperty("Pump")!.SetValue(connection, Task.FromException(new IOException("pump broke")));
        InterruptResult? stop = null;
        Exception? failure = null;
        try { stop = await backend.ReleaseAsync(worker, ct); }
        catch (Exception error) { failure = error; }
        finally { connection.GetType().GetProperty("Pump")!.SetValue(connection, Task.CompletedTask); }
        await Assert.That(failure).IsNull();
        await Assert.That(stop!.Stopped).IsTrue();
        await Assert.That(stop.HomeRetained).IsNull();
        await Assert.That(Directory.Exists(worker.State["codexHome"]!.GetValue<string>())).IsFalse();
        await Assert.That((bool)typeof(CodexProcess).GetField("disposed", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(process)!).IsTrue();
        await Assert.That(connection.GetType().GetProperty("ExecutionError")!.GetValue(connection)).IsEqualTo("pump broke");
        await Assert.That(backend.HasResources).IsFalse();
    }

    [Test]
    [Timeout(60_000)]
    public async Task Probe_retained_home_is_reported_to_stderr_without_a_signal_drain(CancellationToken ct)
    {
        using var lab = new CodexLab();
        File.WriteAllText(Path.Combine(lab.Environment["CODEX_HOME"], "auth.json"), "{}");
        await using var backend = lab.Backend();
        using var home = CodexHome.CreateProbe(new(Path.Combine(lab.Tree.Root, "profiles"), null, new Dictionary<string, string>()),
            lab.Environment["CODEX_HOME"], lab.Tree.Repository);
        var marker = Path.Combine(home.Home, home.MarkerName!);
        // Connect without a launch, exactly as the clean-profile probe does, then invalidate ownership.
        var variables = new Dictionary<string, string>(lab.Environment) { ["CODEX_HOME"] = home.Home };
        await (Task)typeof(CodexBackend).GetMethod("ConnectAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(backend, [lab.Tree.Repository, variables, home, ct, null, null])!;
        File.Delete(marker);
        var output = TestContext.Current!.Output;
        var start = output.GetErrorOutput().Length;
        try
        {
            await backend.CleanupAsync();
            await Assert.That(output.GetErrorOutput()[start..].ReplaceLineEndings("\n")).IsEqualTo(
                "codex: release: temporary Codex home ownership cannot be confirmed (FileNotFoundException)\n");
            await Assert.That(backend.HasResources).IsFalse();
            await Assert.That(Directory.Exists(home.Home)).IsTrue();
        }
        finally { File.WriteAllText(marker, home.MarkerText); }
    }

    [Test]
    [Timeout(60_000)]
    public async Task Fresh_collect_records_one_release_error_for_the_same_retained_home_result(CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows()) Skip.Test("Windows file handles without delete sharing model the SQLite lock.");
        using var core = new CoreLab("collect-once");
        using var codex = new CodexLab(core.Tree);
        var run = await PrepareAsync(core, ct);
        using var home = RecordedHome(core, codex, run, int.MaxValue);
        core.Ledger.Write(run.Id, "queued", new());
        using var held = new FileStream(Path.Combine(home.Home, "goals_1.sqlite"), FileMode.Create, FileAccess.ReadWrite, FileShare.Read);
        var expectedError = DeleteFailure(held.Name);
        await Assert.That(await Commands.CollectAsync(Args.Parse(["collect", core.Ledger.RunId, run.Id]), ct)).IsEqualTo(1);
        await Assert.That(core.Ledger.LoadResult(run.Id)!.Failures).IsEquivalentTo(new[] { "outcome:lost", "settle-error", "no-final-text" }, CollectionOrdering.Matching);
        await Assert.That(core.Ledger.Read().Where(line => line.Type == "error").Select(line => line.Data!["while"]!.GetValue<string>()))
            .IsEquivalentTo(new[] { "release" }, CollectionOrdering.Matching);
        await Assert.That(core.Ledger.Read().Where(line => line.Type == "error").Select(line => line.Data!["message"]!.GetValue<string>()))
            .IsEquivalentTo(new[] { expectedError }, CollectionOrdering.Matching);
        await Assert.That(File.ReadAllText(Path.Combine(core.Ledger.TaskDir(run.Id), "final.md"))).Contains("Outcome: lost");
        held.Dispose();
    }

    [Test]
    [Timeout(60_000)]
    public async Task BeginSettle_returns_and_releases_run_lock_while_home_retry_is_pending(CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows()) Skip.Test("Windows file handles without delete sharing model the SQLite lock.");
        using var core = new CoreLab("settle-lock");
        using var codex = new CodexLab(core.Tree);
        var run = await PrepareAsync(core, ct);
        using var home = RecordedHome(core, codex, run, int.MaxValue);
        // No recorded identities: stop completes synchronously, leaving only the real deletion retry.
        run.Worker!.State["pid"] = null;
        run.WorkerEnded = true;
        using var held = new FileStream(Path.Combine(home.Home, "goals_1.sqlite"), FileMode.Create, FileAccess.ReadWrite, FileShare.Read);
        await using var recorded = new CodexBackend(HomeContext(core)) { RunId = core.Ledger.RunId };
        var returnedPending = false;
        await using var backend = new FakeBackend("codex", new("sandbox", "approval-gate", "native", false))
        {
            OnCollect = (_, _) => Task.FromResult(new WorkerReport("succeeded", "Report", new(), null, true, ["codex:model-y#high"], null, true)),
            OnRelease = (worker, token) =>
            {
                var release = recorded.ReleaseAsync(worker, token);
                returnedPending = !release.IsCompleted;
                return release;
            },
        };
        try
        {
            BeginSettle(Scheduler(core, backend, ct), run);
            await Assert.That(returnedPending).IsTrue();
            await Assert.That(run.Work!.IsCompleted).IsFalse();
            await Assert.That(await Task.Run(() =>
            {
                if (!Monitor.TryEnter(run)) return false;
                Monitor.Exit(run);
                return true;
            }, ct)).IsTrue();
        }
        finally { await run.Work!; held.Dispose(); }
    }

    [Test]
    [Timeout(60_000)]
    public async Task Prior_confirmed_stop_allows_commit_and_verify_even_when_release_throws(CancellationToken ct)
    {
        using var core = new CoreLab("prior-stop");
        var run = await PrepareAsync(core, ct);
        var verify = Path.Combine(core.Tree.Root, "verified");
        run.Record.Spec.Verify = [OperatingSystem.IsWindows() ? $"Set-Content -LiteralPath '{verify}' -Value ran" : $"touch '{verify}'"];
        File.WriteAllText(Path.Combine(run.Record.Workspace.Path!, "a.txt"), "worker edit");
        run.SaveWorker(new("codex", run.Id, "worker", "turn", new()), core.Ledger);
        await using var backend = new FakeBackend("codex", new("sandbox", "approval-gate", "native", false))
        {
            OnCollect = (_, _) => Task.FromResult(new WorkerReport("succeeded", "Report", new(), null, true, ["codex:model-y#high"], null, true,
                StopResult: new(true, "previous stop confirmed"))),
            OnRelease = (_, _) => throw new InvalidOperationException("release broke"),
        };
        var result = await Settlement.SettleAsync(backend, core.Ledger, run, null, ct, core.Git);
        await Assert.That(result.Failures).IsEquivalentTo(new[] { "release-failed", "settle-error" }, CollectionOrdering.Matching);
        await Assert.That(result.WorkerStopped).IsTrue();
        await Assert.That(result.FilesChanged).IsEquivalentTo(new[] { "a.txt" }, CollectionOrdering.Matching);
        await Assert.That(result.Commit).IsNotNull();
        await Assert.That((await core.Git.CheckAsync(run.Record.Workspace.Path!, ct, "show", $"{result.Commit}:a.txt")).Trim()).IsEqualTo("worker edit");
        await Assert.That(result.Verify.Select(check => check.ExitCode)).IsEquivalentTo(new[] { 0 }, CollectionOrdering.Matching);
        await Assert.That(File.Exists(verify)).IsTrue();
    }

    [Test]
    [Timeout(60_000)]
    public async Task ContinueOf_refuses_release_failed_without_stop_or_collection_evidence(CancellationToken ct)
    {
        using var core = new CoreLab();
        var source = await core.SourceAttemptAsync(ct);
        // Preserve the stopped status so collection evidence, rather than the committed-target gate, decides.
        source.Result.Failures = ["release-failed", "settle-error"];
        source.Result.WorkerStopped = null;
        source.Ledger.SaveResult(source.Result);
        var events = Path.Combine(source.Ledger.Dir, "events.jsonl");
        var lines = File.ReadAllLines(events).Where(line => JsonNode.Parse(line)!["type"]!.GetValue<string>() is not ("collected" or "outcome")).ToArray();
        File.WriteAllLines(events, lines);
        var next = core.Continuation();
        var error = await Assert.ThrowsAsync<HarnessException>(() => Planner.CheckContinuationAsync(next.Record.Spec, next.Record.Settings,
            core.Ledger.RunId, core.Tree.Repository, core.Git, ct));
        await Assert.That(error!.Message).IsEqualTo("continueOf source collection evidence is incomplete or inconsistent");
    }

    static BackendContext HomeContext(CoreLab core) => new(Path.Combine(Locations.ControlDir(core.Ledger.RunId), "profiles"), null, new Dictionary<string, string>());

    static string DeleteFailure(string path)
    {
        try { File.Delete(path); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return error.Message; }
        throw new InvalidOperationException("fixture did not refuse deletion");
    }

    static CodexHome RecordedHome(CoreLab core, CodexLab codex, TaskRun run, int pid)
    {
        File.WriteAllText(Path.Combine(codex.Environment["CODEX_HOME"], "auth.json"), "{}");
        var home = CodexHome.Create(HomeContext(core), new(core.Ledger.RunId, run.Id, "code", new("codex", "model-y", "high"), null, null,
            new("clean", "on", "workspace", "on", "worktree"), run.Record.Workspace.Path!, new([], []), codex.Environment, null, "clean", null), codex.Environment["CODEX_HOME"]);
        run.SaveWorker(new("codex", run.Id, "recorded", null, new()
        {
            ["pid"] = pid, ["codexHome"] = home.Home, ["codexHomeTemporary"] = true,
            ["codexHomeMarkerName"] = home.MarkerName, ["codexHomeMarkerText"] = home.MarkerText,
        }), core.Ledger);
        return home;
    }

    static IEnumerable<object> Connections(CodexBackend backend) =>
        ((System.Collections.IEnumerable)typeof(CodexBackend).GetField("connections", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(backend)!)
            .Cast<object>().Select(entry => entry.GetType().GetProperty("Key")!.GetValue(entry)!);

    static Scheduler Scheduler(CoreLab core, IWorkerBackend backend, CancellationToken ct) =>
        new(new Dictionary<string, IWorkerBackend> { [backend.Name] = backend }, core.Ledger, core.Tree.Repository, AllowList.From(null), core.Policy, 1, ct, core.Git);

    static void BeginSettle(Scheduler scheduler, TaskRun run) =>
        typeof(Scheduler).GetMethod("BeginSettle", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(scheduler, [run, null]);

    static async Task<TaskRun> PrepareAsync(CoreLab core, CancellationToken ct)
    {
        ConfigLayers.Initialize(Args.Parse(["status", "--ledger", Path.GetDirectoryName(core.Ledger.Dir)!]));
        var run = await core.TaskAsync("edit", ct);
        run.Record.Spec.Kind = "code";
        run.Record.Spec.Base = "main";
        run.Record.Spec.Scope = ["a.txt"];
        run.Record.Settings.Backend = new() { Value = "codex" };
        run.Record.Settings.Route = new() { Value = "codex:model-y#high" };
        run.Record.Workspace = new();
        await Worktree.PrepareAsync(run, core.Ledger, core.Tree.Repository, ct, core.Git);
        var info = core.Ledger.LoadRunInfo();
        info.Repo = core.Tree.Repository;
        Json.WriteFile(Path.Combine(core.Ledger.Dir, "run.json"), info, LedgerJson.Files.RunInfo);
        Json.WriteFile(Path.Combine(core.Ledger.Dir, "config.json"), new EffectiveConfig
        { Tasks = new() { [run.Id] = new() { Settings = run.Record.Settings } } }, LedgerJson.Files.EffectiveConfig);
        File.WriteAllText(Path.Combine(Locations.ControlDir(core.Ledger.RunId), "setup.done"), "");
        return run;
    }
}
