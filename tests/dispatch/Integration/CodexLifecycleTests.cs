using System.Diagnostics;
using System.Text.Json.Nodes;
using Dispatch.Tests.Support;
using Microsoft.Extensions.Time.Testing;

namespace Dispatch.Tests.Integration;

[Category("Integration")]
[NotInParallel]
public sealed class CodexLifecycleTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    [Timeout(180_000)]
    public async Task Run_abort_with_a_surviving_mid_turn_process_has_bounded_cleanup(bool cleanupFails, CancellationToken ct)
    {
        using var core = new CoreLab("abort-cleanup", recorded: false);
        using var codex = new CodexLab(core.Tree);
        await LabRepo.CreateAsync(core.Tree, ct);
        File.WriteAllText(Path.Combine(codex.Environment["CODEX_HOME"], "auth.json"), "{}");
        codex.CodeTask();
        ConfigLayers.Initialize(Args.Parse([]));
        codex.Write("task-edit", CodexLab.Handshake + CodexLab.Thread + "{\"hang\":true}");
        var backend = codex.Backend();
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var serial = new ConcurrentExclusiveSchedulerPair(TaskScheduler.Default, 1).ExclusiveScheduler;
        var ledger = new Ledger(Commands.CreateRunId("codex-run"));
        var command = Task.Factory.StartNew(() => Commands.RunAsync(Args.Parse(["run", codex.Tasks, "--repo", core.Tree.Repository]), cancel.Token,
            backendFactory: () => backend, userPathReader: () => null), ct, TaskCreationOptions.None, serial).Unwrap();
        CodexProcess? process = null;
        string? home = null;
        bool bounded;
        bool homeRetained;
        bool workspaceRetained;
        bool survived;
        int exit;
        var output = TestContext.Current!.Output;
        var start = output.GetErrorOutput().Length;
        try
        {
            await UntilAsync(() => command.IsCompleted || ledger.Read().Any(line => line.Type == "prompt"), ct);
            process = backend.WorkerProcesses.Single(item => item.Worker.Turn is not null).Process;
            home = backend.WorkerProcesses.Single(item => item.Worker.Turn is not null).Worker.State["codexHome"]!.GetValue<string>();
            if (cleanupFails) Directory.CreateDirectory(Path.Combine(ledger.TaskDir("edit"), "final.md"));
            var probe = Directory.GetFiles(codex.Records, "start.json", SearchOption.AllDirectories)
                .Select(path => JsonNode.Parse(File.ReadAllText(path))!)
                .Single(item => item["scenario"]!.GetValue<string>() == "preflight.jsonl")["pid"]!.GetValue<int>();
            using (var holder = Process.GetProcessById(probe))
            {
                holder.Kill();
                await holder.WaitForExitAsync(ct);
            }
            var stop = new TaskCompletionSource<InterruptResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            process.SetPendingStop(stop.Task);
            await Task.Factory.StartNew(cancel.Cancel, ct, TaskCreationOptions.None, serial);
            await UntilAsync(() => ledger.Read().Any(line => line.Type == "aborted"), ct);
            await Task.Factory.StartNew(() => { }, ct, TaskCreationOptions.None, serial);
            stop.SetResult(new(false, "mid-turn process survives"));
            bounded = await Task.WhenAny(command, Task.Delay(TimeSpan.FromSeconds(15), ct)) == command;
            homeRetained = Directory.Exists(home);
            workspaceRetained = Directory.Exists(ledger.LoadRecord("edit").Workspace.Path);
            survived = IsAlive(process.Id);
        }
        finally
        {
            // Release the producer even on the base: failure is the bound assertion, never the test timeout.
            if (process is not null && IsAlive(process.Id))
            {
                using var holder = Process.GetProcessById(process.Id);
                holder.Kill(entireProcessTree: true);
                await holder.WaitForExitAsync(ct);
            }
            exit = await command;
            await backend.DisposeAsync();
        }
        await Assert.That(bounded).IsTrue();
        await Assert.That(homeRetained).IsTrue();
        await Assert.That(workspaceRetained).IsTrue();
        await Assert.That(survived).IsTrue();
        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(ledger.Read().Where(line => line.Type == "aborted").Select(line => line.Type)).IsNotEmpty();
        if (cleanupFails)
        {
            var error = output.GetErrorOutput()[start..];
            await Assert.That(error).Contains("AggregateException");
            await Assert.That(error.ToLowerInvariant()).Contains("canceled");
            await Assert.That(error).Contains("final.md");
        }
        else
        {
            await Assert.That(ledger.Read().Count(line => line.Task == "edit" && line.Type == "final")).IsEqualTo(1);
            await Assert.That(ledger.LoadResult("edit")!.Failures).IsEquivalentTo(new[] { "zombie", "outcome:lost", "no-final-text" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        }
    }

    [Test]
    [Timeout(180_000)]
    public async Task Scope_cleanup_settles_uncertainty_while_the_command_owns_its_ledger(CancellationToken ct)
    {
        using var core = new CoreLab();
        using var codex = new CodexLab(core.Tree);
        var run = await core.TaskAsync("edit", ct);
        run.Record.Settings.Backend = new() { Value = "codex" };
        codex.Write("preflight", CodexLab.Handshake + CodexLab.Thread.Split("{\"expect\":\"turn/start\"")[0]);
        await using var scope = new BackendScope([]);
        var backend = codex.Backend();
        ((IDictionary<string, IWorkerBackend>)scope.Backends).Add("codex", backend);
        var worker = (await backend.CreateAsync(new(core.Ledger.RunId, run.Id, "code", new("codex", "model-y", "high"), null, null,
            new("full", "on", "workspace", "on", "worktree"), core.Tree.Repository, new([], []), codex.Environment, null, "full", null), ct)).Worker;
        var process = backend.WorkerProcesses.Single().Process;
        var stop = new TaskCompletionSource<InterruptResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        process.SetPendingStop(stop.Task);
        var cleaning = scope.CleanupAsync();
        stop.SetResult(new(false, $"pid={process.Id} survives"));
        var cleanup = await cleaning;
        var consume = typeof(Commands).GetMethod("SettleCleanupAsync", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        await (Task)consume.Invoke(null, [core.Ledger, new List<TaskRun> { run }, scope.Backends, cleanup])!;
        var result = core.Ledger.LoadResult(run.Id);
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.State).IsEqualTo("failed");
        await Assert.That(result.Failures).IsEquivalentTo(new[] { "zombie", "outcome:lost", "no-final-text", "route-drift" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(result.WorkerStopped).IsFalse();
        await Assert.That(core.Ledger.LoadRecord(run.Id).Session.Id).IsEqualTo(worker.Id);
        await Assert.That(core.Ledger.Read().Select(x => x.Type)).IsEquivalentTo(new[] { "stop-unconfirmed", "outcome", "final" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(core.Ledger.Read().Single(x => x.Type == "stop-unconfirmed").Data!["detail"]!.GetValue<string>()).IsEqualTo($"pid={process.Id} survives");
        await Assert.That(Directory.Exists(scope.OwnedFolder)).IsTrue();
        result.WorkerStopped = true; // An old ledger can carry the late-hard-stop inconsistency.
        core.Ledger.SaveResult(result);
        await (Task)consume.Invoke(null, [core.Ledger, new List<TaskRun> { run }, scope.Backends, cleanup])!;
        await Assert.That(core.Ledger.Read().Count(line => line.Type == "final")).IsEqualTo(1);
        await scope.DisposeAsync();
        await Assert.That(Directory.Exists(scope.OwnedFolder)).IsFalse();
        await (Task)consume.Invoke(null, [core.Ledger, new List<TaskRun> { run }, scope.Backends, scope.CleanupResults])!;
        await Assert.That(TaskRun.FromLedger(core.Ledger.LoadRecord(run.Id), core.Ledger).Zombie).IsFalse();
        await Assert.That(core.Ledger.Read().Last().Data!["detail"]!.GetValue<string>()).Contains("exit=");
        await Assert.That(core.Ledger.Read().Count(line => line.Type == "final")).IsEqualTo(1);
        await Assert.That(await Worktree.CleanAsync(core.Ledger, run.Record, core.Tree.Repository, false, ct, core.Git)).IsTrue();
    }

    [Test]
    [Timeout(180_000)]
    public async Task Surviving_identity_fails_only_its_task(CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows()) Skip.Test("Windows snapshot identity seam");
        using var core = new CoreLab("surviving-identity");
        using var codex = new CodexLab(core.Tree);
        File.WriteAllText(Path.Combine(codex.Environment["CODEX_HOME"], "auth.json"), "{}");
        await using var scope = new BackendScope([]);
        var backend = codex.Backend();
        ((IDictionary<string, IWorkerBackend>)scope.Backends).Add("codex", backend);
        var bad = await core.TaskAsync("bad", ct);
        var good = await core.TaskAsync("good", ct);
        foreach (var run in new[] { bad, good })
        {
            run.Record.Spec.Kind = "code";
            run.Record.Spec.Base = "main";
            run.Record.Spec.Scope = ["a.txt"];
            run.Record.Spec.Route = "model-y#high";
            run.Record.Settings.Backend = new() { Value = "codex" };
            run.Record.Settings.Route = new() { Value = "codex:model-y#high" };
            run.Record.Settings.Access = new() { Value = "workspace" };
            run.Record.Settings.Profile = new() { Value = "clean" };
            run.Record.Workspace = new();
            await Worktree.PrepareAsync(run, core.Ledger, core.Tree.Repository, ct, core.Git);
            codex.Write("task-" + run.Id, (CodexLab.Handshake + CodexLab.Thread
                + "{\"expect\":\"turn/interrupt\",\"reply\":{}}\n"
                + "{\"notify\":\"thread/tokenUsage/updated\",\"params\":{\"threadId\":\"$threadId\",\"turnId\":\"$turnId\",\"tokenUsage\":{\"total\":{\"inputTokens\":17,\"outputTokens\":3}}}}\n"
                + CodexLab.Final).Replace("$threadId", "thread-" + run.Id));
            await new TaskStarter(backend, core.Ledger, core.Tree.Repository, AllowList.From(null), core.Policy, core.Git).StartAsync(run, ct);
        }
        var process = backend.WorkerProcesses.Single(x => x.Worker.TaskId == bad.Id).Process;
        using var self = Process.GetCurrentProcess();
        var survivor = self.Id;
        var start = self.StartTime.ToUniversalTime();
        var snapshot = process.ReadSnapshot;
        var limited = process.QueryLimitedStartTime;
        var nativeOpen = process.NativeOpenProcess;
        var nativeAttempts = new List<int>();
        process.NativeOpenProcess = (_, _, pid) =>
        {
            nativeAttempts.Add(pid);
            throw new System.ComponentModel.Win32Exception(5, "native open denied");
        };
        process.ReadSnapshot = _ => new() { [survivor] = (process.Id, start) };
        process.OpenProcess = pid => pid == survivor ? throw new System.ComponentModel.Win32Exception(5) : Process.GetProcessById(pid);
        process.QueryLimitedStartTime = pid => pid == survivor ? start : limited(pid);
        process.RefreshDescendants();
        File.WriteAllBytes(Path.Combine(bad.Record.Workspace.Path!, "a.txt"), [0, 255, 10, 42]);
        var marker = Path.Combine(core.Tree.Root, "verify-ran");
        bad.Record.Spec.Verify = [OperatingSystem.IsWindows() ? $"Set-Content -LiteralPath '{marker}' -Value ran" : $"touch '{marker}'"];
        var before = await core.Git.CheckAsync(bad.Record.Workspace.Path!, ct, "rev-parse", "HEAD");
        try
        {
            foreach (var run in new[] { bad, good }) await backend.InterruptAsync(run.SavedWorker(), InterruptKind.Cancel, ct);
            var scheduler = new Scheduler(scope.Backends, core.Ledger, core.Tree.Repository, AllowList.From(null), core.Policy, 2, ct, core.Git);
            await Assert.That(await scheduler.RunAsync([bad, good])).IsEqualTo(1);
            Exception? cleanup = null;
            try { await scope.DisposeAsync(); }
            catch (Exception exception) { cleanup = exception; }
            await Assert.That(cleanup).IsNull();
            var result = core.Ledger.LoadResult(bad.Id)!;
            await Assert.That(result.Failures).IsEquivalentTo(new[] { "zombie" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
            await Assert.That(result.WorkerStopped).IsFalse();
            await Assert.That(result.Commit).IsNull();
            await Assert.That(result.Tokens!.Input).IsEqualTo(17);
            await Assert.That(result.Tokens.Output).IsEqualTo(3);
            await Assert.That(File.ReadAllText(Path.Combine(core.Ledger.TaskDir(bad.Id), "final.md"))).Contains("Completed.");
            await Assert.That(result.Verify).IsEmpty();
            await Assert.That(File.ReadAllBytes(Path.Combine(bad.Record.Workspace.Path!, "a.txt"))).IsEquivalentTo(new byte[] { 0, 255, 10, 42 }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
            await Assert.That(await core.Git.CheckAsync(bad.Record.Workspace.Path!, ct, "rev-parse", "HEAD")).IsEqualTo(before);
            await Assert.That(File.Exists(marker)).IsFalse();
            await Assert.That(core.Ledger.LoadResult(good.Id)!.Failures).IsEmpty();
            await Assert.That(core.Ledger.LoadResult(good.Id)!.State).IsEqualTo("done");
            await Assert.That(core.Ledger.Read().Where(x => x.Type == "aborted")).IsEmpty();
            await Assert.That(nativeAttempts).Contains(survivor);
            foreach (var pid in nativeAttempts) await Assert.That(pid).IsEqualTo(survivor);
            await Assert.That(self.HasExited).IsFalse();
            await Assert.That(self.StartTime.ToUniversalTime()).IsEqualTo(start);
        }
        finally
        {
            // The synthetic ancestry ends here, not the test runner. Keep native termination denied
            // until a denied-handle/absent-snapshot pass releases the recorded identity safely.
            process.OpenProcess = pid => pid == survivor ? throw new System.ComponentModel.Win32Exception(5) : Process.GetProcessById(pid);
            process.QueryLimitedStartTime = pid => pid == survivor ? throw new System.ComponentModel.Win32Exception(5) : limited(pid);
            process.ReadSnapshot = _ => new();
            await scope.DisposeAsync();
            process.OpenProcess = Process.GetProcessById;
            process.QueryLimitedStartTime = limited;
            process.ReadSnapshot = snapshot;
            process.NativeOpenProcess = nativeOpen;
        }
    }

    [Test]
    [Timeout(60_000)]
    public async Task Linux_backend_persists_kernel_identity_for_root_and_descendants(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsLinux()) Skip.Test("Linux kernel process identities only");
        using var core = new CoreLab();
        using var codex = new CodexLab(core.Tree);
        var run = await core.TaskAsync("inspect", cancellationToken);
        run.Record.Spec.Route = "model-y#high";
        run.Record.Settings.Backend = new() { Value = "codex" };
        run.Record.Settings.Route = new() { Value = "codex:model-y#high" };
        run.Record.Settings.Access = new() { Value = "workspace" };
        codex.Write("preflight", CodexLab.Handshake + CodexLab.Thread.Replace(
            "{\"expect\":\"turn/start\"", "{\"spawnChild\":true}\n{\"expect\":\"turn/start\"", StringComparison.Ordinal) + "{\"hang\":true}");
        await using var backend = codex.Backend();
        await new TaskStarter(backend, core.Ledger, core.Tree.Repository, AllowList.From(null), core.Policy, core.Git).StartAsync(run, cancellationToken);
        await backend.PollAsync([run.SavedWorker()], cancellationToken);
        var state = JsonNode.Parse(run.Worker!.State.ToJsonString())!;
        var records = new[] { state }.Concat(state["descendants"]!.AsArray().Select(node => node!)).ToArray();
        await Assert.That(records.Length).IsEqualTo(2);
        foreach (var record in records)
        {
            var observed = ObservedProcess.Capture(record["pid"]!.GetValue<int>());
            await Assert.That(record["linuxBootId"]?.GetValue<string>()).IsEqualTo(File.ReadAllText("/proc/sys/kernel/random/boot_id").Trim());
            await Assert.That(record["linuxStartTicks"]?.GetValue<long>()).IsEqualTo(observed.StartTime);
        }
        var recovered = await CodexProcess.StopRecordedAsync(state.AsObject(), cancellationToken);
        await Assert.That(recovered.Stopped).IsTrue();
        foreach (var record in records) await Assert.That(IsAlive(record["pid"]!.GetValue<int>())).IsFalse();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    [Timeout(180_000)]
    public async Task Unreadable_descendant_does_not_fail_polls_or_normal_settlement(bool unobservable, CancellationToken cancellationToken)
    {
        using var core = new CoreLab();
        using var codex = new CodexLab(core.Tree);
        var run = await core.TaskAsync("inspect", cancellationToken);
        run.Record.Spec.Route = "model-y#high";
        run.Record.Settings.Backend = new() { Value = "codex" };
        run.Record.Settings.Route = new() { Value = "codex:model-y#high" };
        run.Record.Settings.Access = new() { Value = "read-only" };
        codex.Write("preflight", CodexLab.Handshake + CodexLab.Thread.Replace("workspaceWrite", "readOnly").Replace(
            "{\"expect\":\"turn/start\"", "{\"spawnChild\":true}\n{\"expect\":\"turn/start\"") + """
            {"request":"item/commandExecution/requestApproval","params":{"threadId":"$threadId","turnId":"$turnId","itemId":"held","command":"inspect"},"result":{"decision":"decline"}}
            {"expect":"turn/steer","reply":{}}

            """ + CodexLab.Final);
        await using var backend = codex.Backend();
        await new TaskStarter(backend, core.Ledger, core.Tree.Repository, AllowList.From(null), core.Policy, core.Git).StartAsync(run, cancellationToken);
        var process = backend.WorkerProcesses.Single().Process;
        var child = Directory.GetFiles(codex.Records, "start.json", SearchOption.AllDirectories)
            .Select(path => JsonNode.Parse(File.ReadAllText(path))!["pid"]!.GetValue<int>()).Single(pid => pid != process.Id);
        var observed = ObservedProcess.Capture(child);
        process.OpenProcess = pid => pid == child ? throw new System.ComponentModel.Win32Exception(5) : Process.GetProcessById(pid);
        if (unobservable) process.QueryLimitedStartTime = _ => throw new System.ComponentModel.Win32Exception(5);
        try
        {
            for (var poll = 0; poll < 3; poll++) await backend.PollAsync([run.SavedWorker()], cancellationToken);
            await Assert.That(run.State).IsEqualTo(TaskState.Running);
            await Assert.That(run.Worker!.State["descendants"]!.AsArray().Select(node => node!["pid"]!.GetValue<int>())).Contains(child);
            var identity = run.Worker.State["descendants"]!.AsArray().Single(node => node!["pid"]!.GetValue<int>() == child)!;
            var unknown = !OperatingSystem.IsWindows();
            if (unknown)
            {
                await Assert.That(identity["startTime"]).IsNull();
                var stop = await CodexProcess.StopRecordedAsync(new JsonObject { ["descendants"] = new JsonArray(identity.DeepClone()) },
                    cancellationToken, process.OpenProcess, process.QueryLimitedStartTime);
                await Assert.That(stop.Stopped).IsFalse();
                await Assert.That(stop.Detail).Contains($"pid={child}");
                await Assert.That(stop.Detail).Contains("cannot safely terminate");
                await Assert.That(observed.IsAlive()).IsTrue();
            }
            else await Assert.That(DateTime.Parse(identity["startTime"]!.GetValue<string>(), System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind).Ticks).IsEqualTo(observed.StartTime);
            var scheduler = new Scheduler(new Dictionary<string, IWorkerBackend> { ["codex"] = backend }, core.Ledger,
                core.Tree.Repository, AllowList.From(null), core.Policy, 1, cancellationToken, core.Git);
            await Assert.That(await scheduler.RunAsync([run])).IsEqualTo(0);
            await Assert.That(core.Ledger.LoadResult(run.Id)!.State).IsEqualTo("done");
            await Assert.That(core.Ledger.Read().Where(item => item.Type == "aborted")).IsEmpty();
            var errors = core.Ledger.Read().Where(item => item.Type == "error").ToArray();
            await Assert.That(errors.Length).IsEqualTo(unobservable || unknown ? 1 : 0);
            if (unobservable || unknown)
            {
                await Assert.That(errors.Single().Task).IsEqualTo(run.Id);
                await Assert.That(errors.Single().Data!["while"]!.GetValue<string>()).IsEqualTo("descendant-unobservable");
                await Assert.That(errors.Single().Data!["message"]!.GetValue<string>()).Contains($"pid={child}");
            }
            await Assert.That(observed.IsAlive()).IsFalse();
        }
        finally
        {
            process.OpenProcess = Process.GetProcessById;
            await observed.CleanupAsync();
        }
    }

    // The localstack hang: a Codex turn completed, but a descendant the harness could not confirm dead
    // kept the stop unconfirmed, no Ended signal came, and the task stayed running until its timeout.
    [Test]
    [Timeout(180_000)]
    public async Task A_completed_turn_whose_stop_stays_unconfirmed_settles_as_a_zombie_without_waiting_for_the_timeout(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows()) Skip.Test("The descendant identity seams model the Windows start-time check.");
        using var core = new CoreLab();
        using var codex = new CodexLab(core.Tree);
        var run = await core.TaskAsync("inspect", cancellationToken);
        run.Record.Spec.Route = "model-y#high";
        run.Record.Settings.Backend = new() { Value = "codex" };
        run.Record.Settings.Route = new() { Value = "codex:model-y#high" };
        run.Record.Settings.Access = new() { Value = "read-only" };
        codex.Write("preflight", CodexLab.Handshake + CodexLab.Thread.Replace("workspaceWrite", "readOnly").Replace(
            "{\"expect\":\"turn/start\"", "{\"spawnChild\":true}\n{\"expect\":\"turn/start\"") + """
            {"request":"item/commandExecution/requestApproval","params":{"threadId":"$threadId","turnId":"$turnId","itemId":"held","command":"inspect"},"result":{"decision":"decline"}}
            {"expect":"turn/steer","reply":{}}

            """ + CodexLab.Final);
        await using var backend = codex.Backend();
        await new TaskStarter(backend, core.Ledger, core.Tree.Repository, AllowList.From(null), core.Policy, core.Git).StartAsync(run, cancellationToken);
        var process = backend.WorkerProcesses.Single().Process;
        var child = Directory.GetFiles(codex.Records, "start.json", SearchOption.AllDirectories)
            .Select(path => JsonNode.Parse(File.ReadAllText(path))!["pid"]!.GetValue<int>()).Single(pid => pid != process.Id);
        // As in the localstack run: opening the child is denied, while its limited start-time query still
        // matches the recorded identity, so the harness can neither confirm it dead nor safely kill it.
        process.RefreshDescendants();
        var recorded = process.Descendants.Single(identity => identity.Pid == child).StartTime;
        var readStart = process.QueryLimitedStartTime;
        process.QueryLimitedStartTime = pid => pid == child ? recorded ?? DateTime.UnixEpoch : readStart(pid);
        process.OpenProcess = pid => pid == child ? throw new System.ComponentModel.Win32Exception(5) : Process.GetProcessById(pid);
        try
        {
            var scheduler = new Scheduler(new Dictionary<string, IWorkerBackend> { ["codex"] = backend }, core.Ledger,
                core.Tree.Repository, AllowList.From(null), core.Policy, 1, cancellationToken, core.Git);
            var clock = Stopwatch.StartNew();
            await Assert.That(await scheduler.RunAsync([run])).IsEqualTo(1);
            await Assert.That(clock.Elapsed).IsLessThan(TimeSpan.FromSeconds(90));
            var result = core.Ledger.LoadResult(run.Id)!;
            await Assert.That(result.State).IsEqualTo("failed");
            await Assert.That(result.Failures).Contains("zombie");
            await Assert.That(result.Outcome).IsEqualTo("succeeded");
            await Assert.That(core.Ledger.Read().Count(item => item.Type == "stop-unconfirmed")).IsEqualTo(1);
            await Assert.That(File.ReadAllText(Path.Combine(core.Ledger.TaskDir(run.Id), "final.md"))).Contains("Completed.");
        }
        finally
        {
            process.OpenProcess = Process.GetProcessById;
            process.QueryLimitedStartTime = readStart;
        }
    }

    [Test]
    [Timeout(180_000)]
    public async Task Task_start_forwards_the_structured_schema_to_the_native_backend(CancellationToken cancellationToken)
    {
        using var core = new CoreLab();
        using var codex = new CodexLab(core.Tree);
        var run = await core.TaskAsync("edit", cancellationToken);
        run.Record.Spec.Kind = "structured";
        run.Record.Spec.Project = "on";
        run.Record.Spec.RawPrompt = false;
        run.Record.Spec.Route = "model-y#high";
        run.Record.Settings.Backend = new() { Value = "codex" };
        run.Record.Settings.Route = new() { Value = "codex:model-y#high" };
        run.Record.Settings.Access = new() { Value = "read-only" };
        var schema = JsonNode.Parse("{\"type\":\"object\",\"additionalProperties\":false,\"properties\":{},\"required\":[]}")!.AsObject();
        run.Record.PromptInputs = new([], new JsonArray(), schema);
        codex.Write("preflight", CodexLab.Handshake + CodexLab.Thread.Replace("workspaceWrite", "readOnly") + CodexLab.Final);
        await using var backend = codex.Backend();
        await new TaskStarter(backend, core.Ledger, core.Tree.Repository, AllowList.From(null), core.Policy, core.Git).StartAsync(run, cancellationToken);
        await backend.CollectAsync(run.SavedWorker(), cancellationToken);
        var input = codex.Input().Single(x => x["method"]?.GetValue<string>() == "turn/start")["params"]!;
        await Assert.That(JsonNode.DeepEquals(input["outputSchema"], schema)).IsTrue();
        var prompt = input["input"]![0]!["text"]!.GetValue<string>();
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(prompt))).ToLowerInvariant();
        await Assert.That(core.Ledger.Read().Single(x => x.Type == "prompt").Data!["sha256"]!.GetValue<string>()).IsEqualTo(hash);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    [Timeout(180_000)]
    public async Task Timeout_stops_the_task_and_wrapper_tree_before_settling(bool hardStop, CancellationToken cancellationToken)
    {
        using var core = new CoreLab();
        using var codex = new CodexLab(core.Tree);
        var run = await core.TaskAsync("edit", cancellationToken);
        run.Record.Spec.TimeoutMinutes = 1;
        run.Record.Settings.Backend = new() { Value = "codex" };
        run.Record.Settings.Route = new() { Value = "codex:model-y#high" };
        run.Record.Spec.Route = "model-y#high";
        run.Record.Settings.Access = new() { Value = "workspace" };
        codex.Write("preflight", CodexLab.Handshake + CodexLab.Thread + (hardStop ? """
            {"spawnChild":true,"setsid":true}
            {"notify":"item/started","params":{"threadId":"$threadId","turnId":"$turnId","item":{"id":"ready","type":"commandExecution","command":"wait"}}}
            {"sleep":30000}
            {"writeFile":{"path":"$cwd/late.txt","base64":"bGF0ZQ=="}}
            {"hang":true}
            """ : """
            {"expect":"turn/interrupt","params":{"threadId":"$threadId","turnId":"$turnId"},"reply":{}}
            {"notify":"turn/completed","params":{"threadId":"$threadId","turn":{"id":"$turnId","status":"interrupted"}}}
            """));
        await using var backend = codex.Backend();
        var time = new FakeTimeProvider();
        var scheduler = new Scheduler(new Dictionary<string, IWorkerBackend> { ["codex"] = backend }, core.Ledger, core.Tree.Repository,
            AllowList.From(null), core.Policy, 1, cancellationToken, core.Git, time);
        var running = scheduler.RunAsync([run]);
        try
        {
            await UntilAsync(() => run.State == TaskState.Running, cancellationToken);
            if (hardStop) await UntilAsync(() => Directory.GetFiles(codex.Records, "start.json", SearchOption.AllDirectories).Length == 2, cancellationToken);
            await backend.PollAsync([run.SavedWorker()], cancellationToken);
            await Assert.That(run.Worker!.State["descendants"] is JsonArray).IsTrue();
            var processes = Directory.GetFiles(codex.Records, "start.json", SearchOption.AllDirectories)
                .Select(x => JsonNode.Parse(File.ReadAllText(x))!["pid"]!.GetValue<int>()).ToArray();
            time.Advance(TimeSpan.FromSeconds(59));
            await Assert.That(core.Ledger.Read().Where(x => x.Type == "timeout")).IsEmpty();
            time.Advance(TimeSpan.FromSeconds(1));
            await DriveUntilAsync(() => core.Ledger.Read().Any(x => x.Type == "timeout"), time, cancellationToken);
            if (hardStop) time.Advance(TimeSpan.FromMinutes(2));
            await DriveUntilAsync(() => running.IsCompleted, time, cancellationToken);
            await Assert.That(await running).IsEqualTo(1);
            var result = core.Ledger.LoadResult("edit")!;
            await Assert.That(result.State).IsEqualTo("failed");
            await Assert.That(result.Outcome).IsEqualTo("timeout");
            await Assert.That(result.Failures).Contains("outcome:timeout");
            await Assert.That(File.Exists(Path.Combine(core.Tree.Repository, "late.txt"))).IsFalse();
            foreach (var pid in processes) await Assert.That(IsAlive(pid)).IsFalse();
            if (hardStop)
            {
                await Assert.That(result.WorkerStopped).IsTrue();
                await Assert.That(result.Failures).DoesNotContain("zombie");
                var stopped = core.Ledger.Read().Single(x => x.Type == "hard-stop");
                await Assert.That(stopped.Data!["kind"]!.GetValue<string>()).IsEqualTo("timeout");
                await Assert.That(stopped.Data["stopped"]!.GetValue<bool>()).IsTrue();
            }
        }
        finally
        {
            if (run.Worker is { } worker) await backend.InterruptAsync(worker, InterruptKind.Hard, CancellationToken.None);
            await DriveUntilAsync(() => running.IsCompleted, time, cancellationToken);
            await running;
        }
    }

    static bool IsAlive(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            if (process.HasExited) return false;
            if (!OperatingSystem.IsLinux()) return true;
            var stat = File.ReadAllText($"/proc/{pid}/stat");
            return stat[(stat.LastIndexOf(')') + 2)..].Split(' ')[0] != "Z";
        }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
    }

    static async Task UntilAsync(Func<bool> done, CancellationToken ct)
    {
        while (!done()) await Task.Delay(20, ct);
    }

    static async Task DriveUntilAsync(Func<bool> done, FakeTimeProvider time, CancellationToken ct)
    {
        while (!done())
        {
            time.Advance(TimeSpan.FromMilliseconds(20));
            await Task.Delay(20, ct);
        }
    }
}
