using System.Text;
using Dispatch.Tests.Support;

namespace Dispatch.Tests.Unit;

[Category("Unit")]
public sealed class ConfigExamplesTests
{
    [Test]
    public async Task ExamplePolicy_DenyTable()
    {
        var config = ConfigLayers.Read(System.IO.File.ReadAllBytes(Path.Combine(TestPaths.Harness, "examples", "config.jsonc")), "user", "example config");
        var policy = ConfigLayers.Read(System.IO.File.ReadAllBytes(Path.Combine(TestPaths.Harness, "examples", "policy.jsonc")), "cli", "example policy");
        await Assert.That(config.Restrictions?.Routes).IsNull();
        await Assert.That(policy.Restrictions?.Routes).IsNull();
        var deny = policy.Restrictions!.Deny!;
        await Assert.That(deny.Select(r => r.Name!).ToArray()).IsEquivalentTo(new[] { "no-git-push", "no-gh" });
        var gitRule = deny.Single(r => r.Name == "no-git-push");
        var ghRule = deny.Single(r => r.Name == "no-gh");
        await Assert.That(gitRule.Resources!).IsEquivalentTo(new[]
        {
            "git push*", "git * push*", "git.exe push*", "git.exe * push*",
            "*/git push*", "*/git * push*", "*/git.exe push*", "*/git.exe * push*",
            "*/git.exe\" push*", "*/git.exe\" * push*",
        });
        await Assert.That(ghRule.Resources!).IsEquivalentTo(new[] { "gh *", "gh.exe *", "*/gh *", "*/gh.exe *", "*/gh.exe\" *" });

        var commands = new (string Command, bool IgnoreCaseExpected, bool CaseSensitiveExpected)[]
        {
            ("git push", true, true),
            ("git push origin main", true, true),
            ("git -C d push", true, true),
            ("git -c user.name=Alice push", true, true),
            ("git.exe push", true, true),
            ("git.exe -C d push", true, true),
            ("/bin/git push", true, true),
            ("/bin/git -C d push", true, true),
            ("C:/Git/git.exe push", true, true),
            ("C:/Git/git.exe -C d push", true, true),
            ("\"C:/Git/git.exe\" push", true, true),
            ("\"C:/Git/git.exe\" -C d push", true, true),
            ("/usr/bin/git -C d push", true, true),
            ("C:/Git/cmd/git.exe -C d push", true, true),
            ("git stash push", true, true),
            ("git log --grep push", true, true),
            ("git commit -m 'fix push'", true, true),
            ("gh", true, true),
            ("gh pr create", true, true),
            ("gh.exe pr create", true, true),
            ("/bin/gh issue list", true, true),
            ("C:/Git/gh.exe pr create", true, true),
            ("\"C:/Git/gh.exe\" pr create", true, true),
            ("GIT PUSH", true, false),
            ("GH PR CREATE", true, false),
            ("git status", false, false),
            ("C:/Git/cmd/git.exe status", false, false),
            ("git -c alias.p=push p", false, false),
            ("cd x && git push", false, false),
            ("echo ready", false, false),
        };

        foreach (var ignoreCase in new[] { true, false })
        foreach (var (command, ignoreCaseExpected, caseSensitiveExpected) in commands)
        {
            var denied = deny.Any(rule => Wildcard.Match("shell", rule.Action!, ignoreCase)
                && rule.Resources!.Any(pattern => Wildcard.Match(command, pattern, ignoreCase)));
            await Assert.That(denied).IsEqualTo(ignoreCase ? ignoreCaseExpected : caseSensitiveExpected);
        }
    }

    [Test]
    [Arguments("repo", "backends", "{\"opencode\":{\"env\":[\"EXAMPLE_KEY\"]}}")]
    [Arguments("repo", "locations", "{}")]
    [Arguments("cli", "backends", "{\"codex\":{\"env\":[]}}")]
    [Arguments("cli", "preferences", "{\"backend\":\"codex\"}")]
    [Arguments("cli", "restrictions", "{\"routes\":[]}")]
    [Arguments("cli", "restrictions", "{\"scrub\":[\"^EXAMPLE_\"]}")]
    public async Task Layer_only_members_name_the_file_member_and_layer(string layer, string member, string value)
    {
        var error = await Assert.ThrowsAsync<HarnessException>(() => Task.FromResult(ConfigLayers.Read(
            Encoding.UTF8.GetBytes($$"""{"schemaVersion":2,"{{member}}":{{value}}}"""), layer, "example.jsonc")));
        await Assert.That(error!.Message).Contains("example.jsonc");
        await Assert.That(error.Message).Contains(layer);
        await Assert.That(error.Message).Contains(member == "restrictions" ? value.Contains("routes") ? "routes" : "scrub" : member);
    }
}
