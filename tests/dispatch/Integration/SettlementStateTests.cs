using Dispatch.Tests.Support;
using TUnit.Assertions.Enums;

namespace Dispatch.Tests.Integration;

[Category("Integration")]
[NotInParallel]
public sealed class SettlementStateTests
{
    [Test]
    [Arguments(false, "succeeded")]
    [Arguments(true, "succeeded")]
    [Arguments(false, "failed")]
    [Arguments(true, "failed")]
    [Timeout(30_000)]
    public async Task Settlement_records_ordered_usage_without_changing_totals(bool structured, string outcome, CancellationToken ct)
    {
        using var lab = new CoreLab();
        var run = await lab.TaskAsync("inspect", ct);
        if (structured) lab.Structured(run);
        run.SaveWorker(new("opencode", run.Id, "worker", "turn", new()), lab.Ledger);
        lab.Backend.OnCollect = (_, _) => Task.FromResult(new WorkerReport(outcome,
            structured ? StructuredLab.Valid : "Report\n\nStatus: done",
            new() { Input = 901, CacheRead = 21, Output = 73, Reasoning = 11, CacheWrite = 8 }, 1.25, true,
            ["fixture/model#default"], null, true, Calls:
            [new() { Input = 17, CacheRead = 3, Output = 7, Reasoning = 2, CacheWrite = 8 }, new() { Input = 0, Reasoning = 0 }]));

        var before = DateTimeOffset.UtcNow;
        await Settlement.SettleAsync(lab.Backend, lab.Ledger, run, null, ct, lab.Git);
        var after = DateTimeOffset.UtcNow;
        var events = lab.Ledger.Read();
        var usage = events.Where(x => x.Type == "usage").ToArray();
        await Assert.That(usage.Select(x => x.Data!.ToJsonString()).ToArray()).IsEquivalentTo(new[]
        {
            """{"input":17,"cacheRead":3,"output":7,"reasoning":2}""", """{"input":0,"reasoning":0}""",
        }, CollectionOrdering.Matching);
        await Assert.That(usage.Select(x => x.Task).ToArray()).IsEquivalentTo(new string?[] { "inspect", "inspect" }, CollectionOrdering.Matching);
        await Assert.That(usage.Select(x => x.Run).ToArray()).IsEquivalentTo(new[] { lab.Ledger.RunId, lab.Ledger.RunId }, CollectionOrdering.Matching);
        await Assert.That(events.TakeLast(3).Select(x => x.Type).ToArray())
            .IsEquivalentTo(new[] { "usage", "usage", "outcome" }, CollectionOrdering.Matching);
        foreach (var entry in usage)
        {
            await Assert.That(DateTimeOffset.Parse(entry.Ts)).IsGreaterThanOrEqualTo(before.AddSeconds(-1));
            await Assert.That(DateTimeOffset.Parse(entry.Ts)).IsLessThanOrEqualTo(after);
        }
        var saved = lab.Ledger.LoadResult(run.Id)!;
        await Assert.That(saved.Tokens!.Input).IsEqualTo(901);
        await Assert.That(saved.Tokens.CacheRead).IsEqualTo(21);
        await Assert.That(saved.Tokens.Output).IsEqualTo(73);
        await Assert.That(saved.Tokens.Reasoning).IsEqualTo(11);
        await Assert.That(saved.Tokens.CacheWrite).IsEqualTo(8);
        await Assert.That(saved.Cost).IsEqualTo(1.25);
        await Assert.That(saved.TokensComplete).IsTrue();
        await Assert.That(saved.State).IsEqualTo(outcome == "succeeded" ? "done" : "failed");
        await Assert.That(saved.Failures).IsEquivalentTo(outcome == "succeeded" ? Array.Empty<string>() : ["outcome:failed"], CollectionOrdering.Matching);
    }

    [Test]
    [Arguments("verify", "failed", "verify-failed")]
    [Arguments("scope", "failed", "out-of-scope")]
    [Arguments("both", "failed", "out-of-scope,verify-failed")]
    [Arguments("no-final", "failed", "no-final-text")]
    [Arguments("outcome", "failed", "outcome:failed")]
    [Arguments("execution-error", "failed", "settle-error")]
    [Arguments("structured", "failed", "invalid-output")]
    [Arguments("drift", "failed", "route-drift")]
    [Arguments("advisory", "done", "")]
    [Arguments("blocked", "failed", "worker-stopped")]
    [Arguments("stopped", "failed", "worker-stopped")]
    [Arguments("cancelled", "cancelled", "")]
    [Arguments("success", "done", "")]
    [Timeout(30_000)]
    public async Task Settlement_records_every_failed_condition(string condition, string state, string failures, CancellationToken ct)
    {
        using var lab = new CoreLab();
        var run = await lab.TaskAsync("inspect", ct);
        if (condition is "verify" or "both") run.Record.Spec.Verify = ["exit 0", "exit 3"];
        if (condition == "advisory") (run.Record.Spec.Verify, run.Record.Spec.AdvisoryVerify) = (["exit 0"], ["exit 3"]);
        if (condition == "structured") lab.Structured(run);
        if (condition == "cancelled") run.CancelRequested = true;
        run.SaveWorker(new("opencode", run.Id, "worker", "turn", new()), lab.Ledger);
        lab.Backend.OnCollect = (_, _) =>
        {
            if (condition is "scope" or "both") File.WriteAllText(Path.Combine(lab.Tree.Repository, "outside.txt"), "outside scope");
            return Task.FromResult(new WorkerReport(condition == "outcome" ? "failed" : "succeeded",
                condition switch
                {
                    "no-final" => " \n",
                    "blocked" => "Blocked: this session exposes no shell tool.\n\nStatus: blocked",
                    "stopped" => "Stopped: the plan contradicts the scope.\n\n**Status: stopped**",
                    _ => "Report\n\nStatus: done",
                }, new(), null, true,
                condition == "drift" ? ["fixture/model#default", "opencode/flint-40b-free#default"] : ["fixture/model#default"],
                condition == "execution-error" ? "backend execution failed" : null, true));
        };

        var result = await Settlement.SettleAsync(lab.Backend, lab.Ledger, run, null, ct, lab.Git);

        await Assert.That(result.State).IsEqualTo(state);
        await Assert.That(result.Failures.Order().ToArray()).IsEquivalentTo(failures.Split(',', StringSplitOptions.RemoveEmptyEntries));
        var saved = lab.Ledger.LoadResult(run.Id)!;
        await Assert.That(saved.State).IsEqualTo(state);
        await Assert.That(saved.Failures).IsEquivalentTo(result.Failures);
        await Assert.That(saved.RouteVerified).IsEqualTo(condition != "drift");
        await Assert.That(saved.WorkerStatus).IsEqualTo(condition switch
        { "no-final" => null, "blocked" or "stopped" => condition, "structured" => null, _ => "done" });
        if (condition == "advisory") await Assert.That(saved.Warnings!).IsEquivalentTo(new[] { "advisory-verify-failed" });
        else await Assert.That(saved.Warnings).IsNull();
        if (condition == "drift") await Assert.That(saved.RouteDrift!).IsEquivalentTo(new[] { "opencode/flint-40b-free#default" });
        else await Assert.That(saved.RouteDrift).IsNull();
        if (condition is "verify" or "both" or "advisory")
            await Assert.That(result.Verify.Select(x => x.ExitCode).ToArray()).IsEquivalentTo(new[] { 0, 3 });
        if (condition == "advisory")
            await Assert.That(result.Verify.Select(x => x.Advisory).ToArray()).IsEquivalentTo(new bool?[] { null, true });
    }
}
