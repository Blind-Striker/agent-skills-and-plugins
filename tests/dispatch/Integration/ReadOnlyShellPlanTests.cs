using System.Text.Json.Nodes;
using Dispatch.Tests.Support;

namespace Dispatch.Tests.Integration;

[Category("Integration")]
[NotInParallel("processes")]
public sealed class ReadOnlyShellPlanTests
{
    [Test]
    [Arguments("full", "decline", "user and system-level")]
    [Arguments("clean", "decline", "only system-level")]
    [Arguments("full", "escalate", "user and system-level")]
    [Arguments("clean", "escalate", "only system-level")]
    [Timeout(180_000)]
    public async Task Run_records_shell_provenance_and_inherited_rules_capability_note(string profile, string shell, string ruleLayers, CancellationToken ct)
    {
        using var lab = new CodexLab();
        var repo = await LabRepo.CreateAsync(lab.Tree, ct);
        File.WriteAllText(Path.Combine(lab.Environment["CODEX_HOME"], "auth.json"), "synthetic-auth");
        var file = JsonNode.Parse("""{"schemaVersion":2,"run":"shell-policy","tasks":[{"id":"inspect","kind":"research","base":"main","project":"off","prompt":"Inspect."}]}""")!;
        file["tasks"]![0]!["profile"] = profile;
        file["tasks"]![0]!["readOnlyShell"] = shell;
        File.WriteAllText(lab.Tasks, file.ToJsonString());
        lab.Write("task", CodexLab.Handshake + CodexLab.Thread.Replace("workspaceWrite", "readOnly") + CodexLab.Final);
        using var process = new HarnessProcess(lab.Tree);
        var result = await process.RunAsync(["run", lab.Tasks, "--repo", repo.Root], ct,
            lab.Environment.ToDictionary(x => x.Key, x => (string?)x.Value));
        result.RequireSuccess("read-only run");
        var task = JsonNode.Parse(File.ReadAllText(Path.Combine(lab.RunFolder, "inspect", "task.json")))!;
        var config = JsonNode.Parse(File.ReadAllText(Path.Combine(lab.RunFolder, "config.json")))!;
        await Assert.That(task["settings"]!["profile"]!["actual"]!.GetValue<string>()).IsEqualTo(profile);
        await Assert.That(task["settings"]!["project"]!["value"]!.GetValue<string>()).IsEqualTo("off");
        await Assert.That(task["settings"]!["readOnlyShell"]!["value"]!.GetValue<string>()).IsEqualTo(shell);
        await Assert.That(task["settings"]!["readOnlyShell"]!["from"]!.GetValue<string>()).IsEqualTo("task");
        await Assert.That(JsonNode.DeepEquals(task["settings"], config["tasks"]!["inspect"]!["settings"])).IsTrue();
        var settled = JsonNode.Parse(File.ReadAllText(Path.Combine(lab.RunFolder, "inspect", "result.json")))!;
        var note = settled["capabilityNotes"]?.AsArray().Single()?.GetValue<string>();
        await Assert.That(note).Contains("inherited exec-policy rules");
        await Assert.That(note).Contains(ruleLayers);
        await Assert.That(note).Contains("bypass the read-only sandbox without approval");
    }

    [Test]
    [Arguments("full", "decline", "user and system-level")]
    [Arguments("clean", "decline", "only system-level")]
    [Arguments("full", "escalate", "user and system-level")]
    [Arguments("clean", "escalate", "only system-level")]
    [Timeout(180_000)]
    public async Task Plan_reports_shell_value_source_and_inherited_rules_gap(string profile, string shell, string ruleLayers, CancellationToken ct)
    {
        using var lab = new CodexLab();
        var repo = await LabRepo.CreateAsync(lab.Tree, ct);
        File.WriteAllText(Path.Combine(lab.Environment["CODEX_HOME"], "auth.json"), "synthetic-auth");
        var file = JsonNode.Parse("""{"schemaVersion":2,"tasks":[{"id":"inspect","kind":"research","base":"main","project":"off","prompt":"Inspect."}]}""")!;
        file["tasks"]![0]!["profile"] = profile;
        File.WriteAllText(lab.Tasks, file.ToJsonString());
        using var process = new HarnessProcess(lab.Tree);
        var result = await process.RunAsync(["plan", lab.Tasks, "--repo", repo.Root, "--read-only-shell", shell], ct,
            lab.Environment.ToDictionary(x => x.Key, x => (string?)x.Value));
        result.RequireSuccess("read-only plan");
        await Assert.That(result.Stdout).Contains($"readOnlyShell {shell} (cli)");
        await Assert.That(result.Stdout).Contains("inherited exec-policy rules");
        await Assert.That(result.Stdout).Contains(ruleLayers);
        await Assert.That(result.Stdout).Contains("bypass the read-only sandbox without approval");
    }
}
