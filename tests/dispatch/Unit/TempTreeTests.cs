using Dispatch.Tests.Support;

namespace Dispatch.Tests.Unit;

[Category("Unit")]
public sealed class TempTreeTests
{
    [Test]
    public async Task A_tree_is_isolated_and_removed_on_disposal()
    {
        var cwd = Environment.CurrentDirectory;
        var home = Environment.GetEnvironmentVariable("DISPATCH_HOME");
        string root;
        using (var tree = new TempTree())
        {
            root = tree.Root;
            var child = tree.ChildEnvironment();
            await Assert.That(Path.IsPathFullyQualified(root)).IsTrue();
            foreach (var name in new[] { "DISPATCH_HOME", "CODEX_HOME", "HOME", "XDG_CONFIG_HOME", "XDG_DATA_HOME", "XDG_STATE_HOME", "TEMP", "TMP", "TMPDIR" })
            {
                await Assert.That(Directory.Exists(child[name])).IsTrue();
                await Assert.That(Path.GetRelativePath(root, child[name]!)).DoesNotStartWith("..");
            }
            await Assert.That(await File.ReadAllTextAsync(child["GIT_CONFIG_GLOBAL"]!)).IsEmpty();
            await Assert.That(child["GIT_CONFIG_NOSYSTEM"]).IsEqualTo("1");
            if (OperatingSystem.IsWindows())
            {
                foreach (var name in new[] { "USERPROFILE", "APPDATA", "LOCALAPPDATA" })
                    await Assert.That(Path.GetRelativePath(root, child[name]!)).DoesNotStartWith("..");
            }
            foreach (string name in Environment.GetEnvironmentVariables().Keys)
            {
                if (name.Equals("DOTNET_ROOT", StringComparison.OrdinalIgnoreCase) || name.StartsWith("DOTNET_ROOT_", StringComparison.OrdinalIgnoreCase))
                    await Assert.That(child[name]).IsEqualTo(Environment.GetEnvironmentVariable(name));
            }
            child["DISPATCH_HOME"] = "changed-child-only";
            await Assert.That(tree.ChildEnvironment()["DISPATCH_HOME"]).IsNotEqualTo("changed-child-only");
            var file = Path.Combine(tree.WorktreeRoot, "sample.txt");
            await File.WriteAllTextAsync(file, "temporary");
            File.SetAttributes(file, FileAttributes.ReadOnly);
        }
        await Assert.That(Directory.Exists(root)).IsFalse();
        await Assert.That(Environment.CurrentDirectory).IsEqualTo(cwd);
        await Assert.That(Environment.GetEnvironmentVariable("DISPATCH_HOME")).IsEqualTo(home);
    }
}
