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
    [Arguments(false)]
    [Arguments(true)]
    [Timeout(180_000)]
    public async Task Live_settlement_preserves_success_and_clean_retries_only_the_retained_home(bool missingMarker, CancellationToken ct)
    {
        if (!missingMarker && !OperatingSystem.IsWindows()) Skip.Test("Windows file handles without delete sharing model the SQLite lock.");
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
        using var held = missingMarker ? null : new FileStream(Path.Combine(home, "goals_1.sqlite"), FileMode.Create, FileAccess.ReadWrite, FileShare.Read);
        if (missingMarker) File.Delete(marker);
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
            await Assert.That(errors.Single().Data!["message"]!.GetValue<string>()).Contains(missingMarker ? "FileNotFoundException" : "goals_1.sqlite");
            await Assert.That(Directory.Exists(home)).IsTrue();
            if (!missingMarker) await Assert.That(File.ReadAllText(marker)).IsEqualTo(worker.State["codexHomeMarkerText"]!.GetValue<string>());
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
            await Assert.That(exit).IsEqualTo(missingMarker ? 0 : 1);
            await Assert.That(Directory.Exists(run.Record.Workspace.Path)).IsFalse();
            await Assert.That(Directory.Exists(home)).IsTrue();
            if (missingMarker)
            {
                await Assert.That(output.GetStandardOutput()[start..]).Contains($"edit: codex home has no ownership marker; delete it by hand: {home}");
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
                await Assert.That(output.GetStandardOutput()[start..]).Contains($"edit: codex home has no ownership marker; delete it by hand: {home.Home}");
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
