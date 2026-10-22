using Dispatch.Tests.Support;

namespace Dispatch.Tests.Unit;

[Category("Unit")]
public sealed class BackendBoundaryTests
{
    [Test]
    public async Task The_supervisor_accepts_the_worker_backend_boundary()
    {
        var constructors = typeof(Supervisor).GetConstructors();
        await Assert.That(constructors.Any(c => c.GetParameters().Any(p => p.ParameterType == typeof(IWorkerBackend)))).IsTrue();
        await using var backend = new FakeBackend("opencode", new("policy", "policy", "text", true));
        await Assert.That(backend.Calls).IsEmpty();
    }
}
