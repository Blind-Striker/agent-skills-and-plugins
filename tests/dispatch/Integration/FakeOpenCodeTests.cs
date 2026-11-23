using Dispatch.Tests.Support;
using Microsoft.Extensions.Time.Testing;
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
    [Timeout(60_000)]
    public async Task The_catalog_lists_models_before_agents(CancellationToken ct)
    {
        using var tree = new TempTree();
        await using var server = await OpenCodeServer.StartAsync(new OpenCodeServerOptions
        {
            Command = [FakeOpenCode.Executable, "serve"],
            WorkingDirectory = tree.Root,
            ReadinessTimeout = TimeSpan.FromSeconds(60),
            Environment = FakeOpenCode.Environment(tree, "one-task.jsonl"),
        }, ct);
        using var client = server.CreateClient();

        var catalog = await Catalog.LoadAsync(client, tree.Root, TimeSpan.FromSeconds(5), ct);

        var listings = File.ReadLines(FakeOpenCode.Record(tree)).Select(line => JsonNode.Parse(line)!)
            .Where(request => request["path"]!.GetValue<string>() is "/api/model" or "/api/agent")
            .Select(request => $"{request["method"]!.GetValue<string>()} {request["path"]!.GetValue<string>()}");
        await Assert.That(listings).IsEquivalentTo(new[] { "GET /api/model", "GET /api/agent" }, CollectionOrdering.Matching);
        await Assert.That(catalog.Agents.Select(agent => agent.Id))
            .IsEquivalentTo(new[] { "build" }, CollectionOrdering.Matching);
    }

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
    [Timeout(120_000)]
    public async Task A_stream_the_sdk_reconnects_internally_records_the_gap_and_polls_the_finished_task(CancellationToken ct)
    {
        using var tree = new TempTree();
        var repo = await LabRepo.CreateAsync(tree, ct);
        var scenario = Path.Combine(tree.Root, "reconnect.jsonl");
        File.WriteAllLines(scenario, ReconnectSteps(new() { ["decision"] = "reject", ["message"] = Policy.LeadDeclined })
            .Select(step => step.ToJsonString()));
        var environment = FakeOpenCode.Environment(tree, "one-task.jsonl").ToDictionary(x => x.Key, x => (string?)x.Value);
        environment["FAKE_OPENCODE_SCENARIO"] = scenario;
        var tasks = Path.Combine(tree.Root, "reconnect.tasks.jsonc");
        File.WriteAllText(tasks, """{"schemaVersion":2,"tasks":[{"id":"inspect","kind":"research","base":"main","backend":"opencode","route":"fixture/model","rawPrompt":true,"prompt":"Inspect."}]}""");
        using var child = new HarnessProcess(tree);
        (await child.RunAsync(["run", tasks, "--repo", repo.Root], ct, environment)).RequireSuccess("reconnect run");

        var folder = Directory.GetDirectories(Path.Combine(tree.DispatchHome, "state", "ledgers")).Single();
        var events = File.ReadLines(Path.Combine(folder, "events.jsonl")).Select(line => JsonNode.Parse(line)!).ToList();
        // Without the gap report no bus line is written and only the 30 s watchdog finds the task.
        await Assert.That(events.Select(line => line["type"]!.GetValue<string>())
                .Where(type => type is "bus" or "status" or "collected" or "final"))
            .IsEquivalentTo(new[] { "bus", "collected", "final" }, CollectionOrdering.Matching);
        await Assert.That(events.Where(line => line["type"]!.GetValue<string>() == "bus").Select(line => line["data"]!.ToJsonString()))
            .IsEquivalentTo(new[] { """{"state":"reconnected"}""" }, CollectionOrdering.Matching);
        var requests = File.ReadLines(FakeOpenCode.Record(tree)).Select(line => JsonNode.Parse(line)!).ToList();
        // The task's server sees the dropped stream, then the SDK's own resubscription and the recovery sweep.
        var host = requests.First(request => request["path"]!.GetValue<string>() == "/api/form")["pid"]!.GetValue<int>();
        await Assert.That(requests.Where(request => request["pid"]!.GetValue<int>() == host)
                .Select(request => request["path"]!.GetValue<string>()).Where(path => path is "/api/event" or "/api/form"))
            .IsEquivalentTo(new[] { "/api/event", "/api/form", "/api/event", "/api/form" }, CollectionOrdering.Matching);
        await Assert.That(requests.Where(request => request["unexpected"]!.GetValue<bool>())).IsEmpty();
    }

    [Test]
    [NotInParallel]
    [Timeout(120_000)]
    public async Task A_reconnected_stream_polls_at_once_without_waiting_for_the_watchdog(CancellationToken ct)
    {
        using var core = new CoreLab();
        var run = await core.TaskAsync("inspect", ct);
        await using var lab = new OpenCodeLab(core.Tree);
        lab.Steps.Clear();
        // The in-process policy allows the read that the subprocess run escalates and the lead declines.
        lab.Steps.AddRange(ReconnectSteps(new() { ["decision"] = "once" }).Select(step => step.AsObject()));
        lab.Write();
        var time = new FakeTimeProvider();
        var start = time.GetUtcNow();
        var scheduler = new Scheduler(new Dictionary<string, IWorkerBackend> { ["opencode"] = lab.Backend },
            core.Ledger, core.Tree.Repository, AllowList.From(null), core.Policy, 1, ct, core.Git, time);

        var running = scheduler.RunAsync([run]);
        // The clock moves only while the worker is live, so only the recovery poll can finish the task
        // before the watchdog's 30 s; without it, the watchdog alone finishes the task, too late.
        while (!running.IsCompleted)
        {
            if (run.State is not (TaskState.Pending or TaskState.Dispatching)) time.Advance(TimeSpan.FromMilliseconds(20));
            await Task.Delay(20, ct);
        }

        await Assert.That(await running).IsEqualTo(0);
        await Assert.That(time.GetUtcNow() - start).IsLessThan(Constants.Watchdog);
        await Assert.That(core.Ledger.Read().Where(line => line.Type is "bus" or "collected" or "final")
                .Select(line => line.Type == "bus" ? $"bus:{line.Data!.ToJsonString()}" : line.Type))
            .IsEquivalentTo(new[] { """bus:{"state":"reconnected"}""", "collected", "final" }, CollectionOrdering.Matching);
        await Assert.That(File.ReadLines(FakeOpenCode.Record(lab.Tree)).Select(line => JsonNode.Parse(line)!)
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
            ReadinessTimeout = TimeSpan.FromSeconds(60),
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
            ReadinessTimeout = TimeSpan.FromSeconds(60),
            Environment = FakeOpenCode.Environment(tree, "health.jsonl"),
        }, cancellationToken);
        using var client = server.CreateClient();

        var response = await client.Server.GetInfoAsync(cancellationToken: cancellationToken);

        await Assert.That(response.Status).IsEqualTo(200);
        await Assert.That(response.ServerInfo.Version).IsEqualTo("2.3.2");
        await Assert.That(response.ServerInfo.Pid).IsEqualTo((long)server.ProcessId);
        await Assert.That(server.OwnsProcess).IsTrue();
        using var unauthenticated = new HttpClient();
        using var denied = await unauthenticated.GetAsync(response.ServerInfo.Urls.Single() + "/api/info", cancellationToken);
        await Assert.That(denied.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    static List<JsonNode> ReconnectSteps(JsonObject decision)
    {
        var steps = File.ReadLines(FakeOpenCode.Fixture("one-task.jsonl")).Select(line => JsonNode.Parse(line)!).ToList();
        // The task finishes while the stream is down: its events are lost with the dropped stream.
        steps[9].AsObject().Remove("events");
        steps[9]["dropStreams"] = true;
        steps[8]["body"] = decision;
        // The recovery poll sweeps blockers again before its completion check.
        var noAsks = steps[7].DeepClone();
        noAsks["reply"]!["data"] = new JsonArray();
        var noForms = steps[9].DeepClone();
        noForms.AsObject().Remove("dropStreams");
        steps.InsertRange(10, [noAsks, noForms]);
        steps.Insert(2, steps[1].DeepClone());
        return steps;
    }
}
