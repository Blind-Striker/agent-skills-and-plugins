using OpenCode.Sdk.Models;

static class OpenCodeRules
{
    // Agent rules precede these. Last match wins, so every deny follows every escalation.
    public static List<PermissionRule> Render(WorkerRules source) =>
        [.. source.Escalate.SelectMany(rule => rule.Resources.Select(resource => new PermissionRule
        { Action = rule.Action, Resource = resource, Effect = PermissionEffect.Ask })),
         .. source.Deny.SelectMany(rule => rule.Resources.Select(resource => new PermissionRule
        { Action = rule.Action, Resource = resource, Effect = PermissionEffect.Deny }))];
}

static class OpenCodeModels
{
    public static RouteRef From(ModelRef model) => new(model.ProviderId, model.Id, model.Variant);
    public static ModelRef ToModelRef(RouteRef route) => new()
    {
        ProviderId = route.Provider, Id = route.Model, Variant = route.VariantOrDefault == "default" ? null : route.Variant,
    };
}

static class OpenCodeJson
{
    public static JsonObject? Error(SessionStructuredError? error) => error is null
        ? null : new JsonObject { ["type"] = error.Type, ["message"] = error.Message, ["status"] = error.Status };

    public static Dictionary<string, JsonElement> Metadata(string run, string task) => new(StringComparer.Ordinal)
    {
        ["dispatch.run"] = JsonSerializer.SerializeToElement(run, InputJson.Read.String),
        ["dispatch.task"] = JsonSerializer.SerializeToElement(task, InputJson.Read.String),
    };
}
