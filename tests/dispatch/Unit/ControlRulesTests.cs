using OpenCode.Sdk.Models;

namespace Dispatch.Tests.Unit;

[Category("Unit")]
public sealed class ControlRulesTests
{
    [Test]
    public async Task OpenCodeRules_DefaultLocations_WorktreeEditAllowedControlEditDenied()
    {
        var rules = OpenCodeRules.Render(new WorkerRules([], []), "example");
        PermissionEffect Effect(string path) => rules.LastOrDefault(x => x.Action == "edit" && Wildcard.Match(path, x.Resource))?.Effect ?? PermissionEffect.Allow;
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
