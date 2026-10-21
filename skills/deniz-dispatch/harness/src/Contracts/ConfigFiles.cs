sealed class AllowListFile : VersionedContract
{
    public List<string>? Routes { get; set; }
}

sealed class PolicyFile : VersionedContract
{
    public List<PolicyRule>? Deny { get; set; }

    public List<PolicyRule>? Escalate { get; set; }

    public List<string>? Instructions { get; set; }

    public bool? RawPrompt { get; set; }
}

sealed class PolicyRule : AnnotatedObject
{
    public string? Name { get; set; }

    public string? Action { get; set; }

    public List<string>? Resources { get; set; }
}
