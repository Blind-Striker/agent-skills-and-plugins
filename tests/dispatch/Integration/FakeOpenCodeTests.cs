using Dispatch.Tests.Support;
using OpenCode.Sdk;
using System.Net;
using System.Text.Json.Nodes;

namespace Dispatch.Tests.Integration;

[Category("Integration")]
[NotInParallel("processes")]
public sealed class FakeOpenCodeTests
{
    [Test]
    [Timeout(60_000)]
    public async Task The_fake_keeps_quiet_event_streams_alive_without_reconnecting(CancellationToken ct)
    {
        using var tree = new TempTree();
        await using var server = await OpenCodeServer.StartAsync(new OpenCodeServerOptions
        {
            Command = [FakeOpenCode.Executable, "serve"],
            WorkingDirectory = tree.Root,
            Environment = FakeOpenCode.Environment(tree, "one-task.jsonl"),
        }, ct);
        // Shorten the SDK's idle window, not the server's real keepalive cadence.
        using var client = server.CreateClient(options => options.EventStreamIdleTimeout = TimeSpan.FromSeconds(20));
        using var subscription = CancellationTokenSource.CreateLinkedTokenSource(ct);
        await using var stream = client.Events.SubscribeAsync(cancellationToken: subscription.Token).GetAsyncEnumerator();
        await Assert.That(await stream.MoveNextAsync()).IsTrue();
        await Assert.That(stream.Current).IsTypeOf<OpenCode.Sdk.Models.EventServerConnected>();
        var next = stream.MoveNextAsync().AsTask();
        // Observe two keepalives and exceed the idle window; silence must not cause another GET.
        try { await Task.Delay(TimeSpan.FromSeconds(32), ct); }
        finally
        {
            await subscription.CancelAsync();
            try { await next; }
            catch (OperationCanceledException) when (subscription.IsCancellationRequested) { /* Owned shutdown. */ }
        }

        var events = File.ReadAllLines(Path.Combine(tree.Root, "requests.jsonl"))
            .Select(line => JsonNode.Parse(line)!)
            .Where(request => request["path"]!.GetValue<string>() == "/api/event").ToArray();
        await Assert.That(events.Length).IsEqualTo(1);
        await Assert.That(events[0]["authenticated"]!.GetValue<bool>()).IsTrue();
        await Assert.That(events[0]["unexpected"]!.GetValue<bool>()).IsFalse();
    }

    [Test]
    [Timeout(180_000)]
    public async Task The_fake_starts_through_the_sdk_and_answers_server_info(CancellationToken cancellationToken)
    {
        using var tree = new TempTree();
        await Assert.That(File.Exists(FakeOpenCode.Executable)).IsTrue();
        await using var server = await OpenCodeServer.StartAsync(new OpenCodeServerOptions
        {
            Command = [FakeOpenCode.Executable, "serve"],
            WorkingDirectory = tree.Root,
            Environment = FakeOpenCode.Environment(tree, "health.jsonl"),
        }, cancellationToken);
        using var client = server.CreateClient();

        var response = await client.Server.GetInfoAsync(cancellationToken: cancellationToken);

        await Assert.That(response.Status).IsEqualTo(200);
        await Assert.That(response.ServerInfo.Version).IsEqualTo("2.1.4");
        await Assert.That(response.ServerInfo.Pid).IsEqualTo((long)server.ProcessId);
        await Assert.That(server.OwnsProcess).IsTrue();
        using var unauthenticated = new HttpClient();
        using var denied = await unauthenticated.GetAsync(response.ServerInfo.Urls.Single() + "/api/info", cancellationToken);
        await Assert.That(denied.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }
}
