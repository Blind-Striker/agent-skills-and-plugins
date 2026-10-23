using Dispatch.Tests.Support;

namespace Dispatch.Tests.Integration;

[Category("Integration")]
[NotInParallel]
public sealed class RunAddressTests
{
    const string Name = "rt-527-review";

    static string Seed(TempTree tree, string run, string controlId)
    {
        var ledger = Directory.CreateDirectory(Path.Combine(tree.DispatchHome, "state", "ledgers", run)).FullName;
        Json.WriteFile(Path.Combine(ledger, "run.json"), new RunInfo { Run = run, ControlId = controlId, Repo = tree.Repository }, LedgerJson.Files.RunInfo);
        File.WriteAllText(Path.Combine(ledger, "events.jsonl"), $"{{\"schemaVersion\":2,\"run\":\"{run}\",\"type\":\"run\"}}\n{{\"run\":\"{run}\",\"task\":\"inspect\",\"type\":\"queued\"}}\n");
        var control = Directory.CreateDirectory(Path.Combine(tree.DispatchHome, "state", "control", controlId)).FullName;
        Json.WriteFile(Path.Combine(control, "run.ref"), new RunReference { Run = run, LedgerRoot = Path.GetDirectoryName(ledger)! }, ControlJson.Files.RunReference);
        File.WriteAllText(Path.Combine(control, "setup.done"), "");
        return ledger;
    }

    static int Invoke(string command, string run) => command switch
    {
        "status" => Commands.Status(Args.Parse(["status", run])),
        "asks" => Commands.Asks(Args.Parse(["asks", run])),
        _ => Commands.Cancel(Args.Parse(["cancel", run, "inspect"])),
    };

    [Test]
    [Arguments("status")]
    [Arguments("asks")]
    [Arguments("cancel")]
    [Timeout(30_000)]
    public async Task A_bare_run_name_finds_its_only_dated_ledger(string command, CancellationToken ct)
    {
        await using var lab = new OpenCodeLab();
        var run = $"2026-10-23-{Name}";
        Seed(lab.Tree, run, "abcdefghijklmnopqrstuvwxyz");
        Seed(lab.Tree, "2026-10-23-other", "bcdefghijklmnopqrstuvwxyza");

        await Assert.That(Invoke(command, Name)).IsEqualTo(0);

        if (command == "status") await Assert.That(TestContext.Current!.Output.GetStandardOutput()).Contains($"run {run}");
        if (command == "cancel") await Assert.That(new Ledger(run).TaskStates()["inspect"]).IsEqualTo("cancelled");
    }

    [Test]
    [Timeout(30_000)]
    public async Task A_full_run_id_still_names_its_ledger(CancellationToken ct)
    {
        await using var lab = new OpenCodeLab();
        var run = $"2026-10-23-{Name}";
        Seed(lab.Tree, run, "abcdefghijklmnopqrstuvwxyz");
        Seed(lab.Tree, $"2026-10-22-{Name}", "bcdefghijklmnopqrstuvwxyza");

        await Assert.That(Invoke("status", run)).IsEqualTo(0);

        await Assert.That(TestContext.Current!.Output.GetStandardOutput()).Contains($"run {run}");
    }

    [Test]
    [Timeout(30_000)]
    public async Task A_bare_run_name_with_several_ledgers_is_refused_with_each_id(CancellationToken ct)
    {
        await using var lab = new OpenCodeLab();
        Seed(lab.Tree, $"2026-10-22-{Name}", "abcdefghijklmnopqrstuvwxyz");
        Seed(lab.Tree, $"2026-10-23-{Name}", "bcdefghijklmnopqrstuvwxyza");

        var error = await Assert.That(() => Invoke("status", Name)).Throws<HarnessException>();

        await Assert.That(error!.Message).Contains($"2026-10-22-{Name}, 2026-10-23-{Name}");
    }
}
