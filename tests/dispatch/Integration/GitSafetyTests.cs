using System.Text.Json.Nodes;
using Dispatch.Tests.Support;
using TUnit.Assertions.Enums;

namespace Dispatch.Tests.Integration;

[Category("Integration")]
[NotInParallel]
public sealed class GitSafetyTests
{
    [Test]
    [Arguments("filter")]
    [Arguments("cancel")]
    [Arguments("owned-lock")]
    [Arguments("foreign-lock")]
    [Timeout(60_000)]
    public async Task ContinueOf_apply_keeps_git_write_protections(string protection, CancellationToken ct)
    {
        using var lab = new CoreLab("next-run");
        var source = await lab.SourceAttemptAsync(ct);
        var next = lab.Continuation();
        var repo = new LabRepo(lab.Tree);
        if (protection == "cancel")
        {
            var environment = WrapperEnvironment(lab, Path.Combine(lab.Tree.Root, "unused.lock"), "block");
            environment["FAKE_GIT_CALLS"] = Path.Combine(lab.Tree.Root, "apply.calls");
            var marker = Path.Combine(lab.Tree.Root, "hook.marker");
            File.WriteAllText(Path.Combine(repo.Root, ".git", "hooks", "post-index-change"), $"#!/bin/sh\nprintf ran > '{marker.Replace('\\', '/')}'\n");
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(Path.Combine(repo.Root, ".git", "hooks", "post-index-change"), UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var git = new Git(FakeOpenCode.Executable, lab.Git.ControlFolder, environment);
            using var cancel = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var preparing = Worktree.PrepareAsync(next, lab.Ledger, repo.Root, cancel.Token, git);
            try
            {
                while (!File.Exists(environment["FAKE_GIT_READY"])) await Task.Delay(10, ct);
                cancel.Cancel();
                File.WriteAllText(environment["FAKE_GIT_RELEASE"], "release");
                await preparing;
            }
            finally
            {
                File.WriteAllText(environment["FAKE_GIT_RELEASE"], "release");
                await preparing;
            }
            await Assert.That(File.ReadAllText(Path.Combine(next.Record.Workspace.Path!, "source.txt"))).IsEqualTo("source work\n");
            await Assert.That(File.Exists(marker)).IsFalse();
            var apply = File.ReadLines(environment["FAKE_GIT_CALLS"]).Select(line => JsonNode.Parse(line)!.AsArray().Select(x => x!.GetValue<string>()).ToArray())
                .Single(args => args.Contains("apply"));
            var hooks = apply[3]["core.hooksPath=".Length..];
            if (OperatingSystem.IsWindows())
            {
                await Assert.That(Path.GetDirectoryName(hooks)).IsEqualTo(lab.Git.ControlFolder);
                await Assert.That(Directory.Exists(hooks)).IsFalse();
            }
            else await Assert.That(hooks).IsEqualTo("/dev/null");
            await Assert.That(apply).IsEquivalentTo((string[])[ "-C", next.Record.Workspace.Path!, "-c", "core.hooksPath=" + hooks,
                "-c", "core.fsmonitor=false", "-c", "commit.gpgSign=false", "-c", "gc.auto=0", "-c", "maintenance.auto=false",
                .. OperatingSystem.IsWindows() ? new[] { "-c", "core.longpaths=true" } : [], "-c", "apply.ignoreWhitespace=false",
                "apply", "--index", "--whitespace=nowarn", "--", Path.Combine(source.Ledger.Dir, "source", "wip.patch") ], CollectionOrdering.Matching);
            await Assert.That(lab.Ledger.Read().Single(x => x.Type == "worktree").Data!["continueOf"]!.GetValue<string>()).IsEqualTo("2026-10-31-source/source");
            return;
        }
        await Worktree.PrepareAsync(next, lab.Ledger, repo.Root, ct, lab.Git);
        var path = next.Record.Workspace.Path!;
        // Replay the same collected patch into its clean preimage to isolate the apply boundary.
        await repo.GitAsync(["-C", path, "reset", "--hard", "HEAD"], ct);
        var patch = Path.Combine(source.Ledger.Dir, "source", "wip.patch");
        if (protection == "filter")
        {
            await repo.GitAsync(["config", "filter.fixture.clean", "cat"], ct);
            var git = lab.Git.ForWorkspace(lab.Ledger, next.Id);
            await Assert.That(async () => await git.CheckAsync(path, ct, "apply", "--index", "--whitespace=nowarn", "--", patch))
                .ThrowsExactly<GitConfigChangedException>().WithMessage("git-config-changed: refusing worker-changed filters");
            var change = lab.Ledger.Read().Last(x => x.Type == "git-config-changed");
            await Assert.That(change.Task).IsEqualTo(next.Id);
            await Assert.That(change.Data!["keys"]!.AsArray().Select(x => x!.GetValue<string>())).IsEquivalentTo(new[] { "filter.fixture.clean" }, CollectionOrdering.Matching);
            await Assert.That(File.Exists(Path.Combine(path, "source.txt"))).IsFalse();
            return;
        }
        var lockPath = Path.GetFullPath(await repo.GitAsync(["-C", path, "rev-parse", "--git-path", "index.lock"], ct), path);
        var foreign = protection == "foreign-lock";
        if (foreign) File.WriteAllText(lockPath, "foreign lock bytes");
        var wrapper = WrapperEnvironment(lab, lockPath, "leave-lock");
        var timed = new Git(FakeOpenCode.Executable, lab.Git.ControlFolder, wrapper, TimeSpan.FromSeconds(3)).ForWorkspace(lab.Ledger, next.Id);
        var result = await timed.RunAsync(path, ct, "apply", "--index", "--whitespace=nowarn", "--", patch);
        await Assert.That(result.TimedOut).IsTrue();
        await Assert.That(File.Exists(lockPath)).IsEqualTo(foreign);
        if (foreign) await Assert.That(File.ReadAllText(lockPath)).IsEqualTo("foreign lock bytes");
        await Assert.That(lab.Ledger.Read().Where(x => x.Type == "index-lock-removed").Select(x => x.Data!["path"]!.GetValue<string>()))
            .IsEquivalentTo(foreign ? [] : new[] { lockPath }, CollectionOrdering.Matching);
        await Assert.That(File.Exists(Path.Combine(path, "source.txt"))).IsFalse();
    }

    [Test]
    [Timeout(60_000)]
    public async Task ContinueOf_preserves_git_conversion_semantics(CancellationToken ct)
    {
        using var lab = new CoreLab("next-run");
        var repo = await LabRepo.CreateAsync(lab.Tree, ct);
        var oldLink = await repo.GitAsync(["rev-parse", "HEAD"], ct);
        await repo.GitAsync(["commit", "--allow-empty", "-m", "New gitlink target"], ct);
        var newLink = await repo.GitAsync(["rev-parse", "HEAD"], ct);
        await repo.GitAsync(["config", "filter.fixture.clean", "sed s/WORK/TREE/g"], ct);
        await repo.GitAsync(["config", "filter.fixture.smudge", "sed s/TREE/WORK/g"], ct);
        await repo.GitAsync(["config", "filter.fixture.required", "true"], ct);
        await repo.GitAsync(["config", "apply.whitespace", "fix"], ct);
        await repo.GitAsync(["config", "apply.ignoreWhitespace", "change"], ct);
        File.WriteAllText(Path.Combine(repo.Root, ".gitattributes"), "filtered.txt filter=fixture text eol=lf\ncrlf.txt text eol=crlf\n");
        File.WriteAllText(Path.Combine(repo.Root, "filtered.txt"), "WORK before\n");
        File.WriteAllText(Path.Combine(repo.Root, "crlf.txt"), "before\r\n");
        await repo.GitAsync(["add", "-A"], ct);
        await repo.GitAsync(["update-index", "--add", "--cacheinfo", $"160000,{oldLink},sub"], ct);
        await repo.GitAsync(["commit", "-m", "Conversions and gitlink"], ct);
        var source = await lab.SourceAttemptAsync(ct, edit: async path =>
        {
            File.WriteAllText(Path.Combine(path, "filtered.txt"), "WORK after \t\n");
            File.WriteAllText(Path.Combine(path, "crlf.txt"), "after\r\n");
            await repo.GitAsync(["-C", path, "update-index", "--cacheinfo", $"160000,{newLink},sub"], ct);
        });
        var next = lab.Continuation();
        await Worktree.PrepareAsync(next, lab.Ledger, repo.Root, ct, lab.Git);
        var path = next.Record.Workspace.Path!;
        await Assert.That(source.Result.FilesChanged).IsEquivalentTo(new[] { "crlf.txt", "filtered.txt", "sub" }, CollectionOrdering.Matching);
        await Assert.That(File.ReadAllBytes(Path.Combine(path, "crlf.txt"))).IsEquivalentTo(System.Text.Encoding.UTF8.GetBytes("after\r\n"), CollectionOrdering.Matching);
        await Assert.That(File.ReadAllText(Path.Combine(path, "filtered.txt"))).IsEqualTo("WORK after \t\n");
        await Assert.That(await lab.Git.CheckAsync(path, ct, "show", ":filtered.txt")).IsEqualTo("TREE after \t\n");
        await Assert.That(await lab.Git.CheckAsync(path, ct, "show", ":crlf.txt")).IsEqualTo("after\n");
        await Assert.That(await repo.GitAsync(["-C", path, "ls-files", "--stage", "sub"], ct)).IsEqualTo($"160000 {newLink} 0\tsub");
        await Assert.That(File.Exists(Path.Combine(path, "sub", ".git"))).IsFalse();
        await Assert.That(await repo.GitAsync(["-C", path, "rev-parse", "HEAD"], ct)).IsEqualTo(source.Run.Record.Workspace.BaseSha);
    }

    [Test]
    [Timeout(60_000)]
    public async Task ConfigGate_reads_an_older_baseline_without_its_system_entries(CancellationToken ct)
    {
        // A baseline saved before system scope was ignored still lists system entries such as Git LFS filters.
        using var lab = new CoreLab();
        var repo = await LabRepo.CreateAsync(lab.Tree, ct);
        var git = lab.Git.ForWorkspace(lab.Ledger, "inspect");
        await git.InitializeBaselineAsync(repo.Root, ct);
        var path = Path.Combine(git.ControlFolder, "git-baseline.json");
        var baseline = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        baseline["filter.lfs.clean"] = new JsonArray(new JsonObject
        {
            ["scope"] = "system", ["origin"] = "file:C:/Program Files/Git/etc/gitconfig", ["key"] = "filter.lfs.clean",
            ["hasValue"] = true, ["sha256"] = "00",
        });
        File.WriteAllText(path, baseline.ToJsonString());
        await new GitConfigGate(git).CheckAsync(repo.Root, ct);
        await Assert.That(lab.Ledger.Read().Any(x => x.Type == "git-config-changed")).IsFalse();
    }

    [Test]
    [Timeout(60_000)]
    public async Task ConfigGate_ignores_system_changes_but_reports_global_local_and_worktree_changes(CancellationToken ct)
    {
        using var lab = new CoreLab();
        var repo = await LabRepo.CreateAsync(lab.Tree, ct);
        var environment = lab.Tree.ChildEnvironment();
        environment.Remove("GIT_CONFIG_NOSYSTEM");
        environment["GIT_CONFIG_SYSTEM"] = Path.Combine(lab.Tree.Root, "system.gitconfig");
        File.WriteAllText(environment["GIT_CONFIG_SYSTEM"]!, "");
        async Task<string> GitAsync(params string[] args)
        {
            using var child = new CapturedProcess();
            var result = await child.RunAsync("git", args, repo.Root, environment, ct);
            result.RequireSuccess("scoped config fixture");
            return result.Stdout.TrimEnd();
        }
        await GitAsync("config", "--local", "extensions.worktreeConfig", "true");
        var scopes = new[] { "system", "global", "local", "worktree" };
        foreach (var scope in scopes)
            await GitAsync("config", "--" + scope, $"filter.{scope}.clean", "before");
        var git = new Git(Executables.Resolve("git"), lab.Git.ControlFolder,
            environment.ToDictionary(x => x.Key, x => x.Value!)).ForWorkspace(lab.Ledger, "inspect");
        await git.InitializeBaselineAsync(repo.Root, ct);
        foreach (var scope in scopes)
            await GitAsync("config", "--" + scope, $"filter.{scope}.clean", "after");
        // Prove system config is loaded, not hidden by the fixture's usual NOSYSTEM isolation.
        await Assert.That(await GitAsync("config", "--show-scope", "--get", "filter.system.clean")).IsEqualTo("system\tafter");
        await Assert.That((await git.CheckAsync(repo.Root, ct, "config", "--show-scope", "--get", "filter.system.clean")).TrimEnd()).IsEqualTo("system\tafter");
        await Assert.That(async () => await new GitConfigGate(git).CheckAsync(repo.Root, ct))
            .ThrowsExactly<GitConfigChangedException>().WithMessage("git-config-changed: refusing worker-changed filters");
        var changes = lab.Ledger.Read().Where(x => x.Type == "git-config-changed").ToArray();
        await Assert.That(changes.Length).IsEqualTo(1);
        await Assert.That(changes[0].Data!["keys"]!.AsArray().Select(x => x!.GetValue<string>()))
            .IsEquivalentTo(new[] { "filter.global.clean", "filter.local.clean", "filter.worktree.clean" }, CollectionOrdering.Matching);
        await Assert.That(changes[0].Data!["blocking"]!.GetValue<bool>()).IsTrue();
    }

    [Test]
    [Timeout(60_000)]
    public async Task ConfigGate_FiltersOnlyForeignAutoSetup(CancellationToken ct)
    {
        using var lab = new CoreLab("config-test");
        var repo = await LabRepo.CreateAsync(lab.Tree, ct);
        await repo.GitAsync(["remote", "add", "fixture", repo.Root], ct);
        await repo.GitAsync(["update-ref", "refs/remotes/fixture/main", "HEAD"], ct);
        await repo.GitAsync(["config", "branch.autoSetupRebase", "always"], ct);
        await repo.GitAsync(["worktree", "add", "--track", "-b", "old.foreign", Path.Combine(lab.Tree.Root, "old"), "fixture/main"], ct);
        var run = await lab.TaskAsync("inspect", ct);
        var baselinePath = Path.Combine(lab.Git.ControlFolder, "git-baseline.json");
        var baseline = File.ReadAllText(baselinePath);
        await repo.GitAsync(["config", "--remove-section", "branch.old.foreign"], ct);
        // The harness names its branches dispatch/<run>/<task>; any other branch is foreign.
        const string foreign = "lane/foreign.with.dots";
        await repo.GitAsync(["worktree", "add", "--track", "-b", foreign, Path.Combine(lab.Tree.Root, "new"), "fixture/main"], ct);
        await Assert.That(await repo.GitAsync(["config", $"branch.{foreign}.rebase"], ct)).IsEqualTo("true");
        foreach (var branch in new[] { "dispatch/config-test/owned.with.dots", "dispatch/other-run/sibling-owned" })
            foreach (var key in new[] { "remote", "merge", "rebase" })
                await repo.GitAsync(["config", $"branch.{branch}.{key}", "changed"], ct);
        await repo.GitAsync(["config", $"branch.{foreign}.description", "visible"], ct);
        await repo.GitAsync(["config", $"branch.{foreign}.remote.extra", "visible"], ct);
        await repo.GitAsync(["config", "core.editor", "visible"], ct);
        await repo.GitAsync(["config", "filter.x.clean", "cat"], ct);
        File.WriteAllText(Path.Combine(repo.Root, ".git", "hooks", "post-checkout"), "changed hook");
        var gate = new GitConfigGate(lab.Git.ForWorkspace(lab.Ledger, run.Id));
        await Assert.That(async () => await gate.CheckAsync(repo.Root, ct)).Throws<GitConfigChangedException>();
        var change = lab.Ledger.Read().Last(x => x.Type == "git-config-changed");
        await Assert.That(string.Join(",", change.Data!["keys"]!.AsArray().Select(x => x!.GetValue<string>())))
            .IsEqualTo($"branch.dispatch/config-test/owned.with.dots.merge,branch.dispatch/config-test/owned.with.dots.rebase,branch.dispatch/config-test/owned.with.dots.remote,branch.dispatch/other-run/sibling-owned.merge,branch.dispatch/other-run/sibling-owned.rebase,branch.dispatch/other-run/sibling-owned.remote,branch.{foreign}.description,branch.{foreign}.remote.extra,core.editor,filter.x.clean,hooks/post-checkout");
        await Assert.That(change.Data["blocking"]!.GetValue<bool>()).IsTrue();
        await Assert.That(File.ReadAllText(baselinePath)).IsEqualTo(baseline);
    }

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
