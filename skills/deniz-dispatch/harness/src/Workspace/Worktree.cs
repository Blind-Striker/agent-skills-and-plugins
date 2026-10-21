static class Worktree
{
    public static async Task PrepareAsync(TaskRun run, Ledger ledger, string repo, CancellationToken ct, Git? runner = null)
    {
        var record = run.Record;
        if (record.Workspace.Path is not null) return;
        var baseRef = record.Spec.ReviewOf is { } author ? ledger.LoadRecord(author).Workspace.Branch! : record.Spec.Base!;
        var git = runner ?? Git.ForTask(run.Id);
        var baseSha = await git.IdAsync(repo, ct, "rev-parse", "--verify", $"{baseRef}^{{commit}}");
        var path = Locations.Worktree(run.Id);
        var branch = $"dispatch/{ledger.RunId}/{run.Id}";
        await git.CheckAsync(repo, ct, "worktree", "add", "-b", branch, path, baseSha);
        record.Workspace.Path = path;
        record.Workspace.Branch = branch;
        record.Workspace.BaseRef = baseRef;
        record.Workspace.BaseSha = baseSha;
        ledger.SaveRecord(record);
        ledger.Write(run.Id, "worktree", new JsonObject { ["path"] = path, ["branch"] = branch, ["base"] = baseRef, ["baseSha"] = baseSha });
    }
}
