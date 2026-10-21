using OpenCode.Sdk;
using OpenCode.Sdk.Models;

sealed class Catalog
{
    public required IReadOnlyList<AgentInfo> Agents { get; init; }

    public required IReadOnlyList<ModelInfo> Models { get; init; }

    public required HashSet<string> Manual { get; init; }

    public required List<RouteRef> ManualRoutes { get; init; }

    public static async Task<Catalog> LoadAsync(OpenCodeClient client, string directory, TimeSpan modelWait, CancellationToken ct)
    {
        var where = new LocationSelector { Directory = directory };

        // Activation at a fresh location has no barrier: agents and models can both list empty
        // at first (observed: 0 agents while models were already listed), and the listing can
        // answer 503 while the location's plugins activate (observed on a fresh server). Built-in
        // agents always exist, so poll until both lists are non-empty, under a deadline.
        IReadOnlyList<AgentInfo>? agents = null;
        IReadOnlyList<ModelInfo>? models = null;
        OpenCodeApiException? unavailable = null;
        var deadline = DateTime.UtcNow + modelWait;
        while (true)
        {
            try
            {
                var listedAgents = (await client.Agents.ListAgentsAsync(new AgentListRequest { Location = where }, cancellationToken: ct)).Agents;
                var listedModels = (await client.LanguageModels.ListModelsAsync(new ModelListRequest { Location = where }, cancellationToken: ct)).Models;
                (agents, models, unavailable) = (listedAgents, listedModels, null);
            }
            catch (OpenCodeApiException ex) when (Ready.IsNotReady(ex))
            {
                unavailable = ex;
            }

            if ((agents is { Count: > 0 } && models is { Count: > 0 }) || DateTime.UtcNow >= deadline)
            {
                break;
            }

            await Task.Delay(TimeSpan.FromSeconds(2), ct);
        }

        if (agents is null || models is null)
        {
            throw new NotReadyException($"{directory}: the server still answered {unavailable?.Status} after {modelWait.TotalSeconds:0} s: {unavailable?.Message}");
        }

        var manual = ManualAgents(agents);
        return new Catalog
        {
            Agents = agents,
            Models = models,
            Manual = manual,
            ManualRoutes = agents.Where(x => manual.Contains(x.Id) && x.Model is not null).Select(x => OpenCodeModels.From(x.Model!)).ToList(),
        };
    }

    // Manual class: the description starts with MANUAL, or a roster's primary agent denies the
    // subagent action for exactly this agent. Direct sessions bypass those denies, so the harness
    // enforces them itself.
    static HashSet<string> ManualAgents(IReadOnlyList<AgentInfo> agents)
    {
        var ids = agents.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        var manual = new HashSet<string>(StringComparer.Ordinal);
        foreach (var agent in agents)
        {
            if (agent.Description?.TrimStart().StartsWith("MANUAL", StringComparison.Ordinal) == true)
            {
                manual.Add(agent.Id);
            }

            if (agent.Mode == AgentInfoMode.Subagent)
            {
                continue;
            }

            foreach (var rule in agent.Permissions)
            {
                if (rule.Effect == PermissionEffect.Deny && rule.Action is "subagent" or "task" && ids.Contains(rule.Resource))
                {
                    manual.Add(rule.Resource);
                }
            }
        }

        return manual;
    }

    public static async Task<List<string>> McpServerNamesAsync(OpenCodeClient client, string directory, CancellationToken ct)
    {
        var servers = await Ready.PollAsync(
            () => client.McpServers.ListMcpServersAsync(new McpListRequest { Location = new LocationSelector { Directory = directory } }, cancellationToken: ct),
            TimeSpan.FromSeconds(60),
            $"MCP listing at {directory}",
            ct);
        return servers.McpServers.Select(x => x.Name).Distinct(StringComparer.Ordinal).ToList();
    }

    public ModelInfo? FindModel(RouteRef route) =>
        Models.FirstOrDefault(x => x.ProviderId == route.Provider && x.Id == route.Model);

    // Null when the route can run at this location; otherwise the reason it cannot.
    public string? CheckRoute(RouteRef route)
    {
        var model = FindModel(route);
        if (model is null) return "not listed at this location";
        if (!model.Enabled) return "listed but disabled";
        if (route.VariantOrDefault != "default" && model.Variants.All(v => v.Id != route.Variant))
        {
            return $"variant {route.Variant} not listed (has: {string.Join(',', model.Variants.Select(v => v.Id))})";
        }

        return null;
    }

    public string? CheckAgent(string id)
    {
        var agent = Agents.FirstOrDefault(x => x.Id == id);
        if (agent is null) return "agent not found at this location";
        if (Manual.Contains(id)) return "manual-class agent";
        if (agent.Model is null) return "agent pins no model";
        return CheckRoute(OpenCodeModels.From(agent.Model));
    }

    public bool IsManualRoute(RouteRef route) => ManualRoutes.Any(x => x.Matches(route));

    public string Lineage(RouteRef route) => FindModel(route)?.Family is { Length: > 0 } family ? family : route.Provider;
}

// A fresh location can answer 503 while its plugins activate. Bound every startup listing.
static class Ready
{
    public static bool IsNotReady(OpenCodeApiException ex) => ex.Status == 503;

    public static async Task<T> PollAsync<T>(Func<Task<T>> call, TimeSpan wait, string what, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + wait;
        while (true)
        {
            try { return await call(); }
            catch (OpenCodeApiException ex) when (IsNotReady(ex))
            {
                if (DateTime.UtcNow >= deadline)
                    throw new NotReadyException($"{what}: the server still answered {ex.Status} after {wait.TotalSeconds:0} s: {ex.Message}");
            }
            await Task.Delay(TimeSpan.FromSeconds(1), ct);
        }
    }
}
