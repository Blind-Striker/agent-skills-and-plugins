using System.Text.Json.Nodes;
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
            await Assert.That(result.Stdout.Split('\n').Count(line => line.Contains($"inspect: {rule} is enforced at the approval gate only", StringComparison.Ordinal))).IsEqualTo(1);
    }
}
