static class Commit
{
    public static async Task CollectAsync(Ledger ledger, TaskRun run, TaskResult result, CancellationToken ct, Git? runner = null)
    {
        var record = run.Record;
        var wt = record.Workspace.Path!;
        File.Delete(Path.Combine(ledger.TaskDir(run.Id), "wip.patch"));
        result.WipPatch = null;
        result.WipFileCount = null;
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
        var stopped = result.WorkerStatus is "stopped" or "blocked";
        if (result.CommitMode == "none" || stopped)
        {
            var start = Worktree.InPlace(record)
                ? ledger.Read().Last(x => x.Task == run.Id && x.Type == "worktree").Data!["startTree"]!.GetValue<string>()
                : record.Workspace.BaseSha!;
            var end = await Snapshot.CaptureAsync(git, wt, run.Id, start: false, ct);
            Snapshot.Delete(git, run.Id);
            if (result.CommitMode != "none")
            {
                var stoppedHead = await git.IdAsync(wt, ct, "rev-parse", "HEAD");
                result.Commit = stoppedHead == record.Workspace.BaseSha ? null : stoppedHead;
            }
            await CollectDiffAsync(ledger, run, result, git, wt, start, end, ct, stopped);
            return;
        }
        if (await Worktree.DirtyAsync(git, wt, ct))
        {
            await git.CheckAsync(wt, CancellationToken.None, "reset", "-q", "HEAD", "--", ":/.dispatch");
            await git.CheckAsync(wt, CancellationToken.None, "add", "-A", "--", ":/", ":(top,exclude).dispatch");
            var staged = await git.RunAsync(wt, ct, "diff", "--cached", "--quiet", "--ignore-submodules=none", "--no-ext-diff", "--no-textconv");
            if (staged.ExitCode == 1)
            {
                var commit = await git.RunAsync(wt, CancellationToken.None, "commit", "-q", "--no-verify", "-m", $"chore(dispatch): {ledger.RunId}/{run.Id}");
                if (commit.ExitCode != 0) result.Error = $"harness commit failed: {commit.Stderr.Trim()}";
            }
            else if (staged.ExitCode != 0) throw new HarnessException($"git staged diff failed: {staged.Stderr.Trim()}");
        }
        var head = await git.IdAsync(wt, ct, "rev-parse", "HEAD");
        result.Commit = head == record.Workspace.BaseSha ? null : head;
        await CollectDiffAsync(ledger, run, result, git, wt, record.Workspace.BaseSha!, "HEAD", ct);
        if (Worktree.InPlace(record)) Snapshot.Delete(git, run.Id);
    }

    static async Task CollectDiffAsync(Ledger ledger, TaskRun run, TaskResult result, Git git, string wt, string start, string end, CancellationToken ct, bool stopped = false)
    {
        var record = run.Record;
        result.DiffStat = (await git.CheckAsync(wt, ct, "diff", "--ignore-submodules=none", "--no-ext-diff", "--no-textconv", "--shortstat", start, end)).Trim();
        result.FilesChanged = (await git.CheckAsync(wt, ct, "diff", "--ignore-submodules=none", "--no-ext-diff", "--no-textconv", "--name-only", "-z", "--no-renames", start, end))
            .Split('\0', StringSplitOptions.RemoveEmptyEntries).ToList();
        var ignoreCase = await Scope.IgnoreCaseAsync(git, wt, ct);
        result.OutOfScope = record.Spec.Kind is "review" or "research" ? [.. result.FilesChanged]
            : result.FilesChanged.Where(x => IsDispatch(x) || (record.Settings.Scope is { } scope ? !Scope.InScope(x, scope, ignoreCase) : !Scope.InScope(x, record.Spec.Scope, ignoreCase))).ToList();
        if (result.CommitMode != "none" && !stopped)
        {
            var leftover = await git.CheckAsync(wt, ct, "status", "--porcelain=v1", "-z", "--untracked-files=all", "--no-renames");
            result.OutOfScope = result.OutOfScope.Concat(leftover.Split('\0', StringSplitOptions.RemoveEmptyEntries).Select(x => x[3..])).Distinct(StringComparer.Ordinal).ToList();
        }
        var patch = await PatchAsync(git, wt, start, end, ct);
        if (stopped && patch.Length > 0)
        {
            await ledger.WriteArtifactAsync(run.Id, "wip.patch", patch, ct);
            result.WipPatch = "wip.patch";
            result.WipFileCount = result.FilesChanged.Count;
        }
        var diff = stopped && result.CommitMode != "none"
            ? await PatchAsync(git, wt, record.Workspace.BaseSha!, "HEAD", ct) : patch;
        await ledger.WriteArtifactAsync(run.Id, "diff.patch", diff, ct);
        var collected = new JsonObject
        {
            ["outcome"] = result.Outcome, ["commit"] = result.Commit, ["diffstat"] = result.DiffStat,
            ["files"] = result.FilesChanged.Count, ["outOfScope"] = Json.Array(result.OutOfScope), ["routesUsed"] = Json.Array(result.RoutesUsed),
        };
        if (result.CommitMode == "none") collected["endTree"] = end;
        ledger.Write(run.Id, "collected", collected);
    }

    static Task<byte[]> PatchAsync(Git git, string wt, string start, string end, CancellationToken ct) =>
        git.CheckBytesAsync(wt, ct, "diff", "--ignore-submodules=none", "--submodule=short", "--binary", "--full-index", "--no-color", "--no-ext-diff", "--no-textconv", "--src-prefix=a/", "--dst-prefix=b/", start, end);

    static bool IsDispatch(string path) => path == ".dispatch" || path.StartsWith(".dispatch/", StringComparison.Ordinal);
}
