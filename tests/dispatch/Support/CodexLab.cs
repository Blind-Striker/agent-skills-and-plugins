using System.Text.Json.Nodes;

namespace Dispatch.Tests.Support;

internal sealed class CodexLab : IDisposable
{
    readonly bool ownsTree;
    public TempTree Tree { get; }
    public Dictionary<string, string> Environment { get; }
    public string Scenarios { get; }
    public string Records { get; }
    public string Tasks => Path.Combine(Tree.Root, "tasks.jsonc");
    public static string Executable => Path.Combine(TestPaths.Tests, "Fakes", "FakeCodex", "bin",
        new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name, "net10.0", OperatingSystem.IsWindows() ? "codex.exe" : "codex");

    public CodexLab(TempTree? tree = null)
    {
        ownsTree = tree is null;
        Tree = tree ?? new();
        Environment = Tree.ChildEnvironment().Where(x => x.Value is not null).ToDictionary(x => x.Key, x => x.Value!);
        Scenarios = Directory.CreateDirectory(Path.Combine(Tree.Root, "scenarios")).FullName;
        Records = Path.Combine(Tree.Root, "record");
        Environment["FAKE_CODEX_SCENARIOS"] = Scenarios;
        Environment["FAKE_CODEX_RECORD"] = Records;
        File.WriteAllText(Path.Combine(Scenarios, "version.txt"), "codex-cli 0.163.0\n");
        File.WriteAllText(Path.Combine(Scenarios, "login-status.json"), "{\"exit\":0,\"stdout\":\"Signed in\"}");
        Write("preflight", Handshake + """
            {"expect":"account/read","reply":{"account":{"type":"chatgpt"},"requiresOpenaiAuth":true}}
            {"expect":"model/list","reply":{"data":[{"id":"model-y","model":"model-y","supportedReasoningEfforts":[{"reasoningEffort":"high"},{"reasoningEffort":"low"}],"defaultReasoningEffort":"high"}],"nextCursor":null}}
            {"os":"windows","expect":"windowsSandbox/readiness","reply":{"status":"ready"}}
            """);
        Write("probe", File.ReadAllText(Path.Combine(Scenarios, "preflight.jsonl")));
        var config = Directory.CreateDirectory(Path.Combine(Tree.DispatchHome, "config")).FullName;
        File.WriteAllText(Path.Combine(config, "config.jsonc"), new JsonObject
        {
            ["schemaVersion"] = 2,
            ["preferences"] = new JsonObject { ["backend"] = "codex", ["defaultRoutes"] = new JsonObject { ["codex"] = "model-y#high" } },
            ["locations"] = new JsonObject { ["worktreeRoot"] = Tree.WorktreeRoot },
            ["backends"] = new JsonObject
            {
                ["codex"] = new JsonObject { ["executable"] = Executable },
                ["opencode"] = new JsonObject { ["executable"] = Path.Combine(Tree.Root, "unconfigured-backend") },
            },
        }.ToJsonString());
    }

    public void Write(string name, string steps) => File.WriteAllText(Path.Combine(Scenarios, name + ".jsonl"), steps + "\n");
    public CodexBackend Backend() => new(new(Path.Combine(Tree.Root, "profiles"), Executable,
        Environment.Where(x => x.Key.StartsWith("FAKE_CODEX_", StringComparison.Ordinal)).ToDictionary(x => x.Key, x => x.Value)), Environment);
    public void CodeTask() => File.WriteAllText(Tasks, """
        {"schemaVersion":2,"run":"codex-run","tasks":[{"id":"edit","kind":"code","base":"main","prompt":"Write a.txt.","scope":["a.txt"],"verify":["git diff --exit-code HEAD -- a.txt"]}]}
        """);

    public JsonObject[] Wire() => Directory.GetFiles(Records, "wire.jsonl", SearchOption.AllDirectories)
        .SelectMany(ReadWire).ToArray();

    static JsonObject[] ReadWire(string path)
    {
        // Each published log is immutable. Share deletion so the fake can replace its name while we read.
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(x => JsonNode.Parse(x)!.AsObject()).ToArray();
    }
    public JsonObject[] Input() => Wire().Where(x => x["direction"]!.GetValue<string>() == "in")
        .Select(x => x["message"]!.AsObject()).ToArray();
    public string RunFolder => Directory.GetDirectories(Path.Combine(Tree.DispatchHome, "state", "ledgers")).Single();
    public JsonObject Result => JsonNode.Parse(File.ReadAllText(Path.Combine(RunFolder, "edit", "result.json")))!.AsObject();
    public void Dispose() { if (ownsTree) Tree.Dispose(); }

    public const string Handshake = """
        {"expect":"initialize","reply":{"codexHome":"$codexHome","platformOs":"$platformOs","platformFamily":"$platformFamily","userAgent":"fixture"}}
        {"expect":"initialized"}

        """;
    public const string Thread = """
        {"expect":"thread/start","reply":{"thread":{"id":"$threadId","cliVersion":"0.163.0"},"model":"model-y","modelProvider":"fixture","sandbox":{"type":"workspaceWrite"},"approvalPolicy":"on-request","cwd":"$cwd"}}
        {"expect":"turn/start","reply":{"turn":{"id":"$turnId","status":"inProgress"}}}

        """;
    public const string Final = """
        {"notify":"item/completed","params":{"threadId":"$threadId","turnId":"$turnId","item":{"id":"answer","type":"agentMessage","phase":"final_answer","text":"Completed."}}}
        {"notify":"turn/completed","params":{"threadId":"$threadId","turn":{"id":"$turnId","status":"completed"}}}
        """;
}
