sealed class Policy
{
    // Forms cannot be answered by the harness; the model gets this when its question is cancelled.
    public const string NoHuman =
        "Nobody is available to answer questions during this dispatch run. "
        + "Continue without an answer, and say in your final report what you needed.";

    // Feedback for an operator's reject without --message. A reject with a message lets the model go on.
    public const string LeadDeclined =
        "The operator declined this action. Continue without it, and say in your final report if the task needed it.";

    public required string Path { get; init; }

    public required List<PolicyRule> Deny { get; init; }

    public required List<PolicyRule> Escalate { get; init; }

    public required List<string> Instructions { get; init; }

    // Opt-in for experiments that measure what a model does with the task text alone: the task
    // prompt goes to the session verbatim, with no standing-instruction prefix. The session rules
    // (escalate, deny) and the settle-time scope flagging still apply.
    public bool RawPrompt { get; init; }

    public static Policy From(EffectiveConfig config, bool rawPrompt = false) => new()
    {
        Path = "config.json",
        Deny = config.Restrictions?.Deny.Select(r => new PolicyRule { Name = r.Name, Action = r.Action, Resources = r.Resources.ToList() }).ToList() ?? [],
        Escalate = config.Restrictions?.Escalate.Select(r => new PolicyRule { Name = r.Name, Action = r.Action, Resources = r.Resources.ToList() }).ToList() ?? [],
        Instructions = config.Instructions.Select(i => i.Text).ToList(),
        RawPrompt = rawPrompt,
    };

    public string Describe() =>
        $"policy {Path}: deny [{string.Join(", ", Deny.Select(x => x.Name))}], escalate [{string.Join(", ", Escalate.Select(x => x.Name))}], "
        + (RawPrompt ? "raw prompts (no standing instructions)" : $"{Instructions.Count} standing instructions");

    public PolicyRule? DenialFor(string action, IReadOnlyList<string> resources) =>
        Deny.FirstOrDefault(rule => Matches(rule, action, resources));

    // No escalation match leaves the backend-specific default decision to the core.
    public PolicyRule? EscalationFor(string action, IReadOnlyList<string> resources) =>
        Escalate.FirstOrDefault(rule => Matches(rule, action, resources));

    static bool Matches(PolicyRule rule, string action, IReadOnlyList<string> resources) =>
        Wildcard.Match(action, rule.Action!, OperatingSystem.IsWindows())
        && resources.Any(resource => rule.Resources!.Any(pattern => Wildcard.Match(resource, pattern, OperatingSystem.IsWindows())));

}
