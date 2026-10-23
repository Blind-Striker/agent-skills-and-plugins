using Dispatch.Tests.Support;

namespace Dispatch.Tests.Integration;

[Category("Integration")]
[NotInParallel]
public sealed class SettlementStateTests
{
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
