using System.Text.Json.Nodes;
using Dispatch.Tests.Support;
using TUnit.Assertions.Enums;

namespace Dispatch.Tests.Integration;

[Category("Integration")]
[NotInParallel]
public sealed class OpenCodeSeamTests
{
    [Test]
    [Timeout(30_000)]
    public async Task Reattached_continue_uses_run_scoped_temp(CancellationToken ct)
    {
        await using var lab = new OpenCodeLab();
        lab.Add("GET /api/session/worker", OpenCodeLab.Session("worker", "interrupted"));
        lab.Add("PUT /api/session/worker/environment", status: 204);
        lab.Prompt("worker", "new-turn", 10);
        lab.Write();
        var worker = new WorkerRef("opencode", "inspect", "worker", "old-turn", new() { ["promptCreated"] = 2d });
        await lab.Backend.ReattachAsync(worker, ct);
        await lab.Backend.PromptAsync(worker, new("Continue.", true), ct);
        var request = File.ReadLines(Path.Combine(lab.Tree.Root, "requests.jsonl")).Select(x => JsonNode.Parse(x)!)
            .Single(x => x["method"]!.GetValue<string>() == "PUT");
        var expected = Path.Combine(lab.Tree.WorktreeRoot, "run", ".tmp", "inspect");
        foreach (var name in new[] { "TEMP", "TMP", "TMPDIR" })
            await Assert.That(request["body"]!["variables"]![name]!.GetValue<string>()).IsEqualTo(expected);
        await Assert.That(Directory.Exists(Path.Combine(lab.Tree.WorktreeRoot, ".dispatch-tmp", "inspect"))).IsFalse();
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    [Timeout(30_000)]
    public async Task Continue_stale_outcome_without_current_end_marker_does_not_settle(bool missingIdle, CancellationToken ct)
    {
        await using var lab = new OpenCodeLab();
        lab.Add("GET /api/session/worker", OpenCodeLab.Session("worker", "interrupted"));
        lab.Add("PUT /api/session/worker/environment", status: 204);
        lab.Prompt("worker", "new-turn", 10);
        lab.Add("GET /api/session/active", new JsonObject { ["data"] = new JsonObject() }, repeat: true);
        lab.Add("GET /api/permission/request", new JsonObject { ["location"] = new JsonObject { ["directory"] = "$directory" }, ["data"] = new JsonArray() }, repeat: true);
        lab.Add("GET /api/form", new JsonObject { ["location"] = new JsonObject { ["directory"] = "$directory" }, ["data"] = new JsonArray() }, repeat: true);
        var stale = OpenCodeLab.Session("worker", "succeeded", 3);
        if (missingIdle) stale["data"]!["time"]!.AsObject().Remove("idle");
        lab.Add("GET /api/session/worker", stale);
        lab.Add("GET /api/session/worker", OpenCodeLab.Session("worker", "succeeded", 11), repeat: true);
        lab.Write();
        var old = new WorkerRef("opencode", "task", "worker", "old-turn", new() { ["promptCreated"] = 2d });
        await lab.Backend.ReattachAsync(old, ct);
        var current = await lab.Backend.PromptAsync(old, new("Continue.", true), ct);
        await lab.Backend.PollAsync([current], ct);
        await lab.Backend.PollAsync([current], ct);
        var premature = new List<WorkerSignal>();
        while (lab.Backend.Signals.TryRead(out var signal)) premature.Add(signal);
        await Assert.That(premature.OfType<WorkerSignal.Ended>().Any()).IsFalse();
        await lab.Backend.PollAsync([current], ct);
        var ended = (WorkerSignal.Ended)await lab.ReadAsync(x => x is WorkerSignal.Ended, ct);
        await Assert.That(ended.Turn).IsEqualTo("new-turn");
        await Assert.That(ended.Outcome).IsEqualTo("succeeded");
    }

    [Test]
    [Timeout(30_000)]
    public async Task Completion_poll_drains_already_received_execution_events_before_ended(CancellationToken ct)
    {
        await using var lab = new OpenCodeLab();
        lab.Add("GET /api/session/worker", OpenCodeLab.Session("worker", "succeeded"), repeat: true);
        var child = OpenCodeLab.Session("child");
        child["data"]!["parentID"] = "worker";
        var ownerRead = lab.Add("GET /api/session/child", child);
        var arrived = Path.Combine(lab.Tree.Root, "owner-read");
        var release = Path.Combine(lab.Tree.Root, "release");
        ownerRead["arrived"] = arrived;
        ownerRead["waitFor"] = release;
        var trigger = lab.Add("POST /api/session/worker/interrupt", new JsonObject { ["interrupted"] = false });
        trigger["events"] = new JsonArray(
            OpenCodeLab.Event("permission.asked", new() { ["sessionID"] = "child", ["id"] = "child-ask", ["action"] = "read", ["resources"] = new JsonArray("README.md") }),
            OpenCodeLab.Event("session.execution.succeeded", new() { ["sessionID"] = "worker" }));
        lab.Add("GET /api/session/active", new JsonObject { ["data"] = new JsonObject() });
        lab.Add("GET /api/permission/request", new JsonObject { ["location"] = new JsonObject { ["directory"] = "$directory" }, ["data"] = new JsonArray() });
        lab.Add("GET /api/form", new JsonObject { ["location"] = new JsonObject { ["directory"] = "$directory" }, ["data"] = new JsonArray() });
        lab.Write();
        var worker = new WorkerRef("opencode", "task", "worker", "turn", new() { ["promptCreated"] = 2d });
        await lab.Backend.ReattachAsync(worker, ct);
        await lab.Backend.InterruptAsync(worker, InterruptKind.Cancel, ct);
        while (!File.Exists(arrived)) { ct.ThrowIfCancellationRequested(); await Task.Yield(); }
        var polling = lab.Backend.PollAsync([worker], ct);
        var overtook = await Task.WhenAny(polling, Task.Delay(TimeSpan.FromSeconds(1), ct)) == polling;
        await File.WriteAllTextAsync(release, "release", ct);
        await polling;
        var sequence = new List<WorkerSignal>();
        while (await lab.Backend.Signals.ReadAsync(ct) is { } signal)
        {
            sequence.Add(signal);
            if (signal is WorkerSignal.Status) break;
        }
        while (lab.Backend.Signals.TryRead(out var signal)) sequence.Add(signal);

        await Assert.That(overtook).IsFalse();
        await Assert.That(sequence.FindIndex(x => x is WorkerSignal.Status)).IsLessThan(sequence.FindIndex(x => x is WorkerSignal.Ended));
    }

    [Test]
    [Timeout(30_000)]
    public async Task Delayed_raw_status_is_attributed_to_its_stored_turn_not_the_continue_turn(CancellationToken ct)
    {
        await using var lab = new OpenCodeLab();
        lab.Add("GET /api/session/worker", OpenCodeLab.Session("worker", "interrupted"));
        lab.Add("PUT /api/session/worker/environment", status: 204);
        lab.Prompt("worker", "new-turn", 10);
        var trigger = lab.Add("POST /api/session/worker/interrupt", new JsonObject { ["interrupted"] = false });
        trigger["events"] = new JsonArray(
            OpenCodeLab.Event("session.execution.succeeded", new() { ["sessionID"] = "worker" }, 3),
            OpenCodeLab.Event("session.execution.succeeded", new() { ["sessionID"] = "worker" }, 11),
            OpenCodeLab.Event("permission.asked", new() { ["sessionID"] = "worker", ["id"] = "marker", ["action"] = "read", ["resources"] = new JsonArray("README.md") }, 11));
        lab.Write();
        var old = new WorkerRef("opencode", "task", "worker", "old-turn", new() { ["promptCreated"] = 2d });
        await lab.Backend.ReattachAsync(old, ct);
        var current = await lab.Backend.PromptAsync(old, new("Continue.", true), ct);

        await lab.Backend.InterruptAsync(current, InterruptKind.Cancel, ct);
        var statuses = new List<WorkerSignal.Status>();
        while (await lab.Backend.Signals.ReadAsync(ct) is { } signal)
        {
            if (signal is WorkerSignal.Status status) statuses.Add(status);
            if (signal is WorkerSignal.Ask { BlockerRef.AskId: "marker" }) break;
        }

        await Assert.That(statuses.Select(x => x.Turn)).IsEquivalentTo(["old-turn", "new-turn"]);
    }

    [Test]
    [Timeout(30_000)]
    public async Task Post_prompt_blocker_sweep_does_not_overtake_delayed_execution_status(CancellationToken ct)
    {
        await using var lab = new OpenCodeLab();
        lab.Create("worker");
        lab.Prompt("worker");
        lab.Add("GET /api/session/active", new JsonObject { ["data"] = new JsonObject() }, repeat: true);
        lab.Add("GET /api/session/worker", OpenCodeLab.Session("worker", "succeeded"), repeat: true);
        lab.Add("GET /api/permission/request", new JsonObject { ["location"] = new JsonObject { ["directory"] = "$directory" }, ["data"] = new JsonArray() }, repeat: true);
        lab.Add("GET /api/form", new JsonObject { ["location"] = new JsonObject { ["directory"] = "$directory" }, ["data"] = new JsonArray() }, repeat: true);
        var trigger = lab.Add("POST /api/session/worker/interrupt", new JsonObject { ["interrupted"] = false });
        trigger["events"] = new JsonArray(OpenCodeLab.Event("session.execution.succeeded", new() { ["sessionID"] = "worker" }));
        lab.Write();
        var worker = (await lab.CreateAsync("worker", ct)).Worker;
        worker = await lab.Backend.PromptAsync(worker, new("Inspect.", false), ct);

        await lab.Backend.PollAsync([worker], ct);
        var premature = false;
        while (lab.Backend.Signals.TryRead(out var signal)) premature |= signal is WorkerSignal.Ended;
        await Assert.That(premature).IsFalse();
        await lab.Backend.InterruptAsync(worker, InterruptKind.Cancel, ct);
        await lab.ReadAsync(x => x is WorkerSignal.Status, ct);
        await lab.Backend.PollAsync([worker], ct);
        var ended = (WorkerSignal.Ended)await lab.ReadAsync(x => x is WorkerSignal.Ended, ct);
        await Assert.That(ended.Outcome).IsEqualTo("succeeded");
    }

    [Test]
    [Timeout(30_000)]
    public async Task Unadmitted_workers_events_do_not_block_other_workers_events(CancellationToken ct)
    {
        await using var lab = new OpenCodeLab();
        lab.Create("slow");
        lab.Create("fast");
        lab.Prompt("fast");
        var release = Path.Combine(lab.Tree.Root, "release");
        var prompt = lab.Prompt("slow");
        prompt["waitFor"] = release;
        prompt["beforeEvents"] = new JsonArray(
            OpenCodeLab.Event("permission.asked", new() { ["sessionID"] = "slow", ["id"] = "early", ["action"] = "read", ["resources"] = new JsonArray("README.md") }),
            OpenCodeLab.Event("session.status", new() { ["sessionID"] = "slow", ["status"] = new JsonObject { ["type"] = "retry", ["attempt"] = 1, ["message"] = "retry", ["next"] = 4 } }),
            OpenCodeLab.Event("permission.asked", new() { ["sessionID"] = "fast", ["id"] = "ready", ["action"] = "read", ["resources"] = new JsonArray("README.md") }));
        prompt["events"] = new JsonArray(OpenCodeLab.Event("permission.asked", new() { ["sessionID"] = "slow", ["id"] = "marker", ["action"] = "read", ["resources"] = new JsonArray("README.md") }));
        lab.Write();
        var slow = (await lab.CreateAsync("slow", ct)).Worker;
        var fast = (await lab.CreateAsync("fast", ct)).Worker;
        await lab.Backend.PromptAsync(fast, new("Inspect.", false), ct);
        var prompting = lab.Backend.PromptAsync(slow, new("Inspect.", false), ct);
        var progressed = false;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(2));
        try { await lab.ReadAsync(x => x is WorkerSignal.Ask { TaskId: "fast" }, deadline.Token); progressed = true; }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { /* Observation window ended. */ }
        finally { await File.WriteAllTextAsync(release, "release", ct); }
        var admitted = await prompting;

        await Assert.That(progressed).IsTrue();
        var early = (WorkerSignal.Ask)await lab.ReadAsync(x => x is WorkerSignal.Ask { TaskId: "slow" }, ct);
        await Assert.That(early.Turn).IsEqualTo(admitted.Turn);
        var retries = new List<WorkerSignal.Retry>();
        while (await lab.Backend.Signals.ReadAsync(ct) is { } signal)
        {
            if (signal is WorkerSignal.Retry retry) retries.Add(retry);
            if (signal is WorkerSignal.Ask { BlockerRef.AskId: "marker" }) break;
        }
        await Assert.That(retries.Select(x => x.Turn)).IsEquivalentTo([admitted.Turn!]);
    }

    [Test]
    [Timeout(30_000)]
    public async Task Failed_create_cleanup_keeps_the_worker_reference_and_never_finalizes_live_work(CancellationToken ct)
    {
        using var core = new CoreLab();
        var run = await core.TaskAsync("inspect", ct);
        await using var lab = new OpenCodeLab(core.Tree);
        lab.Add("POST /api/session", OpenCodeLab.Session("worker"));
        lab.Add("PUT /api/session/worker/environment", new JsonObject { ["_tag"] = "InternalError", ["message"] = "environment failed" }, 500);
        lab.Add("DELETE /api/session/worker", new JsonObject { ["_tag"] = "InternalError", ["message"] = "release failed" }, 500, repeat: true);
        lab.Add("POST /api/session/worker/interrupt", new JsonObject { ["interrupted"] = true });
        lab.Write();
        var scheduler = new Scheduler(new Dictionary<string, IWorkerBackend> { ["opencode"] = lab.Backend },
            core.Ledger, core.Tree.Repository, AllowList.From(null), core.Policy, 1, ct, core.Git);

        var exit = await scheduler.RunAsync([run]);

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(core.Ledger.LoadRecord(run.Id).Session.Id).IsEqualTo("worker");
        await Assert.That(run.State).IsEqualTo(TaskState.Dispatching);
        await Assert.That(core.Ledger.Read().Any(x => x.Type == "final")).IsFalse();
    }

    [Test]
    [Timeout(30_000)]
    public async Task Calls_include_token_assistants_across_pages_and_continuations(CancellationToken ct)
    {
        await using var lab = new OpenCodeLab();
        lab.Add("GET /api/session/worker", OpenCodeLab.Session("worker", "interrupted"));
        lab.Add("PUT /api/session/worker/environment", status: 204);
        lab.Prompt("worker", "new-turn", 10);
        var session = OpenCodeLab.Session("worker", "succeeded", 12);
        session["data"]!["tokens"]!["input"] = 900;
        session["data"]!["cost"] = 1.25;
        lab.Add("GET /api/session/worker", session, repeat: true);
        var newest = Assistant("answer", "Current final", 2, 0, 3, 0);
        var tokenless = Assistant("pending", "", 1, 1, 1, 1);
        tokenless.Remove("tokens");
        tokenless["model"]!["id"] = "old-model";
        var prompt = JsonNode.Parse("""{"id":"new-turn","type":"user","time":{"created":10},"text":"Continue."}""")!;
        var first = lab.Add("GET /api/session/worker/message", new JsonObject
        {
            ["data"] = new JsonArray(newest, prompt, tokenless), ["cursor"] = new JsonObject { ["next"] = "older" },
        });
        first["query"] = new JsonObject { ["order"] = "desc" };
        var oldIdle = JsonNode.Parse("""{"id":"old-idle","type":"idle","time":{"created":3},"outcome":"failed","error":{"type":"fixture","message":"old error"}}""")!;
        var second = lab.Add("GET /api/session/worker/message", new JsonObject
        {
            ["data"] = new JsonArray(oldIdle, Assistant("zero", "Zero call", 0, 0, 0, 0), Assistant("old", "Old final", 17, 3, 7, 2)),
            ["cursor"] = new JsonObject(),
        });
        second["query"] = new JsonObject { ["cursor"] = "older" };
        lab.Steps.Add(first.DeepClone().AsObject());
        lab.Steps.Add(second.DeepClone().AsObject());
        // The existing settlement reread must rebuild calls, not append them again.
        newest["content"]![0]!["text"] = "";
        lab.Write();
        var worker = new WorkerRef("opencode", "task", "worker", "old-turn", new() { ["promptCreated"] = 2d });
        await lab.Backend.ReattachAsync(worker, ct);
        worker = await lab.Backend.PromptAsync(worker, new("Continue.", true), ct);
        var report = await lab.Backend.CollectAsync(worker, ct);
        // input counts every prompt token: uncached, cache read and cache write (each fixture call writes 8).
        await Assert.That(report.Calls).IsNotNull();
        await Assert.That(report.Calls!.Select(x => (x.Input, x.CacheRead, x.Output, x.Reasoning)).ToArray())
            .IsEquivalentTo(new (double?, double?, double?, double?)[] { (28, 3, 7, 2), (8, 0, 0, 0), (10, 0, 3, 0) }, CollectionOrdering.Matching);
        await Assert.That(report.FinalText).IsEqualTo("Current final");
        await Assert.That(report.ExecutionError).IsNull();
        await Assert.That(report.Complete).IsTrue();
        await Assert.That(report.UsageComplete).IsTrue();
        await Assert.That(report.RoutesUsed).IsEquivalentTo(new[] { "fixture/model#default" }, CollectionOrdering.Matching);
        await Assert.That(report.Usage.Input).IsEqualTo(900);
        await Assert.That(report.Cost).IsEqualTo(1.25);

        static JsonObject Assistant(string id, string text, int input, int cached, int output, int reasoning) => new()
        {
            ["id"] = id, ["type"] = "assistant", ["time"] = new JsonObject { ["created"] = 2, ["completed"] = 3 },
            ["agent"] = "build", ["model"] = new JsonObject { ["id"] = "model", ["providerID"] = "fixture" },
            ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text }),
            ["tokens"] = new JsonObject { ["input"] = input, ["output"] = output, ["reasoning"] = reasoning,
                ["cache"] = new JsonObject { ["read"] = cached, ["write"] = 8 } },
        };
    }

    [Test]
    [Arguments("succeeded")]
    [Arguments("failed")]
    [Timeout(30_000)]
    public async Task An_earlier_model_call_error_is_recovered_only_when_the_session_succeeded(string outcome, CancellationToken ct)
    {
        await using var lab = new OpenCodeLab();
        lab.Add("GET /api/session/worker", OpenCodeLab.Session("worker", outcome, 12), repeat: true);
        var dropped = Assistant("dropped", "", 5, 0, 1, 0);
        dropped.Remove("tokens");
        dropped["error"] = JsonNode.Parse("""{"type":"fixture","message":"WebSocket closed with code 1012"}""");
        var prompt = JsonNode.Parse("""{"id":"turn","type":"user","time":{"created":1},"text":"Do it."}""")!;
        var page = lab.Add("GET /api/session/worker/message", new JsonObject
        {
            ["data"] = new JsonArray(Assistant("answer", "Committed.", 2, 0, 3, 0), dropped, prompt), ["cursor"] = new JsonObject(),
        }, repeat: true);
        page["query"] = new JsonObject { ["order"] = "desc" };
        lab.Write();
        var worker = new WorkerRef("opencode", "task", "worker", "turn", new() { ["promptCreated"] = 1d });
        await lab.Backend.ReattachAsync(worker, ct);
        var report = await lab.Backend.CollectAsync(worker, ct);
        await Assert.That(report.FinalText).IsEqualTo("Committed.");
        if (outcome == "succeeded")
        {
            await Assert.That(report.ExecutionError).IsNull();
            await Assert.That(report.RecoveredErrors!).IsEquivalentTo(["WebSocket closed with code 1012"]);
        }
        else
        {
            await Assert.That(report.ExecutionError).IsEqualTo("WebSocket closed with code 1012");
            await Assert.That(report.RecoveredErrors).IsNull();
        }

        static JsonObject Assistant(string id, string text, int input, int cached, int output, int reasoning) => new()
        {
            ["id"] = id, ["type"] = "assistant", ["time"] = new JsonObject { ["created"] = 2, ["completed"] = 3 },
            ["agent"] = "build", ["model"] = new JsonObject { ["id"] = "model", ["providerID"] = "fixture" },
            ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text }),
            ["tokens"] = new JsonObject { ["input"] = input, ["output"] = output, ["reasoning"] = reasoning,
                ["cache"] = new JsonObject { ["read"] = cached, ["write"] = 0 } },
        };
    }

    [Test]
    [Timeout(30_000)]
    public async Task Missing_session_is_already_released(CancellationToken ct)
    {
        await using var lab = new OpenCodeLab();
        var missing = new JsonObject { ["_tag"] = "SessionNotFoundError", ["message"] = "missing" };
        lab.Add("GET /api/session/worker", missing.DeepClone(), 404);
        lab.Add("DELETE /api/session/worker", missing.DeepClone(), 404);
        lab.Write();
        var worker = new WorkerRef("opencode", "task", "worker", "turn", new());

        var result = await lab.Backend.ReattachAsync(worker, ct);
        await lab.Backend.ReleaseAsync(worker, ct);

        await Assert.That(result).IsTypeOf<ReattachResult.Unreadable>();
    }

    [Test]
    [Arguments(200, true)]
    [Arguments(200, false)]
    [Arguments(404, false)]
    [Timeout(30_000)]
    public async Task Interrupt_preserves_server_response_separately_from_confirmed_stop(int status, bool accepted, CancellationToken ct)
    {
        await using var lab = new OpenCodeLab();
        lab.Add("POST /api/session/worker/interrupt", status == 200
            ? new JsonObject { ["interrupted"] = accepted }
            : new JsonObject { ["_tag"] = "SessionNotFoundError", ["message"] = "missing" }, status);
        lab.Write();

        var result = await lab.Backend.InterruptAsync(new("opencode", "task", "worker", "turn", new()), InterruptKind.Cancel, ct);

        var data = System.Text.Json.JsonSerializer.SerializeToNode(result)!;
        await Assert.That(data["Status"]?.GetValue<int>()).IsEqualTo(status);
        await Assert.That(data["Interrupted"]?.GetValue<bool>()).IsEqualTo(status == 200 ? accepted : (bool?)null);
        await Assert.That(result.Stopped).IsFalse();
    }

    [Test]
    [Timeout(30_000)]
    public async Task Worker_poll_failure_does_not_skip_healthy_workers_or_fail_host_health(CancellationToken ct)
    {
        await using var lab = new OpenCodeLab();
        lab.Add("GET /api/session/bad", OpenCodeLab.Session("bad"));
        lab.Add("GET /api/session/good", OpenCodeLab.Session("good", "succeeded"), repeat: true);
        lab.Add("GET /api/session/active", new JsonObject { ["data"] = new JsonObject() }, repeat: true);
        lab.Add("GET /api/permission/request", new JsonObject { ["_tag"] = "InternalError", ["message"] = "bad worker" }, 500);
        lab.Add("GET /api/permission/request", new JsonObject { ["location"] = new JsonObject { ["directory"] = "$directory" }, ["data"] = new JsonArray() }, repeat: true);
        lab.Add("GET /api/form", new JsonObject { ["location"] = new JsonObject { ["directory"] = "$directory" }, ["data"] = new JsonArray() }, repeat: true);
        lab.Write();
        var bad = new WorkerRef("opencode", "bad", "bad", "turn", new());
        var good = new WorkerRef("opencode", "good", "good", "turn", new() { ["promptCreated"] = 2d });
        await lab.Backend.ReattachAsync(bad, ct);
        await lab.Backend.ReattachAsync(good, ct);

        var poll = await lab.Backend.PollAsync([bad, good], ct);

        await Assert.That(poll.Hosts.Any(x => x.State == "disconnected")).IsFalse();
        await Assert.That(await lab.ReadAsync(x => x is WorkerSignal.Fault, ct)).IsTypeOf<WorkerSignal.Fault>();
        await Assert.That(await lab.ReadAsync(x => x is WorkerSignal.Ended { TaskId: "good" }, ct)).IsTypeOf<WorkerSignal.Ended>();
    }

    [Test]
    [Timeout(30_000)]
    public async Task Ask_arriving_during_listing_is_not_retired_by_that_snapshot(CancellationToken ct)
    {
        await using var lab = new OpenCodeLab();
        lab.Add("GET /api/session/worker", OpenCodeLab.Session("worker"), repeat: true);
        lab.Add("GET /api/session/active", new JsonObject { ["data"] = new JsonObject() });
        var release = Path.Combine(lab.Tree.Root, "release");
        var listing = lab.Add("GET /api/permission/request", new JsonObject { ["location"] = new JsonObject { ["directory"] = "$directory" }, ["data"] = new JsonArray() });
        listing["beforeEvents"] = new JsonArray(OpenCodeLab.Event("permission.asked", new()
        { ["sessionID"] = "worker", ["id"] = "new-ask", ["action"] = "shell", ["resources"] = new JsonArray("publish") }));
        listing["waitFor"] = release;
        lab.Add("GET /api/form", new JsonObject { ["location"] = new JsonObject { ["directory"] = "$directory" }, ["data"] = new JsonArray() });
        lab.Write();
        var worker = new WorkerRef("opencode", "task", "worker", "turn", new() { ["promptCreated"] = 2d });
        await lab.Backend.ReattachAsync(worker, ct);

        var polling = lab.Backend.PollAsync([worker], ct);
        try { await lab.ReadAsync(x => x is WorkerSignal.Ask { BlockerRef.AskId: "new-ask" }, ct); }
        finally { await File.WriteAllTextAsync(release, "release", ct); }
        await polling;
        var remaining = new List<WorkerSignal>();
        while (lab.Backend.Signals.TryRead(out var signal)) remaining.Add(signal);

        await Assert.That(remaining.OfType<WorkerSignal.AskClosed>().Any(x => x.AskId == "new-ask")).IsFalse();
    }

    [Test]
    [Timeout(30_000)]
    public async Task Mapping_error_does_not_stop_later_events_or_host_disposal(CancellationToken ct)
    {
        var lab = new OpenCodeLab();
        lab.Create("worker");
        lab.Prompt("worker")["events"] = new JsonArray(
            OpenCodeLab.Event("permission.asked", new() { ["sessionID"] = "child", ["id"] = "bad", ["action"] = "read", ["resources"] = new JsonArray("README.md") }),
            OpenCodeLab.Event("permission.asked", new() { ["sessionID"] = "worker", ["id"] = "good", ["action"] = "read", ["resources"] = new JsonArray("README.md") }));
        lab.Add("GET /api/session/child", new JsonObject { ["data"] = new JsonObject() });
        lab.Write();
        Exception? disposalError = null;
        var stopped = false;
        bool received;
        try
        {
            var worker = (await lab.CreateAsync("worker", ct)).Worker;
            await lab.Backend.PromptAsync(worker, new("Inspect.", false), ct);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(TimeSpan.FromSeconds(2));
            try
            {
                await lab.ReadAsync(x => x is WorkerSignal.Fault, deadline.Token);
                await lab.ReadAsync(x => x is WorkerSignal.Ask { BlockerRef.AskId: "good" }, deadline.Token);
                received = true;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { received = false; }
        }
        finally
        {
            var pid = JsonNode.Parse(File.ReadLines(Path.Combine(lab.Tree.Root, "requests.jsonl")).First())!["pid"]!.GetValue<int>();
            try { await lab.DisposeAsync(); }
            catch (Exception ex) { disposalError = ex; }
            try
            {
                using var process = System.Diagnostics.Process.GetProcessById(pid);
                stopped = process.HasExited;
                if (!stopped) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(ct); }
            }
            catch (ArgumentException) { stopped = true; }
        }
        await Assert.That(disposalError).IsNull();
        await Assert.That(stopped).IsTrue();
        await Assert.That(received).IsTrue();
    }
}
