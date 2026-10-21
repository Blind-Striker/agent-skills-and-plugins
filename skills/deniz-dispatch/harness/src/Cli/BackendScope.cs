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

    public static string Selected(Args args, ConfigLayers layers)
    {
        if (args.Get("backend") is { } explicitBackend) return explicitBackend;
        // Catalog commands have no task settings. Merge their preference from the loaded copies,
        // including the committed repository bytes rather than its possibly edited working copy.
        foreach (var layer in new[] { "repo", "user" })
            if (layers.Copies.TryGetValue(layer + ".jsonc", out var bytes)
                && ConfigLayers.Read(bytes, layer, layer + " config").Preferences?.Backend is { } backend)
                return backend;
        return "opencode";
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
