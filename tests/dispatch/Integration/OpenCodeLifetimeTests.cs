using System.Diagnostics;
using System.Text.Json.Nodes;
using Dispatch.Tests.Support;

namespace Dispatch.Tests.Integration;

[Category("Integration")]
[NotInParallel]
public sealed class OpenCodeLifetimeTests
{
    [Test]
    [Timeout(60_000)]
    public async Task Exited_server_descendants_end_on_disposal(CancellationToken ct)
    {
        using var tree = new TempTree();
        try
        {
            var evidence = await ObserveAsync(tree.Root, tree.ChildEnvironment(), ct);
            await RequireStoppedAsync(evidence, ct);
        }
        finally { await ObservedProcess.CleanupRecordedChildrenAsync(tree.Root); }
    }

    [Test]
    [Timeout(120_000)]
    public async Task Exited_server_descendants_end_on_owner_exit(CancellationToken ct)
    {
        if (Environment.GetEnvironmentVariable("FAKE_OPENCODE_OWNER_PROBE") is { } root)
        {
            // The outer test owns cleanup: it must observe the descendants after this process exits.
            var evidence = await ObserveAsync(root, new Dictionary<string, string?>(), ct);
            await File.WriteAllTextAsync(Path.Combine(root, "evidence.json"), evidence.ToJsonString(), ct);
            return;
        }

        using var tree = new TempTree();
        using var owner = new CapturedProcess();
        var environment = tree.ChildEnvironment();
        environment["FAKE_OPENCODE_OWNER_PROBE"] = tree.Root;
        try
        {
            var result = await owner.RunAsync("dotnet", [typeof(OpenCodeLifetimeTests).Assembly.Location,
                "--treenode-filter", "/*/*/OpenCodeLifetimeTests/Exited_server_descendants_end_on_owner_exit"],
                TestPaths.Tests, environment, ct);
            result.RequireSuccess("host owner process");
            var evidence = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(tree.Root, "evidence.json"), ct))!.AsObject();
            Console.WriteLine($"After owner exit: {evidence.ToJsonString()}");
            await RequireStoppedAsync(evidence, ct);
        }
        finally { await ObservedProcess.CleanupRecordedChildrenAsync(tree.Root); }
    }

    static async Task<JsonObject> ObserveAsync(string root, IReadOnlyDictionary<string, string?> environment, CancellationToken ct)
    {
        var childRecord = Path.Combine(root, "children");
        var exitWhen = Path.Combine(root, "exit");
        var passthrough = environment.Where(x => x.Value is not null).ToDictionary(x => x.Key, x => x.Value!);
        passthrough["FAKE_OPENCODE_SCENARIO"] = FakeOpenCode.Fixture("one-task.jsonl");
        passthrough["FAKE_OPENCODE_RECORD"] = Path.Combine(root, "requests.jsonl");
        passthrough["FAKE_OPENCODE_CHILD_RECORD"] = childRecord;
        passthrough["FAKE_OPENCODE_EXIT_WHEN"] = exitWhen;
        var context = new BackendContext(root, FakeOpenCode.Executable, passthrough);
        JsonObject evidence;
        await using (var host = await HarnessServer.StartAsync(false, context, root,
            new("full", "on", "workspace", "on", "worktree"), ct))
        {
            var server = ObservedProcess.Capture(host.Server.ProcessId);
            var descendants = JsonNode.Parse(await File.ReadAllTextAsync($"{childRecord}.{server.Pid}.json", ct))!.AsObject();
            var (child, grandchild) = ObservedProcess.Descendants(descendants);
            evidence = new JsonObject
            {
                ["owner"] = Environment.ProcessId, ["serverInJob"] = server.IsInJob(),
                ["childInJob"] = child.IsInJob(), ["grandchildInJob"] = grandchild.IsInJob(),
                ["sdkJobLimits"] = ObservedProcess.SdkJobLimits(), ["serverInSdkJob"] = server.IsInSdkJob(),
                ["childInSdkJob"] = child.IsInSdkJob(), ["grandchildInSdkJob"] = grandchild.IsInSdkJob(),
                ["descendants"] = descendants,
            };
            await Assert.That(child.IsAlive() && grandchild.IsAlive()).IsTrue();
            await File.WriteAllTextAsync(exitWhen, "exit", ct);
            using var process = Process.GetProcessById(server.Pid);
            await process.WaitForExitAsync(ct);
            await Assert.That(process.ExitCode).IsEqualTo(0);
        }
        var identities = ObservedProcess.Descendants(evidence["descendants"]!);
        evidence["childAliveAfterDisposal"] = identities.Child.IsAlive();
        evidence["grandchildAliveAfterDisposal"] = identities.Grandchild.IsAlive();
        Console.WriteLine($"After host disposal: {evidence.ToJsonString()}");
        return evidence;
    }

    static async Task RequireStoppedAsync(JsonObject evidence, CancellationToken ct)
    {
        var (child, grandchild) = ObservedProcess.Descendants(evidence["descendants"]!);
        var deadline = Stopwatch.StartNew();
        while ((child.IsAlive() || grandchild.IsAlive()) && deadline.Elapsed < TimeSpan.FromSeconds(3))
            await Task.Delay(20, ct);
        await Assert.That(child.IsAlive()).IsFalse();
        await Assert.That(grandchild.IsAlive()).IsFalse();
    }
}
