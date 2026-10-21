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

    public static Policy Load(string? path)
    {
        path = System.IO.Path.GetFullPath(path ?? Locations.PolicyFile);
        if (!File.Exists(path))
        {
            throw new HarnessException($"missing policy {path}");
        }

        PolicyFile file;
        try
        {
            file = JsonSerializer.Deserialize(File.ReadAllText(path), InputJson.Read.PolicyFile)
                ?? throw new HarnessException($"policy {path} is empty");
        }
        catch (JsonException ex)
        {
            throw new HarnessException($"policy {path} is not valid: {ex.Message}");
        }

        var policy = new Policy
        {
            Path = path,
            Deny = file.Deny ?? [],
            Escalate = file.Escalate ?? [],
            Instructions = (file.Instructions ?? []).Where(x => !string.IsNullOrWhiteSpace(x)).ToList(),
            RawPrompt = file.RawPrompt == true,
        };
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rule in policy.Deny.Concat(policy.Escalate))
        {
            if (string.IsNullOrWhiteSpace(rule.Name) || !names.Add(rule.Name)) throw new HarnessException($"policy {path}: every rule needs a unique name ('{rule.Name}')");
            if (string.IsNullOrWhiteSpace(rule.Action)) throw new HarnessException($"policy {path}: rule {rule.Name} needs an action");
            if (rule.Resources is not { Count: > 0 } || rule.Resources.Any(string.IsNullOrWhiteSpace)) throw new HarnessException($"policy {path}: rule {rule.Name} needs resources");
        }

        // Behaviour comes from these instructions, so a policy without them is a mistake, unless
        // the policy sends raw prompts, which carry no instructions by design.
        if (policy.Instructions.Count == 0 && !policy.RawPrompt)
        {
            throw new HarnessException($"policy {path}: the standing instructions are empty");
        }

        if (policy.RawPrompt && policy.Instructions.Count > 0)
        {
            throw new HarnessException($"policy {path}: rawPrompt sends no standing instructions, so the policy must not list any");
        }

        return policy;
    }

    public string Describe() =>
        $"policy {Path}: deny [{string.Join(", ", Deny.Select(x => x.Name))}], escalate [{string.Join(", ", Escalate.Select(x => x.Name))}], "
        + (RawPrompt ? "raw prompts (no standing instructions)" : $"{Instructions.Count} standing instructions");

    // Session rules come after the agent's rules, and the last matching rule wins
    // (core/src/permission.ts). Escalations become asks so the server raises them; the denies
    // come last so nothing reopens them.
    public List<PermissionRule> Rules()
    {
        var rules = new List<PermissionRule>();
        foreach (var rule in Escalate)
        {
            rules.AddRange(rule.Resources!.Select(resource => new PermissionRule { Action = rule.Action!, Resource = resource, Effect = PermissionEffect.Ask }));
        }

        foreach (var rule in Deny)
        {
            rules.AddRange(rule.Resources!.Select(resource => new PermissionRule { Action = rule.Action!, Resource = resource, Effect = PermissionEffect.Deny }));
        }

        return rules;
    }

    // The escalation entry that names this ask, or null, which means "approve once".
    public PolicyRule? EscalationFor(string action, IReadOnlyList<string> resources) =>
        Escalate.FirstOrDefault(rule =>
            Wildcard.Match(action, rule.Action!)
            && resources.Any(resource => rule.Resources!.Any(pattern => Wildcard.Match(resource, pattern))));

    // The session's shell environment without credentials, with a per-task TEMP.
    public static (Dictionary<string, string> Variables, List<string> Removed) Environment(string task)
    {
        var secret = new Regex(@"(TOKEN|SECRET|PASSWORD|PASSWD|API_?KEY|ACCESS_?KEY|PRIVATE_?KEY|CREDENTIAL|(^|_)AUTH($|_)|(^|_)PAT$|^GH_|^GITHUB_|^NUGET_|^OPENCODE_SERVER)", RegexOptions.IgnoreCase);
        var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var removed = new List<string>();
        foreach (System.Collections.DictionaryEntry entry in System.Environment.GetEnvironmentVariables())
        {
            var name = (string)entry.Key;
            if (secret.IsMatch(name))
            {
                removed.Add(name);
            }
            else
            {
                variables[name] = entry.Value as string ?? "";
            }
        }

        var temp = Locations.TaskTemp(task);
        Directory.CreateDirectory(temp);
        variables["TEMP"] = temp;
        variables["TMP"] = temp;
        variables["TMPDIR"] = temp;

        // A reused MSBuild server keeps the environment of whoever started it, so the per-task
        // temporary folder would not apply to its NuGet scratch work.
        variables["DOTNET_CLI_USE_MSBUILD_SERVER"] = "0";
        removed.Sort(StringComparer.Ordinal);
        return (variables, removed);
    }

    // The prompt a dispatched session receives: the standing instructions first, then the task.
    // A raw-prompt policy sends the task text alone.
    public string Prompt(PlannedOrRecord task, string worktree)
    {
        if (RawPrompt)
        {
            return task.PromptText;
        }

        var lines = new List<string> { $"Your worktree: {worktree}" };
        lines.AddRange(Instructions);

        // The server's refusal names only the tool ("Permission denied: shell"); without this line
        // a model can read two denied commands as a denied tool.
        if (Deny.Count > 0)
        {
            var denied = string.Join(", ", Deny.SelectMany(rule => rule.Resources!.Select(resource => $"{rule.Action} `{resource}`")));
            lines.Add($"These calls are denied by rule and fail with \"Permission denied\": {denied}. A denial covers only the matching call; other calls of the same tool still work.");
        }

        lines.Add($"Declared scope: {(task.Spec.Scope is { Count: > 0 } s ? string.Join(", ", s) : "(none declared)")}. Changes outside it are flagged for review.");
        if (task.Spec.Kind is "review" or "research")
        {
            lines.Add("This is a read-only task: do not create, change or delete files. Report your findings instead.");
        }

        lines.Add("Nobody answers questions during this run: a question is cancelled, so continue without the answer.");
        return $"""
            Standing instructions from the dispatch harness (they apply to the whole task):
            {string.Join("\n", lines.Select(x => $"- {x}"))}

            ---
            Task:

            {task.PromptText}
            """;
    }
}
