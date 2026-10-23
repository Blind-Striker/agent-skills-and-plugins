using Dispatch.Tests.Support;

namespace Dispatch.Tests.Integration;

[Category("Integration")]
[NotInParallel]
public sealed class RunSetupTests
{
    [Test]
    [Timeout(30_000)]
    public async Task Structured_setup_crash_before_frozen_config_publish_leaves_no_setup_marker(CancellationToken ct)
    {
        await using var lab = new OpenCodeLab();
        using var structured = new StructuredLab("opencode");
        var repo = await LabRepo.CreateAsync(lab.Tree, ct);
        var backend = new FakeBackend("opencode", new("policy", "policy", "text", true));
        backend.OnProbe = (_, _) => Task.FromResult(new BackendProbe(true, "fixture", [], []));
        backend.OnValidate = (plans, _) => Task.FromResult<IReadOnlyList<LiveCheck>>(plans.Select(x => new LiveCheck(x.TaskId, [], [], "fixture", "clean", null)).ToList());
        var visited = false;
        string? folder = null;
        var result = await Commands.RunAsync(Args.Parse(["run", structured.Tasks, "--repo", repo.Root]), ct,
            backendFactory: () => backend, beforeFrozenConfigMove: pending =>
            {
                visited = true;
                folder = Path.GetDirectoryName(pending)!;
                throw new IOException("interrupted before frozen config publish");
            });

        await Assert.That(visited).IsTrue();
        await Assert.That(result).IsEqualTo(1);
        await Assert.That(File.Exists(Path.Combine(folder!, "answer", "input.json"))).IsTrue();
        await Assert.That(File.Exists(Path.Combine(folder!, "answer", "result.schema.json"))).IsTrue();
        var ledger = new Ledger(folder!);
        await Assert.That(File.Exists(Path.Combine(Locations.ControlDir(ledger.RunId), "setup.done"))).IsFalse();
        await Assert.That(ConfigLayers.Frozen(folder!).Tasks["answer"].InputSha256).IsNull();
        await Assert.That(backend.Calls.Any(x => x.Member == nameof(IWorkerBackend.CreateAsync))).IsFalse();
    }

    [Test]
    [Timeout(30_000)]
    public async Task Default_run_name_drops_the_tasks_suffix_of_the_file_name(CancellationToken ct)
    {
        await using var lab = new OpenCodeLab();
        var repo = await LabRepo.CreateAsync(lab.Tree, ct);
        var tasks = Path.Combine(lab.Tree.Root, "review.tasks.jsonc");
        File.WriteAllText(tasks, """{"schemaVersion":2,"defaults":{"backend":"opencode"},"tasks":[{"id":"inspect","kind":"research","base":"main","route":"fixture/model","prompt":"Inspect."}]}""");
        var backend = new FakeBackend("opencode", new("policy", "policy", "text", true));
        backend.OnProbe = (_, _) => Task.FromResult(new BackendProbe(true, "fixture", [], []));
        backend.OnValidate = (plans, _) => Task.FromResult<IReadOnlyList<LiveCheck>>(plans.Select(x => new LiveCheck(x.TaskId, [], [], "fixture", "full", null)).ToList());
        string? run = null;
        var result = await Commands.RunAsync(Args.Parse(["run", tasks, "--repo", repo.Root]), ct, (info, _) =>
        {
            run = info.Run;
            throw new IOException("setup interrupted");
        }, () => backend);
        await Assert.That(result).IsEqualTo(1);
        await Assert.That(run).IsEqualTo(Commands.CreateRunId("review"));
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    [Timeout(30_000)]
    public async Task Run_RetainsLockUntilBackendShutdownCompletes(bool shutdownFails, CancellationToken ct)
    {
        await using var lab = new OpenCodeLab();
        var repo = await LabRepo.CreateAsync(lab.Tree, ct);
        var tasks = Path.Combine(lab.Tree.Root, "tasks.jsonc");
        File.WriteAllText(tasks, """{"schemaVersion":2,"run":"shutdown","defaults":{"backend":"opencode"},"tasks":[{"id":"inspect","kind":"research","base":"main","route":"fixture/model","prompt":"Inspect."}]}""");
        var backend = new FakeBackend("opencode", new("policy", "policy", "text", true));
        backend.OnProbe = (_, _) => Task.FromResult(new BackendProbe(true, "fixture", [], []));
        backend.OnValidate = (plans, _) => Task.FromResult<IReadOnlyList<LiveCheck>>(plans.Select(x => new LiveCheck(x.TaskId, [], [], "fixture", "full", null)).ToList());
        var stopping = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        backend.OnDispose = async () =>
        {
            stopping.TrySetResult();
            await release.Task.WaitAsync(ct);
            if (shutdownFails) throw new IOException("shutdown failed");
        };
        string? control = null;
        var running = Commands.RunAsync(Args.Parse(["run", tasks, "--repo", repo.Root]), ct, (info, _) =>
        {
            control = Locations.ControlDir(info.Run);
            throw new IOException("setup interrupted");
        }, () => backend);
        bool held;
        try
        {
            await stopping.Task.WaitAsync(ct);
            using var contender = SupervisorLock.TryAcquireFolder(control!);
            held = contender is null;
        }
        finally { release.TrySetResult(); }
        await Assert.That(await running).IsEqualTo(1);
        await Assert.That(held).IsTrue();
        using var acquired = SupervisorLock.AcquireFolder(control!);
        await Assert.That(acquired).IsNotNull();
    }

    [Test]
    [Arguments("claimed")]
    [Arguments("queued")]
    [Timeout(60_000)]
    public async Task Run_LockHeldFromBeforeClaim(string stopAt, CancellationToken ct)
    {
        await using var lab = new OpenCodeLab();
        var repo = await LabRepo.CreateAsync(lab.Tree, ct);
        var tasks = Path.Combine(lab.Tree.Root, "tasks.jsonc");
        File.WriteAllText(tasks, """{"schemaVersion":2,"run":"setup","defaults":{"backend":"opencode"},"tasks":[{"id":"inspect","kind":"research","base":"main","route":"fixture/model","prompt":"Inspect."}]}""");
        lab.Write();
        var visited = false;
        var verified = false;
        var result = await Commands.RunAsync(Args.Parse(["run", tasks, "--repo", repo.Root]), ct, async (info, stage) =>
        {
            if (stage != stopAt) return;
            visited = true;
            var control = Path.Combine(lab.Tree.DispatchHome, "state", "control", info.ControlId!);
            await Assert.That(File.Exists(Path.Combine(control, "setup.done"))).IsFalse();
            foreach (var command in new[] { "resume", "collect", "clean" })
            {
                using var child = new HarnessProcess(lab.Tree);
                var refused = await child.RunAsync(command == "collect" ? [command, info.Run, "inspect"] : [command, info.Run], ct);
                await Assert.That(refused.ExitCode).IsEqualTo(2);
                await Assert.That(refused.Stderr).Contains("supervised");
            }
            using var status = new HarnessProcess(lab.Tree);
            var shown = await status.RunAsync(["status", info.Run], ct);
            shown.RequireSuccess("status during setup");
            await Assert.That(shown.Stdout).Contains("supervised now");
            verified = true;
            throw new IOException("setup interrupted");
        });
        await Assert.That(visited).IsTrue();
        await Assert.That(verified).IsTrue();
        await Assert.That(result).IsEqualTo(1);
        var run = Commands.CreateRunId("setup");
        foreach (var command in new[] { "resume", "collect" })
        {
            using var child = new HarnessProcess(lab.Tree);
            var refused = await child.RunAsync(command == "collect" ? [command, run, "inspect"] : [command, run], ct);
            await Assert.That(refused.ExitCode).IsEqualTo(2);
            await Assert.That(refused.Stderr).Contains("setup never finished");
        }
        using var clean = new HarnessProcess(lab.Tree);
        (await clean.RunAsync(["clean", run], ct)).RequireSuccess("clean unfinished run");
        var folder = Locations.ControlDir(run);
        await Assert.That(Directory.GetFileSystemEntries(folder).Select(x => Path.GetFileName(x)).Order().ToArray())
            .IsEquivalentTo(new[] { "run.ref", "supervisor.lock" });
        using (var reacquired = SupervisorLock.AcquireFolder(folder)) { }
        await Assert.That(File.Exists(Path.Combine(folder, "supervisor.lock"))).IsTrue();
    }
}
