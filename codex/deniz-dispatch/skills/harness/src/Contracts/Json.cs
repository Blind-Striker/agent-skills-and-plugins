[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(AllowListFile))]
[JsonSerializable(typeof(PolicyFile))]
[JsonSerializable(typeof(TaskFile))]
[JsonSerializable(typeof(RunInfo))]
[JsonSerializable(typeof(TaskRecord))]
[JsonSerializable(typeof(TaskResult))]
[JsonSerializable(typeof(LedgerLine))]
[JsonSerializable(typeof(PendingAsk))]
[JsonSerializable(typeof(AskDecision))]
[JsonSerializable(typeof(string))]
partial class DispatchJson : JsonSerializerContext;

// The same shapes, indented, for the files people read: run.json, task.json, result.json, and
// the pending asks and decisions.
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(RunInfo))]
[JsonSerializable(typeof(TaskRecord))]
[JsonSerializable(typeof(TaskResult))]
[JsonSerializable(typeof(PendingAsk))]
[JsonSerializable(typeof(AskDecision))]
partial class DispatchJsonIndented : JsonSerializerContext;

static class Json
{
    public static void WriteFile<T>(string path, T value, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> info)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(value, info) + "\n");
    }

    public static JsonArray Array(IEnumerable<string> items) => new(items.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray());

    public static JsonObject? Error(SessionStructuredError? error) => error is null
        ? null
        : new JsonObject { ["type"] = error.Type, ["message"] = error.Message, ["status"] = error.Status };

    // A concrete type on purpose: Optional<T> offers no conversion from an interface-typed value.
    public static Dictionary<string, JsonElement> Metadata(string run, string task) => new(StringComparer.Ordinal)
    {
        ["dispatch.run"] = JsonSerializer.SerializeToElement(run, DispatchJson.Default.String),
        ["dispatch.task"] = JsonSerializer.SerializeToElement(task, DispatchJson.Default.String),
    };
}
