using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Channels;

internal sealed class Replay(JsonObject[] steps, Wire wire, string home, string scenarioPath)
{
    readonly Channel<(byte[] Raw, JsonObject Message)> incoming = Channel.CreateUnbounded<(byte[], JsonObject)>();
    readonly Stream stdout = Console.OpenStandardOutput();
    readonly Dictionary<string, JsonNode?> variables = new()
    {
        ["$threadId"] = JsonValue.Create("thread-main"),
        ["$otherThreadId"] = JsonValue.Create("thread-other"),
        ["$turnId"] = JsonValue.Create("turn-main"),
        ["$cwd"] = JsonValue.Create(Environment.CurrentDirectory),
        ["$outside"] = JsonValue.Create(Path.GetDirectoryName(Environment.CurrentDirectory)),
        ["$codexHome"] = JsonValue.Create(home),
        ["$platformOs"] = JsonValue.Create(OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "macos" : "linux"),
        ["$platformFamily"] = JsonValue.Create(OperatingSystem.IsWindows() ? "windows" : "unix"),
    };
    long requestId;
    PosixSignalRegistration? termRegistration;

    public async Task<int> RunAsync(int offset)
    {
        // Keep recording during sleep/hang, including ignored interrupts. Exactly one reader owns stdin.
        _ = PumpAsync();
        for (var index = offset; index < steps.Length; index++)
        {
            var original = steps[index];
            if (original["os"] is { } os && os.GetValue<string>() != variables["$platformOs"]!.GetValue<string>()) continue;
            if (original["expect"] is { } method)
            {
                var (raw, message) = await ReceiveAsync();
                var hasReply = original.ContainsKey("reply") || original.ContainsKey("error");
                variables["$id"] = message["id"]?.DeepClone();
                var step = Expand(original)!.AsObject();
                if (message["method"]?.GetValue<string>() != method.GetValue<string>()
                    || message.ContainsKey("jsonrpc") || message.ContainsKey("id") != hasReply
                    || (step.ContainsKey("params") && !Matches(step["params"], message["params"])))
                    throw Unexpected(raw, message, $"expected {method}");
                if (hasReply)
                {
                    if (message["id"] is not JsonValue id || !(id.TryGetValue<long>(out _) || id.TryGetValue<string>(out _)))
                        throw Unexpected(raw, message, "request id must be an integer or string");
                    var member = step.ContainsKey("error") ? "error" : "result";
                    await SendAsync(new JsonObject { ["id"] = message["id"]!.DeepClone(), [member] = step[member == "result" ? "reply" : "error"]?.DeepClone() });
                }
                continue;
            }
            var action = Expand(original)!.AsObject();
            if (action["notify"] is { } notification)
                await SendAsync(new JsonObject { ["method"] = notification.DeepClone(), ["params"] = action["params"]?.DeepClone() });
            else if (action["request"] is { } request)
            {
                var id = JsonValue.Create($"server-{++requestId}");
                variables["$id"] = id.DeepClone();
                await SendAsync(new JsonObject { ["id"] = id, ["method"] = request.DeepClone(), ["params"] = action["params"]?.DeepClone() });
                if (action["defer"]?.GetValue<bool>() == true) continue;
                var (raw, response) = await ReceiveAsync();
                if (!JsonNode.DeepEquals(response["id"], id) || response.ContainsKey("method") || response.ContainsKey("jsonrpc")
                    || response.ContainsKey("result") == response.ContainsKey("error"))
                    throw Unexpected(raw, response, "expected server request response");
                if (action.ContainsKey("result") && !JsonNode.DeepEquals(action["result"], response["result"]))
                    throw Unexpected(raw, response, "server request result differs");
            }
            else if (action["writeFile"] is { } file)
            {
                var path = file["path"]!.GetValue<string>();
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
                await File.WriteAllBytesAsync(path, Convert.FromBase64String(file["base64"]!.GetValue<string>()));
            }
            else if (action["sleep"] is { } delay) await Task.Delay(delay.GetValue<int>());
            else if (action["stderr"] is { } stderr)
            {
                var bytes = Encoding.UTF8.GetBytes(stderr.GetValue<string>());
                await Console.OpenStandardError().WriteAsync(bytes);
            }
            else if (action["termMarker"] is { } marker)
            {
                if (OperatingSystem.IsWindows()) throw new InvalidDataException("SIGTERM requires Unix");
                termRegistration?.Dispose();
                termRegistration = PosixSignalRegistration.Create(PosixSignal.SIGTERM, _ =>
                {
                    File.WriteAllText(marker.GetValue<string>(), "SIGTERM\n");
                    Environment.Exit(0);
                });
            }
            else if (action["exit"] is { } exit) return exit.GetValue<int>();
            else if (action["hang"]?.GetValue<bool>() == true)
            {
                // Ignore EOF too: stopping tests must actually kill this process.
                await foreach (var ignored in incoming.Reader.ReadAllAsync()) { _ = ignored; }
                await Task.Delay(Timeout.Infinite);
            }
            else if (action["spawnChild"]?.GetValue<bool>() == true)
                return await SpawnAsync(index + 1, action["setsid"]?.GetValue<bool>() == true, action["orphan"]?.GetValue<bool>() == true);
            else throw new InvalidDataException($"unknown scenario step {index + 1}");
        }
        // Normal EOF succeeds only after every step; extra client frames are always errors.
        await foreach (var (raw, message) in incoming.Reader.ReadAllAsync())
            throw Unexpected(raw, message, "scenario exhausted");
        return 0;
    }

    async Task PumpAsync()
    {
        try
        {
            await foreach (var raw in Wire.Lines(Console.OpenStandardInput()))
            {
                JsonNode? parsed;
                try
                {
                    parsed = JsonNode.Parse(new UTF8Encoding(false, true).GetString(raw));
                }
                catch (Exception exception) when (exception is System.Text.Json.JsonException or DecoderFallbackException)
                {
                    wire.Record("in", raw, null);
                    throw;
                }
                wire.Record("in", raw, parsed);
                var message = parsed as JsonObject ?? throw new InvalidDataException("client frame must be an object");
                await incoming.Writer.WriteAsync((raw, message));
            }
            incoming.Writer.TryComplete();
        }
        catch (Exception exception)
        {
            wire.Unexpected(exception.Message);
            incoming.Writer.TryComplete(exception);
        }
    }

    async Task<(byte[] Raw, JsonObject Message)> ReceiveAsync()
    {
        if (await incoming.Reader.WaitToReadAsync()) return await incoming.Reader.ReadAsync();
        throw new InvalidDataException("client EOF before scenario completed");
    }

    async Task SendAsync(JsonObject message)
    {
        var raw = Encoding.UTF8.GetBytes(message.ToJsonString(Wire.Json) + "\n");
        wire.Record("out", raw, message);
        await stdout.WriteAsync(raw);
        await stdout.FlushAsync();
    }

    InvalidDataException Unexpected(byte[] raw, JsonObject message, string reason)
    {
        wire.Record("unexpected", raw, message);
        return new InvalidDataException($"{reason}: {message.ToJsonString()}");
    }

    async Task<int> SpawnAsync(int offset, bool newSession, bool orphan)
    {
        // Record alongside this process, never in the immutable fixture folder.
        var record = Environment.GetEnvironmentVariable("FAKE_CODEX_RECORD")!;
        var directory = Path.Combine(record, $"child-{Environment.ProcessId}-{offset}");
        var start = new ProcessStartInfo(Environment.ProcessPath ?? throw new InvalidOperationException("apphost path unavailable"))
        {
            UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true,
            CreateNoWindow = true,
        };
        foreach (var argument in new[] { "--child", scenarioPath, offset.ToString(System.Globalization.CultureInfo.InvariantCulture), directory, newSession ? "setsid" : "inherited" })
            start.ArgumentList.Add(argument);
        using var child = Process.Start(start) ?? throw new IOException("could not spawn child");
        if (orphan)
        {
            // Forward readiness once, then exit without waiting for the child or its pipes.
            using var ready = new StreamReader(child.StandardOutput.BaseStream, new UTF8Encoding(false, true));
            var line = await ready.ReadLineAsync() ?? throw new IOException("orphan child did not report readiness");
            await SendAsync(JsonNode.Parse(line)!.AsObject());
            return 0;
        }
        using var forwardingCancellation = new CancellationTokenSource();
        var input = ForwardAsync();
        var output = CopyOutputAsync();
        await child.WaitForExitAsync();
        forwardingCancellation.Cancel();
        await input;
        await output;
        return child.ExitCode;

        async Task ForwardAsync()
        {
            try
            {
                await foreach (var (raw, _) in incoming.Reader.ReadAllAsync(forwardingCancellation.Token))
                {
                    await child.StandardInput.BaseStream.WriteAsync(raw, forwardingCancellation.Token);
                    await child.StandardInput.BaseStream.FlushAsync(forwardingCancellation.Token);
                }
                child.StandardInput.Close();
            }
            catch (OperationCanceledException) when (forwardingCancellation.IsCancellationRequested)
            {
                // The child has exited, so forwarding no longer owns a live consumer.
                return;
            }
            catch (IOException) when (child.HasExited)
            {
                // A scenario may intentionally exit without consuming the queued client frames.
                return;
            }
        }

        async Task CopyOutputAsync()
        {
            await foreach (var raw in Wire.Lines(child.StandardOutput.BaseStream))
            {
                wire.Record("out", raw, JsonNode.Parse(raw));
                await stdout.WriteAsync(raw);
                await stdout.FlushAsync();
            }
        }
    }

    static bool Matches(JsonNode? expected, JsonNode? actual) => expected switch
    {
        JsonObject obj => actual is JsonObject value && obj.All(pair => value.ContainsKey(pair.Key) && Matches(pair.Value, value[pair.Key])),
        _ => JsonNode.DeepEquals(expected, actual),
    };

    JsonNode? Expand(JsonNode? node) => node switch
    {
        JsonValue scalar when scalar.TryGetValue<string>(out var text) => ExpandText(text),
        JsonObject obj => new JsonObject(obj.Select(pair => new KeyValuePair<string, JsonNode?>(pair.Key, Expand(pair.Value)))),
        JsonArray array => new JsonArray(array.Select(Expand).ToArray()),
        _ => node?.DeepClone(),
    };

    JsonNode? ExpandText(string text)
    {
        if (variables.TryGetValue(text, out var value)) return value?.DeepClone();
        foreach (var (key, replacement) in variables.OrderByDescending(pair => pair.Key.Length))
            if (replacement is JsonValue scalar && scalar.TryGetValue<string>(out var part)) text = text.Replace(key, part, StringComparison.Ordinal);
        return JsonValue.Create(text);
    }
}
