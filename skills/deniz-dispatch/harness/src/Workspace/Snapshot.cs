static class Snapshot
{
    public static string Index(Git git, string task) => Path.Combine(git.ControlFolder, "index-" + task);

    public static async Task<string> CaptureAsync(Git git, string repo, string task, bool start, CancellationToken ct)
    {
        var index = Index(git, task);
        Directory.CreateDirectory(git.ControlFolder);
        var isolated = git.WithIndex(index);
        if (start || !File.Exists(index))
        {
            var source = (await git.CheckAsync(repo, ct, "rev-parse", "--git-path", "index")).Trim();
            source = Path.GetFullPath(source, repo);
            if (File.Exists(source)) File.Copy(source, index, overwrite: true);
            else await isolated.CheckAsync(repo, ct, "read-tree", "--empty");
        }
        await isolated.CheckAsync(repo, ct, "add", "-A", "--", ":/");
        return await isolated.IdAsync(repo, ct, "write-tree");
    }

    public static void Delete(Git git, string task)
    {
        File.Delete(Index(git, task));
    }
}
