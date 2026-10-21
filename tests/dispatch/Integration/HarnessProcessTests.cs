using Dispatch.Tests.Support;

namespace Dispatch.Tests.Integration;

[Category("Integration")]
public sealed class HarnessProcessTests
{
    [Test]
    [Timeout(180_000)]
    public async Task The_documented_entry_without_a_command_prints_usage_and_exits_2(CancellationToken cancellationToken)
    {
        using var tree = new TempTree();
        using var harness = new HarnessProcess(tree);

        var result = await harness.RunAsync([], cancellationToken);

        await Assert.That(result.ExitCode).IsEqualTo(2);
        await Assert.That(result.Stdout).Contains("usage: dotnet dispatch.cs --");
        await Assert.That(result.Stderr).DoesNotContain("usage: dotnet dispatch.cs --");
    }

    [Test]
    [Timeout(180_000)]
    public async Task A_relative_dispatch_home_is_refused_with_exit_2(CancellationToken cancellationToken)
    {
        using var tree = new TempTree();
        using var harness = new HarnessProcess(tree);

        var result = await harness.RunAsync([], cancellationToken, new Dictionary<string, string?> { ["DISPATCH_HOME"] = "relative-home" });

        await Assert.That(result.ExitCode).IsEqualTo(2);
        await Assert.That(result.Stderr).Contains("DISPATCH_HOME must be an absolute path: relative-home");
        await Assert.That(result.Stdout).DoesNotContain("usage: dotnet dispatch.cs --");
    }
}
