using Dispatch.Tests.Support;
using TUnit.Assertions.Enums;

namespace Dispatch.Tests.Integration;

[Category("Integration")]
[NotInParallel]
public sealed class WorkspaceTests
{
    [Test]
    [Timeout(60_000)]
    public async Task ContinueOf_patch_context_is_independent_of_git_configuration(CancellationToken ct)
    {
        using var lab = new CoreLab("next-run");
        var repo = await LabRepo.CreateAsync(lab.Tree, ct);
        File.WriteAllText(Path.Combine(repo.Root, "context.txt"), "first\nbefore\nlast\n");
        await repo.GitAsync(["add", "context.txt"], ct);
        await repo.GitAsync(["commit", "-m", "Context fixture"], ct);
        await repo.GitAsync(["config", "diff.context", "0"], ct);
        var expected = System.Text.Encoding.UTF8.GetBytes("first\nafter\nlast\n");
        await lab.SourceAttemptAsync(ct, edit: path => File.WriteAllBytesAsync(Path.Combine(path, "context.txt"), expected, ct));
        var next = lab.Continuation();
        await Worktree.PrepareAsync(next, lab.Ledger, repo.Root, ct, lab.Git);
        await Assert.That(File.ReadAllBytes(Path.Combine(next.Record.Workspace.Path!, "context.txt")))
            .IsEquivalentTo(expected, CollectionOrdering.Matching);
    }
    [Test]
    [Timeout(60_000)]
    public async Task ContinueOf_replays_all_patch_file_kinds(CancellationToken ct)
    {
        using var lab = new CoreLab("next-run");
        var repo = await LabRepo.CreateAsync(lab.Tree, ct);
        File.WriteAllText(Path.Combine(repo.Root, ".gitattributes"), "crlf.txt text eol=crlf\n*.bin -text\n");
        File.WriteAllText(Path.Combine(repo.Root, "crlf.txt"), "before\r\n");
        File.WriteAllBytes(Path.Combine(repo.Root, "data.bin"), [0, 1, 255]);
        File.WriteAllText(Path.Combine(repo.Root, "delete.txt"), "delete me\n");
        File.WriteAllText(Path.Combine(repo.Root, ".gitignore"), "ignored.out\n");
        await repo.GitAsync(["add", "-A"], ct);
        await repo.GitAsync(["commit", "-m", "Patch kinds"], ct);
        var source = await lab.SourceAttemptAsync(ct, edit: path =>
        {
            File.WriteAllText(Path.Combine(path, "crlf.txt"), "after\r\nsecond\r\n");
            File.WriteAllBytes(Path.Combine(path, "data.bin"), [0, 255, 0, 128, 5]);
            File.WriteAllText(Path.Combine(path, "new.txt"), "untracked content\n");
            File.WriteAllText(Path.Combine(path, "ignored.out"), "not collected");
            File.Delete(Path.Combine(path, "delete.txt"));
            return Task.CompletedTask;
        });
        var next = lab.Continuation();
        await Worktree.PrepareAsync(next, lab.Ledger, repo.Root, ct, lab.Git);
        var path = next.Record.Workspace.Path!;
        await Assert.That(File.ReadAllBytes(Path.Combine(path, "crlf.txt"))).IsEquivalentTo(System.Text.Encoding.UTF8.GetBytes("after\r\nsecond\r\n"), CollectionOrdering.Matching);
        await Assert.That(File.ReadAllBytes(Path.Combine(path, "data.bin"))).IsEquivalentTo(new byte[] { 0, 255, 0, 128, 5 }, CollectionOrdering.Matching);
        await Assert.That(File.ReadAllText(Path.Combine(path, "new.txt"))).IsEqualTo("untracked content\n");
        await Assert.That(File.Exists(Path.Combine(path, "delete.txt"))).IsFalse();
        await Assert.That(File.Exists(Path.Combine(path, "ignored.out"))).IsFalse();
        await Assert.That(await repo.GitAsync(["-C", path, "diff", "--cached", "--name-only"], ct)).IsEqualTo("crlf.txt\ndata.bin\ndelete.txt\nnew.txt");
        await Assert.That(await repo.GitAsync(["-C", path, "rev-parse", "HEAD"], ct)).IsEqualTo(source.Run.Record.Workspace.BaseSha);
        await Assert.That(next.Record.Workspace.StartSha).IsEqualTo(source.Run.Record.Workspace.BaseSha);
        await Assert.That(next.Record.Workspace.BaseRef).IsEqualTo("main");
        await Assert.That(next.Record.Workspace.Branch).IsEqualTo("dispatch/next-run/next");
        await Assert.That(lab.Ledger.Read().Single(x => x.Type == "worktree").Data!["continueOf"]!.GetValue<string>()).IsEqualTo("2026-10-31-source/source");

        var sourcePath = source.Run.Record.Workspace.Path!;
        File.Move(Path.Combine(sourcePath, "README.md"), Path.Combine(sourcePath, "renamed.md"));
        File.WriteAllText(Path.Combine(sourcePath, "spaced café.txt"), "Unicode path content\n");
        await repo.GitAsync(["-C", sourcePath, "add", "-A"], ct);
        // The stop's snapshot re-adds from disk: where the file system has modes, the mode must be there.
        if (OperatingSystem.IsWindows()) await repo.GitAsync(["-C", sourcePath, "update-index", "--chmod=+x", "crlf.txt"], ct);
        else File.SetUnixFileMode(Path.Combine(sourcePath, "crlf.txt"), UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        await Commit.CollectAsync(source.Ledger, source.Run, source.Result, ct, source.Git);
        source.Ledger.SaveResult(source.Result);
        source.Ledger.Write("source", "final", new() { ["state"] = "failed" });
        var allKinds = lab.Continuation("all-kinds");
        await Worktree.PrepareAsync(allKinds, lab.Ledger, repo.Root, ct, lab.Git);
        var replay = allKinds.Record.Workspace.Path!;
        await Assert.That(File.Exists(Path.Combine(replay, "README.md"))).IsFalse();
        await Assert.That(File.ReadAllText(Path.Combine(replay, "renamed.md"))).IsEqualTo("# Lab repository\n");
        await Assert.That(File.ReadAllText(Path.Combine(replay, "spaced café.txt"))).IsEqualTo("Unicode path content\n");
        await Assert.That(await repo.GitAsync(["-C", replay, "ls-files", "--stage", "crlf.txt"], ct)).StartsWith("100755 ");
        await Assert.That(await repo.GitAsync(["-C", replay, "diff", "--cached", "--name-status", "--find-renames", "--", "README.md", "renamed.md"], ct))
            .IsEqualTo("R100\tREADME.md\trenamed.md");
    }

    [Test]
    [Timeout(60_000)]
    public async Task ReviewOf_a_continuation_starts_from_its_branch(CancellationToken ct)
    {
        using var lab = new CoreLab("next-run");
        var source = await lab.SourceAttemptAsync(ct);
        var next = lab.Continuation();
        await Worktree.PrepareAsync(next, lab.Ledger, lab.Tree.Repository, ct, lab.Git);
        var result = new TaskResult { WorkerStatus = "done" };
        await Commit.CollectAsync(lab.Ledger, next, result, ct, lab.Git);
        var review = new TaskRun { Record = new() { Spec = new() { Id = "review", Kind = "review", ReviewOf = next.Id } } };
        await Worktree.PrepareAsync(review, lab.Ledger, lab.Tree.Repository, ct, lab.Git);
        await Assert.That(review.Record.Workspace.BaseRef).IsEqualTo("dispatch/next-run/next");
        await Assert.That(review.Record.Workspace.StartSha).IsEqualTo(result.Commit);
        await Assert.That(review.Record.Workspace.ReviewedBaseSha).IsEqualTo(source.Run.Record.Workspace.BaseSha);
        await Assert.That(File.ReadAllText(Path.Combine(review.Record.Workspace.Path!, "source.txt"))).IsEqualTo("source work\n");
    }

    [Test]
    [Timeout(60_000)]
    public async Task Stopped_target_with_own_commits_replays_from_base(CancellationToken ct)
    {
        using var lab = new CoreLab("next-run");
        var source = await lab.SourceAttemptAsync(ct, ownCommit: true);
        var next = lab.Continuation();
        await Worktree.PrepareAsync(next, lab.Ledger, lab.Tree.Repository, ct, lab.Git);
        var path = next.Record.Workspace.Path!;
        await Assert.That(source.Result.Commit).IsNotEqualTo(source.Run.Record.Workspace.BaseSha);
        await Assert.That(next.Record.Workspace.StartSha).IsEqualTo(source.Run.Record.Workspace.BaseSha);
        await Assert.That(next.Record.Workspace.BaseSha).IsEqualTo(source.Run.Record.Workspace.BaseSha);
        await Assert.That(File.ReadAllText(Path.Combine(path, "source.txt"))).IsEqualTo("source work\n");
        await Assert.That(File.ReadAllText(Path.Combine(path, "uncommitted.txt"))).IsEqualTo("uncommitted work\n");
        await Assert.That(await new LabRepo(lab.Tree).GitAsync(["-C", path, "diff", "--cached", "--name-only"], ct)).IsEqualTo("source.txt\nuncommitted.txt");
    }

    [Test]
    [Arguments("verify-failed")]
    [Arguments("outcome")]
    [Timeout(60_000)]
    public async Task Committed_failed_target_starts_at_commit_without_replay(string failure, CancellationToken ct)
    {
        using var lab = new CoreLab("next-run");
        var source = await lab.SourceAttemptAsync(ct, "done", failure: failure);
        await Assert.That(source.Result.Failures).IsEquivalentTo(new[] { failure == "outcome" ? "outcome:failed" : "verify-failed" }, CollectionOrdering.Matching);
        var next = lab.Continuation();
        await Worktree.PrepareAsync(next, lab.Ledger, lab.Tree.Repository, ct, lab.Git);
        await Assert.That(next.Record.Workspace.StartSha).IsEqualTo(source.Result.Commit);
        await Assert.That(next.Record.Workspace.BaseSha).IsEqualTo(source.Run.Record.Workspace.BaseSha);
        await Assert.That(File.ReadAllText(Path.Combine(next.Record.Workspace.Path!, "source.txt"))).IsEqualTo("source work\n");
        await Assert.That(await new LabRepo(lab.Tree).GitAsync(["-C", next.Record.Workspace.Path!, "status", "--porcelain=v1"], ct)).IsEqualTo("");
        await Assert.That(await new LabRepo(lab.Tree).GitAsync(["-C", next.Record.Workspace.Path!, "rev-parse", "HEAD"], ct)).IsEqualTo(source.Result.Commit);
    }

    [Test]
    [Arguments("stopped")]
    [Arguments("verify-failed")]
    [Timeout(60_000)]
    public async Task Two_continuations_keep_the_original_diff_base(string initial, CancellationToken ct)
    {
        using var lab = new CoreLab("middle-run");
        var source = await lab.SourceAttemptAsync(ct, initial == "stopped" ? "stopped" : "done", failure: initial == "stopped" ? null : initial);
        var middle = lab.Continuation("middle");
        await Worktree.PrepareAsync(middle, lab.Ledger, lab.Tree.Repository, ct, lab.Git);
        File.WriteAllText(Path.Combine(middle.Record.Workspace.Path!, "middle.txt"), "middle work\n");
        var middleResult = new TaskResult { Run = lab.Ledger.RunId, Task = middle.Id, State = "failed", WorkerStatus = "blocked", BaseSha = middle.Record.Workspace.BaseSha };
        await Commit.CollectAsync(lab.Ledger, middle, middleResult, ct, lab.Git);
        lab.Ledger.SaveResult(middleResult);
        await lab.Ledger.WriteArtifactAsync(middle.Id, "final.md", "Middle report.\nStatus: blocked", ct);
        lab.Ledger.Write(middle.Id, "final", new() { ["state"] = "failed" });
        await Assert.That(middleResult.FilesChanged).IsEquivalentTo(new[] { "middle.txt", "source.txt" }, CollectionOrdering.Matching);
        await Assert.That(middleResult.WipPatch).IsEqualTo("wip.patch");
        await Assert.That(File.ReadAllText(Path.Combine(lab.Ledger.Dir, middle.Id, "wip.patch"))).Contains("+source work");
        var lastLedger = new Ledger("last-run");
        var last = lab.Continuation("last", "middle-run/middle");
        await Worktree.PrepareAsync(last, lastLedger, lab.Tree.Repository, ct, lab.Git);
        await Assert.That(last.Record.Workspace.BaseSha).IsEqualTo(source.Run.Record.Workspace.BaseSha);
        await Assert.That(last.Record.Workspace.StartSha).IsEqualTo(source.Run.Record.Workspace.BaseSha);
        await Assert.That(File.ReadAllText(Path.Combine(last.Record.Workspace.Path!, "source.txt"))).IsEqualTo("source work\n");
        await Assert.That(File.ReadAllText(Path.Combine(last.Record.Workspace.Path!, "middle.txt"))).IsEqualTo("middle work\n");
        File.WriteAllText(Path.Combine(last.Record.Workspace.Path!, "last.txt"), "last work\n");
        var final = new TaskResult { WorkerStatus = "done" };
        await Commit.CollectAsync(lastLedger, last, final, ct, lab.Git);
        await Assert.That(final.FilesChanged).IsEquivalentTo(new[] { "last.txt", "middle.txt", "source.txt" }, CollectionOrdering.Matching);
        await Assert.That(await new LabRepo(lab.Tree).GitAsync(["-C", last.Record.Workspace.Path!, "show", "HEAD:source.txt"], ct)).IsEqualTo("source work");
    }

    [Test]
    [Arguments("state")]
    [Arguments("scope")]
    [Arguments("report")]
    [Arguments("patch")]
    [Timeout(60_000)]
    public async Task ContinueOf_rechecks_the_source_before_start(string change, CancellationToken ct)
    {
        using var lab = new CoreLab("next-run");
        var source = await lab.SourceAttemptAsync(ct);
        var next = lab.Continuation();
        next.Record.Spec.Scope = ["source.txt"];
        var plan = await lab.PlanAsync(ct, next.Record.Spec);
        await Assert.That(plan.Errors).IsEmpty();
        await Assert.That(plan.Tasks.Select(x => x.Spec.Id!)).IsEquivalentTo(new[] { "next" }, CollectionOrdering.Matching);
        if (change == "state") source.Ledger.Write("source", "final", new() { ["state"] = "done" });
        else if (change == "report") File.WriteAllText(Path.Combine(source.Ledger.Dir, "source", "final.md"), "Fresh report 世界");
        else
        {
            File.WriteAllText(Path.Combine(source.Run.Record.Workspace.Path!, change == "scope" ? "outside.txt" : "source.txt"), "fresh collected content\n");
            await Commit.CollectAsync(source.Ledger, source.Run, source.Result, ct, source.Git);
            source.Ledger.SaveResult(source.Result);
            source.Ledger.Write("source", "final", new() { ["state"] = "failed" });
        }
        if (change is "state" or "scope")
        {
            await Assert.That(async () => await Worktree.PrepareAsync(next, lab.Ledger, lab.Tree.Repository, ct, lab.Git))
                .Throws<HarnessException>().WithMessage(change == "state" ? "continueOf source must have a consistent final failed result" : "continueOf inherited paths outside scope: outside.txt");
            await Assert.That(next.Record.Workspace.Path).IsNull();
            await Assert.That(lab.Backend.Calls).IsEmpty();
        }
        else
        {
            await Worktree.PrepareAsync(next, lab.Ledger, lab.Tree.Repository, ct, lab.Git);
            await Assert.That(File.ReadAllText(Path.Combine(next.Record.Workspace.Path!, "source.txt"))).IsEqualTo(change == "patch" ? "fresh collected content\n" : "source work\n");
            var final = File.ReadAllText(Path.Combine(source.Ledger.Dir, "source", "final.md"));
            await Assert.That(next.Record.PromptText).IsEqualTo("Continues 2026-10-31-source/source; its collected changes are already in this folder.\n\n```\n"
                + final + (final.EndsWith('\n') ? "" : "\n") + "```\n\nAmend the earlier work.");
        }
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(true, false)]
    [Arguments(false, true)]
    [Timeout(60_000)]
    public async Task ContinueOf_can_start_after_source_clean(bool committed, bool empty, CancellationToken ct)
    {
        using var lab = new CoreLab("next-run");
        var source = await lab.SourceAttemptAsync(ct, committed ? "done" : "blocked", edit: empty ? _ => Task.CompletedTask : null, failure: committed ? "verify-failed" : null);
        await Assert.That(await Worktree.CleanAsync(source.Ledger, source.Run.Record, lab.Tree.Repository, true, ct, source.Git)).IsTrue();
        var next = lab.Continuation();
        await Worktree.PrepareAsync(next, lab.Ledger, lab.Tree.Repository, ct, lab.Git);
        await Assert.That(File.ReadAllText(Path.Combine(next.Record.Workspace.Path!, "README.md"))).IsEqualTo("# Lab repository\n");
        if (empty)
        {
            await Assert.That(File.Exists(Path.Combine(next.Record.Workspace.Path!, "source.txt"))).IsFalse();
            await Assert.That(source.Result.WipPatch).IsNull();
        }
        else await Assert.That(File.ReadAllText(Path.Combine(next.Record.Workspace.Path!, "source.txt"))).IsEqualTo("source work\n");
        await Assert.That(Directory.Exists(source.Run.Record.Workspace.Path)).IsFalse();
        await Assert.That(next.Record.Workspace.StartSha).IsEqualTo(committed ? source.Result.Commit : source.Run.Record.Workspace.BaseSha);
    }

    [Test]
    [Timeout(60_000)]
    public async Task ContinueOf_apply_failure_uses_workspace_start_failure(CancellationToken ct)
    {
        using var lab = new CoreLab("next-run");
        var source = await lab.SourceAttemptAsync(ct);
        var patch = Path.Combine(source.Ledger.Dir, "source", "wip.patch");
        File.WriteAllText(patch, "not a patch\n");
        var next = lab.Continuation();
        next.Record.Spec.Prepare = ["exit 0"];
        next.Record.Spec.Verify = ["exit 0"];
        await Assert.That(await lab.Scheduler(ct).RunAsync([next])).IsEqualTo(1);
        var path = next.Record.Workspace.Path!;
        // Git words this error differently across versions.
        var message = $"dispatch failed: HarnessException: git apply --index --whitespace=nowarn -- {patch} failed in {path}: error: ";
        var events = lab.Ledger.Read().Where(x => x.Task == next.Id).ToList();
        await Assert.That(events.Select(x => x.Type)).IsEquivalentTo(new[] { "error", "final" }, CollectionOrdering.Matching);
        await Assert.That(events[0].Data!["message"]!.GetValue<string>()).StartsWith(message);
        await Assert.That(events[1].Data!["state"]!.GetValue<string>()).IsEqualTo("failed");
        await Assert.That(events[1].Data!["reason"]!.GetValue<string>()).IsEqualTo(events[0].Data!["message"]!.GetValue<string>());
        await Assert.That(lab.Backend.Calls).IsEmpty();
        await Assert.That(lab.Ledger.LoadResult(next.Id)).IsNull();
        await Assert.That(lab.Ledger.LoadRecord(next.Id).Workspace.Path).IsEqualTo(path);
        await Assert.That(next.Record.Prepare).IsNull();
        await Assert.That(await Worktree.CleanAsync(lab.Ledger, next.Record, lab.Tree.Repository, true, ct, lab.Git)).IsTrue();
        await Assert.That(Directory.Exists(path)).IsFalse();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    [Timeout(60_000)]
    public async Task Continuation_cleanup_and_status_stay_run_local(bool sourceFirst, CancellationToken ct)
    {
        using var lab = new CoreLab("next-run");
        var source = await lab.SourceAttemptAsync(ct);
        var next = lab.Continuation();
        await Worktree.PrepareAsync(next, lab.Ledger, lab.Tree.Repository, ct, lab.Git);
        lab.Ledger.Write(next.Id, "final", new() { ["state"] = "failed" });
        next.Record.Session.Id = "new-worker";
        lab.Ledger.SaveRecord(next.Record);
        var steps = File.ReadLines(FakeOpenCode.Fixture("one-task.jsonl")).Take(4)
            .Select(line => System.Text.Json.Nodes.JsonNode.Parse(line)!.AsObject()).ToList();
        foreach (var (ledger, run) in new[] { (source.Ledger, source.Run), (lab.Ledger, next) })
        {
            Json.WriteFile(Path.Combine(ledger.Dir, "config.json"), new EffectiveConfig
            { Tasks = new() { [run.Id] = new() { Settings = run.Record.Settings } } }, LedgerJson.Files.EffectiveConfig);
            var info = ledger.LoadRunInfo();
            info.Repo = lab.Tree.Repository;
            Json.WriteFile(Path.Combine(ledger.Dir, "run.json"), info, LedgerJson.Files.RunInfo);
            File.WriteAllText(Path.Combine(Locations.ControlDir(ledger.RunId), "setup.done"), "");
            var id = run.Record.Session.Id!;
            steps.Add(new() { ["expect"] = $"GET /api/session/{id}", ["repeat"] = true, ["reply"] = OpenCodeLab.Session(id, "succeeded") });
            var messages = System.Text.Json.Nodes.JsonNode.Parse(File.ReadLines(FakeOpenCode.Fixture("one-task.jsonl")).ElementAt(12))!.AsObject();
            messages["expect"] = $"GET /api/session/{id}/message";
            messages["reply"]!["data"]![1]!["content"]![0]!["text"] = $"Recollected {id}.\nStatus: stopped";
            steps.Add(messages);
            steps.Add(new() { ["expect"] = $"DELETE /api/session/{id}", ["status"] = 204 });
        }
        var scenario = Path.Combine(lab.Tree.Root, "collect.jsonl");
        File.WriteAllLines(scenario, steps.Select(step => step.ToJsonString()));
        var environment = FakeOpenCode.Environment(lab.Tree, "one-task.jsonl").ToDictionary(x => x.Key, x => (string?)x.Value);
        environment["FAKE_OPENCODE_SCENARIO"] = scenario;
        foreach (var (ledger, run, other) in new[] { (source.Ledger, source.Run, lab.Ledger), (lab.Ledger, next, source.Ledger) })
        {
            var before = Directory.GetFiles(other.Dir, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal)
                .Select(file => file + ":" + Convert.ToBase64String(File.ReadAllBytes(file))).ToArray();
            using var status = new HarnessProcess(lab.Tree);
            var shown = await status.RunAsync(["status", ledger.RunId, "--ledger", Locations.LedgerRoot], ct, environment);
            shown.RequireSuccess("status continuation run");
            await Assert.That(shown.Stdout).Contains($"  {run.Id,-24} failed");
            await Assert.That(shown.Stdout).DoesNotContain($"  {(run.Id == "source" ? "next" : "source"),-24} ");
            using var collect = new HarnessProcess(lab.Tree);
            var collected = await collect.RunAsync(["collect", ledger.RunId, run.Id, "--ledger", Locations.LedgerRoot], ct, environment);
            await Assert.That(collected.ExitCode).IsEqualTo(1);
            await Assert.That(ledger.LoadResult(run.Id)!.FilesChanged).IsEquivalentTo(new[] { "source.txt" }, CollectionOrdering.Matching);
            await Assert.That(File.ReadAllText(Path.Combine(ledger.Dir, run.Id, "final.md"))).Contains($"Recollected {run.Record.Session.Id}.\nStatus: stopped");
            await Assert.That(Directory.GetFiles(other.Dir, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal)
                .Select(file => file + ":" + Convert.ToBase64String(File.ReadAllBytes(file))))
                .IsEquivalentTo(before, CollectionOrdering.Matching);
        }
        var sourceBytes = File.ReadAllBytes(Path.Combine(source.Ledger.Dir, "source", "result.json"));
        var firstLedger = sourceFirst ? source.Ledger : lab.Ledger;
        var first = sourceFirst ? source.Run : next;
        var secondLedger = sourceFirst ? lab.Ledger : source.Ledger;
        var second = sourceFirst ? next : source.Run;
        await Assert.That(await Worktree.CleanAsync(firstLedger, first.Record, lab.Tree.Repository, false, ct, sourceFirst ? source.Git : lab.Git)).IsFalse();
        await Assert.That(await Worktree.CleanAsync(firstLedger, first.Record, lab.Tree.Repository, true, ct, sourceFirst ? source.Git : lab.Git)).IsTrue();
        await Assert.That(File.ReadAllText(Path.Combine(second.Record.Workspace.Path!, "source.txt"))).IsEqualTo("source work\n");
        await Assert.That(await new LabRepo(lab.Tree).GitAsync(["rev-parse", second.Record.Workspace.Branch!], ct)).IsEqualTo(second.Record.Workspace.StartSha);
        await Assert.That(source.Ledger.TaskStates().Select(x => $"{x.Key}:{x.Value}")).IsEquivalentTo(new[] { "source:failed" }, CollectionOrdering.Matching);
        await Assert.That(lab.Ledger.TaskStates().Select(x => $"{x.Key}:{x.Value}")).IsEquivalentTo(new[] { "next:failed" }, CollectionOrdering.Matching);
        await Assert.That(File.ReadAllBytes(Path.Combine(source.Ledger.Dir, "source", "result.json"))).IsEquivalentTo(sourceBytes, CollectionOrdering.Matching);
        await Assert.That(await Worktree.CleanAsync(secondLedger, second.Record, lab.Tree.Repository, true, ct, sourceFirst ? lab.Git : source.Git)).IsTrue();
        await Assert.That(await new LabRepo(lab.Tree).GitAsync(["branch", "--format=%(refname:short)"], ct)).IsEqualTo("main");
    }

    [Test]
    [Arguments("codex")]
    [Arguments("opencode")]
    [Timeout(60_000)]
    public async Task Clean_keeps_a_clean_merged_zombies_worktree_and_scratch_until_forced(string backend, CancellationToken ct)
    {
        using var lab = new CoreLab("zombie-clean");
        var run = await lab.TaskAsync("inspect", ct);
        run.Record.Settings.Backend = new() { Value = backend };
        run.Record.Workspace = new();
        run.Record.Spec.Base = "main";
        await Worktree.PrepareAsync(run, lab.Ledger, lab.Tree.Repository, ct, lab.Git);
        var path = run.Record.Workspace.Path!;
        var temp = Locations.TaskTemp(run.Id, lab.Ledger.RunId);
        File.WriteAllText(Path.Combine(temp, "keep"), "scratch bytes");
        var other = Path.Combine(Directory.CreateDirectory(Path.Combine(lab.Tree.Root, "unrelated")).FullName, "keep");
        File.WriteAllText(other, "unrelated bytes");
        lab.Ledger.SaveResult(new() { Run = lab.Ledger.RunId, Task = run.Id, State = "failed", WorkerStopped = false, Failures = ["zombie"] });
        lab.Ledger.Write(run.Id, "stop-unconfirmed", new() { ["outcome"] = "succeeded", ["detail"] = "pid=1972 survives" });
        if (backend == "opencode")
        {
            // OpenCode's historical zombie result still retains work; Codex cleanup evidence is backend-specific.
            lab.Ledger.Write(run.Id, "cancel", new());
            lab.Ledger.Write(run.Id, "hard-stop", new() { ["stopped"] = true, ["detail"] = "old hard-stop reply" });
        }
        var (removed, output) = await CaptureCleanAsync(lab, run, false, ct);
        await Assert.That(removed).IsFalse();
        await Assert.That(output).IsEqualTo("inspect: retained zombie worktree and scratch; --force removes them anyway" + Environment.NewLine);
        await Assert.That(File.ReadAllText(Path.Combine(path, "README.md"))).IsEqualTo("# Lab repository\n");
        await Assert.That(File.ReadAllText(Path.Combine(temp, "keep"))).IsEqualTo("scratch bytes");
        await Assert.That(await Worktree.CleanAsync(lab.Ledger, run.Record, lab.Tree.Repository, true, ct, lab.Git)).IsTrue();
        await Assert.That(Directory.Exists(path)).IsFalse();
        await Assert.That(Directory.Exists(temp)).IsFalse();
        await Assert.That(await new LabRepo(lab.Tree).GitAsync(["branch", "--format=%(refname:short)"], ct)).IsEqualTo("main");
        await Assert.That(File.ReadAllText(other)).IsEqualTo("unrelated bytes");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    [Timeout(60_000)]
    public async Task Clean_uses_the_recorded_feature_base_not_main(bool taskCommit, CancellationToken ct)
    {
        using var lab = new CoreLab("feature-base");
        var run = await lab.TaskAsync("inspect", ct);
        var repo = new LabRepo(lab.Tree);
        await repo.GitAsync(["checkout", "-b", "feature"], ct);
        File.WriteAllText(Path.Combine(repo.Root, "feature.txt"), "feature work\n");
        await repo.GitAsync(["add", "feature.txt"], ct);
        await repo.GitAsync(["commit", "-m", "Feature work"], ct);
        var feature = await repo.GitAsync(["rev-parse", "feature"], ct);
        await repo.GitAsync(["checkout", "main"], ct);
        run.Record.Workspace = new();
        run.Record.Spec.Base = "feature";
        await Worktree.PrepareAsync(run, lab.Ledger, repo.Root, ct, lab.Git);
        var path = run.Record.Workspace.Path!;
        if (taskCommit)
        {
            File.WriteAllText(Path.Combine(path, "task.txt"), "task work\n");
            await repo.GitAsync(["-C", path, "add", "task.txt"], ct);
            await repo.GitAsync(["-C", path, "commit", "-m", "Task work"], ct);
        }
        var (removed, output) = await CaptureCleanAsync(lab, run, false, ct);
        await Assert.That(removed).IsEqualTo(!taskCommit);
        await Assert.That(Directory.Exists(path)).IsEqualTo(taskCommit);
        await Assert.That(await repo.GitAsync(["branch", "--format=%(refname:short)"], ct))
            .IsEqualTo(taskCommit ? $"{run.Record.Workspace.Branch}\nfeature\nmain" : "feature\nmain");
        await Assert.That(await repo.GitAsync(["rev-parse", "feature"], ct)).IsEqualTo(feature);
        await Assert.That(await repo.GitAsync(["branch", "--show-current"], ct)).IsEqualTo("main");
        if (taskCommit)
            await Assert.That(output).IsEqualTo("inspect: refused (unmerged: not on feature); --force removes it anyway" + Environment.NewLine);
        else await Assert.That(output).DoesNotContain("refused");
    }

    [Test]
    [Arguments("clean", false)]
    [Arguments("changes", false)]
    [Arguments("local commits", false)]
    [Arguments("local commits", true)]
    [Timeout(60_000)]
    public async Task Clean_checks_initialized_submodules(string state, bool force, CancellationToken ct)
    {
        using var lab = new CoreLab("submodule");
        using var source = new TempTree();
        var upstream = await LabRepo.CreateAsync(source, ct);
        var run = await lab.TaskAsync("inspect", ct);
        var repo = new LabRepo(lab.Tree);
        await repo.GitAsync(["-c", "protocol.file.allow=always", "submodule", "add", upstream.Root, "sub"], ct);
        await repo.GitAsync(["commit", "-am", "Add submodule"], ct);
        run.Record.Workspace = new();
        run.Record.Spec.Base = "main";
        await Worktree.PrepareAsync(run, lab.Ledger, repo.Root, ct, lab.Git);
        var path = run.Record.Workspace.Path!;
        await repo.GitAsync(["-c", "protocol.file.allow=always", "-C", path, "submodule", "update", "--init"], ct);
        var sub = Path.Combine(path, "sub");
        var file = Path.Combine(sub, "local.txt");
        if (state != "clean") File.WriteAllText(file, "local work\n");
        if (state == "local commits")
        {
            var recorded = await repo.GitAsync(["-C", sub, "rev-parse", "HEAD"], ct);
            await repo.GitAsync(["-C", sub, "checkout", "-b", "local-work"], ct);
            await repo.GitAsync(["-C", sub, "add", "local.txt"], ct);
            await repo.GitAsync(["-C", sub, "-c", "user.name=Test", "-c", "user.email=test@example.invalid", "commit", "-m", "Local work"], ct);
            await repo.GitAsync(["-C", sub, "checkout", "--detach", recorded], ct);
            await Assert.That(await repo.GitAsync(["-C", path, "status", "--porcelain=v1", "-z", "--untracked-files=all", "--ignore-submodules=none"], ct)).IsEqualTo("");
        }
        var (removed, output) = await CaptureCleanAsync(lab, run, force, ct);
        var expectedRemoval = force || state == "clean";
        await Assert.That(removed).IsEqualTo(expectedRemoval);
        await Assert.That(Directory.Exists(path)).IsEqualTo(!expectedRemoval);
        await Assert.That(await repo.GitAsync(["branch", "--format=%(refname:short)"], ct))
            .IsEqualTo(expectedRemoval ? "main" : $"{run.Record.Workspace.Branch}\nmain");
        var entries = (await repo.GitAsync(["worktree", "list", "--porcelain"], ct)).ReplaceLineEndings("\n").Split("\n\n");
        await Assert.That(entries.Length).IsEqualTo(expectedRemoval ? 1 : 2);
        for (var i = 0; i < entries.Length; i++)
        {
            var lines = entries[i].Split('\n');
            await Assert.That(lines[0]).StartsWith("worktree ");
            await Assert.That(Locations.Same(lines[0][9..], i == 0 ? repo.Root : path)).IsTrue();
            await Assert.That(string.Join('\n', lines.Skip(1))).IsEqualTo($"HEAD {run.Record.Workspace.BaseSha}\nbranch refs/heads/{(i == 0 ? "main" : run.Record.Workspace.Branch)}");
        }
        if (expectedRemoval) await Assert.That(output).DoesNotContain("refused");
        else
        {
            var reason = state == "changes" ? "has changes" : "holds commits no remote has";
            await Assert.That(output).IsEqualTo($"inspect: refused (submodule {reason}); --force removes it anyway" + Environment.NewLine);
            if (state == "changes") await Assert.That(File.ReadAllText(file)).IsEqualTo("local work\n");
            else await Assert.That(await repo.GitAsync(["-C", sub, "branch", "--list", "local-work", "--format=%(refname:short)"], ct)).IsEqualTo("local-work");
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    [Timeout(60_000)]
    public async Task Clean_keeps_deinitialized_submodule_git_data_with_local_commits(bool nested, CancellationToken ct)
    {
        using var lab = new CoreLab("deinit");
        using var source = new TempTree();
        using var nestedSource = new TempTree();
        var upstream = await LabRepo.CreateAsync(source, ct);
        if (nested)
        {
            var nestedUpstream = await LabRepo.CreateAsync(nestedSource, ct);
            await upstream.GitAsync(["-c", "protocol.file.allow=always", "submodule", "add", nestedUpstream.Root, "subB"], ct);
            await upstream.GitAsync(["commit", "-am", "Add nested submodule"], ct);
        }
        var (repo, run, path) = await SubmoduleWorktreeAsync(lab, upstream, ct);
        var sub = nested ? Path.Combine(path, "subA", "subB") : Path.Combine(path, "subB");
        await repo.GitAsync(["-C", sub, "checkout", "-b", "local-work"], ct);
        File.WriteAllText(Path.Combine(sub, "local.txt"), "local work\n");
        await repo.GitAsync(["-C", sub, "add", "local.txt"], ct);
        await repo.GitAsync(["-C", sub, "-c", "user.name=Test", "-c", "user.email=test@example.invalid", "commit", "-m", "Local work"], ct);
        var gitDir = await repo.GitAsync(["-C", sub, "rev-parse", "--absolute-git-dir"], ct);
        await repo.GitAsync(["-C", nested ? Path.Combine(path, "subA") : path, "submodule", "deinit", "-f", "subB"], ct);
        await Assert.That(await repo.GitAsync(["-C", path, "status", "--porcelain=v1", "--ignore-submodules=none"], ct)).IsEqualTo("");
        var (removed, output) = await CaptureCleanAsync(lab, run, false, ct);
        await Assert.That(removed).IsFalse();
        await Assert.That(output).IsEqualTo("inspect: refused (submodule holds commits no remote has); --force removes it anyway" + Environment.NewLine);
        await Assert.That(Directory.Exists(gitDir)).IsTrue();
        await Assert.That(await repo.GitAsync([$"--git-dir={gitDir}", "branch", "--list", "local-work", "--format=%(refname:short)"], ct)).IsEqualTo("local-work");
    }

    [Test]
    [Timeout(60_000)]
    public async Task Clean_refuses_a_registration_without_a_matching_admin_folder(CancellationToken ct)
    {
        using var lab = new CoreLab("unmatched-admin");
        var run = await lab.TaskAsync("inspect", ct);
        run.Record.Workspace = new();
        run.Record.Spec.Base = "main";
        await Worktree.PrepareAsync(run, lab.Ledger, lab.Tree.Repository, ct, lab.Git);
        var path = run.Record.Workspace.Path!;
        var repo = new LabRepo(lab.Tree);
        var admin = Path.GetFullPath(File.ReadAllText(Path.Combine(path, ".git"))[8..].Trim(), path);
        File.WriteAllText(Path.Combine(admin, "gitdir"), Path.Combine(lab.Tree.Root, "unmatched"));
        var registration = await repo.GitAsync(["worktree", "list", "--porcelain"], ct);
        var result = (Removed: true, Output: "");
        await Assert.That(async () => { result = await CaptureCleanAsync(lab, run, false, ct); }).ThrowsNothing();
        await Assert.That(result.Output).IsEqualTo("inspect: refused (worktree admin folder not found); --force removes it anyway" + Environment.NewLine);
        await Assert.That(result.Removed).IsFalse();
        await Assert.That(File.ReadAllText(Path.Combine(path, "README.md"))).IsEqualTo("# Lab repository\n");
        await Assert.That(Directory.Exists(admin)).IsTrue();
        await Assert.That(await repo.GitAsync(["worktree", "list", "--porcelain"], ct)).IsEqualTo(registration);
        await Assert.That(await repo.GitAsync(["branch", "--format=%(refname:short)"], ct))
            .IsEqualTo($"{run.Record.Workspace.Branch}\nmain");
    }

    [Test]
    [Timeout(60_000)]
    public async Task Clean_reports_superproject_dirt_before_submodule_checks(CancellationToken ct)
    {
        using var lab = new CoreLab("superproject-dirt");
        using var source = new TempTree();
        var upstream = await LabRepo.CreateAsync(source, ct);
        var (_, run, path) = await SubmoduleWorktreeAsync(lab, upstream, ct);
        File.AppendAllText(Path.Combine(path, "README.md"), "keep changes\n");
        var (removed, output) = await CaptureCleanAsync(lab, run, false, ct);
        await Assert.That(removed).IsFalse();
        await Assert.That(output).IsEqualTo("inspect: refused (dirty worktree); --force removes it anyway" + Environment.NewLine);
        await Assert.That(File.ReadAllText(Path.Combine(path, "README.md"))).IsEqualTo("# Lab repository\nkeep changes\n");
    }

    [Test]
    [Timeout(60_000)]
    public async Task Clean_refuses_remote_tag_commits_outside_remote_branches(CancellationToken ct)
    {
        using var lab = new CoreLab("remote-tag");
        using var source = new TempTree();
        var upstream = await LabRepo.CreateAsync(source, ct);
        var (repo, run, path) = await SubmoduleWorktreeAsync(lab, upstream, ct);
        await upstream.GitAsync(["checkout", "--detach"], ct);
        File.WriteAllText(Path.Combine(upstream.Root, "tag-only.txt"), "tagged work\n");
        await upstream.GitAsync(["add", "tag-only.txt"], ct);
        await upstream.GitAsync(["commit", "-m", "Tag-only work"], ct);
        await upstream.GitAsync(["tag", "kept"], ct);
        await upstream.GitAsync(["checkout", "main"], ct);
        var sub = Path.Combine(path, "subA");
        await repo.GitAsync(["-C", sub, "fetch", "--tags"], ct);
        await Assert.That(await repo.GitAsync(["-C", sub, "rev-list", "--count", "--all", "--not", "--remotes"], ct)).IsEqualTo("1");
        var (removed, output) = await CaptureCleanAsync(lab, run, false, ct);
        await Assert.That(removed).IsFalse();
        await Assert.That(Directory.Exists(path)).IsTrue();
        await Assert.That(output).IsEqualTo("inspect: refused (submodule holds commits no remote has); --force removes it anyway" + Environment.NewLine);
    }

    [Test]
    [Timeout(60_000)]
    public async Task Clean_refuses_local_tag_commits(CancellationToken ct)
    {
        using var lab = new CoreLab("local-tag");
        using var source = new TempTree();
        var upstream = await LabRepo.CreateAsync(source, ct);
        var (repo, run, path) = await SubmoduleWorktreeAsync(lab, upstream, ct);
        var sub = Path.Combine(path, "subA");
        var recorded = await repo.GitAsync(["-C", sub, "rev-parse", "HEAD"], ct);
        File.WriteAllText(Path.Combine(sub, "local.txt"), "local work\n");
        await repo.GitAsync(["-C", sub, "add", "local.txt"], ct);
        await repo.GitAsync(["-C", sub, "-c", "user.name=Test", "-c", "user.email=test@example.invalid", "commit", "-m", "Local work"], ct);
        await repo.GitAsync(["-C", sub, "tag", "mywork"], ct);
        await repo.GitAsync(["-C", sub, "checkout", "--detach", recorded], ct);
        var (removed, output) = await CaptureCleanAsync(lab, run, false, ct);
        await Assert.That(removed).IsFalse();
        await Assert.That(Directory.Exists(path)).IsTrue();
        await Assert.That(output).IsEqualTo("inspect: refused (submodule holds commits no remote has); --force removes it anyway" + Environment.NewLine);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    [Timeout(60_000)]
    public async Task Clean_checks_submodule_commits_when_worktree_folder_is_gone(bool localCommits, CancellationToken ct)
    {
        using var lab = new CoreLab("missing-submodule-tree");
        using var source = new TempTree();
        var upstream = await LabRepo.CreateAsync(source, ct);
        var (repo, run, path) = await SubmoduleWorktreeAsync(lab, upstream, ct);
        var sub = Path.Combine(path, "subA");
        var modules = Path.Combine(await repo.GitAsync(["-C", path, "rev-parse", "--absolute-git-dir"], ct), "modules");
        if (localCommits)
        {
            File.WriteAllText(Path.Combine(sub, "local.txt"), "local work\n");
            await repo.GitAsync(["-C", sub, "add", "local.txt"], ct);
            await repo.GitAsync(["-C", sub, "-c", "user.name=Test", "-c", "user.email=test@example.invalid", "commit", "-m", "Local work"], ct);
        }
        Directory.Delete(path, recursive: true);
        var (removed, output) = await CaptureCleanAsync(lab, run, false, ct);
        await Assert.That(removed).IsEqualTo(!localCommits);
        await Assert.That(Directory.Exists(modules)).IsEqualTo(localCommits);
        await Assert.That(output).IsEqualTo(localCommits
            ? "inspect: refused (submodule holds commits no remote has); --force removes it anyway" + Environment.NewLine : "");
    }

    [Test]
    [Timeout(60_000)]
    public async Task Clean_reports_unmerged_work_before_submodule_dirt(CancellationToken ct)
    {
        using var lab = new CoreLab("unmerged-submodule-dirt");
        using var source = new TempTree();
        var upstream = await LabRepo.CreateAsync(source, ct);
        var (repo, run, path) = await SubmoduleWorktreeAsync(lab, upstream, ct);
        File.WriteAllText(Path.Combine(path, "task.txt"), "task work\n");
        await repo.GitAsync(["-C", path, "add", "task.txt"], ct);
        await repo.GitAsync(["-C", path, "commit", "-m", "Task work"], ct);
        File.WriteAllText(Path.Combine(path, "subA", "untracked.txt"), "submodule work\n");
        var (removed, output) = await CaptureCleanAsync(lab, run, false, ct);
        await Assert.That(removed).IsFalse();
        await Assert.That(Directory.Exists(path)).IsTrue();
        await Assert.That(output).IsEqualTo("inspect: refused (submodule has changes); --force removes it anyway" + Environment.NewLine);
    }

    [Test]
    [Arguments("fast-forward")]
    [Arguments("detached landed")]
    [Arguments("rebase")]
    [Arguments("squash")]
    [Arguments("later edit")]
    [Arguments("CRLF and binary")]
    [Timeout(60_000)]
    public async Task Clean_recognizes_landed_work_on_a_pinned_base(string landing, CancellationToken ct)
    {
        using var lab = new CoreLab("landed");
        using var origin = new TempTree();
        var (repo, run) = await LandingWorktreeAsync(lab, origin, ct);
        var path = run.Record.Workspace.Path!;
        File.WriteAllText(Path.Combine(path, "change [1].txt"), "first\n");
        if (landing == "CRLF and binary")
        {
            File.WriteAllText(Path.Combine(path, "crlf.txt"), "first\r\nsecond\r\n");
            File.WriteAllBytes(Path.Combine(path, "data.bin"), [0, 255, 0, 128, 5]);
        }
        var first = await CommitFilesAsync(repo, path, ct);
        File.AppendAllText(Path.Combine(path, "change [1].txt"), "second\n");
        var tip = await CommitFilesAsync(repo, path, ct);
        string proof;
        if (landing is "fast-forward" or "detached landed")
        {
            await repo.GitAsync(["merge", "--ff-only", run.Record.Workspace.Branch!], ct);
            proof = tip;
            if (landing == "detached landed")
            {
                await repo.GitAsync(["-C", path, "checkout", "--detach"], ct);
                File.WriteAllText(Path.Combine(path, "detached.txt"), "detached landed work\n");
                var detached = await CommitFilesAsync(repo, path, ct);
                await repo.GitAsync(["merge", "--ff-only", detached], ct);
            }
        }
        else
        {
            File.WriteAllText(Path.Combine(repo.Root, "unrelated.txt"), "target work\n");
            await CommitFilesAsync(repo, repo.Root, ct);
            if (landing == "rebase")
            {
                await repo.GitAsync(["checkout", "-b", "landing", tip], ct);
                await repo.GitAsync(["rebase", "main"], ct);
                proof = await repo.GitAsync(["rev-parse", "HEAD"], ct);
                await Assert.That(proof).IsNotEqualTo(tip);
                await Assert.That(await repo.GitAsync(["rev-parse", "HEAD~1"], ct)).IsNotEqualTo(first);
                await Assert.That(await repo.GitAsync(["rev-list", "--count", "main..HEAD"], ct)).IsEqualTo("2");
                await repo.GitAsync(["checkout", "main"], ct);
                await repo.GitAsync(["merge", "--ff-only", "landing"], ct);
            }
            else proof = await SquashAsync(repo, run, ct);
        }
        if (landing == "later edit")
        {
            File.WriteAllText(Path.Combine(repo.Root, "change [1].txt"), "later target content\n");
            await CommitFilesAsync(repo, repo.Root, ct);
        }
        await repo.GitAsync(["push", "origin", "main"], ct);
        await AssertLandedAsync(lab, run, landing is "fast-forward" or "detached landed" ? "ancestor of refs/remotes/origin/main"
            : $"content matches {proof[..12]} on refs/remotes/origin/main", ct);
    }

    [Test]
    [Timeout(60_000)]
    public async Task Clean_recognizes_a_continuations_inherited_base(CancellationToken ct)
    {
        using var lab = new CoreLab("2026-11-01-source");
        using var origin = new TempTree();
        var (repo, source) = await LandingWorktreeAsync(lab, origin, ct);
        File.WriteAllText(Path.Combine(source.Record.Workspace.Path!, "source.txt"), "source work\n");
        var sourceResult = new TaskResult { Run = lab.Ledger.RunId, Task = source.Id, State = "failed", WorkerStatus = "done", Failures = ["verify-failed"] };
        await Commit.CollectAsync(lab.Ledger, source, sourceResult, ct);
        lab.Ledger.SaveResult(sourceResult);
        await lab.Ledger.WriteArtifactAsync(source.Id, "final.md", "Source report.\nStatus: done\n", ct);
        lab.Ledger.Write(source.Id, "final", new() { ["state"] = "failed" });
        var nextLedger = new Ledger("2026-11-02-next");
        Json.WriteFile(Path.Combine(nextLedger.Dir, "run.json"), new RunInfo { Run = nextLedger.RunId, Repo = repo.Root, ControlId = Locations.NewControlId() }, LedgerJson.Files.RunInfo);
        var next = lab.Continuation("next", $"{lab.Ledger.RunId}/{source.Id}");
        next.Record.Settings.Backend = new() { Value = "codex" };
        await Worktree.PrepareAsync(next, nextLedger, repo.Root, ct);
        await Assert.That(next.Record.Workspace.BaseSha).IsEqualTo(source.Record.Workspace.BaseSha);
        await Assert.That(next.Record.Workspace.StartSha).IsEqualTo(sourceResult.Commit);
        File.WriteAllText(Path.Combine(next.Record.Workspace.Path!, "next.txt"), "next work\n");
        await CommitFilesAsync(repo, next.Record.Workspace.Path!, ct);
        var proof = await SquashAsync(repo, next, ct);
        await repo.GitAsync(["push", "origin", "main"], ct);
        await AssertLandedAsync(lab, next, $"content matches {proof[..12]} on refs/remotes/origin/main", ct, nextLedger);
        await Assert.That(File.ReadAllText(Path.Combine(source.Record.Workspace.Path!, "source.txt"))).IsEqualTo("source work\n");
    }

    [Test]
    [Arguments("nowhere")]
    [Arguments("one line")]
    [Arguments("indentation")]
    [Arguments("no origin HEAD")]
    [Arguments("needs fetch")]
    [Arguments("dirty")]
    [Arguments("untracked")]
    [Arguments("detached")]
    [Arguments("missing base")]
    [Arguments("empty commit")]
    [Timeout(60_000)]
    public async Task Clean_refuses_without_landing_proof(string state, CancellationToken ct)
    {
        using var lab = new CoreLab("not-landed");
        using var origin = new TempTree();
        var (repo, run) = await LandingWorktreeAsync(lab, origin, ct);
        var path = run.Record.Workspace.Path!;
        string tip;
        if (state == "empty commit")
        {
            await repo.GitAsync(["-C", path, "commit", "--allow-empty", "-m", "Empty task commit"], ct);
            tip = await repo.GitAsync(["-C", path, "rev-parse", "HEAD"], ct);
        }
        else
        {
            File.WriteAllText(Path.Combine(path, "change.txt"), "first\n    second\nlast\n");
            tip = await CommitFilesAsync(repo, path, ct);
        }
        if (state is "one line" or "indentation")
        {
            await repo.GitAsync(["merge", "--squash", run.Record.Workspace.Branch!], ct);
            File.WriteAllText(Path.Combine(repo.Root, "change.txt"), state == "one line" ? "first\n    different\nlast\n" : "first\n  second\nlast\n");
            await CommitFilesAsync(repo, repo.Root, ct);
            await repo.GitAsync(["push", "origin", "main"], ct);
        }
        if (state is "dirty" or "untracked" or "detached")
        {
            await repo.GitAsync(["merge", "--ff-only", run.Record.Workspace.Branch!], ct);
            await repo.GitAsync(["push", "origin", "main"], ct);
            if (state == "dirty") File.AppendAllText(Path.Combine(path, "change.txt"), "keep dirty work\n");
            if (state == "untracked") File.WriteAllText(Path.Combine(path, "untracked.txt"), "keep untracked work\n");
            if (state == "detached")
            {
                await repo.GitAsync(["-C", path, "checkout", "--detach"], ct);
                File.WriteAllText(Path.Combine(path, "detached.txt"), "only copy\n");
                await CommitFilesAsync(repo, path, ct);
            }
        }
        if (state == "no origin HEAD") await repo.GitAsync(["symbolic-ref", "--delete", "refs/remotes/origin/HEAD"], ct);
        if (state == "needs fetch")
            await repo.GitAsync(["-C", origin.Repository, "fetch", repo.Root, $"{run.Record.Workspace.Branch}:refs/heads/main"], ct);
        var reason = $"unmerged: not on {run.Record.Workspace.BaseRef}" + (state == "no origin HEAD" ? "" : " or refs/remotes/origin/main; fetch if it landed recently");
        if (state is "dirty" or "untracked")
        {
            // A broken baseline would fail a landing probe: dirt must refuse before reaching it.
            run.Record.Workspace.BaseSha = new string('f', 40);
            reason = "dirty worktree";
        }
        if (state == "missing base")
        {
            run.Record.Workspace.BaseSha = new string('f', 40);
            var failure = await lab.Git.RunAsync(repo.Root, ct, "diff", "--name-only", "-z", "--no-renames", "--ignore-submodules=none", run.Record.Workspace.BaseSha, tip);
            await Assert.That(failure.ExitCode).IsNotEqualTo(0);
            reason = "landing check failed: " + failure.Stderr.ReplaceLineEndings("\n").Split('\n')[0];
        }
        await AssertRefusedAsync(lab, run, reason, ct);
        if (state == "needs fetch")
        {
            await Assert.That(await repo.GitAsync(["rev-parse", "origin/main"], ct)).IsEqualTo(run.Record.Workspace.BaseSha);
            await repo.GitAsync(["fetch", "origin"], ct);
            await Assert.That(await repo.GitAsync(["rev-parse", "origin/main"], ct)).IsEqualTo(tip);
        }
    }

    [Test]
    [Timeout(60_000)]
    public async Task Clean_keeps_a_merge_resolutions_content_that_never_landed(CancellationToken ct)
    {
        using var lab = new CoreLab("merge-resolution");
        using var origin = new TempTree();
        var (repo, run) = await LandingWorktreeAsync(lab, origin, ct);
        var path = run.Record.Workspace.Path!;
        File.WriteAllText(Path.Combine(path, "task.txt"), "task work\n");
        var taskCommit = await CommitFilesAsync(repo, path, ct);
        await repo.GitAsync(["checkout", "-b", "side", run.Record.Workspace.BaseSha!], ct);
        File.WriteAllText(Path.Combine(repo.Root, "side.txt"), "side work\n");
        var sideCommit = await CommitFilesAsync(repo, repo.Root, ct);
        await repo.GitAsync(["checkout", "main"], ct);
        await repo.GitAsync(["-C", path, "merge", "--no-ff", "--no-commit", "side"], ct);
        File.WriteAllText(Path.Combine(path, "resolution.txt"), "only in merge\n");
        await CommitFilesAsync(repo, path, ct);
        await repo.GitAsync(["cherry-pick", taskCommit, sideCommit], ct);
        await repo.GitAsync(["push", "origin", "main"], ct);
        await AssertRefusedAsync(lab, run, $"unmerged: not on {run.Record.Workspace.BaseRef} or refs/remotes/origin/main; fetch if it landed recently", ct);
    }

    [Test]
    [Timeout(60_000)]
    public async Task Clean_keeps_a_mode_change_the_target_lacks(CancellationToken ct)
    {
        using var lab = new CoreLab("mode-change");
        using var origin = new TempTree();
        var (repo, run) = await LandingWorktreeAsync(lab, origin, ct);
        var path = run.Record.Workspace.Path!;
        // core.filemode=false makes the index-only fixture clean on every host; the tree still carries 100755.
        await repo.GitAsync(["-C", path, "-c", "core.filemode=false", "update-index", "--chmod=+x", "README.md"], ct);
        await repo.GitAsync(["-C", path, "-c", "core.filemode=false", "commit", "-m", "Executable readme"], ct);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(Path.Combine(path, "README.md"), UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
        await Assert.That(await repo.GitAsync(["-C", path, "status", "--porcelain=v1"], ct)).IsEqualTo("");
        await Assert.That(await repo.GitAsync(["-C", path, "ls-files", "--stage", "README.md"], ct)).StartsWith("100755 ");
        await Assert.That(await repo.GitAsync(["ls-files", "--stage", "README.md"], ct)).StartsWith("100644 ");
        await AssertRefusedAsync(lab, run, $"unmerged: not on {run.Record.Workspace.BaseRef} or refs/remotes/origin/main; fetch if it landed recently", ct);
    }

    [Test]
    [Timeout(60_000)]
    public async Task Clean_keeps_todays_empty_pinned_base_result(CancellationToken ct)
    {
        using var lab = new CoreLab("empty-pinned");
        using var origin = new TempTree();
        var (repo, run) = await LandingWorktreeAsync(lab, origin, ct);
        var (exit, output) = await CleanCommandAsync(lab.Ledger, run, ct);
        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(output).DoesNotContain("landed (");
        await Assert.That(Directory.Exists(run.Record.Workspace.Path)).IsFalse();
        await Assert.That(await repo.GitAsync(["branch", "--format=%(refname:short)"], ct)).IsEqualTo("main");
        await Assert.That(lab.Ledger.Read().Single(x => x.Type == "cleaned").Data!.ContainsKey("landed")).IsFalse();
    }

    [Test]
    [Arguments(500)]
    [Arguments(501)]
    [Timeout(60_000)]
    public async Task Clean_bounds_same_content_paths(int count, CancellationToken ct)
    {
        using var lab = new CoreLab("path-bound");
        using var origin = new TempTree();
        var (repo, run) = await LandingWorktreeAsync(lab, origin, ct);
        for (var i = 0; i < count; i++) File.WriteAllText(Path.Combine(run.Record.Workspace.Path!, $"file-{i}.txt"), "landed work\n");
        await CommitFilesAsync(repo, run.Record.Workspace.Path!, ct);
        var proof = await SquashAsync(repo, run, ct);
        await repo.GitAsync(["push", "origin", "main"], ct);
        if (count == 500) await AssertLandedAsync(lab, run, $"content matches {proof[..12]} on refs/remotes/origin/main", ct);
        else await AssertRefusedAsync(lab, run, $"unmerged: not on {run.Record.Workspace.BaseRef} or refs/remotes/origin/main; fetch if it landed recently", ct);
    }

    [Test]
    [Timeout(60_000)]
    public async Task Clean_needs_one_candidate_holding_all_paths_together(CancellationToken ct)
    {
        using var lab = new CoreLab("paths-together");
        using var origin = new TempTree();
        var (repo, run) = await LandingWorktreeAsync(lab, origin, ct);
        var path = run.Record.Workspace.Path!;
        File.WriteAllText(Path.Combine(path, "first.txt"), "first work\n");
        File.WriteAllText(Path.Combine(path, "second.txt"), "second work\n");
        await CommitFilesAsync(repo, path, ct);
        File.WriteAllText(Path.Combine(repo.Root, "first.txt"), "first work\n");
        await CommitFilesAsync(repo, repo.Root, ct);
        File.WriteAllText(Path.Combine(repo.Root, "first.txt"), "changed before second landed\n");
        File.WriteAllText(Path.Combine(repo.Root, "second.txt"), "second work\n");
        await CommitFilesAsync(repo, repo.Root, ct);
        await repo.GitAsync(["push", "origin", "main"], ct);
        await AssertRefusedAsync(lab, run, $"unmerged: not on {run.Record.Workspace.BaseRef} or refs/remotes/origin/main; fetch if it landed recently", ct);
    }

    [Test]
    [Timeout(60_000)]
    public async Task Clean_continues_other_tasks_after_a_landing_probe_failure(CancellationToken ct)
    {
        using var lab = new CoreLab("probe-siblings");
        using var origin = new TempTree();
        var (repo, run) = await LandingWorktreeAsync(lab, origin, ct);
        File.WriteAllText(Path.Combine(run.Record.Workspace.Path!, "only-copy.txt"), "keep this work\n");
        await CommitFilesAsync(repo, run.Record.Workspace.Path!, ct);
        run.Record.Workspace.BaseSha = new string('f', 40);
        var sibling = new TaskRun { Record = new()
        {
            Spec = new() { Id = "sibling", Kind = "code", Base = run.Record.Spec.Base },
            Settings = new() { Backend = new() { Value = "codex" } },
        } };
        await Worktree.PrepareAsync(sibling, lab.Ledger, repo.Root, ct);
        lab.Ledger.Write(sibling.Id, "final", new() { ["state"] = "done" });
        var (exit, _) = await CleanCommandAsync(lab.Ledger, run, ct);
        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(File.ReadAllText(Path.Combine(run.Record.Workspace.Path!, "only-copy.txt"))).IsEqualTo("keep this work\n");
        await Assert.That(Directory.Exists(sibling.Record.Workspace.Path)).IsFalse();
        await Assert.That(await repo.GitAsync(["branch", "--format=%(refname:short)"], ct)).IsEqualTo($"{run.Record.Workspace.Branch}\nmain");
        await Assert.That(lab.Ledger.Read().Where(x => x.Type is "cleaned" or "clean-failed").Select(x => x.Task + ":" + x.Type
            + (x.Type == "clean-failed" ? ":" + x.Data!["error"]!.GetValue<string>() : "")))
            .IsEquivalentTo(new[] { "inspect:clean-failed:workspace cleanup refused", "sibling:cleaned" }, CollectionOrdering.Matching);
    }

    static async Task<(LabRepo Repo, TaskRun Run)> LandingWorktreeAsync(CoreLab lab, TempTree origin, CancellationToken ct)
    {
        var repo = await LabRepo.CreateAsync(lab.Tree, ct);
        File.WriteAllText(Path.Combine(repo.Root, ".gitattributes"), "crlf.txt text eol=crlf\n*.bin -text\n");
        await CommitFilesAsync(repo, repo.Root, ct);
        await repo.GitAsync(["init", "--bare", "--initial-branch=main", origin.Repository], ct);
        await repo.GitAsync(["remote", "add", "origin", origin.Repository], ct);
        await repo.GitAsync(["push", "origin", "main"], ct);
        await repo.GitAsync(["symbolic-ref", "refs/remotes/origin/HEAD", "refs/remotes/origin/main"], ct);
        var run = new TaskRun { Record = new()
        {
            Spec = new() { Id = "inspect", Kind = "code", Base = await repo.GitAsync(["rev-parse", "HEAD"], ct), Scope = ["**"] },
            Settings = new() { Backend = new() { Value = "codex" } },
        } };
        var info = lab.Ledger.LoadRunInfo();
        info.Repo = repo.Root;
        Json.WriteFile(Path.Combine(lab.Ledger.Dir, "run.json"), info, LedgerJson.Files.RunInfo);
        await Worktree.PrepareAsync(run, lab.Ledger, repo.Root, ct);
        return (repo, run);
    }

    static async Task<string> CommitFilesAsync(LabRepo repo, string path, CancellationToken ct)
    {
        await repo.GitAsync(["-C", path, "add", "-A"], ct);
        await repo.GitAsync(["-C", path, "commit", "-m", "Fixture content"], ct);
        return await repo.GitAsync(["-C", path, "rev-parse", "HEAD"], ct);
    }

    static async Task<string> SquashAsync(LabRepo repo, TaskRun run, CancellationToken ct)
    {
        await repo.GitAsync(["merge", "--squash", run.Record.Workspace.Branch!], ct);
        await repo.GitAsync(["commit", "-m", "Landed squash"], ct);
        return await repo.GitAsync(["rev-parse", "HEAD"], ct);
    }

    static async Task<(int Exit, string Output)> CleanCommandAsync(Ledger ledger, TaskRun run, CancellationToken ct)
    {
        ledger.SaveRecord(run.Record);
        ledger.Write(run.Id, "final", new() { ["state"] = "done" });
        var output = TestContext.Current!.Output;
        var start = output.GetStandardOutput().Length;
        var exit = await Commands.CleanAsync(Args.Parse(["clean", ledger.RunId]), ct);
        return (exit, output.GetStandardOutput()[start..]);
    }

    static async Task AssertLandedAsync(CoreLab lab, TaskRun run, string reason, CancellationToken ct, Ledger? ledger = null)
    {
        ledger ??= lab.Ledger;
        var repo = new LabRepo(lab.Tree);
        var patch = await repo.GitAsync(["diff", "--binary", "--full-index", run.Record.Workspace.BaseSha!, run.Record.Workspace.Branch!], ct);
        await ledger.WriteArtifactAsync(run.Id, "diff.patch", patch, ct);
        var (exit, output) = await CleanCommandAsync(ledger, run, ct);
        await Assert.That(output.ReplaceLineEndings("\n").Split('\n')[0]).IsEqualTo($"{run.Id}: landed ({reason})");
        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(ledger.Read().Single(x => x.Task == run.Id && x.Type == "cleaned").Data!["landed"]!.GetValue<string>()).IsEqualTo(reason);
        await Assert.That(Directory.Exists(run.Record.Workspace.Path)).IsFalse();
        await Assert.That(await repo.GitAsync(["branch", "--list", run.Record.Workspace.Branch!, "--format=%(refname:short)"], ct)).IsEqualTo("");
        await Assert.That(File.ReadAllText(Path.Combine(ledger.TaskDir(run.Id), "diff.patch"))).IsEqualTo(patch);
    }

    static async Task AssertRefusedAsync(CoreLab lab, TaskRun run, string reason, CancellationToken ct)
    {
        var repo = new LabRepo(lab.Tree);
        var path = run.Record.Workspace.Path!;
        string[] Files() => Directory.GetFiles(path, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal)
            .Select(file => Path.GetRelativePath(path, file) + ":" + Convert.ToBase64String(File.ReadAllBytes(file))).ToArray();
        var files = Files();
        var registration = await repo.GitAsync(["worktree", "list", "--porcelain"], ct);
        var tip = await repo.GitAsync(["rev-parse", run.Record.Workspace.Branch!], ct);
        var (exit, output) = await CleanCommandAsync(lab.Ledger, run, ct);
        await Assert.That(output.ReplaceLineEndings("\n").Split('\n')[0]).IsEqualTo($"{run.Id}: refused ({reason}); --force removes it anyway");
        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(Directory.Exists(path)).IsTrue();
        await Assert.That(Files()).IsEquivalentTo(files, CollectionOrdering.Matching);
        await Assert.That(await repo.GitAsync(["worktree", "list", "--porcelain"], ct)).IsEqualTo(registration);
        await Assert.That(await repo.GitAsync(["rev-parse", run.Record.Workspace.Branch!], ct)).IsEqualTo(tip);
        await Assert.That(lab.Ledger.Read().Where(x => x.Type is "cleaned" or "clean-failed").Select(x => x.Type + ":" + x.Data?["error"]?.GetValue<string>()))
            .IsEquivalentTo(new[] { "clean-failed:workspace cleanup refused" }, CollectionOrdering.Matching);
    }

    static async Task<(LabRepo Repo, TaskRun Run, string Path)> SubmoduleWorktreeAsync(CoreLab lab, LabRepo upstream, CancellationToken ct)
    {
        var run = await lab.TaskAsync("inspect", ct);
        var repo = new LabRepo(lab.Tree);
        foreach (var name in new[] { "subA", "subB" })
            await repo.GitAsync(["-c", "protocol.file.allow=always", "submodule", "add", upstream.Root, name], ct);
        await repo.GitAsync(["commit", "-am", "Add submodules"], ct);
        run.Record.Workspace = new();
        run.Record.Spec.Base = "main";
        await Worktree.PrepareAsync(run, lab.Ledger, repo.Root, ct, lab.Git);
        var path = run.Record.Workspace.Path!;
        await repo.GitAsync(["-c", "protocol.file.allow=always", "-C", path, "submodule", "update", "--init", "--recursive"], ct);
        return (repo, run, path);
    }

    static async Task<(bool Removed, string Output)> CaptureCleanAsync(CoreLab lab, TaskRun run, bool force, CancellationToken ct)
    {
        var output = TestContext.Current!.Output;
        var start = output.GetStandardOutput().Length;
        var removed = await Worktree.CleanAsync(lab.Ledger, run.Record, lab.Tree.Repository, force, ct, lab.Git);
        return (removed, output.GetStandardOutput()[start..]);
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    [Timeout(60_000)]
    public async Task Clean_RecoversBrokenLink(bool dangling, bool metadata, CancellationToken ct)
    {
        using var lab = new CoreLab("partial");
        var run = await lab.TaskAsync("inspect", ct);
        run.Record.Workspace = new();
        run.Record.Spec.Base = "main";
        await Worktree.PrepareAsync(run, lab.Ledger, lab.Tree.Repository, ct, lab.Git);
        var path = run.Record.Workspace.Path!;
        var link = Path.Combine(path, ".git");
        var admin = File.ReadAllText(link)[8..].Trim();
        File.Delete(link);
        Directory.CreateDirectory(link);
        await Assert.That(async () => await Worktree.CleanAsync(lab.Ledger, run.Record, lab.Tree.Repository, true, ct, lab.Git)).Throws<HarnessException>().WithMessage($"refused an unrecognized worktree: {path}");
        Directory.Delete(link);
        if (!metadata) Directory.Delete(admin, true);
        if (dangling) File.WriteAllText(link, "gitdir: missing-metadata\n");
        var removed = true;
        await Assert.That(async () => { removed = await Worktree.CleanAsync(lab.Ledger, run.Record, lab.Tree.Repository, false, ct, lab.Git); }).ThrowsNothing();
        await Assert.That(removed).IsFalse();
        await Assert.That(string.Join(",", Directory.GetFileSystemEntries(path).Select(Path.GetFileName).Order()))
            .IsEqualTo(dangling ? ".git,README.md" : "README.md");
        var repo = new LabRepo(lab.Tree);
        await Assert.That(await repo.GitAsync(["branch", "--list", run.Record.Workspace.Branch!], ct)).Contains(run.Record.Workspace.Branch!);
        await Assert.That(await Worktree.CleanAsync(lab.Ledger, run.Record, repo.Root, true, ct, lab.Git)).IsTrue();
        await Assert.That(Directory.Exists(path)).IsFalse();
        await Assert.That(await repo.GitAsync(["worktree", "list", "--porcelain"], ct)).IsEqualTo($"worktree {repo.Root.Replace('\\', '/')}\nHEAD {run.Record.Workspace.BaseSha}\nbranch refs/heads/main");
        await Assert.That(await repo.GitAsync(["branch", "--list", run.Record.Workspace.Branch!], ct)).IsEqualTo("");
    }

    [Test]
    [Timeout(60_000)]
    public async Task Clean_force_removes_a_worktree_with_paths_past_the_Windows_limit(CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows()) Skip.Test("The 260-character limit is a Windows limit.");
        // node_modules trees pass 260 characters; Git for Windows needs core.longpaths to delete them.
        using var lab = new CoreLab("long-paths");
        var run = await lab.TaskAsync("inspect", ct);
        run.Record.Workspace = new();
        run.Record.Spec.Base = "main";
        await Worktree.PrepareAsync(run, lab.Ledger, lab.Tree.Repository, ct, lab.Git);
        var path = run.Record.Workspace.Path!;
        var deep = Path.Combine(path, "node_modules", new string('d', 120), new string('e', 120));
        Directory.CreateDirectory(deep);
        File.WriteAllText(Path.Combine(deep, "package.json"), "{}");
        await Assert.That(Path.Combine(deep, "package.json").Length).IsGreaterThan(260);
        await Assert.That(await Worktree.CleanAsync(lab.Ledger, run.Record, lab.Tree.Repository, true, ct, lab.Git)).IsTrue();
        await Assert.That(Directory.Exists(path)).IsFalse();
    }

    [Test]
    [Timeout(60_000)]
    public async Task Clean_removes_a_worktree_that_git_itself_cannot_delete(CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows()) Skip.Test("Git for Windows' path limit is a Windows limit.");
        // Past about 4096 characters Git for Windows fails even with core.longpaths; .NET deletes such a tree.
        using var lab = new CoreLab("past-git-limit");
        var run = await lab.TaskAsync("inspect", ct);
        run.Record.Workspace = new();
        run.Record.Spec.Base = "main";
        await Worktree.PrepareAsync(run, lab.Ledger, lab.Tree.Repository, ct, lab.Git);
        var path = run.Record.Workspace.Path!;
        var deep = Path.Combine([path, "node_modules", .. Enumerable.Repeat(new string('n', 200), 22)]);
        Directory.CreateDirectory(deep);
        File.WriteAllText(Path.Combine(deep, "package.json"), "{}");
        await Assert.That(deep.Length).IsGreaterThan(4096);
        await Assert.That(await Worktree.CleanAsync(lab.Ledger, run.Record, lab.Tree.Repository, true, ct, lab.Git)).IsTrue();
        await Assert.That(Directory.Exists(path)).IsFalse();
        await Assert.That(await new LabRepo(lab.Tree).GitAsync(["worktree", "list", "--porcelain"], ct)).DoesNotContain(path.Replace('\\', '/'));
    }

    [Test]
    [Timeout(60_000)]
    public async Task Clean_without_force_keeps_a_worktree_git_refuses_to_remove(CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows()) Skip.Test("Git for Windows' path limit is a Windows limit.");
        // Without --force a git refusal stands (it guards data the dirty check cannot see, such as submodules).
        using var lab = new CoreLab("refused");
        var run = await lab.TaskAsync("inspect", ct);
        run.Record.Workspace = new();
        run.Record.Spec.Base = "main";
        await Worktree.PrepareAsync(run, lab.Ledger, lab.Tree.Repository, ct, lab.Git);
        var path = run.Record.Workspace.Path!;
        var deep = Path.Combine([path, "node_modules", .. Enumerable.Repeat(new string('n', 200), 22)]);
        Directory.CreateDirectory(deep);
        File.WriteAllText(Path.Combine(path, ".git", "..", ".gitignore"), "node_modules/\n");
        await new LabRepo(lab.Tree).GitAsync(["-C", path, "add", ".gitignore"], ct);
        await new LabRepo(lab.Tree).GitAsync(["-C", path, "commit", "-m", "Ignore node_modules"], ct);
        await new LabRepo(lab.Tree).GitAsync(["update-ref", "refs/heads/main", run.Record.Workspace.Branch!], ct);
        await Assert.That(async () => await Worktree.CleanAsync(lab.Ledger, run.Record, lab.Tree.Repository, false, ct, lab.Git))
            .Throws<HarnessException>().WithMessageContaining("git worktree remove failed");
        await Assert.That(Directory.Exists(deep)).IsTrue();
    }

    [Test]
    public async Task RealPath_keeps_a_backslash_in_a_Unix_name()
    {
        if (OperatingSystem.IsWindows()) Skip.Test("A backslash is a separator on Windows.");
        using var tree = new TempTree();
        var repo = Directory.CreateDirectory(Path.Combine(tree.Root, "repo")).FullName;
        var link = Path.Combine(tree.Root, "alias\\name");
        Directory.CreateSymbolicLink(link, repo);
        try
        {
            await Assert.That(Worktree.IsInside(Worktree.RealPath(Path.Combine(link, "scratch")), Worktree.RealPath(repo))).IsTrue();
        }
        finally { Directory.Delete(link); }
    }

    [Test]
    [Timeout(60_000)]
    public async Task RealPath_resolves_a_link_before_a_later_parent_step(CancellationToken ct)
    {
        using var tree = new TempTree();
        var repo = Directory.CreateDirectory(Path.Combine(tree.Root, "repo", "child")).Parent!.FullName;
        Directory.CreateDirectory(Path.Combine(tree.Root, "safe"));
        var hop = Path.Combine(tree.Root, "safe", "hop");
        var alias = Path.Combine(tree.Root, "alias");
        string expected;
        if (OperatingSystem.IsWindows())
        {
            // A junction stores a normalized absolute target, so Windows checks a chain of links.
            foreach (var (link, target) in new[] { (hop, Path.Combine(repo, "child")), (alias, hop) })
            {
                using var command = new CapturedProcess();
                (await command.RunAsync("cmd", ["/c", "mklink", "/J", link, target], tree.Root, tree.ChildEnvironment(), ct)).RequireSuccess("junction fixture");
            }
            expected = Path.Combine(repo, "child", "scratch");
        }
        else
        {
            Directory.CreateSymbolicLink(hop, Path.Combine(repo, "child"));
            Directory.CreateSymbolicLink(alias, Path.Combine(hop, ".."));
            expected = Path.Combine(repo, "scratch");
        }
        try
        {
            await Assert.That(Locations.Same(Worktree.RealPath(Path.Combine(alias, "scratch")), Worktree.RealPath(expected))).IsTrue();
            await Assert.That(Worktree.IsInside(Worktree.RealPath(Path.Combine(alias, "scratch")), Worktree.RealPath(repo))).IsTrue();
        }
        finally
        {
            // The links must go before TempTree deletes the tree, or deletion follows them.
            if (Path.Exists(alias)) Directory.Delete(alias);
            if (Path.Exists(hop)) Directory.Delete(hop);
        }
    }

    [Test]
    [Timeout(60_000)]
    public async Task Clean_never_removes_another_branchs_worktree_in_the_task_folder(CancellationToken ct)
    {
        using var lab = new CoreLab("replaced");
        var run = await lab.TaskAsync("inspect", ct);
        run.Record.Workspace = new();
        run.Record.Spec.Base = "main";
        await Worktree.PrepareAsync(run, lab.Ledger, lab.Tree.Repository, ct, lab.Git);
        var path = run.Record.Workspace.Path!;
        var repo = new LabRepo(lab.Tree);
        await repo.GitAsync(["worktree", "remove", "--force", path], ct);
        await repo.GitAsync(["worktree", "add", "-b", "lane/other", path, "main"], ct);
        File.WriteAllText(Path.Combine(path, "theirs.txt"), "foreign work");
        await Assert.That(async () => await Worktree.CleanAsync(lab.Ledger, run.Record, repo.Root, true, ct, lab.Git))
            .Throws<HarnessException>().WithMessage($"refused a worktree on another branch: {path}");
        await Assert.That(File.ReadAllText(Path.Combine(path, "theirs.txt"))).IsEqualTo("foreign work");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    [Timeout(60_000)]
    public async Task Clean_keeps_a_detached_worktree_whose_HEAD_holds_unmerged_commits(bool folderGone, CancellationToken ct)
    {
        using var lab = new CoreLab("detached-work");
        var run = await lab.TaskAsync("inspect", ct);
        run.Record.Workspace = new();
        run.Record.Spec.Base = "main";
        await Worktree.PrepareAsync(run, lab.Ledger, lab.Tree.Repository, ct, lab.Git);
        var path = run.Record.Workspace.Path!;
        var repo = new LabRepo(lab.Tree);
        await repo.GitAsync(["-C", path, "checkout", "--detach"], ct);
        File.WriteAllText(Path.Combine(path, "detached.txt"), "work");
        await repo.GitAsync(["-C", path, "add", "detached.txt"], ct);
        await repo.GitAsync(["-C", path, "commit", "-m", "Detached work"], ct);
        var head = await repo.GitAsync(["-C", path, "rev-parse", "HEAD"], ct);
        // Git keeps the registration, and its HEAD, when the folder alone disappears.
        if (folderGone) Directory.Delete(path, recursive: true);
        await Assert.That(await Worktree.CleanAsync(lab.Ledger, run.Record, repo.Root, false, ct, lab.Git)).IsFalse();
        await Assert.That(await repo.GitAsync(["worktree", "list", "--porcelain"], ct)).Contains("HEAD " + head);
    }

    [Test]
    [Timeout(60_000)]
    public async Task Clean_force_removes_a_detached_worktree(CancellationToken ct)
    {
        // A worker that stops mid-rebase or detaches HEAD leaves no branch line in git's registration.
        using var lab = new CoreLab("detached");
        var run = await lab.TaskAsync("inspect", ct);
        run.Record.Workspace = new();
        run.Record.Spec.Base = "main";
        await Worktree.PrepareAsync(run, lab.Ledger, lab.Tree.Repository, ct, lab.Git);
        var path = run.Record.Workspace.Path!;
        var repo = new LabRepo(lab.Tree);
        await repo.GitAsync(["-C", path, "checkout", "--detach"], ct);
        await Assert.That(await Worktree.CleanAsync(lab.Ledger, run.Record, repo.Root, true, ct, lab.Git)).IsTrue();
        await Assert.That(Directory.Exists(path)).IsFalse();
        await Assert.That(await repo.GitAsync(["worktree", "list", "--porcelain"], ct)).DoesNotContain(path.Replace('\\', '/'));
        await Assert.That(await repo.GitAsync(["branch", "--list", run.Record.Workspace.Branch!], ct)).IsEqualTo("");
    }

    [Test]
    [Arguments("dirty", false)]
    [Arguments("untracked", false)]
    [Arguments("unmerged", false)]
    [Arguments("dirty", true)] // Intact cases are guards.
    [Arguments("untracked", true)]
    [Arguments("unmerged", true)]
    [Timeout(60_000)]
    public async Task Clean_PreservesUnsafeResidualWork(string state, bool intact, CancellationToken ct)
    {
        using var lab = new CoreLab("unsafe");
        var run = await lab.TaskAsync("inspect", ct);
        run.Record.Workspace = new();
        run.Record.Spec.Base = "main";
        await Worktree.PrepareAsync(run, lab.Ledger, lab.Tree.Repository, ct, lab.Git);
        var path = run.Record.Workspace.Path!;
        var file = Path.Combine(path, state == "untracked" ? "new.txt" : "README.md");
        File.WriteAllText(file, "retained work\n");
        if (state == "unmerged")
        {
            await lab.Git.CheckAsync(path, ct, "add", "README.md");
            await lab.Git.CheckAsync(path, ct, "commit", "-m", "Unmerged work");
        }
        if (!intact) File.Delete(Path.Combine(path, ".git"));
        if (!intact) File.SetAttributes(file, FileAttributes.ReadOnly);
        var removed = true;
        await Assert.That(async () => { removed = await Worktree.CleanAsync(lab.Ledger, run.Record, lab.Tree.Repository, false, ct, lab.Git); }).ThrowsNothing();
        await Assert.That(removed).IsFalse();
        await Assert.That(File.ReadAllText(file)).IsEqualTo("retained work\n");
        await Assert.That(await Worktree.CleanAsync(lab.Ledger, run.Record, lab.Tree.Repository, true, ct, lab.Git)).IsTrue();
        await Assert.That(Directory.Exists(path)).IsFalse();
        await Assert.That(await new LabRepo(lab.Tree).GitAsync(["branch", "--list", run.Record.Workspace.Branch!], ct)).IsEqualTo("");
    }

    [Test]
    [Timeout(60_000)]
    public async Task TempRoot_preparation_and_cleanup_are_task_scoped(CancellationToken ct)
    {
        using var tree = new TempTree();
        var repo = await LabRepo.CreateAsync(tree, ct);
        var root = Directory.CreateDirectory(Path.Combine(tree.Root, "private-temp")).FullName;
        var sibling = Path.Combine(Directory.CreateDirectory(Path.Combine(root, "sibling")).FullName, "keep");
        File.WriteAllText(sibling, "keep");
        var tasks = Path.Combine(tree.Root, "tasks.jsonc");
        var document = System.Text.Json.Nodes.JsonNode.Parse("""{"schemaVersion":2,"run":"cleanup-temp","defaults":{"backend":"opencode"},"tasks":[{"id":"inspect","kind":"research","base":"main","route":"fixture/model","prompt":"Inspect."}]}""")!;
        document["defaults"]!["tempRoot"] = root;
        File.WriteAllText(tasks, document.ToJsonString());
        using var process = new HarnessProcess(tree);
        (await process.RunAsync(["run", tasks, "--repo", repo.Root], ct,
            FakeOpenCode.Environment(tree, "one-task.jsonl").ToDictionary(x => x.Key, x => (string?)x.Value))).RequireSuccess("run");
        var ledger = Directory.GetDirectories(Path.Combine(tree.DispatchHome, "state", "ledgers")).Single();
        var run = Path.GetFileName(ledger);
        var temp = Path.Combine(root, run, "inspect");
        await Assert.That(Directory.Exists(temp)).IsTrue();
        var defaultTemp = Path.Combine(tree.DispatchHome, "state", "worktrees", run, ".tmp", "inspect");
        await Assert.That(Directory.Exists(defaultTemp)).IsFalse();
        using var clean = new HarnessProcess(tree);
        (await clean.RunAsync(["clean", run], ct,
            FakeOpenCode.Environment(tree, "cleanup.jsonl").ToDictionary(x => x.Key, x => (string?)x.Value))).RequireSuccess("clean");
        await Assert.That(Directory.Exists(temp)).IsFalse();
        await Assert.That(Directory.Exists(defaultTemp)).IsFalse();
        await Assert.That(File.ReadAllText(sibling)).IsEqualTo("keep");
    }

    [Test]
    [Timeout(30_000)]
    public async Task Clean_removes_configured_and_legacy_task_temp(CancellationToken ct)
    {
        using var lab = new CoreLab("legacy-temp");
        var run = await lab.TaskAsync("inspect", ct);
        var root = Path.Combine(lab.Tree.Root, "private-temp");
        run.Record.Settings.TempRoot = new() { Value = root, From = "task" };
        lab.Ledger.SaveRecord(run.Record);
        var temp = Path.Combine(root, "legacy-temp", "inspect");
        var defaultTemp = Path.Combine(lab.Tree.WorktreeRoot, "legacy-temp", ".tmp", "inspect");
        Directory.CreateDirectory(temp);
        Directory.CreateDirectory(defaultTemp);

        await Assert.That(await Worktree.CleanAsync(lab.Ledger, run.Record, lab.Tree.Repository, false, ct, lab.Git)).IsTrue();
        await Assert.That(Directory.Exists(temp)).IsFalse();
        await Assert.That(Directory.Exists(defaultTemp)).IsFalse();
    }

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
        using var lab = new CoreLab("workspace", recorded: false);
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
        await Assert.That(File.Exists(Path.Combine(Locations.LedgerRoot, runId, "run.json"))).IsFalse();
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

        var cleanupEnvironment = FakeOpenCode.Environment(tree, "cleanup.jsonl").ToDictionary(x => x.Key, x => (string?)x.Value);
        using var clean = new HarnessProcess(tree);
        (await clean.RunAsync(["clean", runId], ct, cleanupEnvironment)).RequireSuccess("clean in-place");
        using var repeat = new HarnessProcess(tree);
        (await repeat.RunAsync(["clean", runId], ct, cleanupEnvironment)).RequireSuccess("repeat clean in-place");

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
    public async Task InPlace_LedgerRootInsideRepo_RefusedExit2(string command, CancellationToken ct)
    {
        using var tree = new TempTree();
        var repo = await LabRepo.CreateAsync(tree, ct);
        var tasks = Path.Combine(tree.Root, "tasks.jsonc");
        await File.WriteAllTextAsync(tasks, """{"schemaVersion":2,"defaults":{"backend":"opencode"},"tasks":[{"id":"inspect","kind":"research","workspace":"in-place","route":"fixture/model","prompt":"Inspect."}]}""", ct);
        var ledger = Path.Combine(repo.Root, "runs");
        using var harness = new HarnessProcess(tree);
        var result = await harness.RunAsync([command, tasks, "--repo", repo.Root, "--ledger", ledger], ct,
            FakeOpenCode.Environment(tree, "one-task.jsonl").ToDictionary(x => x.Key, x => (string?)x.Value));
        await Assert.That(result.ExitCode).IsEqualTo(2);
        await Assert.That(result.Stdout + result.Stderr).Contains("ledger root inside the repository");
        await Assert.That(Directory.Exists(ledger)).IsFalse();
        await Assert.That(File.Exists(FakeOpenCode.Record(tree))).IsFalse();
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
        await Assert.That(lab.Git.ControlFolder).IsEqualTo(Locations.ControlDir(lab.Ledger.RunId));
        await Assert.That(File.Exists(Path.Combine(lab.Git.ControlFolder, "index-inspect"))).IsFalse();
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
