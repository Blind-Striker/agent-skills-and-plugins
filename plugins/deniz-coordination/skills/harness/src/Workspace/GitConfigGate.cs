sealed class GitConfigGate(Git git)
{
    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim> baselineLocks = new(StringComparer.Ordinal);
    string BaselinePath => Path.Combine(git.ControlFolder, "git-baseline.json");

    public async Task InitializeAsync(string repo, CancellationToken ct)
    {
        var mutex = baselineLocks.GetOrAdd(BaselinePath, _ => new(1));
        await mutex.WaitAsync(ct);
        try
        {
            // Never adopt changes from an earlier worker, including after a supervisor restart.
            if (!File.Exists(BaselinePath))
                Json.WriteBytes(BaselinePath, Encoding.UTF8.GetBytes((await CaptureAsync(repo, ct)).ToJsonString(Json.Options(strict: true, indented: true)) + "\n"));
        }
        finally { mutex.Release(); }
    }

    public async Task CheckAsync(string repo, CancellationToken ct)
    {
        if (!File.Exists(BaselinePath)) throw new HarnessException("git config baseline is missing; refusing workspace git writes");
        var baseline = JsonNode.Parse(await File.ReadAllTextAsync(BaselinePath, ct))!.AsObject();
        var current = await CaptureAsync(repo, ct);
        var owned = new HashSet<string>(StringComparer.Ordinal);
        var hasOwnership = false;
        foreach (var directory in Directory.Exists(Locations.LedgerRoot) ? Directory.GetDirectories(Locations.LedgerRoot) : [])
        {
            if (!File.Exists(Path.Combine(directory, "run.json"))) continue;
            var ledger = new Ledger(directory);
            var info = ledger.LoadRunInfo();
            if (info.ControlId is null || !Locations.Same(Path.Combine(Locations.ControlRoot, info.ControlId), git.ControlFolder)) continue;
            hasOwnership = true;
            foreach (var folder in Directory.GetDirectories(ledger.Dir))
                if (File.Exists(Path.Combine(folder, "task.json")) && ledger.LoadRecord(Path.GetFileName(folder)).Workspace.Branch is { } branch) owned.Add(branch);
        }
        bool ForeignTracking(string key)
        {
            var match = Regex.Match(key, "^branch\\.(.+)\\.(remote|merge|rebase)$", RegexOptions.CultureInvariant);
            return hasOwnership && match.Success && !owned.Contains(match.Groups[1].Value);
        }
        var keys = baseline.Select(x => x.Key).Union(current.Select(x => x.Key), StringComparer.Ordinal)
            .Where(key => !ForeignTracking(key))
            .Where(key => !JsonNode.DeepEquals(baseline[key], current[key])).Order(StringComparer.Ordinal).ToList();
        if (keys.Count == 0) return;
        var blocking = keys.Any(key => Regex.IsMatch(key, "^filter\\..+\\.(clean|smudge|process)$", RegexOptions.CultureInvariant));
        git.Report("git-config-changed", new JsonObject { ["keys"] = Json.Array(keys), ["blocking"] = blocking });
        if (blocking) throw new GitConfigChangedException();
    }

    async Task<JsonObject> CaptureAsync(string repo, CancellationToken ct)
    {
        var config = await git.CheckBytesAsync(repo, ct, "config", "--list", "--show-origin", "--show-scope", "-z");
        var snapshot = new JsonObject();
        var offset = 0;
        byte[] Field()
        {
            var end = Array.IndexOf(config, (byte)0, offset);
            if (end < 0) throw new HarnessException("git returned an invalid config listing");
            var value = config[offset..end];
            offset = end + 1;
            return value;
        }
        while (offset < config.Length)
        {
            var scope = Encoding.UTF8.GetString(Field());
            var origin = Encoding.UTF8.GetString(Field());
            if (origin.StartsWith("file:", StringComparison.Ordinal)) origin = "file:" + Path.GetFullPath(origin[5..], repo);
            var entry = Field();
            var newline = Array.IndexOf(entry, (byte)'\n');
            var key = Encoding.UTF8.GetString(newline < 0 ? entry : entry[..newline]);
            // These command-scope entries are our per-call overrides, not repository changes.
            if (scope == "command" && key is "core.hookspath" or "core.fsmonitor" or "commit.gpgsign" or "gc.auto" or "maintenance.auto") continue;
            var digest = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(newline < 0 ? [] : entry[(newline + 1)..]));
            if (snapshot[key] is not JsonArray entries) snapshot[key] = entries = new();
            entries.Add((JsonNode)new JsonObject { ["scope"] = scope, ["origin"] = origin, ["key"] = key, ["hasValue"] = newline >= 0, ["sha256"] = digest });
        }
        var common = Path.GetFullPath((await git.CheckAsync(repo, ct, "rev-parse", "--git-common-dir")).Trim(), repo);
        var hooks = Path.Combine(common, "hooks");
        if (Directory.Exists(hooks))
            foreach (var path in Directory.EnumerateFiles(hooks, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint }).Order(StringComparer.Ordinal))
                snapshot["hooks/" + Path.GetRelativePath(hooks, path).Replace('\\', '/')] = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(path, ct)));
        return snapshot;
    }
}

sealed class GitConfigChangedException() : HarnessException("git-config-changed: refusing worker-changed filters");
