static class Worktree
{
    public static bool InPlace(TaskRecord record) => (record.Settings.Workspace?.Value ?? record.Spec.Workspace) == "in-place";
    public static string CommitMode(TaskRecord record) => InPlace(record) ? record.Settings.Commit?.Value ?? record.Spec.Commit ?? "none" : "harness";

    public static async Task<(int Length, string Detail)> PathDepthAsync(Git git, string repo, string folder, CancellationToken ct)
    {
        var (length, file) = await LongestPathAsync(git, repo, folder, ct);
        return (length, $"worktree paths reach {length} characters ({file}); builds can fail past 260 on Windows. Set locations.worktreeRoot to a short folder (for example C:\\wt) in the user config");
    }

    static async Task<(int Length, string File)> LongestPathAsync(Git git, string repo, string folder, CancellationToken ct)
    {
        var index = Path.Combine(Path.GetTempPath(), $"path-depth-{Guid.NewGuid():N}.index");
        var isolated = git.WithIndex(index);
        try
        {
            await isolated.CheckAsync(repo, ct, "read-tree", "HEAD");
            var paths = await isolated.CheckAsync(repo, ct, "ls-files", "-z");
            var file = paths.Split('\0', StringSplitOptions.RemoveEmptyEntries).MaxBy(path => path.Length)?.Replace('/', '\\') ?? "";
            return (Path.TrimEndingDirectorySeparator(folder).Length + 1 + file.Length, file);
        }
        finally { File.Delete(index); }
    }

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
        Directory.CreateDirectory(Locations.TaskTemp(run.Id, ledger.RunId, record.Settings.TempRoot?.Value));
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
        var step = "workspace";
        try
        {
            Scratch.Delete(record, ledger);
            var git = runner ?? Runner(ledger, task);
            if (!InPlace(record) && record.Workspace is { Path: { } path, Branch: { } branch })
            {
                async Task<string?> Registration()
                {
                    // Listing is read-only; a repeat clean can have already removed the baseline.
                    var reader = Git.ForTask(task, ledger.RunId, git.ControlFolder);
                    var entries = (await reader.CheckAsync(repo, ct, "worktree", "list", "--porcelain", "-z")).Split("\0\0", StringSplitOptions.RemoveEmptyEntries)
                        .Select(x => x.Split('\0')).ToList();
                    // Git records real paths, so a root reached through a link matches by branch instead;
                    // a detached or rebasing worktree has no branch line and matches by path.
                    var entry = entries.FirstOrDefault(x => x.Any(v => v.StartsWith("worktree ", StringComparison.Ordinal) && Locations.Same(v[9..], path)))
                        ?? entries.FirstOrDefault(x => x.Contains("branch refs/heads/" + branch));
                    return entry is null ? null : string.Join('\0', entry);
                }
                var registration = await Registration();
                var exists = Directory.Exists(path);
                var link = Path.Combine(path, ".git");
                var intact = exists && File.Exists(link) && File.ReadAllText(link) is { } text && text.StartsWith("gitdir: ", StringComparison.Ordinal)
                    && Directory.Exists(Path.GetFullPath(text[8..].Trim(), path));
                if (Directory.Exists(link)) throw new HarnessException($"refused an unrecognized worktree: {path}");
                if (exists && !force && !intact)
                {
                    Console.WriteLine($"{task}: worktree partially removed ({path}); clean --force finishes the removal");
                    return false;
                }
                if (exists && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                    throw new HarnessException($"refused an unrecognized worktree: {path}");
                var branchExists = (await git.RunAsync(repo, ct, "show-ref", "--verify", "--quiet", "refs/heads/" + branch)).ExitCode == 0;
                if ((exists || registration is not null || branchExists) && Directory.Exists(Locations.ControlDir(ledger.RunId)))
                    await new GitConfigGate(git).CheckAsync(repo, ct);
                var dirty = !force && intact && await DirtyAsync(git, path, ct);
                var merged = force || !branchExists || (record.Workspace.BaseRef is { } baseRef &&
                    (await git.RunAsync(repo, ct, "merge-base", "--is-ancestor", branch, baseRef)).ExitCode == 0);
                if (!force && (dirty || !merged))
                {
                    Console.WriteLine($"{task}: refused (dirty or unmerged worktree); --force removes it anyway");
                    return false;
                }
                if (registration?.Split('\0').Any(x => x == "locked" || x.StartsWith("locked ", StringComparison.Ordinal)) == true)
                    throw new HarnessException($"worktree is locked: {path}");
                if (force && !intact) DeleteTemp(path);
                if (registration is not null || intact)
                    await git.CheckAsync(repo, ct, force || !exists ? ["worktree", "remove", "--force", path] : ["worktree", "remove", path]);
                if (Path.Exists(path) || await Registration() is not null) throw new HarnessException($"worktree removal did not remove {path}");
                step = "branch";
                if (branchExists) await git.CheckAsync(repo, ct, "branch", force ? "-D" : "-d", branch);
                if ((await git.RunAsync(repo, ct, "show-ref", "--verify", "--quiet", "refs/heads/" + branch)).ExitCode == 0)
                    throw new HarnessException($"branch removal did not remove {branch}");
            }
            step = "temp";
            foreach (var temp in Locations.CleanupTemps(ledger, record)) DeleteTemp(temp);
            step = "index";
            Snapshot.Delete(git, task);
            if (Path.Exists(Snapshot.Index(git, task))) throw new HarnessException("temporary index remains");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or HarnessException)
        {
            ex.Data["step"] = step;
            throw;
        }
    }

    static void DeleteTemp(string path)
    {
        if (!Directory.Exists(path)) return;
        foreach (var file in Directory.EnumerateFiles(path, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint }))
            File.SetAttributes(file, File.GetAttributes(file) & ~FileAttributes.ReadOnly);
        Directory.Delete(path, recursive: true);
        if (Path.Exists(path)) throw new HarnessException($"removal did not remove {path}");
    }
}

sealed class WorkspaceDirtyException() : HarnessException("workspace-dirty: in-place commit: harness requires a clean working tree");
