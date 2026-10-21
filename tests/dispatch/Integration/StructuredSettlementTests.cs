using System.Text.Json.Nodes;
using Dispatch.Tests.Support;

namespace Dispatch.Tests.Integration;

[Category("Integration")]
[NotInParallel]
public sealed class StructuredSettlementTests
{
    [Test]
    [Timeout(30_000)]
    public async Task Structured_settlement_uses_frozen_inputs_without_committing_diffing_or_verifying(CancellationToken ct)
    {
        using var lab = new CoreLab();
        var run = await lab.TaskAsync("answer", ct);
        lab.Structured(run);
        run.Record.Spec.Verify = ["exit 9"];
        run.Record.PromptInputs!.Items![0]!["id"] = "changed-after-freeze";
        run.Record.PromptInputs.Schema!["type"] = "string";
        File.WriteAllText(Path.Combine(lab.Tree.Repository, "outside.txt"), "Must stay uncommitted.");
        run.SaveWorker(new("opencode", run.Id, "worker", "turn", new()), lab.Ledger);
        lab.Backend.OnCollect = (_, _) => Task.FromResult(new WorkerReport("succeeded", StructuredLab.Valid, new(), null, false, [], null, true));

        var result = await Settlement.SettleAsync(lab.Backend, lab.Ledger, run, null, ct, lab.Git);

        await Assert.That(result.State).IsEqualTo("done");
        await Assert.That(result.Verify).IsEmpty();
        await Assert.That(result.FilesChanged).IsEmpty();
        await Assert.That(result.OutOfScope).IsEmpty();
        await Assert.That(result.Commit).IsNull();
        await Assert.That(result.CommitMode).IsNull();
        await Assert.That(File.Exists(Path.Combine(lab.Ledger.TaskDir(run.Id), "diff.patch"))).IsFalse();
        var repo = new LabRepo(lab.Tree);
        await Assert.That(await repo.GitAsync(["status", "--porcelain"], ct)).Contains("outside.txt");
        var saved = JsonNode.Parse(File.ReadAllText(Path.Combine(lab.Ledger.TaskDir(run.Id), "result.json")))!;
        await Assert.That(saved.AsObject().ContainsKey("tokens")).IsFalse();
        await Assert.That(lab.Ledger.Read().Select(x => x.Type)).IsEquivalentTo(new[] { "collected", "validated", "outcome" });
    }

    [Test]
    [Arguments("input.json")]
    [Arguments("result.schema.json")]
    [Timeout(30_000)]
    public async Task Structured_settlement_rejects_tampered_frozen_copies(string name, CancellationToken ct)
    {
        using var lab = new CoreLab();
        var run = await lab.TaskAsync("answer", ct);
        lab.Structured(run);
        File.AppendAllText(Path.Combine(lab.Ledger.TaskDir(run.Id), name), " ");
        run.SaveWorker(new("opencode", run.Id, "worker", "turn", new()), lab.Ledger);
        lab.Backend.OnCollect = (_, _) => Task.FromResult(new WorkerReport("succeeded", StructuredLab.Valid, new(), null, true, [], null, true));

        var result = await Settlement.SettleAsync(lab.Backend, lab.Ledger, run, null, ct, lab.Git);

        await Assert.That(result.State).IsEqualTo("failed");
        await Assert.That(result.Failures).Contains("invalid-output");
        await Assert.That(result.Error).Contains(name);
        await Assert.That(result.Error).Contains("SHA-256 mismatch");
        await Assert.That(result.Deliverable).IsNull();
    }

    [Test]
    [Timeout(30_000)]
    public async Task Structured_recollection_removes_an_old_deliverable_and_reports_all_validation_errors(CancellationToken ct)
    {
        using var lab = new CoreLab();
        var run = await lab.TaskAsync("answer", ct);
        lab.Structured(run);
        run.SaveWorker(new("opencode", run.Id, "worker", "turn", new()), lab.Ledger);
        lab.Backend.OnCollect = (_, _) => Task.FromResult(new WorkerReport("succeeded", StructuredLab.Valid, new(), null, true, [], null, true));
        await Assert.That((await Settlement.SettleAsync(lab.Backend, lab.Ledger, run, null, ct, lab.Git)).State).IsEqualTo("done");
        lab.Backend.OnCollect = (_, _) => Task.FromResult(new WorkerReport("succeeded", """{"items":[{"id":"i1","key":42,"text":false,"details":17}]}""", new(), null, true, [], null, true));

        var result = await Settlement.SettleAsync(lab.Backend, lab.Ledger, run, null, ct, lab.Git);

        await Assert.That(result.State).IsEqualTo("failed");
        await Assert.That(result.Validation!.Errors.Select(x => x.At)).IsEquivalentTo(new[] { "/items/0/key", "/items/0/text", "/items/0/details", "/items/0/key" });
        await Assert.That(File.Exists(Path.Combine(lab.Ledger.TaskDir(run.Id), "deliverable.json"))).IsFalse();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    [Timeout(30_000)]
    public async Task Recovered_structured_scratch_is_forwarded_empty_and_removed_only_after_a_confirmed_stop(bool zombie, CancellationToken ct)
    {
        using var lab = new CoreLab();
        var run = await lab.TaskAsync("answer", ct);
        lab.Structured(run);
        run.Record.Spec.Project = "off";
        var scratch = Directory.CreateDirectory(Path.Combine(lab.Tree.Root, "dispatch-scratch-" + Guid.NewGuid().ToString("N"))).FullName;
        run.Record.Workspace = new() { Path = scratch };
        lab.Backend.OnCreate = (launch, _) =>
        {
            if (launch.Directory != scratch || Directory.EnumerateFileSystemEntries(launch.Directory).Any())
                throw new InvalidOperationException("worker did not receive the empty scratch directory");
            return Task.FromResult(new CreateResult(new("opencode", run.Id, "worker", null, new()), new("none", "none", "policy")));
        };
        await new TaskStarter(lab.Backend, lab.Ledger, lab.Tree.Repository, AllowList.From(null), lab.Policy, lab.Git).StartAsync(run, ct);
        run.Zombie = zombie;
        run.TimedOut = zombie;
        lab.Backend.OnCollect = (_, _) => Task.FromResult(new WorkerReport("succeeded", StructuredLab.Valid, new(), null, true, [], null, true));

        var result = await Settlement.SettleAsync(lab.Backend, lab.Ledger, run, null, ct, lab.Git);

        await Assert.That(Directory.Exists(scratch)).IsEqualTo(zombie);
        await Assert.That(result.State).IsEqualTo(zombie ? "failed" : "done");
        if (zombie) await Assert.That(result.Failures).Contains("zombie");
    }
}
