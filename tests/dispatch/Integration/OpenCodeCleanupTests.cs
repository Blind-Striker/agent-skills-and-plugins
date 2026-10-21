using System.Text.Json.Nodes;
using Dispatch.Tests.Support;

namespace Dispatch.Tests.Integration;

[Category("Integration")]
[NotInParallel]
public sealed class OpenCodeCleanupTests
{
    [Test]
    [Timeout(45_000)]
    public async Task Orphans_include_final_and_unknown_tasks_across_pages(CancellationToken ct)
    {
        await using var lab = new OpenCodeLab();
        lab.SessionPage(lab.Tree.Repository, null, "second",
            Session("foreign", "another-run", "inspect"), Session("known", "run", "unfinished"));
        lab.SessionPage(lab.Tree.Repository, "second", null,
            Session("final", "run", "finished"), Session("orphan", "run", "unknown"),
            Session("unowned", null, null), Session("another-unfinished-session", "run", "unfinished"));
        lab.Write();

        var orphans = await lab.Backend.FindOrphansAsync(new("run",
            [new("opencode", "unfinished", "known", null, new())], [lab.Tree.Repository, lab.Tree.Repository]), ct);

        await Assert.That(orphans.Select(x => x.Id).Order()).IsEquivalentTo(["final", "orphan"]);
        await Assert.That(orphans.Single(x => x.Id == "final").TaskId).IsEqualTo("finished");
        await Assert.That(orphans.Single(x => x.Id == "orphan").TaskId).IsEqualTo("unknown");
    }

    static JsonObject Session(string id, string? run, string? task)
    {
        var session = OpenCodeLab.Session(id)["data"]!.DeepClone().AsObject();
        session["metadata"] = new JsonObject { ["dispatch.run"] = run, ["dispatch.task"] = task };
        return session;
    }

    [Test]
    [Timeout(90_000)]
    public async Task Clean_releases_recorded_and_paged_orphan_sessions_but_preserves_foreign_sessions(CancellationToken ct)
    {
        await using var lab = new OpenCodeLab();
        var repo = await LabRepo.CreateAsync(lab.Tree, ct);
        var taskFile = Path.Combine(lab.Tree.Root, "tasks.jsonc");
        File.WriteAllText(taskFile, """
            {"schemaVersion":2,"run":"cleanup","tasks":[{"id":"inspect","kind":"research","backend":"opencode","route":"fixture/model","workspace":"in-place","commit":"none","prompt":"Inspect.","verify":["exit 3"]}]}
            """);
        lab.Steps.AddRange(File.ReadLines(FakeOpenCode.Fixture("one-task.jsonl")).Skip(4)
            .Select(x => JsonNode.Parse(x)!.AsObject()));
        lab.Write();
        var environment = new Dictionary<string, string?>
        {
            ["FAKE_OPENCODE_SCENARIO"] = lab.Scenario,
            ["FAKE_OPENCODE_RECORD"] = Path.Combine(lab.Tree.Root, "requests.jsonl"),
        };
        using (var runner = new HarnessProcess(lab.Tree))
        {
            var failed = await runner.RunAsync(["run", taskFile, "--repo", repo.Root], ct, environment);
            await Assert.That(failed.ExitCode).IsEqualTo(1);
        }
        var runFolder = Directory.GetDirectories(Path.Combine(lab.Tree.DispatchHome, "state", "ledgers")).Single();
        var ledger = new Ledger(Path.GetFileName(runFolder));
        await Assert.That(File.ReadAllText(Path.Combine(runFolder, "inspect", "result.json"))).Contains("verify-failed");
        await Assert.That(Requests(lab.Tree).Where(x => x["method"]!.GetValue<string>() == "DELETE")).IsEmpty();
        var control = Locations.ControlDir(ledger.RunId);
        lab.Steps.RemoveRange(4, lab.Steps.Count - 4);
        lab.SessionPage(repo.Root, null, "second", Session("foreign", "foreign-run", "inspect"));
        lab.SessionPage(repo.Root, "second", null,
            Session("ses_fixture", ledger.RunId, "inspect"), Session("orphan", ledger.RunId, "missing-task"));
        lab.Add("DELETE /api/session/ses_fixture", status: 204);
        lab.Add("DELETE /api/session/orphan", status: 204);
        lab.Write();
        using var child = new HarnessProcess(lab.Tree);

        var result = await child.RunAsync(["clean", ledger.RunId], ct, environment);

        result.RequireSuccess("clean sessions");
        var deletes = Requests(lab.Tree).Where(x => x["method"]!.GetValue<string>() == "DELETE")
            .Select(x => x["path"]!.GetValue<string>()).ToArray();
        await Assert.That(deletes).IsEquivalentTo(["/api/session/ses_fixture", "/api/session/orphan"]);
        await Assert.That(Requests(lab.Tree).Where(x => x["unexpected"]!.GetValue<bool>())).IsEmpty();
        await Assert.That(Directory.GetFileSystemEntries(control).Select(x => Path.GetFileName(x)!)).IsEquivalentTo(["run.ref", "supervisor.lock"]);
    }

    static string Seed(TempTree tree, Ledger ledger)
    {
        var controlId = Locations.NewControlId();
        var control = Locations.CreateControlFolder(controlId);
        Json.WriteFile(Path.Combine(ledger.Dir, "run.json"),
            new RunInfo { Run = ledger.RunId, ControlId = controlId, Repo = tree.Repository }, LedgerJson.Files.RunInfo);
        Json.WriteFile(Path.Combine(control, "run.ref"),
            new RunReference { Run = ledger.RunId, LedgerRoot = Path.GetDirectoryName(ledger.Dir)! }, ControlJson.Files.RunReference);
        File.WriteAllText(Path.Combine(control, "setup.done"), "");
        return control;
    }

    [Test]
    [Arguments("done", 2)]
    [Arguments("failed", 1)]
    [Timeout(90_000)]
    public async Task Collect_on_a_released_done_task_is_a_refusal(string state, int exit, CancellationToken ct)
    {
        await using var lab = new OpenCodeLab();
        var ledger = new Ledger("run");
        Seed(lab.Tree, ledger);
        ledger.SaveRecord(new TaskRecord
        {
            Spec = new() { Id = "inspect", Kind = "research" }, Session = new() { Id = "released" },
            Workspace = new() { Path = lab.Tree.Repository },
        });
        Json.WriteFile(Path.Combine(ledger.Dir, "config.json"), new EffectiveConfig
        {
            Tasks = new() { ["inspect"] = new() { Settings = new()
                { Backend = new() { Value = "opencode" }, Route = new() { Value = "opencode:fixture/model" } } } },
        }, LedgerJson.Files.EffectiveConfig);
        ledger.Write("inspect", "final", new JsonObject { ["state"] = state });
        var finalPath = Path.Combine(ledger.Dir, "inspect", "result.json");
        File.WriteAllText(finalPath, "{\"state\":\"" + state + "\"}");
        var events = File.ReadAllText(Path.Combine(ledger.Dir, "events.jsonl"));
        lab.Add("GET /api/session/released", new JsonObject { ["_tag"] = "SessionNotFoundError", ["message"] = "missing" }, 404);
        lab.Write();
        using var child = new HarnessProcess(lab.Tree);

        var result = await child.RunAsync(["collect", "run", "inspect"], ct,
            new Dictionary<string, string?> { ["FAKE_OPENCODE_SCENARIO"] = lab.Scenario,
                ["FAKE_OPENCODE_RECORD"] = Path.Combine(lab.Tree.Root, "requests.jsonl") });

        TestContext.Current!.Output.WriteLine($"exit {result.ExitCode}\n{result.Stdout}\n{result.Stderr}");
        await Assert.That(result.ExitCode).IsEqualTo(exit);
        await Assert.That(File.ReadAllText(finalPath)).IsEqualTo("{\"state\":\"" + state + "\"}");
        if (state == "done")
        {
            await Assert.That(result.Stderr).Contains("the session was released when the task finished; result.json is final");
            await Assert.That(File.ReadAllText(Path.Combine(ledger.Dir, "events.jsonl"))).IsEqualTo(events);
        }
    }

    static JsonObject[] Requests(TempTree tree)
    {
        var path = Path.Combine(tree.Root, "requests.jsonl");
        return File.Exists(path) ? File.ReadAllLines(path).Select(x => JsonNode.Parse(x)!.AsObject()).ToArray() : [];
    }
}
