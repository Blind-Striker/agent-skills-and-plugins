using System.Text.Json.Nodes;
using Dispatch.Tests.Support;

namespace Dispatch.Tests.Integration;

[Category("Integration")]
[NotInParallel]
public sealed class OpenCodeCleanupTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    [Timeout(90_000)]
    public async Task Clean_structured_scratch_checks_ownership_and_removes_only_its_allocation(bool force, CancellationToken ct)
    {
        await using var lab = new OpenCodeLab();
        using var scratch = new ScratchFixture();
        var ledger = new Ledger("run");
        var control = Seed(lab.Tree, ledger);
        var spec = new TaskSpec { Id = "inspect", Kind = "structured", Project = "off" };
        var run = new TaskRun { Record = new() { Spec = spec } };
        var owned = scratch.Allocate(run, ledger, "owned");
        var other = new TaskRun { Record = new() { Spec = spec } };
        var foreign = scratch.Allocate(other, ledger, "foreign");
        var evidence = Path.Combine(foreign, "keep.txt");
        File.WriteAllText(evidence, "another allocation");
        var readOnly = Path.Combine(owned, "readonly.txt");
        File.WriteAllText(readOnly, "temporary");
        File.SetAttributes(readOnly, FileAttributes.ReadOnly);
        run.Record.Workspace.Path = foreign;
        ledger.SaveRecord(run.Record);
        ledger.Write(run.Id, "queued");
        lab.Add("GET /api/session", new JsonObject { ["data"] = new JsonArray(), ["cursor"] = new JsonObject() }, repeat: true);
        lab.Write();
        var environment = new Dictionary<string, string?> { ["FAKE_OPENCODE_SCENARIO"] = lab.Scenario,
            ["FAKE_OPENCODE_RECORD"] = Path.Combine(lab.Tree.Root, "requests.jsonl") };
        string[] arguments = force ? ["clean", "run", "--force"] : ["clean", "run"];
        using (var cleaner = new HarnessProcess(lab.Tree))
        {
            var refused = await cleaner.RunAsync(arguments, ct, environment);
            await Assert.That(refused.ExitCode).IsEqualTo(1);
        }
        await Assert.That(File.ReadAllText(evidence)).IsEqualTo("another allocation");
        await Assert.That(File.Exists(readOnly)).IsTrue();
        await Assert.That(File.Exists(Path.Combine(control, "setup.done"))).IsTrue();
        await Assert.That(ledger.Read().Last(x => x.Type == "cleaned").Data!["error"]!.GetValue<string>())
            .Contains("ownership does not match");

        run.Record.Workspace.Path = owned;
        ledger.SaveRecord(run.Record);
        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var cleaner = new HarnessProcess(lab.Tree);
            (await cleaner.RunAsync(arguments, ct, environment)).RequireSuccess("clean owned structured scratch");
            await Assert.That(Directory.Exists(owned)).IsFalse();
            await Assert.That(File.ReadAllText(evidence)).IsEqualTo("another allocation");
        }
        await Assert.That(Requests(lab.Tree).Where(x => x["unexpected"]!.GetValue<bool>())).IsEmpty();
    }

    [Test]
    [Timeout(90_000)]
    public async Task Clean_finds_structured_sessions_created_before_the_session_reference_was_saved(CancellationToken ct)
    {
        await using var lab = new OpenCodeLab();
        using var scratch = new ScratchFixture();
        var ledger = new Ledger("run");
        Seed(lab.Tree, ledger);
        var run = new TaskRun { Record = new() { Spec = new() { Id = "inspect", Kind = "structured", Project = "off" } } };
        ledger.Write(run.Id, "queued");
        var folder = scratch.Allocate(run, ledger, "queued");
        lab.SessionPage(folder, null, null, Session("orphan", "run", run.Id, ledger.LoadRunInfo().ControlId));
        lab.Add("DELETE /api/session/orphan", status: 204);
        lab.Write();
        using var cleaner = new HarnessProcess(lab.Tree);

        var result = await cleaner.RunAsync(["clean", "run"], ct,
            new Dictionary<string, string?> { ["FAKE_OPENCODE_SCENARIO"] = lab.Scenario,
                ["FAKE_OPENCODE_RECORD"] = Path.Combine(lab.Tree.Root, "requests.jsonl") });

        result.RequireSuccess("clean queued structured session");
        await Assert.That(Requests(lab.Tree).Where(x => x["method"]!.GetValue<string>() == "DELETE")
            .Select(x => x["path"]!.GetValue<string>())).IsEquivalentTo(["/api/session/orphan"]);
        await Assert.That(Requests(lab.Tree).Where(x => x["unexpected"]!.GetValue<bool>())).IsEmpty();
    }

    [Test]
    [Timeout(90_000)]
    public async Task Clean_same_run_and_task_in_two_ledgers_preserves_live_and_legacy_sessions(CancellationToken ct)
    {
        await using var lab = new OpenCodeLab();
        var ledger = new Ledger("run");
        Seed(lab.Tree, ledger);
        ledger.SaveRecord(new TaskRecord
        {
            Spec = new() { Id = "inspect", Kind = "research", Workspace = "in-place" },
            Workspace = new() { Path = lab.Tree.Repository },
        });
        ledger.Write("inspect", "final", new JsonObject { ["state"] = "done" });
        var firstRoot = Locations.LedgerRoot;
        var otherRoot = Path.Combine(lab.Tree.Root, "other-ledger");
        string otherControl;
        lab.Create("owned");
        lab.Create("foreign");
        lab.Write();
        await using (var creator = new OpenCodeBackend())
        {
            var launch = new WorkerLaunch("run", "inspect", "research", new("fixture", "model", null), null, "build",
                new("full", "on", "read-only", "on", "in-place"), lab.Tree.Repository, new([], []),
                new Dictionary<string, string>(), null, "full", null);
            await creator.CreateAsync(launch, ct);
            try
            {
                Locations.Configure(null, otherRoot);
                var other = new Ledger("run");
                otherControl = Seed(lab.Tree, other);
                other.SaveRecord(new TaskRecord
                {
                    Spec = new() { Id = "inspect", Kind = "research", Workspace = "in-place" },
                    Workspace = new() { Path = lab.Tree.Repository }, Session = new() { Id = "foreign" },
                });
                other.Write("inspect", "session", new JsonObject { ["id"] = "foreign" });
                await creator.CreateAsync(launch, ct);
            }
            finally { Locations.Configure(null, firstRoot); }
        }
        using var otherSupervisor = SupervisorLock.AcquireFolder(otherControl);
        var metadata = Requests(lab.Tree).Where(x => x["method"]!.GetValue<string>() == "POST"
            && x["path"]!.GetValue<string>() == "/api/session").Select(x => x["body"]!["metadata"]!.DeepClone()).ToArray();
        lab.Steps.RemoveRange(4, lab.Steps.Count - 4);
        var owned = Session("owned", "run", "inspect");
        owned["metadata"] = metadata[0];
        var foreign = Session("foreign", "run", "inspect");
        foreign["metadata"] = metadata[1];
        lab.SessionPage(lab.Tree.Repository, null, null, owned, foreign, Session("legacy", "run", "inspect"));
        foreach (var id in new[] { "owned", "foreign", "legacy" }) lab.Add($"DELETE /api/session/{id}", status: 204);
        lab.Write();
        using var cleaner = new HarnessProcess(lab.Tree);

        var result = await cleaner.RunAsync(["clean", "run", "--ledger", firstRoot], ct,
            new Dictionary<string, string?> { ["FAKE_OPENCODE_SCENARIO"] = lab.Scenario,
                ["FAKE_OPENCODE_RECORD"] = Path.Combine(lab.Tree.Root, "requests.jsonl") });

        result.RequireSuccess("clean one ledger");
        await Assert.That(Requests(lab.Tree).Where(x => x["method"]!.GetValue<string>() == "DELETE")
            .Select(x => x["path"]!.GetValue<string>())).IsEquivalentTo(["/api/session/owned"]);
        await Assert.That(metadata[0]["dispatch.controlId"]!.GetValue<string>()).IsEqualTo(ledger.LoadRunInfo().ControlId);
        await Assert.That(metadata[1]["dispatch.controlId"]!.GetValue<string>()).IsEqualTo(Path.GetFileName(otherControl));
        await Assert.That(File.Exists(Path.Combine(otherControl, "setup.done"))).IsTrue();
        await Assert.That(Requests(lab.Tree).Where(x => x["unexpected"]!.GetValue<bool>())).IsEmpty();
    }

    [Test]
    [Timeout(45_000)]
    public async Task Orphans_include_final_and_unknown_tasks_across_pages(CancellationToken ct)
    {
        await using var lab = new OpenCodeLab();
        var ledger = new Ledger("run");
        Seed(lab.Tree, ledger);
        var controlId = ledger.LoadRunInfo().ControlId;
        lab.SessionPage(lab.Tree.Repository, null, "second",
            Session("foreign", "another-run", "inspect"), Session("known", "run", "unfinished", controlId));
        lab.SessionPage(lab.Tree.Repository, "second", null,
            Session("final", "run", "finished", controlId), Session("orphan", "run", "unknown", controlId),
            Session("unowned", null, null), Session("another-unfinished-session", "run", "unfinished", controlId));
        lab.Write();

        var orphans = await lab.Backend.FindOrphansAsync(new("run",
            [new("opencode", "unfinished", "known", null, new())], [lab.Tree.Repository, lab.Tree.Repository]), ct);

        await Assert.That(orphans.Select(x => x.Id).Order()).IsEquivalentTo(["final", "orphan"]);
        await Assert.That(orphans.Single(x => x.Id == "final").TaskId).IsEqualTo("finished");
        await Assert.That(orphans.Single(x => x.Id == "orphan").TaskId).IsEqualTo("unknown");
    }

    static JsonObject Session(string id, string? run, string? task, string? controlId = null)
    {
        var session = OpenCodeLab.Session(id)["data"]!.DeepClone().AsObject();
        session["metadata"] = new JsonObject { ["dispatch.run"] = run, ["dispatch.task"] = task };
        if (controlId is not null) session["metadata"]!["dispatch.controlId"] = controlId;
        return session;
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    [Timeout(90_000)]
    public async Task Run_releases_done_worker_after_saving_verified_result(bool releaseFails, CancellationToken ct)
    {
        await using var lab = new OpenCodeLab();
        var repo = await LabRepo.CreateAsync(lab.Tree, ct);
        var taskFile = Path.Combine(lab.Tree.Root, "tasks.jsonc");
        File.WriteAllText(taskFile, """
            {"schemaVersion":2,"run":"settlement","tasks":[{"id":"inspect","kind":"research","backend":"opencode","route":"fixture/model","workspace":"in-place","commit":"none","prompt":"Inspect.","verify":["exit 0"]}]}
            """);
        lab.Steps.AddRange(File.ReadLines(FakeOpenCode.Fixture("one-task.jsonl")).Skip(4)
            .Select(x => JsonNode.Parse(x)!.AsObject())
            .Where(step => step["expect"]!.GetValue<string>() != "DELETE /api/session/ses_fixture"));
        lab.Steps.Single(step => step["expect"]!.GetValue<string>() == "POST /api/session/ses_fixture/permission/per_fixture/reply")["body"]
            = new JsonObject { ["decision"] = "reject", ["message"] = Policy.LeadDeclined };
        var releasing = Path.Combine(lab.Tree.Root, "releasing");
        var continueRelease = Path.Combine(lab.Tree.Root, "continue-release");
        var release = lab.Add("DELETE /api/session/ses_fixture", status: releaseFails ? 500 : 204);
        release["arrived"] = releasing;
        release["waitFor"] = continueRelease;
        lab.Write();
        var environment = new Dictionary<string, string?>
        {
            ["FAKE_OPENCODE_SCENARIO"] = lab.Scenario,
            ["FAKE_OPENCODE_RECORD"] = Path.Combine(lab.Tree.Root, "requests.jsonl"),
        };
        using var runner = new HarnessProcess(lab.Tree);

        var running = runner.RunAsync(["run", taskFile, "--repo", repo.Root], ct, environment);
        try
        {
            using var poll = new PeriodicTimer(TimeSpan.FromMilliseconds(20));
            while (!running.IsCompleted && !File.Exists(releasing)) await poll.WaitForNextTickAsync(ct);
            await Assert.That(File.Exists(releasing)).IsTrue();
            var folder = Directory.GetDirectories(Path.Combine(lab.Tree.DispatchHome, "state", "ledgers")).Single();
            var beforeRelease = JsonNode.Parse(File.ReadAllText(Path.Combine(folder, "inspect", "result.json")))!;
            await Assert.That(beforeRelease["state"]!.GetValue<string>()).IsEqualTo("done");
            await Assert.That(beforeRelease["verify"]![0]!["exitCode"]!.GetValue<int>()).IsEqualTo(0);
        }
        finally { File.WriteAllText(continueRelease, "continue"); }
        var result = await running;

        result.RequireSuccess("settle done task");
        var runFolder = Directory.GetDirectories(Path.Combine(lab.Tree.DispatchHome, "state", "ledgers")).Single();
        var saved = JsonNode.Parse(File.ReadAllText(Path.Combine(runFolder, "inspect", "result.json")))!;
        await Assert.That(saved["state"]!.GetValue<string>()).IsEqualTo("done");
        await Assert.That(saved["verify"]![0]!["exitCode"]!.GetValue<int>()).IsEqualTo(0);
        await Assert.That(Requests(lab.Tree).Count(x => x["method"]!.GetValue<string>() == "DELETE"
            && x["path"]!.GetValue<string>() == "/api/session/ses_fixture")).IsEqualTo(1);
        var ledger = new Ledger(Path.GetFileName(runFolder));
        await Assert.That(ledger.Read().Last(x => x.Task == "inspect" && x.Type == "final").Data!["state"]!.GetValue<string>()).IsEqualTo("done");
        var releaseErrors = ledger.Read().Where(x => x.Task == "inspect" && x.Type == "error" && x.Data?["while"]?.GetValue<string>() == "release").ToArray();
        await Assert.That(releaseErrors.Length).IsEqualTo(releaseFails ? 1 : 0);
        if (releaseFails)
        {
            await Assert.That(releaseErrors[0].Data!["message"]!.GetValue<string>()).Contains("HTTP 500");
            lab.Steps.RemoveRange(4, lab.Steps.Count - 4);
            lab.SessionPage(repo.Root, null, null, Session("ses_fixture", ledger.RunId, "inspect", ledger.LoadRunInfo().ControlId));
            lab.Add("DELETE /api/session/ses_fixture", status: 204);
            lab.Write();
            using var cleaner = new HarnessProcess(lab.Tree);
            (await cleaner.RunAsync(["clean", ledger.RunId], ct, environment)).RequireSuccess("clean after release failure");
            await Assert.That(Requests(lab.Tree).Count(x => x["method"]!.GetValue<string>() == "DELETE")).IsEqualTo(2);
        }
        else
        {
            lab.Steps.RemoveRange(4, lab.Steps.Count - 4);
            lab.Add("GET /api/session/ses_fixture", new JsonObject { ["_tag"] = "SessionNotFoundError", ["message"] = "missing" }, 404);
            lab.Write();
            using var collector = new HarnessProcess(lab.Tree);
            var collected = await collector.RunAsync(["collect", ledger.RunId, "inspect"], ct, environment);
            await Assert.That(collected.ExitCode).IsEqualTo(2);
            await Assert.That(collected.Stderr).Contains("the session was released when the task finished; result.json is final");
        }
        await Assert.That(Requests(lab.Tree).Where(x => x["unexpected"]!.GetValue<bool>())).IsEmpty();
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
        lab.Steps.Single(step => step["expect"]!.GetValue<string>() == "POST /api/session/ses_fixture/permission/per_fixture/reply")["body"]
            = new JsonObject { ["decision"] = "reject", ["message"] = Policy.LeadDeclined };
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
            Session("ses_fixture", ledger.RunId, "inspect", ledger.LoadRunInfo().ControlId),
            Session("orphan", ledger.RunId, "missing-task", ledger.LoadRunInfo().ControlId));
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
