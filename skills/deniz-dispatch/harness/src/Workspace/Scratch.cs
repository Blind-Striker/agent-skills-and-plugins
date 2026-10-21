static class Scratch
{
    const string Prefix = "dispatch-scratch-";

    public static bool Required(TaskSpec spec) => spec.Kind == "structured" && spec.Project != "on";

    public static string Candidate(params string[] candidates)
    {
        var rejected = new List<string>();
        foreach (var candidate in candidates)
        {
            var path = Path.GetFullPath(candidate);
            string? marker = null;
            for (var ancestor = new DirectoryInfo(path); ancestor is not null; ancestor = ancestor.Parent)
            {
                var entry = Path.Combine(ancestor.FullName, ".git");
                if (!Path.Exists(entry)) continue;
                marker = entry;
                break;
            }
            if (marker is null) return path;
            rejected.Add($"{path}: ancestor holds {marker}");
        }
        throw new HarnessException("structured scratch needs a projectless folder; " + string.Join("; ", rejected));
    }

    public static string Candidate() => Candidate(Path.GetTempPath(), Locations.StateRoot);

    public static void Prepare(TaskRun run, Ledger ledger)
    {
        if (run.Record.Workspace.Path is not null) return;
        var parent = Candidate();
        Directory.CreateDirectory(parent);
        var path = Path.Combine(parent, Prefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        run.Record.Workspace.Path = path;
        ledger.SaveRecord(run.Record);
    }

    public static void Delete(TaskRecord record)
    {
        if (!Required(record.Spec) || record.Workspace.Path is not { } path || !Directory.Exists(path)) return;
        // Never remove a repository or an unrelated folder from a recovered record.
        if (record.Workspace.Branch is not null || !Path.GetFileName(path).StartsWith(Prefix, StringComparison.Ordinal))
            throw new HarnessException("refused to delete an unrecognized structured scratch folder");
        foreach (var file in Directory.EnumerateFiles(path, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint }))
            File.SetAttributes(file, File.GetAttributes(file) & ~FileAttributes.ReadOnly);
        Directory.Delete(path, recursive: true);
    }
}
