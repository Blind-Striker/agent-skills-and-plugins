using System.Text.Json.Nodes;

namespace Dispatch.Tests.Support;

internal sealed class OpenCodeLab : IAsyncDisposable
{
    readonly Dictionary<string, string?> saved = new();
    readonly bool ownsTree;
    public TempTree Tree { get; }
    public OpenCodeBackend Backend { get; } = new(runId: "run");
    public List<JsonObject> Steps { get; } = File.ReadLines(FakeOpenCode.Fixture("one-task.jsonl"))
        .Take(4).Select(x => JsonNode.Parse(x)!.AsObject()).ToList();
    public string Scenario => Path.Combine(Tree.Root, "scenario.jsonl");

    public OpenCodeLab(TempTree? tree = null)
    {
        ownsTree = tree is null;
        Tree = tree ?? new();
        var environment = FakeOpenCode.Environment(Tree, "one-task.jsonl");
        environment["FAKE_OPENCODE_SCENARIO"] = Scenario;
        foreach (var name in new[] { "DISPATCH_HOME", "FAKE_OPENCODE_SCENARIO", "FAKE_OPENCODE_RECORD" })
        {
            saved[name] = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, environment[name]);
        }
        Locations.Resolve();
        ConfigLayers.Initialize(Args.Parse([]));
        Locations.Configure(new() { WorktreeRoot = Tree.WorktreeRoot }, null);
    }

    public void SetEnvironment(string name, string? value)
    {
        saved.TryAdd(name, Environment.GetEnvironmentVariable(name));
        Environment.SetEnvironmentVariable(name, value);
    }

    public List<JsonObject> Hosts() => Directory.GetFiles(Tree.Root, "requests.jsonl.*.host.json")
        .Select(path => JsonNode.Parse(File.ReadAllText(path))!.AsObject()).ToList();

    public JsonObject Add(string request, JsonNode? reply = null, int status = 200, bool repeat = false)
    {
        var step = new JsonObject { ["expect"] = request, ["reply"] = reply, ["status"] = status, ["repeat"] = repeat };
        Steps.Add(step);
        return step;
    }

    public static JsonObject Session(string id, string? outcome = null, double idle = 3)
    {
        var result = JsonNode.Parse("""
            {"data":{"id":"worker","projectID":"project","agent":"build","model":{"id":"model","providerID":"fixture"},"cost":0,"tokens":{"input":0,"output":0,"reasoning":0,"cache":{"read":0,"write":0}},"time":{"created":1,"updated":3,"idle":3},"location":{"directory":"$directory"}}}
            """)!.AsObject();
        result["data"]!["id"] = id;
        result["data"]!["outcome"] = outcome;
        result["data"]!["time"]!["idle"] = idle;
        return result;
    }

    public static JsonObject Event(string type, JsonObject data, double created = 3) => new()
    {
        ["id"] = Guid.NewGuid().ToString("N"), ["created"] = created, ["type"] = type,
        ["durable"] = new JsonObject { ["aggregateID"] = data["sessionID"]?.DeepClone(), ["seq"] = 1, ["version"] = 1 },
        ["data"] = data,
    };

    public void Create(string id)
    {
        Add("POST /api/session", Session(id));
        Add($"PUT /api/session/{id}/environment", status: 204);
    }

    public JsonObject Prompt(string id, string turn = "turn", double created = 2) => Add($"POST /api/session/{id}/prompt", new JsonObject
    {
        ["data"] = new JsonObject { ["id"] = turn, ["sessionID"] = id, ["time"] = new JsonObject { ["created"] = created },
            ["type"] = "user", ["payload"] = new JsonObject { ["text"] = "Inspect." }, ["delivery"] = "steer" },
    });

    public void Write() => File.WriteAllLines(Scenario, Steps.Select(x => x.ToJsonString()));

    public void SessionPage(string directory, string? cursor, string? next, params JsonObject[] sessions)
    {
        var step = Add("GET /api/session", new JsonObject
        {
            ["data"] = new JsonArray(sessions.Select(x => (JsonNode)x).ToArray()),
            ["cursor"] = next is null ? new JsonObject() : new JsonObject { ["next"] = next },
        });
        step["query"] = new JsonObject { ["directory"] = directory };
        if (cursor is not null) step["query"]!["cursor"] = cursor;
    }

    public Task<CreateResult> CreateAsync(string id, CancellationToken ct) => Backend.CreateAsync(new("run", id, "research",
        new("fixture", "model", null), null, "build", new("full", "on", "read-only", "on", "worktree"),
        Tree.Repository, new([], []), new Dictionary<string, string>(), null, "full", null), ct);

    public async Task<WorkerSignal> ReadAsync(Func<WorkerSignal, bool> match, CancellationToken ct)
    {
        await foreach (var signal in Backend.Signals.ReadAllAsync(ct))
            if (match(signal)) return signal;
        throw new InvalidOperationException("signal stream ended before the expected signal");
    }

    public async ValueTask DisposeAsync()
    {
        try { await Backend.DisposeAsync(); }
        finally
        {
            foreach (var (name, value) in saved) Environment.SetEnvironmentVariable(name, value);
            Locations.Resolve();
            ConfigLayers.Initialize(Args.Parse([]));
            if (ownsTree) Tree.Dispose();
        }
    }
}
