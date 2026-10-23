sealed class BackendScope : IAsyncDisposable
{
    readonly string? ownedFolder;
    internal string? OwnedFolder => ownedFolder;
    public IReadOnlyDictionary<string, IWorkerBackend> Backends { get; }

    public BackendScope(IEnumerable<string> names, string? controlFolder = null, bool subscribe = true, string? runId = null)
    {
        var profiles = controlFolder is null
            ? Path.Combine(Locations.ControlRoot, $"cmd-{Environment.ProcessId}-{Guid.NewGuid():N}", "profiles")
            : Path.Combine(controlFolder, "profiles");
        if (controlFolder is null)
        {
            ownedFolder = Path.GetDirectoryName(profiles);
            Locations.CreateControlFolder(Path.GetFileName(ownedFolder)!);
        }
        Directory.CreateDirectory(profiles);
        Backends = names.Distinct(StringComparer.Ordinal).ToDictionary(name => name, IWorkerBackend (name) => name switch
        {
            "opencode" => new OpenCodeBackend(subscribe, new(profiles, ConfigLayers.User?.Backends?.Opencode?.Executable,
                Cli.RootEnvironment?.Passthrough(ConfigLayers.User?.Backends?.Opencode?.Env ?? []) ?? new Dictionary<string, string>()), runId),
            "codex" => new CodexBackend(new(profiles, ConfigLayers.User?.Backends?.Codex?.Executable,
                Cli.RootEnvironment?.Passthrough(ConfigLayers.User?.Backends?.Codex?.Env ?? []) ?? new Dictionary<string, string>())),
            _ => throw new HarnessException($"unknown backend: {name}"),
        });
    }

    public static IReadOnlyList<string> Selected(Args args, ConfigLayers layers)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        if (args.Get("backend") is { } explicitBackend) names.Add(explicitBackend);
        else
        {
            string? preferred = null;
            var configured = new HashSet<string>(StringComparer.Ordinal);
            foreach (var layer in new[] { "user", "repo" })
                if (layers.Copies.TryGetValue(layer + ".jsonc", out var bytes)
                    && ConfigLayers.Read(bytes, layer, layer + " config") is { } config)
                {
                    preferred = config.Preferences?.Backend ?? preferred;
                    names.UnionWith(config.Preferences?.DefaultRoutes?.Keys.AsEnumerable() ?? []);
                    if (config.Backends?.Opencode is not null) configured.Add("opencode");
                    if (config.Backends?.Codex is not null) configured.Add("codex");
                }
            if (preferred is not null) names.Add(preferred);
            foreach (var route in layers.Effective.Restrictions?.Routes?.Value ?? []) names.Add(route[..route.IndexOf(':')]);
            foreach (var task in layers.Effective.Tasks.Values)
                if (task.Settings.Backend?.Value is { } backend) names.Add(backend);
            if (names.Count == 0) names.UnionWith(configured);
        }
        if (names.Count == 0 && !(args.Command == "selfcheck" && args.Positional.Count == 0))
            throw new HarnessException("no backend configured; set preferences.backend, defaultRoutes or --backend");
        foreach (var name in names)
            if (name is not ("opencode" or "codex")) throw new HarnessException($"unknown backend: {name}");
        return names.Order(StringComparer.Ordinal).ToArray();
    }
    public async ValueTask DisposeAsync()
    {
        var failures = new List<Exception>();
        foreach (var backend in Backends.Values)
        {
            try { await backend.DisposeAsync(); }
            catch (Exception ex) { failures.Add(ex); }
        }
        if (failures.Count > 0) throw new AggregateException("Backend cleanup unconfirmed", failures);
        if (ownedFolder is not null && Directory.Exists(ownedFolder)) Directory.Delete(ownedFolder, recursive: true);
    }
}
