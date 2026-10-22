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

sealed class PolicyRule : AnnotatedObject
{
    public string? Name { get; set; }

    public string? Action { get; set; }

    public List<string>? Resources { get; set; }
}
