using Dispatch.Tests.Support;
using TUnit.Assertions.Enums;

namespace Dispatch.Tests.Integration;

[Category("Integration")]
[NotInParallel]
public sealed class SupervisorLockTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ContinueOf_reads_source_without_creating_or_repairing_it(bool supervised, CancellationToken ct)
    {
        using var lab = new CoreLab("next-run");
        var source = await lab.SourceAttemptAsync(ct);
        var control = Locations.ControlDir(source.Ledger.RunId);
        using var held = supervised ? SupervisorLock.AcquireFolder(control) : null;
        source.Ledger.Write("sibling", "prompt");
        File.AppendAllText(Path.Combine(source.Ledger.Dir, "events.jsonl"), "{\"torn\":");
        var files = Directory.GetFiles(source.Ledger.Dir, "*", SearchOption.AllDirectories).Concat(Directory.GetFiles(control, "*", SearchOption.AllDirectories))
            .Where(path => Path.GetFileName(path) != "supervisor.lock").Order(StringComparer.Ordinal).ToArray();
        var bytes = files.Select(File.ReadAllBytes).ToArray();
        var next = lab.Continuation();
        var plan = await lab.PlanAsync(ct, next.Record.Spec);
        await Assert.That(plan.Errors).IsEmpty();
        await Assert.That(plan.Tasks.Select(x => x.Spec.Id!)).IsEquivalentTo(new[] { "next" }, CollectionOrdering.Matching);
        await Worktree.PrepareAsync(next, lab.Ledger, lab.Tree.Repository, ct, lab.Git);
        await Assert.That(File.ReadAllText(Path.Combine(next.Record.Workspace.Path!, "source.txt"))).IsEqualTo("source work\n");
        await Assert.That(Directory.GetFiles(source.Ledger.Dir, "*", SearchOption.AllDirectories).Concat(Directory.GetFiles(control, "*", SearchOption.AllDirectories))
            .Where(path => Path.GetFileName(path) != "supervisor.lock").Order(StringComparer.Ordinal))
            .IsEquivalentTo(files, CollectionOrdering.Matching);
        for (var i = 0; i < files.Length; i++)
            await Assert.That(File.ReadAllBytes(files[i])).IsEquivalentTo(bytes[i], CollectionOrdering.Matching);
        await Assert.That(File.Exists(Path.Combine(control, "supervisor.lock"))).IsEqualTo(supervised);
        next.Record.Spec.ContinueOf = "2026-10-31-source/absent";
        var missing = await lab.PlanAsync(ct, next.Record.Spec);
        await Assert.That(missing.Errors).IsEquivalentTo(new[] {
            $"next: no task record {Path.Combine(source.Ledger.Dir, "absent", "task.json")}" }, CollectionOrdering.Matching);
        await Assert.That(Directory.Exists(Path.Combine(source.Ledger.Dir, "absent"))).IsFalse();
    }

    [Test]
    [Timeout(30_000)]
    public async Task Status_Probe_DoesNotBlockAcquire(CancellationToken ct)
    {
        using var lab = new CoreLab();
        var control = Locations.ControlDir(lab.Ledger.RunId);
        using (var initial = SupervisorLock.AcquireFolder(control)) { }
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var probes = Task.Run(() =>
        {
            started.SetResult();
            while (!stop.IsCancellationRequested) SupervisorLock.IsHeld(lab.Ledger.RunId);
        }, ct);
        try
        {
            await started.Task.WaitAsync(ct);
            for (var i = 0; i < 1000; i++)
            {
                ct.ThrowIfCancellationRequested();
                using var held = SupervisorLock.AcquireFolder(control);
            }
        }
        finally { stop.Cancel(); await probes; }
        await Assert.That(File.Exists(Path.Combine(control, "supervisor.lock"))).IsTrue();
    }
}
