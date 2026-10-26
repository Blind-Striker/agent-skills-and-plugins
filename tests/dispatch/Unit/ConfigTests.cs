using System.Text.Json;
using Dispatch.Tests.Support;
using TUnit.Assertions.Enums;

namespace Dispatch.Tests.Unit;

[Category("Unit")]
public sealed class ConfigTests
{
    static TaskFile Tasks() => new() { Tasks = [new TaskSpec { Id = "inspect", Kind = "research", Route = "fixture/model" }] };

    [Test]
    public async Task Environment_merges_names_with_sources()
    {
        var file = Tasks();
        file.Defaults = new() { Backend = "opencode", Environment = JsonSerializer.SerializeToElement(new { SHARED = "defaults", DEFAULT_ONLY = "d" }) };
        file.Tasks![0].Environment = JsonSerializer.SerializeToElement(new { SHARED = "task", TASK_ONLY = "t" });
        var settings = ConfigResolver.Resolve(file,
            new() { Preferences = new() { Environment = JsonSerializer.SerializeToElement(new { SHARED = "user", USER_ONLY = "u", REPO_WINS = "old" }) } },
            new() { Preferences = new() { Environment = JsonSerializer.SerializeToElement(new { SHARED = "repo", REPO_ONLY = "r", REPO_WINS = "new" }) } }).Tasks["inspect"].Settings;
        await Assert.That(settings.Environment!.Select(x => (x.Key, x.Value.Value!, x.Value.From))).IsEquivalentTo(new[]
        {
            ("SHARED", "task", "task"), ("USER_ONLY", "u", "user"), ("REPO_WINS", "new", "repo"),
            ("REPO_ONLY", "r", "repo"), ("DEFAULT_ONLY", "d", "task-file-defaults"), ("TASK_ONLY", "t", "task"),
        }, CollectionOrdering.Matching);
        file.Defaults.Environment = JsonSerializer.SerializeToElement(new { _name9 = "lower", EMPTY = "" });
        file.Tasks[0].Environment = JsonSerializer.SerializeToElement(new { _NAME9 = "upper" });
        var names = ConfigResolver.Resolve(file).Tasks["inspect"].Settings.Environment!;
        await Assert.That(names["_NAME9"].Value).IsEqualTo("upper");
        await Assert.That(names["_NAME9"].From).IsEqualTo("task");
        await Assert.That(names["_name9"].Value).IsEqualTo(OperatingSystem.IsWindows() ? "upper" : "lower");
        await Assert.That(names["EMPTY"].Value).IsEqualTo("");
    }

    [Test]
    public async Task Environment_rejects_invalid_layers_and_owned_names()
    {
        var invalid = new List<(string Json, string Error)>
        {
            ("null", "environment must be an object of variable names to strings"),
            ("[]", "environment must be an object of variable names to strings"),
            ("17", "environment must be an object of variable names to strings"),
            ("true", "environment must be an object of variable names to strings"),
            ("\"text\"", "environment must be an object of variable names to strings"),
            ("{\"VALID\":null}", "environment value for VALID must be a string"),
            ("{\"VALID\":1}", "environment value for VALID must be a string"),
            ("{\"VALID\":false}", "environment value for VALID must be a string"),
            ("{\"VALID\":[]}", "environment value for VALID must be a string"),
            ("{\"VALID\":{}}", "environment value for VALID must be a string"),
        };
        foreach (var name in new[] { "", "9NAME", "BAD-NAME", "BAD NAME", "é", "NAME\n" })
            invalid.Add((JsonSerializer.Serialize(new Dictionary<string, string> { [name] = "value" }), $"environment has an invalid variable name: {name}"));
        foreach (var name in new[] { "TEMP", "TMP", "TMPDIR", "XDG_RUNTIME_DIR", "XDG_CACHE_HOME", "GIT_TERMINAL_PROMPT", "GIT_INDEX_FILE", "GIT_OPTIONAL_LOCKS",
            "CODEX_HOME", "OPENCODE_CONFIG_DIR", "OPENCODE_CONFIG", "OPENCODE_CONFIG_CONTENT", "OPENCODE_DISABLE_CLAUDE_CODE",
            "OPENCODE_CONFIG_PROJECT_DISABLE", "OPENCODE_DISABLE_PROJECT_CONFIG", "OPENCODE_FILEWATCHER_DISABLE", "OPENCODE_DISABLE_FILEWATCHER" })
            invalid.Add((JsonSerializer.Serialize(new Dictionary<string, string> { [name] = "value" }), $"environment may not set {name}: the harness owns it"));
        if (OperatingSystem.IsWindows()) invalid.Add(("{\"temp\":\"value\"}", "environment may not set temp: the harness owns it"));
        foreach (var layer in new[] { "user", "repo", "task-file-defaults", "task" })
            foreach (var (input, diagnostic) in invalid)
            {
                var file = Tasks();
                file.Defaults = new() { Backend = "opencode" };
                var user = new ConfigFile { Preferences = new() };
                var repo = new ConfigFile { Preferences = new() };
                var preferences = layer switch { "user" => user.Preferences, "repo" => repo.Preferences, "task-file-defaults" => file.Defaults, _ => (TaskPreferences)file.Tasks![0] };
                // Invalid lower-layer entries must refuse even when every name is overridden later.
                file.Tasks![0].Environment = JsonSerializer.SerializeToElement(new { VALID = "override", TEMP = "override" });
                preferences.Environment = JsonSerializer.Deserialize<JsonElement>(input);
                var error = await Assert.ThrowsAsync<HarnessException>(() => Task.FromResult(ConfigResolver.Resolve(file, user, repo)));
                await Assert.That(error!.Message).IsEqualTo($"{layer}: {diagnostic}");
                if (layer is "task-file-defaults" or "task")
                {
                    error = await Assert.ThrowsAsync<HarnessException>(() => Task.FromResult(Json.ReadTaskFile(JsonSerializer.Serialize(file, InputJson.Read.TaskFile))));
                    await Assert.That(error!.Message).IsEqualTo($"{layer}: {diagnostic}");
                }
                else
                {
                    var config = layer == "user" ? user : repo;
                    error = await Assert.ThrowsAsync<HarnessException>(() => Task.FromResult(ConfigLayers.Read(
                        System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(config, InputJson.Read.ConfigFile)), layer, "input.jsonc")));
                    await Assert.That(error!.Message).IsEqualTo($"input.jsonc ({layer}): {layer}: {diagnostic}");
                }
            }
    }

    [Test]
    public async Task Environment_rejects_backend_prefixes_with_exact_diagnostic()
    {
        foreach (var layer in new[] { "user", "repo", "task-file-defaults", "task" })
            foreach (var name in new[] { "OPENCODE_PASSWORD", "OPENCODE_FUTURE_SWITCH", "CODEX_FUTURE_SWITCH", "OPENCODE_", "CODEX_", "opencode_password", "codex_future_switch" })
            {
                var preferences = new TaskPreferences { Environment = JsonSerializer.SerializeToElement(new Dictionary<string, string> { [name] = "synthetic" }) };
                HarnessException? failure = null;
                try { ConfigResolver.ValidateEnvironment(preferences, layer); }
                catch (HarnessException ex) { failure = ex; }
                await Assert.That(failure?.Message).IsEqualTo(char.IsUpper(name[0]) || OperatingSystem.IsWindows()
                    ? $"{layer}: environment may not set {name}: the harness owns it" : null);
            }
    }

    [Test]
    [NotInParallel]
    public async Task PrivateFolders_join_validate_and_warn(CancellationToken ct)
    {
        using var lab = new CoreLab("private-folders");
        var paths = new[] { "z-user", "a-repo", "y-defaults", "b-task" }.Select(x => Path.Combine(lab.Tree.Root, x)).ToList();
        var file = Tasks();
        file.Defaults = new() { Backend = "opencode", PrivateFolders = [paths[2], paths[0]] };
        file.Tasks![0].PrivateFolders = [paths[3]];
        var settings = ConfigResolver.Resolve(file, new() { Preferences = new() { PrivateFolders = [paths[0]] } },
            new() { Preferences = new() { PrivateFolders = [paths[1]] } }).Tasks["inspect"].Settings;
        await Assert.That(settings.PrivateFolders!.Value).IsEquivalentTo(paths, CollectionOrdering.Matching);
        await Assert.That(settings.PrivateFolders.From).IsEqualTo("user,repo,task-file-defaults,task");
        file.Defaults.PrivateFolders = null;
        foreach (var backend in new[] { "opencode", "codex" })
            foreach (var access in new[] { "read-only", "workspace", "full" })
                foreach (var nonempty in new[] { false, true })
                {
                    file.Defaults.Backend = backend;
                    file.Tasks[0].Access = access;
                    file.Tasks[0].PrivateFolders = nonempty ? [paths[0]] : [];
                    var config = ConfigResolver.Resolve(file);
                    var expected = new List<string>();
                    if (nonempty && !(OperatingSystem.IsLinux() && backend == "opencode" && access != "full"))
                        expected.Add("inspect: privateFolders is enforced only by the opencode fence on Linux");
                    if (backend == "codex" && access == "read-only")
                        expected.Add("inspect: The pinned Codex app-server cannot disable inherited exec-policy rules; with this profile, only system-level explicit allow rules can bypass the read-only sandbox without approval.");
                    await Assert.That(config.Warnings).IsEquivalentTo(expected, CollectionOrdering.Matching);
                }
        file.Defaults.Backend = "opencode";
        file.Tasks[0].PrivateFolders = ["relative"];
        var error = await Assert.ThrowsAsync<HarnessException>(() => Task.FromResult(ConfigResolver.Resolve(file)));
        await Assert.That(error!.Message).IsEqualTo("inspect: privateFolders from task must hold absolute paths: relative");
        foreach (var (folder, name) in new[] { (Locations.ControlRoot, "control root"), (Locations.LedgerRoot, "ledger root") })
            foreach (var path in new[] { Path.GetDirectoryName(folder)!, folder, Path.Combine(folder, "child") })
            {
                file.Tasks[0].PrivateFolders = [path];
                error = await Assert.ThrowsAsync<HarnessException>(() => Task.FromResult(ConfigResolver.Resolve(file)));
                var refused = path == Path.GetDirectoryName(folder) ? "control root" : name;
                await Assert.That(error!.Message).IsEqualTo($"inspect: privateFolders from task must not contain or lie inside the {refused}: {path}");
            }
        var link = Path.Combine(lab.Tree.Root, "private-alias");
        Directory.CreateDirectory(Locations.ControlRoot);
        if (OperatingSystem.IsWindows())
        {
            using var command = new CapturedProcess();
            (await command.RunAsync("cmd", ["/c", "mklink", "/J", link, Locations.ControlRoot], lab.Tree.Root, lab.Tree.ChildEnvironment(), ct)).RequireSuccess("junction fixture");
        }
        else Directory.CreateSymbolicLink(link, Locations.ControlRoot);
        try
        {
            file.Tasks[0].PrivateFolders = [link];
            error = await Assert.ThrowsAsync<HarnessException>(() => Task.FromResult(ConfigResolver.Resolve(file)));
            await Assert.That(error!.Message).IsEqualTo($"inspect: privateFolders from task must not contain or lie inside the control root: {link}");
        }
        finally { Directory.Delete(link); }
        var cache = Directory.CreateDirectory(paths[0]).FullName;
        if (OperatingSystem.IsWindows())
        {
            using var command = new CapturedProcess();
            (await command.RunAsync("cmd", ["/c", "mklink", "/J", link, cache], lab.Tree.Root, lab.Tree.ChildEnvironment(), ct)).RequireSuccess("junction fixture");
        }
        else Directory.CreateSymbolicLink(link, cache);
        try
        {
            file.Tasks[0].WritableRoots = [cache];
            foreach (var path in new[] { cache, link })
            {
                file.Tasks[0].PrivateFolders = [path];
                error = await Assert.ThrowsAsync<HarnessException>(() => Task.FromResult(ConfigResolver.Resolve(file)));
                await Assert.That(error!.Message).IsEqualTo($"inspect: privateFolders from task must not equal writableRoots: {path}");
            }
        }
        finally { Directory.Delete(link); }
    }

    [Test]
    [NotInParallel]
    public async Task PrivateFolders_refuse_worktree_and_temp_ancestors_with_layer()
    {
        using var lab = new CoreLab("private-roots");
        Locations.Configure(new() { WorktreeRoot = Path.Combine(lab.Tree.WorktreeRoot, "nested") }, null);
        var temp = Path.Combine(lab.Tree.Root, "separate-temp", "tasks");
        foreach (var layer in new[] { "user", "repo", "task-file-defaults", "task" })
            foreach (var (root, name) in new[] { (Locations.WorktreeRoot, "worktree root"), (temp, "temp root") })
                foreach (var path in new[] { root, Path.GetDirectoryName(root)! })
                {
                    var file = Tasks();
                    file.Defaults = new() { Backend = "opencode", TempRoot = temp };
                    var user = new ConfigFile { Preferences = new() };
                    var repo = new ConfigFile { Preferences = new() };
                    var prefs = layer switch { "user" => user.Preferences, "repo" => repo.Preferences,
                        "task-file-defaults" => file.Defaults, _ => (TaskPreferences)file.Tasks![0] };
                    prefs.PrivateFolders = [path];
                    var failure = await Assert.ThrowsAsync<HarnessException>(() => Task.FromResult(ConfigResolver.Resolve(file, user, repo)));
                    await Assert.That(failure!.Message).IsEqualTo($"inspect: privateFolders from {layer} must not contain or equal the {name}: {path}");
                }
    }

    [Test]
    public async Task WritableRoots_guard_joins_all_layers_in_order()
    {
        var root = Path.Combine(Path.GetTempPath(), "writable-roots");
        var paths = new[] { "z-user", "a-repo", "y-defaults", "b-task", "x-cli" }.Select(x => Path.Combine(root, x)).ToList();
        var file = Tasks();
        file.Defaults = new() { Backend = "opencode", WritableRoots = [paths[2], paths[0]] };
        file.Tasks![0].WritableRoots = [paths[3]];
        var settings = ConfigResolver.Resolve(file,
            new() { Preferences = new() { WritableRoots = [paths[0]] } },
            new() { Preferences = new() { WritableRoots = [paths[1]] } }, new() { WritableRoots = [paths[4]] }).Tasks["inspect"].Settings;
        await Assert.That(settings.WritableRoots!.Value).IsEquivalentTo(paths, CollectionOrdering.Matching);
        await Assert.That(settings.WritableRoots.From).IsEqualTo("user,repo,task-file-defaults,task,cli");
    }

    [Test]
    [NotInParallel]
    public async Task WritableRoots_guard_refuses_relative_and_control_or_ledger_overlaps()
    {
        using var lab = new CoreLab("writable-roots");
        var file = Tasks();
        file.Defaults = new() { Backend = "opencode" };
        file.Tasks![0].WritableRoots = ["relative"];
        var error = await Assert.ThrowsAsync<HarnessException>(() => Task.FromResult(ConfigResolver.Resolve(file)));
        await Assert.That(error!.Message).IsEqualTo("inspect: writableRoots from task must hold absolute paths: relative");
        foreach (var (folder, name) in new[] { (Locations.ControlRoot, "control root"), (Locations.LedgerRoot, "ledger root") })
            foreach (var path in new[] { Path.GetDirectoryName(folder)!, folder, Path.Combine(folder, "child") })
            {
                file.Tasks[0].WritableRoots = [path];
                error = await Assert.ThrowsAsync<HarnessException>(() => Task.FromResult(ConfigResolver.Resolve(file)));
                // The shared parent contains control first; equal/child roots identify their own protected folder.
                var refused = path == Path.GetDirectoryName(folder) ? "control root" : name;
                await Assert.That(error!.Message).IsEqualTo($"inspect: writableRoots from task must not contain or lie inside the {refused}: {path}");
            }
    }

    [Test]
    public async Task TempRoot_layers_preserve_value_and_source()
    {
        var root = Path.GetTempPath();
        for (var mask = 0; mask < 16; mask++)
        {
            var file = Tasks();
            file.Defaults = new() { Backend = "opencode", TempRoot = (mask & 4) != 0 ? Path.Combine(root, "defaults") : null };
            file.Tasks![0].TempRoot = (mask & 8) != 0 ? Path.Combine(root, "task") : null;
            var settings = ConfigResolver.Resolve(file,
                user: new() { Preferences = new() { TempRoot = (mask & 1) != 0 ? Path.Combine(root, "user") : null } },
                repo: new() { Preferences = new() { TempRoot = (mask & 2) != 0 ? Path.Combine(root, "repo") : null } }).Tasks["inspect"].Settings;
            var expected = (mask & 8) != 0 ? (Path.Combine(root, "task"), "task")
                : (mask & 4) != 0 ? (Path.Combine(root, "defaults"), "task-file-defaults")
                : (mask & 2) != 0 ? (Path.Combine(root, "repo"), "repo")
                : (mask & 1) != 0 ? (Path.Combine(root, "user"), "user") : ((string?)null, "tool");
            await Assert.That(settings.TempRoot?.Value).IsEqualTo(expected.Item1);
            await Assert.That(settings.TempRoot?.From).IsEqualTo(expected.Item2);
            ConfigLayers.Apply(file.Tasks[0], settings);
            await Assert.That(file.Tasks[0].TempRoot).IsEqualTo(expected.Item1);
        }
    }

    [Test]
    public async Task TempRoot_requires_absolute_path()
    {
        var paths = new[] { "relative", "", "~/temp", OperatingSystem.IsWindows() ? @"C:temp" : "./temp",
            OperatingSystem.IsWindows() ? @"\temp" : "../temp" };
        foreach (var path in paths)
        {
            var file = Tasks();
            file.Defaults = new() { Backend = "opencode", TempRoot = path };
            var error = await Assert.ThrowsAsync<HarnessException>(() => Task.FromResult(ConfigResolver.Resolve(file)));
            await Assert.That(error!.Message).IsEqualTo("tempRoot must be an absolute path");
        }
    }

    [Test]
    [NotInParallel]
    [Arguments("user")]
    [Arguments("repo")]
    [Arguments("task-file-defaults")]
    [Arguments("task")]
    public async Task TempRoot_refuses_worktree_and_ledger_roots_with_task_and_layer(string layer)
    {
        using var lab = new CoreLab("temp-containment");
        foreach (var (root, name) in new[] { (lab.Tree.WorktreeRoot, "worktree root"), (Locations.LedgerRoot, "ledger root") })
        {
            foreach (var path in new[] { root, Path.Combine(root, "private-temp"), Path.Combine(root, "other", "..", "private-temp") })
            {
                var file = Tasks();
                file.Defaults = new() { Backend = "opencode", TempRoot = layer == "task-file-defaults" ? path : null };
                file.Tasks![0].TempRoot = layer == "task" ? path : null;
                var error = await Assert.ThrowsAsync<HarnessException>(() => Task.FromResult(ConfigResolver.Resolve(file,
                    user: new() { Preferences = new() { TempRoot = layer == "user" ? path : null } },
                    repo: new() { Preferences = new() { TempRoot = layer == "repo" ? path : null } })));
                await Assert.That(error!.Message).IsEqualTo($"inspect: tempRoot from {layer} must be outside the {name}");
            }
        }
    }

    [Test]
    [NotInParallel]
    public async Task TempRoot_accepts_siblings_of_worktree_and_ledger_roots()
    {
        using var lab = new CoreLab("temp-siblings");
        foreach (var root in new[] { lab.Tree.WorktreeRoot, Locations.LedgerRoot })
        {
            var path = root + "-temp";
            var file = Tasks();
            file.Defaults = new() { Backend = "opencode", TempRoot = path };
            var setting = ConfigResolver.Resolve(file).Tasks["inspect"].Settings.TempRoot!;
            await Assert.That(setting.Value).IsEqualTo(path);
            await Assert.That(setting.From).IsEqualTo("task-file-defaults");
        }
    }

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
    [Arguments("opencode", "review", null, null, "clean")]
    [Arguments("opencode", "research", null, null, "clean")]
    [Arguments("codex", "research", "workspace", null, "clean")]
    [Arguments("codex", "code", "read-only", null, "clean")]
    [Arguments("codex", "code", null, "read-only", "clean")]
    [Arguments("codex", "code", null, null, "clean")]
    [Arguments("opencode", "code", null, null, "clean")]
    [Arguments("opencode", "mechanical", null, null, "clean")]
    [Arguments("opencode", "code", "full", null, "clean")]
    [Arguments("codex", "code", "full", null, "clean")]
    [Arguments("codex", "mechanical", null, null, "clean")]
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
    [Arguments("codex", "code", null, null, "full")]
    [Arguments("opencode", "code", null, null, "full")]
    [Arguments("codex", "mechanical", null, null, "full")]
    [Arguments("opencode", "mechanical", null, null, "full")]
    [Arguments("codex", "structured", null, null, "clean")]
    [Arguments("opencode", "structured", null, null, "clean")]
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
            await Assert.That(settings.Profile!.Value).IsEqualTo(last >= 0 && last % 2 != 0 ? "full" : "clean");
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
