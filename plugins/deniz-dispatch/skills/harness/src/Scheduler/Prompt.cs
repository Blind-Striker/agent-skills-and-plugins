static class Prompt
{
    // The prompt a dispatched session receives: the standing instructions first, then the task.
    // A raw-prompt policy sends the task text alone.
    public static string Render(this Policy policy, PlannedOrRecord task, string worktree)
    {
        if (policy.RawPrompt) return task.PromptText;
        var lines = new List<string> { $"Your worktree: {worktree}" };
        lines.AddRange(policy.Instructions);
        // The server's refusal names only the tool ("Permission denied: shell"); without this line
        // a model can read two denied commands as a denied tool.
        if (policy.Deny.Count > 0)
        {
            var denied = string.Join(", ", policy.Deny.SelectMany(rule => rule.Resources!.Select(resource => $"{rule.Action} `{resource}`")));
            lines.Add($"These calls are denied by rule and fail with \"Permission denied\": {denied}. A denial covers only the matching call; other calls of the same tool still work.");
        }
        lines.Add($"Declared scope: {(task.Spec.Scope is { Count: > 0 } s ? string.Join(", ", s) : "(none declared)")}. Changes outside it are flagged for review.");
        if (task.Spec.Kind is "review" or "research")
            lines.Add("This is a read-only task: do not create, change or delete files. Report your findings instead.");
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
