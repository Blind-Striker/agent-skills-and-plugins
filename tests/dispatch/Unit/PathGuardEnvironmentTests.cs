using Dispatch.Tests.Support;

namespace Dispatch.Tests.Unit;

[Category("Unit")]
[NotInParallel]
public sealed class PathGuardEnvironmentTests
{
    [Test]
    public async Task WorkerEnvironment_DisablesGlobalToolsPathAppend()
    {
        if (!OperatingSystem.IsWindows()) Skip.Test("The .NET tools PATH append affects the Windows user registry.");
        using var lab = new CoreLab();
        var previous = Environment.GetEnvironmentVariable("DOTNET_ADD_GLOBAL_TOOLS_TO_PATH");
        try
        {
            Environment.SetEnvironmentVariable("DOTNET_ADD_GLOBAL_TOOLS_TO_PATH", "true");
            var variables = WorkerEnvironment.Environment("inspect").Variables;
            await Assert.That(variables["DOTNET_ADD_GLOBAL_TOOLS_TO_PATH"]).IsEqualTo("false");
        }
        finally { Environment.SetEnvironmentVariable("DOTNET_ADD_GLOBAL_TOOLS_TO_PATH", previous); }
    }

    [Test]
    public async Task TempTree_DisablesGlobalToolsPathAppendWithSharedCliHome()
    {
        if (!OperatingSystem.IsWindows()) Skip.Test("The .NET tools PATH append affects the Windows user registry.");
        using var first = new TempTree();
        using var second = new TempTree();
        var environment = first.ChildEnvironment();
        await Assert.That(environment["DOTNET_ADD_GLOBAL_TOOLS_TO_PATH"]).IsEqualTo("false");
        await Assert.That(environment["DOTNET_CLI_HOME"]).IsEqualTo(second.ChildEnvironment()["DOTNET_CLI_HOME"]);
        await Assert.That(Directory.Exists(environment["DOTNET_CLI_HOME"])).IsTrue();
        await Assert.That(Path.GetRelativePath(first.Root, environment["DOTNET_CLI_HOME"]!)).StartsWith("..");
    }
}
