using OpenCode.Sdk.Models;
using TUnit.Assertions.Enums;

namespace Dispatch.Tests.Unit;

[Category("Unit")]
public sealed class ControlRulesTests
{
    [Test]
    public async Task Fence_builders_emit_exact_argv_and_escaped_profile()
    {
        string[] roots = ["/profile", "/space and \"quote\"\\slash", "/dev"];
        await Assert.That(Ready.FenceCommand("bwrap", ["/bin/opencode", "serve"], roots)).IsEquivalentTo(new[]
        {
            "bwrap", "--ro-bind", "/", "/", "--dev", "/dev", "--ro-bind", "/proc", "/proc", "--tmpfs", "/tmp",
            "--bind", "/profile", "/profile", "--bind", "/space and \"quote\"\\slash", "/space and \"quote\"\\slash",
            "--", "/bin/opencode", "serve",
        }, CollectionOrdering.Matching);
        const string profile = "(version 1) (allow default) (deny file-write*) (allow file-write* (subpath \"/profile\") (subpath \"/space and \\\"quote\\\"\\\\slash\") (subpath \"/dev\") (subpath \"/tmp\") (subpath \"/private/tmp\") (subpath \"/private/var/folders\"))";
        await Assert.That(Ready.FenceProfile(roots)).IsEqualTo(profile);
        await Assert.That(Ready.FenceCommand("sandbox-exec", ["/bin/opencode", "serve"], roots))
            .IsEquivalentTo(new[] { "sandbox-exec", "-p", profile, "/bin/opencode", "serve" }, CollectionOrdering.Matching);
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
        string[] expected = xdg
            ? [Path.Combine(home, "DATA", "opencode"), Path.Combine(home, "STATE", "opencode"), Path.Combine(home, "CACHE", "opencode"), Path.Combine(home, "CONFIG", "opencode")]
            : [Path.Combine(home, ".local/share", "opencode"), Path.Combine(home, ".local/state", "opencode"), Path.Combine(home, ".cache", "opencode"), Path.Combine(home, ".config", "opencode")];
        var actual = Ready.WritableSet(launch, profile, home, environment, common);
        await Assert.That(actual).IsEquivalentTo(expected.Concat(new[] { profile, temp, "/dev" })
            .Concat(access == "workspace" ? new[] { workspace, common, cache } : []), CollectionOrdering.Matching);
        await Assert.That(actual).DoesNotContain(Locations.ControlRoot);
        await Assert.That(actual).DoesNotContain(Locations.LedgerRoot);
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
