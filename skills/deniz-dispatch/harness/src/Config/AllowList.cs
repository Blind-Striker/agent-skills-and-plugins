sealed class AllowList
{
    public List<RouteRef> Routes { get; } = [];

    public HashSet<string> Agents { get; } = new(StringComparer.Ordinal);

    public int Count => Routes.Count + Agents.Count;

    public static AllowList Load()
    {
        if (!File.Exists(Locations.AllowListFile))
        {
            throw new HarnessException($"missing allow-list {Locations.AllowListFile}");
        }

        AllowListFile? file;
        try
        {
            file = JsonSerializer.Deserialize(File.ReadAllText(Locations.AllowListFile), InputJson.Read.AllowListFile);
        }
        catch (JsonException ex)
        {
            throw new HarnessException($"allow-list is not valid JSONC: {ex.Message}");
        }

        var list = new AllowList();
        foreach (var raw in file?.Routes ?? [])
        {
            var entry = raw.Trim();
            if (entry.Contains('/'))
            {
                list.Routes.Add(RouteRef.TryParse(entry, out var route, out var error)
                    ? route!
                    : throw new HarnessException($"allow-list entry '{entry}': {error}"));
            }
            else if (Regex.IsMatch(entry, "^[A-Za-z0-9][A-Za-z0-9._-]*$"))
            {
                list.Agents.Add(entry);
            }
            else
            {
                throw new HarnessException($"allow-list entry '{entry}' is neither a route nor an agent name");
            }
        }

        return list;
    }

    public bool AllowsRoute(RouteRef route) => Routes.Any(x => x.Matches(route));

    public bool AllowsAgent(string agent) => Agents.Contains(agent);
}

// provider/model#variant. The model part may contain '/', so the provider ends at the first '/'
// and the variant starts at the last '#'. A missing variant means the default variant.
sealed record RouteRef(string Provider, string Model, string? Variant)
{
    public string VariantOrDefault => string.IsNullOrEmpty(Variant) ? "default" : Variant;

    public static bool TryParse(string text, out RouteRef? route, out string? error)
    {
        route = null;
        error = null;
        if (text.Any(char.IsWhiteSpace))
        {
            error = "routes contain no whitespace";
            return false;
        }

        var slash = text.IndexOf('/');
        if (slash <= 0 || slash == text.Length - 1)
        {
            error = "expected provider/model#variant";
            return false;
        }

        var rest = text[(slash + 1)..];
        string? variant = null;
        var hash = rest.LastIndexOf('#');
        if (hash >= 0)
        {
            variant = rest[(hash + 1)..];
            rest = rest[..hash];
            if (variant.Length == 0 || rest.Length == 0)
            {
                error = "empty model or variant";
                return false;
            }
        }

        route = new RouteRef(text[..slash], rest, variant);
        return true;
    }

    public static RouteRef From(ModelRef model) => new(model.ProviderId, model.Id, model.Variant);

    public bool Matches(RouteRef other) =>
        string.Equals(Provider, other.Provider, StringComparison.Ordinal)
        && string.Equals(Model, other.Model, StringComparison.Ordinal)
        && string.Equals(VariantOrDefault, other.VariantOrDefault, StringComparison.Ordinal);

    public ModelRef ToModelRef() => new()
    {
        ProviderId = Provider,
        Id = Model,
        Variant = VariantOrDefault == "default" ? null : Variant,
    };

    public override string ToString() => $"{Provider}/{Model}#{VariantOrDefault}";
}
