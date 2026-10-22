static class StructuredOutput
{
    public static void Freeze(Ledger ledger, IReadOnlyList<TaskRun> runs, Action<string>? beforeConfigMove = null)
    {
        var structured = runs.Where(x => x.Record.Spec.Kind == "structured").ToList();
        if (structured.Count == 0) return;
        var config = ConfigLayers.Frozen(ledger.Dir);
        foreach (var run in structured)
        {
            var inputs = run.Record.PromptInputs ?? throw new HarnessException($"{run.Id}: structured inputs are unavailable");
            var task = config.Tasks[run.Id];
            task.InputSha256 = Save("input.json", inputs.Items!);
            task.ResultSchemaSha256 = Save("result.schema.json", inputs.Schema!);

            string Save(string name, JsonNode node)
            {
                var bytes = Encoding.UTF8.GetBytes(node.ToJsonString(Json.Options(strict: false, indented: true)) + "\n");
                Json.WriteBytes(Path.Combine(ledger.TaskDir(run.Id), name), bytes);
                return Hash(bytes);
            }
        }
        Json.WriteFile(Path.Combine(ledger.Dir, "config.json"), config, LedgerJson.Files.EffectiveConfig, beforeConfigMove);
    }

    public static async Task ValidateAsync(Ledger ledger, TaskRun run, TaskResult result, string? text, CancellationToken ct)
    {
        var path = Path.Combine(ledger.TaskDir(run.Id), "deliverable.json");
        // A later collection must never leave an earlier valid artifact beside an invalid result.
        if (File.Exists(path)) File.Delete(path);
        try
        {
            var inputs = Load(ledger, run);
            var spec = ledger.FrozenSpec(run.Id);
            var options = spec.Result ?? throw new HarnessException("structured result is missing");
            var check = Deliverable.LoadSchema(inputs.Schema!.ToJsonString()).Validate(text ?? "", inputs.Items!, options,
                spec.Input?.IdField ?? "id");
            result.Validation = check.Validation;
            if (check.Validation.Ok)
            {
                await ledger.WriteArtifactAsync(run.Id, "deliverable.json",
                    check.Document!.ToJsonString(Json.Options(strict: false, indented: true)) + "\n", ct);
                result.Deliverable = "deliverable.json";
            }
        }
        catch (Exception error) when (error is HarnessException or IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or KeyNotFoundException)
        {
            result.Error = "structured validation failed: " + error.Message;
            result.Validation = new() { Errors = [new() { Message = error.Message }] };
        }
        ledger.Write(run.Id, "validated", new JsonObject
        {
            ["ok"] = result.Validation.Ok,
            ["errors"] = new JsonArray(result.Validation.Errors.Select(e => (JsonNode)new JsonObject { ["at"] = e.At, ["message"] = e.Message }).ToArray()),
        });

    }

    public static Prompt.Inputs Load(Ledger ledger, TaskRun run) => Load(ledger, run.Id);

    public static Prompt.Inputs Load(Ledger ledger, string task)
    {
        var frozen = ConfigLayers.Frozen(ledger.Dir).Tasks[task];
        return new([], JsonNode.Parse(Read("input.json", frozen.InputSha256))!.AsArray(),
            JsonNode.Parse(Read("result.schema.json", frozen.ResultSchemaSha256)));

        string Read(string name, string? expected)
        {
            byte[] bytes;
            try { bytes = File.ReadAllBytes(Path.Combine(ledger.TaskDir(task), name)); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            { throw new FrozenInputException($"{task}: {name}: frozen copy is unavailable: {error.Message}"); }
            if (expected is null || Hash(bytes) != expected) throw new FrozenInputException($"{task}: {name}: frozen SHA-256 mismatch");
            return Encoding.UTF8.GetString(bytes);
        }
    }

    static string Hash(byte[] bytes) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
}

sealed class FrozenInputException(string message) : HarnessException(message);
