using System.Text.Json.Nodes;
using Dispatch.Tests.Support;

namespace Dispatch.Tests.Integration;

[Category("Integration")]
[NotInParallel]
public sealed class OpenCodeModesTests
{
    [Test]
    [Timeout(60_000)]
    public async Task Configured_environments_reach_process_and_session_without_changing_probes(CancellationToken ct)
    {
        await using var lab = new OpenCodeLab();
        var originalRecord = FakeOpenCode.Record(lab.Tree);
        var probeNames = Path.Combine(lab.Tree.Root, "probe-names.json");
        lab.SetEnvironment("FAKE_ENV_NAMES", probeNames);
        lab.Create("worker");
        lab.Steps.Single(x => x["expect"]!.GetValue<string>() == "PUT /api/session/worker/environment")["body"] =
            new JsonObject { ["variables"] = new JsonObject { ["TASK_ONLY"] = "session-value", ["SYNTHETIC_TOKEN"] = "synthetic-configured" } };
        foreach (var step in lab.Steps.Where(x => x["expect"]!.GetValue<string>() is "POST /api/session" or "PUT /api/session/worker/environment")) step["repeat"] = true;
        lab.Write();
        await using var backend = new OpenCodeBackend(context: new(Path.Combine(lab.Tree.Root, "configured-profiles"), ConfigLayers.User!.Backends!.Opencode!.Executable,
            new Dictionary<string, string> { ["FAKE_OPENCODE_RECORD"] = originalRecord, ["SYNTHETIC_TOKEN"] = "synthetic-passthrough", ["FAKE_ENV_NAMES"] = probeNames }));
        await backend.ProbeAsync(new(lab.Tree.Repository), ct);
        var names = JsonNode.Parse(File.ReadAllText(probeNames))!.AsArray().Select(x => x!.GetValue<string>()).ToArray();
        await Assert.That(names).DoesNotContain("TASK_ONLY");
        var workers = new List<WorkerRef>();
        foreach (var id in new[] { "z-first", "a-second", "z-again" })
        {
            var record = Path.Combine(lab.Tree.Root, id == "a-second" ? "a-record" : "z-record");
            var configured = new Dictionary<string, string>
            {
                ["FAKE_OPENCODE_RECORD"] = record, ["TASK_ONLY"] = "session-value", ["SYNTHETIC_TOKEN"] = "synthetic-configured",
                ["FAKE_ENV_NAMES"] = record + ".names.json",
            };
            var variables = WorkerEnvironment.Copy(Launch(lab, id).Environment);
            foreach (var (name, value) in configured) variables[name] = value;
            workers.Add((await backend.CreateAsync(Launch(lab, id, access: "full") with { Environment = variables, ConfiguredEnvironment = configured }, ct)).Worker);
            var hostFiles = Directory.GetFiles(lab.Tree.Root, Path.GetFileName(record) + ".*.host.json");
            await Assert.That(hostFiles.Length).IsEqualTo(1);
            var host = JsonNode.Parse(File.ReadAllText(hostFiles.Single()))!;
            await Assert.That(JsonNode.Parse(File.ReadAllText(record + ".names.json"))!.AsArray().Select(x => x!.GetValue<string>())).Contains("TASK_ONLY");
            var calls = File.ReadLines(record).Select(x => JsonNode.Parse(x)!).ToArray();
            var puts = calls.Where(x => x["method"]!.GetValue<string>() == "PUT").ToArray();
            await Assert.That(puts.Length).IsEqualTo(id == "z-again" ? 2 : 1);
            var put = puts.Last();
            await Assert.That(put["body"]!["variables"]!["TASK_ONLY"]!.GetValue<string>()).IsEqualTo("<redacted>");
            await Assert.That(calls.Where(x => x["unexpected"]!.GetValue<bool>())).IsEmpty();
            await Assert.That(put["pid"]!.GetValue<int>()).IsEqualTo(host["pid"]!.GetValue<int>());
        }
        await Assert.That(workers[0].State["host"]!.GetValue<string>()).IsNotEqualTo(workers[1].State["host"]!.GetValue<string>());
        await Assert.That(workers[2].State["host"]!.GetValue<string>()).IsEqualTo(workers[0].State["host"]!.GetValue<string>());
        await Assert.That(Hosts(backend).Count).IsEqualTo(3); // Configuration-free probe plus two environments.
        var probe = File.ReadLines(originalRecord).Select(x => JsonNode.Parse(x)!).ToArray();
        await Assert.That(probe.Select(x => x["path"]!.GetValue<string>()).Distinct()).IsEquivalentTo(new[] { "/api/info", "/api/event" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    [Timeout(60_000)]
    public async Task Configured_host_and_continuation_reuse_frozen_environment(CancellationToken ct)
    {
        await using var lab = new OpenCodeLab();
        lab.Create("worker");
        var put = lab.Steps.Single(x => x["expect"]!.GetValue<string>() == "PUT /api/session/worker/environment");
        put["repeat"] = true;
        put["body"] = new JsonObject { ["variables"] = new JsonObject { ["TASK_ONLY"] = "frozen-value" } };
        lab.Add("GET /api/session/worker", OpenCodeLab.Session("worker", "interrupted"));
        lab.Prompt("worker");
        lab.Write();
        var spec = new TaskSpec { Id = "worker", Kind = "research", Route = "fixture/model", Backend = "opencode", Access = "full",
            Environment = JsonSerializer.SerializeToElement(new { TASK_ONLY = "frozen-value" }) };
        var config = ConfigResolver.Resolve(new() { Tasks = [spec] });
        var record = new TaskRecord { Spec = spec, Settings = config.Tasks["worker"].Settings, Workspace = new() { Path = lab.Tree.Repository } };
        var ledger = new Ledger("run");
        new ConfigLayers { Effective = config }.Freeze(ledger.Dir);
        ledger.SaveRecord(record);
        var context = new BackendContext(Path.Combine(lab.Tree.Root, "frozen-profiles"), ConfigLayers.User!.Backends!.Opencode!.Executable, new Dictionary<string, string>());
        WorkerRef worker;
        await using (var first = new OpenCodeBackend(context: context, runId: "run"))
            worker = (await first.CreateAsync(TaskStarter.BuildLaunch(record, "run", "opencode", Policy.From(config), null).Launch, ct)).Worker;
        lab.SetEnvironment("TASK_ONLY", "changed-live-value");
        await using var recovered = new OpenCodeBackend(context: context, runId: "run");
        await Assert.That(await recovered.ReattachAsync(worker, ct)).IsTypeOf<ReattachResult.Interrupted>();
        HarnessException? failure = null;
        try { worker = await recovered.PromptAsync(worker, new("Continue.", true), ct); }
        catch (HarnessException ex) { failure = ex; }
        await Assert.That(failure).IsNull();
        await Assert.That(worker.Turn).IsEqualTo("turn");
        var puts = File.ReadLines(FakeOpenCode.Record(lab.Tree)).Select(x => JsonNode.Parse(x)!).Where(x => x["method"]!.GetValue<string>() == "PUT").ToArray();
        await Assert.That(puts.Length).IsEqualTo(2);
        await Assert.That(puts[0]["pid"]!.GetValue<int>()).IsNotEqualTo(puts[1]["pid"]!.GetValue<int>());
        await Assert.That(puts[1]["body"]!["variables"]!["TASK_ONLY"]!.GetValue<string>()).IsEqualTo("<redacted>");
    }

    [Test]
    [Arguments("reattach")]
    [Arguments("collect")]
    [Arguments("release")]
    [Arguments("answer")]
    public async Task Configured_recovery_does_not_recreate_temp_and_answers_restart_known_host(string operation, CancellationToken ct)
    {
        await using var lab = new OpenCodeLab();
        lab.Create("worker");
        lab.Add("GET /api/session/worker", OpenCodeLab.Session("worker", "interrupted"), repeat: true);
        lab.Add("GET /api/session/worker/message", new JsonObject { ["data"] = new JsonArray(), ["cursor"] = new JsonObject() }, repeat: true);
        lab.Add("DELETE /api/session/worker", status: 204);
        lab.Add("POST /api/session/worker/permission/ask/reply", status: 204);
        lab.Write();
        var spec = new TaskSpec { Id = "worker", Kind = "research", Backend = "opencode", Route = "fixture/model", Access = "full",
            Environment = JsonSerializer.SerializeToElement(new { TASK_ONLY = "frozen-value" }) };
        var config = ConfigResolver.Resolve(new() { Tasks = [spec] });
        var record = new TaskRecord { Spec = spec, Settings = config.Tasks["worker"].Settings, Workspace = new() { Path = lab.Tree.Repository } };
        var ledger = new Ledger("run");
        new ConfigLayers { Effective = config }.Freeze(ledger.Dir);
        ledger.SaveRecord(record);
        var context = new BackendContext(Path.Combine(lab.Tree.Root, "recovery-profiles"), ConfigLayers.User!.Backends!.Opencode!.Executable, new Dictionary<string, string>());
        WorkerRef worker;
        await using (var first = new OpenCodeBackend(context: context, runId: "run"))
            worker = (await first.CreateAsync(TaskStarter.BuildLaunch(record, "run", "opencode", Policy.From(config), null).Launch, ct)).Worker;
        var temp = Locations.TaskTemp("worker", "run");
        Directory.Delete(temp, recursive: true);
        await using var recovered = new OpenCodeBackend(context: context, runId: "run");
        HarnessException? failure = null;
        try
        {
            if (operation == "release") await recovered.ReleaseAsync(worker, ct);
            else if (operation == "collect") await Assert.That((await recovered.CollectAsync(worker, ct)).Outcome).IsEqualTo("interrupted");
            else
            {
                await Assert.That(await recovered.ReattachAsync(worker, ct)).IsTypeOf<ReattachResult.Interrupted>();
                if (operation == "answer")
                {
                    Hosts(recovered).TryRemove(worker.State["host"]!.GetValue<string>(), out var host);
                    await host!.DisposeAsync();
                    var answer = await recovered.AnswerAsync(new("opencode", worker.Id, null, "ask"), new BlockerAnswer.Once(), ct);
                    await Assert.That(answer.Confirmed).IsTrue();
                }
            }
        }
        catch (HarnessException ex) { failure = ex; }
        await Assert.That(failure).IsNull();
        await Assert.That(Directory.Exists(temp)).IsFalse();
        var calls = File.ReadLines(FakeOpenCode.Record(lab.Tree)).Select(x => JsonNode.Parse(x)!).ToArray();
        await Assert.That(calls.Where(x => x["unexpected"]!.GetValue<bool>())).IsEmpty();
    }

    [Test]
    public async Task Missing_fenced_host_is_lost_on_reattach_and_collection_without_reconstruction(CancellationToken ct)
    {
        await using var lab = new OpenCodeLab();
        lab.Add("DELETE /api/session/session", status: 204);
        lab.Write();
        var backend = lab.Backend;
        var worker = new WorkerRef("opencode", "task", "session", "turn", new JsonObject { ["host"] = "fence=run/task" });
        var reattached = await backend.ReattachAsync(worker, ct);
        await Assert.That(reattached).IsTypeOf<ReattachResult.Ended>();
        await Assert.That(((ReattachResult.Ended)reattached).Outcome).IsEqualTo("lost");
        var report = await backend.CollectAsync(worker, ct);
        await Assert.That(report.Outcome).IsEqualTo("lost");
        await Assert.That(report.ExecutionError).IsEqualTo("fenced opencode server is no longer live");
        await Assert.That(report.Complete).IsTrue();
        await backend.ReleaseAsync(worker, ct);
    }

    [Test]
    [Arguments("collect")]
    [Arguments("release")]
    public async Task Configured_recovery_without_frozen_config_keeps_recorded_environment(string operation, CancellationToken ct)
    {
        await using var lab = new OpenCodeLab();
        lab.Create("worker");
        lab.Add("GET /api/session/worker", OpenCodeLab.Session("worker", "failed"));
        lab.Add("GET /api/session/worker/message", new JsonObject
        {
            ["data"] = new JsonArray(JsonNode.Parse("""
                {"id":"answer","type":"assistant","time":{"created":2,"completed":3},"agent":"build",
                 "model":{"id":"model","providerID":"fixture"},"content":[{"type":"text","text":"Recorded answer."}],
                 "tokens":{"input":7,"output":3,"reasoning":0,"cache":{"read":0,"write":0}}}
                """)), ["cursor"] = new JsonObject(),
        });
        lab.Add("DELETE /api/session/worker", status: 204);
        lab.Write();
        var recordedCalls = Path.Combine(lab.Tree.Root, "configured-calls.jsonl");
        var spec = new TaskSpec { Id = "worker", Kind = "research", Backend = "opencode", Route = "fixture/model", Access = "full",
            Environment = JsonSerializer.SerializeToElement(new Dictionary<string, string> { ["FAKE_OPENCODE_RECORD"] = recordedCalls }) };
        var config = ConfigResolver.Resolve(new() { Tasks = [spec] });
        var record = new TaskRecord { Spec = spec, Settings = config.Tasks["worker"].Settings, Workspace = new() { Path = lab.Tree.Repository } };
        var ledger = new Ledger("run");
        new ConfigLayers { Effective = config }.Freeze(ledger.Dir);
        ledger.SaveRecord(record);
        var context = new BackendContext(Path.Combine(lab.Tree.Root, "recovery-profiles"), ConfigLayers.User!.Backends!.Opencode!.Executable, new Dictionary<string, string>());
        WorkerRef worker;
        await using (var first = new OpenCodeBackend(context: context, runId: "run"))
            worker = (await first.CreateAsync(TaskStarter.BuildLaunch(record, "run", "opencode", Policy.From(config), null).Launch, ct)).Worker;
        File.Delete(Path.Combine(ledger.Dir, "config.json"));
        await using var recovered = new OpenCodeBackend(context: context, runId: "run");
        Exception? failure = null;
        WorkerReport? report = null;
        try
        {
            if (operation == "collect") report = await recovered.CollectAsync(worker, ct);
            else await recovered.ReleaseAsync(worker, ct);
        }
        catch (Exception ex) { failure = ex; }
        await Assert.That(failure).IsNull();
        if (operation == "collect")
        {
            await Assert.That(report!.Outcome).IsEqualTo("failed");
            await Assert.That(report.FinalText).IsEqualTo("Recorded answer.");
            await Assert.That(report.Usage.Input).IsEqualTo(0);
            await Assert.That(report.Calls!.Single().Input).IsEqualTo(7);
        }
        var calls = File.ReadLines(recordedCalls).Select(x => JsonNode.Parse(x)!).ToArray();
        await Assert.That(calls.Where(x => x["path"]!.GetValue<string>().StartsWith("/api/session", StringComparison.Ordinal)).Select(x => $"{x["method"]} {x["path"]}"))
            .IsEquivalentTo(operation == "collect" ? new[] { "POST /api/session", "PUT /api/session/worker/environment", "GET /api/session/worker", "GET /api/session/worker/message" }
                : ["POST /api/session", "PUT /api/session/worker/environment", "DELETE /api/session/worker"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(calls.Where(x => x["unexpected"]!.GetValue<bool>())).IsEmpty();
    }

    [Test]
    [Arguments(204)]
    [Arguments(500)]
    public async Task Lost_fence_release_deletes_through_shared_host_and_reports_failure(int status, CancellationToken ct)
    {
        await using var lab = new OpenCodeLab();
        lab.Add("DELETE /api/session/session", new JsonObject { ["_tag"] = "InternalError", ["message"] = "release failed" }, status);
        lab.Write();
        var worker = new WorkerRef("opencode", "task", "session", "turn", new JsonObject { ["host"] = "fence=run/task" });
        Exception? failure = null;
        try { await lab.Backend.ReleaseAsync(worker, ct); }
        catch (HarnessException ex) { failure = ex; }
        await Assert.That(failure?.Message).IsEqualTo(status == 204 ? null : "session session release failed: HTTP 500");
        var requests = (File.Exists(FakeOpenCode.Record(lab.Tree)) ? File.ReadLines(FakeOpenCode.Record(lab.Tree)) : []).Select(x => JsonNode.Parse(x)!);
        await Assert.That(requests.Where(x => x["method"]!.GetValue<string>() == "DELETE")
            .Select(x => x["path"]!.GetValue<string>())).IsEquivalentTo(new[] { "/api/session/session" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(true, false)]
    [Arguments(false, true)]
    public async Task Unresponsive_or_missing_fenced_host_loses_only_its_worker(bool missing, bool exited, CancellationToken ct)
    {
        await using var lab = new OpenCodeLab();
        lab.Create("worker");
        lab.Create("peer");
        lab.Prompt("peer");
        var active = lab.Add("GET /api/session/active", new JsonObject { ["_tag"] = "InternalError", ["message"] = "offline" }, 500, repeat: true);
        lab.Add("GET /api/permission/request", new JsonObject { ["location"] = new JsonObject { ["directory"] = "$directory" }, ["data"] = new JsonArray() }, repeat: true);
        lab.Add("GET /api/form", new JsonObject { ["location"] = new JsonObject { ["directory"] = "$directory" }, ["data"] = new JsonArray() }, repeat: true);
        lab.Write();
        var worker = Fence(lab.Backend, (await lab.Backend.CreateAsync(Launch(lab, "worker", access: "full"), ct)).Worker) with { Turn = "lost-turn" };
        active["status"] = 200;
        active["reply"] = new JsonObject { ["data"] = new JsonObject { ["peer"] = new JsonObject { ["type"] = "busy" } } };
        lab.Steps.RemoveAll(x => x["reply"]?["data"] is JsonObject data && data["id"]?.GetValue<string>() == "worker"
            || x["expect"]!.GetValue<string>() == "PUT /api/session/worker/environment");
        lab.Write();
        var peer = (await lab.Backend.CreateAsync(Launch(lab, "peer", access: "full"), ct)).Worker with { Turn = "peer-turn" };
        var host = Hosts(lab.Backend)[worker.State["host"]!.GetValue<string>()];
        if (missing) await host.DisposeAsync();
        if (exited)
        {
            using var process = System.Diagnostics.Process.GetProcessById(checked((int)host.Pid));
            process.Kill();
            await host.Server.Exited.WaitAsync(ct);
        }
        if (missing) Hosts(lab.Backend).TryRemove(worker.State["host"]!.GetValue<string>(), out _);
        // Missing/exited-host cases are guards; a live process gets the three-failure watchdog tolerance.
        if (!missing && !exited)
            for (var poll = 1; poll < 3; poll++)
            {
                await lab.Backend.PollAsync([worker, peer], ct);
                var premature = new List<WorkerSignal.Ended>();
                while (lab.Backend.Signals.TryRead(out var signal)) if (signal is WorkerSignal.Ended e) premature.Add(e);
                await Assert.That(premature).IsEmpty();
            }
        Exception? failure = null;
        try { await lab.Backend.PollAsync([worker, peer], ct); }
        catch (HarnessException ex) { failure = ex; }
        await Assert.That(failure).IsNull();
        var ended = new List<WorkerSignal.Ended>();
        while (lab.Backend.Signals.TryRead(out var signal)) if (signal is WorkerSignal.Ended e) ended.Add(e);
        await Assert.That(ended).IsEquivalentTo(new[] { new WorkerSignal.Ended("worker", "lost-turn", "lost", null) }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That((await lab.Backend.PromptAsync(peer, new("Continue.", false), ct)).Turn).IsEqualTo("turn");
    }

    [Test]
    public async Task Successful_active_poll_resets_fenced_host_failure_count(CancellationToken ct)
    {
        await using var lab = new OpenCodeLab();
        lab.Create("worker");
        var error = new JsonObject { ["_tag"] = "InternalError", ["message"] = "offline" };
        lab.Add("GET /api/session/active", error, 500);
        lab.Add("GET /api/session/active", new JsonObject { ["data"] = new JsonObject { ["worker"] = new JsonObject { ["type"] = "busy" } } });
        lab.Add("GET /api/session/active", error.DeepClone(), 500, repeat: true);
        lab.Add("GET /api/permission/request", new JsonObject { ["location"] = new JsonObject { ["directory"] = "$directory" }, ["data"] = new JsonArray() }, repeat: true);
        lab.Add("GET /api/form", new JsonObject { ["location"] = new JsonObject { ["directory"] = "$directory" }, ["data"] = new JsonArray() }, repeat: true);
        lab.Write();
        var worker = Fence(lab.Backend, (await lab.Backend.CreateAsync(Launch(lab, "worker", access: "full"), ct)).Worker) with { Turn = "turn" };
        for (var poll = 1; poll <= 5; poll++)
        {
            await lab.Backend.PollAsync([worker], ct);
            var ended = new List<WorkerSignal.Ended>();
            while (lab.Backend.Signals.TryRead(out var signal)) if (signal is WorkerSignal.Ended e) ended.Add(e);
            await Assert.That(ended).IsEquivalentTo(poll == 5 ? new[] { new WorkerSignal.Ended("worker", "turn", "lost", null) } : [],
                TUnit.Assertions.Enums.CollectionOrdering.Matching);
        }
    }

    [Test]
    public async Task Read_only_linux_launch_rebinds_git_common_under_tmp(CancellationToken ct)
    {
        if (!OperatingSystem.IsLinux()) Skip.Test("Linux bwrap argv only.");
        if (await Ready.FenceAsync(ct) is { } refusal) Skip.Test(refusal);
        await using var lab = new OpenCodeLab();
        var temp = Directory.CreateDirectory(Path.Combine("/tmp", $"dispatch-common-{Guid.NewGuid():N}")).FullName;
        var common = Path.Combine(temp, "common");
        var argv = Path.Combine(temp, "argv");
        var shim = Path.Combine(temp, "bwrap");
        var field = typeof(Ready).GetField("fenceExecutable", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        var original = field.GetValue(null);
        try
        {
            using var git = new CapturedProcess();
            (await git.RunAsync("git", ["init", "--separate-git-dir", common, lab.Tree.Repository], lab.Tree.Root,
                lab.Tree.ChildEnvironment(), ct)).RequireSuccess("separate git folder fixture");
            File.WriteAllText(shim, $"#!/bin/sh\nprintf '%s\\n' \"$@\" > '{argv}'\nwhile [ \"$1\" != -- ]; do shift; done\nshift\nexec \"$@\"\n");
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(shim, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            field.SetValue(null, shim);
            lab.Create("worker");
            lab.Write();
            await lab.Backend.CreateAsync(Launch(lab, "worker", access: "read-only"), ct);
            var arguments = File.ReadAllLines(argv);
            var bind = Array.IndexOf(arguments, common);
            await Assert.That(bind).IsGreaterThan(0);
            await Assert.That(arguments[(bind - 1)..(bind + 2)])
                .IsEquivalentTo(new[] { "--ro-bind", common, common }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        }
        finally { field.SetValue(null, original); Directory.Delete(temp, recursive: true); }
    }

    [Test]
    public async Task Failed_fenced_task_disposes_its_server_but_keeps_session(CancellationToken ct)
    {
        using var core = new CoreLab();
        await using var lab = new OpenCodeLab(core.Tree);
        var run = await core.TaskAsync("worker", ct);
        lab.Create("worker");
        lab.Add("GET /api/session/worker", OpenCodeLab.Session("worker", "failed"));
        lab.Add("GET /api/session/worker/message", new JsonObject { ["data"] = new JsonArray(), ["cursor"] = new JsonObject() });
        lab.Write();
        run.Worker = Fence(lab.Backend, (await lab.Backend.CreateAsync(Launch(lab, "worker", access: "full"), ct)).Worker);
        run.WorkerEnded = true;
        run.State = TaskState.Settling;
        var process = ObservedProcess.Capture(lab.Hosts().Single()["pid"]!.GetValue<int>());
        var scheduler = new Scheduler(new Dictionary<string, IWorkerBackend> { ["opencode"] = lab.Backend }, core.Ledger,
            core.Tree.Repository, AllowList.From(null), core.Policy, 1, ct, core.Git);
        await (Task)typeof(Scheduler).GetMethod("SettleAsync", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(scheduler, [run, "failed"])!;
        await Assert.That(run.State).IsEqualTo(TaskState.Failed);
        await Assert.That(core.Ledger.LoadResult(run.Id)!.Failures).IsEquivalentTo(new[] { "outcome:failed", "no-final-text" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(process.IsAlive()).IsFalse();
        await Assert.That(File.ReadLines(FakeOpenCode.Record(lab.Tree)).Select(x => JsonNode.Parse(x)!["method"]!.GetValue<string>())).DoesNotContain("DELETE");
    }

    static System.Collections.Concurrent.ConcurrentDictionary<string, HarnessServer> Hosts(OpenCodeBackend backend) =>
        (System.Collections.Concurrent.ConcurrentDictionary<string, HarnessServer>)typeof(OpenCodeBackend)
            .GetField("hosts", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(backend)!;

    static WorkerRef Fence(OpenCodeBackend backend, WorkerRef worker)
    {
        var key = "fence=run/" + worker.TaskId;
        foreach (var name in new[] { "hosts", "pumps" })
        {
            var map = (System.Collections.IDictionary)typeof(OpenCodeBackend).GetField(name,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(backend)!;
            var old = worker.State["host"]!.GetValue<string>();
            var value = map[old];
            map.Remove(old);
            map.Add(key, value);
        }
        return worker with { State = new JsonObject { ["host"] = key } };
    }

    [Test]
    public async Task Writable_link_into_control_is_refused_again_at_fenced_start(CancellationToken ct)
    {
        await using var lab = new OpenCodeLab();
        Directory.CreateDirectory(Locations.ControlRoot);
        var link = Path.Combine(lab.Tree.Root, "writable-link");
        if (OperatingSystem.IsWindows())
        {
            using var command = new CapturedProcess();
            (await command.RunAsync("cmd", ["/c", "mklink", "/J", link, Locations.ControlRoot], lab.Tree.Root, lab.Tree.ChildEnvironment(), ct)).RequireSuccess("junction fixture");
        }
        else Directory.CreateSymbolicLink(link, Locations.ControlRoot);
        try
        {
            var launch = Launch(lab, "worker") with { WritableRoots = [link] };
            HarnessException? refusal = null;
            try { Ready.WritableSet(launch, "unused", lab.Tree.Root, launch.Environment, null); }
            catch (HarnessException ex) { refusal = ex; }
            var expected = $"worker: opencode fence writable root overlaps control root: {link}";
            await Assert.That(refusal?.Message).IsEqualTo(expected);
            if (Ready.FenceKind is not null && await Ready.FenceAsync(ct) is null)
            {
                refusal = null;
                try { await lab.Backend.CreateAsync(launch, ct); }
                catch (HarnessException ex) { refusal = ex; }
                await Assert.That(refusal?.Message).IsEqualTo(expected);
                await Assert.That(lab.Hosts()).IsEmpty();
            }
        }
        finally { Directory.Delete(link); }
    }

    [Test]
    public async Task Fenced_creation_failure_disposes_server_with_private_cache_and_temp(CancellationToken ct)
    {
        if (Ready.FenceKind is null) Skip.Test("opencode runs unfenced on Windows.");
        if (await Ready.FenceAsync(ct) is { } refusal) Skip.Test(refusal);
        await using var lab = new OpenCodeLab();
        var create = lab.Add("POST /api/session", new JsonObject { ["_tag"] = "InternalError", ["message"] = "create failed" }, 500);
        var arrived = Path.Combine(lab.Tree.ChildEnvironment()["TEMP"]!, "create-arrived");
        var release = Path.Combine(lab.Tree.ChildEnvironment()["TEMP"]!, "create-release");
        create["arrived"] = arrived;
        create["waitFor"] = release;
        lab.Write();
        var launch = Launch(lab, "worker");
        var creating = lab.Backend.CreateAsync(launch, ct);
        while (!File.Exists(arrived) && !creating.IsCompleted) await Task.Delay(10, ct);
        var host = lab.Hosts().Single();
        var process = ObservedProcess.Capture(host["pid"]!.GetValue<int>());
        var temporary = PrivateTemp(host, launch);
        // Guard: both private folders must exist while the server is live.
        await Assert.That(Directory.Exists(Path.Combine(launch.Environment["TEMP"], "cache"))).IsTrue();
        await Assert.That(Directory.Exists(temporary)).IsTrue();
        await File.WriteAllTextAsync(release, "release", ct);
        HarnessException? failure = null;
        try { await creating; }
        catch (HarnessException ex) { failure = ex; }
        await Assert.That(failure?.Message).IsEqualTo("the server call failed: OpenCodeApiException: The opencode API returned status 500 ('InternalError').");
        await Assert.That(process.IsAlive()).IsFalse();
        foreach (var name in new[] { "TMPDIR", "TEMP", "TMP" })
            await Assert.That(host["environment"]![name]!.GetValue<string>()).IsEqualTo(temporary);
        await Assert.That(host["environment"]!["XDG_CACHE_HOME"]!.GetValue<string>()).IsEqualTo(Path.Combine(launch.Environment["TEMP"], "cache"));
        await Assert.That(Directory.Exists(Path.Combine(launch.Environment["TEMP"], "cache"))).IsFalse();
        await Assert.That(Directory.Exists(temporary)).IsFalse();
    }

    // Linux and macOS give a fenced server a short private TMPDIR for Unix sockets.
    static string PrivateTemp(JsonNode host, WorkerLaunch launch)
    {
        var temporary = host["environment"]!["TMPDIR"]!.GetValue<string>();
        if (OperatingSystem.IsMacOS())
        {
            if (!System.Text.RegularExpressions.Regex.IsMatch(temporary, "^/private/tmp/dispatch-[0-9a-f]{12}$"))
                throw new InvalidOperationException($"unexpected macOS private temp: {temporary}");
        }
        else if (OperatingSystem.IsLinux()
            ? !System.Text.RegularExpressions.Regex.IsMatch(temporary, "^/var/tmp/dispatch-[0-9a-f]{12}$")
            : temporary != Path.Combine(launch.Environment["TEMP"], "tmp"))
            throw new InvalidOperationException($"unexpected private temp: {temporary}");
        return temporary;
    }

    [Test]
    public async Task Fenced_disposal_removes_private_folders_without_removing_task_or_peer_temp(CancellationToken ct)
    {
        if (Ready.FenceKind is null) Skip.Test("opencode runs unfenced on Windows.");
        if (await Ready.FenceAsync(ct) is { } refusal) Skip.Test(refusal);
        await using var lab = new OpenCodeLab();
        lab.Create("worker");
        lab.Create("peer");
        lab.Add("DELETE /api/session/worker", status: 204);
        lab.Write();
        WorkerLaunch TaskLaunch(string id)
        {
            var launch = Launch(lab, id);
            var environment = new Dictionary<string, string>(launch.Environment);
            foreach (var name in new[] { "TEMP", "TMP", "TMPDIR" })
                environment[name] = Path.Combine(lab.Tree.Root, id);
            return launch with { Environment = environment };
        }
        var launch = TaskLaunch("worker");
        var peerLaunch = TaskLaunch("peer");
        var worker = (await lab.Backend.CreateAsync(launch, ct)).Worker;
        var peer = (await lab.Backend.CreateAsync(peerLaunch, ct)).Worker;
        // Each server's record names its own cache, and with it its private temp.
        JsonObject HostOf(WorkerLaunch task) =>
            lab.Hosts().Single(x => x["environment"]!["XDG_CACHE_HOME"]!.GetValue<string>() == Path.Combine(task.Environment["TEMP"], "cache"));
        string[] Private(WorkerLaunch task) => [Path.Combine(task.Environment["TEMP"], "cache"), PrivateTemp(HostOf(task), task)];
        var (own, peers) = (Private(launch), Private(peerLaunch));
        await Assert.That(own[1]).IsNotEqualTo(peers[1]);
        // The session's shell takes the server's short private temp so its Unix sockets fit.
        foreach (var (task, folders) in new[] { (launch, own), (peerLaunch, peers) })
        {
            // Both fake servers name their first session alike; the server's pid tells their requests apart.
            var pid = HostOf(task)["pid"]!.GetValue<int>();
            var puts = File.ReadLines(FakeOpenCode.Record(lab.Tree)).Select(x => JsonNode.Parse(x)!)
                .Where(x => x["pid"]!.GetValue<int>() == pid && x["method"]!.GetValue<string>() == "PUT"
                    && x["path"]!.GetValue<string>().EndsWith("/environment", StringComparison.Ordinal)).ToArray();
            await Assert.That(puts).IsNotEmpty();
            foreach (var put in puts)
                foreach (var name in new[] { "TEMP", "TMP", "TMPDIR" })
                    await Assert.That(put["body"]!["variables"]![name]!.GetValue<string>()).IsEqualTo(folders[1]);
        }
        foreach (var (task, folders) in new[] { (launch, own), (peerLaunch, peers) })
        {
            File.WriteAllText(Path.Combine(task.Environment["TEMP"], "keep"), task.TaskId);
            foreach (var folder in folders) File.WriteAllText(Path.Combine(folder, "server-file"), Path.GetFileName(folder));
        }
        await lab.Backend.ReleaseAsync(worker, ct);
        foreach (var folder in own) await Assert.That(Directory.Exists(folder)).IsFalse();
        await Assert.That(File.ReadAllText(Path.Combine(launch.Environment["TEMP"], "keep"))).IsEqualTo("worker");
        foreach (var folder in peers)
            await Assert.That(File.ReadAllText(Path.Combine(folder, "server-file"))).IsEqualTo(Path.GetFileName(folder));
        await Hosts(lab.Backend)[peer.State["host"]!.GetValue<string>()].DisposeAsync();
        foreach (var folder in peers) await Assert.That(Directory.Exists(folder)).IsFalse();
        await Assert.That(File.ReadAllText(Path.Combine(peerLaunch.Environment["TEMP"], "keep"))).IsEqualTo("peer");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Linux_fenced_launch_has_short_private_temp_and_private_runfile_bind(bool includeSelf, CancellationToken ct)
    {
        if (!OperatingSystem.IsLinux()) Skip.Test("Linux private folders only.");
        if (await Ready.FenceAsync(ct) is { } refusal) Skip.Test(refusal);
        await using var lab = new OpenCodeLab();
        lab.SetEnvironment("XDG_STATE_HOME", lab.Tree.ChildEnvironment()["XDG_STATE_HOME"]);
        var repo = await LabRepo.CreateAsync(lab.Tree, ct);
        var common = Path.Combine(repo.Root, ".git");
        Directory.CreateDirectory(Path.Combine(common, "hooks"));
        var argv = Path.Combine(lab.Tree.Root, "bwrap-argv");
        var shim = Path.Combine(lab.Tree.Root, "bwrap");
        File.WriteAllText(shim, $"#!/bin/sh\nprintf '%s\\n' \"$@\" > '{argv}'\nwhile [ \"$1\" != -- ]; do shift; done\nshift\nexec \"$@\"\n");
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(shim, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var field = typeof(Ready).GetField("fenceExecutable", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        var original = field.GetValue(null);
        field.SetValue(null, shim);
        try
        {
            lab.Create("worker");
            lab.Write();
            var launch = Launch(lab, "worker");
            var configured = new Dictionary<string, string> { ["XDG_DATA_HOME"] = Path.Combine(lab.Tree.Root, "private-data"),
                ["XDG_STATE_HOME"] = Path.Combine(lab.Tree.Root, "private-config") };
            var environment = new Dictionary<string, string>(launch.Environment);
            foreach (var (name, value) in configured) environment[name] = value;
            var target = Path.Combine(lab.Tree.Root, "z-private");
            var other = Path.Combine(lab.Tree.Root, "a-private");
            Directory.CreateDirectory(other);
            var alias = Path.Combine(lab.Tree.Root, "private-alias");
            Directory.CreateSymbolicLink(alias, other);
            var self = Path.Combine(Environment.GetEnvironmentVariable("XDG_DATA_HOME")!, "dotnet", "runfile");
            var data = Path.Combine(Environment.GetEnvironmentVariable("XDG_DATA_HOME")!, "opencode");
            var state = Path.Combine(Environment.GetEnvironmentVariable("XDG_STATE_HOME")!, "opencode");
            Directory.CreateDirectory(Path.Combine(data, "bin"));
            var runtime = Directory.CreateDirectory(Path.Combine(lab.Tree.Root, "runtime")).FullName;
            environment["XDG_RUNTIME_DIR"] = runtime;
            launch = launch with { Environment = environment, ConfiguredEnvironment = configured,
                PrivateFolders = includeSelf ? [target, alias, target, self] : [target, alias, target] };
            await Assert.That(Directory.Exists(target)).IsFalse();
            var worker = (await lab.Backend.CreateAsync(launch, ct)).Worker;
            var host = lab.Hosts().Single();
            var temporary = host["environment"]!["TMPDIR"]!.GetValue<string>();
            var source = Path.Combine(temporary, "private-0");
            var otherSource = Path.Combine(temporary, "private-1");
            var selfSource = Path.Combine(temporary, "private-2");
            var appendedSelf = Path.Combine(temporary, "private-3");
            await Assert.That(temporary).StartsWith("/var/tmp/dispatch-");
            await Assert.That(temporary.Length).IsLessThanOrEqualTo(30);
            await Assert.That(Directory.Exists(temporary)).IsTrue();
            if (!OperatingSystem.IsWindows()) await Assert.That(File.GetUnixFileMode(temporary))
                .IsEqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            foreach (var name in new[] { "TMPDIR", "TEMP", "TMP" })
                await Assert.That(host["environment"]![name]!.GetValue<string>()).IsEqualTo(temporary);
            var puts = File.ReadLines(FakeOpenCode.Record(lab.Tree)).Select(x => JsonNode.Parse(x)!)
                .Where(x => x["method"]!.GetValue<string>() == "PUT" && x["path"]!.GetValue<string>() == "/api/session/worker/environment").ToArray();
            await Assert.That(puts.Length).IsEqualTo(1);
            foreach (var name in new[] { "TMPDIR", "TEMP", "TMP" })
                await Assert.That(puts[0]["body"]!["variables"]![name]!.GetValue<string>()).IsEqualTo(temporary);
            var arguments = File.ReadAllLines(argv);
            await Assert.That(arguments).IsEquivalentTo((string[])
            [
                "--ro-bind", "/", "/", "--dev", "/dev", "--ro-bind", "/proc", "/proc", "--tmpfs", "/tmp",
                "--bind", data, data, "--bind", state, state,
                "--bind", environment["TEMP"], environment["TEMP"], "--bind", launch.Directory, launch.Directory,
                "--bind", common, common,
                "--bind", temporary, temporary,
                "--bind", source, target, "--bind", otherSource, other, "--bind", selfSource, self,
                .. includeSelf ? new[] { "--bind", appendedSelf, self } : Array.Empty<string>(),
                "--tmpfs", runtime, "--ro-bind", Path.Combine(data, "bin"), Path.Combine(data, "bin"),
                "--ro-bind", Path.Combine(common, "hooks"), Path.Combine(common, "hooks"),
                "--ro-bind", Path.Combine(common, "config"), Path.Combine(common, "config"),
                "--", .. HarnessServer.Command(ConfigLayers.User!.Backends!.Opencode!.Executable),
                .. (string[])typeof(OpenCode.Sdk.OpenCodeServer).GetField("LauncherArguments",
                    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!.GetValue(null)!,
            ], TUnit.Assertions.Enums.CollectionOrdering.Matching);
            var bind = Array.IndexOf(arguments, source);
            await Assert.That(bind).IsGreaterThan(0);
            await Assert.That(arguments[(bind - 1)..(bind + 2)])
                .IsEquivalentTo(new[] { "--bind", source, target }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
            await Assert.That(Directory.Exists(source)).IsTrue();
            if (!OperatingSystem.IsWindows()) await Assert.That(File.GetUnixFileMode(source))
                .IsEqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            await Assert.That(Directory.Exists(target)).IsTrue();
            foreach (var folder in new[] { otherSource, selfSource })
            {
                await Assert.That(Directory.Exists(folder)).IsTrue();
                if (!OperatingSystem.IsWindows()) await Assert.That(File.GetUnixFileMode(folder))
                    .IsEqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
            await Assert.That(Directory.Exists(other)).IsTrue();
            await Assert.That(Directory.Exists(self)).IsTrue();
            await Hosts(lab.Backend)[worker.State["host"]!.GetValue<string>()].DisposeAsync();
            await Assert.That(Directory.Exists(temporary)).IsFalse();
            await Assert.That(Directory.Exists(source)).IsFalse();
            await Assert.That(Directory.Exists(otherSource)).IsFalse();
            await Assert.That(Directory.Exists(selfSource)).IsFalse();
            await Assert.That(Directory.Exists(target)).IsTrue();
            await Assert.That(Directory.Exists(other)).IsTrue();
        }
        finally { field.SetValue(null, original); }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Linux_configured_state_cannot_expand_fence_and_private_sources_stay_inside_owned_temp(bool configureState, CancellationToken ct)
    {
        if (!OperatingSystem.IsLinux()) Skip.Test("Linux fence and private sources only.");
        if (await Ready.FenceAsync(ct) is { } unavailable) Skip.Test(unavailable);
        await using var lab = new OpenCodeLab();
        lab.SetEnvironment("XDG_STATE_HOME", lab.Tree.ChildEnvironment()["XDG_STATE_HOME"]);
        lab.Create("worker");
        lab.Write();
        var configured = new Dictionary<string, string>();
        if (configureState) configured["XDG_STATE_HOME"] = Locations.ControlRoot;
        var variables = WorkerEnvironment.Copy(Launch(lab, "worker").Environment);
        foreach (var (name, value) in configured) variables[name] = value;
        HarnessException? failure = null;
        WorkerRef? worker = null;
        try { worker = (await lab.Backend.CreateAsync(Launch(lab, "worker") with { Environment = variables, ConfiguredEnvironment = configured }, ct)).Worker; }
        catch (HarnessException ex) { failure = ex; }
        await Assert.That(failure).IsNull();
        var temporary = lab.Hosts().Single()["environment"]!["TMPDIR"]!.GetValue<string>();
        var source = Path.Combine(temporary, "private-0");
        await Assert.That(Directory.Exists(source)).IsTrue();
        if (!OperatingSystem.IsWindows())
            foreach (var folder in new[] { temporary, source })
                await Assert.That(File.GetUnixFileMode(folder)).IsEqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        await Hosts(lab.Backend)[worker!.State["host"]!.GetValue<string>()].DisposeAsync();
        await Assert.That(Directory.Exists(temporary)).IsFalse();
        await Assert.That(Directory.Exists(source)).IsFalse();
    }

    [Test]
    [Arguments("workspace")]
    [Arguments("ancestor")]
    [Arguments("common")]
    [Arguments("alias")]
    [Arguments("inside")]
    public async Task Linux_private_folders_cannot_hide_workspace_or_git_common(string kind, CancellationToken ct)
    {
        if (!OperatingSystem.IsLinux()) Skip.Test("Linux private binds only.");
        if (await Ready.FenceAsync(ct) is { } unavailable) Skip.Test(unavailable);
        await using var lab = new OpenCodeLab();
        var repo = await LabRepo.CreateAsync(lab.Tree, ct);
        lab.Create("worker");
        lab.Write();
        var common = Path.Combine(repo.Root, ".git");
        var entry = kind switch { "ancestor" => Path.GetDirectoryName(repo.Root)!, "common" => common,
            "inside" => Path.Combine(repo.Root, "private-cache"), "alias" => Path.Combine(lab.Tree.Root, "workspace-alias"), _ => repo.Root };
        if (kind == "alias") Directory.CreateSymbolicLink(entry, repo.Root);
        var existingTemps = Directory.GetDirectories("/var/tmp", "dispatch-*").ToHashSet(StringComparer.Ordinal);
        HarnessException? failure = null;
        try { await lab.Backend.CreateAsync(Launch(lab, "worker") with { PrivateFolders = [entry] }, ct); }
        catch (HarnessException ex) { failure = ex; }
        await Assert.That(failure?.Message).IsEqualTo(kind == "inside" ? null
            : $"worker: privateFolders entry {entry} would hide the {(kind == "common" ? "git common folder" : "workspace")}: {(kind == "common" ? common : repo.Root)}");
        if (kind == "inside") await Assert.That(Directory.Exists(entry)).IsTrue();
        else
        {
            await Assert.That(lab.Hosts()).IsEmpty();
            await Assert.That(Directory.GetDirectories("/var/tmp", "dispatch-*").Except(existingTemps)).IsEmpty();
        }
    }

    [Test]
    [Arguments("private-temp")]
    [Arguments("data")]
    [Arguments("data-ancestor")]
    [Arguments("state")]
    [Arguments("task-temp")]
    [Arguments("writable-root")]
    [Arguments("writable-alias")]
    [Arguments("dev")]
    public async Task Linux_private_folders_cannot_hide_any_launch_writable_grant(string kind, CancellationToken ct)
    {
        if (!OperatingSystem.IsLinux()) Skip.Test("Linux private binds only.");
        if (await Ready.FenceAsync(ct) is { } unavailable) Skip.Test(unavailable);
        await using var lab = new OpenCodeLab();
        // A lab under /var/tmp puts the workspace inside the entry too, and that refusal comes first.
        if (kind == "private-temp" && lab.Tree.Root.StartsWith("/var/tmp/", StringComparison.Ordinal))
            Skip.Test("The lab tree lies under /var/tmp, so /var/tmp also hides the workspace.");
        lab.SetEnvironment("XDG_STATE_HOME", lab.Tree.ChildEnvironment()["XDG_STATE_HOME"]);
        lab.Create("worker");
        lab.Write();
        var writableRoot = Path.Combine(lab.Tree.Root, "extra-writable");
        var launch = Launch(lab, "worker") with { WritableRoots = [writableRoot] };
        var folder = kind switch
        {
            "private-temp" => "/var/tmp", "data" or "data-ancestor" => Path.Combine(Environment.GetEnvironmentVariable("XDG_DATA_HOME")!, "opencode"),
            "state" => Path.Combine(Environment.GetEnvironmentVariable("XDG_STATE_HOME")!, "opencode"),
            "task-temp" => launch.Environment["TEMP"], "dev" => "/dev", _ => writableRoot,
        };
        var entry = kind == "data-ancestor" ? Path.GetDirectoryName(folder)! : folder;
        if (kind == "writable-alias")
        {
            Directory.CreateDirectory(folder);
            entry = Path.Combine(lab.Tree.Root, "writable-alias");
            Directory.CreateSymbolicLink(entry, folder);
        }
        var existingTemps = Directory.GetDirectories("/var/tmp", "dispatch-*").ToHashSet(StringComparer.Ordinal);
        HarnessException? failure = null;
        try { await lab.Backend.CreateAsync(launch with { PrivateFolders = [entry] }, ct); }
        catch (HarnessException ex) { failure = ex; }
        if (kind == "private-temp")
        {
            var prefix = $"worker: privateFolders entry {entry} would hide the writable folder: ";
            await Assert.That(failure?.Message).StartsWith(prefix);
            folder = failure!.Message[prefix.Length..];
            await Assert.That(System.Text.RegularExpressions.Regex.IsMatch(folder, "^/var/tmp/dispatch-[0-9a-f]{12}$")).IsTrue();
        }
        await Assert.That(failure?.Message).IsEqualTo($"worker: privateFolders entry {entry} would hide the writable folder: {folder}");
        await Assert.That(lab.Hosts()).IsEmpty();
        await Assert.That(Directory.GetDirectories("/var/tmp", "dispatch-*").Except(existingTemps)).IsEmpty();
    }

    [Test]
    public async Task Linux_private_runfile_creation_refuses_existing_folder(CancellationToken ct)
    {
        if (!OperatingSystem.IsLinux()) Skip.Test("Linux private runfile bind only.");
        if (await Ready.FenceAsync(ct) is { } unavailable) Skip.Test(unavailable);
        await using var lab = new OpenCodeLab();
        var source = Directory.CreateDirectory(Path.Combine("/var/tmp", $"dispatch-{Guid.NewGuid():N}"[..21]) + "-runfile").FullName;
        var keep = Path.Combine(source, "keep");
        File.WriteAllText(keep, "pre-existing");
        HarnessException? failure = null;
        try
        {
            try { typeof(HarnessServer).GetMethod("CreatePrivateFolder", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!.Invoke(null, ["worker", source]); }
            catch (System.Reflection.TargetInvocationException ex) when (ex.InnerException is HarnessException refusal) { failure = refusal; }
            await Assert.That(failure?.Message).IsEqualTo($"worker: refused an existing private folder: {source}");
            await Assert.That(File.ReadAllText(keep)).IsEqualTo("pre-existing");
        }
        finally { Directory.Delete(source, recursive: true); }
    }

    [Test]
    public async Task Linux_private_folders_are_fresh_and_cleaned_after_failed_start(CancellationToken ct)
    {
        if (!OperatingSystem.IsLinux()) Skip.Test("Linux private binds only.");
        if (await Ready.FenceAsync(ct) is { } unavailable) Skip.Test(unavailable);
        await using var lab = new OpenCodeLab();
        var argv = Path.Combine(lab.Tree.Root, "failed-bwrap-argv");
        var shim = Path.Combine(lab.Tree.Root, "failed-bwrap");
        File.WriteAllText(shim, $"#!/bin/sh\nprintf '%s\\n' \"$@\" > '{argv}'\nexit 7\n");
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(shim, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var field = typeof(Ready).GetField("fenceExecutable", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        var original = field.GetValue(null);
        field.SetValue(null, shim);
        try
        {
            var target = Path.Combine(lab.Tree.Root, "private-cache");
            var sources = new List<string>();
            for (var attempt = 0; attempt < 2; attempt++)
            {
                var launch = Launch(lab, "worker") with { PrivateFolders = [target] };
                await Assert.That(async () => await lab.Backend.CreateAsync(launch, ct)).Throws<HarnessException>();
                var arguments = File.ReadAllLines(argv);
                var bind = Array.IndexOf(arguments, target);
                await Assert.That(bind).IsGreaterThan(0);
                await Assert.That(arguments[bind - 2]).IsEqualTo("--bind");
                var source = arguments[bind - 1];
                sources.Add(source);
                await Assert.That(source).EndsWith("/private-0");
                await Assert.That(Directory.Exists(source)).IsFalse();
                await Assert.That(Directory.Exists(Path.Combine(Path.GetDirectoryName(source)!, "private-1"))).IsFalse();
                await Assert.That(Directory.Exists(Path.GetDirectoryName(source))).IsFalse();
                await Assert.That(Directory.Exists(target)).IsTrue();
            }
            await Assert.That(sources[0]).IsNotEqualTo(sources[1]);
        }
        finally { field.SetValue(null, original); }
    }

    [Test]
    public async Task Linux_fence_git_lookup_does_not_receive_task_configuration(CancellationToken ct)
    {
        if (!OperatingSystem.IsLinux()) Skip.Test("Linux fence Git lookup only.");
        if (await Ready.FenceAsync(ct) is { } unavailable) Skip.Test(unavailable);
        await using var lab = new OpenCodeLab();
        var repo = await LabRepo.CreateAsync(lab.Tree, ct);
        lab.Create("worker");
        lab.Write();
        var variables = WorkerEnvironment.Copy(Launch(lab, "worker").Environment);
        variables["GIT_CONFIG_COUNT"] = "not-a-number";
        var launch = Launch(lab, "worker") with { Environment = variables, ConfiguredEnvironment = new Dictionary<string, string> { ["GIT_CONFIG_COUNT"] = "not-a-number" } };
        var argv = Path.Combine(lab.Tree.Root, "git-bwrap-argv");
        var shim = Path.Combine(lab.Tree.Root, "git-bwrap");
        File.WriteAllText(shim, $"#!/bin/sh\nprintf '%s\\n' \"$@\" > '{argv}'\nwhile [ \"$1\" != -- ]; do shift; done\nshift\nexec \"$@\"\n");
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(shim, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var field = typeof(Ready).GetField("fenceExecutable", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        var original = field.GetValue(null);
        field.SetValue(null, shim);
        try
        {
            await lab.Backend.CreateAsync(launch, ct);
            // Only a configuration-free Git lookup discovers and grants the real common folder.
            var arguments = File.ReadAllLines(argv);
            var common = Path.Combine(repo.Root, ".git");
            var bind = Array.IndexOf(arguments, common);
            await Assert.That(bind).IsGreaterThan(0);
            await Assert.That(arguments[(bind - 1)..(bind + 2)]).IsEquivalentTo(new[] { "--bind", common, common }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        }
        finally { field.SetValue(null, original); }
    }

    [Test]
    [Timeout(30_000)]
    public async Task Fenced_tasks_have_private_keys_and_release_disposes_only_their_server(CancellationToken ct)
    {
        if (Ready.FenceKind is null) Skip.Test("opencode runs unfenced on Windows.");
        if (await Ready.FenceAsync(ct) is { } refusal) Skip.Test(refusal);
        await using var lab = new OpenCodeLab();
        lab.Add("POST /api/session", OpenCodeLab.Session("worker"), repeat: true);
        lab.Add("PUT /api/session/worker/environment", status: 204, repeat: true);
        lab.Add("DELETE /api/session/worker", status: 204, repeat: true);
        lab.Write();
        var first = (await lab.Backend.CreateAsync(Launch(lab, "first"), ct)).Worker;
        var firstPid = lab.Hosts().Single()["pid"]!.GetValue<int>();
        var second = (await lab.Backend.CreateAsync(Launch(lab, "second"), ct)).Worker;
        await Assert.That(new[] { first.State["host"]!.GetValue<string>(), second.State["host"]!.GetValue<string>() })
            .IsEquivalentTo(new[] { "fence=run/first", "fence=run/second" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(lab.Hosts().Count).IsEqualTo(2);
        var secondPid = lab.Hosts().Single(x => x["pid"]!.GetValue<int>() != firstPid)["pid"]!.GetValue<int>();
        var firstServer = ObservedProcess.Capture(firstPid);
        var secondServer = ObservedProcess.Capture(secondPid);
        await lab.Backend.ReleaseAsync(first, ct);
        await Assert.That(firstServer.IsAlive()).IsFalse();
        await Assert.That(secondServer.IsAlive()).IsTrue();
        await lab.Backend.ReleaseAsync(second, ct);
        await Assert.That(secondServer.IsAlive()).IsFalse();
    }

    [Test]
    [Timeout(30_000)]
    public async Task Clean_profile_server_uses_generated_root_and_overrides_inherited_config(CancellationToken ct)
    {
        await using var lab = new OpenCodeLab();
        var global = Directory.CreateDirectory(Path.Combine(lab.Tree.Root, "global-config")).FullName;
        File.WriteAllText(Path.Combine(global, "opencode.json"), """
            {"providers":{"fixture":{"settings":{"endpoint":"fixture"},"models":{"model":{"name":"old"},"unused":{}}},"unused":{}},"instructions":["ignore"],"mcp":{"servers":{}}}
            """);
        File.WriteAllText(Path.Combine(global, "opencode.jsonc"), """
            { // Higher-priority model definition.
              "providers":{"fixture":{"models":{"model":{"name":"selected"},"other":{}}}},
              "shell":"fixture-shell",
            }
            """);
        lab.SetEnvironment("OPENCODE_CONFIG_DIR", global);
        lab.SetEnvironment("OPENCODE_CONFIG", "inherited-config");
        lab.SetEnvironment("OPENCODE_CONFIG_CONTENT", "{\"instructions\":[\"inherited\"]}");
        lab.Create("worker");
        lab.Write();

        var launch = Launch(lab, "worker", "clean");
        var variables = new Dictionary<string, string>(launch.Environment)
        {
            ["OPENCODE_CONFIG_DIR"] = global, ["OPENCODE_CONFIG"] = "inherited-config",
            ["OPENCODE_CONFIG_CONTENT"] = "{\"instructions\":[\"inherited\"]}",
            ["OPENCODE_DISABLE_FILEWATCHER"] = "0",
        };
        await lab.Backend.CreateAsync(launch with { Environment = variables }, ct);

        var host = lab.Hosts().Single();
        var root = host["configRoot"]?.GetValue<string>();
        await Assert.That(root).IsNotEqualTo(global);
        await Assert.That(root).IsNotNull();
        await Assert.That(root!.EndsWith(Path.Combine("profiles", "opencode"), StringComparison.Ordinal)).IsTrue();
        await Assert.That(host["directory"]!.GetValue<string>()).IsEqualTo(root);
        var env = host["environment"]!;
        await Assert.That(env["OPENCODE_CONFIG"]!.GetValue<string>()).IsEqualTo("");
        await Assert.That(env["OPENCODE_CONFIG_CONTENT"]!.GetValue<string>()).IsEqualTo("{}");
        await Assert.That(env["OPENCODE_DISABLE_CLAUDE_CODE"]!.GetValue<string>()).IsEqualTo("1");
        await Assert.That(env["OPENCODE_FILEWATCHER_DISABLE"]!.GetValue<string>()).IsEqualTo("1");
        await Assert.That(env["OPENCODE_DISABLE_FILEWATCHER"]!.GetValue<string>()).IsEqualTo("1");
        var config = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "opencode.json")))!;
        var expected = JsonNode.Parse("""
            {"$schema":"https://opencode.ai/config.json","claude":false,"plugins":["-opencode.config.compatibility"],"providers":{"fixture":{"settings":{"endpoint":"fixture"},"models":{"model":{"name":"selected"}}}},"shell":"fixture-shell"}
            """);
        await Assert.That(JsonNode.DeepEquals(config, expected)).IsTrue();
    }

    [Test]
    [Timeout(30_000)]
    public async Task Project_off_server_overrides_both_project_config_variables(CancellationToken ct)
    {
        await using var lab = new OpenCodeLab();
        lab.SetEnvironment("OPENCODE_CONFIG_PROJECT_DISABLE", "0");
        lab.SetEnvironment("OPENCODE_DISABLE_PROJECT_CONFIG", "0");
        lab.Create("worker");
        lab.Write();

        await lab.Backend.CreateAsync(Launch(lab, "worker", project: "off"), ct);

        var env = lab.Hosts().Single()["environment"]!;
        await Assert.That(env["OPENCODE_CONFIG_PROJECT_DISABLE"]!.GetValue<string>()).IsEqualTo("1");
        await Assert.That(env["OPENCODE_DISABLE_PROJECT_CONFIG"]!.GetValue<string>()).IsEqualTo("1");
    }

    [Test]
    [Arguments("clean", "off", "full", "on")]
    [Arguments("full", "off", "full", "on")]
    [Arguments("clean", "off", "clean", "on")]
    [Timeout(30_000)]
    public async Task Distinct_profile_project_pairs_get_private_hosts_and_matching_pairs_reuse_them(
        string firstProfile, string firstProject, string secondProfile, string secondProject, CancellationToken ct)
    {
        await using var lab = new OpenCodeLab();
        lab.SetEnvironment("OPENCODE_CONFIG_DIR", Path.Combine(lab.Tree.Root, "global-config"));
        lab.SetEnvironment("OPENCODE_CONFIG", "inherited-config");
        lab.SetEnvironment("OPENCODE_CONFIG_CONTENT", "{}");
        lab.SetEnvironment("OPENCODE_DISABLE_CLAUDE_CODE", "0");
        lab.SetEnvironment("OPENCODE_CONFIG_PROJECT_DISABLE", "0");
        lab.SetEnvironment("OPENCODE_DISABLE_PROJECT_CONFIG", "0");
        lab.Write();
        var first = Launch(lab, "first", firstProfile, firstProject);
        var second = Launch(lab, "second", secondProfile, secondProject);
        WorkerPlan Plan(WorkerLaunch launch) => new(launch.TaskId, launch.Kind, launch.Route, launch.Agent,
            launch.Persona, launch.Modes, launch.Directory);

        await lab.Backend.ValidateAsync([Plan(first), Plan(second), Plan(first)], ct);

        var hosts = lab.Hosts();
        await Assert.That(hosts.Count).IsEqualTo(2);
        await Assert.That(hosts.Select(x => x["pid"]!.GetValue<int>()).Distinct().Count()).IsEqualTo(2);
        foreach (var launch in new[] { first, second })
        {
            var host = hosts.Single(x => x["environment"]!["OPENCODE_DISABLE_CLAUDE_CODE"]!.GetValue<string>() == (launch.Modes.Profile == "clean" ? "1" : "0")
                && x["environment"]!["OPENCODE_DISABLE_PROJECT_CONFIG"]!.GetValue<string>() == (launch.Modes.Project == "off" ? "1" : "0"));
            if (launch.Modes.Profile == "full")
                await Assert.That(host["environment"]!["OPENCODE_CONFIG"]!.GetValue<string>()).IsEqualTo("inherited-config");
        }
    }

    [Test]
    [Timeout(30_000)]
    public async Task Mode_denies_follow_escalations_and_report_only_the_enforcement_applied(CancellationToken ct)
    {
        await using var lab = new OpenCodeLab();
        var cases = new[]
        {
            (Access: "read-only", Network: "off", AccessEnforcement: "policy", NetworkEnforcement: "policy"),
            (Access: "read-only", Network: "on", AccessEnforcement: "policy", NetworkEnforcement: "none"),
            (Access: "workspace", Network: "off", AccessEnforcement: "none", NetworkEnforcement: "policy"),
            (Access: "workspace", Network: "on", AccessEnforcement: "none", NetworkEnforcement: "none"),
            (Access: "full", Network: "off", AccessEnforcement: "none", NetworkEnforcement: "policy"),
            (Access: "full", Network: "on", AccessEnforcement: "none", NetworkEnforcement: "none"),
        };
        foreach (var item in cases) lab.Create(item.Access + "-" + item.Network);
        lab.Write();
        foreach (var item in cases)
        {
            var launch = Launch(lab, item.Access + "-" + item.Network, access: item.Access, network: item.Network) with
            {
                Rules = new([], new[] { "shell", "edit", "webfetch", "websearch" }.Select(action =>
                    new EffectiveRule { Name = "task/ask-" + action, Action = action, Resources = ["*"] }).ToList()),
            };
            var created = await lab.Backend.CreateAsync(launch, ct);

            await Assert.That(created.Applied.Access).IsEqualTo(Ready.Fenced(launch.Modes) ? "sandbox" : item.AccessEnforcement);
            await Assert.That(created.Applied.Network).IsEqualTo(item.NetworkEnforcement);
            await Assert.That(created.Applied.Rules).IsEqualTo("policy");
            foreach (var action in new[] { "edit", "webfetch", "websearch" })
            {
                var modeDeny = action == "edit" ? item.Access == "read-only" : item.Network == "off";
                var matching = created.Details!.Rules.Where(x => x.StartsWith(action + " * ", StringComparison.Ordinal)).ToList();
                await Assert.That(matching.Last()).IsEqualTo($"{action} * {(modeDeny ? "deny" : "ask")}");
            }
        }
    }

    [Test]
    [Timeout(30_000)]
    public async Task Hard_stop_of_an_unshared_host_kills_server_children_without_removing_session(CancellationToken ct)
    {
        await using var lab = new OpenCodeLab();
        var records = Path.GetDirectoryName(FakeOpenCode.Record(lab.Tree))!;
        lab.SetEnvironment("FAKE_OPENCODE_CHILD_RECORD", Path.Combine(records, "children"));
        lab.Create("worker");
        lab.Add("POST /api/session/worker/interrupt", new JsonObject { ["interrupted"] = false }, repeat: true);
        lab.Write();
        try
        {
            var worker = (await lab.Backend.CreateAsync(Launch(lab, "worker"), ct)).Worker;
            var pid = lab.Hosts().Single()["pid"]!.GetValue<int>();
            var descendants = JsonNode.Parse(File.ReadAllText(Path.Combine(records, $"children.{pid}.json")))!;
            using var server = System.Diagnostics.Process.GetProcessById(pid);
            var (child, grandchild) = ObservedProcess.Descendants(descendants);
            await lab.Backend.InterruptAsync(worker, InterruptKind.Timeout, ct);
            var stopped = await lab.Backend.InterruptAsync(worker, InterruptKind.Hard, ct);

            await Assert.That(stopped.Stopped).IsTrue();
            await Assert.That(stopped.Detail).IsEqualTo("tree-kill");
            await Assert.That(server.HasExited).IsTrue();
            var deadline = System.Diagnostics.Stopwatch.StartNew();
            while ((child.IsAlive() || grandchild.IsAlive()) && deadline.Elapsed < TimeSpan.FromSeconds(5))
                await Task.Delay(20, ct);
            await Assert.That(child.IsAlive()).IsFalse();
            await Assert.That(grandchild.IsAlive()).IsFalse();
            var requests = File.ReadLines(FakeOpenCode.Record(lab.Tree)).Select(x => JsonNode.Parse(x)!).ToList();
            await Assert.That(requests.Count(x => x["method"]!.GetValue<string>() == "DELETE")).IsEqualTo(0);
        }
        finally
        {
            await ObservedProcess.CleanupRecordedChildrenAsync(records);
        }
    }

    [Test]
    [Arguments(200)]
    [Arguments(500)]
    [Timeout(30_000)]
    public async Task Hard_stop_of_a_shared_host_interrupts_without_killing_other_live_workers(int status, CancellationToken ct)
    {
        await using var lab = new OpenCodeLab();
        lab.Create("worker");
        lab.Create("peer");
        lab.Add("POST /api/session/worker/interrupt", status == 200 ? new JsonObject { ["interrupted"] = false }
            : new JsonObject { ["_tag"] = "InternalError", ["message"] = "interrupt failed" }, status, repeat: true);
        lab.Prompt("peer");
        lab.Write();
        var worker = (await lab.Backend.CreateAsync(Launch(lab, "worker", access: "full"), ct)).Worker;
        var peer = (await lab.Backend.CreateAsync(Launch(lab, "peer", access: "full"), ct)).Worker;
        using var server = System.Diagnostics.Process.GetProcessById(lab.Hosts().Single()["pid"]!.GetValue<int>());

        await lab.Backend.InterruptAsync(worker, InterruptKind.Timeout, ct);
        var stopped = await lab.Backend.InterruptAsync(worker, InterruptKind.Hard, ct);

        await Assert.That(stopped.Stopped).IsFalse();
        await Assert.That(stopped.Detail).IsEqualTo("interrupt");
        await Assert.That(server.HasExited).IsFalse();
        var prompted = await lab.Backend.PromptAsync(peer, new("Inspect.", false), ct);
        await Assert.That(prompted.Turn).IsEqualTo("turn");
        var requests = File.ReadLines(FakeOpenCode.Record(lab.Tree)).Select(x => JsonNode.Parse(x)!).ToList();
        await Assert.That(requests.Count(x => x["path"]!.GetValue<string>() == "/api/session/worker/interrupt")).IsEqualTo(2);
        await Assert.That(requests.Count(x => x["method"]!.GetValue<string>() == "DELETE")).IsEqualTo(0);
    }

    [Test]
    [Timeout(30_000)]
    public async Task Concurrent_hard_stops_confirm_the_same_kill_without_restarting_the_host(CancellationToken ct)
    {
        await using var lab = new OpenCodeLab();
        lab.Create("worker");
        lab.Add("POST /api/session/worker/interrupt", new JsonObject { ["interrupted"] = false }, repeat: true);
        lab.Write();
        var worker = (await lab.Backend.CreateAsync(Launch(lab, "worker"), ct)).Worker;

        var results = await Task.WhenAll(lab.Backend.InterruptAsync(worker, InterruptKind.Hard, ct),
            lab.Backend.InterruptAsync(worker, InterruptKind.Hard, ct));

        await Assert.That(results.Select(x => x.Stopped)).IsEquivalentTo([true, true]);
        await Assert.That(lab.Hosts().Count).IsEqualTo(1);
    }

    static WorkerLaunch Launch(OpenCodeLab lab, string id, string profile = "full", string project = "on",
        string access = "workspace", string network = "on") => new("run", id, "research", new("fixture", "model", null),
        null, "build", new(profile, project, access, network, "worktree"), lab.Tree.Repository,
        new([], []), lab.Tree.ChildEnvironment().ToDictionary(x => x.Key, x => x.Value!), null, profile, null);
}
