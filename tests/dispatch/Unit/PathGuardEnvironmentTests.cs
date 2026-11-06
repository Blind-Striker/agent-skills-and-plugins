using Dispatch.Tests.Support;

namespace Dispatch.Tests.Unit;

[Category("Unit")]
[NotInParallel]
public sealed class PathGuardEnvironmentTests
{
    [Test]
    public async Task Session_git_overrides_are_removed_and_restored_without_changing_the_global_config()
    {
        string[] names = ["GIT_DIR", "GIT_WORK_TREE", "GIT_INDEX_FILE", "GIT_OBJECT_DIRECTORY",
            "GIT_ALTERNATE_OBJECT_DIRECTORIES", "GIT_COMMON_DIR", "GIT_CONFIG_PARAMETERS", "GIT_CONFIG_COUNT",
            "GIT_CONFIG_KEY_0", "GIT_CONFIG_VALUE_0", "GIT_CONFIG_KEY_7", "GIT_CONFIG_VALUE_7"];
        var original = names.ToDictionary(name => name, Environment.GetEnvironmentVariable);
        var global = Environment.GetEnvironmentVariable("GIT_CONFIG_GLOBAL");
        try
        {
            foreach (var name in names) Environment.SetEnvironmentVariable(name, "synthetic-" + name);
            var saved = Gates.ClearGitOverrides();
            foreach (var name in names)
            {
                await Assert.That(Environment.GetEnvironmentVariable(name)).IsNull();
                await Assert.That(saved[name]).IsEqualTo("synthetic-" + name);
            }
            await Assert.That(Environment.GetEnvironmentVariable("GIT_CONFIG_GLOBAL")).IsEqualTo(global);
            Gates.RestoreGitOverrides(saved);
            foreach (var name in names)
                await Assert.That(Environment.GetEnvironmentVariable(name)).IsEqualTo("synthetic-" + name);
        }
        finally
        {
            foreach (var (name, value) in original) Environment.SetEnvironmentVariable(name, value);
        }
    }

    [Test]
    public async Task Global_filter_case_has_method_level_unkeyed_exclusion()
    {
        var method = typeof(Integration.GitRunnerTests).GetMethod(nameof(Integration.GitRunnerTests.FilterInGlobalConfig_IsCaught))!;
        await Assert.That(method.GetCustomAttributesData().Where(attribute => attribute.AttributeType == typeof(NotInParallelAttribute))
            .Select(attribute => attribute.ConstructorArguments.Count))
            .IsEquivalentTo(new[] { 0 }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

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
