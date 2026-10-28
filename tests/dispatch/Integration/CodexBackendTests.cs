using System.Text.Json.Nodes;
using Dispatch.Tests.Support;
using TUnit.Assertions.Enums;

namespace Dispatch.Tests.Integration;

[Category("Integration")]
public sealed class CodexBackendTests
{
    [Test]
    [Arguments("release")]
    [Arguments("collect")]
    [Arguments("resume")]
    [NotInParallel]
    [Timeout(180_000)]
    public async Task Recorded_release_retry_retains_a_walk_discovered_survivor_and_its_home(string operation, CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows()) Skip.Test("Windows native child walk only");
        using var core = new CoreLab();
        using var lab = new CodexLab(core.Tree);
        File.WriteAllText(Path.Combine(lab.Environment["CODEX_HOME"], "auth.json"), "{}");
        var run = await core.TaskAsync("inspect", ct);
        run.Record.Settings.Backend = new() { Value = "codex" };
        lab.Write("task-inspect", CodexLab.Handshake + CodexLab.Thread.Split("{\"expect\":\"turn/start\"")[0] + "{\"hang\":true}");
        await using var original = lab.Backend();
        var worker = (await original.CreateAsync(new(core.Ledger.RunId, run.Id, "research", new("codex", "model-y", "high"), null, null,
            new("clean", "on", "workspace", "on", "in-place"), core.Tree.Repository, new([], []), lab.Environment, null, "clean", null), ct)).Worker;
        run.SaveWorker(worker, core.Ledger);
        var root = original.WorkerProcesses.Single().Process;
        var home = worker.State["codexHome"]!.GetValue<string>();
        await using var recovered = lab.Backend();
        // Script a matching, unkillable child through the existing observation seams. Its PID has
        // no native process, so recovery cannot accidentally terminate an unrelated real process.
        recovered.OpenProcess = pid => pid == int.MaxValue ? throw new System.ComponentModel.Win32Exception(5) : Process.GetProcessById(pid);
        recovered.QueryLimitedStartTime = _ => throw new System.ComponentModel.Win32Exception(5, "query denied");
        recovered.ReadSnapshot = _ => new() { [int.MaxValue] = (root.Id, root.StartTime) };
        InterruptResult? first = null;
        await using var caller = new FakeBackend("codex", recovered.Capabilities)
        {
            OnCollect = (_, _) => Task.FromResult(new WorkerReport("succeeded", "Completed.", new(), null, true, [], null, true)),
            OnRelease = async (reference, token) => first = await recovered.ReleaseAsync(reference, token),
        };
        var initial = await Settlement.SettleAsync(caller, core.Ledger, run, null, ct, core.Git);
        await Assert.That(initial.Failures).IsEquivalentTo(new[] { "zombie" }, CollectionOrdering.Matching);
        await Assert.That(first!.Stopped).IsFalse();
        await Assert.That(first.Detail).IsEqualTo($"recorded process recovery; pid={root.Id}; recordedStartTime={root.StartTime:O}; currentStartTime={root.StartTime:O}; handle=alive; observe pid={int.MaxValue}: query denied; pid={int.MaxValue}; recordedStartTime={root.StartTime:O}; currentStartTime={root.StartTime:O}; snapshot=alive");
        // A fresh command gets only the ledger, not the worker object the first release mutated.
        var ledger = new Ledger(core.Ledger.RunId);
        var retryRun = TaskRun.FromLedger(ledger.LoadRecord(run.Id), ledger);
        var restored = retryRun.SavedWorker();
        await using var fresh = lab.Backend();
        fresh.OpenProcess = recovered.OpenProcess;
        fresh.QueryLimitedStartTime = recovered.QueryLimitedStartTime;
        fresh.ReadSnapshot = recovered.ReadSnapshot;
        InterruptResult? retry;
        if (operation == "release") retry = await fresh.ReleaseAsync(restored, ct);
        else
        {
            if (operation == "resume")
            {
                var scheduler = new Scheduler(new Dictionary<string, IWorkerBackend> { [fresh.Name] = fresh }, ledger,
                    core.Tree.Repository, AllowList.From(null), core.Policy, 1, ct, core.Git);
                await Assert.That(await scheduler.ResumeAsync([retryRun], false)).IsEqualTo(1);
            }
            else await Settlement.SettleAsync(fresh, ledger, retryRun, null, ct, core.Git);
            var result = ledger.LoadResult(run.Id)!;
            await Assert.That(result.State).IsEqualTo("failed");
            await Assert.That(result.WorkerStopped).IsFalse();
            await Assert.That(result.Failures).IsEquivalentTo(new[] { "zombie", "outcome:lost", "settle-error", "no-final-text" }, CollectionOrdering.Matching);
            await Assert.That(result.Verify).IsEmpty();
            await Assert.That(ledger.Read().Select(line => line.Type)).IsEquivalentTo(operation == "resume"
                ? new[] { "stop-unconfirmed", "outcome", "outcome", "final" }
                : new[] { "stop-unconfirmed", "outcome", "outcome" }, CollectionOrdering.Matching);
            retry = ((ReattachResult.Ended)await fresh.ReattachAsync(restored, ct)).StopResult;
        }
        await Assert.That(retry!.Stopped).IsFalse();
        await Assert.That(retry.Detail).IsEqualTo($"recorded process recovery; observe pid={int.MaxValue}: query denied; pid={int.MaxValue}; recordedStartTime={root.StartTime:O}; currentStartTime={root.StartTime:O}; snapshot=alive");
        await Assert.That(root.HasExited).IsTrue();
        await Assert.That(Directory.Exists(home)).IsTrue();
        await Assert.That(restored.State["descendants"]!.AsArray().Select(record => record!["pid"]!.GetValue<int>())).IsEquivalentTo(new[] { int.MaxValue }, CollectionOrdering.Matching);
        await Assert.That(restored.State["descendants"]![0]!["startTime"]!.GetValue<string>()).IsEqualTo(root.StartTime.ToString("O"));
        fresh.ReadSnapshot = _ => new();
        await Assert.That((await fresh.ReleaseAsync(restored, ct))!.Stopped).IsTrue();
        await Assert.That(Directory.Exists(home)).IsFalse();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    [NotInParallel]
    [Timeout(180_000)]
    public async Task Live_unconfirmed_stop_saves_walk_discovered_identities_before_returning(bool reattach, CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows()) Skip.Test("Windows native child walk only");
        using var lab = new CodexLab();
        using var ownerLab = new CodexLab();
        using var childLab = new CodexLab();
        foreach (var fixture in new[] { lab, ownerLab, childLab })
            fixture.Write("preflight", CodexLab.Handshake + CodexLab.Thread.Split("{\"expect\":\"turn/start\"")[0] + "{\"hang\":true}");
        await using var backend = lab.Backend();
        await using var ownerBackend = ownerLab.Backend();
        await using var childBackend = childLab.Backend();
        async Task<WorkerRef> Create(CodexLab fixture, CodexBackend target) => (await target.CreateAsync(new("run", "inspect", "research", new("codex", "model-y", "high"), null, null,
            new("full", "on", "workspace", "on", "in-place"), fixture.Tree.Repository, new([], []), fixture.Environment, null, "full", null), ct)).Worker;
        var worker = await Create(lab, backend);
        await Create(ownerLab, ownerBackend);
        await Create(childLab, childBackend);
        var process = backend.WorkerProcesses.Single().Process;
        var owner = ownerBackend.WorkerProcesses.Single().Process;
        var child = childBackend.WorkerProcesses.Single().Process;
        var snapshot = process.ReadSnapshot;
        var nativeOpen = process.NativeOpenProcess;
        var terminate = process.TerminateProcess;
        process.ReadSnapshot = _ => new() { [owner.Id] = (process.Id, owner.StartTime) };
        process.RefreshDescendants();
        using (var holder = Process.GetProcessById(process.Id))
        {
            holder.Kill();
            await holder.WaitForExitAsync(ct);
        }
        var walking = false;
        process.ReadSnapshot = _ => walking ? new() { [child.Id] = (owner.Id, child.StartTime) } : new();
        process.NativeOpenProcess = (access, inherit, pid) => pid == child.Id
            ? throw new System.ComponentModel.Win32Exception(5, "child denied") : nativeOpen(access, inherit, pid);
        process.TerminateProcess = (handle, exit) =>
        {
            using var holder = Process.GetProcessById(owner.Id);
            var killed = terminate(handle, exit);
            holder.WaitForExit(5000);
            walking = true;
            return killed;
        };
        try
        {
            var stopped = reattach ? ((ReattachResult.Ended)await backend.ReattachAsync(worker, ct)).StopResult!
                : await backend.InterruptAsync(worker, InterruptKind.Hard, ct);
            await Assert.That(stopped.Stopped).IsFalse();
            await Assert.That(stopped.Detail).IsEqualTo($"exit={process.ExitCode}; processGroup=false; groupKill=True; treeKill=False; descendantsKilled=1; stderrDroppedLines=0; kill descendant pid={child.Id}: child denied; pid={owner.Id}; recordedStartTime={owner.StartTime:O}; currentStartTime={owner.StartTime:O}; handle=alive; pid={child.Id}; recordedStartTime={child.StartTime:O}; currentStartTime={child.StartTime:O}; handle=alive\nstderr:\n");
            await Assert.That(worker.State["descendants"]!.AsArray().Select(record => record!["pid"]!.GetValue<int>())).IsEquivalentTo(new[] { owner.Id, child.Id }, CollectionOrdering.Matching);
            await Assert.That(worker.State["descendants"]![1]!["startTime"]!.GetValue<string>()).IsEqualTo(child.StartTime.ToString("O"));
            await Assert.That(owner.HasExited).IsTrue();
            await Assert.That(child.HasExited).IsFalse();
        }
        finally
        {
            process.ReadSnapshot = snapshot;
            process.NativeOpenProcess = nativeOpen;
            process.TerminateProcess = terminate;
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    [NotInParallel]
    [Timeout(180_000)]
    public async Task Restart_stop_uncertainty_is_typed_and_settles_only_its_task(bool resume, CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows()) Skip.Test("Windows snapshot identity seam");
        using var core = new CoreLab();
        using var lab = new CodexLab(core.Tree);
        var run = await core.TaskAsync("inspect", ct);
        run.Record.Settings.Backend = new() { Value = "codex" };
        run.SaveWorker(new("codex", run.Id, "thread", "turn", new()
        {
            ["pid"] = int.MaxValue, ["processStartTime"] = DateTime.UnixEpoch.ToString("O"),
        }), core.Ledger);
        var restored = TaskRun.FromLedger(core.Ledger.LoadRecord(run.Id), core.Ledger);
        await using var backend = lab.Backend();
        backend.OpenProcess = _ => throw new System.ComponentModel.Win32Exception(5, "open denied");
        backend.QueryLimitedStartTime = _ => throw new System.ComponentModel.Win32Exception(5, "query denied");
        backend.ReadSnapshot = _ => new() { [int.MaxValue] = (0, DateTime.UnixEpoch) };
        if (resume)
        {
            var scheduler = new Scheduler(new Dictionary<string, IWorkerBackend> { ["codex"] = backend }, core.Ledger,
                core.Tree.Repository, AllowList.From(null), core.Policy, 1, ct, core.Git);
            await Assert.That(await scheduler.ResumeAsync([restored], false)).IsEqualTo(1);
        }
        else
        {
            var result = await Settlement.SettleAsync(backend, core.Ledger, restored, null, ct, core.Git);
            core.Ledger.Write(run.Id, "final", new() { ["state"] = result.State, ["outcome"] = result.Outcome, ["via"] = "collect" });
        }
        await backend.DisposeAsync();
        var saved = core.Ledger.LoadResult(run.Id)!;
        await Assert.That(saved.State).IsEqualTo("failed");
        await Assert.That(saved.Outcome).IsEqualTo("lost");
        await Assert.That(saved.Failures).IsEquivalentTo(new[] { "zombie", "outcome:lost", "settle-error", "no-final-text" }, CollectionOrdering.Matching);
        await Assert.That(saved.WorkerStopped).IsFalse();
        await Assert.That(saved.Verify).IsEmpty();
        await Assert.That(core.Ledger.Read().Select(x => x.Type)).IsEquivalentTo(new[] { "stop-unconfirmed", "outcome", "final" }, CollectionOrdering.Matching);
        await Assert.That(core.Ledger.Read().Single(x => x.Type == "stop-unconfirmed").Data!["detail"]!.GetValue<string>()).IsEqualTo(
            $"recorded process recovery; observe pid={int.MaxValue}: query denied; pid={int.MaxValue}; recordedStartTime={DateTime.UnixEpoch:O}; currentStartTime={DateTime.UnixEpoch:O}; snapshot=alive");
    }

    [Test]
    [Arguments("reattach")]
    [Arguments("release")]
    [Arguments("resume")]
    [Arguments("collect")]
    [NotInParallel]
    [Timeout(180_000)]
    public async Task Restart_recovery_rejects_reused_recorded_identities(string operation, CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows()) Skip.Test("Windows snapshot identities only");
        using var core = new CoreLab();
        using var lab = new CodexLab(core.Tree);
        var run = await core.TaskAsync("inspect", ct);
        run.Record.Settings.Backend = new() { Value = "codex" };
        run.Record.Settings.Route = new() { Value = "codex:model-y#high" };
        lab.Write("preflight", CodexLab.Handshake + "{\"spawnChild\":true}\n" + CodexLab.Thread.Split("{\"expect\":\"turn/start\"")[0] + "{\"hang\":true}");
        await using var original = lab.Backend();
        var worker = (await original.CreateAsync(new("run", run.Id, "research", new("codex", "model-y", "high"), null, null,
            new("full", "on", "workspace", "on", "in-place"), core.Tree.Repository, new([], []), lab.Environment, null, "full", null), ct)).Worker;
        await original.PollAsync([worker], ct);
        var root = original.WorkerProcesses.Single().Process;
        var childPid = Directory.GetFiles(lab.Records, "start.json", SearchOption.AllDirectories)
            .Select(path => JsonNode.Parse(File.ReadAllText(path))!)
            .Single(record => record["argv"]![0]!.GetValue<string>() == "--child")["pid"]!.GetValue<int>();
        var child = root.Descendants.Single(identity => identity.Pid == childPid);
        var current = new Dictionary<int, (int Parent, DateTime? CreationTime)>
        {
            [root.Id] = (Environment.ProcessId, root.StartTime), [child.Pid] = (root.Id, child.StartTime),
        };
        worker.State["processStartTime"] = root.StartTime.AddDays(-1).ToString("O");
        worker.State["descendants"] = new JsonArray(new JsonObject { ["pid"] = child.Pid, ["startTime"] = child.StartTime!.Value.AddDays(-1).ToString("O") });
        run.SaveWorker(worker, core.Ledger);
        var restored = TaskRun.FromLedger(core.Ledger.LoadRecord(run.Id), core.Ledger);
        await Assert.That(restored.SavedWorker().State["processStartTime"]!.GetValue<string>()).IsEqualTo(root.StartTime.AddDays(-1).ToString("O"));
        await Assert.That(restored.SavedWorker().State["descendants"]![0]!["startTime"]!.GetValue<string>()).IsEqualTo(child.StartTime.Value.AddDays(-1).ToString("O"));
        var trace = new List<string>();
        await using var backend = lab.Backend();
        backend.OpenProcess = pid =>
        {
            trace.Add($"open {pid}");
            throw new System.ComponentModel.Win32Exception(5);
        };
        backend.QueryLimitedStartTime = pid =>
        {
            trace.Add($"limited {pid}");
            throw new System.ComponentModel.Win32Exception(5);
        };
        backend.ReadSnapshot = _ => current;
        if (operation == "release")
        {
            HarnessException? failure = null;
            try { await backend.ReleaseAsync(restored.SavedWorker(), ct); }
            catch (HarnessException exception) { failure = exception; }
            await Assert.That(failure).IsNull();
        }
        else if (operation == "reattach")
        {
            var ended = await backend.ReattachAsync(restored.SavedWorker(), ct);
            await Assert.That(ended).IsTypeOf<ReattachResult.Ended>();
            await Assert.That(((ReattachResult.Ended)ended).Outcome).IsEqualTo("lost");
        }
        else
        {
            if (operation == "resume")
            {
                var scheduler = new Scheduler(new Dictionary<string, IWorkerBackend> { ["codex"] = backend }, core.Ledger,
                    core.Tree.Repository, AllowList.From(null), core.Policy, 1, ct, core.Git);
                await Assert.That(await scheduler.ResumeAsync([restored], false)).IsEqualTo(1);
            }
            else
            {
                HarnessException? failure = null;
                TaskResult? result = null;
                try { result = await Settlement.SettleAsync(backend, core.Ledger, restored, null, ct, core.Git); }
                catch (HarnessException exception) { failure = exception; }
                await Assert.That(failure).IsNull();
                core.Ledger.Write(run.Id, "final", new JsonObject { ["state"] = result!.State, ["outcome"] = result.Outcome, ["via"] = "collect" });
            }
            var saved = core.Ledger.LoadResult(run.Id)!;
            await Assert.That(saved).IsNotNull();
            await Assert.That(saved.State).IsEqualTo("failed");
            await Assert.That(saved.Outcome).IsEqualTo("lost");
            await Assert.That(saved.Failures).IsEquivalentTo(new[] { "outcome:lost", "settle-error", "no-final-text" }, CollectionOrdering.Matching);
            await Assert.That(core.Ledger.Read().Select(item => item.Type)).IsEquivalentTo(new[] { "collected", "verified", "outcome", "final" }, CollectionOrdering.Matching);
            await Assert.That(core.Ledger.Read().Last().Data!["state"]!.GetValue<string>()).IsEqualTo("failed");
        }
        await backend.DisposeAsync();
        await Assert.That(root.HasExited).IsFalse();
        using var holder = Process.GetProcessById(child.Pid);
        await Assert.That(holder.HasExited).IsFalse();
        await Assert.That(holder.StartTime.ToUniversalTime()).IsEqualTo(child.StartTime);
        await Assert.That(trace).IsEquivalentTo(new[]
        {
            $"open {child.Pid}", $"limited {child.Pid}", $"open {root.Id}", $"limited {root.Id}",
            $"open {root.Id}", $"limited {root.Id}", $"open {child.Pid}", $"limited {child.Pid}",
            $"open {root.Id}", $"limited {root.Id}", $"open {child.Pid}", $"limited {child.Pid}",
        }, CollectionOrdering.Matching);
        await Assert.That(core.Ledger.Read().Where(item => item.Type is "aborted" or "error")).IsEmpty();
    }

    [Test]
    public async Task Wire_records_publish_complete_snapshots_while_a_reader_is_open()
    {
        using var lab = new CodexLab();
        var directory = Directory.CreateDirectory(Path.Combine(lab.Records, "000001-preflight.jsonl")).FullName;
        var assembly = System.Reflection.Assembly.LoadFrom(Path.ChangeExtension(CodexLab.Executable, ".dll"));
        var type = assembly.GetType("Wire", throwOnError: true)!;
        var wire = Activator.CreateInstance(type, directory)!;
        var record = type.GetMethod("Record")!;
        void Publish(string method)
        {
            var message = new JsonObject { ["method"] = method };
            record.Invoke(wire, ["in", System.Text.Encoding.UTF8.GetBytes(message.ToJsonString() + "\n"), message]);
        }
        Publish("initialize");
        // Match AppendAllText's sharing: readers may open, but must also share the writer's access.
        using (var writing = new FileStream(Path.Combine(directory, "wire.jsonl"), FileMode.Open, FileAccess.Write, FileShare.Read))
            await Assert.That(lab.Input().Select(x => x["method"]!.GetValue<string>()))
                .IsEquivalentTo(new[] { "initialize" }, CollectionOrdering.Matching);
        using var snapshot = new FileStream(Path.Combine(directory, "wire.jsonl"), FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        Publish("turn/start");
        using var reader = new StreamReader(snapshot);
        var previous = (await reader.ReadToEndAsync()).Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(x => JsonNode.Parse(x)!["message"]!["method"]!.GetValue<string>()).ToArray();
        await Assert.That(previous).IsEquivalentTo(new[] { "initialize" }, CollectionOrdering.Matching);
        await Assert.That(lab.Input().Select(x => x["method"]!.GetValue<string>()))
            .IsEquivalentTo(new[] { "initialize", "turn/start" }, CollectionOrdering.Matching);
    }

    [Test]
    [Timeout(180_000)]
    public async Task Configured_environments_reach_process_and_children_without_changing_probes(CancellationToken ct)
    {
        using var lab = new CodexLab();
        File.WriteAllText(Path.Combine(lab.Environment["CODEX_HOME"], "auth.json"), "{}");
        lab.Environment["FAKE_CODEX_AUTH_MARK"] = "synthetic-passthrough";
        lab.Write("task", CodexLab.Handshake + CodexLab.Thread.Split('\n')[0] + "\n{\"spawnChild\":true}\n{\"notify\":\"warning\",\"params\":{\"message\":\"child-ready\"}}\n");
        await using var backend = lab.Backend();
        await backend.ProbeAsync(new(lab.Tree.Repository), ct);
        var probes = Directory.GetFiles(lab.Records, "start.json", SearchOption.AllDirectories).Select(x => JsonNode.Parse(File.ReadAllText(x))!).ToArray();
        foreach (var probe in probes) await Assert.That(probe["environmentNames"]!.AsArray().Select(x => x!.GetValue<string>())).DoesNotContain("TASK_ONLY");
        foreach (var id in new[] { "z-first", "a-second" })
        {
            var record = Path.Combine(lab.Tree.Root, id + "-record");
            var configured = new Dictionary<string, string>
            {
                ["FAKE_CODEX_RECORD"] = record, ["FAKE_CODEX_AUTH_MARK"] = id + "-synthetic-value", ["TASK_ONLY"] = "synthetic-child-value",
            };
            var variables = WorkerEnvironment.Copy(lab.Environment);
            foreach (var (name, value) in configured) variables[name] = value;
            var created = await backend.CreateAsync(new("run", id, "code", new("codex", "model-y", "high"), null, null,
                new("clean", "on", "workspace", "on", "worktree"), lab.Tree.Repository, new([], []), variables, null, "clean", null,
                ConfiguredEnvironment: configured), ct);
            var process = backend.WorkerProcesses.Single(x => x.Worker.Id == created.Worker.Id).Process;
            var workers = (System.Collections.IEnumerable)typeof(CodexBackend).GetField("workers", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(backend)!;
            var connection = workers.Cast<object>().Select(x => x.GetType().GetProperty("Value")!.GetValue(x)!).Single(x =>
                ((CodexProcess)x.GetType().GetProperty("Process")!.GetValue(x)!).Id == process.Id);
            var rpc = (CodexRpc)connection.GetType().GetProperty("Rpc")!.GetValue(connection)!;
            var ready = await rpc.Notifications.ReadAsync(ct);
            await Assert.That(ready.Method).IsEqualTo("warning");
            await Assert.That(ready.Params.GetProperty("message").GetString()).IsEqualTo("child-ready");
            await Assert.That(File.ReadAllText(Path.Combine(lab.Environment["CODEX_HOME"], "auth.json"))).IsEqualTo(id + "-synthetic-value");
            await Assert.That(Directory.Exists(record)).IsTrue();
            var starts = Directory.GetFiles(record, "start.json", SearchOption.AllDirectories).Select(x => JsonNode.Parse(File.ReadAllText(x))!).ToArray();
            await Assert.That(starts.Length).IsEqualTo(2);
            foreach (var start in starts)
            {
                await Assert.That(start["environmentNames"]!.AsArray().Select(x => x!.GetValue<string>())).Contains("TASK_ONLY");
                await Assert.That(start["codexHome"]!.GetValue<string>()).IsEqualTo(created.Worker.State["codexHome"]!.GetValue<string>());
            }
            await backend.ReleaseAsync(created.Worker, ct);
        }
        await Assert.That(Directory.GetFiles(lab.Records, "start.json", SearchOption.AllDirectories).Length).IsEqualTo(probes.Length);
    }

    [Test]
    [Timeout(180_000)]
    public async Task Configured_PATH_cannot_select_backend_or_session_launcher(CancellationToken ct)
    {
        using var lab = new CodexLab();
        lab.Write("preflight", CodexLab.Handshake + CodexLab.Thread.Split('\n')[0] + "\n");
        var trusted = Directory.CreateDirectory(Path.Combine(lab.Tree.Root, "trusted-tools")).FullName;
        var shim = Path.Combine(trusted, OperatingSystem.IsWindows() ? "codex.cmd" : "codex");
        File.WriteAllText(shim, OperatingSystem.IsWindows() ? $"@echo off\r\n\"{CodexLab.Executable}\" %*\r\n"
            : $"#!/bin/sh\nexec '{CodexLab.Executable}' \"$@\"\n");
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(shim, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        lab.Environment["PATH"] = trusted + Path.PathSeparator + lab.Environment["PATH"];
        var configured = new Dictionary<string, string> { ["PATH"] = Directory.CreateDirectory(Path.Combine(lab.Tree.Root, "task-tools")).FullName };
        var variables = WorkerEnvironment.Copy(lab.Environment);
        variables["PATH"] = configured["PATH"];
        await using var backend = new CodexBackend(new(Path.Combine(lab.Tree.Root, "profiles"), null, new Dictionary<string, string>()), lab.Environment);
        CreateResult? created = null;
        HarnessException? failure = null;
        try { created = await backend.CreateAsync(new("run", "worker", "code", new("codex", "model-y", "high"), null, null,
            new("full", "on", "workspace", "on", "worktree"), lab.Tree.Repository, new([], []), variables, null, "full", null,
            ConfiguredEnvironment: configured), ct); }
        catch (HarnessException ex) { failure = ex; }
        await Assert.That(failure).IsNull();
        await Assert.That(created!.Details!.Route).IsEqualTo("codex:model-y#high");
        if (OperatingSystem.IsLinux())
            await Assert.That(backend.WorkerProcesses.Single().Process.ProcessGroup).IsEqualTo(backend.WorkerProcesses.Single().Process.Id);
        await backend.ReleaseAsync(created.Worker, ct);
    }

    [Test]
    [Timeout(180_000)]
    public async Task Workspace_writableRoots_guard_sends_worktree_temp_and_configured_roots(CancellationToken ct)
    {
        using var lab = new CodexLab();
        await LabRepo.CreateAsync(lab.Tree, ct);
        lab.Write("preflight", CodexLab.Handshake + CodexLab.Thread + CodexLab.Final);
        await using var backend = lab.Backend();
        var temp = Path.Combine(lab.Tree.Root, "task-temp");
        var first = Path.Combine(lab.Tree.Root, "z-cache");
        var second = Path.Combine(lab.Tree.Root, "a-cache");
        var variables = new Dictionary<string, string>(lab.Environment) { ["TEMP"] = temp };
        var created = await backend.CreateAsync(new("run", "edit", "code", new("codex", "model-y", "high"), null, null,
            new("full", "on", "workspace", "on", "worktree"), lab.Tree.Repository, new([], []), variables, null, "full", null,
            [first, second]), ct);
        var worker = await backend.PromptAsync(created.Worker, new("Perform the task.", false), ct);
        // The fake records turn/start when it reads it, which can be after the prompt returns.
        await backend.CollectAsync(worker, ct);
        var turn = lab.Input().Single(x => x["method"]?.GetValue<string>() == "turn/start")["params"]!;
        await Assert.That(turn["sandboxPolicy"]!["writableRoots"]!.AsArray().Select(x => x!.GetValue<string>()))
            .IsEquivalentTo(new[] { lab.Tree.Repository, temp, first, second }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    [Timeout(180_000)]
    public async Task Unknown_internal_access_is_refused_instead_of_requesting_full_access(CancellationToken cancellationToken)
    {
        using var lab = new CodexLab();
        lab.Write("preflight", CodexLab.Handshake + CodexLab.Thread.Replace("workspaceWrite", "dangerFullAccess"));
        await using var backend = lab.Backend();
        var modes = new Modes("full", "on", "workspace", "on", "worktree");
        // Bypass the contract guard to exercise the backend's independent fail-closed boundary.
        typeof(Modes).GetField("<Access>k__BackingField", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(modes, "unknown");
        var failure = await Assert.ThrowsAsync<HarnessException>(() => backend.CreateAsync(new("run", "edit", "code", new("codex", "model-y", "high"), null, null,
            modes, lab.Tree.Repository, new([], []), lab.Environment, null, "full", null), cancellationToken));
        await Assert.That(failure!.Message).Contains("access");
        if (Directory.Exists(lab.Records))
            await Assert.That(lab.Input().Where(x => x["method"]?.GetValue<string>() is "thread/start" or "turn/start")).IsEmpty();
    }

    [Test]
    [Arguments("routes")]
    [Arguments("selfcheck")]
    [Timeout(180_000)]
    public async Task Catalog_commands_select_Codex_from_the_committed_repository_config(string command, CancellationToken cancellationToken)
    {
        using var lab = new CodexLab();
        if (command == "selfcheck") lab.Write("task", CodexLab.Handshake + CodexLab.Thread.Split('\n')[0] + "\n");
        var repo = await LabRepo.CreateAsync(lab.Tree, cancellationToken);
        var userPath = Path.Combine(lab.Tree.DispatchHome, "config", "config.jsonc");
        var user = JsonNode.Parse(File.ReadAllText(userPath))!;
        user["preferences"]!.AsObject().Remove("backend");
        File.WriteAllText(userPath, user.ToJsonString());
        var repositoryPath = Path.Combine(Directory.CreateDirectory(Path.Combine(repo.Root, ".dispatch")).FullName, "config.jsonc");
        File.WriteAllText(repositoryPath, """{"schemaVersion":2,"preferences":{"backend":"codex"}}""");
        await repo.GitAsync(["add", ".dispatch/config.jsonc"], cancellationToken);
        await repo.GitAsync(["commit", "-qm", "Configure backend"], cancellationToken);
        File.WriteAllText(repositoryPath, """{"schemaVersion":2,"preferences":{"backend":"opencode"}}""");
        using var harness = new HarnessProcess(lab.Tree);
        var result = await harness.RunAsync([command, "--repo", repo.Root], cancellationToken,
            lab.Environment.ToDictionary(x => x.Key, x => (string?)x.Value));
        result.RequireSuccess("repository-selected catalog command");
        await Assert.That(result.Stdout).Contains("backend codex");
        await Assert.That(result.Stdout).Contains(command == "routes" ? "codex:model-y" : "Codex 0.163.0");
        // Selfcheck's launch check starts one thread and never a turn; routes starts neither.
        await Assert.That(lab.Input().Where(x => x["method"]?.GetValue<string>() is "thread/start" or "turn/start")
            .Select(x => x["method"]!.GetValue<string>()).ToArray()).IsEquivalentTo(command == "selfcheck" ? new[] { "thread/start" } : []);
        // Neither command owns a run, so neither writes into the shared ledger root.
        var ledgers = Path.Combine(lab.Tree.DispatchHome, "state", "ledgers");
        await Assert.That(Directory.Exists(ledgers) ? Directory.GetFileSystemEntries(ledgers) : []).IsEmpty();
    }

    [Test]
    [Timeout(180_000)]
    public async Task Stale_main_turn_approval_is_declined_without_admitting_an_ask(CancellationToken cancellationToken)
    {
        using var lab = new CodexLab();
        lab.Write("preflight", CodexLab.Handshake + CodexLab.Thread + """
            {"request":"item/commandExecution/requestApproval","params":{"threadId":"$threadId","turnId":"old-turn","itemId":"old-call","command":"old-command"},"result":{"decision":"decline"}}

            """ + CodexLab.Final);
        await using var backend = lab.Backend();
        var created = await backend.CreateAsync(new("run", "edit", "code", new("codex", "model-y", "high"), null, null,
            new("full", "on", "workspace", "on", "worktree"), lab.Tree.Repository, new([], []), lab.Environment, null, "full", null), cancellationToken);
        var worker = await backend.PromptAsync(created.Worker, new("Perform the task.", false), cancellationToken);
        WorkerSignal signal;
        do { signal = await backend.Signals.ReadAsync(cancellationToken); }
        while (signal is not (WorkerSignal.Ask or WorkerSignal.Ended));
        await Assert.That(signal).IsTypeOf<WorkerSignal.Ended>();
        await Assert.That((await backend.CollectAsync(worker, cancellationToken)).FinalText).IsEqualTo("Completed.");
        await Assert.That(lab.Input().Single(x => x.ContainsKey("result"))["result"]!["decision"]!.GetValue<string>()).IsEqualTo("decline");
        await Assert.That(lab.Input().Where(x => x["method"]?.GetValue<string>() == "turn/steer")).IsEmpty();
    }

    [Test]
    [Arguments("usage")]
    [Arguments("reroute")]
    [Arguments("error")]
    [Timeout(180_000)]
    public async Task Stale_main_turn_events_do_not_mutate_the_current_report_or_signals(string kind, CancellationToken cancellationToken)
    {
        using var lab = new CodexLab();
        var stale = kind switch
        {
            "usage" => """{"notify":"thread/tokenUsage/updated","params":{"threadId":"$threadId","turnId":"old-turn","tokenUsage":{"total":{"inputTokens":900,"outputTokens":900}}}}""",
            "reroute" => """{"notify":"model/rerouted","params":{"threadId":"$threadId","turnId":"old-turn","toModel":"stale-model","fromModel":"model-y","reason":"capacity"}}""",
            _ => """{"notify":"error","params":{"threadId":"$threadId","turnId":"old-turn","willRetry":true,"error":{"message":"stale error"}}}""",
        };
        lab.Write("preflight", CodexLab.Handshake + CodexLab.Thread + """
            {"notify":"thread/tokenUsage/updated","params":{"threadId":"$threadId","turnId":"$turnId","tokenUsage":{"total":{"inputTokens":10,"outputTokens":2}}}}

            """ + stale + "\n" + CodexLab.Final);
        await using var backend = lab.Backend();
        var created = await backend.CreateAsync(new("run", "edit", "code", new("codex", "model-y", "high"), null, null,
            new("full", "on", "workspace", "on", "worktree"), lab.Tree.Repository, new([], []), lab.Environment, null, "full", null), cancellationToken);
        var worker = await backend.PromptAsync(created.Worker, new("Perform the task.", false), cancellationToken);
        var report = await backend.CollectAsync(worker, cancellationToken);
        await Assert.That(report.Usage.Input).IsEqualTo(10);
        await Assert.That(report.Usage.Output).IsEqualTo(2);
        await Assert.That(report.RoutesUsed).IsEquivalentTo(new[] { "codex:model-y#high" });
        var signals = new List<WorkerSignal>();
        while (backend.Signals.TryRead(out var signal)) signals.Add(signal);
        await Assert.That(signals.OfType<WorkerSignal.Fault>()).IsEmpty();
        await Assert.That(signals.OfType<WorkerSignal.Retry>()).IsEmpty();
        await Assert.That(report.FinalText).IsEqualTo("Completed.");
    }

    [Test]
    [Timeout(180_000)]
    public async Task Preflight_resolves_a_configured_command_shim_with_spaces_on_Windows(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows()) Skip.Test("Windows command shims only");
        using var lab = new CodexLab();
        var folder = Directory.CreateDirectory(Path.Combine(lab.Tree.Root, "shim with spaces & punctuation")).FullName;
        var shim = Path.Combine(folder, "codex.cmd");
        File.WriteAllText(shim, $"@echo off\r\n\"{CodexLab.Executable}\" %*\r\n");
        await using var backend = new CodexBackend(new(Path.Combine(lab.Tree.Root, "profiles"), shim, new Dictionary<string, string>()), lab.Environment);
        await Assert.That((await backend.ProbeAsync(new(lab.Tree.Repository), cancellationToken)).Ready).IsTrue();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    [Timeout(180_000)]
    public async Task Codex_rejects_PowerShell_only_resolution(bool configured, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows()) Skip.Test("Windows PowerShell shims only");
        using var lab = new CodexLab();
        var first = Directory.CreateDirectory(Path.Combine(lab.Tree.Root, "first shim")).FullName;
        var second = Directory.CreateDirectory(Path.Combine(lab.Tree.Root, "second shim")).FullName;
        var script = Path.Combine(first, "codex.ps1");
        File.WriteAllText(script, "exit 97\n");
        File.WriteAllText(Path.Combine(second, "codex.ps1"), "exit 98\n");
        lab.Environment["PATH"] = first + Path.PathSeparator + second;
        lab.Environment["PATHEXT"] = ".PS1;.CMD;.EXE;.BAT;.COM";
        var context = new BackendContext(Path.Combine(lab.Tree.Root, "profiles"), configured ? script : null, new Dictionary<string, string>());
        var expected = $"codex resolves only to a PowerShell script ({script}); set backends.codex.executable to its .cmd or .exe";

        foreach (var version in new[] { true, false })
        {
            var commandError = await Assert.ThrowsAsync<HarnessException>(() => CodexProcess.CommandAsync(
                context, lab.Tree.Repository, lab.Environment, version, cancellationToken));
            await Assert.That(commandError!.Message).IsEqualTo(expected);
        }
        var startError = await Assert.ThrowsAsync<HarnessException>(async () =>
        {
            await using var process = CodexProcess.Start(context, lab.Tree.Repository, lab.Environment);
        });
        await Assert.That(startError!.Message).IsEqualTo(expected);
    }

    [Test]
    [Arguments("crash", "outcome:lost")]
    [Arguments("no-final", "no-final-text")]
    [Timeout(180_000)]
    public async Task Missing_final_text_or_an_app_server_crash_settles_failed(string scenario, string failure, CancellationToken cancellationToken)
    {
        using var lab = new CodexLab();
        var repo = await LabRepo.CreateAsync(lab.Tree, cancellationToken);
        lab.CodeTask();
        lab.Write("task", CodexLab.Handshake + CodexLab.Thread + (scenario == "crash" ? """
            {"stderr":"synthetic crash detail\n"}
            {"exit":17}
            """ : """
            {"notify":"turn/completed","params":{"threadId":"$threadId","turn":{"id":"$turnId","status":"completed"}}}
            """));
        using var harness = new HarnessProcess(lab.Tree);
        var result = await harness.RunAsync(["run", lab.Tasks, "--repo", repo.Root], cancellationToken,
            lab.Environment.ToDictionary(x => x.Key, x => (string?)x.Value));
        await Assert.That(result.ExitCode).IsEqualTo(1);
        await Assert.That(lab.Result["state"]!.GetValue<string>()).IsEqualTo("failed");
        await Assert.That(lab.Result["failures"]!.AsArray().Select(x => x!.GetValue<string>())).Contains(failure);
        if (scenario == "crash") await Assert.That(lab.Result["executionError"]!.GetValue<string>()).Contains("synthetic crash detail");
    }

    [Test]
    [Timeout(180_000)]
    public async Task Resolved_approval_closes_the_blocker_without_sending_a_late_response(CancellationToken cancellationToken)
    {
        using var lab = new CodexLab();
        lab.Write("preflight", CodexLab.Handshake + CodexLab.Thread + """
            {"request":"item/commandExecution/requestApproval","defer":true,"params":{"threadId":"$threadId","turnId":"$turnId","itemId":"call","command":"outside-command"}}
            {"expect":"turn/interrupt","reply":{}}
            {"notify":"serverRequest/resolved","params":{"threadId":"$threadId","requestId":"server-1"}}
            {"expect":"turn/interrupt","reply":{}}
            {"notify":"turn/completed","params":{"threadId":"$threadId","turn":{"id":"$turnId","status":"interrupted"}}}
            """);
        await using var backend = lab.Backend();
        var created = await backend.CreateAsync(new("run", "edit", "code", new("codex", "model-y", "high"), null, null,
            new("full", "on", "workspace", "on", "worktree"), lab.Tree.Repository, new([], []), lab.Environment, null, "full", null), cancellationToken);
        var worker = await backend.PromptAsync(created.Worker, new("Perform the task.", false), cancellationToken);
        WorkerSignal.Ask? ask = null;
        while (ask is null) ask = await backend.Signals.ReadAsync(cancellationToken) as WorkerSignal.Ask;
        await backend.InterruptAsync(worker, InterruptKind.Cancel, cancellationToken);
        WorkerSignal.AskClosed? closed = null;
        while (closed is null) closed = await backend.Signals.ReadAsync(cancellationToken) as WorkerSignal.AskClosed;
        await Assert.That(closed.AskId).IsEqualTo(ask.BlockerRef.AskId);
        await Assert.That((await backend.AnswerAsync(ask.BlockerRef, new BlockerAnswer.Once(), cancellationToken)).Confirmed).IsFalse();
        await backend.InterruptAsync(worker, InterruptKind.Cancel, cancellationToken);
        await Assert.That((await backend.CollectAsync(worker, cancellationToken)).Outcome).IsEqualTo("interrupted");
        await Assert.That(lab.Input().Where(x => x.ContainsKey("result") || x.ContainsKey("error"))).IsEmpty();
    }

    [Test]
    [Timeout(180_000)]
    public async Task Validate_refuses_a_kind_the_backend_does_not_serve(CancellationToken cancellationToken)
    {
        using var lab = new CodexLab();
        await using var backend = lab.Backend();
        var checks = await backend.ValidateAsync([new("edit", "unsupported", new("codex", "model-y", "high"), null, null,
            new("full", "on", "workspace", "on", "worktree"), lab.Tree.Repository)], cancellationToken);
        await Assert.That(checks.Single().Errors).Contains(x => x.Contains("unsupported", StringComparison.Ordinal));
        await Assert.That(lab.Input().Where(x => x["method"]?.GetValue<string>() is "thread/start" or "turn/start")).IsEmpty();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    [Timeout(180_000)]
    public async Task Resume_of_an_interrupted_Codex_task_settles_lost_without_a_new_prompt(bool proceed, CancellationToken cancellationToken)
    {
        using var lab = new CodexLab();
        var repo = await LabRepo.CreateAsync(lab.Tree, cancellationToken);
        lab.CodeTask();
        lab.Write("task", CodexLab.Handshake + CodexLab.Thread + """
            {"notify":"turn/completed","params":{"threadId":"$threadId","turn":{"id":"$turnId","status":"interrupted"}}}
            """);
        using var first = new HarnessProcess(lab.Tree);
        var initial = await first.RunAsync(["run", lab.Tasks, "--repo", repo.Root], cancellationToken,
            lab.Environment.ToDictionary(x => x.Key, x => (string?)x.Value));
        await Assert.That(initial.ExitCode).IsEqualTo(1);
        var runId = Path.GetFileName(lab.RunFolder);
        using var resume = new HarnessProcess(lab.Tree);
        var resumed = await resume.RunAsync(proceed ? ["resume", runId, "--continue"] : ["resume", runId], cancellationToken,
            lab.Environment.ToDictionary(x => x.Key, x => (string?)x.Value));
        await Assert.That(resumed.ExitCode).IsEqualTo(1);
        await Assert.That(File.Exists(Path.Combine(lab.RunFolder, "edit", "result.json"))).IsTrue();
        await Assert.That(lab.Result["outcome"]!.GetValue<string>()).IsEqualTo("lost");
        await Assert.That(lab.Result["state"]!.GetValue<string>()).IsEqualTo("failed");
        await Assert.That(lab.Input().Count(x => x["method"]?.GetValue<string>() == "turn/start")).IsEqualTo(1);
    }

    [Test]
    [Timeout(180_000)]
    public async Task Resume_after_a_killed_supervisor_reclaims_the_temporary_Codex_home_and_settles_lost(CancellationToken cancellationToken)
    {
        using var lab = new CodexLab();
        var repo = await LabRepo.CreateAsync(lab.Tree, cancellationToken);
        File.WriteAllText(Path.Combine(lab.Environment["CODEX_HOME"], "auth.json"), "{\"synthetic\":true}");
        lab.CodeTask();
        var task = JsonNode.Parse(File.ReadAllText(lab.Tasks))!;
        task["tasks"]![0]!["profile"] = "clean";
        File.WriteAllText(lab.Tasks, task.ToJsonString());
        lab.Write("task", CodexLab.Handshake + CodexLab.Thread + "{\"hang\":true}\n");
        using var supervisor = new HarnessProcess(lab.Tree);
        var running = supervisor.RunAsync(["run", lab.Tasks, "--repo", repo.Root], cancellationToken,
            lab.Environment.ToDictionary(x => x.Key, x => (string?)x.Value));
        var ledgers = Path.Combine(lab.Tree.DispatchHome, "state", "ledgers");
        string? home = null;
        try
        {
            while (!running.IsCompleted && home is null)
            {
                var path = Directory.Exists(ledgers) ? Directory.GetDirectories(ledgers).Select(x => Path.Combine(x, "edit", "task.json")).SingleOrDefault(File.Exists) : null;
                JsonNode? record = null;
                // The supervisor replaces the record while this loop polls it; a locked read is "not yet".
                try { record = path is null ? null : JsonNode.Parse(File.ReadAllText(path)); }
                catch (IOException) { }
                if (record?["session"]?["turn"] is not null) home = record["backendState"]?["codexHome"]?.GetValue<string>();
                if (home is null) await Task.Delay(50, cancellationToken);
            }
        }
        finally
        {
            // A Windows job closes with its supervisor; on POSIX the worker outlives it, and resume must stop it from its record.
            if (OperatingSystem.IsWindows()) supervisor.Kill(); else supervisor.KillRoot();
            await running;
        }
        var worker = Directory.GetFiles(lab.Records, "start.json", SearchOption.AllDirectories).Select(x => JsonNode.Parse(File.ReadAllText(x))!)
            .Single(x => x["scenario"]!.GetValue<string>() == "task.jsonl")["pid"]!.GetValue<int>();
        try
        {
            // The killed supervisor never released the worker, so its temporary home is still there.
            await Assert.That(home).IsNotNull();
            await Assert.That(Path.GetFileName(home!)).IsEqualTo("codex-edit");
            await Assert.That(Directory.Exists(home)).IsTrue();
            await Assert.That(IsAlive(worker)).IsEqualTo(!OperatingSystem.IsWindows());
            using var status = new HarnessProcess(lab.Tree);
            var shown = await status.RunAsync(["status", Path.GetFileName(lab.RunFolder)], cancellationToken,
                lab.Environment.ToDictionary(x => x.Key, x => (string?)x.Value));
            await Assert.That(shown.Stdout).Contains("(no supervisor: `resume");
            using var resume = new HarnessProcess(lab.Tree);
            var resumed = await resume.RunAsync(["resume", Path.GetFileName(lab.RunFolder)], cancellationToken,
                lab.Environment.ToDictionary(x => x.Key, x => (string?)x.Value));
            await Assert.That(resumed.ExitCode).IsEqualTo(1);
            await Assert.That(resumed.Stdout).DoesNotContain("ownership");
            await Assert.That(lab.Result["outcome"]!.GetValue<string>()).IsEqualTo("lost");
            await Assert.That(lab.Result["state"]!.GetValue<string>()).IsEqualTo("failed");
            await Assert.That(await ExitsWithinAsync(worker)).IsTrue();
            await Assert.That(Directory.Exists(home)).IsFalse();
        }
        finally
        {
            if (IsAlive(worker)) { using var leftover = Process.GetProcessById(worker); leftover.Kill(entireProcessTree: true); }
        }
    }

    [Test]
    [Timeout(180_000)]
    public async Task A_fresh_backend_hard_stops_a_recorded_worker_and_declines_a_soft_interrupt(CancellationToken cancellationToken)
    {
        using var lab = new CodexLab();
        lab.Write("preflight", CodexLab.Handshake + CodexLab.Thread + "{\"hang\":true}\n");
        await using var first = lab.Backend();
        var created = await first.CreateAsync(new("run", "edit", "code", new("codex", "model-y", "high"), null, null,
            new("full", "on", "workspace", "on", "worktree"), lab.Tree.Repository, new([], []), lab.Environment, null, "full", null), cancellationToken);
        var worker = await first.PromptAsync(created.Worker, new("Perform the task.", false), cancellationToken);
        // What a later process holds: the state after the ledger's round trip.
        var recorded = worker with
        {
            State = JsonSerializer.SerializeToNode(worker.State.Deserialize(LedgerJson.Files.BackendState), LedgerJson.Files.BackendState)!.AsObject(),
        };
        var pid = recorded.State["pid"]!.GetValue<int>();
        await using var second = lab.Backend();
        var soft = await second.InterruptAsync(recorded, InterruptKind.Cancel, cancellationToken);
        await Assert.That(soft.Stopped).IsFalse();
        await Assert.That(IsAlive(pid)).IsTrue();
        var hard = await second.InterruptAsync(recorded, InterruptKind.Hard, cancellationToken);
        await Assert.That(hard.Stopped).IsTrue();
        await Assert.That(await ExitsWithinAsync(pid)).IsTrue();
    }

    static bool IsAlive(int pid)
    {
        // A killed child stays a zombie until its parent reaps it; it no longer runs.
        if (OperatingSystem.IsLinux() && File.Exists($"/proc/{pid}/stat")
            && File.ReadAllText($"/proc/{pid}/stat") is var stat && stat[(stat.LastIndexOf(')') + 2)..].StartsWith('Z'))
            return false;
        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException) { return false; }
    }

    // A stop confirms the signal; the kernel and the parent's reaping finish the exit shortly after.
    static async Task<bool> ExitsWithinAsync(int pid)
    {
        for (var waited = 0; waited < 10_000 && IsAlive(pid); waited += 100) await Task.Delay(100);
        return !IsAlive(pid);
    }

    [Test]
    [Arguments("routes")]
    [Arguments("selfcheck")]
    [Timeout(180_000)]
    public async Task Catalog_commands_cover_the_selected_Codex_backend_without_prompting(string command, CancellationToken cancellationToken)
    {
        using var lab = new CodexLab();
        if (command == "selfcheck") lab.Write("task", CodexLab.Handshake + CodexLab.Thread.Split('\n')[0] + "\n");
        var repo = await LabRepo.CreateAsync(lab.Tree, cancellationToken);
        using var harness = new HarnessProcess(lab.Tree);
        var result = await harness.RunAsync([command, "--backend", "codex", "--repo", repo.Root], cancellationToken,
            lab.Environment.ToDictionary(x => x.Key, x => (string?)x.Value));
        result.RequireSuccess("Codex catalog command");
        await Assert.That(result.Stdout).Contains(command == "routes" ? "codex:model-y" : "Codex 0.163.0");
        // Selfcheck's launch check starts one thread and never a turn; routes starts neither.
        await Assert.That(lab.Input().Where(x => x["method"]?.GetValue<string>() is "thread/start" or "turn/start")
            .Select(x => x["method"]!.GetValue<string>()).ToArray()).IsEquivalentTo(command == "selfcheck" ? new[] { "thread/start" } : []);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    [Timeout(180_000)]
    public async Task Clean_profile_is_probed_and_its_actual_mode_is_frozen(bool wrongHome, CancellationToken cancellationToken)
    {
        using var lab = new CodexLab();
        var repo = await LabRepo.CreateAsync(lab.Tree, cancellationToken);
        File.WriteAllText(Path.Combine(lab.Environment["CODEX_HOME"], "auth.json"), "{\"synthetic\":true}");
        lab.CodeTask();
        var task = JsonNode.Parse(File.ReadAllText(lab.Tasks))!;
        task["tasks"]![0]!["profile"] = "clean";
        File.WriteAllText(lab.Tasks, task.ToJsonString());
        if (wrongHome)
        {
            var path = Path.Combine(lab.Scenarios, "probe.jsonl");
            File.WriteAllText(path, File.ReadAllText(path).Replace("\"codexHome\":\"$codexHome\"", "\"codexHome\":\"$cwd\""));
        }
        lab.Write("task", CodexLab.Handshake + CodexLab.Thread + CodexLab.Final);
        using var harness = new HarnessProcess(lab.Tree);
        (await harness.RunAsync(["run", lab.Tasks, "--repo", repo.Root], cancellationToken,
            lab.Environment.ToDictionary(x => x.Key, x => (string?)x.Value))).RequireSuccess("clean code run");
        var starts = Directory.GetFiles(lab.Records, "start.json", SearchOption.AllDirectories).Select(x => JsonNode.Parse(File.ReadAllText(x))!).ToArray();
        await Assert.That(starts.Count(x => x["scenario"]!.GetValue<string>() == "probe.jsonl")).IsEqualTo(1);
        var frozen = JsonNode.Parse(File.ReadAllText(Path.Combine(lab.RunFolder, "config.json")))!["tasks"]!["edit"]!["settings"]!["profile"]!;
        await Assert.That(frozen["actual"]!.GetValue<string>()).IsEqualTo(wrongHome ? "clean-partial" : "clean");
        var record = JsonNode.Parse(File.ReadAllText(Path.Combine(lab.RunFolder, "edit", "task.json")))!;
        await Assert.That(record["settings"]!["profile"]!["actual"]!.GetValue<string>()).IsEqualTo(wrongHome ? "clean-partial" : "clean");
        if (wrongHome) await Assert.That(frozen["reason"]!.GetValue<string>()).Contains("marker");
        var workerHome = record["backendState"]!["codexHome"]!.GetValue<string>();
        if (wrongHome) await Assert.That(workerHome).IsEqualTo(lab.Environment["CODEX_HOME"]);
        else
        {
            await Assert.That(Path.GetFileName(workerHome)).IsEqualTo("codex-edit");
            await Assert.That(Directory.Exists(workerHome)).IsFalse();
        }
    }

    [Test]
    [Timeout(180_000)]
    public async Task Repeated_cumulative_usage_records_only_one_call(CancellationToken cancellationToken)
    {
        using var lab = new CodexLab();
        const string update = """
            {"notify":"thread/tokenUsage/updated","params":{"threadId":"$threadId","turnId":"$turnId","tokenUsage":{"total":{"totalTokens":24,"inputTokens":17,"cachedInputTokens":3,"outputTokens":7,"reasoningOutputTokens":2},"last":{"totalTokens":24,"inputTokens":17,"cachedInputTokens":3,"outputTokens":7,"reasoningOutputTokens":2}}}}
            """;
        lab.Write("preflight", CodexLab.Handshake + CodexLab.Thread + update + "\n" + update + "\n" + CodexLab.Final);
        await using var backend = lab.Backend();
        var created = await backend.CreateAsync(new("run", "edit", "code", new("codex", "model-y", "high"), null, null,
            new("full", "on", "workspace", "on", "worktree"), lab.Tree.Repository, new([], []), lab.Environment, null, "full", null), cancellationToken);
        var worker = await backend.PromptAsync(created.Worker, new("Perform the task.", false), cancellationToken);
        var report = await backend.CollectAsync(worker, cancellationToken);
        await Assert.That(report.Calls!.Select(x => (x.Input, x.CacheRead, x.Output, x.Reasoning)).ToArray())
            .IsEquivalentTo(new (double?, double?, double?, double?)[] { (17, 3, 7, 2) }, CollectionOrdering.Matching);
        await Assert.That(report.Usage.Input).IsEqualTo(17);
        await Assert.That(report.Usage.Output).IsEqualTo(7);
    }

    [Test]
    [Timeout(180_000)]
    public async Task Collect_reports_a_faulted_pumps_exception_without_throwing(CancellationToken cancellationToken)
    {
        using var lab = new CodexLab();
        lab.Write("preflight", CodexLab.Handshake + CodexLab.Thread + CodexLab.Final);
        await using var backend = lab.Backend();
        var created = await backend.CreateAsync(new("run", "edit", "code", new("codex", "model-y", "high"), null, null,
            new("full", "on", "workspace", "on", "worktree"), lab.Tree.Repository, new([], []), lab.Environment, null, "full", null), cancellationToken);
        var worker = await backend.PromptAsync(created.Worker, new("Perform the task.", false), cancellationToken);
        await backend.CollectAsync(worker, cancellationToken);
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var workers = (System.Collections.IEnumerable)typeof(CodexBackend).GetField("workers", flags)!.GetValue(backend)!;
        var entry = workers.Cast<object>().Single();
        var connection = entry.GetType().GetProperty("Value")!.GetValue(entry)!;
        var property = connection.GetType().GetProperty("Pump")!;
        var pump = (Task)property.GetValue(connection)!;
        await pump;
        property.SetValue(connection, Task.FromException(new InvalidOperationException("synthetic pump failure")));
        try
        {
            var report = await backend.CollectAsync(worker, cancellationToken);
            await Assert.That(report.ExecutionError).Contains("synthetic pump failure");
            await Assert.That(report.FinalText).IsEqualTo("Completed.");
            await Assert.That(report.Complete).IsTrue();
        }
        finally { property.SetValue(connection, pump); }
    }

    [Test]
    [Timeout(180_000)]
    public async Task Calls_preserve_last_breakdowns_in_notification_order(CancellationToken cancellationToken)
    {
        using var lab = new CodexLab();
        lab.Write("preflight", CodexLab.Handshake + CodexLab.Thread + """
            {"notify":"thread/tokenUsage/updated","params":{"threadId":"$threadId","turnId":"$turnId","tokenUsage":{"total":{"totalTokens":120,"inputTokens":100,"cachedInputTokens":30,"outputTokens":20,"reasoningOutputTokens":5,"cacheWriteInputTokens":8},"last":{"totalTokens":24,"inputTokens":17,"cachedInputTokens":3,"outputTokens":7,"reasoningOutputTokens":2,"cacheWriteInputTokens":8}}}}
            {"notify":"thread/tokenUsage/updated","params":{"threadId":"$otherThreadId","turnId":"child-turn","tokenUsage":{"total":{"totalTokens":80,"inputTokens":50,"cachedInputTokens":20,"outputTokens":30,"reasoningOutputTokens":10,"cacheWriteInputTokens":8},"last":{"totalTokens":16,"inputTokens":8,"cachedInputTokens":4,"outputTokens":8,"reasoningOutputTokens":1,"cacheWriteInputTokens":8}}}}
            {"notify":"thread/tokenUsage/updated","params":{"threadId":"$threadId","turnId":"old-turn","tokenUsage":{"total":{"totalTokens":1800,"inputTokens":900,"cachedInputTokens":90,"outputTokens":900,"reasoningOutputTokens":90,"cacheWriteInputTokens":8},"last":{"totalTokens":18,"inputTokens":9,"cachedInputTokens":5,"outputTokens":9,"reasoningOutputTokens":3,"cacheWriteInputTokens":8}}}}
            {"notify":"thread/tokenUsage/updated","params":{"threadId":"$threadId","turnId":"$turnId","tokenUsage":{"total":{"totalTokens":240,"inputTokens":200,"cachedInputTokens":60,"outputTokens":40,"reasoningOutputTokens":10,"cacheWriteInputTokens":8},"last":{"totalTokens":5,"inputTokens":2,"cachedInputTokens":0,"outputTokens":3,"reasoningOutputTokens":0,"cacheWriteInputTokens":8}}}}
            {"notify":"thread/tokenUsage/updated","params":{"threadId":"$otherThreadId","turnId":"child-turn","tokenUsage":{"total":{"totalTokens":95,"inputTokens":60,"cachedInputTokens":25,"outputTokens":35,"reasoningOutputTokens":12,"cacheWriteInputTokens":8},"last":{"totalTokens":11,"inputTokens":6,"cachedInputTokens":1,"outputTokens":5,"reasoningOutputTokens":0,"cacheWriteInputTokens":8}}}}
            {"notify":"thread/tokenUsage/updated","params":{"threadId":"$threadId","turnId":"$turnId","tokenUsage":{"total":{"totalTokens":360,"inputTokens":300,"cachedInputTokens":90,"outputTokens":60,"reasoningOutputTokens":15,"cacheWriteInputTokens":8},"last":{"totalTokens":20,"inputTokens":11,"cachedInputTokens":6,"outputTokens":9,"reasoningOutputTokens":4,"cacheWriteInputTokens":8}}}}

            """ + CodexLab.Final);
        await using var backend = lab.Backend();
        var created = await backend.CreateAsync(new("run", "edit", "code", new("codex", "model-y", "high"), null, null,
            new("full", "on", "workspace", "on", "worktree"), lab.Tree.Repository, new([], []), lab.Environment, null, "full", null), cancellationToken);
        var worker = await backend.PromptAsync(created.Worker, new("Perform the task.", false), cancellationToken);
        var report = await backend.CollectAsync(worker, cancellationToken);
        await Assert.That(report.Calls).IsNotNull();
        await Assert.That(report.Calls!.Select(x => (x.Input, x.CacheRead, x.Output, x.Reasoning)).ToArray())
            .IsEquivalentTo(new (double?, double?, double?, double?)[] { (17, 3, 7, 2), (2, 0, 3, 0), (11, 6, 9, 4) }, CollectionOrdering.Matching);
        await Assert.That(report.Usage.Input).IsEqualTo(360);
        await Assert.That(report.Usage.Output).IsEqualTo(95);
        await Assert.That(report.FinalText).IsEqualTo("Completed.");
    }

    [Test]
    [Timeout(180_000)]
    public async Task Notifications_keep_the_task_turn_final_and_sum_latest_usage_per_thread(CancellationToken cancellationToken)
    {
        using var lab = new CodexLab();
        lab.Write("preflight", CodexLab.Handshake + CodexLab.Thread + """
            {"notify":"turn/started","params":{"threadId":"$threadId","turn":{"id":"$turnId","status":"inProgress"}}}
            {"notify":"item/started","params":{"threadId":"$otherThreadId","turnId":"child-turn","item":{"id":"call","type":"commandExecution","command":"echo progress"},"startedAtMs":1000}}
            {"notify":"item/completed","params":{"threadId":"$otherThreadId","turnId":"child-turn","item":{"id":"call","type":"commandExecution","command":"echo progress","status":"completed","exitCode":0},"completedAtMs":2000}}
            {"notify":"error","params":{"threadId":"$threadId","turnId":"$turnId","willRetry":true,"error":{"message":"retrying"}}}
            {"notify":"thread/tokenUsage/updated","params":{"threadId":"$threadId","turnId":"$turnId","tokenUsage":{"total":{"inputTokens":10,"outputTokens":2,"reasoningOutputTokens":1,"cachedInputTokens":3,"cacheWriteInputTokens":4}}}}
            {"notify":"thread/tokenUsage/updated","params":{"threadId":"$threadId","turnId":"$turnId","tokenUsage":{"total":{"inputTokens":20,"outputTokens":4,"reasoningOutputTokens":2,"cachedInputTokens":6,"cacheWriteInputTokens":8}}}}
            {"notify":"thread/tokenUsage/updated","params":{"threadId":"$otherThreadId","turnId":"child-turn","tokenUsage":{"total":{"inputTokens":5,"outputTokens":3,"reasoningOutputTokens":1,"cachedInputTokens":2,"cacheWriteInputTokens":1}}}}
            {"notify":"model/rerouted","params":{"threadId":"$otherThreadId","turnId":"child-turn","toModel":"model-z","fromModel":"model-y","reason":"capacity"}}
            {"notify":"turn/completed","params":{"threadId":"$otherThreadId","turn":{"id":"child-turn","status":"failed"}}}
            {"notify":"turn/completed","params":{"threadId":"$threadId","turn":{"id":"old-turn","status":"failed"}}}
            {"notify":"item/completed","params":{"threadId":"$threadId","turnId":"$turnId","item":{"id":"answer","type":"agentMessage","phase":"final_answer","text":"Final answer."}}}
            {"notify":"item/completed","params":{"threadId":"$threadId","turnId":"$turnId","item":{"id":"comment","type":"agentMessage","phase":"commentary","text":"Later commentary."}}}
            {"notify":"item/completed","params":{"threadId":"$otherThreadId","turnId":"child-turn","item":{"id":"child-answer","type":"agentMessage","phase":"final_answer","text":"Child answer."}}}
            {"notify":"turn/completed","params":{"threadId":"$threadId","turn":{"id":"$turnId","status":"completed"}}}
            """);
        await using var backend = lab.Backend();
        var created = await backend.CreateAsync(new("run", "edit", "code", new("codex", "model-y", "high"), null, null,
            new("full", "on", "workspace", "on", "worktree"), lab.Tree.Repository, new([], []), lab.Environment, null, "full", null), cancellationToken);
        var worker = await backend.PromptAsync(created.Worker, new("Perform the task.", false), cancellationToken);
        var report = await backend.CollectAsync(worker, cancellationToken);
        await Assert.That(report.FinalText).IsEqualTo("Final answer.");
        await Assert.That(report.Usage.Input).IsEqualTo(25);
        await Assert.That(report.Usage.Output).IsEqualTo(7);
        await Assert.That(report.Usage.Reasoning).IsEqualTo(3);
        await Assert.That(report.Usage.CacheRead).IsEqualTo(8);
        await Assert.That(report.Usage.CacheWrite).IsEqualTo(9);
        await Assert.That(report.UsageComplete).IsTrue();
        await Assert.That(report.RoutesUsed).IsEquivalentTo(new[] { "codex:model-y#high", "codex:model-z#high" });
        var signals = new List<WorkerSignal>();
        while (backend.Signals.TryRead(out var signal)) signals.Add(signal);
        await Assert.That(signals.OfType<WorkerSignal.Ended>().Select(x => x.Outcome)).IsEquivalentTo(new[] { "succeeded" });
        await Assert.That(signals.OfType<WorkerSignal.Ended>().Single().Turn).IsEqualTo("turn-main");
        await Assert.That(signals.OfType<WorkerSignal.Retry>().Single().Message).IsEqualTo("retrying");
        await Assert.That(signals.OfType<WorkerSignal.ToolCall>().Select(x => x.Marker)).IsEquivalentTo(new[] { "start", "end" });
        await Assert.That(signals.OfType<WorkerSignal.ToolCall>().Single(x => x.Marker == "end").Outcome).IsEqualTo("succeeded");
        await Assert.That(signals.OfType<WorkerSignal.Status>().Select(x => x.Execution)).Contains("succeeded");
    }

    [Test]
    [Arguments("code", "workspace", "on", false)]
    [Arguments("mechanical", "workspace", "off", false)]
    [Arguments("review", "read-only", "on", false)]
    [Arguments("research", "read-only", "off", false)]
    [Arguments("structured", "read-only", "on", false)]
    [Arguments("structured", "read-only", "off", true)]
    [Arguments("code", "full", "on", false)]
    [Timeout(180_000)]
    public async Task Modes_forward_sandbox_network_schema_prompt_and_catalog_default_effort(string kind, string access, string network, bool contracts, CancellationToken cancellationToken)
    {
        using var lab = new CodexLab();
        var preflight = Path.Combine(lab.Scenarios, "preflight.jsonl");
        File.WriteAllText(preflight, File.ReadAllText(preflight).Replace("\"defaultReasoningEffort\":\"high\"", "\"defaultReasoningEffort\":\"low\""));
        var sandboxType = access switch { "read-only" => "readOnly", "workspace" => "workspaceWrite", _ => "dangerFullAccess" };
        var sandboxMode = access switch { "read-only" => "read-only", "workspace" => "workspace-write", _ => "danger-full-access" };
        lab.Write("task", CodexLab.Handshake + CodexLab.Thread.Replace("workspaceWrite", sandboxType) + CodexLab.Final);
        await using var backend = lab.Backend();
        var route = new RouteRef("codex", "model-y", null);
        var spec = new TaskSpec { Id = "edit", Kind = kind, Access = access == "full" ? "full" : null, Network = network,
            Input = new() { Items = JsonNode.Parse("[{\"id\":\"a\",\"text\":\"İyi günler 世界\"}]")!.AsArray() }, Result = new() { ItemsAt = "/items" } };
        var resolved = ConfigResolver.Resolve(new TaskFile { Tasks = [spec] }, new ConfigFile
        {
            Preferences = new() { Backend = "codex", DefaultRoutes = new() { ["codex"] = "model-y" } },
        }).Tasks["edit"].Settings;
        await Assert.That(resolved.Access!.From).IsEqualTo(access == "full" ? "task" : "tool");
        var modes = new Modes(resolved.Profile!.Value!, resolved.Project!.Value!, resolved.Access.Value!, resolved.Network!.Value!, resolved.Workspace!.Value!);
        var checks = await backend.ValidateAsync([new("edit", kind, route, null, null, modes, lab.Tree.Repository)], cancellationToken);
        await Assert.That(checks.Single().Errors).IsEmpty();
        var schema = JsonNode.Parse("{\"type\":\"object\",\"properties\":{\"items\":{\"type\":\"array\"}},\"required\":[\"items\"],\"additionalProperties\":false}")!.AsObject();
        var promptInputs = new Prompt.Inputs(contracts ? [new(new(), "First contract\r\n"), new(new(), "Second contract")] : [], spec.Input.Items, schema);
        var text = new Policy { Path = "config.json", Deny = [], Escalate = [], Instructions = [] }.Render(new(spec, "Perform the task."), lab.Tree.Repository, promptInputs);
        var created = await backend.CreateAsync(new("run", "edit", kind, route, null, null, modes, lab.Tree.Repository, new([], []), lab.Environment,
            kind == "structured" ? schema : null, checks.Single().Profile, checks.Single().ProfileReason), cancellationToken);
        var worker = await backend.PromptAsync(created.Worker, new(text, false), cancellationToken);
        var report = await backend.CollectAsync(worker, cancellationToken);
        await Assert.That(report.Outcome).IsEqualTo("succeeded");
        var turn = lab.Input().Single(x => x["method"]?.GetValue<string>() == "turn/start")["params"]!;
        await Assert.That(turn["effort"]!.GetValue<string>()).IsEqualTo("low");
        await Assert.That(turn["input"]!.AsArray().Count).IsEqualTo(1);
        await Assert.That(turn["input"]![0]!["text"]!.GetValue<string>()).IsEqualTo(text);
        await Assert.That(turn["cwd"]!.GetValue<string>()).IsEqualTo(lab.Tree.Repository);
        await Assert.That(JsonNode.DeepEquals(turn["outputSchema"], kind == "structured" ? schema : null)).IsTrue();
        await Assert.That(turn["sandboxPolicy"]!["type"]!.GetValue<string>()).IsEqualTo(sandboxType);
        if (access == "full") await Assert.That(turn["sandboxPolicy"]!.AsObject().ContainsKey("networkAccess")).IsFalse();
        else await Assert.That(turn["sandboxPolicy"]!["networkAccess"]!.GetValue<bool>()).IsEqualTo(network == "on");
        if (access == "workspace") await Assert.That(turn["sandboxPolicy"]!["writableRoots"]![0]!.GetValue<string>()).IsEqualTo(lab.Tree.Repository);
        var thread = lab.Input().Single(x => x["method"]?.GetValue<string>() == "thread/start")["params"]!;
        await Assert.That(thread["model"]!.GetValue<string>()).IsEqualTo("model-y");
        await Assert.That(thread["sandbox"]!.GetValue<string>()).IsEqualTo(sandboxMode);
        await Assert.That(thread["approvalPolicy"]!.GetValue<string>()).IsEqualTo("on-request");
        await Assert.That(thread["approvalsReviewer"]!.GetValue<string>()).IsEqualTo("user");
        await Assert.That(thread["ephemeral"]!.GetValue<bool>()).IsTrue();
        await Assert.That(thread.AsObject().ContainsKey("baseInstructions")).IsFalse();
        await Assert.That(thread.AsObject().ContainsKey("developerInstructions")).IsFalse();
        var trust = thread["config"]!["projects"]!.AsObject().Single().Value!["trust_level"]!.GetValue<string>();
        await Assert.That(trust).IsEqualTo(kind == "structured" ? "untrusted" : "trusted");
        await Assert.That(created.Applied.Access).IsEqualTo(access == "full" ? "none" : "sandbox");
        await Assert.That(created.Applied.Rules).IsEqualTo("approval-gate");
        await Assert.That(report.RoutesUsed).Contains("codex:model-y#low");
        if (contracts) await Assert.That(text).StartsWith("First contract\r\n\nSecond contract\n\n");
    }

    [Test]
    [Arguments("commandExecution", false)]
    [Arguments("fileChange", false)]
    [Arguments("commandExecution", true)]
    [Arguments("fileChange", true)]
    [Timeout(180_000)]
    public async Task Approvals_default_to_decline_and_steer_or_wait_for_operator_once(string tool, bool escalate, CancellationToken cancellationToken)
    {
        using var lab = new CodexLab();
        var repo = await LabRepo.CreateAsync(lab.Tree, cancellationToken);
        lab.CodeTask();
        if (escalate)
        {
            var tasks = JsonNode.Parse(File.ReadAllText(lab.Tasks))!;
            tasks["policy"] = JsonNode.Parse("{\"escalate\":[{\"name\":\"operator\",\"action\":\"*\",\"resources\":[\"*\"]}]}");
            File.WriteAllText(lab.Tasks, tasks.ToJsonString());
        }
        lab.Write("task", CodexLab.Handshake + CodexLab.Thread + """
            {"notify":"item/started","params":{"threadId":"$otherThreadId","turnId":"child-turn","item":{"id":"call","type":"fileChange","changes":[{"path":"$cwd/a.txt"}]}}}
            {"request":"item/TOOL/requestApproval","params":{"threadId":"$otherThreadId","turnId":"child-turn","itemId":"call","command":"outside-command","grantRoot":"$outside"},"result":{"decision":"DECISION"}}

            """.Replace("TOOL", tool).Replace("DECISION", escalate ? "accept" : "decline")
            + (escalate ? "" : """
                {"expect":"turn/steer","params":{"threadId":"$otherThreadId","expectedTurnId":"child-turn"},"reply":{}}

                """) + CodexLab.Final);
        using var harness = new HarnessProcess(lab.Tree);
        var running = harness.RunAsync(["run", lab.Tasks, "--repo", repo.Root], cancellationToken,
            lab.Environment.ToDictionary(x => x.Key, x => (string?)x.Value));
        if (escalate)
        {
            var asks = Array.Empty<string>();
            var controlRoot = Path.Combine(lab.Tree.DispatchHome, "state", "control");
            while (!running.IsCompleted && asks.Length == 0)
            {
                asks = Directory.Exists(controlRoot) ? Directory.GetDirectories(controlRoot)
                    .Where(path => Locations.IsControlId(Path.GetFileName(path)))
                    .Select(path => Path.Combine(path, "asks", "edit-1.json"))
                    .Where(File.Exists).ToArray() : [];
                if (asks.Length == 0) await Task.Delay(20, cancellationToken);
            }
            await Assert.That(asks.Length).IsEqualTo(1);
            var ask = JsonNode.Parse(File.ReadAllText(asks[0]))!;
            await Assert.That(lab.Input().Where(x => x.ContainsKey("result"))).IsEmpty();
            using var reply = new HarnessProcess(lab.Tree);
            var answered = await reply.RunAsync(["reply", ask["run"]!.GetValue<string>(), "edit-1", "once"], cancellationToken);
            answered.RequireSuccess("operator reply");
        }
        (await running).RequireSuccess("approval run");
        var answer = lab.Input().Single(x => x.ContainsKey("result"));
        await Assert.That(answer["result"]!["decision"]!.GetValue<string>()).IsEqualTo(escalate ? "accept" : "decline");
        await Assert.That(string.Join('\n', lab.Input().Select(x => x.ToJsonString()))).DoesNotContain("acceptForSession");
        if (!escalate)
            await Assert.That(lab.Input().Single(x => x["method"]?.GetValue<string>() == "turn/steer")["params"]!["input"]![0]!["text"]!.GetValue<string>())
                .IsEqualTo("The approval request was rejected. Rejection message:\n" + Policy.LeadDeclined);
    }

    [Test]
    [Arguments("commandExecution")]
    [Arguments("fileChange")]
    [Timeout(180_000)]
    public async Task Operator_rejection_steer_identifies_the_exact_feedback_and_task_continues(string tool, CancellationToken cancellationToken)
    {
        using var lab = new CodexLab();
        var repo = await LabRepo.CreateAsync(lab.Tree, cancellationToken);
        lab.CodeTask();
        var tasks = JsonNode.Parse(File.ReadAllText(lab.Tasks))!;
        tasks["policy"] = JsonNode.Parse("{\"escalate\":[{\"name\":\"operator\",\"action\":\"*\",\"resources\":[\"*\"]}]}");
        File.WriteAllText(lab.Tasks, tasks.ToJsonString());
        lab.Write("task", CodexLab.Handshake + CodexLab.Thread + """
            {"request":"item/TOOL/requestApproval","params":{"threadId":"$threadId","turnId":"$turnId","itemId":"call","command":"outside-command","grantRoot":"$outside"},"result":{"decision":"decline"}}
            {"expect":"turn/steer","params":{"threadId":"$threadId","expectedTurnId":"$turnId"},"reply":{}}
            {"writeFile":{"path":"$cwd/a.txt","base64":"Y29udGludWVkCg=="}}

            """.Replace("TOOL", tool) + CodexLab.Final);
        using var harness = new HarnessProcess(lab.Tree);
        var running = harness.RunAsync(["run", lab.Tasks, "--repo", repo.Root], cancellationToken,
            lab.Environment.ToDictionary(x => x.Key, x => (string?)x.Value));
        try
        {
            var controlRoot = Path.Combine(lab.Tree.DispatchHome, "state", "control");
            string? askPath = null;
            while (!running.IsCompleted && askPath is null)
            {
                askPath = Directory.Exists(controlRoot) ? Directory.GetDirectories(controlRoot)
                    .Where(path => Locations.IsControlId(Path.GetFileName(path)))
                    .Select(path => Path.Combine(path, "asks", "edit-1.json"))
                    .SingleOrDefault(File.Exists) : null;
                if (askPath is null) await Task.Delay(20, cancellationToken);
            }
            await Assert.That(askPath).IsNotNull();
            var ask = JsonNode.Parse(File.ReadAllText(askPath!))!;
            await Assert.That(lab.Input().Where(x => x.ContainsKey("result"))).IsEmpty();
            const string feedback = "Leave that action denied.\nContinue with the local report: İyi günler.";
            using var reply = new HarnessProcess(lab.Tree);
            (await reply.RunAsync(["reply", ask["run"]!.GetValue<string>(), "edit-1", "reject", "--message", feedback], cancellationToken))
                .RequireSuccess("operator rejection");
            (await running).RequireSuccess("continued rejection run");
            var wire = lab.Input();
            var decline = wire.Single(x => x.ContainsKey("result"));
            await Assert.That(decline["result"]!["decision"]!.GetValue<string>()).IsEqualTo("decline");
            var steer = wire.Single(x => x["method"]?.GetValue<string>() == "turn/steer");
            await Assert.That(steer["params"]!["input"]!.AsArray().Count).IsEqualTo(1);
            await Assert.That(steer["params"]!["input"]![0]!["text"]!.GetValue<string>())
                .IsEqualTo("The approval request was rejected. Rejection message:\n" + feedback);
            await Assert.That(Array.IndexOf(wire, steer)).IsGreaterThan(Array.IndexOf(wire, decline));
            await Assert.That(lab.Result["state"]!.GetValue<string>()).IsEqualTo("done");
            await Assert.That(await repo.GitAsync(["show", lab.Result["commit"]!.GetValue<string>() + ":a.txt"], cancellationToken)).IsEqualTo("continued");
        }
        finally { harness.Kill(); await running; }
    }

    [Test]
    [Arguments("version")]
    [Arguments("signed-out")]
    [Arguments("account")]
    [Timeout(180_000)]
    public async Task Preflight_refuses_before_claim_when_version_or_login_is_wrong(string failure, CancellationToken cancellationToken)
    {
        using var lab = new CodexLab();
        var repo = await LabRepo.CreateAsync(lab.Tree, cancellationToken);
        lab.CodeTask();
        lab.Write("task", CodexLab.Handshake + CodexLab.Thread + CodexLab.Final);
        if (failure == "version") File.WriteAllText(Path.Combine(lab.Scenarios, "version.txt"), "codex-cli 0.0.1\n");
        else if (failure == "signed-out") File.WriteAllText(Path.Combine(lab.Scenarios, "login-status.json"), "{\"exit\":1,\"stderr\":\"Signed out\"}");
        else
        {
            lab.Write("preflight", CodexLab.Handshake + "{\"expect\":\"account/read\",\"reply\":{\"account\":null,\"requiresOpenaiAuth\":true}}");
        }
        using var harness = new HarnessProcess(lab.Tree);
        var result = await harness.RunAsync(["run", lab.Tasks, "--repo", repo.Root], cancellationToken,
            lab.Environment.ToDictionary(x => x.Key, x => (string?)x.Value));
        await Assert.That(result.ExitCode).IsEqualTo(2);
        await Assert.That(result.Stderr).Contains(failure == "version" ? "0.163.0" : "codex login");
        await Assert.That(Directory.GetFiles(lab.Tree.DispatchHome, "run.json", SearchOption.AllDirectories)).IsEmpty();
        await Assert.That(lab.Input().Where(x => x["method"]?.GetValue<string>() == "turn/start")).IsEmpty();
    }

    [Test]
    [Timeout(180_000)]
    public async Task Run_code_task_commits_verifies_and_settles_done(CancellationToken cancellationToken)
    {
        using var lab = new CodexLab();
        var repo = await LabRepo.CreateAsync(lab.Tree, cancellationToken);
        lab.CodeTask();
        lab.Write("task", CodexLab.Handshake + CodexLab.Thread + """
            {"writeFile":{"path":"$cwd/a.txt","base64":"Y2hhbmdlZAo="}}

            """ + CodexLab.Final);
        using var harness = new HarnessProcess(lab.Tree);
        var result = await harness.RunAsync(["run", lab.Tasks, "--repo", repo.Root], cancellationToken,
            lab.Environment.ToDictionary(x => x.Key, x => (string?)x.Value), documentedEntry: true);
        result.RequireSuccess("Codex code run");
        await Assert.That(lab.Result["state"]!.GetValue<string>()).IsEqualTo("done");
        await Assert.That(lab.Result["backend"]!.GetValue<string>()).IsEqualTo("codex");
        await Assert.That(lab.Result["commit"]!.GetValue<string>()).IsNotEmpty();
        await Assert.That(lab.Result["verify"]![0]!["exitCode"]!.GetValue<int>()).IsEqualTo(0);
        var commit = lab.Result["commit"]!.GetValue<string>();
        await Assert.That(await repo.GitAsync(["show", $"{commit}:a.txt"], cancellationToken)).IsEqualTo("changed");
        await Assert.That(lab.Wire().Any(x => x["direction"]!.GetValue<string>() == "unexpected")).IsFalse();
    }
}
