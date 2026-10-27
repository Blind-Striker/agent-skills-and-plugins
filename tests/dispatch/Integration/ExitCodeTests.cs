using Dispatch.Tests.Support;
using System.Text.Json.Nodes;
using TUnit.Assertions.Enums;
using Microsoft.Extensions.Time.Testing;

namespace Dispatch.Tests.Integration;

[Category("Integration")]
[NotInParallel("processes")]
public sealed class ExitCodeTests
{
    const string CacheWarning = "this harness runs from Codex's plugin cache, which keeps one version and may replace it during a run; start it from a stable copy outside that cache";

    [Test]
    [NotInParallel]
    [Timeout(60_000)]
    public async Task Run_zombie_is_task_failure_not_abort_and_clean_force_keeps_the_unconfirmed_home(CancellationToken ct)
    {
        using var core = new CoreLab("run-zombie", recorded: false);
        using var codex = new CodexLab(core.Tree);
        await LabRepo.CreateAsync(core.Tree, ct);
        codex.CodeTask();
        ConfigLayers.Initialize(Args.Parse([]));
        File.WriteAllText(Path.Combine(codex.Environment["CODEX_HOME"], "auth.json"), "{}");
        CodexHome? home = null;
        var backend = new FakeBackend("codex", new("sandbox", "approval-gate", "native", false))
        {
            OnProbe = (_, _) => Task.FromResult(new BackendProbe(true, "fixture", [], [])),
            OnValidate = (plans, _) => Task.FromResult<IReadOnlyList<LiveCheck>>(plans.Select(plan => new LiveCheck(plan.TaskId, [], [], "model-y", "clean", null)).ToArray()),
            OnPoll = (_, _) => Task.FromResult(new PollResult([])),
            OnCollect = (_, _) => Task.FromResult(new WorkerReport("succeeded", "Completed.", new(), null, true, [], null, true)),
            OnRelease = (_, _) => Task.FromResult<InterruptResult?>(new(false, "recorded identity uncertain")),
        };
        backend.OnCreate = (launch, _) =>
        {
            home = CodexHome.Create(new(Path.Combine(Locations.ControlDir(launch.RunId), "profiles"), CodexLab.Executable, new Dictionary<string, string>()),
                launch, codex.Environment["CODEX_HOME"]);
            return Task.FromResult(new CreateResult(new("codex", launch.TaskId, "worker", null, new()
            {
                ["pid"] = Environment.ProcessId, ["codexHome"] = home.Home, ["codexHomeTemporary"] = true,
                ["codexHomeMarkerName"] = home.MarkerName, ["codexHomeMarkerText"] = home.MarkerText,
            }), new("sandbox", "sandbox", "approval-gate")));
        };
        backend.OnPrompt = (input, _) =>
        {
            backend.Emit(new WorkerSignal.Ended(input.Worker.TaskId, "turn", "succeeded", null));
            return Task.FromResult(input.Worker with { Turn = "turn" });
        };
        try
        {
            await Assert.That(await Commands.RunAsync(Args.Parse(["run", codex.Tasks, "--repo", core.Tree.Repository]), ct,
                backendFactory: () => backend, userPathReader: () => null)).IsEqualTo(1);
            var ledger = new Ledger(Commands.CreateRunId("codex-run"));
            var result = ledger.LoadResult("edit")!;
            await Assert.That(result.State).IsEqualTo("failed");
            await Assert.That(result.Failures).IsEquivalentTo(new[] { "zombie" }, CollectionOrdering.Matching);
            await Assert.That(result.WorkerStopped).IsFalse();
            await Assert.That(ledger.Read().Where(line => line.Type == "aborted")).IsEmpty();
            await Assert.That(ledger.Read().Count(line => line.Task == "edit" && line.Type == "final")).IsEqualTo(1);
            var record = ledger.LoadRecord("edit");
            await Assert.That(Directory.Exists(record.Workspace.Path)).IsTrue();
            await Assert.That(Directory.Exists(home!.Home)).IsTrue();
            await Assert.That(await Commands.CleanAsync(Args.Parse(["clean", ledger.RunId, "--force"]), ct)).IsEqualTo(1);
            await Assert.That(Directory.Exists(record.Workspace.Path)).IsFalse();
            await Assert.That(Directory.Exists(home.Home)).IsTrue();
            record.BackendState.Pid = int.MaxValue;
            ledger.SaveRecord(record);
            await Assert.That(await Commands.CleanAsync(Args.Parse(["clean", ledger.RunId]), ct)).IsEqualTo(0);
            await Assert.That(Directory.Exists(home.Home)).IsFalse();
            await Assert.That(ledger.Read().Where(line => line.Type == "aborted")).IsEmpty();
        }
        finally { home?.Dispose(); }
    }

    [Test]
    [Timeout(180_000)]
    public async Task Plugin_cache_warning_uses_the_documented_file_app_entry(CancellationToken ct)
    {
        using var lab = new CodexLab();
        var environment = CatalogCommandTests.Configure(lab, both: false);
        var home = environment["CODEX_HOME"]!;
        Directory.CreateDirectory(Path.Combine(home, "plugins"));
        var cache = Path.Combine(home, "plugins", "cache");
        var target = Path.GetDirectoryName(TestPaths.Harness)!;
        try
        {
            // Make the real harness a cache descendant without copying it or using a real Codex home.
            if (OperatingSystem.IsWindows())
            {
                using var command = new CapturedProcess();
                (await command.RunAsync("cmd", ["/c", "mklink", "/J", cache, target], lab.Tree.Root,
                    lab.Tree.ChildEnvironment(), ct)).RequireSuccess("cache junction fixture");
            }
            else Directory.CreateSymbolicLink(cache, target);
            using var process = new HarnessProcess(lab.Tree);
            var output = await process.RunAsync(["selfcheck", "--backend", "codex"], ct, environment, documentedEntry: true);
            await Assert.That(output.ExitCode).IsEqualTo(0);
            await Assert.That(output.Stdout).Contains("PASS  launch:");
            await Assert.That(output.Stderr.ReplaceLineEndings("\n")).IsEqualTo(CacheWarning + "\n");
        }
        finally
        {
            if (Directory.Exists(cache)) Directory.Delete(cache);
        }
    }

    [Test]
    [NotInParallel]
    public async Task Plugin_cache_source_without_injection_is_under_the_harness()
    {
        using var lab = new CoreLab("source-location");
        var sourceMethod = typeof(Cli).GetMethod("SourceFile", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        var source = (string)sourceMethod.Invoke(null, sourceMethod.GetParameters().Length == 0 ? null : [Type.Missing])!;
        await Assert.That(source.Replace('\\', '/')).EndsWith("/harness/src/Cli/Cli.cs");
        await Assert.That(Path.GetFullPath(source)).IsEqualTo(Path.Combine(TestPaths.Harness, "src", "Cli", "Cli.cs"));
        await Assert.That(await Cli.MainAsync(["status"])).IsEqualTo(0);
    }

    [Test]
    [NotInParallel]
    public async Task Plugin_cache_warning_prefers_the_file_app_entry_path()
    {
        using var lab = new CoreLab("entry-location");
        var saved = WorkerEnvironment.Snapshot();
        var previous = AppContext.GetData("EntryPointFilePath");
        try
        {
            var home = Path.Combine(lab.Tree.Root, "codex");
            Environment.SetEnvironmentVariable("CODEX_HOME", home);
            var harness = Path.Combine(home, "plugins", "cache", "pkg", "harness");
            AppContext.SetData("EntryPointFilePath", Path.Combine(harness, "dispatch.cs"));
            var output = TestContext.Current!.Output;
            var start = output.GetErrorOutput().Length;
            await Assert.That(await Cli.MainAsync(["plan"])).IsEqualTo(2);
            await Assert.That(output.GetErrorOutput()[start..].ReplaceLineEndings("\n")).IsEqualTo(CacheWarning + "\ndispatch: missing task file\n");
        }
        finally
        {
            AppContext.SetData("EntryPointFilePath", previous);
            RestoreEnvironment(saved);
        }
    }

    [Test]
    [NotInParallel]
    [Arguments("empty-home")]
    [Arguments("invalid-source")]
    [Arguments("source-link")]
    [Arguments("home-link")]
    [Arguments("escape-link")]
    public async Task Plugin_cache_warning_resolves_links_and_tolerates_bad_paths(string placement, CancellationToken ct)
    {
        using var lab = new CoreLab("cache-paths");
        var saved = WorkerEnvironment.Snapshot();
        var home = Path.Combine(lab.Tree.Root, "codex");
        var cache = Directory.CreateDirectory(Path.Combine(home, "plugins", "cache")).FullName;
        var source = Path.Combine(cache, "pkg", "harness", "src", "Cli", "Cli.cs");
        var link = Path.Combine(lab.Tree.Root, "alias");
        try
        {
            if (placement == "empty-home")
            {
                home = "";
                source = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex", "plugins", "cache", "pkg", "harness", "src", "Cli", "Cli.cs");
            }
            else if (placement == "invalid-source") source = Path.Combine("bad\0path", "src", "Cli", "Cli.cs");
            else
            {
                var target = placement == "home-link" ? home : placement == "source-link" ? cache
                    : Directory.CreateDirectory(Path.Combine(lab.Tree.Root, "outside")).FullName;
                if (placement == "escape-link") link = Path.Combine(cache, "escape");
                if (OperatingSystem.IsWindows())
                {
                    using var command = new CapturedProcess();
                    (await command.RunAsync("cmd", ["/c", "mklink", "/J", link, target], lab.Tree.Root, lab.Tree.ChildEnvironment(), ct)).RequireSuccess("cache junction fixture");
                }
                else Directory.CreateSymbolicLink(link, target);
                if (placement == "home-link") home = link;
                else source = Path.Combine(link, "pkg", "harness", "src", "Cli", "Cli.cs");
            }
            Environment.SetEnvironmentVariable("CODEX_HOME", home);
            var output = TestContext.Current!.Output;
            var start = output.GetErrorOutput().Length;
            await Assert.That(await Cli.MainAsync(["plan"], source)).IsEqualTo(2);
            await Assert.That(output.GetErrorOutput()[start..].ReplaceLineEndings("\n")).IsEqualTo(
                (placement is "invalid-source" or "escape-link" ? "" : CacheWarning + "\n") + "dispatch: missing task file\n");
        }
        finally
        {
            RestoreEnvironment(saved);
            if (Directory.Exists(link)) Directory.Delete(link);
        }
    }

    [Test]
    [NotInParallel]
    public async Task Wait_reads_final_events_after_the_supervisor_exits(CancellationToken ct)
    {
        using var lab = new CoreLab("wait-race");
        lab.Ledger.Write("zulu", "queued");
        lab.Ledger.Write("alpha", "queued");
        using var held = SupervisorLock.Acquire(lab.Ledger.RunId);
        var checkedLock = false;
        bool SupervisorHeld(string run)
        {
            checkedLock = true;
            // One deterministic supervisor step at the lock observation, not a randomized race.
            File.AppendAllText(Path.Combine(lab.Ledger.Dir, "events.jsonl"),
                "{\"task\":\"zulu\",\"type\":\"final\",\"data\":{\"state\":\"done\"}}\n{\"task\":\"alpha\",\"type\":\"final\",\"data\":{\"state\":\"done\"}}\n");
            held.Dispose();
            return SupervisorLock.IsHeld(run);
        }
        var output = TestContext.Current!.Output;
        var start = output.GetStandardOutput().Length;
        var exit = await Commands.WaitAsync(Args.Parse(["wait", lab.Ledger.RunId]), ct, new FakeTimeProvider(), SupervisorHeld);
        await Assert.That(checkedLock).IsTrue();
        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(output.GetStandardOutput()[start..].ReplaceLineEndings("\n")).IsEqualTo(
            ExpectedStatus(lab.Ledger.RunId, new[] { ("zulu", "done"), ("alpha", "done") }, ""));
    }

    [Test]
    [NotInParallel]
    [Arguments("arrives")]
    [Arguments("missing")]
    [Arguments("timeout")]
    public async Task Wait_gives_a_missing_ledger_two_minutes_inside_its_timeout(string condition, CancellationToken ct)
    {
        using var lab = new CoreLab("2026-01-01-wait-grace", recorded: false);
        var time = new FakeTimeProvider();
        var scheduler = new ConcurrentExclusiveSchedulerPair(TaskScheduler.Default, 1).ExclusiveScheduler;
        var before = Snapshot(lab.Tree.Root);
        var args = Args.Parse(condition == "timeout" ? ["wait", "wait-grace", "--timeout", "1"] : ["wait", "wait-grace"]);
        var waiting = await StartWaitAsync(args, time, scheduler, ct);
        await Assert.That(waiting.IsCompleted).IsFalse();
        await AdvanceAsync(time, scheduler, TimeSpan.FromSeconds(condition == "timeout" ? 59 : 119), ct);
        await Assert.That(waiting.IsCompleted).IsFalse();
        await Assert.That(Snapshot(lab.Tree.Root)).IsEquivalentTo(before, CollectionOrdering.Matching);
        if (condition == "arrives")
        {
            lab.Ledger.Write("zulu", "final", new JsonObject { ["state"] = "done" });
            lab.Ledger.Write("alpha", "final", new JsonObject { ["state"] = "done" });
        }
        var output = TestContext.Current!.Output;
        var start = output.GetStandardOutput().Length;
        await AdvanceAsync(time, scheduler, TimeSpan.FromSeconds(1), ct);
        await Assert.That(waiting.IsCompleted).IsTrue();
        if (condition == "arrives")
        {
            await Assert.That(await waiting).IsEqualTo(0);
            await Assert.That(output.GetStandardOutput()[start..].ReplaceLineEndings("\n")).IsEqualTo(
                ExpectedStatus(lab.Ledger.RunId, new[] { ("zulu", "done"), ("alpha", "done") }, ""));
        }
        else
        {
            await Assert.That(async () => await waiting).ThrowsExactly<HarnessException>()
                .WithMessage($"no ledger for run wait-grace under {Locations.LedgerRoot}");
            await Assert.That(output.GetStandardOutput()[start..]).IsEqualTo("");
            await Assert.That(Snapshot(lab.Tree.Root)).IsEquivalentTo(before, CollectionOrdering.Matching);
        }
    }

    [Test]
    [NotInParallel]
    public async Task Wait_timeout_with_no_states_names_the_cause(CancellationToken ct)
    {
        using var lab = new CoreLab("wait-empty");
        using var held = SupervisorLock.Acquire(lab.Ledger.RunId);
        var time = new FakeTimeProvider();
        var scheduler = new ConcurrentExclusiveSchedulerPair(TaskScheduler.Default, 1).ExclusiveScheduler;
        var output = TestContext.Current!.Output;
        var start = output.GetErrorOutput().Length;
        var waiting = await StartWaitAsync(Args.Parse(["wait", lab.Ledger.RunId, "--timeout", "1"]), time, scheduler, ct);
        await Assert.That(waiting.IsCompleted).IsFalse();
        await AdvanceAsync(time, scheduler, TimeSpan.FromMinutes(1), ct);
        await Assert.That(waiting.IsCompleted).IsTrue();
        await Assert.That(await waiting).IsEqualTo(3);
        await Assert.That(output.GetErrorOutput()[start..].ReplaceLineEndings("\n")).IsEqualTo(
            $"run {lab.Ledger.RunId}: wait timed out; no tasks recorded yet\n");
    }

    [Test]
    public async Task Timeout_outside_wait_is_a_usage_error(CancellationToken ct)
    {
        using var tree = new TempTree();
        using var process = new HarnessProcess(tree);
        var output = await process.RunAsync(["run", "--timeout", "7"], ct);
        await Assert.That(output.ExitCode).IsEqualTo(2);
        await Assert.That(output.Stdout).IsEqualTo("");
        await Assert.That(output.Stderr.ReplaceLineEndings("\n")).IsEqualTo(
            "dispatch: --timeout is only for wait; use timeoutMinutes in the task file or --timeout-minutes for worker timeouts\n");
    }

    [Test]
    [NotInParallel]
    [Arguments("selfcheck")]
    [Arguments("plan")]
    [Arguments("run")]
    [Arguments("status")]
    [Arguments("wait")]
    [Arguments("routes")]
    [Arguments("clean")]
    public async Task Plugin_cache_warning_matches_source_and_command(string command)
    {
        using var lab = new CoreLab("cache-warning");
        var saved = WorkerEnvironment.Snapshot();
        try
        {
            foreach (var explicitHome in new[] { false, true })
                foreach (var placement in new[] { "inside", "lookalike", "unrelated", "case" })
                {
                    var home = explicitHome ? Path.Combine(lab.Tree.Root, "codex-home")
                        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
                    Environment.SetEnvironmentVariable("CODEX_HOME", explicitHome ? home : null);
                    var source = placement switch
                    {
                        "lookalike" => Path.Combine(home, "plugins", "cache-other", "pkg", "harness", "src", "Cli", "Cli.cs"),
                        "unrelated" => Path.Combine(lab.Tree.Root, "other", "plugins", "cache", "pkg", "harness", "src", "Cli", "Cli.cs"),
                        _ => Path.Combine(home, "plugins", "cache", "old", "..", "pkg", "harness", "src", "Cli", "Cli.cs"),
                    };
                    if (placement == "case") source = source.ToUpperInvariant();
                    var output = TestContext.Current!.Output;
                    var start = output.GetErrorOutput().Length;
                    var exit = await Cli.MainAsync(command == "selfcheck" ? [command, Path.Combine(lab.Tree.Root, "missing.jsonc")] : [command], source);
                    await Assert.That(exit).IsEqualTo(command == "status" ? 0 : 2);
                    var warnings = output.GetErrorOutput()[start..].ReplaceLineEndings("\n").Split('\n').Where(line => line == CacheWarning);
                    var expected = command is "selfcheck" or "plan" or "run"
                        && (placement == "inside" || placement == "case" && OperatingSystem.IsWindows());
                    await Assert.That(warnings).IsEquivalentTo(expected ? new[] { CacheWarning } : [], CollectionOrdering.Matching);
                }
        }
        finally { RestoreEnvironment(saved); }
    }

    [Test]
    [NotInParallel]
    public async Task Plugin_cache_warning_still_allows_run_settlement(CancellationToken ct)
    {
        using var lab = new CoreLab("cache-run");
        var repo = await LabRepo.CreateAsync(lab.Tree, ct);
        var tasks = Path.Combine(lab.Tree.Root, "tasks.jsonc");
        File.WriteAllText(tasks, """{"schemaVersion":2,"run":"cache-run","defaults":{"backend":"opencode"},"tasks":[{"id":"inspect","kind":"research","base":"main","route":"fixture/model","prompt":"Inspect."}]}""");
        var environment = lab.Tree.ChildEnvironment();
        foreach (var (name, value) in FakeOpenCode.Environment(lab.Tree, "one-task.jsonl")) environment[name] = value;
        var saved = WorkerEnvironment.Snapshot();
        try
        {
            RestoreEnvironment(environment.Where(x => x.Value is not null).ToDictionary(x => x.Key, x => x.Value!));
            var source = Path.Combine(environment["CODEX_HOME"]!, "plugins", "cache", "pkg", "harness", "src", "Cli", "Cli.cs");
            var output = TestContext.Current!.Output;
            var start = output.GetErrorOutput().Length;
            var exit = await Cli.MainAsync(["run", tasks, "--repo", repo.Root], source);
            await Assert.That(exit).IsEqualTo(0);
            await Assert.That(output.GetErrorOutput()[start..].ReplaceLineEndings("\n")).IsEqualTo(CacheWarning + "\n");
            var run = Directory.GetDirectories(Path.Combine(lab.Tree.DispatchHome, "state", "ledgers")).Single();
            var result = JsonNode.Parse(File.ReadAllText(Path.Combine(run, "inspect", "result.json")))!;
            await Assert.That(result["state"]!.GetValue<string>()).IsEqualTo("done");
            await Assert.That(result["failures"]!.AsArray()).IsEmpty();
        }
        finally { RestoreEnvironment(saved); }
    }

    static void RestoreEnvironment(IReadOnlyDictionary<string, string> values)
    {
        foreach (var name in WorkerEnvironment.Snapshot().Keys.Except(values.Keys, WorkerEnvironment.NameComparer)) Environment.SetEnvironmentVariable(name, null);
        foreach (var (name, value) in values) Environment.SetEnvironmentVariable(name, value);
    }

    [Test]
    [Arguments("done", 0)]
    [Arguments("failed", 1)]
    [Arguments("cancelled", 1)]
    [Arguments("queued", 1)]
    [Arguments("empty", 1)]
    public async Task Wait_returns_status_and_final_verdict(string state, int expected, CancellationToken ct)
    {
        using var tree = new TempTree();
        var ledger = Seed(tree, state == "empty" ? "queued" : state);
        if (state == "empty") File.WriteAllText(Path.Combine(ledger, "events.jsonl"), "{\"schemaVersion\":2,\"run\":\"recorded\",\"type\":\"run\"}\n");
        else File.AppendAllText(Path.Combine(ledger, "events.jsonl"), "{\"run\":\"recorded\",\"task\":\"alpha\",\"type\":\"final\",\"data\":{\"state\":\"done\"}}\n");
        var rows = state == "empty" ? Array.Empty<(string, string)>() : new[] { ("inspect", state), ("alpha", "done") };
        var status = ExpectedStatus("recorded", rows, state is "empty" or "queued" ? " (no supervisor: `resume recorded` settles it)" : "");
        using var process = new HarnessProcess(tree);
        var output = await process.RunAsync(["wait", "recorded"], ct);
        await Assert.That(output.ExitCode).IsEqualTo(expected);
        await Assert.That(output.Stdout.ReplaceLineEndings("\n")).IsEqualTo(status);
        await Assert.That(output.Stderr).IsEqualTo("");
        using var statusProcess = new HarnessProcess(tree);
        var statusOutput = await statusProcess.RunAsync(["status", "recorded"], ct);
        await Assert.That(statusOutput.ExitCode).IsEqualTo(0);
        await Assert.That(statusOutput.Stdout.ReplaceLineEndings("\n")).IsEqualTo(status);
    }

    [Test]
    [NotInParallel]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Wait_observes_completion_or_supervisor_loss(bool loseSupervisor, CancellationToken ct)
    {
        using var lab = new CoreLab("wait-live");
        lab.Ledger.Write("zulu", "queued");
        lab.Ledger.Write("alpha", "queued");
        using var held = SupervisorLock.Acquire(lab.Ledger.RunId);
        var time = new FakeTimeProvider();
        var scheduler = new ConcurrentExclusiveSchedulerPair(TaskScheduler.Default, 1).ExclusiveScheduler;
        var waiting = await StartWaitAsync(Args.Parse(["wait", lab.Ledger.RunId]), time, scheduler, ct);
        await Assert.That(waiting.IsCompleted).IsFalse();
        await AdvanceAsync(time, scheduler, TimeSpan.FromSeconds(4), ct);
        await Assert.That(waiting.IsCompleted).IsFalse();
        if (loseSupervisor) held.Dispose();
        else
        {
            lab.Ledger.Write("zulu", "final", new JsonObject { ["state"] = "done" });
            lab.Ledger.Write("alpha", "final", new JsonObject { ["state"] = "done" });
        }
        var output = TestContext.Current!.Output;
        var start = output.GetStandardOutput().Length;
        await AdvanceAsync(time, scheduler, TimeSpan.FromSeconds(1), ct);
        await Assert.That(waiting.IsCompleted).IsTrue();
        await Assert.That(await waiting).IsEqualTo(loseSupervisor ? 1 : 0);
        await Assert.That(output.GetStandardOutput()[start..].ReplaceLineEndings("\n")).IsEqualTo(ExpectedStatus(lab.Ledger.RunId,
            new[] { ("zulu", loseSupervisor ? "queued" : "done"), ("alpha", loseSupervisor ? "queued" : "done") },
            loseSupervisor ? $" (no supervisor: `resume {lab.Ledger.RunId}` settles it)" : " (supervised now)"));
    }

    [Test]
    [NotInParallel]
    [Arguments(false, false)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task Wait_timeout_names_unfinished_tasks(bool explicitTimeout, bool deadlineCompletion, CancellationToken ct)
    {
        using var lab = new CoreLab("wait-timeout");
        lab.Ledger.Write("zulu", "queued");
        lab.Ledger.Write("finished", "final", new JsonObject { ["state"] = "failed" });
        lab.Ledger.Write("alpha", "queued");
        using var held = SupervisorLock.Acquire(lab.Ledger.RunId);
        // Jump the wall clock between deadline creation and its first remaining-time calculation.
        var time = new FakeTimeProvider { AutoAdvanceAmount = TimeSpan.FromMinutes(explicitTimeout ? 2 : 60) - TimeSpan.FromSeconds(1) };
        var scheduler = new ConcurrentExclusiveSchedulerPair(TaskScheduler.Default, 1).ExclusiveScheduler;
        var args = Args.Parse(explicitTimeout ? ["wait", lab.Ledger.RunId, "--timeout", "2"] : ["wait", lab.Ledger.RunId]);
        var output = TestContext.Current!.Output;
        var errorStart = output.GetErrorOutput().Length;
        var waiting = await StartWaitAsync(args, time, scheduler, ct);
        await Assert.That(waiting.IsCompleted).IsFalse();
        await AdvanceAsync(time, scheduler, TimeSpan.FromMilliseconds(500), ct);
        await Assert.That(waiting.IsCompleted).IsFalse();
        if (deadlineCompletion)
            foreach (var id in new[] { "zulu", "alpha" }) lab.Ledger.Write(id, "final", new JsonObject { ["state"] = "done" });
        var start = output.GetStandardOutput().Length;
        await AdvanceAsync(time, scheduler, TimeSpan.FromMilliseconds(500), ct);
        await Assert.That(waiting.IsCompleted).IsTrue();
        await Assert.That(await waiting).IsEqualTo(deadlineCompletion ? 1 : 3);
        await Assert.That(output.GetErrorOutput()[errorStart..].ReplaceLineEndings("\n")).IsEqualTo(deadlineCompletion ? ""
            : $"run {lab.Ledger.RunId}: wait timed out; unfinished tasks: zulu, alpha\n");
        await Assert.That(output.GetStandardOutput()[start..].ReplaceLineEndings("\n")).IsEqualTo(deadlineCompletion
            ? ExpectedStatus(lab.Ledger.RunId, new[] { ("zulu", "done"), ("finished", "failed"), ("alpha", "done") }, " (supervised now)") : "");
    }

    [Test]
    [NotInParallel]
    public async Task Wait_cancellation_ends_the_delay_without_writes(CancellationToken ct)
    {
        using var lab = new CoreLab("wait-cancel");
        lab.Ledger.Write("inspect", "queued");
        using var held = SupervisorLock.Acquire(lab.Ledger.RunId);
        var before = Snapshot(lab.Ledger.Dir);
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var waiting = Commands.WaitAsync(Args.Parse(["wait", lab.Ledger.RunId]), cancel.Token, new FakeTimeProvider());
        await Assert.That(waiting.IsCompleted).IsFalse();
        cancel.Cancel();
        await Assert.That(async () => await waiting).Throws<OperationCanceledException>();
        await Assert.That(Snapshot(lab.Ledger.Dir)).IsEquivalentTo(before, CollectionOrdering.Matching);
    }

    [Test]
    [Arguments("0")]
    [Arguments("-1")]
    [Arguments("1.5")]
    [Arguments("text")]
    [Arguments("missing")]
    public async Task Wait_rejects_invalid_timeout(string timeout, CancellationToken ct)
    {
        using var tree = new TempTree();
        Seed(tree, "done");
        using var process = new HarnessProcess(tree);
        var output = await process.RunAsync(timeout == "missing" ? ["wait", "recorded", "--timeout"] : ["wait", "recorded", "--timeout", timeout], ct);
        await Assert.That(output.ExitCode).IsEqualTo(2);
        await Assert.That(output.Stdout).IsEqualTo("");
        await Assert.That(output.Stderr.ReplaceLineEndings("\n")).IsEqualTo(timeout == "missing"
            ? "dispatch: --timeout needs a value\n" : "dispatch: --timeout needs a positive whole number of minutes\n");
    }

    [Test]
    public async Task Wait_leaves_ledger_unchanged(CancellationToken ct)
    {
        using var tree = new TempTree();
        var folder = Seed(tree, "queued");
        File.AppendAllText(Path.Combine(folder, "events.jsonl"), "torn tail");
        var before = Snapshot(Path.Combine(tree.DispatchHome, "state"));
        using var process = new HarnessProcess(tree);
        var output = await process.RunAsync(["wait", "recorded", "--ledger", Path.GetDirectoryName(folder)!], ct);
        await Assert.That(output.ExitCode).IsEqualTo(1);
        await Assert.That(output.Stdout.ReplaceLineEndings("\n")).IsEqualTo(ExpectedStatus("recorded", new[] { ("inspect", "queued") }, " (no supervisor: `resume recorded` settles it)"));
        await Assert.That(Snapshot(Path.Combine(tree.DispatchHome, "state"))).IsEquivalentTo(before, CollectionOrdering.Matching);
        using var unknown = new HarnessProcess(tree);
        var refusal = await unknown.RunAsync(["wait", "unknown"], ct);
        await Assert.That(refusal.ExitCode).IsEqualTo(2);
        await Assert.That(refusal.Stdout).IsEqualTo("");
        await Assert.That(refusal.Stderr.ReplaceLineEndings("\n")).IsEqualTo($"dispatch: no ledger for run unknown under {Path.GetDirectoryName(folder)}\n");
    }

    static async Task<Task<int>> StartWaitAsync(Args args, FakeTimeProvider time, TaskScheduler scheduler, CancellationToken ct)
    {
        var waiting = Task.Factory.StartNew(() => Commands.WaitAsync(args, ct, time), ct, TaskCreationOptions.None, scheduler).Unwrap();
        await Task.Factory.StartNew(() => { }, ct, TaskCreationOptions.None, scheduler);
        return waiting;
    }

    static async Task AdvanceAsync(FakeTimeProvider time, TaskScheduler scheduler, TimeSpan amount, CancellationToken ct)
    {
        time.Advance(amount);
        // FIFO barrier: the delay continuation has run and either finished or registered its next timer.
        await Task.Factory.StartNew(() => { }, ct, TaskCreationOptions.None, scheduler);
    }

    static (string Path, string? Bytes)[] Snapshot(string root) => Directory.GetFileSystemEntries(root, "*", SearchOption.AllDirectories)
        .Order(StringComparer.Ordinal).Select(path => (Path.GetRelativePath(root, path), File.Exists(path) ? Convert.ToBase64String(File.ReadAllBytes(path)) : null)).ToArray();

    static string ExpectedStatus(string run, IEnumerable<(string Id, string State)> rows, string driver) => $"run {run}{driver}\n"
        + $"  {"task",-24} {"state",-10} {"outcome",-12} {"cost",8} {"in/out tokens",-16} {"files",5} {"scope!",6} {"asks",4} {"esc",3} verify\n"
        + string.Concat(rows.Select(row => $"  {row.Id,-24} {row.State,-10} {"-",-12} {"-",8} {"-",-16} {"-",5} {"-",6} {"-",4} {"-",3} -\n"
            + (row.State is "running" or "waiting" or "dispatching" or "settling" ? "    tools: 0; last: -\n" : "")));

    [Test]
    [Timeout(30_000)]
    public async Task Status_preserves_verify_order_and_not_run(CancellationToken ct)
    {
        using var tree = new TempTree();
        var ledger = Seed(tree, "failed");
        Json.WriteFile(Path.Combine(ledger, "inspect", "result.json"), new TaskResult
        {
            Task = "inspect", Run = "recorded", State = "failed", Verify = [
                new() { Command = "first", NotRun = "worker stopped" }, new() { Command = "second" },
                new() { Command = "third", ExitCode = 7 }, new() { Command = "fourth", ExitCode = 9, NotRun = "worker stopped" }],
        }, LedgerJson.Files.TaskResult);
        using var process = new HarnessProcess(tree);
        var output = await process.RunAsync(["status", "recorded"], ct);
        await Assert.That(output.ExitCode).IsEqualTo(0);
        await Assert.That(output.Stderr).IsEqualTo("");
        await Assert.That(output.Stdout.ReplaceLineEndings("\n").Split('\n').Where(line => line.StartsWith("  inspect ", StringComparison.Ordinal)))
            .IsEquivalentTo(new[] { $"  {"inspect",-24} {"failed",-10} {"-",-12} {"-",8} {"-",-16} {0,5} {0,6} {0,4} {0,3} not run ok exit 7 not run" }, CollectionOrdering.Matching);
    }

    [Test]
    [Timeout(180_000)]
    public async Task Selfcheck_launch_create_failure_exits_1_and_checks_other_backend(CancellationToken ct)
    {
        using var lab = new CodexLab();
        var environment = CatalogCommandTests.Configure(lab, both: true);
        var repo = await LabRepo.CreateAsync(lab.Tree, ct);
        var scenario = Path.Combine(lab.Scenarios, "task.jsonl");
        var steps = File.ReadLines(scenario).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => JsonNode.Parse(x)!).ToArray();
        var create = steps.Single(x => x["expect"]?.GetValue<string>() == "thread/start");
        create.AsObject().Remove("reply");
        create["error"] = new JsonObject { ["code"] = -32603, ["message"] = "synthetic launch rejected" };
        File.WriteAllLines(scenario, steps.Select(x => x.ToJsonString()));
        using var harness = new HarnessProcess(lab.Tree);
        var result = await harness.RunAsync(["selfcheck", "--repo", repo.Root], ct, environment);
        TestContext.Current!.Output.WriteLine(result.ToString());
        await Assert.That(result.ExitCode).IsEqualTo(1);
        await Assert.That(result.Stdout.Split('\n').Select(x => x.TrimEnd('\r')).Where(x => x.StartsWith("FAIL  ", StringComparison.Ordinal)).ToArray())
            .IsEquivalentTo(new[] { "FAIL  launch: synthetic launch rejected" }, CollectionOrdering.Matching);
        await Assert.That(result.Stdout.Split('\n').Select(x => x.TrimEnd('\r')).Where(x => x.Contains("  launch:", StringComparison.Ordinal)).ToArray())
            .IsEquivalentTo(new[] { "FAIL  launch: synthetic launch rejected", "PASS  launch: ses_fixture" }, CollectionOrdering.Matching);
        await Assert.That(Directory.GetDirectories(Path.Combine(lab.Tree.DispatchHome, "state", "control"))).IsEmpty();
        await Assert.That(Directory.GetFileSystemEntries(lab.Tree.WorktreeRoot)).IsEmpty();
    }

    [Test]
    [Arguments("plan", false, 0)]
    [Arguments("plan", true, 2)]
    [Arguments("selfcheck", false, 0)]
    [Arguments("selfcheck", true, 1)]
    [Timeout(45_000)]
    public async Task Checks_distinguish_a_refusal_from_a_failed_selfcheck(string command, bool missingTool, int expected, CancellationToken ct)
    {
        using var tree = new TempTree();
        var repo = await LabRepo.CreateAsync(tree, ct);
        var tasks = Path.Combine(tree.Root, "tasks.jsonc");
        File.WriteAllText(tasks, """{"schemaVersion":2,"defaults":{"backend":"opencode"},"tasks":[{"id":"inspect","kind":"research","base":"main","route":"fixture/model","prompt":"Inspect."}]}""");
        var env = FakeOpenCode.Environment(tree, "one-task.jsonl").ToDictionary(x => x.Key, x => (string?)x.Value);
        if (missingTool)
        {
            var config = Path.Combine(tree.DispatchHome, "config", "config.jsonc");
            var document = JsonNode.Parse(File.ReadAllText(config))!;
            document["backends"]!["opencode"]!["executable"] = Path.Combine(tree.Root, "missing-executable");
            File.WriteAllText(config, document.ToJsonString());
        }
        else if (command == "selfcheck")
        {
            var fixture = File.ReadAllLines(FakeOpenCode.Fixture("one-task.jsonl")).Select(x => JsonNode.Parse(x)!).ToArray();
            var session = fixture[11]["reply"]!.DeepClone();
            session["data"]!["permissions"] = new JsonArray(new JsonObject
            {
                ["action"] = "edit", ["resource"] = Path.Combine(tree.DispatchHome, "state", "control", "**").Replace('\\', '/'), ["effect"] = "deny",
            });
            var steps = fixture.Take(4).ToList();
            var empty = JsonNode.Parse("""{"location":{"directory":"$directory"},"data":[]}""")!;
            steps.Add(new JsonObject { ["expect"] = "GET /api/mcp", ["reply"] = empty.DeepClone() });
            steps.Add(new JsonObject { ["expect"] = "POST /api/session", ["reply"] = session.DeepClone() });
            steps.Add(new JsonObject { ["expect"] = "GET /api/session/ses_fixture", ["reply"] = session.DeepClone() });
            steps.Add(fixture[10].DeepClone());
            foreach (var endpoint in new[] { "permission", "form" })
                steps.Add(new JsonObject { ["expect"] = $"GET /api/session/ses_fixture/{endpoint}", ["reply"] = empty.DeepClone() });
            steps.Add(new JsonObject { ["expect"] = "DELETE /api/session/ses_fixture", ["status"] = 204 });
            steps.Add(new JsonObject { ["expect"] = "GET /api/session/ses_fixture", ["status"] = 404,
                ["reply"] = new JsonObject { ["_tag"] = "SessionNotFoundError", ["sessionID"] = "ses_fixture", ["message"] = "removed" } });
            var launchStart = steps.Count;
            steps.Add(new JsonObject { ["expect"] = "POST /api/session", ["reply"] = OpenCodeLab.Session("ses_fixture") });
            steps.Add(new JsonObject { ["expect"] = "PUT /api/session/ses_fixture/environment", ["status"] = 204 });
            steps.Add(new JsonObject { ["expect"] = "DELETE /api/session/ses_fixture", ["status"] = 204 });
            var scenario = Path.Combine(tree.Root, "selfcheck.jsonl");
            FakeOpenCode.SelfcheckScenario(tree, scenario, steps, launchStart, env);
        }
        using var child = new HarnessProcess(tree);

        var result = await child.RunAsync(command == "plan" ? [command, tasks, "--repo", repo.Root] : [command, "--repo", repo.Root], ct, env);

        TestContext.Current!.Output.WriteLine(result.ToString());
        await Assert.That(result.ExitCode).IsEqualTo(expected);
        await Assert.That(Directory.GetFiles(tree.DispatchHome, "run.json", SearchOption.AllDirectories)).IsEmpty();
    }

    [Test]
    [Arguments("plan")]
    [Arguments("run")]
    [Timeout(45_000)]
    public async Task An_opencode_server_off_the_pin_is_refused_before_a_run_starts(string command, CancellationToken ct)
    {
        using var tree = new TempTree();
        var repo = await LabRepo.CreateAsync(tree, ct);
        var tasks = Path.Combine(tree.Root, "tasks.jsonc");
        File.WriteAllText(tasks, """{"schemaVersion":2,"defaults":{"backend":"opencode"},"tasks":[{"id":"inspect","kind":"research","base":"main","route":"fixture/model","prompt":"Inspect."}]}""");
        var scenario = Path.Combine(tree.Root, "other-version.jsonl");
        File.WriteAllLines(scenario, File.ReadAllLines(FakeOpenCode.Fixture("one-task.jsonl"))
            .Select(line => line.Replace("\"version\":\"2.1.4\"", "\"version\":\"2.1.5\"")));
        var env = FakeOpenCode.Environment(tree, "one-task.jsonl").ToDictionary(x => x.Key, x => (string?)x.Value);
        env["FAKE_OPENCODE_SCENARIO"] = scenario;
        using var child = new HarnessProcess(tree);

        var result = await child.RunAsync([command, tasks, "--repo", repo.Root], ct, env);

        TestContext.Current!.Output.WriteLine(result.ToString());
        await Assert.That(result.ExitCode).IsEqualTo(2);
        await Assert.That(result.Stdout + result.Stderr).Contains($"'2.1.5' differs from pin {Constants.PinnedUpstream}");
        await Assert.That(Directory.GetFiles(tree.DispatchHome, "run.json", SearchOption.AllDirectories)).IsEmpty();
    }

    [Test]
    [Arguments("queued")]
    [Arguments("failed")]
    [Arguments("cancelled")]
    [Timeout(30_000)]
    public async Task Status_reports_states_without_judging_them(string state, CancellationToken ct)
    {
        using var tree = new TempTree();
        Seed(tree, state);
        using var child = new HarnessProcess(tree);

        var result = await child.RunAsync(["status", "recorded"], ct);

        await Assert.That(result.ExitCode).IsEqualTo(0);
        await Assert.That(result.Stdout).Contains(state);
    }

    [Test]
    [Arguments("success", 0, "")]
    [Arguments("verify", 1, "verify-failed")]
    [Arguments("scope", 1, "out-of-scope")]
    [Timeout(45_000)]
    public async Task Run_exit_reflects_settlement(string condition, int expected, string failure, CancellationToken ct)
    {
        using var tree = new TempTree();
        var repo = await LabRepo.CreateAsync(tree, ct);
        var tasks = Path.Combine(tree.Root, "tasks.jsonc");
        var document = JsonNode.Parse("""{"schemaVersion":2,"run":"exit-check","defaults":{"backend":"opencode"},"tasks":[{"id":"inspect","kind":"code","base":"main","route":"fixture/model","prompt":"Inspect.","scope":["allowed/**"]}]}""")!;
        if (condition == "verify") document["tasks"]![0]!["verify"] = new JsonArray("exit 3");
        File.WriteAllText(tasks, document.ToJsonString());
        var env = FakeOpenCode.Environment(tree, "one-task.jsonl").ToDictionary(x => x.Key, x => (string?)x.Value);
        var steps = File.ReadAllLines(FakeOpenCode.Fixture("one-task.jsonl")).Select(x => JsonNode.Parse(x)!).ToArray();
        var arrived = Path.Combine(Path.GetDirectoryName(FakeOpenCode.Record(tree))!, "prompt-arrived");
        var release = Path.Combine(tree.Root, "release-prompt");
        if (condition == "scope")
        {
            steps[6]["arrived"] = arrived;
            steps[6]["waitFor"] = release;
        }
        var scenario = Path.Combine(tree.Root, "scenario.jsonl");
        File.WriteAllLines(scenario, steps.Select(x => x.ToJsonString()));
        env["FAKE_OPENCODE_SCENARIO"] = scenario;
        var promptArrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var watcher = new FileSystemWatcher(Path.GetDirectoryName(arrived)!, Path.GetFileName(arrived));
        watcher.Created += (_, _) => promptArrived.TrySetResult();
        watcher.Error += (_, error) => promptArrived.TrySetException(error.GetException());
        watcher.EnableRaisingEvents = true;
        using var child = new HarnessProcess(tree);
        var running = child.RunAsync(["run", tasks, "--repo", repo.Root], ct, env);
        if (condition == "scope")
        {
            if (await Task.WhenAny(promptArrived.Task, running).WaitAsync(ct) == running)
                throw new InvalidOperationException((await running).ToString());
            await promptArrived.Task.WaitAsync(ct);
            var worktree = Directory.GetDirectories(Path.Combine(tree.DispatchHome, "state", "worktrees"))
                .Single(path => Directory.Exists(Path.Combine(path, "inspect")));
            File.WriteAllText(Path.Combine(worktree, "inspect", "outside.txt"), "worker edit");
            File.WriteAllText(release, "go");
        }

        var result = await running;

        await Assert.That(result.ExitCode).IsEqualTo(expected);
        var ledger = Directory.GetDirectories(Path.Combine(tree.DispatchHome, "state", "ledgers")).Single();
        var saved = JsonNode.Parse(File.ReadAllText(Path.Combine(ledger, "inspect", "result.json")))!;
        await Assert.That(saved["state"]!.GetValue<string>()).IsEqualTo(expected == 0 ? "done" : "failed");
        await Assert.That(saved["failures"]!.AsArray().Select(x => x!.GetValue<string>()).ToArray())
            .IsEquivalentTo(failure.Length == 0 ? [] : new[] { failure });
    }

    [Test]
    [Timeout(45_000)]
    public async Task Collect_FailedTask_Exits1(CancellationToken ct)
    {
        using var tree = new TempTree();
        var repo = await LabRepo.CreateAsync(tree, ct);
        var ledger = Seed(tree, "done");
        await repo.BaselineAsync(Path.Combine(tree.DispatchHome, "state", "control", "abcdefghijklmnopqrstuvwxyz"), ct);
        Json.WriteFile(Path.Combine(ledger, "inspect", "task.json"), new TaskRecord
        {
            Spec = new() { Id = "inspect", Kind = "code", Scope = ["**"], Verify = ["exit 3"] },
            Session = new() { Id = "ses_fixture" },
            Workspace = new() { Path = repo.Root, BaseSha = await repo.GitAsync(["rev-parse", "HEAD"], ct) },
        }, LedgerJson.Files.TaskRecord);
        using var child = new HarnessProcess(tree);

        var result = await child.RunAsync(["collect", "recorded", "inspect"], ct,
            FakeOpenCode.Environment(tree, "one-task.jsonl").ToDictionary(x => x.Key, x => (string?)x.Value));

        await Assert.That(result.ExitCode).IsEqualTo(1);
        await Assert.That(File.ReadAllText(Path.Combine(ledger, "inspect", "result.json"))).Contains("verify-failed");
    }

    [Test]
    [Arguments("usage")]
    [Arguments("unknown")]
    [Arguments("missing-value")]
    [Arguments("plan-errors")]
    [Arguments("run-errors")]
    [Timeout(30_000)]
    public async Task Refusals_exit_2_without_starting_a_run(string kind, CancellationToken ct)
    {
        using var tree = new TempTree();
        var tasks = Path.Combine(tree.Root, "tasks.jsonc");
        File.WriteAllText(tasks, """{"schemaVersion":2,"tasks":[]}""");
        string[] args = kind switch
        {
            "usage" => [], "unknown" => ["not-a-command"], "missing-value" => ["run", "--max"],
            "plan-errors" => ["plan", tasks], _ => ["run", tasks],
        };
        using var child = new HarnessProcess(tree);

        var result = await child.RunAsync(args, ct);

        await Assert.That(result.ExitCode).IsEqualTo(2);
        await Assert.That(Directory.GetFiles(tree.DispatchHome, "run.json", SearchOption.AllDirectories)).IsEmpty();
        await Assert.That(result.Stderr).DoesNotContain("Unhandled exception");
    }

    [Test]
    [Timeout(30_000)]
    public async Task Collect_CorruptMiddleLedgerLine_Exits1(CancellationToken ct)
    {
        using var tree = new TempTree();
        var ledger = Seed(tree, "done");
        Json.WriteFile(Path.Combine(ledger, "inspect", "task.json"), new TaskRecord
        {
            Spec = new() { Id = "inspect", Kind = "research" },
            Session = new() { Id = "ses_fixture" }, Workspace = new() { Path = tree.Repository },
        }, LedgerJson.Files.TaskRecord);
        File.AppendAllText(Path.Combine(ledger, "events.jsonl"), "not-json\n{\"run\":\"recorded\",\"type\":\"tail\"}\n");
        using var child = new HarnessProcess(tree);
        var result = await child.RunAsync(["collect", "recorded", "inspect"], ct,
            FakeOpenCode.Environment(tree, "one-task.jsonl").ToDictionary(x => x.Key, x => (string?)x.Value));
        await Assert.That(result.ExitCode).IsEqualTo(1);
        await Assert.That(result.Stderr).Contains("events.jsonl");
        await Assert.That(result.Stderr).Contains("line 3");
        await Assert.That(File.Exists(FakeOpenCode.Record(tree))).IsFalse();
    }

    [Test]
    [Arguments("status", 2)]
    [Arguments("resume", 1)]
    [Timeout(30_000)]
    public async Task Corrupt_middle_line_uses_the_command_exit_rule(string command, int expected, CancellationToken ct)
    {
        using var tree = new TempTree();
        var ledger = Seed(tree, "done");
        File.AppendAllText(Path.Combine(ledger, "events.jsonl"), "not-json\n{\"run\":\"recorded\",\"type\":\"tail\"}\n");
        using var child = new HarnessProcess(tree);

        var result = await child.RunAsync([command, "recorded"], ct,
            FakeOpenCode.Environment(tree, "one-task.jsonl").ToDictionary(x => x.Key, x => (string?)x.Value));

        await Assert.That(result.ExitCode).IsEqualTo(expected);
        await Assert.That(result.Stderr).Contains("events.jsonl");
        await Assert.That(result.Stderr).Contains("line 3");
    }

    [Test]
    [Timeout(45_000)]
    public async Task Run_UnexpectedIOException_ExitsCleanly(CancellationToken ct)
    {
        using var tree = new TempTree();
        var repo = await LabRepo.CreateAsync(tree, ct);
        var tasks = Path.Combine(tree.Root, "tasks.jsonc");
        File.WriteAllText(tasks, """{"schemaVersion":2,"run":"blocked-setup","defaults":{"backend":"opencode"},"tasks":[{"id":"inspect","kind":"research","base":"main","route":"fixture/model","prompt":"Inspect."}]}""");
        var ledger = Path.Combine(tree.DispatchHome, "state", "ledgers", Commands.CreateRunId("blocked-setup"));
        // The run claim succeeds, then copying the frozen task file encounters this directory.
        Directory.CreateDirectory(Path.Combine(ledger, "tasks.jsonc"));
        using var child = new HarnessProcess(tree);

        var result = await child.RunAsync(["run", tasks, "--repo", repo.Root], ct,
            FakeOpenCode.Environment(tree, "one-task.jsonl").ToDictionary(x => x.Key, x => (string?)x.Value));

        await Assert.That(result.ExitCode).IsEqualTo(1);
        await Assert.That(File.Exists(Path.Combine(ledger, "run.json"))).IsTrue();
        var events = File.ReadAllLines(Path.Combine(ledger, "events.jsonl")).Select(x => JsonNode.Parse(x)!);
        await Assert.That(events.Any(x => x["type"]!.GetValue<string>() == "aborted")).IsTrue();
        await Assert.That(result.Stderr.Trim().Split('\n').Length).IsEqualTo(1);
    }

    [Test]
    [Arguments("done", false, 0)]
    [Arguments("failed", false, 1)]
    [Arguments("cancelled", false, 1)]
    [Arguments("queued", false, 1)]
    [Arguments("failed", true, 1)]
    [Timeout(45_000)]
    public async Task Resume_judges_every_task_in_the_run(string state, bool recoverAnother, int expected, CancellationToken ct)
    {
        using var tree = new TempTree();
        var repo = await LabRepo.CreateAsync(tree, ct);
        var ledger = Seed(tree, state);
        await repo.BaselineAsync(Path.Combine(tree.DispatchHome, "state", "control", "abcdefghijklmnopqrstuvwxyz"), ct);
        if (recoverAnother)
        {
            var record = new TaskRecord
            {
                Spec = new() { Id = "recover", Kind = "research", RawPrompt = true },
                Session = new() { Id = "ses_fixture" },
                Workspace = new() { Path = repo.Root, BaseSha = await repo.GitAsync(["rev-parse", "HEAD"], ct) },
            };
            Json.WriteFile(Path.Combine(ledger, "recover", "task.json"), record, LedgerJson.Files.TaskRecord);
            var frozen = System.Text.Json.JsonSerializer.Deserialize(File.ReadAllText(Path.Combine(ledger, "config.json")), LedgerJson.Files.EffectiveConfig)!;
            frozen.Tasks["recover"] = frozen.Tasks["inspect"];
            Json.WriteFile(Path.Combine(ledger, "config.json"), frozen, LedgerJson.Files.EffectiveConfig);
            File.AppendAllText(Path.Combine(ledger, "events.jsonl"), "{\"run\":\"recorded\",\"task\":\"recover\",\"type\":\"queued\"}\n");
        }
        using var child = new HarnessProcess(tree);

        var result = await child.RunAsync(["resume", "recorded"], ct,
            FakeOpenCode.Environment(tree, "one-task.jsonl").ToDictionary(x => x.Key, x => (string?)x.Value));

        await Assert.That(result.ExitCode).IsEqualTo(expected);
        if (recoverAnother)
            await Assert.That(JsonNode.Parse(File.ReadAllText(Path.Combine(ledger, "recover", "result.json")))!["state"]!.GetValue<string>()).IsEqualTo("done");
    }

    // A coordinator that does not poll `asks` learns from the summary that a worker's call was declined
    // because its escalation closed unanswered.
    [Test]
    [Timeout(30_000)]
    public async Task Status_notes_escalated_asks_that_closed_without_an_answer(CancellationToken ct)
    {
        using var tree = new TempTree();
        var ledger = Seed(tree, "failed");
        File.AppendAllText(Path.Combine(ledger, "events.jsonl"), new JsonObject
        {
            ["run"] = "recorded", ["task"] = "inspect", ["type"] = "escalation-dropped",
            ["data"] = new JsonObject { ["id"] = "inspect-1", ["action"] = "bash", ["reason"] = "task interrupted" },
        }.ToJsonString() + "\n");
        using var child = new HarnessProcess(tree);

        var result = await child.RunAsync(["status", "recorded"], ct);

        await Assert.That(result.ExitCode).IsEqualTo(0);
        await Assert.That(result.Stdout).Contains("asks closed without an answer: 1 (bash: task interrupted)");
    }

    static string Seed(TempTree tree, string state)
    {
        var ledger = Directory.CreateDirectory(Path.Combine(tree.DispatchHome, "state", "ledgers", "recorded")).FullName;
        const string controlId = "abcdefghijklmnopqrstuvwxyz";
        Json.WriteFile(Path.Combine(ledger, "run.json"), new RunInfo { Run = "recorded", ControlId = controlId, Repo = tree.Repository }, LedgerJson.Files.RunInfo);
        File.WriteAllText(Path.Combine(ledger, "events.jsonl"), "{\"schemaVersion\":2,\"run\":\"recorded\",\"type\":\"run\"}\n" + new JsonObject
        {
            ["run"] = "recorded", ["task"] = "inspect", ["type"] = state == "queued" ? "queued" : "final",
            ["data"] = new JsonObject { ["state"] = state },
        }.ToJsonString() + "\n");
        Json.WriteFile(Path.Combine(ledger, "inspect", "task.json"), new TaskRecord { Spec = new() { Id = "inspect", Kind = "research" } }, LedgerJson.Files.TaskRecord);
        Json.WriteFile(Path.Combine(ledger, "config.json"), new EffectiveConfig
        {
            Tasks = new() { ["inspect"] = new() { Settings = new()
            {
                Backend = new() { Value = "opencode" }, Route = new() { Value = "opencode:fixture/model" }, RawPrompt = new() { Value = true },
            } } },
        }, LedgerJson.Files.EffectiveConfig);
        var control = Directory.CreateDirectory(Path.Combine(tree.DispatchHome, "state", "control", controlId)).FullName;
        File.WriteAllText(Path.Combine(control, "setup.done"), "");
        return ledger;
    }

    [Test]
    [Timeout(30_000)]
    public async Task Unexpected_exception_before_start_exits_2_with_one_line(CancellationToken ct)
    {
        using var tree = new TempTree();
        // A valid JSON event with an invalid state type reaches the catch-all on every OS.
        // A blocked home only throws when startup creates a Windows hooks folder.
        var ledger = Directory.CreateDirectory(Path.Combine(tree.DispatchHome, "state", "ledgers", "recorded")).FullName;
        File.WriteAllText(Path.Combine(ledger, "events.jsonl"), """
            {"type":"final","task":"inspect","data":{"state":42}}

            """);
        using var child = new HarnessProcess(tree);

        var result = await child.RunAsync(["status"], ct);

        await Assert.That(result.ExitCode).IsEqualTo(2);
        await Assert.That(result.Stderr.Trim().Split('\n').Length).IsEqualTo(1);
        await Assert.That(result.Stderr).StartsWith("dispatch: ");
        await Assert.That(result.Stderr).Contains("Exception:");
    }
}
