static class Scratch
{
    const string Prefix = "dispatch-scratch-";
    const string OwnerFile = ".dispatch-owner.json";

    public static bool Required(TaskSpec spec) => spec.Kind == "structured" && spec.Project != "on";

    public static string Candidate(params string[] candidates) => Select(candidates).Resolved;

    static (string Given, string Resolved) Select(params string[] candidates)
    {
        var rejected = new List<string>();
        foreach (var candidate in candidates)
        {
            try { var path = Resolve(candidate, 0); CheckPath(path); return (candidate, path); }
            catch (HarnessException error) { rejected.Add(error.Message); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            { rejected.Add($"structured scratch cannot resolve {candidate}: {error.Message}"); }
        }
        throw new HarnessException("structured scratch needs a projectless folder; " + string.Join("; ", rejected));
    }

    public static string Candidate() => Candidate(Roots());

    static string[] Roots() => [Path.GetTempPath(), Locations.StateRoot];

    // Only a candidate root is resolved: it comes from the operator's environment, where macOS links
    // /var and /tmp into /private. Allocation, records and deletion then use the resolved path, so a
    // link at or below it, or a recorded path through a link, is still refused by CheckPath.
    static string Resolve(string path, int depth)
    {
        if (depth > 32) throw new HarnessException($"structured scratch refuses a link cycle: {path}");
        var full = Path.GetFullPath(path);
        var current = Path.GetPathRoot(full)!;
        foreach (var segment in full[current.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            var next = Path.Combine(current, segment);
            var target = Directory.Exists(next) ? new DirectoryInfo(next).ResolveLinkTarget(returnFinalTarget: true) : null;
            current = target is null ? next : Resolve(target.FullName, depth + 1);
        }
        return current;
    }

    static void CheckPath(string path)
    {
        // Refuse links before reading through them, including links in any ancestor.
        for (var ancestor = new DirectoryInfo(Path.GetFullPath(path)); ancestor is not null; ancestor = ancestor.Parent)
        {
            if (Path.Exists(ancestor.FullName) && (File.GetAttributes(ancestor.FullName) & FileAttributes.ReparsePoint) != 0)
                throw new HarnessException($"structured scratch refuses a reparse point: {ancestor.FullName}");
            var entry = Path.Combine(ancestor.FullName, ".git");
            if (Path.Exists(entry)) throw new HarnessException($"structured scratch refuses a repository: {entry}");
        }
    }

    public static void Prepare(TaskRun run, Ledger ledger)
    {
        if (run.Record.Workspace.Path is { } recorded)
        {
            CheckOwner(run.Record, ledger, recorded);
            return;
        }
        var (given, parent) = Select(Roots());
        Directory.CreateDirectory(parent);
        var allocationId = Guid.NewGuid().ToString("N");
        var path = Path.Combine(parent, Prefix + allocationId);
        Directory.CreateDirectory(path);
        Json.WriteBytes(Path.Combine(path, OwnerFile), Encoding.UTF8.GetBytes(new JsonObject
        {
            ["run"] = ledger.RunId, ["task"] = run.Id, ["allocationId"] = allocationId,
        }.ToJsonString()));
        run.Record.Workspace.Path = path;
        run.Record.Workspace.ScratchAllocationId = allocationId;
        ledger.SaveRecord(run.Record);
        ledger.Write(run.Id, "scratch", new JsonObject { ["path"] = path, ["root"] = given, ["resolvedRoot"] = parent });
    }

    static void CheckOwner(TaskRecord record, Ledger ledger, string path)
    {
        CheckPath(path);
        if (record.Workspace.Branch is not null || !Path.GetFileName(path).StartsWith(Prefix, StringComparison.Ordinal))
            throw new HarnessException("refused an unrecognized structured scratch folder");
        var marker = Path.Combine(path, OwnerFile);
        if (!File.Exists(marker) || (File.GetAttributes(marker) & FileAttributes.ReparsePoint) != 0)
            throw new HarnessException("structured scratch ownership marker is missing or unsafe");
        JsonNode? owner;
        try { owner = JsonNode.Parse(File.ReadAllText(marker)); }
        catch (JsonException) { throw new HarnessException("structured scratch ownership marker is invalid"); }
        if (owner is not JsonObject document || document["run"]?.ToString() != ledger.RunId || document["task"]?.ToString() != record.Spec.Id
            || string.IsNullOrEmpty(record.Workspace.ScratchAllocationId) || document["allocationId"]?.ToString() != record.Workspace.ScratchAllocationId)
            throw new HarnessException("structured scratch ownership does not match the run, task and allocation");
    }

    public static void Delete(TaskRecord record, Ledger ledger)
    {
        if (!Required(record.Spec) || record.Workspace.Path is not { } path || !Directory.Exists(path)) return;
        CheckOwner(record, ledger, path);
        foreach (var file in Directory.EnumerateFiles(path, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint }))
            File.SetAttributes(file, File.GetAttributes(file) & ~FileAttributes.ReadOnly);
        Directory.Delete(path, recursive: true);
    }
}
