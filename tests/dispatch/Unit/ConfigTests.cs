using System.Text.Json;
using Dispatch.Tests.Support;

namespace Dispatch.Tests.Unit;

[Category("Unit")]
public sealed class ConfigTests
{
    static TaskFile Tasks() => new() { Tasks = [new TaskSpec { Id = "inspect", Kind = "research", Route = "fixture/model" }] };

    [Test]
    public async Task OpenCode_model_tags_preserve_colons_and_only_backend_names_are_prefixes()
    {
        var file = Tasks();
        var task = file.Tasks![0];
        task.Backend = "opencode";
        task.Route = "ollama/model:tag";
        await Assert.That(ConfigResolver.Resolve(file).Tasks["inspect"].Settings.Route!.Value)
            .IsEqualTo("opencode:ollama/model:tag");
        task.Route = "opencode:ollama/model:tag";
        await Assert.That(ConfigResolver.Resolve(file).Tasks["inspect"].Settings.Route!.Value)
            .IsEqualTo("opencode:ollama/model:tag");
        task.Route = "codex:";
        var error = await Assert.ThrowsAsync<HarnessException>(() => Task.FromResult(ConfigResolver.Resolve(file)));
        await Assert.That(error!.Message).Contains("route backend codex");
        await Assert.That(error.Message).Contains("task backend opencode");
    }

    [Test]
    [Arguments("codex", "review", null, null, "clean")]
    [Arguments("codex", "research", null, null, "clean")]
    [Arguments("opencode", "review", null, null, "full")]
    [Arguments("opencode", "research", null, null, "full")]
    [Arguments("codex", "research", "workspace", null, "full")]
    [Arguments("codex", "code", "read-only", null, "clean")]
    [Arguments("codex", "code", null, "read-only", "clean")]
    [Arguments("codex", "code", null, null, "full")]
    [Arguments("codex", "mechanical", null, null, "full")]
    [Arguments("codex", "structured", null, null, "clean")]
    [Arguments("opencode", "structured", null, null, "clean")]
    public async Task Profile_default_follows_resolved_backend_and_access(string backend, string kind, string? access, string? ceiling, string profile)
    {
        var file = Tasks();
        file.Tasks![0].Kind = kind;
        var config = ConfigResolver.Resolve(file, repo: new() { Restrictions = new() { MaxAccess = ceiling } },
            cli: new() { Backend = backend, Access = access });
        var settings = config.Tasks["inspect"].Settings;
        await Assert.That(settings.Profile!.Value).IsEqualTo(profile);
        await Assert.That(settings.Profile.From).IsEqualTo("tool");
        await Assert.That(TaskStarter.ModesFor(new() { Spec = file.Tasks[0], Settings = settings }).Profile).IsEqualTo(profile);
        if (backend == "codex" && settings.Access!.Value == "read-only")
            await Assert.That(ConfigResolver.CapabilityNotes(settings).Single()).Contains("only system-level");
    }

    [Test]
    [Arguments("user")]
    [Arguments("repo")]
    [Arguments("task-file-defaults")]
    [Arguments("task")]
    [Arguments("cli")]
    public async Task Explicit_full_profile_overrides_read_only_codex_default(string layer)
    {
        var file = Tasks();
        file.Defaults = new() { Backend = "codex", Profile = layer == "task-file-defaults" ? "full" : null };
        file.Tasks![0].Profile = layer == "task" ? "full" : null;
        var settings = ConfigResolver.Resolve(file,
            user: new() { Preferences = new() { Profile = layer == "user" ? "full" : null } },
            repo: new() { Preferences = new() { Profile = layer == "repo" ? "full" : null } },
            cli: new() { Profile = layer == "cli" ? "full" : null }).Tasks["inspect"].Settings;
        await Assert.That(settings.Profile!.Value).IsEqualTo("full");
        await Assert.That(settings.Profile.From).IsEqualTo(layer);
        await Assert.That(ConfigResolver.CapabilityNotes(settings).Single()).Contains("user and system-level");
    }

    [Test]
    [Arguments("codex", "review", null, null, "clean")]
    [Arguments("codex", "research", null, null, "clean")]
    [Arguments("opencode", "review", null, null, "full")]
    [Arguments("opencode", "research", null, null, "full")]
    [Arguments("codex", "research", "workspace", null, "full")]
    [Arguments("codex", "code", "read-only", null, "clean")]
    [Arguments("codex", "research", null, "full", "full")]
    public async Task Worker_modes_preserve_profile_defaults_without_resolved_settings(string backend, string kind, string? access, string? explicitProfile, string profile)
    {
        var record = new TaskRecord { Spec = new() { Backend = backend, Kind = kind, Access = access, Profile = explicitProfile } };
        await Assert.That(TaskStarter.ModesFor(record).Profile).IsEqualTo(profile);
    }

    [Test]
    [Arguments("codex", "codex", "model-y#high")]
    [Arguments("opencode", "opencode", "fixture/model#high")]
    [Arguments("codex", "opencode", "fixture/model#high")]
    [Arguments("opencode", "codex", "model-y#high")]
    public async Task Qualified_task_routes_must_match_the_effective_backend(string backend, string routeBackend, string route)
    {
        var file = Tasks();
        var task = file.Tasks![0];
        task.Backend = backend;
        task.Route = $"{routeBackend}:{route}";
        if (backend != routeBackend)
        {
            var error = await Assert.ThrowsAsync<HarnessException>(() => Task.FromResult(ConfigResolver.Resolve(file)));
            await Assert.That(error!.Message).Contains($"route backend {routeBackend}");
            await Assert.That(error!.Message).Contains($"task backend {backend}");
            return;
        }

        var qualified = ConfigResolver.Resolve(file).Tasks["inspect"].Settings;
        await Assert.That(qualified.Route!.Value).IsEqualTo($"{backend}:{route}");
        task.Route = route;
        var bare = ConfigResolver.Resolve(file).Tasks["inspect"].Settings;
        await Assert.That(bare.Route!.Value).IsEqualTo(qualified.Route.Value);
        await Assert.That(bare.Effort!.Value).IsEqualTo("high");
        ConfigLayers.Apply(task, qualified);
        await Assert.That(task.Route).IsEqualTo(route);
    }

    [Test]
    public async Task Preferences_use_the_last_present_layer_and_keep_provenance()
    {
        for (var mask = 0; mask < 32; mask++)
        {
            var file = Tasks();
            var preferences = Enumerable.Range(0, 5).Select(i => new ConfigPreferences
            {
                Backend = "opencode", DefaultRoutes = new() { ["opencode"] = $"fixture/model{i}" },
                Effort = $"effort{i}", TimeoutMinutes = 10 + i, Profile = i % 2 == 0 ? "clean" : "full", MaxParallel = i + 1,
            }).ToArray();
            file.Tasks![0].Route = null;
            if ((mask & 4) != 0)
            {
                file.Defaults = new TaskDefaults { Backend = "opencode", DefaultRoutes = preferences[2].DefaultRoutes,
                    Effort = "effort2", TimeoutMinutes = 12, Profile = "clean" };
                file.MaxParallel = 3;
            }
            if ((mask & 8) != 0)
            {
                file.Tasks[0].Backend = "opencode";
                file.Tasks[0].Route = "fixture/model3";
                file.Tasks[0].Effort = "effort3";
                file.Tasks[0].TimeoutMinutes = 13;
                file.Tasks[0].Profile = "full";
            }
            // A route/backend are always required, even when every optional layer is absent.
            var user = new ConfigFile { Preferences = (mask & 1) != 0 ? preferences[0] : new ConfigPreferences
                { Backend = "opencode", DefaultRoutes = new() { ["opencode"] = "fixture/model" } } };
            var config = ConfigResolver.Resolve(file, user, (mask & 2) != 0 ? new ConfigFile { Preferences = preferences[1] } : null,
                (mask & 16) != 0 ? preferences[4] : null);
            var settings = config.Tasks["inspect"].Settings;
            var last = Enumerable.Range(0, 5).Where(i => (mask & (1 << i)) != 0).DefaultIfEmpty(-1).Last();
            var from = last < 0 ? "tool" : new[] { "user", "repo", "task-file-defaults", "task", "cli" }[last];
            await Assert.That(settings.TimeoutMinutes!.Value).IsEqualTo(last < 0 ? 60 : 10 + last);
            await Assert.That(settings.TimeoutMinutes.From).IsEqualTo(from);
            await Assert.That(settings.Backend!.Value).IsEqualTo("opencode");
            await Assert.That(settings.Backend.From).IsEqualTo(last < 0 ? "user" : from);
            await Assert.That(settings.Profile!.Value).IsEqualTo(last < 0 || last % 2 != 0 ? "full" : "clean");
            await Assert.That(settings.Profile.From).IsEqualTo(from);
            await Assert.That(settings.Effort?.Value).IsEqualTo(last < 0 ? null : $"effort{last}");
            if (last >= 0) await Assert.That(settings.Effort!.From).IsEqualTo(from);
            await Assert.That(settings.Route!.Value).IsEqualTo(last < 0 ? "opencode:fixture/model" : $"opencode:fixture/model{last}#effort{last}");
            await Assert.That(settings.Route.From).IsEqualTo(last < 0 ? "user" : from);
            var runLast = new[] { 0, 1, 2, 4 }.Where(i => (mask & (1 << i)) != 0).DefaultIfEmpty(-1).Last();
            await Assert.That(config.MaxParallel!.Value).IsEqualTo(runLast < 0 ? 3 : runLast + 1);
            await Assert.That(config.MaxParallel.From).IsEqualTo(runLast < 0 ? "tool" : new[] { "user", "repo", "task-file-defaults", "task", "cli" }[runLast]);
        }
    }
}
