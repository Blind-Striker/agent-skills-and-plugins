static class Prompt
{
    public sealed record Contract(ContractRecord Record, string Text);
    public sealed record Inputs(IReadOnlyList<Contract> Contracts, JsonArray? Items = null, JsonNode? Schema = null);

    public static Inputs Load(TaskFile file, TaskSpec task, string tasksPath, Args? args,
        IReadOnlyList<string> instructions, Dictionary<string, Contract>? cache = null)
    {
        var directory = Path.GetDirectoryName(tasksPath)!;
        cache ??= new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        var contracts = new List<Contract>();
        var seen = new HashSet<string>(cache.Comparer);
        foreach (var (paths, source) in new[] { (file.Contracts, "task-file"), (task.Contracts, "task"), (args?.Contracts, "cli") })
        {
            foreach (var relative in paths ?? [])
            {
                if (relative.StartsWith('@')) throw new HarnessException($"contract aliases were removed; name the file path: {relative}");
                var path = Path.GetFullPath(Path.Combine(directory, relative));
                if (!seen.Add(path)) continue;
                var label = $"{task.Id}: contract {source} {path}";
                if (!cache.TryGetValue(path, out var contract))
                {
                    try
                    {
                        var bytes = File.ReadAllBytes(path);
                        var text = new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF');
                        var version = text.Split('\n')[0].TrimEnd();
                        contract = new(new ContractRecord { Path = path, Sha256 = Hash(bytes), Version = version[..Math.Min(120, version.Length)] }, text);
                        cache.Add(path, contract);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DecoderFallbackException)
                    {
                        throw new HarnessException($"{label}: cannot read UTF-8 text: {ex.Message}");
                    }
                }
                contracts.Add(new(new() { Path = path, From = source,
                    Sha256 = contract.Record.Sha256, Version = contract.Record.Version }, contract.Text));
            }
        }
        CheckRaw(task.RawPrompt == true, task, contracts.Count, instructions.Count);
        var items = task.Input?.Items;
        JsonNode? schema = null;
        if (task.Kind == "structured")
        {
            if (task.Prepare is { Count: > 0 }) throw new HarnessException("prepare is forbidden for structured tasks");
            if (task.Scope is not null || task.Verify is not null || task.AdvisoryVerify is not null || task.Shell is not null || task.ReviewOf is not null || task.Agent is not null || task.Persona is not null)
                throw new HarnessException("structured tasks refuse scope, verify, advisoryVerify, shell, reviewOf, agent and persona");
            if (task.Input is not { } input || (input.Items is null) == (input.ItemsFile is null))
                throw new HarnessException("structured input needs exactly one of items or itemsFile");
            if (task.Result is not { Schema: { Length: > 0 } } result || result.ItemsAt is null ||
                (result.ItemsAt != "" && (!result.ItemsAt.StartsWith('/') || Regex.IsMatch(result.ItemsAt, "~(?![01])", RegexOptions.CultureInvariant))) ||
                result.Identity is not ("exactly-once" or "many"))
                throw new HarnessException("structured result needs schema, a JSON Pointer itemsAt and identity exactly-once or many");
            if (task.Input?.ItemsFile is { } itemsFile)
                items = JsonNode.Parse(File.ReadAllText(Path.Combine(directory, itemsFile)), documentOptions: new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true, AllowDuplicateProperties = false })?.AsArray();
            if (items is null) throw new HarnessException("structured input must be an array of objects");
            _ = Deliverable.InputItems(items, result, input.IdField);
            var schemaText = File.ReadAllText(Path.Combine(directory, result.Schema));
            _ = Deliverable.LoadSchema(schemaText);
            schema = JsonNode.Parse(schemaText, documentOptions: new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true, AllowDuplicateProperties = false });
        }
        return new(contracts, items, schema);
    }

    static void CheckRaw(bool raw, TaskSpec task, int contracts, int instructions)
    {
        if (raw && (contracts > 0 || instructions > 0 || task.Kind == "structured"))
            throw new HarnessException($"{task.Id}: rawPrompt conflicts with contracts, instructions or structured input");
    }

    public static string Render(this Policy policy, PlannedOrRecord task, string worktree, Inputs? inputs = null)
    {
        var raw = policy.RawPrompt || task.Spec.RawPrompt == true;
        CheckRaw(raw, task.Spec, inputs?.Contracts.Count ?? task.Spec.Contracts?.Count ?? 0, policy.Instructions.Count);
        if (inputs is null && task.Spec.Contracts is { Count: > 0 })
            throw new HarnessException($"{task.Spec.Id}: original contract text is unavailable; cannot assemble the prompt");
        if (raw) return task.PromptText.TrimEnd('\n') + "\n";
        var text = new StringBuilder();
        foreach (var contract in inputs?.Contracts ?? [])
        {
            text.Append(contract.Text);
            if (!contract.Text.EndsWith('\n')) text.Append('\n');
            text.Append('\n');
        }
        var lines = new List<string> { $"Your working folder: {worktree}" };
        if (policy.Deny.Count > 0)
        {
            var denied = string.Join(", ", policy.Deny.SelectMany(rule => rule.Resources!.Select(resource => $"{rule.Action} `{resource}`")));
            lines.Add($"These calls are denied by rule: {denied}. A denial covers only the matching call; other calls of the same tool still work.");
        }
        if (task.Spec.Kind != "structured")
            lines.Add($"Declared scope: {(task.Spec.Scope is { Count: > 0 } s ? string.Join(", ", s) : "(none declared)")}. Changes outside it are flagged for review.");
        if (task.Spec.Kind != "structured" && task.Spec.Access == "read-only")
            lines.Add("This is a read-only task: do not create, change or delete files. Report your findings instead.");
        lines.Add("Nobody answers questions during this run: a question is cancelled, so continue without the answer.");
        if (task.Spec.Kind != "structured")
            lines.Add($"End your final message with exactly one status line: `{Settlement.StatusPrefix} done` when the task is done, "
                + $"`{Settlement.StatusPrefix} stopped` when you stopped on a rule or a contradiction, "
                + $"`{Settlement.StatusPrefix} blocked` when the environment kept you from the work (a missing tool, access or input).");
        lines.AddRange(policy.Instructions);
        text.Append("Standing instructions from the dispatch harness (they apply to the whole task):\n")
            .Append(string.Join("\n", lines.Select(x => $"- {x}"))).Append("\n\n---\nTask:\n\n").Append(task.PromptText.TrimEnd('\n')).Append('\n');
        if (task.Spec.Kind == "structured")
        {
            Block(text, $"Input items (JSON; the field `{task.Spec.Input?.IdField ?? "id"}` identifies each item):", inputs?.Items ?? task.Spec.Input?.Items);
            if (inputs?.Schema is { } schema) Block(text, "Result schema (JSON Schema):", schema);
            text.Append($"\nAnswer with exactly one JSON document that matches the result schema, with the answers in the array at `{task.Spec.Result?.ItemsAt}`, and nothing else.\n");
        }
        return text.ToString();
    }

    static void Block(StringBuilder text, string label, JsonNode? node)
    {
        var json = node?.ToJsonString(Json.Options(strict: false, indented: true)) ?? "[]";
        var longest = Regex.Matches(json, "`+").Select(m => m.Length).DefaultIfEmpty(0).Max();
        var fence = new string('`', Math.Max(3, longest + 1));
        text.Append('\n').Append(label).Append('\n').Append(fence).Append("json\n").Append(json).Append('\n').Append(fence).Append('\n');
    }

    static string Hash(byte[] bytes) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();

    public static JsonObject Event(string text, string turn, Inputs? inputs = null, bool raw = false, bool isContinue = false)
    {
        var result = new JsonObject { ["turn"] = turn, ["chars"] = text.Length, ["sha256"] = Hash(Encoding.UTF8.GetBytes(text)) };
        if (inputs?.Contracts is { Count: > 0 } contracts) result["contracts"] = Json.Array(contracts.Select(c => c.Record.Sha256));
        if (raw) result["rawPrompt"] = true;
        if (isContinue) result["continue"] = true;
        return result;
    }
}
