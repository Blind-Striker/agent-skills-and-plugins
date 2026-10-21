using System.Text.Json.Nodes;
using Dispatch.Tests.Support;

namespace Dispatch.Tests.Integration;

[Category("Integration")]
[NotInParallel("processes")]
public sealed class ReadOnlyShellPlanTests
{
    [Test]
    [Timeout(180_000)]
    public async Task Run_records_shell_provenance_and_inherited_rules_capability_note(CancellationToken ct)
    {
        using var lab = new CodexLab();
        var repo = await LabRepo.CreateAsync(lab.Tree, ct);
        File.WriteAllText(lab.Tasks, """{"schemaVersion":2,"run":"shell-policy","tasks":[{"id":"inspect","kind":"research","base":"main","readOnlyShell":"decline","prompt":"Inspect."}]}""");
        lab.Write("task", CodexLab.Handshake + CodexLab.Thread.Replace("workspaceWrite", "readOnly") + CodexLab.Final);
        using var process = new HarnessProcess(lab.Tree);
        var result = await process.RunAsync(["run", lab.Tasks, "--repo", repo.Root], ct,
            lab.Environment.ToDictionary(x => x.Key, x => (string?)x.Value));
        result.RequireSuccess("read-only run");
        var task = JsonNode.Parse(File.ReadAllText(Path.Combine(lab.RunFolder, "inspect", "task.json")))!;
        var config = JsonNode.Parse(File.ReadAllText(Path.Combine(lab.RunFolder, "config.json")))!;
        await Assert.That(task["settings"]!["readOnlyShell"]!["value"]!.GetValue<string>()).IsEqualTo("decline");
        await Assert.That(task["settings"]!["readOnlyShell"]!["from"]!.GetValue<string>()).IsEqualTo("task");
        await Assert.That(JsonNode.DeepEquals(task["settings"], config["tasks"]!["inspect"]!["settings"])).IsTrue();
        var settled = JsonNode.Parse(File.ReadAllText(Path.Combine(lab.RunFolder, "inspect", "result.json")))!;
        await Assert.That(settled["capabilityNotes"]?[0]?.GetValue<string>()).Contains("inherited exec-policy rules");
    }

    [Test]
    [Timeout(180_000)]
    public async Task Plan_reports_shell_value_source_and_inherited_rules_gap(CancellationToken ct)
    {
        using var lab = new CodexLab();
        var repo = await LabRepo.CreateAsync(lab.Tree, ct);
        File.WriteAllText(lab.Tasks, """{"schemaVersion":2,"tasks":[{"id":"inspect","kind":"research","base":"main","prompt":"Inspect."}]}""");
        using var process = new HarnessProcess(lab.Tree);
        var result = await process.RunAsync(["plan", lab.Tasks, "--repo", repo.Root, "--read-only-shell", "decline"], ct,
            lab.Environment.ToDictionary(x => x.Key, x => (string?)x.Value));
        result.RequireSuccess("read-only plan");
        await Assert.That(result.Stdout).Contains("readOnlyShell decline (cli)");
        await Assert.That(result.Stdout).Contains("inherited exec-policy rules");
        await Assert.That(result.Stdout).Contains("without approval");
    }
}
