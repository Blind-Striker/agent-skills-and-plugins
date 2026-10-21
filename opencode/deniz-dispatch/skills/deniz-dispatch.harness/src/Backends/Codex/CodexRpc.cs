using System.Text.Json.Serialization.Metadata;

sealed class CodexRpc : IAsyncDisposable
{
    const int MaxLineBytes = 64 * 1024 * 1024;
    const int MaxNotifications = 10_000;
    const int MaxHandlers = 64;
    readonly Stream stdin;
    readonly Stream stdout;
    readonly Func<CodexServerRequest, CancellationToken, Task<CodexReply>>? handler;
    readonly Action<string>? protocolError;
    readonly Func<string>? exitDetail;
    readonly SemaphoreSlim writer = new(1);
    readonly CancellationTokenSource lifetime = new();
    readonly ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> pending = new();
    readonly ConcurrentDictionary<long, Task> handlers = new();
    readonly Channel<CodexNotification> notifications = Channel.CreateBounded<CodexNotification>(new BoundedChannelOptions(MaxNotifications)
    {
        SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.Wait,
    });
    readonly object state = new();
    Exception? closed;
    long nextId;
    long nextHandler;

    public CodexRpc(Stream stdin, Stream stdout,
        Func<CodexServerRequest, CancellationToken, Task<CodexReply>>? handler = null,
        Action<string>? protocolError = null, Func<string>? exitDetail = null)
    {
        this.stdin = stdin;
        this.stdout = stdout;
        this.handler = handler;
        this.protocolError = protocolError;
        this.exitDetail = exitDetail;
        Completion = Task.Run(ReadLoopAsync);
    }

    public async Task<JsonElement> RequestAsync<T>(string method, T parameters, JsonTypeInfo<T> typeInfo,
        TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        var limit = timeout ?? TimeSpan.FromSeconds(method == "initialize" ? 30 : 60);
        if (limit <= TimeSpan.Zero || limit.TotalMilliseconds > uint.MaxValue - 1)
            throw new ArgumentOutOfRangeException(nameof(timeout));
        var id = Interlocked.Increment(ref nextId);
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (state)
        {
            if (closed is not null) throw closed;
            pending[id] = completion;
        }
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(limit);
        try
        {
            await WriteAsync(json =>
            {
                json.WriteNumber("id", id);
                json.WriteString("method", method);
                json.WritePropertyName("params");
                JsonSerializer.Serialize(json, parameters, typeInfo);
            }, deadline.Token);
            return await completion.Task.WaitAsync(deadline.Token);
        }
        catch (Exception) when (completion.Task.IsFaulted)
        {
            return await completion.Task;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && deadline.IsCancellationRequested)
        {
            throw new TimeoutException($"app-server request timed out: {method}");
        }
        finally
        {
            pending.TryRemove(id, out _);
        }
    }

    public ChannelReader<CodexNotification> Notifications => notifications.Reader;
    public Task Completion { get; }
    public Task NotifyAsync(string method, CancellationToken cancellationToken = default) =>
        WriteAsync(json => json.WriteString("method", method), cancellationToken);

    public Task NotifyAsync<T>(string method, T parameters, JsonTypeInfo<T> typeInfo,
        CancellationToken cancellationToken = default) => WriteAsync(json =>
        {
            json.WriteString("method", method);
            json.WritePropertyName("params");
            JsonSerializer.Serialize(json, parameters, typeInfo);
        }, cancellationToken);

    async Task WriteAsync(Action<Utf8JsonWriter> envelope, CancellationToken token)
    {
        lock (state) { if (closed is not null) throw closed; }
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer, new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            json.WriteStartObject();
            envelope(json);
            json.WriteEndObject();
        }
        buffer.WriteByte((byte)'\n');
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token, deadline.Token);
        var acquired = false;
        var writing = false;
        try
        {
            await writer.WaitAsync(linked.Token);
            acquired = true;
            lock (state) { if (closed is not null) throw closed; }
            writing = true;
            await stdin.WriteAsync(buffer.GetBuffer().AsMemory(0, checked((int)buffer.Length)), linked.Token).AsTask().WaitAsync(linked.Token);
            await stdin.FlushAsync(linked.Token).WaitAsync(linked.Token);
        }
        catch (OperationCanceledException ex) when (deadline.IsCancellationRequested)
        {
            var reason = new TimeoutException("app-server write timed out after 30 seconds", ex);
            Close(reason);
            throw reason;
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException)
        {
            // A partial frame cannot safely be followed by another message.
            if (writing) Close(new IOException("app-server input failed", ex));
            lock (state) { if (closed is not null) throw closed; }
            throw;
        }
        finally { if (acquired) writer.Release(); }
    }

    async Task ReadLoopAsync()
    {
        Exception reason = new EndOfStreamException("app-server exited");
        try
        {
            var bytes = new byte[16 * 1024];
            using var line = new MemoryStream();
            var oversized = false;
            while (true)
            {
                var count = await stdout.ReadAsync(bytes, lifetime.Token);
                if (count == 0) break;
                var start = 0;
                for (var i = 0; i < count; i++)
                {
                    if (bytes[i] != (byte)'\n') continue;
                    Append(bytes.AsSpan(start, i - start));
                    Consume();
                    start = i + 1;
                }
                Append(bytes.AsSpan(start, count - start));
            }
            if (line.Length > 0 || oversized) Consume();
            reason = new EndOfStreamException("app-server exited" + (exitDetail is null ? "" : $" ({exitDetail()})"));

            void Append(ReadOnlySpan<byte> part)
            {
                if (oversized) return;
                if (line.Length + part.Length > MaxLineBytes)
                {
                    line.Write(part[..Math.Min(part.Length, Math.Max(0, 800 - (int)Math.Min(line.Length, 800)))]);
                    line.SetLength(Math.Min(line.Length, 800));
                    oversized = true;
                    return;
                }
                line.Write(part);
            }

            void Consume()
            {
                var data = line.GetBuffer().AsMemory(0, checked((int)line.Length));
                if (oversized) Report("app-server line exceeds 64 MiB", data.Span);
                else Receive(data);
                line.SetLength(0);
                line.Position = 0;
                oversized = false;
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
            reason = closed ?? reason;
        }
        catch (Exception ex)
        {
            reason = new IOException($"app-server read failed: {ex.Message}" + (exitDetail is null ? "" : $" ({exitDetail()})"), ex);
        }
        finally
        {
            Close(reason);
            notifications.Writer.TryComplete();
        }
    }

    void Receive(ReadOnlyMemory<byte> line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw new JsonException("expected an object");
            var hasId = root.TryGetProperty("id", out var id);
            if (root.TryGetProperty("method", out var method))
            {
                var name = method.GetString() ?? throw new JsonException("expected a method");
                var parameters = root.TryGetProperty("params", out var p) ? p.Clone() : default;
                if (hasId)
                {
                    if (id.ValueKind != JsonValueKind.String && (id.ValueKind != JsonValueKind.Number || !id.TryGetInt64(out _)))
                        throw new JsonException("invalid request id");
                    StartHandler(new(id.Clone(), name, parameters));
                }
                else if (KnownNotification(name) && !notifications.Writer.TryWrite(new(name, parameters)))
                    throw new IOException($"app-server notification queue overflow ({MaxNotifications} messages)");
            }
            else if (hasId && id.ValueKind == JsonValueKind.Number && id.TryGetInt64(out var number))
            {
                if (root.TryGetProperty("error", out var error))
                {
                    var exception = new CodexRpcException(error.GetProperty("code").GetInt64(),
                        error.GetProperty("message").GetString()!, error.TryGetProperty("data", out var data) ? data.Clone() : null);
                    if (pending.TryRemove(number, out var waiting)) waiting.TrySetException(exception);
                }
                else if (root.TryGetProperty("result", out var result))
                {
                    if (pending.TryRemove(number, out var waiting)) waiting.TrySetResult(result.Clone());
                }
                else throw new JsonException("expected a response result or error");
            }
            else throw new JsonException("unrecognized envelope");
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        {
            Report("invalid app-server message", line.Span);
        }
    }

    void StartHandler(CodexServerRequest request)
    {
        // Only the reader adds handlers; completions may remove them concurrently.
        if (handlers.Count >= MaxHandlers)
            throw new IOException($"app-server server request overflow ({MaxHandlers} in flight)");
        var key = Interlocked.Increment(ref nextHandler);
        var task = Task.Run(async () =>
        {
            try
            {
                CodexReply reply;
                try
                {
                    reply = handler is null ? new(null, new(-32601, $"method not found: {request.Method}"))
                        : await handler(request, lifetime.Token).WaitAsync(lifetime.Token);
                    reply = reply.ValidateApproval(request.Method);
                }
                catch (CodexRpcException ex) { reply = new(null, new(ex.Code, ex.Message, ex.ErrorData)); }
                catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { return; }
                catch (Exception ex) { reply = new(null, new(-32603, ex.Message)); }
                await WriteAsync(json =>
                {
                    json.WritePropertyName("id");
                    request.Id.WriteTo(json);
                    if (reply.Error is { } error)
                    {
                        json.WritePropertyName("error");
                        JsonSerializer.Serialize(json, error, CodexJson.Wire.CodexRpcError);
                    }
                    else
                    {
                        json.WritePropertyName("result");
                        if (reply.Result is { } result) result.WriteTo(json);
                        else json.WriteNullValue();
                    }
                }, lifetime.Token);
            }
            catch (Exception ex) when (lifetime.IsCancellationRequested || ex is IOException or ObjectDisposedException)
            {
                // Close faults the pending callers; a reply after shutdown has no receiver.
                Close(new IOException("app-server reply failed", ex));
            }
        });
        handlers[key] = task;
        _ = task.ContinueWith(_ => handlers.TryRemove(key, out var ignored), CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    static bool KnownNotification(string method) => method is "turn/started" or "turn/completed" or "item/started" or "item/completed"
        or "thread/tokenUsage/updated" or "error" or "model/rerouted" or "serverRequest/resolved" or "warning" or "configWarning" or "windows/worldWritableWarning";

    void Report(string reason, ReadOnlySpan<byte> bytes)
    {
        var prefix = Encoding.UTF8.GetString(bytes[..Math.Min(bytes.Length, 800)]);
        protocolError?.Invoke($"{reason}: {prefix[..Math.Min(prefix.Length, 200)]}");
    }

    void Close(Exception reason)
    {
        lock (state)
        {
            if (closed is not null) return;
            closed = reason;
            foreach (var entry in pending)
                if (pending.TryRemove(entry.Key, out var completion)) completion.TrySetException(reason);
        }
        lifetime.Cancel();
    }

    public async ValueTask DisposeAsync()
    {
        Close(new ObjectDisposedException(nameof(CodexRpc)));
        await Completion;
        await Task.WhenAll(handlers.Values);
        // The process owner owns both streams, including the graceful stdin-close sequence.
    }
}
