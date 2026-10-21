namespace Dispatch.Tests.Support;

internal static class FakeOpenCode
{
    public static string Executable => Path.Combine(TestPaths.Tests, "Fakes", "FakeOpenCode", "bin",
        new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name, "net10.0", OperatingSystem.IsWindows() ? "opencode.exe" : "opencode");

    public static string Fixture(string name) => Path.Combine(TestPaths.Tests, "fixtures", "scenarios", "opencode", name);

    public static Dictionary<string, string> Environment(TempTree tree, string scenario)
    {
        var environment = tree.ChildEnvironment().ToDictionary(x => x.Key, x => x.Value!);
        environment["DISPATCH_OPENCODE"] = Executable;
        environment["FAKE_OPENCODE_SCENARIO"] = Fixture(scenario);
        environment["FAKE_OPENCODE_RECORD"] = Path.Combine(tree.Root, "requests.jsonl");
        return environment;
    }
}
