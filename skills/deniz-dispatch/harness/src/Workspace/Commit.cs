static class Commit
{
    public static async Task CollectAsync(Ledger ledger, TaskRun run, TaskResult result, CancellationToken ct, Git? runner = null)
    {
        var record = run.Record;
        var wt = record.Workspace.Path!;
        var git = runner ?? Git.ForTask(run.Id);
        if ((await git.CheckAsync(wt, ct, "status", "--porcelain")).Trim().Length > 0)
        {
            await git.CheckAsync(wt, CancellationToken.None, "add", "-A");
            var commit = await git.RunAsync(wt, CancellationToken.None, "commit", "-q", "--no-verify", "-m", $"dispatch: {ledger.RunId}/{run.Id}");
            if (commit.ExitCode != 0) result.Error = $"harness commit failed: {commit.Stderr.Trim()}";
        }
        var head = await git.IdAsync(wt, ct, "rev-parse", "HEAD");
        result.Commit = head == record.Workspace.BaseSha ? null : head;
        result.DiffStat = (await git.CheckAsync(wt, ct, "diff", "--ignore-submodules=none", "--no-ext-diff", "--no-textconv", "--shortstat", record.Workspace.BaseSha!, "HEAD")).Trim();
        result.FilesChanged = (await git.CheckAsync(wt, ct, "diff", "--ignore-submodules=none", "--no-ext-diff", "--no-textconv", "--name-only", "-z", "--no-renames", record.Workspace.BaseSha!, "HEAD"))
            .Split('\0', StringSplitOptions.RemoveEmptyEntries).ToList();
        result.OutOfScope = record.Spec.Kind is "review" or "research" ? [.. result.FilesChanged]
            : result.FilesChanged.Where(x => record.Settings.Scope is { } scope ? !ConfigResolver.InScope(x, scope) : !Scope.InScope(x, record.Spec.Scope)).ToList();
        var patch = await git.CheckBytesAsync(wt, ct, "diff", "--ignore-submodules=none", "--submodule=short", "--binary", "--full-index", "--no-color", "--no-ext-diff", "--no-textconv", "--src-prefix=a/", "--dst-prefix=b/", record.Workspace.BaseSha!, "HEAD");
        await ledger.WriteArtifactAsync(run.Id, "diff.patch", patch, ct);
        ledger.Write(run.Id, "collected", new JsonObject
        {
            ["outcome"] = result.Outcome, ["commit"] = result.Commit, ["diffstat"] = result.DiffStat,
            ["files"] = result.FilesChanged.Count, ["outOfScope"] = Json.Array(result.OutOfScope), ["routesUsed"] = Json.Array(result.RoutesUsed),
        });
    }
}
