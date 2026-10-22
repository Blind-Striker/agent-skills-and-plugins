using Dispatch.Tests.Support;

namespace Dispatch.Tests.Unit;

[Category("Unit")]
public sealed class CodexRpcTests
{
    [Test, Timeout(30_000)]
    public async Task Requests_match_responses_by_id(CancellationToken token)
    {
        await using var pipe = new CodexPipe();
        var pending = pipe.Client.RequestAsync("initialize", Element("{\"clientInfo\":{\"name\":\"test\",\"version\":\"1\"}}"), CodexJson.Wire.JsonElement, cancellationToken: token);
        var request = await pipe.ReadAsync(token);
        await Assert.That(request.GetProperty("method").GetString()).IsEqualTo("initialize");
        await Assert.That(request.TryGetProperty("jsonrpc", out _)).IsFalse();
        await Assert.That(request.GetProperty("params").GetProperty("clientInfo").GetProperty("name").GetString()).IsEqualTo("test");
        await pipe.SendAsync($"{{\"id\":{request.GetProperty("id")},\"result\":{{\"value\":42}}}}", token);
        await Assert.That((await pending).GetProperty("value").GetInt32()).IsEqualTo(42);
    }

    internal static JsonElement Element(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    [Test, Timeout(30_000)]
    public async Task Out_of_order_responses_complete_their_own_requests(CancellationToken token)
    {
        await using var pipe = new CodexPipe();
        var first = pipe.Client.RequestAsync("thread/start", Element("{}"), CodexJson.Wire.JsonElement, cancellationToken: token);
        var second = pipe.Client.RequestAsync("turn/start", Element("{}"), CodexJson.Wire.JsonElement, cancellationToken: token);
        var a = await pipe.ReadAsync(token);
        var b = await pipe.ReadAsync(token);
        await Assert.That(a.GetProperty("id").GetInt64()).IsNotEqualTo(b.GetProperty("id").GetInt64());
        await pipe.SendAsync($"{{\"id\":{b.GetProperty("id")},\"result\":\"second\"}}", token);
        await Assert.That((await second).GetString()).IsEqualTo("second");
        await Assert.That(first.IsCompleted).IsFalse();
        await pipe.SendAsync($"{{\"id\":{a.GetProperty("id")},\"result\":\"first\"}}", token);
        await Assert.That((await first).GetString()).IsEqualTo("first");
    }

    [Test, Timeout(30_000)]
    public async Task Error_response_preserves_code_message_and_data(CancellationToken token)
    {
        await using var pipe = new CodexPipe();
        var pending = pipe.Client.RequestAsync("turn/start", Element("{}"), CodexJson.Wire.JsonElement, cancellationToken: token);
        var request = await pipe.ReadAsync(token);
        await pipe.SendAsync($"{{\"id\":{request.GetProperty("id")},\"error\":{{\"code\":-32000,\"message\":\"turn ended\",\"data\":{{\"turnId\":\"t\"}}}}}}", token);
        var error = await Assert.ThrowsAsync<CodexRpcException>(async () => await pending)
            ?? throw new InvalidOperationException("expected an RPC error");
        await Assert.That(error.Code).IsEqualTo(-32000);
        await Assert.That(error.Message).IsEqualTo("turn ended");
        await Assert.That(error.ErrorData!.Value.GetProperty("turnId").GetString()).IsEqualTo("t");
    }

    [Test, Timeout(60_000)]
    [Arguments("\"approval-id\"")]
    [Arguments("9223372036854775807")]
    public async Task A_held_server_request_does_not_block_the_reader_and_echoes_its_id(string id, CancellationToken token)
    {
        var entered = new TaskCompletionSource<CodexServerRequest>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<CodexReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var pipe = new CodexPipe(async (request, ct) =>
        {
            entered.SetResult(request);
            return await release.Task.WaitAsync(ct);
        });
        await pipe.SendAsync($"{{\"id\":{id},\"method\":\"item/commandExecution/requestApproval\",\"params\":{{\"threadId\":\"child\"}}}}", token);
        var serverRequest = await entered.Task.WaitAsync(token);
        // Cross the write deadline while an operator decision is still pending.
        await Task.Delay(TimeSpan.FromSeconds(31), token);
        await pipe.SendAsync("{\"method\":\"turn/completed\",\"params\":{\"threadId\":\"main\"}}", token);
        await Assert.That((await pipe.Client.Notifications.ReadAsync(token)).Params.GetProperty("threadId").GetString()).IsEqualTo("main");
        await Assert.That(serverRequest.Params.GetProperty("threadId").GetString()).IsEqualTo("child");
        release.SetResult(new(Element("{\"decision\":\"decline\"}")));
        var reply = await pipe.ReadAsync(token);
        await Assert.That(reply.GetProperty("id").GetRawText()).IsEqualTo(id);
        await Assert.That(reply.GetProperty("result").GetProperty("decision").GetString()).IsEqualTo("decline");
        await Assert.That(reply.TryGetProperty("jsonrpc", out _)).IsFalse();
    }

    [Test, Timeout(30_000)]
    [Arguments(long.MinValue)]
    [Arguments(long.MaxValue)]
    public async Task Error_codes_preserve_int64_boundaries(long code, CancellationToken token)
    {
        await using var pipe = new CodexPipe((_, _) => Task.FromException<CodexReply>(new CodexRpcException(code, "handler boundary", Element("{\"detail\":42}"))));
        var pending = pipe.Client.RequestAsync("turn/start", Element("{}"), CodexJson.Wire.JsonElement,
            timeout: TimeSpan.FromMilliseconds(500), cancellationToken: token);
        var request = await pipe.ReadAsync(token);
        await pipe.SendAsync($"{{\"id\":{request.GetProperty("id")},\"error\":{{\"code\":{code},\"message\":\"boundary\"}}}}", token);
        var error = await Assert.ThrowsAsync<CodexRpcException>(async () => await pending)
            ?? throw new InvalidOperationException("expected an RPC error");
        await Assert.That(error.Code).IsEqualTo(code);
        await pipe.SendAsync("{\"id\":\"server\",\"method\":\"test/error\",\"params\":{}}", token);
        var reply = await pipe.ReadAsync(token);
        await Assert.That(reply.GetProperty("error").GetProperty("code").GetInt64()).IsEqualTo(code);
        await Assert.That(reply.GetProperty("error").GetProperty("message").GetString()).IsEqualTo("handler boundary");
        await Assert.That(reply.GetProperty("error").GetProperty("data").GetProperty("detail").GetInt32()).IsEqualTo(42);
    }

    [Test, Timeout(30_000)]
    public async Task Notifications_keep_their_wire_order(CancellationToken token)
    {
        await using var pipe = new CodexPipe();
        foreach (var method in new[] { "turn/started", "item/completed", "turn/completed" })
            await pipe.SendAsync($"{{\"method\":\"{method}\",\"params\":{{\"threadId\":\"thread\"}}}}", token);
        var methods = new List<string>();
        for (var i = 0; i < 3; i++) methods.Add((await pipe.Client.Notifications.ReadAsync(token)).Method);
        await Assert.That(string.Join(",", methods)).IsEqualTo("turn/started,item/completed,turn/completed");
    }

    [Test, Timeout(30_000)]
    public async Task End_of_stream_fails_all_pending_and_future_requests(CancellationToken token)
    {
        await using var pipe = new CodexPipe();
        var first = pipe.Client.RequestAsync("initialize", Element("{}"), CodexJson.Wire.JsonElement, cancellationToken: token);
        var second = pipe.Client.RequestAsync("thread/start", Element("{}"), CodexJson.Wire.JsonElement, cancellationToken: token);
        await pipe.ReadAsync(token);
        await pipe.ReadAsync(token);
        await pipe.EndAsync();
        await pipe.Client.Completion.WaitAsync(token);
        await Assert.That(async () => await first).Throws<EndOfStreamException>();
        await Assert.That(async () => await second).Throws<EndOfStreamException>();
        await Assert.That(async () => await pipe.Client.RequestAsync("late", Element("{}"), CodexJson.Wire.JsonElement, cancellationToken: token)).Throws<EndOfStreamException>();
        await pipe.Client.Notifications.Completion.WaitAsync(token);
    }

    [Test, Timeout(30_000)]
    public async Task Turkish_text_round_trips_as_unescaped_utf8_without_a_bom(CancellationToken token)
    {
        const string text = "İstanbul, ıhlamur, çığ, öğüt, şüphe — Türkçe\nikinci satır";
        await using var pipe = new CodexPipe();
        var parameters = Element("{\"text\":\"İstanbul, ıhlamur, çığ, öğüt, şüphe — Türkçe\\nikinci satır\"}");
        var pending = pipe.Client.RequestAsync("turn/steer", parameters, CodexJson.Wire.JsonElement, cancellationToken: token);
        var line = await pipe.ReadLineAsync(token);
        await Assert.That(line).StartsWith("{");
        await Assert.That(line).Contains("İstanbul, ıhlamur, çığ, öğüt, şüphe — Türkçe");
        using var request = JsonDocument.Parse(line);
        await pipe.SendAsync($"{{\"id\":{request.RootElement.GetProperty("id")},\"result\":{parameters.GetRawText()}}}", token);
        await Assert.That(Convert.ToHexString(Encoding.UTF8.GetBytes((await pending).GetProperty("text").GetString()!)))
            .IsEqualTo(Convert.ToHexString(Encoding.UTF8.GetBytes(text)));
    }

    [Test, Timeout(30_000)]
    public async Task Unknown_server_requests_get_method_not_found(CancellationToken token)
    {
        await using var pipe = new CodexPipe();
        await pipe.SendAsync("{\"id\":\"unknown\",\"method\":\"future/request\",\"params\":{}}", token);
        var reply = await pipe.ReadAsync(token);
        await Assert.That(reply.GetProperty("id").GetString()).IsEqualTo("unknown");
        await Assert.That(reply.GetProperty("error").GetProperty("code").GetInt32()).IsEqualTo(-32601);
    }

    [Test, Timeout(30_000)]
    public async Task Invalid_lines_are_reported_and_skipped(CancellationToken token)
    {
        var errors = new List<string>();
        await using var pipe = new CodexPipe(protocolError: errors.Add);
        await pipe.SendAsync("not JSON", token);
        await pipe.SendAsync("{\"method\":\"turn/completed\",\"params\":{}}", token);
        await Assert.That((await pipe.Client.Notifications.ReadAsync(token)).Method).IsEqualTo("turn/completed");
        await Assert.That(errors.Single()).Contains("not JSON");
    }

    [Test, Timeout(30_000)]
    public async Task End_of_stream_captures_exit_details_at_shutdown_not_at_start(CancellationToken token)
    {
        var detail = "starting";
        await using var pipe = new CodexPipe(exitDetail: () => detail);
        var pending = pipe.Client.RequestAsync("initialize", Element("{}"), CodexJson.Wire.JsonElement, cancellationToken: token);
        await pipe.ReadAsync(token);
        await pipe.SendAsync("{\"method\":\"turn/started\",\"params\":{}}", token);
        await pipe.Client.Notifications.ReadAsync(token);
        detail = "exit 7; diagnostic tail";
        await pipe.EndAsync();
        var error = await Assert.ThrowsAsync<EndOfStreamException>(async () => await pending)
            ?? throw new InvalidOperationException("expected an end-of-stream error");
        await Assert.That(error.Message).Contains("exit 7; diagnostic tail");
    }

    [Test, Timeout(30_000)]
    public async Task Read_failure_fails_all_pending_requests_with_current_exit_details(CancellationToken token)
    {
        var detail = "starting";
        await using var pipe = new CodexPipe(exitDetail: () => detail);
        var first = pipe.Client.RequestAsync("initialize", Element("{}"), CodexJson.Wire.JsonElement, cancellationToken: token);
        var second = pipe.Client.RequestAsync("thread/start", Element("{}"), CodexJson.Wire.JsonElement, cancellationToken: token);
        await pipe.ReadAsync(token);
        await pipe.ReadAsync(token);
        detail = "exit 9; stderr tail: broken output";
        var cause = new IOException("output pipe failed");
        await pipe.FailAsync(cause);
        foreach (var pending in new[] { first, second })
        {
            var error = await Assert.ThrowsAsync<IOException>(async () => await pending)
                ?? throw new InvalidOperationException("expected a read error");
            await Assert.That(error.Message).Contains("exit 9; stderr tail: broken output");
            await Assert.That(error.InnerException).IsSameReferenceAs(cause);
        }
        await pipe.Client.Completion.WaitAsync(token);
    }

    [Test, Timeout(30_000)]
    public async Task Notification_overflow_fails_the_connection_without_blocking_the_reader(CancellationToken token)
    {
        await using var pipe = new CodexPipe();
        var pending = pipe.Client.RequestAsync("turn/start", Element("{}"), CodexJson.Wire.JsonElement, cancellationToken: token);
        await pipe.ReadAsync(token);
        await pipe.SendAsync(string.Join('\n', Enumerable.Repeat("{\"method\":\"error\"}", 10_001)), token);
        var error = await Assert.ThrowsAsync<IOException>(async () => await pending.WaitAsync(TimeSpan.FromSeconds(2), token))
            ?? throw new InvalidOperationException("expected notification overflow");
        await Assert.That(error.Message).Contains("notification");
        await Assert.That(error.Message).Contains("10000");
        await pipe.Client.Completion.WaitAsync(token);
        var count = 0;
        while (pipe.Client.Notifications.TryRead(out _)) count++;
        await Assert.That(count).IsEqualTo(10_000);
    }

    [Test, Timeout(30_000)]
    public async Task Server_request_overflow_fails_the_connection_without_starting_another_handler(CancellationToken token)
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<CodexReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        var count = 0;
        await using var pipe = new CodexPipe(async (_, ct) =>
        {
            if (Interlocked.Increment(ref count) == 64) entered.SetResult();
            return await release.Task.WaitAsync(ct);
        });
        var pending = pipe.Client.RequestAsync("turn/start", Element("{}"), CodexJson.Wire.JsonElement, cancellationToken: token);
        await pipe.ReadAsync(token);
        for (var id = 1; id <= 64; id++)
            await pipe.SendAsync($"{{\"id\":{id},\"method\":\"item/commandExecution/requestApproval\",\"params\":{{}}}}", token);
        await entered.Task.WaitAsync(token);
        await pipe.SendAsync("{\"id\":65,\"method\":\"item/commandExecution/requestApproval\",\"params\":{}}", token);
        var error = await Assert.ThrowsAsync<IOException>(async () => await pending.WaitAsync(TimeSpan.FromSeconds(2), token))
            ?? throw new InvalidOperationException("expected server request overflow");
        await Assert.That(error.Message).Contains("server request");
        await Assert.That(error.Message).Contains("64");
        await pipe.Client.Completion.WaitAsync(token);
        await Assert.That(count).IsEqualTo(64);
    }

    [Test, Timeout(60_000)]
    [Arguments("notification")]
    [Arguments("notificationWithParams")]
    [Arguments("request")]
    [Arguments("reply")]
    public async Task A_server_that_never_reads_times_out_every_write_and_fails_all_pending_requests(string kind, CancellationToken token)
    {
        var payload = Element(JsonSerializer.Serialize(new string('x', 128 * 1024), CodexJson.Wire.String));
        await using var pipe = new CodexPipe((_, _) => Task.FromResult(new CodexReply(payload)));
        var first = pipe.Client.RequestAsync("thread/start", Element("{}"), CodexJson.Wire.JsonElement, cancellationToken: token);
        var second = pipe.Client.RequestAsync("turn/start", Element("{}"), CodexJson.Wire.JsonElement, cancellationToken: token);
        await pipe.ReadAsync(token);
        await pipe.ReadAsync(token);
        Task? write = null;
        if (kind == "reply")
            await pipe.SendAsync("{\"id\":\"server\",\"method\":\"test/reply\",\"params\":{}}", token);
        else
            write = kind switch
            {
                "notification" => pipe.Client.NotifyAsync(new string('x', 128 * 1024), token),
                "notificationWithParams" => pipe.Client.NotifyAsync("test/notification", payload, CodexJson.Wire.JsonElement, token),
                _ => pipe.Client.RequestAsync("test/request", payload, CodexJson.Wire.JsonElement, cancellationToken: token),
            };
        foreach (var pending in new[] { first, second })
        {
            var error = await Assert.ThrowsAsync<TimeoutException>(async () => await pending.WaitAsync(TimeSpan.FromSeconds(35), token))
                ?? throw new InvalidOperationException("expected a write timeout");
            await Assert.That(error.Message).Contains("write timed out");
        }
        if (write is not null)
        {
            var error = await Assert.ThrowsAsync<TimeoutException>(async () => await write)
                ?? throw new InvalidOperationException("expected a write timeout");
            await Assert.That(error.Message).Contains("write timed out");
        }
        await pipe.Client.Completion.WaitAsync(token);
        await Assert.That(async () => await pipe.Client.NotifyAsync("late", token)).Throws<TimeoutException>();
    }

    [Test, Timeout(30_000)]
    [Arguments("item/commandExecution/requestApproval", "{\"decision\":\"acceptForSession\"}")]
    [Arguments("item/fileChange/requestApproval", "{\"decision\":\"acceptForSession\"}")]
    [Arguments("item/commandExecution/requestApproval", "{\"decision\":{\"acceptWithExecpolicyAmendment\":{\"execpolicy_amendment\":[\"tool\"]}}}")]
    [Arguments("item/fileChange/requestApproval", "{\"decision\":{\"acceptWithExecpolicyAmendment\":{\"execpolicy_amendment\":[\"tool\"]}}}")]
    [Arguments("item/commandExecution/requestApproval", "{\"decision\":{\"applyNetworkPolicyAmendment\":{\"network_policy_amendment\":{\"host\":\"example.invalid\",\"action\":\"allow\"}}}}")]
    [Arguments("item/fileChange/requestApproval", "{\"decision\":{\"applyNetworkPolicyAmendment\":{\"network_policy_amendment\":{\"host\":\"example.invalid\",\"action\":\"allow\"}}}}")]
    [Arguments("item/permissions/requestApproval", "{\"permissions\":{},\"scope\":\"session\"}")]
    [Arguments("item/permissions/requestApproval", "{\"permissions\":{},\"scope\":\"thread\"}")]
    [Arguments("item/permissions/requestApproval", "{\"permissions\":{},\"scope\":null}")]
    [Arguments("item/permissions/requestApproval", "{\"permissions\":{}}")]
    [Arguments("item/permissions/requestApproval", "{\"permissions\":{},\"scope\":\"turn\",\"scope\":\"session\"}")]
    [Arguments("item/commandExecution/requestApproval", "{\"decision\":\"accept\",\"acceptForSession\":true}")]
    [Arguments("item/fileChange/requestApproval", "{\"decision\":0}")]
    [Arguments("item/commandExecution/requestApproval", "null")]
    [Arguments("execCommandApproval", "{\"decision\":\"approved_for_session\"}")]
    [Arguments("applyPatchApproval", "{\"decision\":\"approved_for_session\"}")]
    [Arguments("execCommandApproval", "{\"decision\":{\"approved_execpolicy_amendment\":{\"proposed_execpolicy_amendment\":[\"tool\"]}}}")]
    [Arguments("applyPatchApproval", "{\"decision\":{\"approved_execpolicy_amendment\":{\"proposed_execpolicy_amendment\":[\"tool\"]}}}")]
    [Arguments("execCommandApproval", "{\"decision\":{\"network_policy_amendment\":{\"network_policy_amendment\":{\"host\":\"example.invalid\",\"action\":\"allow\"}}}}")]
    [Arguments("applyPatchApproval", "{\"decision\":\"approved_mcp_policy_amendment\"}")]
    public async Task Approval_replies_reject_persistent_or_open_forms(string method, string result, CancellationToken token)
    {
        var safe = method == "item/permissions/requestApproval" ? "{\"permissions\":{},\"scope\":\"turn\"}"
            : method is "execCommandApproval" or "applyPatchApproval" ? "{\"decision\":{\"denied\":{\"rejection\":\"not permitted\"}}}"
            : "{\"decision\":\"accept\"}";
        await using var pipe = new CodexPipe((request, _) => Task.FromResult(new CodexReply(
            Element(request.Id.GetString() == "unsafe" ? result : safe))));
        await pipe.SendAsync($"{{\"id\":\"unsafe\",\"method\":\"{method}\",\"params\":{{}}}}", token);
        var refused = await pipe.ReadAsync(token);
        await Assert.That(refused.GetProperty("id").GetString()).IsEqualTo("unsafe");
        await Assert.That(refused.TryGetProperty("result", out _)).IsFalse();
        await Assert.That(refused.GetProperty("error").GetProperty("code").GetInt64()).IsEqualTo(-32603L);
        await Assert.That(refused.GetProperty("error").GetProperty("message").GetString()).Contains("approval reply");
        await pipe.SendAsync($"{{\"id\":\"safe\",\"method\":\"{method}\",\"params\":{{}}}}", token);
        var accepted = await pipe.ReadAsync(token);
        await Assert.That(accepted.GetProperty("id").GetString()).IsEqualTo("safe");
        await Assert.That(accepted.GetProperty("result").GetRawText()).IsEqualTo(safe);
    }
}
