using Dispatch.Tests.Support;

namespace Dispatch.Tests.Integration;

[Category("Integration")]
public sealed class GatesTests
{
    [Test]
    [Timeout(30_000)]
    public async Task The_session_build_gate_completed_before_tests_start(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = Gates.BuildResult;

        await Assert.That(result.ExitCode).IsEqualTo(0);
        await Assert.That(result.Stdout).Contains("dispatch.dll");
    }
}
