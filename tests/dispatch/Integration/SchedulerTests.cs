using Dispatch.Tests.Support;
using Microsoft.Extensions.Time.Testing;

namespace Dispatch.Tests.Integration;

[Category("Integration")]
[NotInParallel]
public sealed class SchedulerTests
{
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
        var sent = File.ReadLines(Path.Combine(tree.Root, "requests.jsonl")).Select(x => System.Text.Json.Nodes.JsonNode.Parse(x)!)
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
            ? throw new IOException("release failed") : Task.CompletedTask;
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
