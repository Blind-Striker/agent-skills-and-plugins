using Dispatch.Tests.Support;

namespace Dispatch.Tests.Integration;

[Category("Integration")]
[NotInParallel]
public sealed class WorkspaceTests
{
    [Test]
    [Timeout(60_000)]
    public async Task InPlace_CommitNone_SnapshotsAnUnbornBranch(CancellationToken ct)
    {
        using var lab = new CoreLab("unborn");
        var run = await InPlaceAsync(lab, ct);
        var repo = new LabRepo(lab.Tree);
        await repo.GitAsync(["checkout", "--orphan", "empty"], ct);
        await repo.GitAsync(["rm", "-rf", "."], ct);
        lab.Backend.OnPrompt = async (input, token) =>
        {
            await File.WriteAllBytesAsync(Path.Combine(repo.Root, "task.txt"), [0, 255, 1], token);
            return input.Worker with { Turn = "turn-inspect" };
        };
        lab.Backend.OnPoll = (_, _) => { lab.End(run); return Task.FromResult(new PollResult([])); };

        await Assert.That(await lab.Scheduler(ct).RunAsync([run])).IsEqualTo(0);
        var result = lab.Ledger.LoadResult(run.Id)!;
        await Assert.That(result.BaseSha).IsNull();
        await Assert.That(result.Commit).IsNull();
        await Assert.That(result.FilesChanged).IsEquivalentTo(["task.txt"]);
        await Assert.That(await File.ReadAllTextAsync(Path.Combine(lab.Ledger.TaskDir(run.Id), "diff.patch"), ct)).Contains("GIT binary patch");
        await Assert.That(await repo.GitAsync(["branch", "--show-current"], ct)).IsEqualTo("empty");
    }

    [Test]
    [Arguments("branch")]
    [Arguments("path")]
    [Arguments("nested-root")]
    [Arguments("writers")]
    [Arguments("outside-dir")]
    [Timeout(60_000)]
    public async Task Workspace_UnsafeLayout_PlanRefusesExit2(string obstacle, CancellationToken ct)
    {
        using var lab = new CoreLab("workspace");
        var repo = await LabRepo.CreateAsync(lab.Tree, ct);
        var tasks = Path.Combine(lab.Tree.Root, "tasks.jsonc");
        var document = System.Text.Json.Nodes.JsonNode.Parse("""{"schemaVersion":2,"run":"workspace","tasks":[{"id":"inspect","kind":"code","base":"main","route":"fixture/model","scope":["a.txt"],"prompt":"Inspect."}]}""")!;
        var runId = Commands.CreateRunId("workspace");
        if (obstacle == "branch") await repo.GitAsync(["branch", $"dispatch/{runId}/inspect"], ct);
        else if (obstacle == "path") Directory.CreateDirectory(Path.Combine(lab.Tree.WorktreeRoot, runId, "inspect"));
        else
        {
            document["tasks"]![0]!.AsObject().Remove("base");
            document["tasks"]![0]!["workspace"] = "in-place";
            if (obstacle == "nested-root")
                Locations.Configure(new ConfigLocations { WorktreeRoot = Path.Combine(repo.Root, "nested") }, null);
            else if (obstacle == "outside-dir") document["tasks"]![0]!["dir"] = "..";
            else document["tasks"]!.AsArray().Add(System.Text.Json.Nodes.JsonNode.Parse("""{"id":"second","kind":"mechanical","workspace":"in-place","route":"fixture/model","scope":["b.txt"],"prompt":"Inspect."}"""));
        }
        await File.WriteAllTextAsync(tasks, document.ToJsonString(), ct);

        var plan = await Planner.LoadAsync(tasks, repo.Root, AllowList.From(null), ct, lab.Git);

        await Assert.That(plan.Report()).IsEqualTo(2);
        await Assert.That(File.Exists(Path.Combine(lab.Ledger.Dir, "run.json"))).IsFalse();
    }

    [Test]
    [Timeout(60_000)]
    public async Task Clean_InPlace_LeavesFolderRemovesTempAndIndex(CancellationToken ct)
    {
        using var tree = new TempTree();
        var repo = await LabRepo.CreateAsync(tree, ct);
        var tasks = Path.Combine(tree.Root, "tasks.jsonc");
        await File.WriteAllTextAsync(tasks, """{"schemaVersion":2,"run":"cleanup","defaults":{"backend":"opencode"},"policy":{"deny":[{"name":"no-edits","action":"edit","resources":["*"]}]},"tasks":[{"id":"inspect","kind":"code","workspace":"in-place","route":"fixture/model","scope":["**"],"prompt":"Inspect the lab repository."}]}""", ct);
        var environment = FakeOpenCode.Environment(tree, "one-task.jsonl").ToDictionary(x => x.Key, x => (string?)x.Value);
        using var harness = new HarnessProcess(tree);
        (await harness.RunAsync(["run", tasks, "--repo", repo.Root], ct, environment)).RequireSuccess("in-place run");
        var ledger = Directory.GetDirectories(Path.Combine(tree.DispatchHome, "state", "ledgers")).Single();
        var runId = Path.GetFileName(ledger);
        var info = System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(ledger, "run.json"), ct))!;
        var control = Path.Combine(tree.DispatchHome, "state", "control", info["controlId"]?.GetValue<string>() ?? runId);
        Directory.CreateDirectory(control);
        var index = Path.Combine(control, "index-inspect");
        await File.WriteAllTextAsync(index, "temporary", ct);
        await File.WriteAllTextAsync(index + ".lock", "temporary", ct);
        var temp = Path.Combine(tree.DispatchHome, "state", "worktrees", runId, ".tmp", "inspect");
        Directory.CreateDirectory(temp);
        var readOnly = Path.Combine(temp, "readonly");
        await File.WriteAllTextAsync(readOnly, "temporary", ct);
        File.SetAttributes(readOnly, FileAttributes.ReadOnly);
        await File.AppendAllTextAsync(Path.Combine(repo.Root, "README.md"), "keep changes\n", ct);
        var operatorIndex = await File.ReadAllBytesAsync(Path.Combine(repo.Root, ".git", "index"), ct);
        var head = await repo.GitAsync(["rev-parse", "HEAD"], ct);

        using var clean = new HarnessProcess(tree);
        (await clean.RunAsync(["clean", runId], ct)).RequireSuccess("clean in-place");
        using var repeat = new HarnessProcess(tree);
        (await repeat.RunAsync(["clean", runId], ct)).RequireSuccess("repeat clean in-place");

        await Assert.That(Directory.Exists(temp)).IsFalse();
        await Assert.That(File.Exists(index)).IsFalse();
        await Assert.That(File.Exists(index + ".lock")).IsFalse();
        await Assert.That(await File.ReadAllTextAsync(Path.Combine(repo.Root, "README.md"), ct)).Contains("keep changes");
        await Assert.That(File.ReadAllBytes(Path.Combine(repo.Root, ".git", "index")).SequenceEqual(operatorIndex)).IsTrue();
        await Assert.That(await repo.GitAsync(["rev-parse", "HEAD"], ct)).IsEqualTo(head);
        await Assert.That(await repo.GitAsync(["branch", "--show-current"], ct)).IsEqualTo("main");
    }

    [Test]
    [Timeout(60_000)]
    public async Task InPlace_TwoTasksSameTree_NeverOverlap(CancellationToken ct)
    {
        using var lab = new CoreLab("serialized");
        var first = await InPlaceAsync(lab, ct, id: "first");
        var second = await InPlaceAsync(lab, ct, id: "second");
        first.Record.Spec.Kind = "research";
        TaskState? secondAtFirstStart = null;
        var order = new List<string>();
        lab.Backend.OnPrompt = (input, _) =>
        {
            if (input.Worker.TaskId == first.Id) secondAtFirstStart = second.State;
            order.Add("start:" + input.Worker.TaskId);
            return Task.FromResult(input.Worker with { Turn = "turn-" + input.Worker.TaskId });
        };
        lab.Backend.OnPoll = (live, _) =>
        {
            foreach (var worker in live) lab.End(worker.TaskId == first.Id ? first : second);
            return Task.FromResult(new PollResult([]));
        };
        lab.Backend.OnCollect = (worker, _) =>
        {
            order.Add("end:" + worker.TaskId);
            return Task.FromResult(new WorkerReport("succeeded", "Report", new(), null, true, [], null, true));
        };

        await Assert.That(await lab.Scheduler(ct, max: 2).RunAsync([first, second])).IsEqualTo(0);
        await Assert.That(secondAtFirstStart).IsEqualTo(TaskState.Pending);
        await Assert.That(string.Join(",", order)).IsEqualTo("start:first,end:first,start:second,end:second");
        var events = lab.Ledger.Read();
        await Assert.That(events.FindIndex(x => x.Task == second.Id && x.Type == "worktree"))
            .IsGreaterThan(events.FindIndex(x => x.Task == first.Id && x.Type == "final"));
    }

    [Test]
    [Timeout(60_000)]
    public Task InPlace_CommitHarness_RunsNoHookOnCurrentBranch(CancellationToken ct) => CheckDispatchAsync("in-place", "harness", ct);

    [Test]
    [Arguments("worktree", "harness")]
    [Arguments("in-place", "none")]
    [Timeout(60_000)]
    public Task DotDispatchChange_NeverStagedAlwaysOutOfScope(string layout, string commit, CancellationToken ct) => CheckDispatchAsync(layout, commit, ct);

    static async Task CheckDispatchAsync(string layout, string commit, CancellationToken ct)
    {
        using var lab = new CoreLab("workspace");
        var run = await InPlaceAsync(lab, ct, commit);
        if (layout == "worktree")
        {
            run.Record.Spec.Workspace = "worktree";
            run.Record.Settings.Workspace = new() { Value = "worktree" };
            run.Record.Spec.Base = "main";
        }
        else
        {
            Directory.CreateDirectory(Path.Combine(lab.Tree.Repository, "nested"));
            run.Record.Spec.Dir = "nested";
        }
        run.Record.Spec.Scope = ["**"];
        var repo = new LabRepo(lab.Tree);
        var head = await repo.GitAsync(["rev-parse", "HEAD"], ct);
        WorkerLaunch? launched = null;
        var create = lab.Backend.OnCreate!;
        lab.Backend.OnCreate = (launch, token) => { launched = launch; return create(launch, token); };
        var marker = Path.Combine(lab.Tree.Root, "hook-marker");
        foreach (var hook in new[] { "pre-commit", "prepare-commit-msg", "post-commit", "post-index-change", "reference-transaction" })
        {
            var file = Path.Combine(repo.Root, ".git", "hooks", hook);
            await File.WriteAllTextAsync(file, $"#!/bin/sh\nprintf ran > '{marker.Replace('\\', '/').Replace("'", "'\\''")}'\n", ct);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        lab.Backend.OnPrompt = async (input, token) =>
        {
            var folder = layout == "in-place" ? repo.Root : run.Record.Workspace.Path!;
            await File.WriteAllTextAsync(Path.Combine(folder, "task.txt"), "worker\n", token);
            Directory.CreateDirectory(Path.Combine(folder, ".dispatch"));
            await File.WriteAllTextAsync(Path.Combine(folder, ".dispatch", "config.jsonc"), "{}\n", token);
            return input.Worker with { Turn = "turn-inspect" };
        };
        lab.Backend.OnPoll = (_, _) => { lab.End(run); return Task.FromResult(new PollResult([])); };

        await lab.Scheduler(ct).RunAsync([run]);

        var result = lab.Ledger.LoadResult(run.Id)!;
        await Assert.That(Locations.Same(launched!.Directory, layout == "in-place" ? Path.Combine(repo.Root, "nested") : Path.Combine(lab.Tree.WorktreeRoot, "workspace", "inspect"))).IsTrue();
        foreach (var name in new[] { "TEMP", "TMP", "TMPDIR" })
            await Assert.That(launched.Environment[name]).IsEqualTo(Path.Combine(lab.Tree.WorktreeRoot, "workspace", ".tmp", "inspect"));
        await Assert.That(result.OutOfScope).IsEquivalentTo([".dispatch/config.jsonc"]);
        await Assert.That(File.Exists(marker)).IsFalse();
        await Assert.That(await repo.GitAsync(["-C", run.Record.Workspace.Path!, "diff", "--cached", "--name-only"], ct)).IsEqualTo("");
        await Assert.That(await repo.GitAsync(["-C", layout == "in-place" ? repo.Root : run.Record.Workspace.Path!, "ls-tree", "-r", "--name-only", "HEAD", ".dispatch"], ct)).IsEqualTo("");
        if (layout == "in-place")
            await Assert.That(await repo.GitAsync(["branch", "--show-current"], ct)).IsEqualTo("main");
        if (commit == "harness")
        {
            await Assert.That(result.Commit).IsNotNull();
            await Assert.That(await repo.GitAsync(["-C", run.Record.Workspace.Path!, "show", "HEAD:task.txt"], ct)).IsEqualTo("worker");
        }
        else await Assert.That(await repo.GitAsync(["rev-parse", "HEAD"], ct)).IsEqualTo(head);
        if (layout == "worktree")
        {
            await Assert.That(run.Record.Workspace.Branch).IsEqualTo("dispatch/workspace/inspect");
            await Assert.That(run.Record.Workspace.BaseSha).IsEqualTo(head);
            await Assert.That(await Worktree.CleanAsync(lab.Ledger, run.Record, repo.Root, false, ct, lab.Git)).IsFalse();
            await Assert.That(Directory.Exists(run.Record.Workspace.Path)).IsTrue();
            await Assert.That(Directory.Exists(launched.Environment["TEMP"])).IsTrue();
            await Assert.That(await repo.GitAsync(["show-ref", "--verify", "refs/heads/dispatch/workspace/inspect"], ct)).Contains(result.Commit!);
            await Assert.That(await Worktree.CleanAsync(lab.Ledger, run.Record, repo.Root, true, ct, lab.Git)).IsTrue();
            await Assert.That(await Worktree.CleanAsync(lab.Ledger, run.Record, repo.Root, true, ct, lab.Git)).IsTrue();
            await Assert.That(Directory.Exists(run.Record.Workspace.Path)).IsFalse();
            await Assert.That(Directory.Exists(launched.Environment["TEMP"])).IsFalse();
            await Assert.That(await repo.GitAsync(["branch", "--list", "dispatch/workspace/inspect"], ct)).IsEqualTo("");
        }
    }

    [Test]
    [Arguments("plan")]
    [Arguments("run")]
    [Timeout(60_000)]
    public async Task InPlace_DirtyFolder_CommitHarness_RefusedExit2(string command, CancellationToken ct)
    {
        using var tree = new TempTree();
        var repo = await LabRepo.CreateAsync(tree, ct);
        await File.AppendAllTextAsync(Path.Combine(repo.Root, "README.md"), "operator\n", ct);
        var tasks = Path.Combine(tree.Root, "tasks.jsonc");
        await File.WriteAllTextAsync(tasks, """{"schemaVersion":2,"defaults":{"backend":"opencode"},"tasks":[{"id":"inspect","kind":"code","workspace":"in-place","commit":"harness","route":"fixture/model","scope":["README.md"],"prompt":"Inspect."}]}""", ct);
        using var harness = new HarnessProcess(tree);

        var result = await harness.RunAsync([command, tasks, "--repo", repo.Root], ct);

        await Assert.That(result.ExitCode).IsEqualTo(2);
        await Assert.That(result.Stdout + result.Stderr).Contains("workspace-dirty");
        await Assert.That(Directory.Exists(Path.Combine(tree.DispatchHome, "state", "ledgers"))).IsFalse();
    }

    [Test]
    [Timeout(60_000)]
    public async Task InPlace_DirtyAtTaskStart_FailsWorkspaceDirty(CancellationToken ct)
    {
        using var lab = new CoreLab();
        var run = await InPlaceAsync(lab, ct, "harness");
        await File.AppendAllTextAsync(Path.Combine(lab.Tree.Repository, "README.md"), "operator\n", ct);
        lab.Backend.OnPoll = (_, _) => { lab.End(run); return Task.FromResult(new PollResult([])); };

        await Assert.That(await lab.Scheduler(ct).RunAsync([run])).IsEqualTo(1);
        await Assert.That(lab.Backend.Calls.Any(x => x.Member == "CreateAsync")).IsFalse();
        await Assert.That(lab.Ledger.LoadResult(run.Id)!.Failures).Contains("workspace-dirty");
    }

    [Test]
    [Timeout(60_000)]
    public async Task InPlace_DirtyFolder_CommitNone_DiffsOnlyTaskChanges(CancellationToken ct)
    {
        using var lab = new CoreLab();
        var run = await InPlaceAsync(lab, ct);
        var repo = new LabRepo(lab.Tree);
        await File.WriteAllTextAsync(Path.Combine(repo.Root, "README.md"), "operator staged\n", ct);
        await repo.GitAsync(["add", "README.md"], ct);
        await File.AppendAllTextAsync(Path.Combine(repo.Root, "README.md"), "operator unstaged\n", ct);
        await File.WriteAllTextAsync(Path.Combine(repo.Root, "operator.txt"), "operator untracked\n", ct);
        var index = await File.ReadAllBytesAsync(Path.Combine(repo.Root, ".git", "index"), ct);
        var head = await repo.GitAsync(["rev-parse", "HEAD"], ct);
        lab.Backend.OnPrompt = async (input, token) =>
        {
            await File.WriteAllTextAsync(Path.Combine(repo.Root, "task.txt"), "worker\n", token);
            return input.Worker with { Turn = "turn-inspect" };
        };
        lab.Backend.OnPoll = (_, _) => { lab.End(run); return Task.FromResult(new PollResult([])); };

        await Assert.That(await lab.Scheduler(ct).RunAsync([run])).IsEqualTo(0);

        var result = lab.Ledger.LoadResult(run.Id)!;
        await Assert.That(result.CommitMode).IsEqualTo("none");
        await Assert.That(result.Commit).IsNull();
        await Assert.That(result.FilesChanged).IsEquivalentTo(["task.txt"]);
        await Assert.That(await repo.GitAsync(["rev-parse", "HEAD"], ct)).IsEqualTo(head);
        await Assert.That(await repo.GitAsync(["branch", "--format=%(refname:short)"], ct)).IsEqualTo("main");
        await Assert.That(File.ReadAllBytes(Path.Combine(repo.Root, ".git", "index")).SequenceEqual(index)).IsTrue();
        await Assert.That(await File.ReadAllTextAsync(Path.Combine(repo.Root, "README.md"), ct)).IsEqualTo("operator staged\noperator unstaged\n");
        await Assert.That(await File.ReadAllTextAsync(Path.Combine(repo.Root, "operator.txt"), ct)).IsEqualTo("operator untracked\n");
        await Assert.That(await File.ReadAllTextAsync(Path.Combine(lab.Ledger.TaskDir(run.Id), "diff.patch"), ct)).Contains("+worker");
        await Assert.That(lab.Ledger.Read().Single(x => x.Type == "worktree").Data!["startTree"]).IsNotNull();
        await Assert.That(lab.Ledger.Read().Single(x => x.Type == "collected").Data!["endTree"]).IsNotNull();
        var start = lab.Ledger.Read().Single(x => x.Type == "worktree").Data!["startTree"]!.GetValue<string>();
        var end = lab.Ledger.Read().Single(x => x.Type == "collected").Data!["endTree"]!.GetValue<string>();
        var apply = Path.Combine(lab.Tree.Root, "apply");
        await repo.GitAsync(["worktree", "add", "--detach", apply, head], ct);
        await repo.GitAsync(["-C", apply, "read-tree", "--reset", "-u", start], ct);
        await repo.GitAsync(["-C", apply, "apply", "--index", Path.Combine(lab.Ledger.TaskDir(run.Id), "diff.patch")], ct);
        await Assert.That(await repo.GitAsync(["-C", apply, "write-tree"], ct)).IsEqualTo(end);
        await Assert.That(File.Exists(Path.Combine(lab.Tree.Root, "control", "index-inspect"))).IsFalse();
    }

    static async Task<TaskRun> InPlaceAsync(CoreLab lab, CancellationToken ct, string? commit = null, string id = "inspect")
    {
        var run = await lab.TaskAsync(id, ct);
        run.Record.Spec.Kind = "code";
        run.Record.Spec.Workspace = "in-place";
        run.Record.Spec.Commit = commit;
        run.Record.Spec.Scope = ["task.txt"];
        run.Record.Settings.Workspace = new() { Value = "in-place" };
        run.Record.Settings.Commit = commit is null ? null : new() { Value = commit };
        run.Record.Workspace = new();
        lab.Ledger.SaveRecord(run.Record);
        return run;
    }
}
