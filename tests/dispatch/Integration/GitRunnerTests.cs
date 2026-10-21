using System.Text.Json.Nodes;
using Dispatch.Tests.Support;

namespace Dispatch.Tests.Integration;

[Category("Integration")]
[NotInParallel("processes")]
public sealed class GitRunnerTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    [Timeout(180_000)]
    public async Task Proc_ExitedParentWithOpenChildPipes_ReturnsWithinTimeoutAndGrace(bool cancel, CancellationToken cancellationToken)
    {
        using var tree = new TempTree();
        var pidFile = Path.Combine(tree.Root, "child.pid");
        using var window = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (cancel) window.CancelAfter(TimeSpan.FromSeconds(2));
        var started = Stopwatch.StartNew();
        try
        {
            var result = await Proc.RunAsync(FakeOpenCode.Executable, ["--pipe-parent", pidFile], tree.Root,
                window.Token, cancel ? TimeSpan.FromSeconds(20) : TimeSpan.FromSeconds(2),
                environment: tree.ChildEnvironment().ToDictionary(x => x.Key, x => x.Value!));
            await Assert.That(started.Elapsed).IsLessThan(TimeSpan.FromSeconds(5));
            await Assert.That(result.TimedOut).IsTrue();
            await Assert.That(result.ExitCode).IsEqualTo(-1);
            await Assert.That(result.Output).Contains("parent stdout");
            await Assert.That(result.Output).Contains("child stdout");
            await Assert.That(result.Stderr).Contains("parent stderr");
            await Assert.That(result.Stderr).Contains("child stderr");
        }
        finally
        {
            if (File.Exists(pidFile))
            {
                try
                {
                    using var child = Process.GetProcessById(int.Parse(await File.ReadAllTextAsync(pidFile, CancellationToken.None)));
                    if (!child.HasExited) child.Kill(entireProcessTree: true);
                    await child.WaitForExitAsync(CancellationToken.None);
                }
                catch (ArgumentException)
                {
                    // The finite-lived child already exited and was reaped.
                }
            }
        }
    }

    [Test]
    [Timeout(180_000)]
    public async Task GitWarningOnStderr_DoesNotReachIds(CancellationToken cancellationToken)
    {
        using var tree = new TempTree();
        var environment = tree.ChildEnvironment().ToDictionary(x => x.Key, x => x.Value!);
        environment["FAKE_GIT_PROBE"] = "warn";
        var git = new Git(FakeOpenCode.Executable, Path.Combine(tree.Root, "control"), environment);
        var baseSha = await git.IdAsync(tree.Repository, cancellationToken, "rev-parse", "--verify", "main^{commit}");
        var commit = await git.IdAsync(tree.Repository, cancellationToken, "rev-parse", "HEAD");
        await Assert.That(baseSha).IsEqualTo(new string('a', 40));
        await Assert.That(commit).IsEqualTo(new string('b', 40));
        var result = await git.RunAsync(tree.Repository, cancellationToken, "rev-parse", "HEAD");
        await Assert.That(result.Stderr).IsEqualTo("warning: synthetic git diagnostic\n");
        await Assert.That(result.Stdout.SequenceEqual(Encoding.UTF8.GetBytes(new string('b', 40) + "\n"))).IsTrue();
    }

    [Test]
    [Timeout(180_000)]
    public async Task Command_GitCallsLeaveNoCommandControlFolder(CancellationToken cancellationToken)
    {
        using var tree = new TempTree();
        var repo = await LabRepo.CreateAsync(tree, cancellationToken);
        var run = await PrepareAsync(tree, repo, ["README.md"], cancellationToken);
        await File.AppendAllTextAsync(Path.Combine(run.Worktree, "README.md"), "change\n", cancellationToken);
        await CollectAsync(tree, run.RunId, cancellationToken);
        var control = Path.Combine(tree.DispatchHome, "state", "control");
        var folders = Directory.Exists(control) ? Directory.GetDirectories(control, "cmd-*") : [];
        await Assert.That(folders).IsEmpty();
    }

    [Test]
    [Timeout(180_000)]
    public Task Commit_DoesNotRunRepoHooks(CancellationToken cancellationToken) => CheckHooksAsync(false, cancellationToken);

    [Test]
    [Timeout(180_000)]
    public Task Commit_IgnoresRepoHooksPath(CancellationToken cancellationToken) => CheckHooksAsync(true, cancellationToken);

    static async Task CheckHooksAsync(bool customPath, CancellationToken ct)
    {
        using var tree = new TempTree();
        using var controlTree = new TempTree();
        var repo = await LabRepo.CreateAsync(tree, ct);
        var control = await LabRepo.CreateAsync(controlTree, ct);
        var run = await PrepareAsync(tree, repo, customPath ? ["README.md", ".husky/**"] : ["README.md"], ct);
        string[] hooks = ["pre-commit", "prepare-commit-msg", "post-commit", "post-index-change", "reference-transaction"];
        await InstallHooksAsync(control, control.Root, controlTree.Root, hooks, customPath, ct);
        await File.AppendAllTextAsync(Path.Combine(control.Root, "README.md"), "control\n", ct);
        await control.GitAsync(["add", "README.md"], ct);
        await control.GitAsync(["commit", "-m", "Control hooks"], ct);
        foreach (var hook in hooks)
            await Assert.That(File.Exists(Path.Combine(controlTree.Root, hook + ".marker"))).IsTrue();

        await InstallHooksAsync(repo, run.Worktree, tree.Root, hooks, customPath, ct);
        await File.AppendAllTextAsync(Path.Combine(run.Worktree, "README.md"), "harness\n", ct);
        await CollectAsync(tree, run.RunId, ct);
        await Assert.That(await repo.GitAsync(["-C", run.Worktree, "rev-parse", "HEAD"], ct)).IsNotEqualTo(run.BaseSha);
        foreach (var hook in hooks)
            await Assert.That(File.Exists(Path.Combine(tree.Root, hook + ".marker"))).IsFalse();
    }

    static async Task InstallHooksAsync(LabRepo repo, string worktree, string markers, string[] hooks, bool customPath, CancellationToken ct)
    {
        var folder = customPath ? Path.Combine(worktree, ".husky") : Path.Combine(repo.Root, ".git", "hooks");
        Directory.CreateDirectory(folder);
        if (customPath) await repo.GitAsync(["config", "core.hooksPath", ".husky"], ct);
        foreach (var hook in hooks)
        {
            var path = Path.Combine(folder, hook);
            var marker = Path.Combine(markers, hook + ".marker").Replace('\\', '/').Replace("'", "'\\''");
            await File.WriteAllTextAsync(path, $"#!/bin/sh\nprintf ran > '{marker}'\n", ct);
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    [Test]
    [Timeout(180_000)]
    public async Task Scope_NonAsciiPath_InScope(CancellationToken cancellationToken)
    {
        using var tree = new TempTree();
        var repo = await LabRepo.CreateAsync(tree, cancellationToken);
        var run = await PrepareAsync(tree, repo, ["日本語.txt"], cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(run.Worktree, "日本語.txt"), "change\n", cancellationToken);
        await CollectAsync(tree, run.RunId, cancellationToken);
        var result = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(run.TaskDir, "result.json"), cancellationToken))!;
        await Assert.That(result["filesChanged"]!.AsArray().Select(x => x!.GetValue<string>())).IsEquivalentTo(["日本語.txt"]);
        await Assert.That(result["outOfScope"]!.AsArray()).IsEmpty();
    }

    [Test]
    [Timeout(180_000)]
    public async Task Scope_RenameOutOfScopeSource_IsFlagged(CancellationToken cancellationToken)
    {
        using var tree = new TempTree();
        var repo = await LabRepo.CreateAsync(tree, cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(repo.Root, "outside.txt"), "unchanged content\n", cancellationToken);
        await repo.GitAsync(["add", "outside.txt"], cancellationToken);
        await repo.GitAsync(["commit", "-m", "Add rename source"], cancellationToken);
        var run = await PrepareAsync(tree, repo, ["inside.txt"], cancellationToken);
        File.Move(Path.Combine(run.Worktree, "outside.txt"), Path.Combine(run.Worktree, "inside.txt"));
        await CollectAsync(tree, run.RunId, cancellationToken, expectedExit: 1);
        var result = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(run.TaskDir, "result.json"), cancellationToken))!;
        await Assert.That(result["filesChanged"]!.AsArray().Select(x => x!.GetValue<string>())).IsEquivalentTo(["outside.txt", "inside.txt"]);
        await Assert.That(result["outOfScope"]!.AsArray().Select(x => x!.GetValue<string>())).IsEquivalentTo(["outside.txt"]);
    }

    [Test]
    [Arguments("all", "short")]
    [Arguments("none", "log")]
    [Arguments("all", "log")]
    [Timeout(180_000)]
    public async Task DiffPatch_RoundTripsExactly(string ignoreSubmodules, string submoduleFormat, CancellationToken cancellationToken)
    {
        using var tree = new TempTree();
        var repo = await LabRepo.CreateAsync(tree, cancellationToken);
        var oldLink = await repo.GitAsync(["rev-parse", "HEAD"], cancellationToken);
        await repo.GitAsync(["commit", "--allow-empty", "-m", "Create gitlink target"], cancellationToken);
        var newLink = await repo.GitAsync(["rev-parse", "HEAD"], cancellationToken);
        await repo.GitAsync(["update-index", "--add", "--cacheinfo", $"160000,{oldLink},module"], cancellationToken);
        await repo.GitAsync(["commit", "-m", "Add gitlink"], cancellationToken);
        // Older Git also applies diff.ignoreSubmodules when detecting changes for a commit.
        // Enable the hostile diff settings after the submodule-only fixture commit.
        await repo.GitAsync(["config", "diff.noprefix", "true"], cancellationToken);
        await repo.GitAsync(["config", "diff.mnemonicPrefix", "true"], cancellationToken);
        await repo.GitAsync(["config", "diff.ignoreSubmodules", ignoreSubmodules], cancellationToken);
        await repo.GitAsync(["config", "diff.submodule", submoduleFormat], cancellationToken);
        var run = await PrepareAsync(tree, repo, ["*.txt", "*.bin"], cancellationToken);
        await repo.GitAsync(["-C", run.Worktree, "update-index", "--cacheinfo", $"160000,{newLink},module"], cancellationToken);
        var files = new Dictionary<string, byte[]>
        {
            ["crlf.txt"] = "one\r\ntwo\r\n"u8.ToArray(),
            ["no-newline.txt"] = "no final newline"u8.ToArray(),
            ["binary.bin"] = [0, 1, 255, 0, 128],
            ["legacy.txt"] = [0xff, 0xfe, 0x61, 0x0d, 0x0a],
            ["日本語.txt"] = "content\n"u8.ToArray(),
        };
        foreach (var (name, bytes) in files)
            await File.WriteAllBytesAsync(Path.Combine(run.Worktree, name), bytes, cancellationToken);
        await CollectAsync(tree, run.RunId, cancellationToken, expectedExit: 1);
        var apply = Path.Combine(tree.WorktreeRoot, "apply");
        await repo.GitAsync(["worktree", "add", "--detach", apply, run.BaseSha], cancellationToken);
        await repo.GitAsync(["-C", apply, "apply", "--index", Path.Combine(run.TaskDir, "diff.patch")], cancellationToken);
        var actual = await repo.GitAsync(["-C", apply, "write-tree"], cancellationToken);
        var expected = await repo.GitAsync(["-C", run.Worktree, "rev-parse", "HEAD^{tree}"], cancellationToken);
        await Assert.That(actual).IsEqualTo(expected);
        foreach (var (name, bytes) in files)
            await Assert.That(File.ReadAllBytes(Path.Combine(apply, name)).SequenceEqual(bytes)).IsTrue();
        var result = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(run.TaskDir, "result.json"), cancellationToken))!;
        await Assert.That(result["filesChanged"]!.AsArray().Select(x => x!.GetValue<string>())).IsEquivalentTo([.. files.Keys, "module"]);
        await Assert.That(result["outOfScope"]!.AsArray().Select(x => x!.GetValue<string>())).IsEquivalentTo(["module"]);
        await Assert.That(result["diffStat"]!.GetValue<string>()).Contains("6 files changed");
    }

    static async Task<(string RunId, string Worktree, string TaskDir, string BaseSha)> PrepareAsync(
        TempTree tree, LabRepo repo, string[] scope, CancellationToken ct)
    {
        var config = Directory.CreateDirectory(Path.Combine(tree.DispatchHome, "config")).FullName;
        await File.WriteAllTextAsync(Path.Combine(config, "config.jsonc"), """{"schemaVersion":2,"preferences":{"backend":"opencode"},"restrictions":{"routes":["opencode:fixture/model"],"deny":[{"name":"no-edits","action":"edit","resources":["*"]}]},"instructions":["Report findings only."]}""", ct);
        var tasks = Path.Combine(tree.Root, "tasks.jsonc");
        var task = JsonNode.Parse("""{"schemaVersion":2,"run":"git-test","tasks":[{"id":"inspect","kind":"code","base":"main","route":"fixture/model","prompt":"Inspect the lab repository."}]}""")!;
        task["tasks"]![0]!["scope"] = new JsonArray(scope.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray());
        await File.WriteAllTextAsync(tasks, task.ToJsonString(), ct);
        using var harness = new HarnessProcess(tree);
        (await harness.RunAsync(["run", tasks, "--repo", repo.Root, "--max", "1"], ct,
            FakeOpenCode.Environment(tree, "one-task.jsonl").ToDictionary(x => x.Key, x => (string?)x.Value))).RequireSuccess("prepare harness task");
        var ledger = Directory.GetDirectories(Path.Combine(tree.DispatchHome, "state", "ledgers")).Single();
        var taskDir = Path.Combine(ledger, "inspect");
        var record = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(taskDir, "task.json"), ct))!;
        return (Path.GetFileName(ledger), record["workspace"]!["path"]!.GetValue<string>(), taskDir, record["workspace"]!["baseSha"]!.GetValue<string>());
    }

    static async Task CollectAsync(TempTree tree, string runId, CancellationToken ct, int expectedExit = 0)
    {
        using var harness = new HarnessProcess(tree);
        var result = await harness.RunAsync(["collect", runId, "inspect"], ct,
            FakeOpenCode.Environment(tree, "one-task.jsonl").ToDictionary(x => x.Key, x => (string?)x.Value));
        await Assert.That(result.ExitCode).IsEqualTo(expectedExit);
    }
}
