sealed class ConfigLayers
{
    public static ConfigFile? User { get; private set; }
    static byte[]? userBytes;
    public required EffectiveConfig Effective { get; init; }
    public Dictionary<string, byte[]> Copies { get; } = new(StringComparer.Ordinal);
    public AllowList Allow => AllowList.From(Effective.Restrictions?.Routes);
    public Policy Policy => Policy.From(Effective);

    public static void Initialize(Args args)
    {
        userBytes = File.Exists(Locations.UserConfigFile) ? ReadBytes(Locations.UserConfigFile, "user") : null;
        User = userBytes is null ? null : Read(userBytes, "user", Locations.UserConfigFile);
        Locations.Configure(User?.Locations, args.Get("ledger"));
    }

    public static ConfigFile Read(byte[] bytes, string layer, string path)
    {
        try
        {
            var text = Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF');
            var file = JsonSerializer.Deserialize(text, InputJson.Read.ConfigFile) ?? throw new JsonException("config is empty");
            ConfigResolver.Validate(file, layer);
            return file;
        }
        catch (Exception ex) when (ex is JsonException or HarnessException)
        {
            throw new HarnessException($"{path} ({layer}): {ex.Message}");
        }
    }

    static byte[] ReadBytes(string path, string layer)
    {
        try { return File.ReadAllBytes(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new HarnessException($"{path} ({layer}): cannot read config: {ex.Message}");
        }
    }

    public static async Task<ConfigLayers> LoadAsync(string? repo, TaskFile file, string? tasksPath, Args args,
        CancellationToken ct, Git? git = null)
    {
        git ??= Git.Default;
        ConfigFile? repository = null;
        byte[]? repoBytes = null;
        string? head = null;
        var dirty = false;
        var repoPath = repo is null ? null : Path.Combine(repo, ".dispatch", "config.jsonc");
        if (repo is not null)
        {
            var revision = await git.RunAsync(repo, ct, "rev-parse", "--verify", "HEAD");
            if (revision.ExitCode == 0)
            {
                head = Encoding.UTF8.GetString(revision.Stdout).Trim();
                var content = await git.RunAsync(repo, ct, "show", $"{head}:.dispatch/config.jsonc");
                if (content.ExitCode == 0)
                {
                    repoBytes = content.Stdout;
                    repository = Read(repoBytes, "repo", repoPath!);
                }
            }
            var working = File.Exists(repoPath) ? ReadBytes(repoPath!, "repo working copy") : null;
            dirty = repoBytes is null ? working is not null : working is null || !working.AsSpan().SequenceEqual(repoBytes);
        }
        var policyPath = args.Get("policy");
        var policyBytes = policyPath is null ? null : ReadBytes(policyPath, "cli --policy");
        var policy = policyBytes is null ? null : Read(policyBytes, "cli", policyPath!);
        EffectiveConfig effective;
        try { effective = ConfigResolver.Resolve(file, User, repository, Preferences(args), policy); }
        catch (HarnessException ex) { throw new HarnessException($"{tasksPath ?? "config"}: {ex.Message}"); }
        effective.Locations = new() { LedgerRoot = Locations.LedgerRoot, WorktreeRoot = Locations.WorktreeRoot };
        var layers = new ConfigLayers { Effective = effective };
        if (userBytes is not null) layers.AddSource("user", Locations.UserConfigFile, userBytes);
        if (repoBytes is not null || dirty)
        {
            effective.Sources.Add(new() { Layer = "repo", Path = repoPath, Head = head, Dirty = dirty, Copy = repoBytes is null ? null : ".sources/repo.jsonc" });
            if (repoBytes is not null) layers.Copies["repo.jsonc"] = repoBytes;
        }
        if (dirty) effective.Warnings.Add($"{repoPath}: working copy differs from HEAD; only committed config is applied");
        if (policyBytes is not null) layers.AddSource("cli", policyPath!, policyBytes, "policy");
        if (tasksPath is not null) effective.Sources.Add(new() { Layer = "task-file", Path = tasksPath, Copy = "tasks.jsonc" });
        effective.Sources.Add(new() { Layer = "cli", Args = args.Original.ToList() });
        foreach (var warning in effective.Warnings) Console.WriteLine($"warning: {warning}");
        return layers;
    }

    void AddSource(string layer, string path, byte[] bytes, string? copyName = null)
    {
        var name = (copyName ?? layer) + ".jsonc";
        Copies[name] = bytes;
        Effective.Sources.Add(new() { Layer = layer, Path = path, Copy = ".sources/" + name });
    }

    public void Freeze(string directory)
    {
        foreach (var (name, bytes) in Copies) Json.WriteBytes(Path.Combine(directory, ".sources", name), bytes);
        Json.WriteFile(Path.Combine(directory, "config.json"), Effective, LedgerJson.Files.EffectiveConfig);
    }

    public static EffectiveConfig Frozen(string directory) =>
        JsonSerializer.Deserialize(File.ReadAllText(Path.Combine(directory, "config.json")), LedgerJson.Files.EffectiveConfig)
        ?? throw new HarnessException("frozen config is empty");

    public static ConfigPreferences Preferences(Args args)
    {
        int? Number(string flag)
        {
            if (args.Get(flag) is not { } raw) return null;
            if (!int.TryParse(raw, CultureInfo.InvariantCulture, out var value)) throw new HarnessException($"--{flag} needs an integer");
            return value;
        }
        var routes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var route in args.Routes)
        {
            _ = AllowList.Normalize(route);
            var colon = route.IndexOf(':');
            if (!routes.TryAdd(route[..colon], route[(colon + 1)..])) throw new HarnessException("--route may be given once per backend");
        }
        bool? rawPrompt = args.Get("raw-prompt") switch { null => null, "true" => true, "false" => false,
            _ => throw new HarnessException("--raw-prompt needs true or false") };
        return new() { Backend = args.Get("backend"), DefaultRoutes = routes, Effort = args.Get("effort"), MaxParallel = Number("max"),
            TimeoutMinutes = Number("timeout-minutes"), Profile = args.Get("profile"), Project = args.Get("project"), Access = args.Get("access"),
            Network = args.Get("network"), Workspace = args.Get("workspace"), Commit = args.Get("commit"), RawPrompt = rawPrompt };
    }

    public static void Apply(TaskSpec task, EffectiveSettings settings)
    {
        task.Backend = settings.Backend?.Value;
        if (task.Agent is null) task.Route = settings.Route!.Value![(task.Backend!.Length + 1)..];
        task.Effort = settings.Effort?.Value;
        task.TimeoutMinutes = settings.TimeoutMinutes?.Value;
        task.Profile = settings.Profile?.Value;
        task.Project = settings.Project?.Value;
        task.Access = settings.Access?.Value;
        task.Network = settings.Network?.Value;
        task.Workspace = settings.Workspace?.Value;
        task.RawPrompt = settings.RawPrompt?.Value;
    }
}
