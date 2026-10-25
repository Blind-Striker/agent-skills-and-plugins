using Dispatch.Tests.Support;
using System.Text.Json.Nodes;
using TUnit.Assertions.Enums;

namespace Dispatch.Tests.Integration;

[Category("Integration")]
[NotInParallel]
public sealed class SettlementStateTests
{
    [Test]
    [Timeout(60_000)]
    public async Task Harness_commit_uses_conventional_commit_message(CancellationToken ct)
    {
        using var lab = new CoreLab("commit-message");
        var run = await lab.TaskAsync("change-file", ct);
        await PrepareAsync(lab, run, "worktree", "harness", ct);
        var wt = run.Record.Workspace.Path!;
        File.WriteAllText(Path.Combine(wt, "README.md"), "worker change\n");
        var result = await SettleAsync(lab, run, "done", ct);
        await Assert.That(result.Failures).IsEmpty();
        await Assert.That(result.Commit).IsNotNull();
        var repo = new LabRepo(lab.Tree);
        var message = await repo.GitAsync(["-C", wt, "log", "-1", "--format=%B", result.Commit!], ct);
        await Assert.That(message.Trim()).IsEqualTo($"chore(dispatch): {lab.Ledger.RunId}/{run.Id}");
    }

    [Test]
    [Arguments("stopped", "worktree", "harness")]
    [Arguments("blocked", "worktree", "harness")]
    [Arguments("stopped", "in-place", "harness")]
    [Arguments("blocked", "in-place", "harness")]
    [Arguments("stopped", "in-place", "none")]
    [Arguments("blocked", "in-place", "none")]
    [Arguments("done", "worktree", "harness")]
    [Timeout(60_000)]
    public async Task Stopped_workspace_retains_wip(string status, string layout, string commit, CancellationToken ct)
    {
        using var lab = new CoreLab("wip");
        var run = await lab.TaskAsync("inspect", ct);
        var repo = new LabRepo(lab.Tree);
        File.WriteAllText(Path.Combine(repo.Root, "deleted.txt"), "remove me\n");
        File.WriteAllBytes(Path.Combine(repo.Root, "binary.bin"), [0, 1, 2]);
        await repo.GitAsync(["add", "."], ct);
        await repo.GitAsync(["commit", "-m", "patch baseline"], ct);
        await PrepareAsync(lab, run, layout, commit, ct);
        var wt = run.Record.Workspace.Path!;
        File.WriteAllText(Path.Combine(wt, "README.md"), "staged worker\n");
        await repo.GitAsync(["-C", wt, "add", "README.md"], ct);
        File.AppendAllText(Path.Combine(wt, "README.md"), "unstaged worker\n");
        byte[] binary = [0, 255, 128, 0, 42];
        File.WriteAllBytes(Path.Combine(wt, "binary.bin"), binary);
        File.Delete(Path.Combine(wt, "deleted.txt"));
        File.WriteAllText(Path.Combine(wt, "new.txt"), "untracked worker\n");
        var indexPath = await repo.GitAsync(["-C", wt, "rev-parse", "--path-format=absolute", "--git-path", "index"], ct);
        var index = Convert.ToBase64String(File.ReadAllBytes(indexPath));
        var result = await SettleAsync(lab, run, status, ct);
        var stopped = status != "done";
        var folder = lab.Ledger.TaskDir(run.Id);
        var saved = JsonNode.Parse(File.ReadAllText(Path.Combine(folder, "result.json")))!;
        await Assert.That(result.Failures).IsEquivalentTo(stopped ? new[] { "worker-stopped" } : [], CollectionOrdering.Matching);
        await Assert.That(result.FilesChanged).IsEquivalentTo(new[] { "README.md", "binary.bin", "deleted.txt", "new.txt" }, CollectionOrdering.Matching);
        await Assert.That(result.OutOfScope).IsEmpty();
        await Assert.That(saved["wipPatch"]?.GetValue<string>()).IsEqualTo(stopped ? "wip.patch" : null);
        await Assert.That(saved["wipFileCount"]?.GetValue<int>()).IsEqualTo(stopped ? 4 : (int?)null);
        await Assert.That(File.Exists(Path.Combine(folder, "wip.patch"))).IsEqualTo(stopped);
        var head = await repo.GitAsync(["-C", wt, "rev-parse", "HEAD"], ct);
        await Assert.That(head).IsEqualTo(stopped ? run.Record.Workspace.BaseSha : result.Commit);
        if (stopped)
        {
            await Assert.That(result.Commit).IsNull();
            await Assert.That(Convert.ToBase64String(File.ReadAllBytes(indexPath))).IsEqualTo(index);
            var diff = File.ReadAllBytes(Path.Combine(folder, "diff.patch"));
            await Assert.That(Convert.ToBase64String(diff)).IsEqualTo(commit == "none" ? Convert.ToBase64String(File.ReadAllBytes(Path.Combine(folder, "wip.patch"))) : "");
        }
        var apply = Path.Combine(lab.Tree.Root, "apply");
        await repo.GitAsync(["worktree", "add", "--detach", apply, run.Record.Workspace.BaseSha!], ct);
        await repo.GitAsync(["-C", apply, "apply", "--index", Path.Combine(folder, stopped ? "wip.patch" : "diff.patch")], ct);
        await Assert.That(await repo.GitAsync(["-C", apply, "ls-files"], ct)).IsEqualTo("README.md\nbinary.bin\nnew.txt");
        foreach (var path in new[] { wt, apply })
        {
            await Assert.That(File.ReadAllText(Path.Combine(path, "README.md"))).IsEqualTo("staged worker\nunstaged worker\n");
            await Assert.That(File.ReadAllText(Path.Combine(path, "new.txt"))).IsEqualTo("untracked worker\n");
            await Assert.That(File.ReadAllBytes(Path.Combine(path, "binary.bin"))).IsEquivalentTo(binary, CollectionOrdering.Matching);
            await Assert.That(File.Exists(Path.Combine(path, "deleted.txt"))).IsFalse();
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    [Timeout(60_000)]
    public async Task Stopped_in_place_uses_start_snapshot(bool unborn, CancellationToken ct)
    {
        using var lab = new CoreLab("start-snapshot");
        var run = await lab.TaskAsync("inspect", ct);
        var repo = new LabRepo(lab.Tree);
        if (unborn)
        {
            await repo.GitAsync(["checkout", "--orphan", "empty"], ct);
            await repo.GitAsync(["rm", "-rf", "."], ct);
        }
        File.WriteAllText(Path.Combine(repo.Root, "README.md"), "operator staged\n");
        await repo.GitAsync(["add", "README.md"], ct);
        File.AppendAllText(Path.Combine(repo.Root, "README.md"), "operator unstaged\n");
        File.WriteAllText(Path.Combine(repo.Root, "operator.txt"), "operator untracked\n");
        await PrepareAsync(lab, run, "in-place", "none", ct);
        var index = Convert.ToBase64String(File.ReadAllBytes(Path.Combine(repo.Root, ".git", "index")));
        File.AppendAllText(Path.Combine(repo.Root, "README.md"), "worker\n");
        var result = await SettleAsync(lab, run, "stopped", ct);
        await Assert.That(result.FilesChanged).IsEquivalentTo(new[] { "README.md" }, CollectionOrdering.Matching);
        await Assert.That(Convert.ToBase64String(File.ReadAllBytes(Path.Combine(repo.Root, ".git", "index")))).IsEqualTo(index);
        await Assert.That(File.Exists(Path.Combine(lab.Ledger.TaskDir(run.Id), "wip.patch"))).IsTrue();
        var start = lab.Ledger.Read().Single(x => x.Type == "worktree").Data!["startTree"]!.GetValue<string>();
        var apply = Path.Combine(lab.Tree.Root, "apply");
        Directory.CreateDirectory(apply);
        var isolated = lab.Git.WithIndex(Path.Combine(lab.Tree.Root, "apply-index"));
        await isolated.CheckAsync(apply, ct, "--git-dir=" + Path.Combine(repo.Root, ".git"), "--work-tree=" + apply, "read-tree", "--reset", "-u", start);
        await isolated.CheckAsync(apply, ct, "--git-dir=" + Path.Combine(repo.Root, ".git"), "--work-tree=" + apply, "apply", "--index", Path.Combine(lab.Ledger.TaskDir(run.Id), "wip.patch"));
        await Assert.That(File.ReadAllText(Path.Combine(apply, "README.md"))).IsEqualTo("operator staged\noperator unstaged\nworker\n");
        await Assert.That(File.ReadAllText(Path.Combine(apply, "operator.txt"))).IsEqualTo("operator untracked\n");
        await Assert.That(await isolated.CheckAsync(apply, ct, "--git-dir=" + Path.Combine(repo.Root, ".git"), "ls-files")).IsEqualTo("README.md\noperator.txt\n");
        await Assert.That(result.BaseSha).IsEqualTo(unborn ? null : await repo.GitAsync(["rev-parse", "HEAD"], ct));
    }

    [Test]
    [Timeout(60_000)]
    public async Task Stopped_workspace_preserves_scope_checks(CancellationToken ct)
    {
        using var lab = new CoreLab("scope-stop");
        var run = await lab.TaskAsync("inspect", ct);
        await PrepareAsync(lab, run, "worktree", "harness", ct);
        run.Record.Settings.Scope = new() { Value = ["*.txt", "!excluded.txt"] };
        var wt = run.Record.Workspace.Path!;
        Directory.CreateDirectory(Path.Combine(wt, ".dispatch"));
        foreach (var path in new[] { "new.txt", "excluded.txt", ".dispatch/config.jsonc" })
            File.WriteAllText(Path.Combine(wt, path), "worker\n");
        var result = await SettleAsync(lab, run, "stopped", ct);
        await Assert.That(result.FilesChanged).IsEquivalentTo(new[] { ".dispatch/config.jsonc", "excluded.txt", "new.txt" }, CollectionOrdering.Matching);
        await Assert.That(result.OutOfScope).IsEquivalentTo(new[] { ".dispatch/config.jsonc", "excluded.txt" }, CollectionOrdering.Matching);
        await Assert.That(result.Failures).IsEquivalentTo(new[] { "out-of-scope", "worker-stopped" }, CollectionOrdering.Matching);
        await Assert.That(File.Exists(Path.Combine(lab.Ledger.TaskDir(run.Id), "wip.patch"))).IsTrue();
    }

    [Test]
    [Arguments("worktree", "harness")]
    [Arguments("in-place", "none")]
    [Timeout(60_000)]
    public async Task Recollection_removes_stale_wip(string layout, string commit, CancellationToken ct)
    {
        using var lab = new CoreLab("recollect");
        var run = await lab.TaskAsync("inspect", ct);
        await PrepareAsync(lab, run, layout, commit, ct);
        var file = Path.Combine(run.Record.Workspace.Path!, "new.txt");
        File.WriteAllText(file, "worker\n");
        await SettleAsync(lab, run, "blocked", ct);
        var folder = lab.Ledger.TaskDir(run.Id);
        await Assert.That(File.Exists(Path.Combine(folder, "wip.patch"))).IsTrue();
        await Assert.That(File.ReadAllText(Path.Combine(folder, "wip.patch"))).Contains("+worker");
        File.Delete(file);
        var result = await SettleAsync(lab, run, "blocked", ct);
        await Assert.That(File.Exists(Path.Combine(folder, "wip.patch"))).IsFalse();
        var saved = JsonNode.Parse(File.ReadAllText(Path.Combine(folder, "result.json")))!;
        await Assert.That(saved["wipPatch"]).IsNull();
        await Assert.That(saved["wipFileCount"]).IsNull();
        await Assert.That(result.Failures).IsEquivalentTo(new[] { "worker-stopped" }, CollectionOrdering.Matching);
    }

    [Test]
    [Arguments("stopped")]
    [Arguments("blocked")]
    [Arguments("done")]
    [Timeout(60_000)]
    public async Task Stopped_verify_is_not_run_but_done_empty_diff_verifies(string status, CancellationToken ct)
    {
        using var lab = new CoreLab("verify-stop");
        var run = await lab.TaskAsync("inspect", ct);
        await PrepareAsync(lab, run, "worktree", "harness", ct);
        var marker = Path.Combine(run.Record.Workspace.Path!, "marker.txt");
        var command = OperatingSystem.IsWindows() ? "Set-Content marker.txt ran; exit 3" : "printf ran > marker.txt; exit 3";
        run.Record.Spec.Verify = [command];
        run.Record.Spec.AdvisoryVerify = ["exit 7"];
        var result = await SettleAsync(lab, run, status, ct);
        await Assert.That(result.Verify.Select(x => x.Command).ToArray()).IsEquivalentTo(new[] { command, "exit 7" }, CollectionOrdering.Matching);
        await Assert.That(result.Verify.Select(x => x.Advisory).ToArray()).IsEquivalentTo(new bool?[] { null, true }, CollectionOrdering.Matching);
        await Assert.That(result.Failures).IsEquivalentTo(status == "done" ? new[] { "verify-failed" } : ["worker-stopped"], CollectionOrdering.Matching);
        await Assert.That(File.Exists(marker)).IsEqualTo(status == "done");
        var saved = JsonNode.Parse(File.ReadAllText(Path.Combine(lab.Ledger.TaskDir(run.Id), "result.json")))!;
        await Assert.That(saved["verify"]!.AsArray().Select(x => x!["notRun"]?.GetValue<string>()).ToArray())
            .IsEquivalentTo(status == "done" ? new string?[] { null, null } : ["worker stopped", "worker stopped"], CollectionOrdering.Matching);
        if (status == "done") await Assert.That(result.Warnings!).IsEquivalentTo(new[] { "advisory-verify-failed" }, CollectionOrdering.Matching);
        else await Assert.That(result.Warnings).IsNull();
        await Assert.That(result.VerifyShell is null).IsEqualTo(status != "done");
        var log = File.ReadAllText(Path.Combine(lab.Ledger.TaskDir(run.Id), "verify.log"));
        await Assert.That(log).Contains(status == "done" ? "[exit 3" : "worker stopped");
    }

    static async Task PrepareAsync(CoreLab lab, TaskRun run, string layout, string commit, CancellationToken ct)
    {
        run.Record.Spec.Kind = "code";
        run.Record.Spec.Scope = ["**"];
        run.Record.Spec.Workspace = layout;
        run.Record.Spec.Commit = commit;
        run.Record.Spec.Base = "main";
        run.Record.Workspace = new();
        await Worktree.PrepareAsync(run, lab.Ledger, lab.Tree.Repository, ct, lab.Git);
        run.SaveWorker(new("opencode", run.Id, "worker", "turn", new()), lab.Ledger);
    }

    static Task<TaskResult> SettleAsync(CoreLab lab, TaskRun run, string status, CancellationToken ct)
    {
        lab.Backend.OnCollect = (_, _) => Task.FromResult(new WorkerReport("succeeded", "Report\n\nStatus: " + status,
            new(), null, true, ["fixture/model#default"], null, true));
        return Settlement.SettleAsync(lab.Backend, lab.Ledger, run, null, ct, lab.Git);
    }

    [Test]
    [Timeout(30_000)]
    public async Task Continued_and_recollected_tasks_write_only_unwritten_calls(CancellationToken ct)
    {
        using var lab = new CoreLab();
        var run = await lab.TaskAsync("inspect", ct);
        run.SaveWorker(new("opencode", run.Id, "worker", "turn", new()), lab.Ledger);
        lab.Ledger.Write("another-task", "usage", new() { ["input"] = 99 });
        var calls = new List<TokenTotals> { new() { Input = 17, CacheRead = 3, Output = 7, Reasoning = 2 } };
        lab.Backend.OnCollect = (_, _) => Task.FromResult(new WorkerReport("succeeded", "Report\n\nStatus: done",
            new() { Input = calls.Sum(x => x.Input), Output = calls.Sum(x => x.Output) }, null, true,
            ["fixture/model#default"], null, true, Calls: calls));
        await Settlement.SettleAsync(lab.Backend, lab.Ledger, run, null, ct, lab.Git);
        calls.Add(new() { Input = 23, CacheRead = 5, Output = 11, Reasoning = 4 });
        var continued = new TaskRun { Record = lab.Ledger.LoadRecord(run.Id) };
        var reopened = new Ledger(lab.Ledger.RunId);
        continued.SaveWorker(new("opencode", run.Id, "worker", "continued-turn", new()), reopened);
        await Settlement.SettleAsync(lab.Backend, reopened, continued, null, ct, lab.Git);
        await Settlement.SettleAsync(lab.Backend, new Ledger(lab.Ledger.RunId), continued, null, ct, lab.Git);

        await Assert.That(reopened.Read().Where(x => x.Type == "usage" && x.Task == run.Id)
            .Select(x => x.Data!.ToJsonString()).ToArray()).IsEquivalentTo(new[]
        {
            """{"input":17,"cacheRead":3,"output":7,"reasoning":2}""",
            """{"input":23,"cacheRead":5,"output":11,"reasoning":4}""",
        }, CollectionOrdering.Matching);
        await Assert.That(reopened.LoadResult(run.Id)!.Tokens!.Input).IsEqualTo(17 + 23);
        await Assert.That(reopened.LoadResult(run.Id)!.Tokens!.Output).IsEqualTo(7 + 11);
    }

    [Test]
    [Arguments(false, "succeeded")]
    [Arguments(true, "succeeded")]
    [Arguments(false, "failed")]
    [Arguments(true, "failed")]
    [Timeout(30_000)]
    public async Task Settlement_records_ordered_usage_without_changing_totals(bool structured, string outcome, CancellationToken ct)
    {
        using var lab = new CoreLab();
        var run = await lab.TaskAsync("inspect", ct);
        if (structured) lab.Structured(run);
        run.SaveWorker(new("opencode", run.Id, "worker", "turn", new()), lab.Ledger);
        lab.Backend.OnCollect = (_, _) => Task.FromResult(new WorkerReport(outcome,
            structured ? StructuredLab.Valid : "Report\n\nStatus: done",
            new() { Input = 901, CacheRead = 21, Output = 73, Reasoning = 11, CacheWrite = 8 }, 1.25, true,
            ["fixture/model#default"], null, true, Calls:
            [new() { Input = 17, CacheRead = 3, Output = 7, Reasoning = 2, CacheWrite = 8 }, new() { Input = 0, Reasoning = 0 }]));

        var before = DateTimeOffset.UtcNow;
        await Settlement.SettleAsync(lab.Backend, lab.Ledger, run, null, ct, lab.Git);
        var after = DateTimeOffset.UtcNow;
        var events = lab.Ledger.Read();
        var usage = events.Where(x => x.Type == "usage").ToArray();
        await Assert.That(usage.Select(x => x.Data!.ToJsonString()).ToArray()).IsEquivalentTo(new[]
        {
            """{"input":17,"cacheRead":3,"output":7,"reasoning":2}""", """{"input":0,"reasoning":0}""",
        }, CollectionOrdering.Matching);
        await Assert.That(usage.Select(x => x.Task).ToArray()).IsEquivalentTo(new string?[] { "inspect", "inspect" }, CollectionOrdering.Matching);
        await Assert.That(usage.Select(x => x.Run).ToArray()).IsEquivalentTo(new[] { lab.Ledger.RunId, lab.Ledger.RunId }, CollectionOrdering.Matching);
        await Assert.That(events.TakeLast(3).Select(x => x.Type).ToArray())
            .IsEquivalentTo(new[] { "usage", "usage", "outcome" }, CollectionOrdering.Matching);
        foreach (var entry in usage)
        {
            await Assert.That(DateTimeOffset.Parse(entry.Ts)).IsGreaterThanOrEqualTo(before.AddSeconds(-1));
            await Assert.That(DateTimeOffset.Parse(entry.Ts)).IsLessThanOrEqualTo(after);
        }
        var saved = lab.Ledger.LoadResult(run.Id)!;
        await Assert.That(saved.Tokens!.Input).IsEqualTo(901);
        await Assert.That(saved.Tokens.CacheRead).IsEqualTo(21);
        await Assert.That(saved.Tokens.Output).IsEqualTo(73);
        await Assert.That(saved.Tokens.Reasoning).IsEqualTo(11);
        await Assert.That(saved.Tokens.CacheWrite).IsEqualTo(8);
        await Assert.That(saved.Cost).IsEqualTo(1.25);
        await Assert.That(saved.TokensComplete).IsTrue();
        await Assert.That(saved.State).IsEqualTo(outcome == "succeeded" ? "done" : "failed");
        await Assert.That(saved.Failures).IsEquivalentTo(outcome == "succeeded" ? Array.Empty<string>() : ["outcome:failed"], CollectionOrdering.Matching);
    }

    [Test]
    [Arguments("verify", "failed", "verify-failed")]
    [Arguments("scope", "failed", "out-of-scope")]
    [Arguments("both", "failed", "out-of-scope,verify-failed")]
    [Arguments("no-final", "failed", "no-final-text")]
    [Arguments("outcome", "failed", "outcome:failed")]
    [Arguments("execution-error", "failed", "settle-error")]
    [Arguments("structured", "failed", "invalid-output")]
    [Arguments("drift", "failed", "route-drift")]
    [Arguments("advisory", "done", "")]
    [Arguments("blocked", "failed", "worker-stopped")]
    [Arguments("stopped", "failed", "worker-stopped")]
    [Arguments("cancelled", "cancelled", "")]
    [Arguments("success", "done", "")]
    [Timeout(30_000)]
    public async Task Settlement_records_every_failed_condition(string condition, string state, string failures, CancellationToken ct)
    {
        using var lab = new CoreLab();
        var run = await lab.TaskAsync("inspect", ct);
        if (condition is "verify" or "both") run.Record.Spec.Verify = ["exit 0", "exit 3"];
        if (condition == "advisory") (run.Record.Spec.Verify, run.Record.Spec.AdvisoryVerify) = (["exit 0"], ["exit 3"]);
        if (condition == "structured") lab.Structured(run);
        if (condition == "cancelled") run.CancelRequested = true;
        run.SaveWorker(new("opencode", run.Id, "worker", "turn", new()), lab.Ledger);
        lab.Backend.OnCollect = (_, _) =>
        {
            if (condition is "scope" or "both") File.WriteAllText(Path.Combine(lab.Tree.Repository, "outside.txt"), "outside scope");
            return Task.FromResult(new WorkerReport(condition == "outcome" ? "failed" : "succeeded",
                condition switch
                {
                    "no-final" => " \n",
                    "blocked" => "Blocked: this session exposes no shell tool.\n\nStatus: blocked",
                    "stopped" => "Stopped: the plan contradicts the scope.\n\n**Status: stopped**",
                    _ => "Report\n\nStatus: done",
                }, new(), null, true,
                condition == "drift" ? ["fixture/model#default", "opencode/flint-40b-free#default"] : ["fixture/model#default"],
                condition == "execution-error" ? "backend execution failed" : null, true));
        };

        var result = await Settlement.SettleAsync(lab.Backend, lab.Ledger, run, null, ct, lab.Git);

        await Assert.That(result.State).IsEqualTo(state);
        await Assert.That(result.Failures.Order().ToArray()).IsEquivalentTo(failures.Split(',', StringSplitOptions.RemoveEmptyEntries));
        var saved = lab.Ledger.LoadResult(run.Id)!;
        await Assert.That(saved.State).IsEqualTo(state);
        await Assert.That(saved.Failures).IsEquivalentTo(result.Failures);
        await Assert.That(saved.RouteVerified).IsEqualTo(condition != "drift");
        await Assert.That(saved.WorkerStatus).IsEqualTo(condition switch
        { "no-final" => null, "blocked" or "stopped" => condition, "structured" => null, _ => "done" });
        if (condition == "advisory") await Assert.That(saved.Warnings!).IsEquivalentTo(new[] { "advisory-verify-failed" });
        else await Assert.That(saved.Warnings).IsNull();
        if (condition == "drift") await Assert.That(saved.RouteDrift!).IsEquivalentTo(new[] { "opencode/flint-40b-free#default" });
        else await Assert.That(saved.RouteDrift).IsNull();
        if (condition is "verify" or "both" or "advisory")
            await Assert.That(result.Verify.Select(x => x.ExitCode).ToArray()).IsEquivalentTo(new[] { 0, 3 });
        if (condition == "advisory")
            await Assert.That(result.Verify.Select(x => x.Advisory).ToArray()).IsEquivalentTo(new bool?[] { null, true });
    }
}
