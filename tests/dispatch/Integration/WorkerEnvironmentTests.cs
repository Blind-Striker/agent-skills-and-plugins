using System.Text.Json.Nodes;
using Dispatch.Tests.Support;

namespace Dispatch.Tests.Integration;

[Category("Integration")]
public sealed class WorkerEnvironmentTests
{
    [Test]
    [Timeout(180_000)]
    public async Task RootScrub_RepositoryPatternsApplyBeforeBackendStart(CancellationToken cancellationToken)
    {
        using var tree = new TempTree();
        var repo = await LabRepo.CreateAsync(tree, cancellationToken);
        var repositoryConfig = Path.Combine(Directory.CreateDirectory(Path.Combine(repo.Root, ".dispatch")).FullName, "config.jsonc");
        await File.WriteAllTextAsync(repositoryConfig, """{"schemaVersion":2,"restrictions":{"scrub":["^FAKE_EXTRA_"]}}""", cancellationToken);
        await repo.GitAsync(["add", ".dispatch"], cancellationToken);
        await repo.GitAsync(["commit", "-m", "Add scrub restriction"], cancellationToken);
        var tasks = Path.Combine(tree.Root, "tasks.jsonc");
        await File.WriteAllTextAsync(tasks, """{"schemaVersion":2,"defaults":{"backend":"opencode"},"tasks":[{"id":"inspect","kind":"research","base":"main","route":"fixture/model","prompt":"Inspect."}]}""", cancellationToken);
        var environment = FakeOpenCode.Environment(tree, "one-task.jsonl").ToDictionary(p => p.Key, p => (string?)p.Value);
        var namesFile = Path.Combine(tree.Root, "names.json");
        environment["FAKE_ENV_NAMES"] = namesFile;
        environment["FAKE_EXTRA_VALUE"] = "synthetic";
        using var harness = new HarnessProcess(tree);
        var result = await harness.RunAsync(["plan", tasks, "--repo", repo.Root], cancellationToken, environment);
        result.RequireSuccess("repository scrub probe");
        var names = JsonNode.Parse(await File.ReadAllTextAsync(namesFile, cancellationToken))!.AsArray().Select(n => n!.GetValue<string>()).ToArray();
        await Assert.That(names).DoesNotContain("FAKE_EXTRA_VALUE");
    }

    [Test]
    [Timeout(180_000)]
    public async Task BackendProcess_GetsPassthroughOnly(CancellationToken cancellationToken)
    {
        using var tree = new TempTree();
        var repo = await LabRepo.CreateAsync(tree, cancellationToken);
        var tasks = Path.Combine(tree.Root, "tasks.jsonc");
        await File.WriteAllTextAsync(tasks, """{"schemaVersion":2,"defaults":{"backend":"opencode"},"tasks":[{"id":"inspect","kind":"research","base":"main","route":"fixture/model","prompt":"Inspect."}]}""", cancellationToken);
        var environment = FakeOpenCode.Environment(tree, "one-task.jsonl").ToDictionary(p => p.Key, p => (string?)p.Value);
        var configPath = Path.Combine(tree.DispatchHome, "config", "config.jsonc");
        var config = JsonNode.Parse(File.ReadAllText(configPath))!;
        config["backends"]!["opencode"]!["env"] = new JsonArray("FAKE_PROVIDER_TOKEN");
        config["backends"]!["codex"] = new JsonObject { ["env"] = new JsonArray("FAKE_OTHER_TOKEN") };
        await File.WriteAllTextAsync(configPath, config.ToJsonString(), cancellationToken);
        var namesFile = Path.Combine(tree.Root, "names.json");
        environment["FAKE_ENV_NAMES"] = namesFile;
        environment["FAKE_PROVIDER_TOKEN"] = "synthetic";
        environment["FAKE_OTHER_TOKEN"] = "synthetic";
        using var harness = new HarnessProcess(tree);
        var result = await harness.RunAsync(["plan", tasks, "--repo", repo.Root], cancellationToken, environment);
        result.RequireSuccess("passthrough probe");
        var names = JsonNode.Parse(await File.ReadAllTextAsync(namesFile, cancellationToken))!.AsArray().Select(n => n!.GetValue<string>()).ToArray();
        await Assert.That(names).Contains("FAKE_PROVIDER_TOKEN");
        await Assert.That(names).DoesNotContain("FAKE_OTHER_TOKEN");
    }

    [Test]
    [Timeout(180_000)]
    public async Task RootScrub_ChildProcessSeesNoScrubbedName(CancellationToken cancellationToken)
    {
        using var tree = new TempTree();
        var repo = await LabRepo.CreateAsync(tree, cancellationToken);
        var tasks = Path.Combine(tree.Root, "tasks.jsonc");
        await File.WriteAllTextAsync(tasks, """{"schemaVersion":2,"defaults":{"backend":"opencode"},"tasks":[{"id":"inspect","kind":"research","base":"main","route":"fixture/model","prompt":"Inspect."}]}""", cancellationToken);
        var environment = FakeOpenCode.Environment(tree, "one-task.jsonl").ToDictionary(p => p.Key, p => (string?)p.Value);
        var namesFile = Path.Combine(tree.Root, "names.json");
        environment["FAKE_ENV_NAMES"] = namesFile;
        environment["FAKE_TOKEN"] = "synthetic";
        environment["FAKE_DB_PWD"] = "synthetic";
        environment["FAKE_KEEP"] = "synthetic";
        using var harness = new HarnessProcess(tree);
        var result = await harness.RunAsync(["plan", tasks, "--repo", repo.Root], cancellationToken, environment);
        result.RequireSuccess("root scrub probe");
        var names = JsonNode.Parse(await File.ReadAllTextAsync(namesFile, cancellationToken))!.AsArray().Select(n => n!.GetValue<string>()).ToArray();
        await Assert.That(names).Contains("FAKE_KEEP");
        await Assert.That(names).DoesNotContain("FAKE_TOKEN");
        await Assert.That(names).DoesNotContain("FAKE_DB_PWD");
    }
}
