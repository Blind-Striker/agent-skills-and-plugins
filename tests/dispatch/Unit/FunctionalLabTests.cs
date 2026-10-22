using Dispatch.Tests.Support;

namespace Dispatch.Tests.Unit;

[Category("Unit")]
public sealed class FunctionalLabTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Missing_auth_skips_but_blocked_profiles_do_not_prevent_an_auth_volume_home(bool blockedTemp)
    {
        using var host = new TempTree();
        using var tree = new TempTree();
        var userHome = Directory.CreateDirectory(Path.Combine(host.Root, "source-codex")).FullName;
        if (blockedTemp) File.WriteAllText(Path.Combine(userHome, "auth.json"), "synthetic-auth");
        var temporaryRoot = Path.Combine(host.Root, "system-temp");
        if (blockedTemp) File.WriteAllText(temporaryRoot, "not-a-directory");
        else Directory.CreateDirectory(temporaryRoot);

        if (blockedTemp)
        {
            using var lab = new FunctionalLab("codex", "model-y", tree, userHome, temporaryRoot);
            await Assert.That(lab.Environment["CODEX_HOME"]).StartsWith(Path.Combine(Path.GetTempPath(), "deniz-dispatch-codex") + Path.DirectorySeparatorChar);
            await Assert.That(File.ReadAllText(temporaryRoot)).IsEqualTo("not-a-directory");
        }
        else
        {
            var error = await Assert.ThrowsAsync<TUnit.Core.Exceptions.SkipTestException>(() =>
            {
                using var lab = new FunctionalLab("codex", "model-y", tree, userHome, temporaryRoot);
                return Task.CompletedTask;
            });
            await Assert.That(error!.Message).Contains("hardlinked file auth");
            await Assert.That(Directory.GetFileSystemEntries(temporaryRoot)).IsEmpty();
        }
        await Assert.That(Directory.GetFileSystemEntries(userHome)).IsEquivalentTo(
            blockedTemp ? new[] { Path.Combine(userHome, "auth.json") } : Array.Empty<string>());
    }

    [Test]
    public async Task Codex_home_and_canary_use_auth_volume_temp_and_are_removed_with_lab()
    {
        using var host = new TempTree();
        using var tree = new TempTree();
        var userHome = Directory.CreateDirectory(Path.Combine(host.Root, "source-codex")).FullName;
        File.WriteAllText(Path.Combine(userHome, "auth.json"), "synthetic-auth");
        File.WriteAllText(Path.Combine(userHome, "AGENTS.md"), "source-instructions");
        var temporaryRoot = Directory.CreateDirectory(Path.Combine(host.Root, "system-temp")).FullName;
        string home;
        using (var lab = new FunctionalLab("codex", "model-y", tree, userHome, temporaryRoot))
        {
            home = lab.Environment["CODEX_HOME"]!;
            await Assert.That(home).StartsWith(Path.Combine(Path.GetTempPath(), "deniz-dispatch-codex") + Path.DirectorySeparatorChar);
            await Assert.That(Path.GetRelativePath(userHome, home)).StartsWith("..");
            File.WriteAllText(Path.Combine(home, "AGENTS.md"), "temporary-canary");
            await Assert.That(File.ReadAllText(Path.Combine(userHome, "AGENTS.md"))).IsEqualTo("source-instructions");
            await Assert.That(lab.Environment["DISPATCH_HOME"]).IsEqualTo(tree.DispatchHome);
            await Assert.That(lab.Environment["TEMP"]).IsEqualTo(tree.ChildEnvironment()["TEMP"]);
        }
        await Assert.That(Directory.Exists(home)).IsFalse();
        await Assert.That(Directory.Exists(temporaryRoot)).IsTrue();
        await Assert.That(File.ReadAllText(Path.Combine(userHome, "AGENTS.md"))).IsEqualTo("source-instructions");
        await Assert.That(File.ReadAllText(Path.Combine(userHome, "auth.json"))).IsEqualTo("synthetic-auth");
    }
}
