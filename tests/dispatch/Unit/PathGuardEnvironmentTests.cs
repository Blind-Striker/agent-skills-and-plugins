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
        var previousCertificate = Environment.GetEnvironmentVariable("DOTNET_GENERATE_ASPNET_CERTIFICATE");
        try
        {
            Environment.SetEnvironmentVariable("DOTNET_ADD_GLOBAL_TOOLS_TO_PATH", "true");
            Environment.SetEnvironmentVariable("DOTNET_GENERATE_ASPNET_CERTIFICATE", "true");
            var inherited = WorkerEnvironment.Environment("inspect").Variables;
            await Assert.That(inherited["DOTNET_ADD_GLOBAL_TOOLS_TO_PATH"]).IsEqualTo("true");
            await Assert.That(inherited["DOTNET_GENERATE_ASPNET_CERTIFICATE"]).IsEqualTo("true");
            var variables = WorkerEnvironment.Environment("inspect", configured: new Dictionary<string, Setting<string>>
            {
                ["DOTNET_ADD_GLOBAL_TOOLS_TO_PATH"] = new() { Value = "false", From = "task" },
                ["DOTNET_GENERATE_ASPNET_CERTIFICATE"] = new() { Value = "false", From = "task" },
            }).Variables;
            await Assert.That(variables["DOTNET_ADD_GLOBAL_TOOLS_TO_PATH"]).IsEqualTo("false");
            await Assert.That(variables["DOTNET_GENERATE_ASPNET_CERTIFICATE"]).IsEqualTo("false");
        }
        finally
        {
            Environment.SetEnvironmentVariable("DOTNET_ADD_GLOBAL_TOOLS_TO_PATH", previous);
            Environment.SetEnvironmentVariable("DOTNET_GENERATE_ASPNET_CERTIFICATE", previousCertificate);
        }
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
