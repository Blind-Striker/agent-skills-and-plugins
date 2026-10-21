sealed class TaskFile : VersionedContract, IJsonOnDeserialized
{
    public string? Run { get; set; }
    public string? Repo { get; set; }
    public int? MaxParallel { get; set; }
    public TaskDefaults? Defaults { get; set; }
    public TaskPolicy? Policy { get; set; }
    public List<string>? Contracts { get; set; }
    public List<TaskSpec>? Tasks { get; set; }

    void IJsonOnDeserialized.OnDeserialized()
    {
        Json.CheckVersion(SchemaVersion);
        if (Tasks is not { Count: > 0 }) throw new JsonException("tasks must be a non-empty array");
        foreach (var task in Tasks)
        {
            if (task is null || string.IsNullOrWhiteSpace(task.Kind))
                throw new JsonException($"{task?.Id ?? "<no id>"}: kind is required");
        }
    }
}

class TaskPreferences : AnnotatedObject
{
    public string? Backend { get; set; }
    public string? Effort { get; set; }
    public int? TimeoutMinutes { get; set; }
    public string? Profile { get; set; }
    public string? Project { get; set; }
    public string? Access { get; set; }
    public string? Network { get; set; }
    public string? Workspace { get; set; }
    public string? Commit { get; set; }
    public bool? RawPrompt { get; set; }
}

sealed class TaskDefaults : TaskPreferences
{
    public Dictionary<string, string>? DefaultRoutes { get; set; }
    public string? Dir { get; set; }
}

sealed class TaskSpec : TaskPreferences
{
    public string? Id { get; set; }
    public string? Dir { get; set; }

    [JsonRequired]
    public string Kind { get; set; } = "";

    public string? Base { get; set; }
    public string? Agent { get; set; }
    public string? Persona { get; set; }
    public string? Route { get; set; }
    public string? Prompt { get; set; }
    public string? PromptFile { get; set; }
    public List<string>? Scope { get; set; }
    public List<string>? Verify { get; set; }
    public string? ReviewOf { get; set; }
    public List<string>? Contracts { get; set; }
    public string? Shell { get; set; }
    public StructuredInput? Input { get; set; }
    public StructuredResult? Result { get; set; }
}

sealed class StructuredInput : AnnotatedObject
{
    public JsonArray? Items { get; set; }
    public string? ItemsFile { get; set; }
    public string IdField { get; set; } = "id";
}

sealed class StructuredResult : AnnotatedObject
{
    public string? Schema { get; set; }
    public string? ItemsAt { get; set; }
    public string Identity { get; set; } = "exactly-once";
    public List<string>? Immutable { get; set; }
}

sealed class ConfigFile : VersionedContract
{
    public ConfigPreferences? Preferences { get; set; }
    public ConfigRestrictions? Restrictions { get; set; }
    public List<string>? Instructions { get; set; }
    public ConfigLocations? Locations { get; set; }
    public BackendConfigs? Backends { get; set; }
}

sealed class ConfigPreferences : TaskPreferences
{
    public Dictionary<string, string>? DefaultRoutes { get; set; }
    public int? MaxParallel { get; set; }
}

class RestrictionValues : AnnotatedObject
{
    public List<PolicyRule>? Deny { get; set; }
    public List<PolicyRule>? Escalate { get; set; }
    public List<string>? Scope { get; set; }
    public string? MaxAccess { get; set; }
    public bool? NetworkAllowed { get; set; }
}

sealed class TaskPolicy : RestrictionValues
{
    public List<string>? Instructions { get; set; }
}

sealed class ConfigRestrictions : RestrictionValues
{
    public List<string>? Routes { get; set; }
    public List<string>? Scrub { get; set; }
}

sealed class ConfigLocations : AnnotatedObject
{
    public string? LedgerRoot { get; set; }
    public string? WorktreeRoot { get; set; }
}

sealed class BackendConfigs : AnnotatedObject
{
    public BackendConfig? Opencode { get; set; }
    public BackendConfig? Codex { get; set; }
}

sealed class BackendConfig : AnnotatedObject
{
    public string? Executable { get; set; }
    public List<string>? Env { get; set; }
}
