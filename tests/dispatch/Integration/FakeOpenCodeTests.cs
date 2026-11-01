using Dispatch.Tests.Support;
using OpenCode.Sdk;
using System.Net;
using System.Text.Json.Nodes;
using TUnit.Assertions.Enums;

namespace Dispatch.Tests.Integration;

[Category("Integration")]
[NotInParallel("processes")]
public sealed class FakeOpenCodeTests
{
    [Test]
    [NotInParallel]
    [Arguments("permission.asked")]
    [Arguments("form.created")]
    [Arguments("session.execution.succeeded")]
    [Timeout(60_000)]
    public async Task Undecodable_control_events_reach_the_ledger_and_other_unknown_events_are_ignored(string type, CancellationToken ct)
    {
        using var tree = new TempTree();
        var repo = await LabRepo.CreateAsync(tree, ct);
        var steps = File.ReadLines(FakeOpenCode.Fixture("one-task.jsonl")).Select(line => JsonNode.Parse(line)!).ToList();
        steps[6]["events"] = new JsonArray(
            OpenCodeLab.Event("unrelated.future", new()),
            OpenCodeLab.Event(type, new() { ["sessionID"] = 1 }));
        steps[8]["body"] = new JsonObject { ["decision"] = "reject", ["message"] = Policy.LeadDeclined };
        var scenario = Path.Combine(tree.Root, "unknown-events.jsonl");
        File.WriteAllLines(scenario, steps.Select(step => step.ToJsonString()));
        var environment = FakeOpenCode.Environment(tree, "one-task.jsonl").ToDictionary(x => x.Key, x => (string?)x.Value);
        environment["FAKE_OPENCODE_SCENARIO"] = scenario;
        var tasks = Path.Combine(tree.Root, "events.tasks.jsonc");
        File.WriteAllText(tasks, """{"schemaVersion":2,"tasks":[{"id":"inspect","kind":"research","base":"main","backend":"opencode","route":"fixture/model","rawPrompt":true,"prompt":"Inspect."}]}""");
        using var child = new HarnessProcess(tree);
        (await child.RunAsync(["run", tasks, "--repo", repo.Root], ct, environment)).RequireSuccess("unknown-event run");
        var folder = Directory.GetDirectories(Path.Combine(tree.DispatchHome, "state", "ledgers")).Single();
        var events = File.ReadLines(Path.Combine(folder, "events.jsonl")).Select(line => JsonNode.Parse(line)!).ToList();
        await Assert.That(events.Where(line => line["type"]!.GetValue<string>() == "error")
            .Select(line => $"{line["task"]}:{line["data"]!["while"]}:{line["data"]!["message"]}"))
            .IsEquivalentTo(new[] { $":unknown-event:{type}" }, CollectionOrdering.Matching);
        await Assert.That(events.Where(line => line["type"]!.GetValue<string>() == "tool")
            .Select(line => $"{line["task"]}:{line["data"]!["tool"]}:{line["data"]!["outcome"]}"))
            .IsEquivalentTo(new[] { "inspect:read:succeeded" }, CollectionOrdering.Matching);
        await Assert.That(File.ReadLines(FakeOpenCode.Record(tree)).Select(line => JsonNode.Parse(line)!)
            .Where(line => line["unexpected"]!.GetValue<bool>())).IsEmpty();
    }

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

        var events = File.ReadAllLines(FakeOpenCode.Record(tree))
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
        await Assert.That(response.ServerInfo.Version).IsEqualTo("2.2.5");
        await Assert.That(response.ServerInfo.Pid).IsEqualTo((long)server.ProcessId);
        await Assert.That(server.OwnsProcess).IsTrue();
        using var unauthenticated = new HttpClient();
        using var denied = await unauthenticated.GetAsync(response.ServerInfo.Urls.Single() + "/api/info", cancellationToken);
        await Assert.That(denied.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }
}
