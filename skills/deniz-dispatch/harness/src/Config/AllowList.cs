sealed class AllowList
{
    HashSet<string>? qualified;
    public bool IsRestricted { get; private init; } = true;

    public static AllowList From(RouteRestriction? restriction)
    {
        var list = new AllowList { IsRestricted = restriction is not null,
            qualified = restriction?.Value.Select(Normalize).ToHashSet(StringComparer.Ordinal) };
        foreach (var entry in list.qualified ?? [])
        {
            if (!entry.StartsWith("opencode:", StringComparison.Ordinal)) continue;
            var text = entry[9..];
            if (text.Contains('/')) { RouteRef.TryParse(text, out var route, out _); list.Routes.Add(route!); }
            else list.Agents.Add(text);
        }
        return list;
    }

    public bool Allows(string qualifiedRoute) => !IsRestricted || (qualified?.Contains(Normalize(qualifiedRoute)) ?? false);

    public static string Normalize(string text)
    {
        var colon = text.IndexOf(':');
        if (colon <= 0 || text.Any(char.IsWhiteSpace)) throw new HarnessException($"invalid qualified route: {text}");
        var backend = text[..colon];
        var route = text[(colon + 1)..];
        if (backend == "opencode")
        {
            if (route.Contains('/'))
            {
                if (!RouteRef.TryParse(route, out _, out var error)) throw new HarnessException($"invalid route {text}: {error}");
            }
            else if (!Regex.IsMatch(route, "^[A-Za-z0-9][A-Za-z0-9._-]*$")) throw new HarnessException($"invalid agent route: {text}");
        }
        else if (backend != "codex" || !Regex.IsMatch(route, "^[^#\\s:]+(#[^#\\s:]+)?$"))
            throw new HarnessException($"invalid route: {text}");
        return text.EndsWith("#default", StringComparison.Ordinal) ? text[..^8] : text;
    }
    public List<RouteRef> Routes { get; } = [];

    public HashSet<string> Agents { get; } = new(StringComparer.Ordinal);

    public int Count => Routes.Count + Agents.Count;

    public bool AllowsRoute(RouteRef route) => !IsRestricted || (qualified is not null ? Allows($"opencode:{route}") : Routes.Any(x => x.Matches(route)));

    public bool AllowsAgent(string agent) => !IsRestricted || (qualified is not null ? Allows($"opencode:{agent}") : Agents.Contains(agent));
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
