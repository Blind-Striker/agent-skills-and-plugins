sealed class AllowListFile
{
    public List<string>? Routes { get; set; }
}

sealed class PolicyFile
{
    public List<PolicyRule>? Deny { get; set; }

    public List<PolicyRule>? Escalate { get; set; }

    public List<string>? Instructions { get; set; }

    public bool? RawPrompt { get; set; }
}

sealed class PolicyRule
{
    public string? Name { get; set; }

    public string? Action { get; set; }

    public List<string>? Resources { get; set; }
}
