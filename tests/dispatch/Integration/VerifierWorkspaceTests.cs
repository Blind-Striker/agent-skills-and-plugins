using Dispatch.Tests.Support;

namespace Dispatch.Tests.Integration;

[Category("Integration")]
[NotInParallel]
public sealed class VerifierWorkspaceTests
{
    [Test]
    [Timeout(30_000)]
    public async Task Verify_RunsOnCommittedTreeInWorktree(CancellationToken ct)
    {
        using var lab = new CoreLab();
        var run = await lab.TaskAsync("inspect", ct);
        var workspace = Path.Combine(lab.Tree.WorktreeRoot, "inspect");
        await lab.Git.CheckAsync(lab.Tree.Repository, ct, "worktree", "add", "-b", "verify-worktree", workspace, run.Record.Workspace.BaseSha!);
        run.Record.Workspace.Path = workspace;
        await File.WriteAllTextAsync(Path.Combine(workspace, "change.txt"), "committed-change\n", ct);
        run.Record.Spec.Verify = [OperatingSystem.IsWindows()
            ? "(Get-Location).Path; git rev-parse HEAD; git show HEAD:change.txt"
            : "pwd; git rev-parse HEAD; git show HEAD:change.txt"];
        run.SaveWorker(new("opencode", run.Id, "worker", "turn", new()), lab.Ledger);

        var result = await Settlement.SettleAsync(lab.Backend, lab.Ledger, run, null, ct, lab.Git);

        await Assert.That(result.Commit).IsNotNull();
        await Assert.That(result.Verify.Single().ExitCode).IsEqualTo(0);
        var log = File.ReadAllText(Path.Combine(lab.Ledger.RunId, run.Id, "verify.log")).Replace("\r\n", "\n");
        await Assert.That(log).Contains($"[stdout]\n{workspace}\n{result.Commit}\ncommitted-change\n");
        await Assert.That(await lab.Git.CheckAsync(workspace, ct, "status", "--porcelain")).IsEqualTo("");
    }
}
