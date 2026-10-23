using Dispatch.Tests.Support;

namespace Dispatch.Tests.Integration;

[Category("Integration")]
[NotInParallel("processes")]
public sealed class PlanOutputTests
{
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
