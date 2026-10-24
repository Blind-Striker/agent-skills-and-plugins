namespace Dispatch.Tests.Support;

internal static class FakeOpenCode
{
    public static string Executable => Path.Combine(TestPaths.Tests, "Fakes", "FakeOpenCode", "bin",
        new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name, "net10.0", OperatingSystem.IsWindows() ? "opencode.exe" : "opencode");

    public static string Fixture(string name) => Path.Combine(TestPaths.Tests, "fixtures", "scenarios", "opencode", name);

    public static string Record(TempTree tree) => Path.Combine(tree.ChildEnvironment()["XDG_DATA_HOME"]!, "opencode", "requests.jsonl");

    public static void SelfcheckScenario(TempTree tree, string path, List<System.Text.Json.Nodes.JsonNode> steps,
        int launchStart, IDictionary<string, string?> environment)
    {
        File.WriteAllLines(path, steps.Select(step => step.ToJsonString()));
        environment["FAKE_OPENCODE_SCENARIO"] = path;
        if (Ready.FenceKind is null) return;
        // A fenced full/on launch has a fresh process, not the probe host's consumed cursor.
        // Clean/project-off already used a different host from the full/on probe before fencing.
        var launch = path + ".launch";
        File.WriteAllLines(launch, steps.Take(4).Concat(steps.Skip(launchStart)).Select(step => step.ToJsonString()));
        var executable = Path.Combine(Directory.CreateDirectory(path + ".bin").FullName, "opencode");
        File.WriteAllText(executable, $"#!/bin/sh\nif [ \"$XDG_CACHE_HOME\" = \"${{TMPDIR%/tmp}}/cache\" ] && [ \"$OPENCODE_DISABLE_CLAUDE_CODE\" != 1 ] && [ \"$OPENCODE_DISABLE_PROJECT_CONFIG\" != 1 ]; then\nexport FAKE_OPENCODE_SCENARIO='{launch}'\nfi\nexec '{Executable}' \"$@\"\n");
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(executable, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var configPath = Path.Combine(tree.DispatchHome, "config", "config.jsonc");
        var config = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(configPath))!;
        config["backends"]!["opencode"]!["executable"] = executable;
        File.WriteAllText(configPath, config.ToJsonString());
    }

    public static Dictionary<string, string> Environment(TempTree tree, string scenario)
    {
        var environment = tree.ChildEnvironment().ToDictionary(x => x.Key, x => x.Value!);
        var config = Directory.CreateDirectory(Path.Combine(tree.DispatchHome, "config")).FullName;
        var path = Path.Combine(config, "config.jsonc");
        var document = File.Exists(path) ? System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))! : new System.Text.Json.Nodes.JsonObject { ["schemaVersion"] = 2 };
        document["backends"] = new System.Text.Json.Nodes.JsonObject
        {
            ["opencode"] = new System.Text.Json.Nodes.JsonObject { ["executable"] = Executable },
        };
        File.WriteAllText(path, document.ToJsonString());
        environment["FAKE_OPENCODE_SCENARIO"] = Fixture(scenario);
        Directory.CreateDirectory(Path.GetDirectoryName(Record(tree))!);
        environment["FAKE_OPENCODE_RECORD"] = Record(tree);
        return environment;
    }
}
