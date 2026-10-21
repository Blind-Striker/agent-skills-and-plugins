static class Worktree
{
    public static bool InPlace(TaskRecord record) => (record.Settings.Workspace?.Value ?? record.Spec.Workspace) == "in-place";
    public static string CommitMode(TaskRecord record) => InPlace(record) ? record.Settings.Commit?.Value ?? record.Spec.Commit ?? "none" : "harness";

    public static bool IsInside(string path, string parent)
    {
        var relative = Path.GetRelativePath(Path.GetFullPath(parent), Path.GetFullPath(path));
        return relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) && !Path.IsPathRooted(relative);
    }

    public static Git Runner(Ledger ledger, string task)
    {
        var info = File.Exists(Path.Combine(ledger.Dir, "run.json")) ? ledger.LoadRunInfo() : null;
        return Git.ForTask(task, ledger.RunId, Path.Combine(Locations.StateRoot, "control", info?.ControlId ?? ledger.RunId)).ForWorkspace(ledger, task);
    }

    public static async Task PrepareAsync(TaskRun run, Ledger ledger, string repo, CancellationToken ct, Git? runner = null)
    {
        var record = run.Record;
        if (record.Workspace.Path is not null) return;
        var git = (runner ?? Runner(ledger, run.Id)).ForWorkspace(ledger, run.Id);
        await git.InitializeBaselineAsync(repo, ct);
        try { await new GitConfigGate(git).CheckAsync(repo, ct); }
        catch (GitConfigChangedException)
        {
            ledger.SaveResult(new TaskResult { Run = ledger.RunId, Task = run.Id, Kind = record.Spec.Kind,
                State = "failed", CommitMode = CommitMode(record), Failures = ["git-config-changed"] });
            throw;
        }
        repo = Path.GetFullPath((await git.CheckAsync(repo, ct, "rev-parse", "--show-toplevel")).Trim());
        Directory.CreateDirectory(Locations.TaskTemp(run.Id, ledger.RunId));
        if (InPlace(record))
        {
            var folder = await FolderAsync(git, repo, record.Settings.Dir?.Value ?? record.Spec.Dir, ct);
            var commits = CommitMode(record) == "harness";
            if (commits && await DirtyAsync(git, repo, ct)) throw new WorkspaceDirtyException();
            var hasHead = (await git.RunAsync(repo, ct, "rev-parse", "--verify", "HEAD^{commit}")).ExitCode == 0;
            if (commits && !hasHead) throw new HarnessException("in-place commit: harness requires a HEAD commit");
            var start = await Snapshot.CaptureAsync(git, repo, run.Id, start: true, ct);
            record.Workspace.Path = folder;
            record.Workspace.BaseSha = hasHead ? await git.IdAsync(repo, ct, "rev-parse", "HEAD") : null;
            ledger.SaveRecord(record);
            ledger.Write(run.Id, "worktree", new JsonObject { ["path"] = folder, ["startTree"] = start });
            return;
        }
        var baseRef = record.Spec.ReviewOf is { } author ? ledger.LoadRecord(author).Workspace.Branch! : record.Spec.Base!;
        var baseSha = await git.IdAsync(repo, ct, "rev-parse", "--verify", $"{baseRef}^{{commit}}");
        var path = Locations.Worktree(run.Id, ledger.RunId);
        var branch = $"dispatch/{ledger.RunId}/{run.Id}";
        await git.CheckAsync(repo, ct, "worktree", "add", "-b", branch, path, baseSha);
        record.Workspace.Path = path;
        record.Workspace.Branch = branch;
        record.Workspace.BaseRef = baseRef;
        record.Workspace.BaseSha = baseSha;
        ledger.SaveRecord(record);
        ledger.Write(run.Id, "worktree", new JsonObject { ["path"] = path, ["branch"] = branch, ["base"] = baseRef, ["baseSha"] = baseSha });
    }

    public static async Task<bool> DirtyAsync(Git git, string repo, CancellationToken ct) =>
        (await git.CheckBytesAsync(repo, ct, "status", "--porcelain=v1", "-z", "--untracked-files=all")).Length > 0;

    public static async Task<string> FolderAsync(Git git, string repo, string? dir, CancellationToken ct)
    {
        var folder = Path.GetFullPath(dir ?? ".", repo);
        if (dir is not null && Path.IsPathRooted(dir) || !IsInside(folder, repo))
            throw new HarnessException("in-place dir must be relative to and inside the repository");
        if (!Directory.Exists(folder)) throw new HarnessException("in-place dir does not exist");
        var top = await git.CheckAsync(folder, ct, "rev-parse", "--show-toplevel");
        if (!Locations.Same(top.Trim(), repo)) throw new HarnessException("in-place dir belongs to another working tree");
        return folder;
    }

    public static async Task<bool> CleanAsync(Ledger ledger, TaskRecord record, string repo, bool force, CancellationToken ct, Git? runner = null)
    {
        var task = record.Spec.Id!;
        var git = runner ?? Runner(ledger, task);
        if (!InPlace(record) && record.Workspace is { Path: { } path, Branch: { } branch })
        {
            var exists = Directory.Exists(path);
            var branchExists = (await git.RunAsync(repo, ct, "show-ref", "--verify", "--quiet", "refs/heads/" + branch)).ExitCode == 0;
            var dirty = exists && await DirtyAsync(git, path, ct);
            var merged = !branchExists || (record.Workspace.BaseRef is { } baseRef &&
                (await git.RunAsync(repo, ct, "merge-base", "--is-ancestor", branch, baseRef)).ExitCode == 0);
            if (!force && (dirty || !merged))
            {
                Console.WriteLine($"{task}: refused (dirty or unmerged worktree); --force removes it anyway");
                return false;
            }
            if (exists && (await git.RunAsync(repo, ct, force
                ? ["worktree", "remove", "--force", path] : ["worktree", "remove", path])).ExitCode != 0) return false;
            if (branchExists && (await git.RunAsync(repo, ct, "branch", force ? "-D" : "-d", branch)).ExitCode != 0) return false;
        }
        DeleteTemp(Locations.TaskTemp(task, ledger.RunId));
        Snapshot.Delete(git, task);
        ledger.Write(task, "cleaned", new JsonObject { ["worktree"] = record.Workspace.Path, ["branch"] = record.Workspace.Branch, ["force"] = force });
        return true;
    }

    static void DeleteTemp(string path)
    {
        if (!Directory.Exists(path)) return;
        foreach (var file in Directory.EnumerateFiles(path, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint }))
            File.SetAttributes(file, File.GetAttributes(file) & ~FileAttributes.ReadOnly);
        Directory.Delete(path, recursive: true);
    }
}

sealed class WorkspaceDirtyException() : HarnessException("workspace-dirty: in-place commit: harness requires a clean working tree");
