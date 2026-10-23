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
            var unknown = unobservable || !OperatingSystem.IsWindows();
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
            await Assert.That(errors.Length).IsEqualTo(unknown ? 1 : 0);
            if (unknown)
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
