using System.Text;
using Dispatch.Tests.Support;
using Microsoft.Win32;

namespace Dispatch.Tests.Integration;

[Category("Integration")]
[NotInParallel]
public sealed class PathGuardEnvironmentTests
{
    [Test]
    [Timeout(60_000)]
    public async Task DotnetFirstUse_DoesNotChangeUserPathOrCreateToolpathSentinel(CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip.Test("The .NET tools PATH append affects the Windows user registry.");
            return;
        }
        using var tree = new TempTree();
        var home = Directory.CreateDirectory(Path.Combine(tree.Root, "first-use-home")).FullName;
        var environment = tree.ChildEnvironment();
        // Refuse to start an unsafe control: the test must never opt in to registry writes.
        await Assert.That(environment["DOTNET_ADD_GLOBAL_TOOLS_TO_PATH"]).IsEqualTo("false");
        environment["DOTNET_CLI_HOME"] = home;
        await Assert.That(Directory.GetFileSystemEntries(home)).IsEmpty();
        using var key = Registry.CurrentUser.OpenSubKey("Environment", writable: false);
        var before = key?.GetValue("Path", null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
        using var child = new CapturedProcess();
        var result = await child.RunAsync("dotnet", ["nuget", "locals", "all", "--list"], TestPaths.Harness, environment, ct);
        result.RequireSuccess("dotnet first use");
        var after = key?.GetValue("Path", null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
        await Assert.That(after is null).IsEqualTo(before is null);
        await Assert.That(Encoding.Unicode.GetBytes(after ?? "").SequenceEqual(Encoding.Unicode.GetBytes(before ?? ""))).IsTrue();
        await Assert.That(Directory.GetFiles(home, "*.toolpath.sentinel", SearchOption.AllDirectories)).IsEmpty();
        await Assert.That(Directory.GetFiles(home, "*.dotnetFirstUseSentinel", SearchOption.AllDirectories)).IsNotEmpty();
    }
}
