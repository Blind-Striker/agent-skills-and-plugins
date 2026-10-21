using Dispatch.Tests.Support;

namespace Dispatch.Tests.Integration;

[Category("Integration")]
[NotInParallel]
public sealed class SupervisorLockTests
{
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
