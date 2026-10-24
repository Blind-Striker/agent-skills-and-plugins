using System.Text.Json.Nodes;
using Dispatch.Tests.Support;

namespace Dispatch.Tests.Integration;

[Category("Integration")]
[NotInParallel("processes")]
public sealed class RunControlTests
{
    [Test]
    [Timeout(45_000)]
    public async Task Run_ControlFolderInsideWorkspace_RefusedBeforeClaim(CancellationToken ct)
    {
        using var tree = new TempTree();
        var repo = await LabRepo.CreateAsync(tree, ct);
        var tasks = Path.Combine(tree.Root, "tasks.jsonc");
        File.WriteAllText(tasks, """{"schemaVersion":2,"run":"unsafe","defaults":{"backend":"opencode"},"tasks":[{"id":"inspect","kind":"research","base":"main","route":"fixture/model","prompt":"Inspect."}]}""");
        var env = FakeOpenCode.Environment(tree, "one-task.jsonl").ToDictionary(x => x.Key, x => (string?)x.Value);
        var config = Path.Combine(tree.DispatchHome, "config", "config.jsonc");
        var document = JsonNode.Parse(File.ReadAllText(config))!;
        document["locations"] = new JsonObject { ["worktreeRoot"] = Path.Combine(tree.DispatchHome, "state") };
        File.WriteAllText(config, document.ToJsonString());
        using var child = new HarnessProcess(tree);
        var result = await child.RunAsync(["run", tasks, "--repo", repo.Root], ct, env);
        await Assert.That(result.ExitCode).IsEqualTo(2);
        await Assert.That(result.Stderr).Contains("control");
        await Assert.That(Directory.Exists(Path.Combine(tree.DispatchHome, "state", "ledgers"))).IsFalse();
        var controlRoot = Path.Combine(tree.DispatchHome, "state", "control");
        await Assert.That(Directory.Exists(controlRoot) && Directory.GetDirectories(controlRoot).Length > 0).IsFalse();
    }

    [Test]
    [Timeout(90_000)]
    public async Task Run_SameRunIdTwice_OneExits2(CancellationToken ct)
    {
        using var tree = new TempTree();
        var repo = await LabRepo.CreateAsync(tree, ct);
        var tasks = Path.Combine(tree.Root, "tasks.jsonc");
        File.WriteAllText(tasks, """{"schemaVersion":2,"run":"race","tasks":[{"id":"inspect","kind":"research","base":"main","route":"fixture/model","prompt":"Inspect."}]}""");
        var config = Directory.CreateDirectory(Path.Combine(tree.DispatchHome, "config")).FullName;
        File.WriteAllText(Path.Combine(config, "config.jsonc"), new JsonObject
        {
            ["schemaVersion"] = 2,
            ["preferences"] = new JsonObject { ["backend"] = "opencode" },
            ["locations"] = new JsonObject { ["worktreeRoot"] = tree.WorktreeRoot }
        }.ToJsonString());
        var environment = FakeOpenCode.Environment(tree, "one-task.jsonl").ToDictionary(x => x.Key, x => (string?)x.Value);
        if (!OperatingSystem.IsWindows())
        {
            var root = Directory.CreateDirectory(Path.Combine(tree.DispatchHome, "state", "control")).FullName;
            File.SetUnixFileMode(root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }
        var release = Path.Combine(tree.Root, "release");
        var records = Path.GetDirectoryName(FakeOpenCode.Record(tree))!;
        Dictionary<string, string?> EnvironmentFor(int n)
        {
            var steps = File.ReadAllLines(FakeOpenCode.Fixture("one-task.jsonl")).Select(x => JsonNode.Parse(x)!.AsObject()).ToArray();
            var step = steps.Single(x => x["expect"]!.GetValue<string>() == "GET /api/model");
            step["arrived"] = Path.Combine(records, $"arrived-{n}");
            step["waitFor"] = release;
            steps.Single(x => x["expect"]!.GetValue<string>() == "POST /api/session").Remove("body");
            var path = Path.Combine(tree.Root, $"scenario-{n}.jsonl");
            File.WriteAllLines(path, steps.Select(x => x.ToJsonString()));
            return new(environment) { ["FAKE_OPENCODE_SCENARIO"] = path, ["FAKE_OPENCODE_RECORD"] = Path.Combine(records, $"requests-{n}.jsonl") };
        }
        using var first = new HarnessProcess(tree);
        using var second = new HarnessProcess(tree);
        var one = first.RunAsync(["run", tasks, "--repo", repo.Root], ct, EnvironmentFor(1));
        var two = second.RunAsync(["run", tasks, "--repo", repo.Root], ct, EnvironmentFor(2));
        while (!File.Exists(Path.Combine(records, "arrived-1")) || !File.Exists(Path.Combine(records, "arrived-2")))
        {
            if (one.IsCompleted || two.IsCompleted)
                throw new InvalidOperationException($"run ended before the preflight barrier: {(one.IsCompleted ? await one : await two)}");
            await Task.Delay(10, ct);
        }
        File.WriteAllText(release, "go");
        var results = await Task.WhenAll(one, two);
        await Assert.That(results.Select(x => x.ExitCode).Order().ToArray()).IsEquivalentTo(new[] { 0, 2 });
        await Assert.That(results.Single(x => x.ExitCode == 2).Stderr).Contains("already recorded");
        var run = Directory.GetDirectories(Path.Combine(tree.DispatchHome, "state", "ledgers")).Single();
        var info = JsonNode.Parse(File.ReadAllText(Path.Combine(run, "run.json")))!;
        var controlRoot = Path.Combine(tree.DispatchHome, "state", "control");
        var control = Directory.GetDirectories(controlRoot).Single();
        await Assert.That(Path.GetFileName(control)).IsEqualTo(info["controlId"]!.GetValue<string>());
        await Assert.That(File.Exists(Path.Combine(control, "setup.done"))).IsTrue();
        await Assert.That(File.Exists(Path.Combine(control, "supervisor.lock"))).IsTrue();
        await Assert.That(File.Exists(Path.Combine(control, "run.ref"))).IsTrue();
        var requests = Enumerable.Range(1, 2).SelectMany(n => File.ReadLines(Path.Combine(records, $"requests-{n}.jsonl")))
            .Select(x => JsonNode.Parse(x)!).ToArray();
        await Assert.That(requests.Count(x => x["method"]!.GetValue<string>() == "POST" && x["path"]!.GetValue<string>() == "/api/session")).IsEqualTo(1);
        var prompt = requests.Single(x => x["path"]!.GetValue<string>().EndsWith("/prompt", StringComparison.Ordinal))["body"]!["text"]!.GetValue<string>();
        await Assert.That(prompt.Contains(control, StringComparison.OrdinalIgnoreCase)).IsFalse();
        if (!OperatingSystem.IsWindows())
        {
            const UnixFileMode mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
            await Assert.That(File.GetUnixFileMode(controlRoot)).IsEqualTo(mode);
            await Assert.That(File.GetUnixFileMode(control)).IsEqualTo(mode);
        }
    }
}
