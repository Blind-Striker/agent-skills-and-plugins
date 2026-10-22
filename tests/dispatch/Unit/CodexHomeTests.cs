using Win32Exception = System.ComponentModel.Win32Exception;
using Dispatch.Tests.Support;
using Tomlyn;
using Tomlyn.Model;

namespace Dispatch.Tests.Unit;

[Category("Unit")]
public sealed class CodexHomeTests
{
    [Test]
    public async Task Clean_home_tries_a_sibling_when_temp_is_on_another_volume()
    {
        using var tree = new TempTree();
        var userHome = Directory.CreateDirectory(Path.Combine(tree.Root, "user-codex")).FullName;
        File.WriteAllText(Path.Combine(userHome, "auth.json"), "synthetic-auth");
        using var home = CodexHome.Create(new(Path.Combine(tree.Root, "profiles"), null, new Dictionary<string, string>()),
            Launch(tree.Repository), userHome, createHardLink: (destination, source) =>
            {
                if (destination.StartsWith(Path.Combine(Path.GetTempPath(), "deniz-dispatch-codex"), StringComparison.Ordinal))
                    throw new Win32Exception(17);
                File.Copy(source, destination);
            });

        await Assert.That(home.Profile).IsEqualTo("clean");
        await Assert.That(home.Home).StartsWith(Path.Combine(tree.Root, ".dispatch-codex") + Path.DirectorySeparatorChar);
        home.Dispose();
        await Assert.That(Directory.Exists(home.Home)).IsFalse();
        await Assert.That(File.ReadAllText(Path.Combine(userHome, "auth.json"))).IsEqualTo("synthetic-auth");
    }

    [Test]
    public async Task Clean_home_uses_system_temp_instead_of_dispatch_profiles()
    {
        using var tree = new TempTree();
        var userHome = Directory.CreateDirectory(Path.Combine(tree.Root, "user-codex")).FullName;
        File.WriteAllText(Path.Combine(userHome, "auth.json"), "synthetic-auth");
        var profiles = Path.Combine(tree.Root, "profiles");
        using var home = CodexHome.Create(new(profiles, null, new Dictionary<string, string>()), Launch(tree.Repository), userHome);

        await Assert.That(home.Profile).IsEqualTo("clean");
        await Assert.That(home.Home).StartsWith(Path.Combine(Path.GetTempPath(), "deniz-dispatch-codex") + Path.DirectorySeparatorChar);
        await Assert.That(Directory.Exists(profiles)).IsFalse();
        File.WriteAllText(Path.Combine(userHome, "auth.json"), "synthetic-refresh");
        await Assert.That(File.ReadAllText(Path.Combine(home.Home, "auth.json"))).IsEqualTo("synthetic-refresh");
    }

    [Test]
    [Arguments(".sandbox/setup_marker.json")]
    [Arguments(".sandbox-secrets/sandbox_users.json")]
    public async Task Sandbox_state_link_failure_records_clean_partial_and_removes_incomplete_home(string failing)
    {
        if (!OperatingSystem.IsWindows()) Skip.Test("Windows sandbox setup is Windows-only.");
        using var tree = new TempTree();
        var userHome = Directory.CreateDirectory(Path.Combine(tree.Root, "user-codex")).FullName;
        foreach (var relative in new[] { "auth.json", ".sandbox/setup_marker.json", ".sandbox-secrets/sandbox_users.json" })
        {
            var path = Path.Combine(userHome, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "synthetic-state");
        }
        var profiles = Path.Combine(tree.Root, "profiles");
        using var home = CodexHome.Create(new(profiles, null, new Dictionary<string, string>()), Launch(tree.Repository), userHome,
            "elevated", createHardLink: (destination, source) =>
            {
                if (source == Path.Combine(userHome, failing)) throw new Win32Exception(5);
                // Stand in for successful link operations before the injected filesystem failure.
                File.Copy(source, destination);
            });
        await Assert.That(home.Profile).IsEqualTo("clean-partial");
        await Assert.That(home.Reason).Contains(failing);
        await Assert.That(home.Reason).Contains("OS error 5");
        await Assert.That(home.Home).IsEqualTo(userHome);
        await Assert.That(home.Temporary).IsFalse();
        await Assert.That(Directory.Exists(Path.Combine(profiles, "codex-task"))).IsFalse();
        foreach (var relative in new[] { "auth.json", ".sandbox/setup_marker.json", ".sandbox-secrets/sandbox_users.json" })
            await Assert.That(File.ReadAllText(Path.Combine(userHome, relative))).IsEqualTo("synthetic-state");
    }

    [Test]
    [Arguments("elevated")]
    [Arguments("unelevated")]
    public async Task Minimal_home_retains_sandbox_mode_when_used_as_source_for_another_clean_home(string sandbox)
    {
        if (!OperatingSystem.IsWindows()) Skip.Test("Windows sandbox selection is Windows-only.");
        using var tree = new TempTree();
        var userHome = Directory.CreateDirectory(Path.Combine(tree.Root, "user-codex")).FullName;
        File.WriteAllText(Path.Combine(userHome, "auth.json"), "synthetic-auth");
        var context = new BackendContext(Path.Combine(tree.Root, "profiles"), null, new Dictionary<string, string>());
        using var home = CodexHome.CreateProbe(context, userHome, tree.Repository, windowsSandbox: sandbox);
        await Assert.That(CodexHome.ReadWindowsSandbox(home.Home)).IsEqualTo(sandbox);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Clean_home_links_only_sandbox_readiness_files_and_cleanup_preserves_sources(bool probe)
    {
        if (!OperatingSystem.IsWindows()) Skip.Test("Windows sandbox setup is Windows-only.");
        using var tree = new TempTree();
        var userHome = Directory.CreateDirectory(Path.Combine(tree.Root, "user-codex")).FullName;
        File.WriteAllText(Path.Combine(userHome, "auth.json"), "synthetic-auth");
        var state = new[] { ".sandbox/setup_marker.json", ".sandbox-secrets/sandbox_users.json" };
        foreach (var relative in state)
        {
            var path = Path.Combine(userHome, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "synthetic-state");
        }
        var excluded = new[] { "AGENTS.md", "config.toml", "rules/canary.rules", "skills/canary/SKILL.md", "history.jsonl",
            ".sandbox/logs/canary.log", ".sandbox-secrets/unrelated.json", ".sandbox-bin/canary.exe", "cap_sid" };
        foreach (var relative in excluded)
        {
            var path = Path.Combine(userHome, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "user-context-canary");
        }
        var context = new BackendContext(Path.Combine(tree.Root, "profiles"), null, new Dictionary<string, string>());
        using var home = probe ? CodexHome.CreateProbe(context, userHome, tree.Repository, windowsSandbox: "elevated")
            : CodexHome.Create(context, Launch(tree.Repository), userHome, "elevated");
        await Assert.That(home.Profile).IsEqualTo("clean");
        foreach (var relative in state)
        {
            var target = Path.Combine(home.Home, relative);
            await Assert.That(File.Exists(target)).IsTrue();
            // Synthetic source writes prove file identity without reading real sandbox credentials.
            File.WriteAllText(Path.Combine(userHome, relative), "synthetic-updated-state");
            await Assert.That(File.ReadAllText(target)).IsEqualTo("synthetic-updated-state");
        }
        foreach (var relative in excluded.Where(path => path != "config.toml"))
            await Assert.That(Path.Exists(Path.Combine(home.Home, relative))).IsFalse();
        await Assert.That(File.ReadAllText(Path.Combine(home.Home, "config.toml"))).DoesNotContain("user-context-canary");
        home.Dispose();
        await Assert.That(Directory.Exists(home.Home)).IsFalse();
        foreach (var relative in state)
            await Assert.That(File.ReadAllText(Path.Combine(userHome, relative))).IsEqualTo("synthetic-updated-state");
        foreach (var relative in excluded)
            await Assert.That(File.ReadAllText(Path.Combine(userHome, relative))).IsEqualTo("user-context-canary");
    }

    [Test]
    public async Task Clean_home_without_file_auth_records_clean_partial_and_keeps_runtime_limits()
    {
        using var tree = new TempTree();
        var userHome = Path.Combine(tree.Root, "user-codex");
        var profiles = Path.Combine(tree.Root, "control", "owner", "profiles");
        using var home = CodexHome.Create(new(profiles, null, new Dictionary<string, string>()), Launch(tree.Repository), userHome);

        await Assert.That(home.Profile).IsEqualTo("clean-partial");
        await Assert.That(home.Reason).Contains("auth.json");
        await Assert.That(home.Home).IsEqualTo(userHome);
        await Assert.That(home.Temporary).IsFalse();
        await Assert.That(Directory.Exists(Path.Combine(profiles, "codex-task"))).IsFalse();
        await Assert.That(home.RuntimeConfig["include_skill_instructions"]!.GetValue<bool>()).IsFalse();
        await Assert.That(home.RuntimeConfig["project_doc_max_bytes"]!.GetValue<int>()).IsEqualTo(0);
        foreach (var name in new[] { "apps", "plugins", "remote_plugin" })
            await Assert.That(home.RuntimeConfig["features"]![name]!.GetValue<bool>()).IsFalse();
        await Assert.That(home.RuntimeConfig.ContainsKey("cli_auth_credentials_store")).IsFalse();
    }

    [Test]
    [Arguments(17)]
    [Arguments(50)]
    [Arguments(5)]
    public async Task Clean_home_link_failure_on_another_controlled_root_records_reason_and_never_copies(int error)
    {
        using var tree = new TempTree();
        var userHome = Directory.CreateDirectory(Path.Combine(tree.Root, "auth-root", "user-codex")).FullName;
        var auth = Path.Combine(userHome, "auth.json");
        File.WriteAllText(auth, "synthetic-auth");
        var userConfig = Path.Combine(userHome, "config.toml");
        File.WriteAllText(userConfig, "model = 'canary'\n");
        var profiles = Path.Combine(tree.Root, "profile-root", "control", "owner", "profiles");
        using var home = CodexHome.Create(new(profiles, null, new Dictionary<string, string>()), Launch(tree.Repository), userHome,
            createHardLink: (destination, source) =>
            {
                if (source != auth || Path.GetFileName(destination) != "auth.json")
                    throw new InvalidOperationException("unexpected hardlink source or destination");
                throw new Win32Exception(error, "synthetic hardlink failure");
            });

        await Assert.That(home.Profile).IsEqualTo("clean-partial");
        await Assert.That(home.Reason).Contains(error.ToString(System.Globalization.CultureInfo.InvariantCulture));
        await Assert.That(home.Reason).Contains("source volume");
        await Assert.That(home.Reason).Contains("temp volume");
        await Assert.That(home.Reason).Contains("sibling volume");
        await Assert.That(home.Home).IsEqualTo(userHome);
        await Assert.That(home.Temporary).IsFalse();
        await Assert.That(Directory.Exists(Path.Combine(profiles, "codex-task"))).IsFalse();
        await Assert.That(home.RuntimeConfig["include_skill_instructions"]!.GetValue<bool>()).IsFalse();
        await Assert.That(home.RuntimeConfig["features"]!["plugins"]!.GetValue<bool>()).IsFalse();
        home.Dispose();
        await Assert.That(File.ReadAllText(auth)).IsEqualTo("synthetic-auth");
        await Assert.That(File.ReadAllText(userConfig)).IsEqualTo("model = 'canary'\n");
    }

    [Test]
    [Arguments("full", null)]
    [Arguments("clean", "probe home is signed out")]
    [Arguments("clean", "probe sandbox is not ready")]
    public async Task Full_or_frozen_partial_uses_inherited_home_without_trying_a_link(string profile, string? reason)
    {
        using var tree = new TempTree();
        var userHome = Directory.CreateDirectory(Path.Combine(tree.Root, "user-codex")).FullName;
        File.WriteAllText(Path.Combine(userHome, "auth.json"), "synthetic-auth");
        var profiles = Path.Combine(tree.Root, "control", "owner", "profiles");
        using var home = CodexHome.Create(new(profiles, null, new Dictionary<string, string>()),
            Launch(tree.Repository, profile: profile, actual: reason is null ? "full" : "clean-partial", reason: reason), userHome,
            createHardLink: (_, _) => throw new InvalidOperationException("inherited homes must not try a link"));

        await Assert.That(home.Profile).IsEqualTo(reason is null ? "full" : "clean-partial");
        await Assert.That(home.Reason).IsEqualTo(reason);
        await Assert.That(home.Home).IsEqualTo(userHome);
        await Assert.That(home.Temporary).IsFalse();
        await Assert.That(Directory.Exists(profiles)).IsFalse();
        await Assert.That(home.RuntimeConfig.ContainsKey("include_skill_instructions")).IsEqualTo(profile == "clean");
        await Assert.That(home.RuntimeConfig.ContainsKey("features")).IsEqualTo(profile == "clean");
        await Assert.That(home.RuntimeConfig["project_doc_max_bytes"]!.GetValue<int>()).IsEqualTo(0);
        home.Dispose();
        await Assert.That(File.ReadAllText(Path.Combine(userHome, "auth.json"))).IsEqualTo("synthetic-auth");
    }

    [Test]
    [Arguments("on")]
    [Arguments("off")]
    public async Task Runtime_and_TOML_overrides_round_trip_a_dotted_folder_as_one_key(string project)
    {
        using var tree = new TempTree();
        var cwd = Path.Combine(tree.Repository, ".claude", "worktrees", "a.b ' \" 世界\tline\nend");
        using var home = CodexHome.Create(new(tree.Root, null, new Dictionary<string, string>()),
            Launch(cwd, project), Path.Combine(tree.Root, "user-codex"));
        var values = home.ConfigOverrides;
        await Assert.That(values.Count).IsGreaterThan(0);
        // Codex splits the override's left-hand side on every dot. Only top-level keys are safe.
        foreach (var assignment in values)
            await Assert.That(assignment[..assignment.IndexOf('=')]).DoesNotContain(".");
        var parsed = TomlSerializer.Deserialize<TomlTable>(string.Join("\n", values))!;
        var projects = (TomlTable)parsed["projects"]!;
        await Assert.That(projects.Count).IsEqualTo(1);
        var key = projects.Keys.Single();
        await Assert.That(string.Equals(key, cwd, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)).IsTrue();
        await Assert.That((string)((TomlTable)projects[key]!)["trust_level"]!).IsEqualTo(project == "on" ? "trusted" : "untrusted");
        await Assert.That(home.RuntimeConfig["projects"]!.AsObject().Count).IsEqualTo(1);
        await Assert.That(home.RuntimeConfig["projects"]![key]!["trust_level"]!.GetValue<string>()).IsEqualTo(project == "on" ? "trusted" : "untrusted");
        await Assert.That((bool)parsed["include_skill_instructions"]!).IsFalse();
        await Assert.That((bool)((TomlTable)parsed["features"]!)["remote_plugin"]!).IsFalse();
    }

    [Test]
    public async Task Marker_recognizes_a_filesystem_alias_but_not_an_unrelated_or_changed_home()
    {
        using var tree = new TempTree();
        var userHome = Directory.CreateDirectory(Path.Combine(tree.Root, "user-codex")).FullName;
        File.WriteAllText(Path.Combine(userHome, "auth.json"), "synthetic-auth");
        var profiles = Path.Combine(tree.Root, "control", "owner", "profiles");
        using var home = CodexHome.Create(new(profiles, null, new Dictionary<string, string>()), Launch(tree.Repository), userHome);
        var alias = home.Home.ToUpperInvariant();
        if (!OperatingSystem.IsWindows())
        {
            alias = Path.Combine(tree.Root, "home-alias");
            Directory.CreateSymbolicLink(alias, home.Home);
        }
        await Assert.That(home.MatchesReportedHome(alias)).IsTrue();
        await Assert.That(home.MatchesReportedHome(userHome)).IsFalse();
        var marker = Directory.GetFiles(home.Home, ".dispatch-home-*").Single();
        var original = File.ReadAllText(marker);
        File.WriteAllText(marker, "{\"run\":\"another-run\",\"task\":\"task\"}\n");
        await Assert.That(home.MatchesReportedHome(alias)).IsFalse();
        File.Delete(marker);
        await Assert.That(home.MatchesReportedHome(alias)).IsFalse();
        await Assert.ThrowsAsync<HarnessException>(() => Task.Run(home.Dispose));
        await Assert.That(Directory.Exists(home.Home)).IsTrue();
        File.WriteAllText(marker, original);
    }

    [Test]
    public async Task An_existing_home_is_not_adopted_overwritten_or_deleted()
    {
        using var tree = new TempTree();
        var userHome = Directory.CreateDirectory(Path.Combine(tree.Root, "user-codex")).FullName;
        File.WriteAllText(Path.Combine(userHome, "auth.json"), "synthetic-auth");
        var profiles = Path.Combine(tree.Root, "control", "owner", "profiles");
        using var first = CodexHome.Create(new(profiles, null, new Dictionary<string, string>()), Launch(tree.Repository), userHome);
        var existing = first.Home;
        var canary = Path.Combine(existing, "canary");
        File.WriteAllText(canary, "other owner");
        using var home = CodexHome.Create(new(profiles, null, new Dictionary<string, string>()), Launch(tree.Repository), userHome,
            createHardLink: (destination, _) =>
            {
                if (Path.GetDirectoryName(destination) == existing) throw new InvalidOperationException("must not write into an existing home");
                throw new Win32Exception(5);
            });
        await Assert.That(home.Profile).IsEqualTo("clean-partial");
        await Assert.That(home.Reason).Contains("exists");
        home.Dispose();
        await Assert.That(File.ReadAllText(canary)).IsEqualTo("other owner");
        await Assert.That(first.MatchesReportedHome(existing)).IsTrue();
    }

    [Test]
    [Arguments("../escape")]
    [Arguments("task/subfolder")]
    public async Task An_unsafe_task_id_cannot_create_a_profile_outside_its_fixed_subfolder(string task)
    {
        using var tree = new TempTree();
        var userHome = Directory.CreateDirectory(Path.Combine(tree.Root, "user-codex")).FullName;
        File.WriteAllText(Path.Combine(userHome, "auth.json"), "synthetic-auth");
        var profiles = Path.Combine(tree.Root, "control", "owner", "profiles");
        await Assert.ThrowsAsync<ArgumentException>(() => Task.Run(() =>
        {
            using var home = CodexHome.Create(new(profiles, null, new Dictionary<string, string>()), Launch(tree.Repository, task: task), userHome);
        }));
        await Assert.That(Directory.Exists(profiles)).IsFalse();
    }

    [Test]
    public async Task Failed_config_write_removes_the_incomplete_home()
    {
        using var tree = new TempTree();
        var userHome = Directory.CreateDirectory(Path.Combine(tree.Root, "user-codex")).FullName;
        var auth = Path.Combine(userHome, "auth.json");
        File.WriteAllText(auth, "synthetic-auth");
        var profiles = Path.Combine(tree.Root, "control", "owner", "profiles");
        var writeFailed = false;
        try
        {
            using var home = CodexHome.Create(new(profiles, null, new Dictionary<string, string>()), Launch(tree.Repository), userHome,
                createHardLink: (destination, _) => Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(destination)!, "config.toml")));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            writeFailed = true;
        }
        await Assert.That(writeFailed).IsTrue();
        await Assert.That(Directory.Exists(Path.Combine(profiles, "codex-task"))).IsFalse();
        await Assert.That(File.ReadAllText(auth)).IsEqualTo("synthetic-auth");
    }

    internal static WorkerLaunch Launch(string directory, string project = "off", string profile = "clean",
        string? actual = null, string? reason = null, string task = "task") =>
        new("run", task, "structured", new("codex", "model-y", null), null, null,
            new(profile, project, "read-only", "on", "worktree"), directory,
            new([], []), new Dictionary<string, string>(), null, actual ?? profile, reason);
}
