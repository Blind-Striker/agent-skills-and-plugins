using System.Text;
using Dispatch.Tests.Support;

namespace Dispatch.Tests.Unit;

[Category("Unit")]
public sealed class ConfigExamplesTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Shipped_examples_parse_strictly_and_ship_no_route_list(bool ignoreCase)
    {
        var config = ConfigLayers.Read(System.IO.File.ReadAllBytes(Path.Combine(TestPaths.Harness, "examples", "config.jsonc")), "user", "example config");
        var policy = ConfigLayers.Read(System.IO.File.ReadAllBytes(Path.Combine(TestPaths.Harness, "examples", "policy.jsonc")), "cli", "example policy");
        await Assert.That(config.Restrictions?.Routes).IsNull();
        await Assert.That(policy.Restrictions?.Routes).IsNull();
        await Assert.That(policy.Restrictions!.Deny!.Select(r => r.Name!).ToArray()).IsEquivalentTo(new[] { "no-git-push", "no-gh" });
        var resources = policy.Restrictions.Deny!.SelectMany(r => r.Resources!).ToArray();
        await Assert.That(resources.Length).IsEqualTo(15);
        foreach (var command in new[] { "git push", "git -C folder push", "git.exe push", "git.exe -C folder push",
                     "/bin/git push", "/bin/git -C folder push", "C:/Git/git.exe push", "C:/Git/git.exe -C folder push",
                     "\"C:/Git/git.exe\" push", "\"C:/Git/git.exe\" -C folder push", "gh", "gh.exe pr create",
                     "/bin/gh issue list", "C:/Git/gh.exe pr create", "\"C:/Git/gh.exe\" pr create" })
            await Assert.That(resources.Any(p => Wildcard.Match(command, p, ignoreCase))).IsTrue();
        await Assert.That(resources.Any(p => Wildcard.Match("GIT PUSH", p, ignoreCase))).IsEqualTo(ignoreCase);
        await Assert.That(resources.Any(p => Wildcard.Match("git status", p, ignoreCase))).IsFalse();
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
