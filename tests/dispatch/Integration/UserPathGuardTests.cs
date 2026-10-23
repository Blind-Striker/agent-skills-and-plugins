using Dispatch.Tests.Support;

namespace Dispatch.Tests.Integration;

[Category("Integration")]
[NotInParallel]
public sealed class UserPathGuardTests
{
    [Test]
    [Timeout(30_000)]
    public async Task Run_OversizedUserPath_RefusesBeforeBackendOrRunClaim(CancellationToken ct)
    {
        await using var lab = new OpenCodeLab();
        var repo = await LabRepo.CreateAsync(lab.Tree, ct);
        var tasks = Tasks(lab.Tree);
        var started = false;
        var backend = Backend();
        var exit = await Commands.RunAsync(Args.Parse(["run", tasks, "--repo", repo.Root]), ct,
            (_, _) => throw new IOException("setup interrupted"),
            () => { started = true; return backend; }, () => new string('x', 16001));
        var error = TestContext.Current!.Output.GetErrorOutput();
        await Assert.That(exit).IsEqualTo(2);
        await Assert.That(started).IsFalse();
        await Assert.That(Directory.Exists(Path.Combine(Locations.LedgerRoot, Commands.CreateRunId("path-check")))).IsFalse();
        await Assert.That(error).Contains(@"HKCU\Environment\Path");
        await Assert.That(error).Contains("16001");
        await Assert.That(error).Contains("reg query HKCU\\Environment /v Path");
    }

    [Test]
    [Timeout(60_000)]
    public async Task SelfCheck_OversizedUserPath_WarnsWithoutFailingChecks(CancellationToken ct)
    {
        await using var lab = new OpenCodeLab();
        var repo = await LabRepo.CreateAsync(lab.Tree, ct);
        var empty = JsonNode.Parse("""{"location":{"directory":"$directory"},"data":[]}""")!;
        var fixture = File.ReadLines(FakeOpenCode.Fixture("one-task.jsonl")).Select(x => JsonNode.Parse(x)!).ToArray();
        var session = fixture[11]["reply"]!.DeepClone();
        session["data"]!["permissions"] = new JsonArray(new JsonObject
        {
            ["action"] = "edit", ["resource"] = Path.Combine(lab.Tree.DispatchHome, "state", "control", "**").Replace('\\', '/'), ["effect"] = "deny",
        });
        lab.Add("GET /api/mcp", empty.DeepClone());
        lab.Add("POST /api/session", session.DeepClone());
        lab.Add("GET /api/session/ses_fixture", session.DeepClone());
        lab.Steps.Add(fixture[10].DeepClone().AsObject());
        foreach (var endpoint in new[] { "permission", "form" })
            lab.Add($"GET /api/session/ses_fixture/{endpoint}", empty.DeepClone());
        lab.Add("DELETE /api/session/ses_fixture", status: 204);
        lab.Add("GET /api/session/ses_fixture", new JsonObject { ["_tag"] = "SessionNotFoundError", ["sessionID"] = "ses_fixture", ["message"] = "removed" }, status: 404);
        // The selfcheck launch check starts and releases one worker after the checks above.
        lab.Add("POST /api/session", OpenCodeLab.Session("ses_launch"));
        lab.Add("PUT /api/session/ses_fixture/environment", status: 204);
        lab.Add("PUT /api/session/ses_launch/environment", status: 204);
        lab.Add("DELETE /api/session/ses_launch", status: 204);
        lab.Write();
        var exit = await Commands.SelfCheckAsync(Args.Parse(["selfcheck", "--repo", repo.Root]), ct, () => new string('x', 8001));
        var error = TestContext.Current!.Output.GetErrorOutput();
        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(error).Contains("warning");
        await Assert.That(error).Contains(@"HKCU\Environment\Path");
        await Assert.That(error).Contains("8001");
        await Assert.That(error).Contains("reg query");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    [Timeout(30_000)]
    public async Task Run_ReportsPathChangeAfterBackendShutdown(bool abort, CancellationToken ct)
    {
        await using var lab = new OpenCodeLab();
        var repo = await LabRepo.CreateAsync(lab.Tree, ct);
        var tasks = Tasks(lab.Tree);
        var backend = Backend();
        var current = "keep;old";
        var stopped = false;
        backend.OnDispose = () => { current = "keep;new"; stopped = true; return ValueTask.CompletedTask; };
        backend.OnPrompt = (input, _) =>
        {
            var worker = input.Worker with { Turn = "turn" };
            backend.Emit(new WorkerSignal.Ended(worker.TaskId, worker.Turn!, "succeeded", null));
            return Task.FromResult(worker);
        };
        var reads = 0;
        var exit = await Commands.RunAsync(Args.Parse(["run", tasks, "--repo", repo.Root]), ct,
            abort ? (_, _) => throw new IOException("setup interrupted") : null, () => backend,
            () => { reads++; return current; });
        var output = TestContext.Current!.Output.GetStandardOutput();
        await Assert.That(exit).IsEqualTo(abort ? 1 : 0);
        await Assert.That(stopped).IsTrue();
        await Assert.That(reads).IsEqualTo(2);
        var ledger = new Ledger(Commands.CreateRunId("path-check"));
        var lines = ledger.Read();
        await Assert.That(lines.Last().Type).IsEqualTo("warning");
        var data = lines.Last().Data!;
        await Assert.That(data["added"]!.AsArray().Single()!.GetValue<string>()).IsEqualTo("new");
        await Assert.That(data["removed"]!.AsArray().Single()!.GetValue<string>()).IsEqualTo("old");
        await Assert.That(output).Contains("warning");
        await Assert.That(output).Contains("new");
        await Assert.That(output).Contains("old");
    }

    static string Tasks(TempTree tree)
    {
        var tasks = Path.Combine(tree.Root, "tasks.jsonc");
        File.WriteAllText(tasks, """{"schemaVersion":2,"run":"path-check","defaults":{"backend":"opencode"},"tasks":[{"id":"inspect","kind":"research","base":"main","route":"fixture/model","prompt":"Inspect."}]}""");
        return tasks;
    }

    static FakeBackend Backend() => new("opencode", new("policy", "policy", "text", true))
    {
        OnProbe = (_, _) => Task.FromResult(new BackendProbe(true, "fixture", [], [])),
        OnValidate = (plans, _) => Task.FromResult<IReadOnlyList<LiveCheck>>(plans.Select(x => new LiveCheck(x.TaskId, [], [], "fixture", "full", null)).ToList()),
        OnCreate = (launch, _) => Task.FromResult(new CreateResult(new("opencode", launch.TaskId, "worker", null, new()), new("none", "none", "policy"))),
        OnPoll = (_, _) => Task.FromResult(new PollResult([])),
        OnCollect = (_, _) => Task.FromResult(new WorkerReport("succeeded", "Report", new(), null, true, [], null, true)),
        OnRelease = (_, _) => Task.CompletedTask,
        OnFindOrphans = (_, _) => Task.FromResult<IReadOnlyList<WorkerRef>>([]),
    };
}
