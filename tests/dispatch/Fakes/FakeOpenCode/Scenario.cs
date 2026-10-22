using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;

// JSONL rules are consumed once (or repeated with repeat:true), independently per method/path.
// body/query are subset expectations. reply/status define HTTP output; stream opens SSE, with
// initial events. events on an ordinary response are broadcast after its body is flushed.
// This keeps concurrency request-driven, with no sleeps or assumed model execution times.
internal sealed class Scenario : IDisposable
{
    readonly JsonObject[] steps;
    readonly HashSet<JsonObject> consumed = [];
    readonly object gate = new();
    readonly SemaphoreSlim recordGate = new(1);
    readonly string record;
    readonly ConcurrentDictionary<Guid, Channel<JsonNode?>> streams = new();
    readonly ConcurrentDictionary<string, string> locations = new(StringComparer.Ordinal);
    readonly Dictionary<string, FileStream> directoryHandles = new(StringComparer.Ordinal);

    public Scenario(string path, string record)
    {
        steps = File.ReadLines(path).Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => JsonNode.Parse(line)!.AsObject()).ToArray();
        this.record = record;
    }

    public async Task HandleAsync(HttpContext context, string url)
    {
        var request = context.Request;
        var ct = context.RequestAborted;
        var body = request.ContentLength is > 0 ? await JsonNode.ParseAsync(request.Body, cancellationToken: ct) : null;
        var query = new JsonObject(request.Query.Select(pair => new KeyValuePair<string, JsonNode?>(pair.Key, JsonValue.Create(pair.Value.ToString()))));
        var path = request.Path.Value!;
        var sessionPath = path.Split('/');
        var directory = body?["location"]?["directory"]?.GetValue<string>()
            ?? request.Query["location[directory]"].FirstOrDefault()
            ?? (sessionPath.Length > 3 && locations.TryGetValue(sessionPath[3], out var saved) ? saved : Environment.CurrentDirectory);
        var variables = new Dictionary<string, JsonNode?>
        {
            ["$pid"] = JsonValue.Create(Environment.ProcessId),
            ["$url"] = JsonValue.Create(url),
            ["$cwd"] = JsonValue.Create(Environment.CurrentDirectory),
            ["$directory"] = JsonValue.Create(directory),
        };
        JsonObject? selected = null;
        lock (gate)
        {
            if (Environment.GetEnvironmentVariable("FAKE_OPENCODE_HOLD_DIRECTORY") == "1" && !directoryHandles.ContainsKey(directory))
            {
                directoryHandles[directory] = File.Open(Path.Combine(directory, $"held-{Environment.ProcessId}"), FileMode.Create, FileAccess.ReadWrite, FileShare.Read);
                File.WriteAllText($"{record}.{Environment.ProcessId}.directory.json", JsonSerializer.Serialize(directory));
            }
            // A mismatched body is an unexpected request, not a fallback to a success response.
            var step = steps.FirstOrDefault(step => !consumed.Contains(step) && step["expect"]!.GetValue<string>() == $"{request.Method} {path}");
            if (step is not null)
            {
                var expanded = Expand(step, variables)!.AsObject();
                if (Matches(expanded["body"], body) && Matches(expanded["query"], query))
                {
                    selected = expanded;
                    if (step["repeat"]?.GetValue<bool>() != true) consumed.Add(step);
                }
            }
        }
        await RecordAsync(context, body, authenticated: true, unexpected: selected is null);
        if (selected is null)
        {
            context.Response.StatusCode = 500;
            await context.Response.WriteAsJsonAsync(new { unexpected = $"{request.Method} {path}" }, ct);
            return;
        }

        context.Response.StatusCode = selected["status"]?.GetValue<int>() ?? 200;
        foreach (var frame in selected["beforeEvents"]?.AsArray() ?? [])
            foreach (var channel in streams.Values) await channel.Writer.WriteAsync(frame!, ct);
        if (selected["arrived"]?.GetValue<string>() is { } arrived) await File.WriteAllTextAsync(arrived, "arrived", ct);
        if (selected["waitFor"]?.GetValue<string>() is { } release)
            while (!File.Exists(release)) await Task.Delay(10, ct);
        if (selected["stream"]?.GetValue<bool>() == true)
        {
            context.Response.ContentType = "text/event-stream";
            var id = Guid.NewGuid();
            var channel = Channel.CreateUnbounded<JsonNode?>();
            streams[id] = channel;
            using var streamLifetime = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var keepalive = KeepAliveAsync(channel.Writer, streamLifetime.Token);
            try
            {
                foreach (var frame in selected["events"]?.AsArray() ?? []) await WriteFrameAsync(context, frame!);
                await foreach (var frame in channel.Reader.ReadAllAsync(ct)) await WriteFrameAsync(context, frame);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // The client's owned server/stream is closing; the subscription ends here.
                return;
            }
            finally
            {
                streams.TryRemove(id, out _);
                await streamLifetime.CancelAsync();
                try { await keepalive; }
                catch (OperationCanceledException) when (streamLifetime.IsCancellationRequested) { /* Owned shutdown. */ }
                channel.Writer.TryComplete();
            }
            return;
        }

        if (request.Method == "POST" && path == "/api/session" && selected["reply"]?["data"]?["id"] is { } sessionId)
            locations[sessionId.GetValue<string>()] = directory;
        if (selected["reply"] is { } reply) await context.Response.WriteAsJsonAsync(reply, ct);
        await context.Response.Body.FlushAsync(ct);
        foreach (var frame in selected["events"]?.AsArray() ?? [])
            foreach (var channel in streams.Values) await channel.Writer.WriteAsync(frame!, ct);
    }

    public void Dispose()
    {
        foreach (var handle in directoryHandles.Values) handle.Dispose();
    }

    public async Task RecordAsync(HttpContext context, JsonNode? body, bool authenticated, bool unexpected)
    {
        var safeBody = body?.DeepClone();
        if (safeBody?["variables"] is JsonObject environment)
            foreach (var name in environment.Select(pair => pair.Key).ToArray())
                if (name is not ("TEMP" or "TMP" or "TMPDIR")) environment[name] = "<redacted>";
        // No headers or host environment values are persisted, especially the SDK lease credential.
        var line = JsonSerializer.Serialize(new
        {
            pid = Environment.ProcessId,
            method = context.Request.Method,
            path = context.Request.Path.Value,
            query = context.Request.QueryString.Value,
            body = safeBody,
            authenticated,
            unexpected,
        });
        await recordGate.WaitAsync(context.RequestAborted);
        try { await File.AppendAllTextAsync(record, line + "\n", context.RequestAborted); }
        finally { recordGate.Release(); }
    }

    static async Task KeepAliveAsync(ChannelWriter<JsonNode?> stream, CancellationToken ct)
    {
        // Match the real server's quiet-stream cadence. The reader serializes these with events.
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
        while (await timer.WaitForNextTickAsync(ct)) await stream.WriteAsync(null, ct);
    }

    static async Task WriteFrameAsync(HttpContext context, JsonNode? frame)
    {
        await context.Response.WriteAsync(frame is null ? ": keepalive\n\n" : $"data: {frame.ToJsonString()}\n\n", context.RequestAborted);
        await context.Response.Body.FlushAsync(context.RequestAborted);
    }

    static bool Matches(JsonNode? expected, JsonNode? actual) => expected switch
    {
        null => true,
        JsonObject obj => actual is JsonObject value && obj.All(pair => value.ContainsKey(pair.Key) && Matches(pair.Value, value[pair.Key])),
        _ => JsonNode.DeepEquals(expected, actual),
    };

    static JsonNode? Expand(JsonNode? node, IReadOnlyDictionary<string, JsonNode?> variables) => node switch
    {
        JsonValue scalar when scalar.TryGetValue<string>(out var text) && variables.TryGetValue(text, out var value) => value?.DeepClone(),
        JsonObject obj => new JsonObject(obj.Select(pair => new KeyValuePair<string, JsonNode?>(pair.Key, Expand(pair.Value, variables)))),
        JsonArray array => new JsonArray(array.Select(item => Expand(item, variables)).ToArray()),
        _ => node?.DeepClone(),
    };
}
