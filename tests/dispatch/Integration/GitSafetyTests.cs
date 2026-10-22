using System.Text.Json.Nodes;
using Dispatch.Tests.Support;

namespace Dispatch.Tests.Integration;

[Category("Integration")]
[NotInParallel]
public sealed class GitSafetyTests
{
    [Test]
    [Timeout(60_000)]
    public async Task ConfigEvent_NeverContainsValues(CancellationToken ct)
    {
        using var lab = new CoreLab();
        var repo = await LabRepo.CreateAsync(lab.Tree, ct);
        const string before = "https://fixture:before@example.invalid/repo";
        const string after = "https://fixture:after@example.invalid/repo";
        await repo.GitAsync(["config", "remote.fixture.url", before], ct);
        var run = await lab.TaskAsync("inspect", ct);
        await repo.GitAsync(["config", "remote.fixture.url", after], ct);
        var result = await Settlement.SettleAsync(lab.Backend, lab.Ledger, run, null, ct, lab.Git);
        await Assert.That(result.State).IsEqualTo("done");
        await Assert.That(result.Warnings ?? []).Contains("git-config-changed");
        var changes = lab.Ledger.Read().Where(x => x.Type == "git-config-changed").ToList();
        await Assert.That(changes.Count).IsGreaterThan(0);
        foreach (var change in changes)
        {
            await Assert.That(change.Data!["keys"]!.AsArray().Select(x => x!.GetValue<string>())).IsEquivalentTo(["remote.fixture.url"]);
            await Assert.That(change.Data["blocking"]!.GetValue<bool>()).IsFalse();
            await Assert.That(change.Data.ToJsonString()).DoesNotContain(before);
            await Assert.That(change.Data.ToJsonString()).DoesNotContain(after);
        }
        var baseline = await File.ReadAllTextAsync(Path.Combine(lab.Git.ControlFolder, "git-baseline.json"), ct);
        await Assert.That(baseline).Contains("sha256");
        await Assert.That(baseline).DoesNotContain(before);
    }

    [Test]
    [Timeout(60_000)]
    public async Task SnapshotCleanup_DoesNotDeleteForeignLock(CancellationToken ct)
    {
        using var lab = new CoreLab();
        var run = await lab.TaskAsync("inspect", ct);
        var index = Snapshot.Index(lab.Git, run.Id);
        await File.WriteAllTextAsync(index, "temporary index", ct);
        await File.WriteAllTextAsync(index + ".lock", "foreign", ct);
        Snapshot.Delete(lab.Git, run.Id);
        await Assert.That(File.Exists(index)).IsFalse();
        await Assert.That(File.Exists(index + ".lock")).IsTrue();
        await Assert.That(await File.ReadAllTextAsync(index + ".lock", ct)).IsEqualTo("foreign");
    }

    [Test]
    [Timeout(60_000)]
    public async Task FilterChangedByEarlierTask_FailsLaterTask(CancellationToken ct)
    {
        using var lab = new CoreLab("config-test");
        var first = await lab.TaskAsync("first", ct);
        var later = await lab.TaskAsync("later", ct);
        later.Record.Workspace = new();
        later.Record.Spec.Base = "main";
        var starts = new List<string>();
        lab.Backend.OnCreate = (launch, _) =>
        {
            starts.Add(launch.TaskId);
            return Task.FromResult(new CreateResult(new("opencode", launch.TaskId, "worker-" + launch.TaskId, null, new()), new("none", "none", "policy")));
        };
        lab.Backend.OnPrompt = async (input, token) =>
        {
            await new LabRepo(lab.Tree).GitAsync(["config", "filter.x.clean", "cat"], token);
            return input.Worker with { Turn = "turn-" + input.Worker.TaskId };
        };
        lab.Backend.OnPoll = (live, _) =>
        {
            foreach (var worker in live) lab.End(worker.TaskId == first.Id ? first : later);
            return Task.FromResult(new PollResult([]));
        };
        await Assert.That(await lab.Scheduler(ct).RunAsync([first, later])).IsEqualTo(1);
        await Assert.That(starts).IsEquivalentTo(["first"]);
        await Assert.That(later.Record.Workspace.Path).IsNull();
        await Assert.That(lab.Ledger.LoadResult(later.Id)!.Failures).Contains("git-config-changed");
        await Assert.That(lab.Ledger.Read().Any(x => x.Task == later.Id && x.Type == "git-config-changed")).IsTrue();
    }

    [Test]
    [Timeout(60_000)]
    public async Task HooksFolder_PlantedHook_NotRun(CancellationToken ct)
    {
        using var lab = new CoreLab();
        var run = await lab.TaskAsync("inspect", ct);
        run.Record.Spec.Kind = "code";
        run.Record.Spec.Scope = ["README.md"];
        var environment = WrapperEnvironment(lab, Path.Combine(lab.Tree.Repository, ".git", "index.lock"), "forward");
        environment["FAKE_GIT_CALLS"] = Path.Combine(lab.Tree.Root, "git.calls");
        await File.WriteAllTextAsync(environment["FAKE_GIT_RELEASE"], "release", ct);
        var git = new Git(FakeOpenCode.Executable, lab.Git.ControlFolder, environment);
        await git.CheckAsync(lab.Tree.Repository, ct, "status", "--porcelain=v1");
        var previous = JsonNode.Parse(File.ReadLines(environment["FAKE_GIT_CALLS"]).Last())![3]!.GetValue<string>()["core.hooksPath=".Length..];
        var marker = Path.Combine(lab.Tree.Root, "hook.marker");
        if (OperatingSystem.IsWindows())
        {
            await Assert.That(Directory.Exists(previous)).IsFalse();
            await Assert.That(Path.GetDirectoryName(previous)).IsEqualTo(lab.Git.ControlFolder);
            Directory.CreateDirectory(previous);
            await File.WriteAllTextAsync(Path.Combine(previous, "pre-commit"), $"#!/bin/sh\nprintf ran > '{marker.Replace('\\', '/')}'\n", ct);
        }
        else await Assert.That(previous).IsEqualTo("/dev/null");
        await File.AppendAllTextAsync(Path.Combine(lab.Tree.Repository, "README.md"), "change\n", ct);
        var result = await Settlement.SettleAsync(lab.Backend, lab.Ledger, run, null, ct, git);
        await Assert.That(result.State).IsEqualTo("done");
        await Assert.That(result.Commit).IsNotNull();
        await Assert.That(File.Exists(marker)).IsFalse();
        var hooks = File.ReadLines(environment["FAKE_GIT_CALLS"]).Select(x => JsonNode.Parse(x)![3]!.GetValue<string>()).ToList();
        if (OperatingSystem.IsWindows()) await Assert.That(hooks.Distinct().Count()).IsEqualTo(hooks.Count);
        else await Assert.That(hooks.All(x => x == "core.hooksPath=/dev/null")).IsTrue();
    }

    [Test]
    [Timeout(60_000)]
    public async Task CtrlC_DuringCommit_CommitCompletesAndNoIndexLock(CancellationToken ct)
    {
        using var lab = new CoreLab();
        var run = await lab.TaskAsync("inspect", ct);
        run.Record.Spec.Kind = "code";
        run.Record.Spec.Scope = ["README.md"];
        await File.AppendAllTextAsync(Path.Combine(lab.Tree.Repository, "README.md"), "change\n", ct);
        var lockPath = Path.Combine(lab.Tree.Repository, ".git", "index.lock");
        var environment = WrapperEnvironment(lab, lockPath, "block");
        var git = new Git(FakeOpenCode.Executable, lab.Git.ControlFolder, environment);
        using var shutdown = CancellationTokenSource.CreateLinkedTokenSource(ct);
        lab.Backend.OnPoll = (live, _) => { if (live.Count > 0) lab.End(run); return Task.FromResult(new PollResult([])); };
        var scheduler = new Scheduler(new Dictionary<string, IWorkerBackend> { [lab.Backend.Name] = lab.Backend },
            lab.Ledger, lab.Tree.Repository, AllowList.From(null), lab.Policy, 1, shutdown.Token, git);
        var running = scheduler.RunAsync([run]);
        try
        {
            while (!File.Exists(environment["FAKE_GIT_READY"])) await Task.Delay(10, ct);
            shutdown.Cancel();
            while (!lab.Ledger.Read().Any(x => x.Type == "aborted")) await Task.Delay(10, ct);
            await File.WriteAllTextAsync(environment["FAKE_GIT_RELEASE"], "release", ct);
            await Assert.That(async () => await running).Throws<OperationCanceledException>();
            await Assert.That(run.State).IsEqualTo(TaskState.Done);
            await Assert.That(lab.Ledger.LoadResult(run.Id)!.Commit).IsNotNull();
            await Assert.That(File.Exists(lockPath)).IsFalse();
            await Assert.That(lab.Ledger.Read().Last(x => x.Task == run.Id).Type).IsEqualTo("final");
        }
        finally
        {
            await File.WriteAllTextAsync(environment["FAKE_GIT_RELEASE"], "release", CancellationToken.None);
            try { await running; }
            catch (OperationCanceledException) when (shutdown.IsCancellationRequested) { /* Expected supervisor shutdown. */ }
        }
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(true, false)]
    [Arguments(false, true)]
    [Timeout(60_000)]
    public async Task IndexLock_TimeoutRemovesOnlyHarnessLock(bool preexisting, bool temporaryIndex, CancellationToken ct)
    {
        using var lab = new CoreLab();
        var run = await lab.TaskAsync("inspect", ct);
        var lockPath = temporaryIndex ? Path.Combine(lab.Git.ControlFolder, "index-inspect.lock") : Path.Combine(lab.Tree.Repository, ".git", "index.lock");
        if (preexisting) await File.WriteAllTextAsync(lockPath, "foreign", ct);
        var environment = WrapperEnvironment(lab, lockPath, "leave-lock");
        var git = new Git(FakeOpenCode.Executable, lab.Git.ControlFolder, environment, TimeSpan.FromSeconds(3)).ForWorkspace(lab.Ledger, run.Id);
        await git.InitializeBaselineAsync(lab.Tree.Repository, ct);
        if (temporaryIndex) git = git.WithIndex(lockPath[..^5]);
        var result = await git.RunAsync(lab.Tree.Repository, ct, "commit", "-m", "Commit test");
        await Assert.That(File.Exists(environment["FAKE_GIT_READY"])).IsTrue();
        await Assert.That(result.TimedOut).IsTrue();
        await Assert.That(File.Exists(lockPath)).IsEqualTo(preexisting);
        var events = lab.Ledger.Read().Where(x => x.Type == "index-lock-removed").ToList();
        await Assert.That(events.Count).IsEqualTo(preexisting ? 0 : 1);
        if (preexisting) await Assert.That(await File.ReadAllTextAsync(lockPath, ct)).IsEqualTo("foreign");
        else await Assert.That(events.Single().Data!["path"]!.GetValue<string>()).IsEqualTo(lockPath);
    }

    internal static Dictionary<string, string> WrapperEnvironment(CoreLab lab, string lockPath, string mode) =>
        new(lab.Tree.ChildEnvironment().ToDictionary(x => x.Key, x => x.Value!))
        {
            ["FAKE_GIT_MODE"] = mode,
            ["FAKE_GIT_EXECUTABLE"] = Executables.Resolve("git"),
            ["FAKE_GIT_LOCK"] = lockPath,
            ["FAKE_GIT_READY"] = Path.Combine(lab.Tree.Root, "git.ready"),
            ["FAKE_GIT_RELEASE"] = Path.Combine(lab.Tree.Root, "git.release"),
        };
}
