using OpenCode.Sdk.Models;
using TUnit.Assertions.Enums;

namespace Dispatch.Tests.Unit;

[Category("Unit")]
public sealed class ControlRulesTests
{
    [Test]
    public async Task Fence_profile_uses_real_paths_for_writable_and_deny_entries(CancellationToken ct)
    {
        var root = Directory.CreateTempSubdirectory("dispatch-profile-").FullName;
        var target = Directory.CreateDirectory(Path.Combine(root, "real")).FullName;
        var link = Path.Combine(root, "alias");
        try
        {
            if (OperatingSystem.IsWindows())
            {
                using var command = new Dispatch.Tests.Support.CapturedProcess();
                (await command.RunAsync("cmd", ["/c", "mklink", "/J", link, target], root,
                    new Dictionary<string, string?>(), ct)).RequireSuccess("junction fixture");
            }
            else Directory.CreateSymbolicLink(link, target);
            // Guard on non-macOS: exercise the same builder with already-resolved inputs there.
            var input = OperatingSystem.IsMacOS() ? link : Worktree.RealPath(link);
            var real = Worktree.RealPath(target).Replace("\\", "\\\\", StringComparison.Ordinal);
            var dev = OperatingSystem.IsWindows() ? "/dev" : Worktree.RealPath("/dev");
            await Assert.That(Ready.FenceProfile([Path.Combine(input, "write")], [Path.Combine(input, "deny")]))
                .IsEqualTo($"(version 1) (allow default) (deny file-write*) (allow file-write* (subpath \"{real}{(OperatingSystem.IsWindows() ? "\\\\" : "/")}write\") (subpath \"{dev}\")) (deny file-write* (subpath \"{real}{(OperatingSystem.IsWindows() ? "\\\\" : "/")}deny\"))");
        }
        finally { if (Directory.Exists(link)) Directory.Delete(link); Directory.Delete(root, recursive: true); }
    }

    [Test]
    public async Task Fence_builders_emit_exact_argv_and_escaped_profile()
    {
        string[] roots = ["/profile", "/space and \"quote\"\\slash", "/dev"];
        string[] readOnly = ["/data/bin", "/git/hooks", "/git/config"];
        await Assert.That(Ready.FenceCommand("bwrap", "/usr/bin/bwrap", ["/bin/opencode", "serve"], roots, readOnly, "/tmp/inspect", "/run/user/1000")).IsEquivalentTo(new[]
        {
            "/usr/bin/bwrap", "--ro-bind", "/", "/", "--dev", "/dev", "--ro-bind", "/proc", "/proc", "--tmpfs", "/tmp",
            "--ro-bind", "/tmp/inspect", "/tmp/inspect",
            "--bind", "/profile", "/profile", "--bind", "/space and \"quote\"\\slash", "/space and \"quote\"\\slash",
            "--tmpfs", "/run/user/1000",
            "--ro-bind", "/data/bin", "/data/bin", "--ro-bind", "/git/hooks", "/git/hooks", "--ro-bind", "/git/config", "/git/config",
            "--", "/bin/opencode", "serve",
        }, CollectionOrdering.Matching);
        const string profile = "(version 1) (allow default) (deny file-write*) (allow file-write* (subpath \"/profile\") (subpath \"/space and \\\"quote\\\"\\\\slash\") (subpath \"/dev\")) (deny file-write* (subpath \"/data/bin\")) (deny file-write* (subpath \"/git/hooks\")) (deny file-write* (subpath \"/git/config\"))";
        await Assert.That(Ready.FenceProfile(roots, readOnly)).IsEqualTo(profile);
        await Assert.That(Ready.FenceCommand("sandbox-exec", "/usr/bin/sandbox-exec", ["/bin/opencode", "serve"], roots, readOnly, null, null))
            .IsEquivalentTo(new[] { "/usr/bin/sandbox-exec", "-p", profile, "/bin/opencode", "serve" }, CollectionOrdering.Matching);
        await Assert.That(Ready.FenceCommand("bwrap", "/usr/bin/bwrap", ["true"], ["/probe-temp"], [], null, null))
            .IsEquivalentTo(new[] { "/usr/bin/bwrap", "--ro-bind", "/", "/", "--dev", "/dev", "--ro-bind", "/proc", "/proc", "--tmpfs", "/tmp",
                "--bind", "/probe-temp", "/probe-temp", "--", "true" }, CollectionOrdering.Matching);
    }

    [Test]
    public async Task Fence_bwrap_binds_private_runfile_after_writable_before_read_only()
    {
        await Assert.That(Ready.FenceCommand("bwrap", "/usr/bin/bwrap", ["opencode", "serve"],
            ["/task-temp", "/workspace"], ["/data/opencode/bin"], null, "/run/user/1000",
            ("/task-temp/runfile", "/data/dotnet/runfile"))).IsEquivalentTo(new[]
        {
            "/usr/bin/bwrap", "--ro-bind", "/", "/", "--dev", "/dev", "--ro-bind", "/proc", "/proc", "--tmpfs", "/tmp",
            "--bind", "/task-temp", "/task-temp", "--bind", "/workspace", "/workspace",
            "--bind", "/task-temp/runfile", "/data/dotnet/runfile", "--tmpfs", "/run/user/1000",
            "--ro-bind", "/data/opencode/bin", "/data/opencode/bin", "--", "opencode", "serve",
        }, CollectionOrdering.Matching);
    }

    [Test]
    [Arguments("read-only", false)]
    [Arguments("workspace", false)]
    [Arguments("read-only", true)]
    [Arguments("workspace", true)]
    public async Task Fence_writable_set_contains_only_backend_folders_and_access_specific_roots(string access, bool xdg)
    {
        var home = Path.Combine(Path.GetTempPath(), "fence-home");
        var profile = Path.Combine(Locations.ControlRoot, "profiles", "opencode");
        var workspace = Path.Combine(Path.GetTempPath(), "worktree");
        var temp = Path.Combine(Path.GetTempPath(), "task-temp");
        var common = Path.Combine(Path.GetTempPath(), "repo", ".git");
        var cache = Path.Combine(home, "build-cache");
        var environment = new Dictionary<string, string>();
        if (xdg) foreach (var name in new[] { "DATA", "STATE", "CACHE", "CONFIG" })
            environment[$"XDG_{name}_HOME"] = Path.Combine(home, name);
        var launch = new WorkerLaunch("run", "task", "code", new("fixture", "model", null), null, null,
            new("clean", "on", access, "on", "worktree"), workspace, new([], []), new Dictionary<string, string> { ["TEMP"] = temp },
            null, "clean", null, [cache]);
        var actual = Ready.WritableSet(launch, profile, home, environment, common);
        foreach (var root in actual)
            foreach (var protectedRoot in new[] { Locations.ControlRoot, Locations.LedgerRoot })
            {
                await Assert.That(Worktree.IsInside(Worktree.RealPath(root), Worktree.RealPath(protectedRoot))).IsFalse();
                await Assert.That(Worktree.IsInside(Worktree.RealPath(protectedRoot), Worktree.RealPath(root))).IsFalse();
            }
        await Assert.That(actual).DoesNotContain(Path.Combine(home, xdg ? "CONFIG" : ".config", "opencode"));
        await Assert.That(actual).DoesNotContain(Path.Combine(home, xdg ? "CACHE" : ".cache", "opencode"));
        await Assert.That(actual).Contains(temp);
        foreach (var extra in new[] { workspace, common, cache })
            if (access == "workspace") await Assert.That(actual).Contains(extra);
            else await Assert.That(actual).DoesNotContain(extra);
    }

    [Test]
    [Arguments("bwrap")]
    [Arguments("sandbox-exec")]
    [Arguments(null)]
    public async Task Read_only_fence_preserves_shell_policy_and_protected_edit_denies(string? kind)
    {
        var source = new WorkerRules([new() { Name = "policy/no-push", Action = "shell", Resources = ["git push*"] }],
            [new() { Name = "policy/ask", Action = "shell", Resources = ["publish*"] }]);
        var rules = OpenCodeRules.Render(source, "example", new("clean", "on", "read-only", "off", "worktree"), kind);
        var expected = new List<string> { "shell|publish*|ask", "shell|git push*|deny", "edit|*|deny" };
        if (kind is null) expected.Add("shell|*|deny");
        expected.AddRange(["webfetch|*|deny", "websearch|*|deny",
            $"edit|{Path.GetFullPath(Path.Combine(Locations.ControlRoot, "**")).Replace('\\', '/')}|deny",
            $"edit|{Path.GetFullPath(Path.Combine(Locations.LedgerRoot, "example", "**")).Replace('\\', '/')}|deny"]);
        await Assert.That(rules.Select(x => $"{x.Action}|{x.Resource}|{x.Effect}"))
            .IsEquivalentTo(expected, CollectionOrdering.Matching);
    }

    [Test]
    public async Task OpenCodeRules_DefaultLocations_WorktreeEditAllowedControlEditDenied()
    {
        var rules = OpenCodeRules.Render(new WorkerRules([], []), "example");
        PermissionEffect Effect(string path) => rules.LastOrDefault(x => x.Action == "edit" && Wildcard.Match(path, x.Resource, OperatingSystem.IsWindows()))?.Effect ?? PermissionEffect.Allow;
        var worktreeFile = Path.GetFullPath(Path.Combine(Locations.StateRoot, "worktrees", "example", "inspect", "file.txt"));
        var controlFile = Path.GetFullPath(Path.Combine(Locations.StateRoot, "control", "abcdefghijklmnopqrstuvwxyz", "decisions", "inspect-1.json"));
        await Assert.That(Effect(worktreeFile)).IsEqualTo(PermissionEffect.Allow);
        await Assert.That(Effect(controlFile)).IsEqualTo(PermissionEffect.Deny);
    }

    [Test]
    public async Task OpenCodeRules_EndWithControlAndLedgerDenies()
    {
        var rules = OpenCodeRules.Render(new WorkerRules(
            [new() { Name = "user/deny", Action = "shell", Resources = ["publish*"] }],
            [new() { Name = "repo/ask", Action = "edit", Resources = ["*"] }]), "example");
        await Assert.That(rules[^1].Action).IsEqualTo("edit");
        await Assert.That(rules[^1].Effect).IsEqualTo(PermissionEffect.Deny);
        await Assert.That(rules[^2].Resource).IsEqualTo(Path.GetFullPath(Path.Combine(Locations.StateRoot, "control", "**")).Replace('\\', '/'));
        await Assert.That(rules[^1].Resource).IsEqualTo(Path.GetFullPath(Path.Combine(Locations.LedgerRoot, "example", "**")).Replace('\\', '/'));
        await Assert.That(rules.Count).IsEqualTo(4);
    }
}
