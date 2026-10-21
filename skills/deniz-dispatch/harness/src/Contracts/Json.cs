[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    NewLine = "\n",
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(TaskFile))]
[JsonSerializable(typeof(ConfigFile))]
[JsonSerializable(typeof(string))]
partial class InputJson : JsonSerializerContext
{
    public static InputJson Read { get; } = new(Json.Options(strict: true, indented: true));
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    WriteIndented = true,
    NewLine = "\n",
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(PendingAsk))]
[JsonSerializable(typeof(AskDecision))]
[JsonSerializable(typeof(CancelRequest))]
[JsonSerializable(typeof(RunReference))]
partial class ControlJson : JsonSerializerContext
{
    public static ControlJson Files { get; } = new(Json.Options(strict: true, indented: true));
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip,
    WriteIndented = true,
    NewLine = "\n",
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(RunInfo))]
[JsonSerializable(typeof(EffectiveConfig))]
[JsonSerializable(typeof(TaskRecord))]
[JsonSerializable(typeof(TaskResult))]
[JsonSerializable(typeof(LedgerLine))]
partial class LedgerJson : JsonSerializerContext
{
    public static LedgerJson Files { get; } = new(Json.Options(strict: false, indented: true));
    public static LedgerJson Events { get; } = new(Json.Options(strict: false, indented: false));
}

abstract class AnnotatedObject
{
    [JsonPropertyName("$schema")]
    public JsonElement? SchemaAnnotation { get => null; set => _ = value; }

    [JsonPropertyName("$comment")]
    public JsonElement? CommentAnnotation { get => null; set => _ = value; }
}

abstract class VersionedContract : AnnotatedObject, IJsonOnDeserialized
{
    [JsonRequired, JsonPropertyOrder(-10)]
    public int SchemaVersion { get; set; } = Constants.SchemaVersion;

    void IJsonOnDeserialized.OnDeserialized() => Json.CheckVersion(SchemaVersion);
}

static class Json
{
    public static JsonSerializerOptions Options(bool strict, bool indented) => new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        UnmappedMemberHandling = strict ? JsonUnmappedMemberHandling.Disallow : JsonUnmappedMemberHandling.Skip,
        WriteIndented = indented,
        NewLine = "\n",
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static void CheckVersion(int version)
    {
        if (version > Constants.SchemaVersion)
            throw new JsonException("written by a newer dispatch; update the plugin");
        if (version != Constants.SchemaVersion)
            throw new JsonException("schemaVersion must be 2; version 1 is not read");
    }

    public static TaskFile ReadTaskFile(string text)
    {
        using var doc = JsonDocument.Parse(text, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        if (doc.RootElement.ValueKind == JsonValueKind.Object &&
            (!doc.RootElement.TryGetProperty("schemaVersion", out var version) ||
             (version.ValueKind == JsonValueKind.Number && version.TryGetInt32(out var number) && number == 1)))
            throw new JsonException("task file v1 is not read. To convert it: add \"schemaVersion\": 2; add a kind to every task; add \"defaults\": { \"backend\": \"opencode\" }; rename maxMinutes to timeoutMinutes; delete fallback");
        return JsonSerializer.Deserialize(text, InputJson.Read.TaskFile) ?? throw new JsonException("task file is empty");
    }

    public static string Timestamp(TimeProvider? time = null) =>
        (time ?? TimeProvider.System).GetUtcNow().UtcDateTime.ToString("o", CultureInfo.InvariantCulture);

    public static void WriteFile<T>(string path, T value, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> info, Action<string>? beforeMove = null)
        => WriteBytes(path, Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value, info) + "\n"), beforeMove);

    public static void WriteBytes(string path, ReadOnlySpan<byte> bytes, Action<string>? beforeMove = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = $"{path}.tmp-{Environment.ProcessId}-{Guid.NewGuid():N}";
        var created = false;
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
            {
                created = true;
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            beforeMove?.Invoke(temp);
            var timer = Stopwatch.StartNew();
            while (true)
            {
                try
                {
                    File.Move(temp, path, overwrite: true);
                    break;
                }
                catch (Exception ex) when ((ex is IOException or UnauthorizedAccessException) && timer.Elapsed < TimeSpan.FromSeconds(2))
                {
                    Thread.Sleep(25);
                }
            }
        }
        finally
        {
            if (created) File.Delete(temp);
        }
    }

    public static JsonArray Array(IEnumerable<string> items) => new(items.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray());

    public static JsonObject? Error(SessionStructuredError? error) => error is null
        ? null
        : new JsonObject { ["type"] = error.Type, ["message"] = error.Message, ["status"] = error.Status };

    // A concrete type on purpose: Optional<T> offers no conversion from an interface-typed value.
    public static Dictionary<string, JsonElement> Metadata(string run, string task) => new(StringComparer.Ordinal)
    {
        ["dispatch.run"] = JsonSerializer.SerializeToElement(run, InputJson.Read.String),
        ["dispatch.task"] = JsonSerializer.SerializeToElement(task, InputJson.Read.String),
    };
}
