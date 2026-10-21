using System.Text;
using System.Text.Json.Nodes;
using Dispatch.Tests.Support;
using Tomlyn;
using Tomlyn.Model;

namespace Dispatch.Tests.Integration;

[Category("Integration")]
[NotInParallel("processes")]
public sealed class CodexHomeTests
{
    [Test]
    [Timeout(180_000)]
    public async Task Probe_home_is_under_command_context_profiles_and_the_fake_selects_probe(CancellationToken cancellationToken)
    {
        using var tree = new TempTree();
        var environment = tree.ChildEnvironment().Where(pair => pair.Value is not null)
            .ToDictionary(pair => pair.Key, pair => pair.Value!);
        var userHome = environment["CODEX_HOME"];
        File.WriteAllText(Path.Combine(userHome, "auth.json"), "synthetic-auth");
        var profiles = Path.Combine(tree.DispatchHome, "state", "control", "cmd-owner", "profiles");
        var executable = Path.Combine(TestPaths.Tests, "Fakes", "FakeCodex", "bin",
            new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name, "net10.0", OperatingSystem.IsWindows() ? "codex.exe" : "codex");
        var context = new BackendContext(profiles, executable, new Dictionary<string, string>());
        using var home = CodexHome.CreateProbe(context, userHome, tree.Repository);
        await Assert.That(home.Home).IsEqualTo(Path.Combine(profiles, "probe-codex"));
        var scenarios = Directory.CreateDirectory(Path.Combine(tree.Root, "scenarios")).FullName;
        File.WriteAllText(Path.Combine(scenarios, "probe.jsonl"), "{\"notify\":\"ready\",\"params\":{}}\n");
        environment["FAKE_CODEX_SCENARIOS"] = scenarios;
        environment["FAKE_CODEX_RECORD"] = Path.Combine(tree.Root, "record");
        environment["CODEX_HOME"] = home.Home;
        await using (var process = CodexProcess.Start(context, tree.Repository, environment))
        {
            using var reader = new StreamReader(process.Stdout, Encoding.UTF8, false, leaveOpen: true);
            await Assert.That(await reader.ReadLineAsync(cancellationToken)).Contains("ready");
            await Assert.That((await process.StopAsync()).Stopped).IsTrue();
        }
        var record = JsonNode.Parse(File.ReadAllText(Directory.GetFiles(environment["FAKE_CODEX_RECORD"], "start.json", SearchOption.AllDirectories).Single()))!;
        await Assert.That(record["scenario"]!.GetValue<string>()).IsEqualTo("probe.jsonl");
        await Assert.That(record["codexHome"]!.GetValue<string>()).IsEqualTo(Path.Combine(profiles, "probe-codex"));
        home.Dispose();
        await Assert.That(Directory.Exists(Path.Combine(profiles, "probe-codex"))).IsFalse();
        await Assert.That(File.ReadAllText(Path.Combine(userHome, "auth.json"))).IsEqualTo("synthetic-auth");
    }

    [Test]
    [Arguments("on")]
    [Arguments("off")]
    [Timeout(180_000)]
    public async Task Clean_profile_uses_minimal_home_hardlinked_auth_and_removes_it_after_stop(string project, CancellationToken cancellationToken)
    {
        using var tree = new TempTree();
        var environment = tree.ChildEnvironment().Where(pair => pair.Value is not null)
            .ToDictionary(pair => pair.Key, pair => pair.Value!);
        var userHome = environment["CODEX_HOME"];
        var configPath = Path.Combine(userHome, "config.toml");
        var original = Encoding.UTF8.GetBytes("[mcp_servers.canary]\ncommand = 'canary'\n");
        File.WriteAllBytes(configPath, original);
        File.WriteAllText(Path.Combine(userHome, "auth.json"), "synthetic-auth");
        var profiles = Path.Combine(tree.DispatchHome, "state", "control", "owner", "profiles");
        var executable = Path.Combine(TestPaths.Tests, "Fakes", "FakeCodex", "bin",
            new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name, "net10.0", OperatingSystem.IsWindows() ? "codex.exe" : "codex");
        var context = new BackendContext(profiles, executable, new Dictionary<string, string>());
        var cwd = Directory.CreateDirectory(Path.Combine(tree.Repository, ".claude", "worktrees", "a.b ' 世界")).FullName;
        using var home = CodexHome.Create(context, Unit.CodexHomeTests.Launch(cwd, project), userHome, "elevated");
        await Assert.That(home.Profile).IsEqualTo("clean");
        await Assert.That(home.Temporary).IsTrue();
        await Assert.That(home.Reason).IsNull();
        await Assert.That(home.Home).IsEqualTo(Path.Combine(profiles, "codex-task"));
        var config = TomlSerializer.Deserialize<TomlTable>(File.ReadAllText(Path.Combine(home.Home, "config.toml")))!;
        await Assert.That(config.Keys.Order().ToArray()).IsEquivalentTo(
            (new[] { "cli_auth_credentials_store", "include_skill_instructions", "features", "projects" })
                .Concat(project == "off" ? ["project_doc_max_bytes"] : [])
                .Concat(OperatingSystem.IsWindows() ? ["windows"] : []).ToArray());
        await Assert.That((string)config["cli_auth_credentials_store"]!).IsEqualTo("file");
        await Assert.That((bool)config["include_skill_instructions"]!).IsFalse();
        var projects = (TomlTable)config["projects"]!;
        await Assert.That(projects.Count).IsEqualTo(1);
        var trustKey = projects.Keys.Single();
        await Assert.That(string.Equals(trustKey, cwd, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)).IsTrue();
        await Assert.That((string)((TomlTable)projects[trustKey]!)["trust_level"]!).IsEqualTo(project == "on" ? "trusted" : "untrusted");
        foreach (var feature in new[] { "apps", "plugins", "remote_plugin" })
            await Assert.That((bool)((TomlTable)config["features"]!)[feature]!).IsFalse();
        if (project == "off") await Assert.That((long)config["project_doc_max_bytes"]!).IsEqualTo(0);
        if (OperatingSystem.IsWindows())
            await Assert.That((string)((TomlTable)config["windows"]!)["sandbox"]!).IsEqualTo("elevated");
        else
            await Assert.That(File.GetUnixFileMode(home.Home)).IsEqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        var marker = JsonNode.Parse(File.ReadAllText(Directory.GetFiles(home.Home, ".dispatch-home-*").Single()))!;
        await Assert.That(marker["run"]!.GetValue<string>()).IsEqualTo("run");
        await Assert.That(marker["task"]!.GetValue<string>()).IsEqualTo("task");
        var scenarios = Directory.CreateDirectory(Path.Combine(tree.Root, "scenarios")).FullName;
        File.WriteAllText(Path.Combine(scenarios, "task-task.jsonl"), "{\"notify\":\"ready\",\"params\":{}}\n");
        environment["FAKE_CODEX_SCENARIOS"] = scenarios;
        environment["FAKE_CODEX_RECORD"] = Path.Combine(tree.Root, "record");
        environment["FAKE_CODEX_AUTH_MARK"] = "synthetic-refreshed-auth";
        environment["CODEX_HOME"] = home.Home;
        await using (var process = CodexProcess.Start(context, cwd, environment))
        {
            using var reader = new StreamReader(process.Stdout, Encoding.UTF8, false, leaveOpen: true);
            await Assert.That(await reader.ReadLineAsync(cancellationToken)).Contains("ready");
            await Assert.That((await process.StopAsync()).Stopped).IsTrue();
        }
        var record = JsonNode.Parse(File.ReadAllText(Directory.GetFiles(environment["FAKE_CODEX_RECORD"], "start.json", SearchOption.AllDirectories).Single()))!;
        await Assert.That(record["codexHome"]!.GetValue<string>()).IsEqualTo(home.Home);
        await Assert.That(record["scenario"]!.GetValue<string>()).IsEqualTo("task-task.jsonl");
        await Assert.That(File.ReadAllText(Path.Combine(userHome, "auth.json"))).IsEqualTo("synthetic-refreshed-auth");
        await Assert.That(File.ReadAllBytes(configPath).SequenceEqual(original)).IsTrue();
        home.Dispose();
        home.Dispose();
        await Assert.That(Directory.Exists(Path.Combine(profiles, "codex-task"))).IsFalse();
        await Assert.That(File.ReadAllText(Path.Combine(userHome, "auth.json"))).IsEqualTo("synthetic-refreshed-auth");
        await Assert.That(File.ReadAllBytes(configPath).SequenceEqual(original)).IsTrue();
    }
}
