sealed class OpenCodeProfile(string profilesFolder)
{
    readonly HashSet<RouteRef> routes = [];
    JsonObject? global;
    public string Root { get; } = Path.Combine(profilesFolder, "opencode");

    public void Prepare(IEnumerable<RouteRef> requested)
    {
        var changed = false;
        foreach (var route in requested) changed |= routes.Add(route);
        if (!changed && global is not null) return;
        global ??= ReadGlobal();
        var providers = new JsonObject();
        foreach (var group in routes.GroupBy(x => x.Provider))
        {
            if (global["providers"]?[group.Key] is not JsonObject source) continue;
            var provider = (JsonObject)source.DeepClone();
            var models = new JsonObject();
            foreach (var model in group.Select(x => x.Model).Distinct())
                if (source["models"]?[model] is { } definition) models[model] = definition.DeepClone();
            provider["models"] = models;
            providers[group.Key] = provider;
        }
        var config = new JsonObject
        {
            ["$schema"] = "https://opencode.ai/config.json",
            ["claude"] = false,
            ["plugins"] = new JsonArray("-opencode.config.compatibility"),
            ["providers"] = providers,
        };
        if (global["shell"] is { } shell) config["shell"] = shell.DeepClone();
        CreateFolder(Root);
        File.WriteAllText(Path.Combine(Root, "opencode.json"), config.ToJsonString(new JsonSerializerOptions { WriteIndented = true, NewLine = "\n" }) + "\n");
    }

    public static void CreateFolder(string path)
    {
        if (OperatingSystem.IsWindows()) Directory.CreateDirectory(path);
        else Directory.CreateDirectory(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    static JsonObject ReadGlobal()
    {
        var root = Environment.GetEnvironmentVariable("OPENCODE_CONFIG_DIR");
        if (string.IsNullOrEmpty(root))
            root = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } xdg
                ? Path.Combine(xdg, "opencode")
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "opencode");
        var result = new JsonObject();
        foreach (var name in new[] { "opencode.json", "opencode.jsonc" })
        {
            var path = Path.Combine(root, name);
            if (!File.Exists(path)) continue;
            try
            {
                var document = JsonNode.Parse(File.ReadAllText(path), documentOptions: new JsonDocumentOptions
                { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }) as JsonObject
                    ?? throw new HarnessException("global opencode config must be an object");
                Merge(result, document);
            }
            catch (JsonException) { throw new HarnessException("global opencode config is not valid JSONC"); }
        }
        return result;
    }

    static void Merge(JsonObject target, JsonObject source)
    {
        foreach (var (key, value) in source)
            if (target[key] is JsonObject existing && value is JsonObject incoming) Merge(existing, incoming);
            else target[key] = value?.DeepClone();
    }
}
