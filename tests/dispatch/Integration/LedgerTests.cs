using System.Text.Json.Nodes;
using Dispatch.Tests.Support;

namespace Dispatch.Tests.Integration;

[Category("Integration")]
[NotInParallel("processes")]
public sealed class LedgerTests
{
    [Test]
    [Timeout(180_000)]
    public async Task Append_WhileThirdPartyHoldsReadOnlyShare_RetriesThenSucceeds(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows()) Skip.Test("Windows file sharing is required");
        using var tree = new TempTree();
        var ledger = new Ledger(tree.Root);
        ledger.Write(null, "queued");
        var holder = new FileStream(Path.Combine(tree.Root, "events.jsonl"), FileMode.Open, FileAccess.Read, FileShare.Read);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var writing = Task.Run(() =>
        {
            started.SetResult();
            ledger.Write(null, "resumed");
        }, cancellationToken);
        try
        {
            await started.Task.WaitAsync(cancellationToken);
            // Exercise a transient sharing violation for the specified 500 ms reader lifetime.
            await Task.Delay(500, cancellationToken);
        }
        finally
        {
            holder.Dispose();
            await writing;
        }
        await Assert.That(ledger.Read().Select(line => line.Type)).IsEquivalentTo(["queued", "resumed"]);
    }

    [Test]
    [Timeout(180_000)]
    public async Task Repair_CompleteJsonWithoutNewline_NeverBecomesAnEvent(CancellationToken cancellationToken)
    {
        using var tree = new TempTree();
        var path = Path.Combine(tree.Root, "events.jsonl");
        File.WriteAllText(path, "{\"type\":\"queued\",\"task\":\"inspect\"}\n{\"type\":\"final\",\"task\":\"phantom\"}");
        var ledger = new Ledger(tree.Root);
        await Assert.That(ledger.Read().Select(line => line.Type)).IsEquivalentTo(["queued"]);
        ledger.Write(null, "resumed");
        await Assert.That(ledger.Read().Select(line => line.Type)).IsEquivalentTo(["queued", "ledger-repaired", "resumed"]);

        using var child = new HarnessProcess(tree);
        var result = await child.RunAsync(["status", tree.Root], cancellationToken);
        result.RequireSuccess("status after torn event repair");
        await Assert.That(result.Stdout).Contains("inspect");
        await Assert.That(result.Stdout).DoesNotContain("phantom");
    }

    [Test]
    [Timeout(180_000)]
    public Task Supervisor_KilledMidRun_ResumeReadsLedger(CancellationToken cancellationToken) =>
        RecoverAfterCrash(false, cancellationToken);

    [Test]
    [Timeout(180_000)]
    public Task Repair_InterruptedMarker_NextProcessRecovers(CancellationToken cancellationToken) =>
        RecoverAfterCrash(true, cancellationToken);

    static async Task RecoverAfterCrash(bool interruptRepair, CancellationToken cancellationToken)
    {
        using var tree = new TempTree();
        var repo = await LabRepo.CreateAsync(tree, cancellationToken);
        var config = Directory.CreateDirectory(Path.Combine(tree.DispatchHome, "config")).FullName;
        await File.WriteAllTextAsync(Path.Combine(config, "config.jsonc"), """{"schemaVersion":2,"preferences":{"backend":"opencode"},"restrictions":{"routes":["opencode:fixture/model"],"deny":[{"name":"no-edits","action":"edit","resources":["*"]}]},"instructions":["Report findings only."]}""", cancellationToken);
        var tasks = Path.Combine(tree.Root, "tasks.jsonc");
        await File.WriteAllTextAsync(tasks, """{"schemaVersion":2,"run":"recover","tasks":[{"id":"inspect","kind":"research","base":"main","route":"fixture/model","prompt":"Inspect the lab repository."}]}""", cancellationToken);
        var scenario = Path.Combine(tree.Root, "running.jsonl");
        var steps = File.ReadAllLines(FakeOpenCode.Fixture("one-task.jsonl")).Select(line => JsonNode.Parse(line)!.AsObject()).ToArray();
        steps.Single(step => step["expect"]!.GetValue<string>() == "GET /api/form").Remove("events");
        await File.WriteAllLinesAsync(scenario, steps.Select(step => step.ToJsonString()), cancellationToken);
        var environment = FakeOpenCode.Environment(tree, "one-task.jsonl").ToDictionary(x => x.Key, x => (string?)x.Value);
        environment["FAKE_OPENCODE_SCENARIO"] = scenario;
        using var running = new HarnessProcess(tree);
        var execution = running.RunAsync(["run", tasks, "--repo", repo.Root, "--max", "1"], cancellationToken, environment);
        var ledgers = Path.Combine(tree.DispatchHome, "state", "ledgers");
        string? folder = null;
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(25));
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                if (execution.IsCompleted)
                    throw new InvalidOperationException($"harness exited before the kill point: {await execution}");
                folder = Directory.Exists(ledgers) ? Directory.GetDirectories(ledgers).SingleOrDefault() : null;
                if (folder is null) continue;
                var eventPath = Path.Combine(folder, "events.jsonl");
                if (!File.Exists(eventPath)) continue;
                using var stream = new FileStream(eventPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream);
                if ((await reader.ReadToEndAsync(cancellationToken)).Contains("\"type\":\"prompt\"", StringComparison.Ordinal)) break;
            }
        }
        finally
        {
            running.Kill();
            await execution;
        }

        var runFolder = folder ?? throw new InvalidOperationException("no run folder at the kill point");
        var events = Path.Combine(runFolder, "events.jsonl");
        // Inject the crash window deterministically rather than hoping a process kill tears a write.
        await File.AppendAllTextAsync(events, "{\"type\":", cancellationToken);
        if (interruptRepair)
        {
            new Ledger(runFolder).Write(null, "resuming");
            var content = File.ReadAllText(events);
            var marker = content.LastIndexOf("{\"ts\":", content.IndexOf("\"type\":\"ledger-repaired\"", StringComparison.Ordinal), StringComparison.Ordinal);
            // Model death during the marker append, after the atomic tail removal completed.
            await File.WriteAllTextAsync(events, content[..marker] + "{\"type\":\"ledger-repaired\",\"data\":", cancellationToken);
        }
        using var resumed = new HarnessProcess(tree);
        var result = await resumed.RunAsync(["resume", Path.GetFileName(runFolder)], cancellationToken,
            FakeOpenCode.Environment(tree, "one-task.jsonl").ToDictionary(x => x.Key, x => (string?)x.Value));

        result.RequireSuccess("resume after supervisor termination");
        var recovered = new Ledger(runFolder);
        await Assert.That(recovered.Read().Count(line => line.Type == "ledger-repaired")).IsEqualTo(1);
        await Assert.That(recovered.TaskStates()["inspect"]).IsEqualTo("done");
        await Assert.That(recovered.LoadRunInfo().Run).IsEqualTo(Path.GetFileName(runFolder));
        await Assert.That(recovered.LoadRecord("inspect").Session.Id).IsEqualTo("ses_fixture");
        await Assert.That(recovered.LoadResult("inspect")?.State).IsEqualTo("done");
        var sidecars = Directory.GetFiles(runFolder, "events.torn-*.jsonl");
        await Assert.That(sidecars.Length).IsEqualTo(interruptRepair ? 2 : 1);
        await Assert.That(sidecars.Select(File.ReadAllText)).Contains("{\"type\":");
        if (interruptRepair)
            await Assert.That(sidecars.Select(File.ReadAllText)).Contains("{\"type\":\"ledger-repaired\",\"data\":");
    }
}
