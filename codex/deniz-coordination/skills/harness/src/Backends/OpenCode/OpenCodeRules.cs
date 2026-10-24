using OpenCode.Sdk.Models;

static class OpenCodeRules
{
    // Agent rules precede these. Last match wins, so every deny follows every escalation.
    public static AppliedEnforcement Enforcement(Modes modes) => new(
        Ready.Fenced(modes) ? "sandbox" : modes.Access == "read-only" ? "policy" : "none", modes.Network == "off" ? "policy" : "none", "policy");

    public static List<PermissionRule> Render(WorkerRules source, string? run = null, Modes? modes = null)
        => Render(source, run, modes, Ready.FenceKind);

    internal static List<PermissionRule> Render(WorkerRules source, string? run, Modes? modes, string? fenceKind)
    {
        List<PermissionRule> rules = [];
        // Override inherited shell allows; only explicit escalation may reach the harness.
        if (modes?.Access == "read-only" && modes.ReadOnlyShell == "escalate")
            rules.Add(new() { Action = "shell", Resource = "*", Effect = PermissionEffect.Ask });
        rules.AddRange([.. source.Escalate.SelectMany(rule => rule.Resources.Select(resource => new PermissionRule
        { Action = rule.Action, Resource = resource, Effect = PermissionEffect.Ask })),
         .. source.Deny.SelectMany(rule => rule.Resources.Select(resource => new PermissionRule
         { Action = rule.Action, Resource = resource, Effect = PermissionEffect.Deny }))]);
        if (modes?.Access == "read-only") rules.Add(new() { Action = "edit", Resource = "*", Effect = PermissionEffect.Deny });
        if (modes?.Access == "read-only" && modes.ReadOnlyShell == "decline" && fenceKind is null)
            rules.Add(new() { Action = "shell", Resource = "*", Effect = PermissionEffect.Deny });
        if (modes?.Network == "off")
        {
            rules.Add(new() { Action = "webfetch", Resource = "*", Effect = PermissionEffect.Deny });
            rules.Add(new() { Action = "websearch", Resource = "*", Effect = PermissionEffect.Deny });
        }
        rules.Add(new() { Action = "edit", Resource = Path.GetFullPath(Path.Combine(Locations.ControlRoot, "**")).Replace('\\', '/'), Effect = PermissionEffect.Deny });
        if (run is not null)
            rules.Add(new() { Action = "edit", Resource = Path.GetFullPath(Path.Combine(Locations.LedgerRoot, run, "**")).Replace('\\', '/'), Effect = PermissionEffect.Deny });
        return rules;
    }
}

static class OpenCodeModels
{
    public static RouteRef From(ModelRef model) => new(model.ProviderId, model.Id, model.Variant);
    public static ModelRef ToModelRef(RouteRef route) => new()
    {
        ProviderId = route.Provider,
        Id = route.Model,
        Variant = route.VariantOrDefault == "default" ? null : route.Variant,
    };
}

static class OpenCodeJson
{
    public static JsonObject? Error(SessionStructuredError? error) => error is null
        ? null : new JsonObject { ["type"] = error.Type, ["message"] = error.Message, ["status"] = error.Status };

    public static Dictionary<string, JsonElement> Metadata(string run, string task, string? controlId = null)
    {
        var metadata = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["dispatch.run"] = JsonSerializer.SerializeToElement(run, InputJson.Read.String),
            ["dispatch.task"] = JsonSerializer.SerializeToElement(task, InputJson.Read.String),
        };
        if (controlId is not null) metadata["dispatch.controlId"] = JsonSerializer.SerializeToElement(controlId, InputJson.Read.String);
        return metadata;
    }
}
