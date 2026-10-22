static class Commit
{
    public static async Task CollectAsync(Ledger ledger, TaskRun run, TaskResult result, CancellationToken ct, Git? runner = null)
    {
        var record = run.Record;
        var wt = record.Workspace.Path!;
        var git = (runner ?? Worktree.Runner(ledger, run.Id)).ForWorkspace(ledger, run.Id, (type, data) =>
        {
            if (type == "git-config-changed" && data["blocking"]?.GetValue<bool>() == false)
            {
                result.Warnings ??= [];
                if (!result.Warnings.Contains("git-config-changed")) result.Warnings.Add("git-config-changed");
            }
        });
        if (Worktree.InPlace(record)) wt = (await git.CheckAsync(wt, ct, "rev-parse", "--show-toplevel")).Trim();
        result.CommitMode = Worktree.CommitMode(record);
        if (result.CommitMode == "none")
        {
            var start = ledger.Read().Last(x => x.Task == run.Id && x.Type == "worktree").Data!["startTree"]!.GetValue<string>();
            var end = await Snapshot.CaptureAsync(git, wt, run.Id, start: false, ct);
            Snapshot.Delete(git, run.Id);
            await CollectDiffAsync(ledger, run, result, git, wt, start, end, ct);
            return;
        }
        if (await Worktree.DirtyAsync(git, wt, ct))
        {
            await git.CheckAsync(wt, CancellationToken.None, "reset", "-q", "HEAD", "--", ":/.dispatch");
            await git.CheckAsync(wt, CancellationToken.None, "add", "-A", "--", ":/", ":(top,exclude).dispatch");
            var staged = await git.RunAsync(wt, ct, "diff", "--cached", "--quiet", "--ignore-submodules=none", "--no-ext-diff", "--no-textconv");
            if (staged.ExitCode == 1)
            {
                var commit = await git.RunAsync(wt, CancellationToken.None, "commit", "-q", "--no-verify", "-m", $"dispatch: {ledger.RunId}/{run.Id}");
                if (commit.ExitCode != 0) result.Error = $"harness commit failed: {commit.Stderr.Trim()}";
            }
            else if (staged.ExitCode != 0) throw new HarnessException($"git staged diff failed: {staged.Stderr.Trim()}");
        }
        var head = await git.IdAsync(wt, ct, "rev-parse", "HEAD");
        result.Commit = head == record.Workspace.BaseSha ? null : head;
        await CollectDiffAsync(ledger, run, result, git, wt, record.Workspace.BaseSha!, "HEAD", ct);
        if (Worktree.InPlace(record)) Snapshot.Delete(git, run.Id);
    }

    static async Task CollectDiffAsync(Ledger ledger, TaskRun run, TaskResult result, Git git, string wt, string start, string end, CancellationToken ct)
    {
        var record = run.Record;
        result.DiffStat = (await git.CheckAsync(wt, ct, "diff", "--ignore-submodules=none", "--no-ext-diff", "--no-textconv", "--shortstat", start, end)).Trim();
        result.FilesChanged = (await git.CheckAsync(wt, ct, "diff", "--ignore-submodules=none", "--no-ext-diff", "--no-textconv", "--name-only", "-z", "--no-renames", start, end))
            .Split('\0', StringSplitOptions.RemoveEmptyEntries).ToList();
        var ignoreCase = await Scope.IgnoreCaseAsync(git, wt, ct);
        result.OutOfScope = record.Spec.Kind is "review" or "research" ? [.. result.FilesChanged]
            : result.FilesChanged.Where(x => IsDispatch(x) || (record.Settings.Scope is { } scope ? !Scope.InScope(x, scope, ignoreCase) : !Scope.InScope(x, record.Spec.Scope, ignoreCase))).ToList();
        if (result.CommitMode != "none")
        {
            var leftover = await git.CheckAsync(wt, ct, "status", "--porcelain=v1", "-z", "--untracked-files=all", "--no-renames");
            result.OutOfScope = result.OutOfScope.Concat(leftover.Split('\0', StringSplitOptions.RemoveEmptyEntries).Select(x => x[3..])).Distinct(StringComparer.Ordinal).ToList();
        }
        var patch = await git.CheckBytesAsync(wt, ct, "diff", "--ignore-submodules=none", "--submodule=short", "--binary", "--full-index", "--no-color", "--no-ext-diff", "--no-textconv", "--src-prefix=a/", "--dst-prefix=b/", start, end);
        await ledger.WriteArtifactAsync(run.Id, "diff.patch", patch, ct);
        var collected = new JsonObject
        {
            ["outcome"] = result.Outcome, ["commit"] = result.Commit, ["diffstat"] = result.DiffStat,
            ["files"] = result.FilesChanged.Count, ["outOfScope"] = Json.Array(result.OutOfScope), ["routesUsed"] = Json.Array(result.RoutesUsed),
        };
        if (result.CommitMode == "none") collected["endTree"] = end;
        ledger.Write(run.Id, "collected", collected);
    }

    static bool IsDispatch(string path) => path == ".dispatch" || path.StartsWith(".dispatch/", StringComparison.Ordinal);
}
