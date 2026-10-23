using Dispatch.Tests.Support;
using TUnit.Assertions.Enums;

namespace Dispatch.Tests.Integration;

[Category("Integration")]
[NotInParallel("processes")]
public sealed class PlanOutputTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    [Timeout(180_000)]
    public async Task Plan_prints_resolved_temp_paths(bool configured, CancellationToken ct)
    {
        using var tree = new TempTree();
        var repo = await LabRepo.CreateAsync(tree, ct);
        var root = Path.Combine(tree.Root, "private-temp");
        var tasks = Path.Combine(tree.Root, "tasks.jsonc");
        var document = System.Text.Json.Nodes.JsonNode.Parse("""{"schemaVersion":2,"run":"ignored","defaults":{"backend":"opencode"},"tasks":[{"id":"zulu","kind":"research","base":"main","route":"fixture/model","prompt":"Inspect."},{"id":"alpha","kind":"research","base":"main","route":"fixture/model","prompt":"Inspect."},{"id":"structured","kind":"structured","route":"fixture/model","project":"on","prompt":"Transform.","input":{"items":[{"id":"a"}]},"result":{"schema":"schema.json","itemsAt":""}}]}""")!;
        if (configured) document["tasks"]![0]!["tempRoot"] = root;
        File.WriteAllText(Path.Combine(tree.Root, "schema.json"), """{"type":"array"}""");
        File.WriteAllText(tasks, document.ToJsonString());
        using var process = new HarnessProcess(tree);
        var result = await process.RunAsync(["plan", tasks, "--repo", repo.Root, "--run", "preview"], ct,
            FakeOpenCode.Environment(tree, "one-task.jsonl").ToDictionary(x => x.Key, x => (string?)x.Value));
        result.RequireSuccess("plan");
        var run = $"{DateTime.UtcNow:yyyy-MM-dd}-preview";
        var defaultRoot = Path.Combine(tree.DispatchHome, "state", "worktrees");
        var zulu = configured ? Path.Combine(root, run, "zulu") : Path.Combine(defaultRoot, run, ".tmp", "zulu");
        await Assert.That(Lines(result).Where(line => line.Contains(": temp ", StringComparison.Ordinal)).ToArray()).IsEquivalentTo(new[]
        {
            $"INFO zulu: temp {zulu} {(configured ? "(tempRoot from task)" : "(default)")}",
            $"INFO alpha: temp {Path.Combine(defaultRoot, run, ".tmp", "alpha")} (default)",
        }, CollectionOrdering.Matching);
        await Assert.That(Directory.Exists(root)).IsFalse();
        await Assert.That(Directory.Exists(Path.Combine(defaultRoot, run))).IsFalse();
    }

    [Test]
    [Arguments("plan")]
    [Arguments("run")]
    [Timeout(180_000)]
    public async Task Each_codex_approval_gate_warning_prints_once(string command, CancellationToken ct)
    {
        using var lab = new CodexLab();
        var repo = await LabRepo.CreateAsync(lab.Tree, ct);
        File.WriteAllText(Path.Combine(lab.Environment["CODEX_HOME"], "auth.json"), "synthetic-auth");
        File.WriteAllText(lab.Tasks, """{"schemaVersion":2,"run":"gate-warnings","tasks":[{"id":"inspect","kind":"research","base":"main","project":"off","prompt":"Inspect."}]}""");
        var policy = Path.Combine(lab.Tree.Root, "policy.jsonc");
        File.WriteAllText(policy, """{"schemaVersion":2,"restrictions":{"escalate":[{"name":"push","action":"shell","resources":["git push*"]}],"deny":[{"name":"gh","action":"shell","resources":["gh *"]}]}}""");
        lab.Write("task", CodexLab.Handshake + CodexLab.Thread.Replace("workspaceWrite", "readOnly") + CodexLab.Final);
        using var process = new HarnessProcess(lab.Tree);
        var result = await process.RunAsync([command, lab.Tasks, "--repo", repo.Root, "--policy", policy], ct,
            lab.Environment.ToDictionary(x => x.Key, x => (string?)x.Value));
        result.RequireSuccess(command);
        foreach (var rule in new[] { "cli/push", "cli/gh" })
            await Assert.That(Lines(result).Count(line => line == $"WARN inspect: {rule} is enforced at the approval gate only")).IsEqualTo(1);
    }

    [Test]
    [Timeout(180_000)]
    public async Task Codex_plan_reports_a_sandboxed_shell_only_for_its_read_only_task(CancellationToken ct)
    {
        using var lab = new CodexLab();
        var repo = await LabRepo.CreateAsync(lab.Tree, ct);
        File.WriteAllText(Path.Combine(lab.Environment["CODEX_HOME"], "auth.json"), "synthetic-auth");
        File.WriteAllText(lab.Tasks, """{"schemaVersion":2,"tasks":[{"id":"inspect","kind":"research","base":"main","prompt":"Inspect."},{"id":"edit","kind":"code","base":"main","prompt":"Edit.","scope":["a.txt"]}]}""");
        using var process = new HarnessProcess(lab.Tree);
        var result = await process.RunAsync(["plan", lab.Tasks, "--repo", repo.Root], ct,
            lab.Environment.ToDictionary(x => x.Key, x => (string?)x.Value));
        result.RequireSuccess("plan");
        var lines = Lines(result);
        await Assert.That(lines).Contains("INFO inspect: sandboxed shell; escalations declined (readOnlyShell decline, from tool)");
        await Assert.That(lines.Any(line => line.StartsWith("INFO edit:", StringComparison.Ordinal) && line.Contains("shell", StringComparison.Ordinal))).IsFalse();
        await Assert.That(lines.Any(line => line.StartsWith("WARN inspect: The pinned Codex app-server", StringComparison.Ordinal))).IsTrue();
        await Assert.That(lines.Any(line => line.Contains("readOnlyShell", StringComparison.Ordinal) && !line.StartsWith("INFO ", StringComparison.Ordinal))).IsFalse();
    }

    [Test]
    [Timeout(180_000)]
    public async Task Opencode_plan_reports_no_shell_only_for_its_read_only_task(CancellationToken ct)
    {
        using var tree = new TempTree();
        var repo = await LabRepo.CreateAsync(tree, ct);
        var tasks = Path.Combine(tree.Root, "tasks.jsonc");
        File.WriteAllText(tasks, """{"schemaVersion":2,"defaults":{"backend":"opencode"},"tasks":[{"id":"inspect","kind":"research","base":"main","route":"fixture/model","prompt":"Inspect."},{"id":"edit","kind":"code","base":"main","route":"fixture/model","prompt":"Edit.","scope":["a.txt"]}]}""");
        using var process = new HarnessProcess(tree);
        var result = await process.RunAsync(["plan", tasks, "--repo", repo.Root], ct,
            FakeOpenCode.Environment(tree, "one-task.jsonl").ToDictionary(x => x.Key, x => (string?)x.Value));
        result.RequireSuccess("plan");
        var lines = Lines(result);
        await Assert.That(lines).Contains("INFO inspect: no shell for a read-only task (readOnlyShell decline, from tool)");
        await Assert.That(lines.Any(line => line.StartsWith("INFO edit:", StringComparison.Ordinal) && line.Contains("shell", StringComparison.Ordinal))).IsFalse();
    }

    [Test]
    [Timeout(180_000)]
    public async Task Plan_errors_carry_the_ERROR_level_and_refuse_the_plan(CancellationToken ct)
    {
        using var lab = new CodexLab();
        var repo = await LabRepo.CreateAsync(lab.Tree, ct);
        File.WriteAllText(Path.Combine(lab.Environment["CODEX_HOME"], "auth.json"), "synthetic-auth");
        File.WriteAllText(lab.Tasks, """{"schemaVersion":2,"tasks":[{"id":"inspect","kind":"research","base":"missing","prompt":"Inspect."}]}""");
        using var process = new HarnessProcess(lab.Tree);
        var result = await process.RunAsync(["plan", lab.Tasks, "--repo", repo.Root], ct,
            lab.Environment.ToDictionary(x => x.Key, x => (string?)x.Value));
        await Assert.That(result.ExitCode).IsEqualTo(2);
        await Assert.That(Lines(result).Any(line => line.StartsWith("ERROR inspect: base missing does not resolve", StringComparison.Ordinal))).IsTrue();
        await Assert.That(Lines(result).Any(line => line.StartsWith("WARN inspect: The pinned Codex app-server", StringComparison.Ordinal))).IsTrue();
        await Assert.That(Lines(result)).Contains("plan refused: 1 error(s)");
    }

    static string[] Lines(CapturedResult result) => result.Stdout.Split('\n').Select(line => line.TrimEnd('\r')).ToArray();
}
