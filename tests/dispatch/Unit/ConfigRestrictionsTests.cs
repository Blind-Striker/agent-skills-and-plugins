namespace Dispatch.Tests.Unit;

[Category("Unit")]
public sealed class ConfigRestrictionsTests
{
    static TaskFile FileFor(string kind = "code") => new() { Defaults = new() { Backend = "opencode" },
        Tasks = [new TaskSpec { Id = "work", Kind = kind, Route = "fixture/model", Scope = ["src/**", "!src/private/**"] }] };

    [Test]
    public async Task Duplicate_task_ids_are_a_refusal_not_an_unhandled_dictionary_error()
    {
        var file = FileFor();
        file.Tasks!.Add(new TaskSpec { Id = "work", Kind = "research", Route = "fixture/model" });
        await Assert.That(() => ConfigResolver.Resolve(file)).Throws<HarnessException>();
    }

    [Test]
    public async Task Deny_and_escalate_names_must_be_unique_within_a_layer()
    {
        PolicyRule Rule() => new() { Name = "same", Action = "shell", Resources = ["git push*"] };
        await Assert.That(() => ConfigResolver.Resolve(FileFor(), new() { Restrictions = new() { Deny = [Rule()], Escalate = [Rule()] } }))
            .Throws<HarnessException>();
    }

    [Test]
    [Arguments("access")]
    [Arguments("network")]
    public async Task Explicit_preferences_cannot_lift_an_earlier_ceiling(string preference)
    {
        var file = FileFor();
        var user = new ConfigFile { Restrictions = new() { NetworkAllowed = false } };
        var repo = new ConfigFile { Restrictions = new() { MaxAccess = "workspace" } };
        if (preference == "access") file.Tasks![0].Access = "full";
        else file.Tasks![0].Network = "on";
        var error = await Assert.ThrowsAsync<HarnessException>(() => Task.FromResult(ConfigResolver.Resolve(file, user, repo)));
        await Assert.That(error!.Message).Contains("task");
        await Assert.That(error.Message).Contains(preference == "access" ? "repo" : "user");
    }

    [Test]
    public async Task Denies_scopes_and_instructions_accumulate_without_reopening()
    {
        var file = FileFor();
        PolicyRule Rule() => new() { Name = "same", Action = "shell", Resources = ["git push*"] };
        var user = new ConfigFile { Restrictions = new() { Deny = [Rule()], Scope = ["!src/vendor/**"] }, Instructions = ["First", "Repeat"] };
        var repo = new ConfigFile { Restrictions = new() { Escalate = [Rule()], Scope = ["src/allowed/**"] }, Instructions = ["Repeat", "Second"] };
        file.Policy = new() { Deny = [Rule()], Scope = ["!src/allowed/hidden/**"], Instructions = ["Third"] };
        var config = ConfigResolver.Resolve(file, user, repo, policy: new() { Restrictions = new() { Deny = [Rule()] }, Instructions = ["Fourth"] });
        await Assert.That(config.Restrictions!.Deny.Select(r => r.Name).ToArray()).IsEquivalentTo(new[] { "user/same", "task-file/same", "cli/same" });
        var rules = OpenCodeRules.Render(TaskStarter.RulesFor(Policy.From(config)));
        await Assert.That(rules[0].Effect).IsEqualTo(OpenCode.Sdk.Models.PermissionEffect.Ask);
        await Assert.That(rules.Skip(1).All(r => r.Effect == OpenCode.Sdk.Models.PermissionEffect.Deny)).IsTrue();
        await Assert.That(config.Instructions.Select(i => i.Text).ToArray()).IsEquivalentTo(new[] { "First", "Repeat", "Second", "Third", "Fourth" });
        var scope = config.Tasks["work"].Settings.Scope;
        foreach (var path in new[] { "docs/readme.md", "src/private/x", "src/vendor/x", "src/else/x", "src/allowed/hidden/x" })
            await Assert.That(ConfigResolver.InScope(path, scope)).IsFalse();
        await Assert.That(ConfigResolver.InScope("src/allowed/file.cs", scope)).IsTrue();
    }

    [Test]
    [Arguments("code", "full", "on", "workspace")]
    [Arguments("mechanical", "clean", "on", "workspace")]
    [Arguments("review", "clean", "on", "read-only")]
    [Arguments("research", "clean", "on", "read-only")]
    [Arguments("structured", "clean", "off", "read-only")]
    public async Task Defaults_are_per_kind_and_only_tool_defaults_are_clamped(string kind, string profile, string project, string access)
    {
        var file = FileFor(kind);
        var plain = ConfigResolver.Resolve(file).Tasks["work"].Settings;
        await Assert.That(plain.Profile!.Value).IsEqualTo(profile);
        await Assert.That(plain.Project!.Value).IsEqualTo(project);
        await Assert.That(plain.Access!.Value).IsEqualTo(access);
        await Assert.That(plain.Network!.Value).IsEqualTo("on");
        await Assert.That(plain.Commit!.From).IsEqualTo("worktree");
        var clamped = ConfigResolver.Resolve(file, repo: new() { Restrictions = new() { MaxAccess = "read-only", NetworkAllowed = false } }).Tasks["work"].Settings;
        await Assert.That(clamped.Access!.Value).IsEqualTo("read-only");
        await Assert.That(clamped.Network!.Value).IsEqualTo("off");
        await Assert.That(clamped.Network.From).IsEqualTo("tool, clamped by repo");
    }

    [Test]
    public async Task Route_lists_intersect_and_absence_is_not_an_empty_list()
    {
        var route = "opencode:fixture/model";
        ConfigFile List(params string[] entries) => new() { Restrictions = new() { Routes = entries.ToList() } };
        var file = FileFor();
        foreach (var (user, repo) in new (ConfigFile?, ConfigFile?)[] { (null, null), (List(route), null), (null, List(route)),
                     (List(route, "codex:other"), List(route, "opencode:fixture/other")) })
        {
            var config = ConfigResolver.Resolve(file, user, repo);
            await Assert.That(AllowList.From(config.Restrictions!.Routes).Allows(route)).IsTrue();
            await Assert.That(config.Restrictions.Routes is null).IsEqualTo(user is null && repo is null);
        }
        foreach (var (user, repo) in new[] { (List(), List(route)), (List(route), List()), (List(route), List("codex:model")) })
            await Assert.That(() => ConfigResolver.Resolve(file, user, repo)).Throws<HarnessException>();
        file.Tasks![0].Route = null;
        await Assert.That(() => ConfigResolver.Resolve(file)).Throws<HarnessException>();
    }

    [Test]
    public async Task Backend_selection_and_route_variants_obey_their_own_layers()
    {
        var file = FileFor("research");
        file.Defaults = new() { Backend = "codex", Effort = "high", DefaultRoutes = new() { ["codex"] = "model-y" } };
        file.Tasks![0].Route = "model-y#low";
        var user = new ConfigFile { Preferences = new() { Backend = "opencode", DefaultRoutes = new() { ["opencode"] = "fixture/unused" } } };
        var settings = ConfigResolver.Resolve(file, user).Tasks["work"].Settings;
        await Assert.That(settings.Backend!.Value).IsEqualTo("codex");
        await Assert.That(settings.Backend.From).IsEqualTo("task-file-defaults");
        await Assert.That(settings.Route!.Value).IsEqualTo("codex:model-y#low");
        await Assert.That(settings.Effort!.From).IsEqualTo("task");
        file.Tasks[0].Effort = "high";
        await Assert.That(() => ConfigResolver.Resolve(file, user)).Throws<HarnessException>();
    }
}
