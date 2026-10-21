sealed class BackendScope : IAsyncDisposable
{
    readonly string? ownedFolder;
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

    public static string Selected(Args args) => args.Get("backend") ?? ConfigLayers.User?.Preferences?.Backend ?? "opencode";
    public async ValueTask DisposeAsync()
    {
        try { foreach (var backend in Backends.Values) await backend.DisposeAsync(); }
        finally { if (ownedFolder is not null && Directory.Exists(ownedFolder)) Directory.Delete(ownedFolder, recursive: true); }
    }
}
