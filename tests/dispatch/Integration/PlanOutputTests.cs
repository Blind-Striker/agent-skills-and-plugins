using Dispatch.Tests.Support;
using TUnit.Assertions.Enums;

namespace Dispatch.Tests.Integration;

[Category("Integration")]
[NotInParallel("processes")]
public sealed class PlanOutputTests
{
    [Test]
    [NotInParallel]
    [Arguments("both")]
    [Arguments("paths")]
    [Arguments("failure")]
    public async Task ContinueOf_refuses_unledgered_work_at_plan_and_preparation(string evidence, CancellationToken ct)
    {
        using var lab = new CoreLab("new-attempt");
        var repo = await LabRepo.CreateAsync(lab.Tree, ct);
        string? lockPath = null;
        var source = await lab.SourceAttemptAsync(ct, "done", edit: async path =>
        {
            File.WriteAllText(Path.Combine(path, "committed.txt"), "worker commit\n");
            await repo.GitAsync(["-C", path, "add", "-A"], ct);
            await repo.GitAsync(["-C", path, "commit", "-m", "Worker work"], ct);
            File.WriteAllText(Path.Combine(path, "z-left.txt"), "leftover z\n");
            File.WriteAllText(Path.Combine(path, "a-left.txt"), "leftover a\n");
            lockPath = Path.GetFullPath(await repo.GitAsync(["-C", path, "rev-parse", "--git-path", "refs/heads/dispatch/2026-10-31-source/source.lock"], ct), path);
            File.WriteAllText(lockPath, "owned test lock");
        });
        File.Delete(lockPath!);
        await Assert.That(source.Result.Failures).IsEquivalentTo(new[] { "commit-failed", "out-of-scope" }, CollectionOrdering.Matching);
        await Assert.That(source.Result.FilesChanged).IsEquivalentTo(new[] { "committed.txt" }, CollectionOrdering.Matching);
        await Assert.That(source.Result.OutOfScope).IsEquivalentTo(new[] { "a-left.txt", "z-left.txt" }, CollectionOrdering.Matching);
        await Assert.That(File.ReadAllText(Path.Combine(source.Run.Record.Workspace.Path!, "a-left.txt"))).IsEqualTo("leftover a\n");
        if (evidence == "paths") source.Result.Failures.Remove("commit-failed");
        if (evidence == "failure") source.Result.OutOfScope = [];
        source.Ledger.SaveResult(source.Result);
        var next = lab.Continuation();
        var message = "continueOf 2026-10-31-source/source: its uncommitted work is not in the ledger: "
            + (evidence == "failure" ? "commit-failed" : "a-left.txt, z-left.txt");
        await Assert.That((await lab.PlanAsync(ct, next.Record.Spec)).Errors).IsEquivalentTo(new[] { "next: " + message }, CollectionOrdering.Matching);
        await Assert.That(async () => await Worktree.PrepareAsync(next, lab.Ledger, repo.Root, ct, lab.Git))
            .Throws<HarnessException>().WithMessage(message);
        await Assert.That(next.Record.Workspace.Path).IsNull();
    }

    [Test]
    [NotInParallel]
    public async Task ContinueOf_accepts_a_verify_failure_without_leftovers(CancellationToken ct)
    {
        using var lab = new CoreLab("new-attempt");
        var source = await lab.SourceAttemptAsync(ct, "done", failure: "verify-failed");
        var next = lab.Continuation();
        await Assert.That(source.Result.OutOfScope).IsEmpty();
        await Assert.That((await lab.PlanAsync(ct, next.Record.Spec)).Errors).IsEmpty();
        await Worktree.PrepareAsync(next, lab.Ledger, lab.Tree.Repository, ct, lab.Git);
        await Assert.That(File.ReadAllText(Path.Combine(next.Record.Workspace.Path!, "source.txt"))).IsEqualTo("source work\n");
    }

    [Test]
    [NotInParallel]
    public async Task ContinueOf_reuses_a_task_file_name_on_a_later_day(CancellationToken ct)
    {
        using var lab = new CoreLab(Commands.CreateRunId("source"), recorded: false);
        var source = await lab.SourceAttemptAsync(ct);
        var next = lab.Continuation(reference: "source/source");
        var path = Path.Combine(lab.Tree.Root, "source.tasks.jsonc");
        Json.WriteFile(path, new TaskFile { Tasks = [next.Record.Spec] }, InputJson.Read.TaskFile);
        var plan = await Planner.LoadAsync(path, lab.Tree.Repository, AllowList.From(null), ct, lab.Git);
        await Assert.That(plan.Errors).IsEmpty();
        await Assert.That(plan.Tasks.Select(task => task.Spec.Id!)).IsEquivalentTo(new[] { "next" }, CollectionOrdering.Matching);
        lab.Ledger.Write(next.Id, "queued");
        var preparedPlan = await Planner.LoadAsync(path, lab.Tree.Repository, AllowList.From(null), ct, lab.Git);
        await Assert.That(preparedPlan.Errors).IsEmpty();
        await Assert.That(preparedPlan.Tasks.Select(task => task.Spec.Id!)).IsEquivalentTo(new[] { "next" }, CollectionOrdering.Matching);
        await Worktree.PrepareAsync(next, lab.Ledger, lab.Tree.Repository, ct, lab.Git);
        await Assert.That(File.ReadAllText(Path.Combine(next.Record.Workspace.Path!, "source.txt"))).IsEqualTo("source work\n");
        await Assert.That(lab.Ledger.Read().Last().Data!["continueOf"]!.GetValue<string>()).IsEqualTo(source.Ledger.RunId + "/source");
    }

    [Test]
    [NotInParallel]
    public async Task ContinueOf_refuses_a_non_writer_source(CancellationToken ct)
    {
        using var lab = new CoreLab("new-attempt");
        var source = await lab.SourceAttemptAsync(ct);
        source.Run.Record.Spec.Kind = "research";
        source.Ledger.SaveRecord(source.Run.Record);
        var next = lab.Continuation();
        const string message = "continueOf 2026-10-31-source/source: its kind research holds no work to continue";
        await Assert.That((await lab.PlanAsync(ct, next.Record.Spec)).Errors).IsEquivalentTo(new[] { "next: " + message }, CollectionOrdering.Matching);
        await Assert.That(async () => await Worktree.PrepareAsync(next, lab.Ledger, lab.Tree.Repository, ct, lab.Git))
            .Throws<HarnessException>().WithMessage(message);
    }

    [Test]
    [NotInParallel]
    public async Task ContinueOf_warns_for_a_different_recorded_repository(CancellationToken ct)
    {
        using var lab = new CoreLab("new-attempt");
        var source = await lab.SourceAttemptAsync(ct);
        var info = source.Ledger.LoadRunInfo();
        info.Repo = Path.Combine(lab.Tree.Root, "another-worktree");
        Json.WriteFile(Path.Combine(source.Ledger.Dir, "run.json"), info, LedgerJson.Files.RunInfo);
        var next = lab.Continuation();
        var plan = await lab.PlanAsync(ct, next.Record.Spec);
        await Assert.That(plan.Errors).IsEmpty();
        await Assert.That(plan.Warnings).IsEquivalentTo(new[] {
            $"next: continueOf 2026-10-31-source/source was recorded in {info.Repo}, not this repository; commit ids resolve, but its base ref may mean another commit here" }, CollectionOrdering.Matching);
        await Worktree.PrepareAsync(next, lab.Ledger, lab.Tree.Repository, ct, lab.Git);
        await Assert.That(File.ReadAllText(Path.Combine(next.Record.Workspace.Path!, "source.txt"))).IsEqualTo("source work\n");
    }

    [Test]
    [NotInParallel]
    public async Task ContinueOf_checks_all_local_conflicts(CancellationToken ct)
    {
        using var lab = new CoreLab("new-attempt");
        await LabRepo.CreateAsync(lab.Tree, ct);
        var task = lab.Continuation().Record.Spec;
        task.Kind = "research";
        task.Base = "main";
        task.ReviewOf = "author";
        task.Workspace = "in-place";
        var plan = await lab.PlanAsync(ct, task);
        await Assert.That(plan.Errors).IsEquivalentTo(new[] {
            "next: continueOf requires kind code or mechanical", "next: continueOf refuses base",
            "next: continueOf refuses reviewOf", "next: continueOf refuses in-place workspace",
            "next: in-place workspace refuses base and reviewOf" }, CollectionOrdering.Matching);
        task.Kind = "code";
        task.Workspace = "worktree";
        task.Base = null;
        task.ReviewOf = null;
        task.Scope = [];
        task.ContinueOf = "../source";
        var scope = await lab.PlanAsync(ct, task);
        await Assert.That(scope.Errors).IsEquivalentTo(new[] {
            "next: continueOf must name <run>/<task> using safe run and task names",
            "next: code and mechanical tasks declare a scope" }, CollectionOrdering.Matching);
    }

    [Test]
    [NotInParallel]
    public async Task ContinueOf_resolves_only_ledger_root_sources(CancellationToken ct)
    {
        using var lab = new CoreLab("new-attempt");
        var source = await lab.SourceAttemptAsync(ct);
        var task = lab.Continuation().Record.Spec;
        foreach (var reference in new[] { "../source", "/source/task", "C:/source", "source\\task", "source/../task", "source/task/extra", "source//task", "source/..", "source/.dispatch", "source:/task" })
        {
            task.ContinueOf = reference;
            await Assert.That((await lab.PlanAsync(ct, task)).Errors).IsEquivalentTo(new[] {
                "next: continueOf must name <run>/<task> using safe run and task names" }, CollectionOrdering.Matching);
        }
        task.ContinueOf = Commands.CreateRunId("new-attempt") + "/source";
        await Assert.That((await lab.PlanAsync(ct, task)).Errors).IsEquivalentTo(new[] {
            "next: continueOf must name an earlier, distinct run" }, CollectionOrdering.Matching);
        task.ContinueOf = "absent/source";
        await Assert.That((await lab.PlanAsync(ct, task)).Errors).IsEquivalentTo(new[] {
            $"next: no ledger for run absent under {Locations.LedgerRoot}" }, CollectionOrdering.Matching);
        task.ContinueOf = source.Ledger.RunId + "/missing";
        await Assert.That((await lab.PlanAsync(ct, task)).Errors).IsEquivalentTo(new[] {
            $"next: no task record {Path.Combine(source.Ledger.Dir, "missing", "task.json")}" }, CollectionOrdering.Matching);
        foreach (var reference in new[] { source.Ledger.RunId + "/source", "source/source" })
        {
            task.ContinueOf = reference;
            var accepted = await lab.PlanAsync(ct, task);
            await Assert.That(accepted.Errors).IsEmpty();
            await Assert.That(accepted.Tasks.Select(x => x.Spec.Id!)).IsEquivalentTo(new[] { "next" }, CollectionOrdering.Matching);
            await Assert.That(accepted.Info).IsEquivalentTo(new[] {
                $"next: continues {source.Ledger.RunId}/source from {source.Run.Record.Workspace.BaseSha![..12]}" }, CollectionOrdering.Matching);
        }
        Directory.CreateDirectory(Path.Combine(Locations.LedgerRoot, "2026-10-30-source"));
        task.ContinueOf = "source/source";
        await Assert.That((await lab.PlanAsync(ct, task)).Errors).IsEquivalentTo(new[] {
            "next: run name source matches 2 ledgers: 2026-10-30-source, 2026-10-31-source; name the full run id" }, CollectionOrdering.Matching);
    }

    [Test]
    [NotInParallel]
    [Arguments("done", "source must have a consistent final failed result")]
    [Arguments("cancelled", "source must have a consistent final failed result")]
    [Arguments("running", "source must have a consistent final failed result")]
    [Arguments("result", "source must have a consistent final failed result")]
    [Arguments("zombie", "refuses a zombie or unconfirmed worker")]
    [Arguments("unconfirmed", "refuses a zombie or unconfirmed worker")]
    [Arguments("in-place", "requires a harness-managed worktree with harness commit mode")]
    [Arguments("foreign", "requires a harness-managed worktree with harness commit mode")]
    [Arguments("commit-mode", "requires a harness-managed worktree with harness commit mode")]
    [Arguments("ineligible", "requires a stopped/blocked or committed failed target")]
    [Arguments("collected", "source collection evidence is incomplete or inconsistent")]
    [Arguments("old-collected", "source collection evidence is incomplete or inconsistent")]
    [Arguments("missing-patch", "source collection evidence is incomplete or inconsistent")]
    [Arguments("unnamed-patch", "source collection evidence is incomplete or inconsistent")]
    [Arguments("wrong-patch", "source collection evidence is incomplete or inconsistent")]
    [Arguments("count", "source collection evidence is incomplete or inconsistent")]
    [Arguments("final", "")]
    public async Task ContinueOf_requires_an_eligible_complete_failed_attempt(string obstacle, string diagnostic, CancellationToken ct)
    {
        using var lab = new CoreLab("new-attempt");
        var source = await lab.SourceAttemptAsync(ct);
        switch (obstacle)
        {
            case "done": case "cancelled": source.Ledger.Write("source", "final", new() { ["state"] = obstacle }); break;
            case "running": source.Ledger.Write("source", "prompt"); break;
            case "result": source.Result.State = "done"; break;
            case "zombie": source.Result.Failures.Add("zombie"); break;
            case "unconfirmed": source.Ledger.Write("source", "stop-unconfirmed"); break;
            case "in-place": source.Run.Record.Spec.Workspace = "in-place"; break;
            case "foreign": source.Run.Record.Workspace.Branch = "foreign"; break;
            case "commit-mode": source.Result.CommitMode = "none"; break;
            case "ineligible": source.Result.WorkerStatus = "done"; break;
            case "collected":
                var events = Path.Combine(source.Ledger.Dir, "events.jsonl");
                File.WriteAllLines(events, File.ReadAllLines(events).Where(x => !x.Contains("\"type\":\"collected\"", StringComparison.Ordinal)).ToArray());
                break;
            case "old-collected": source.Ledger.Write("source", "final", new() { ["state"] = "failed" }); break;
            case "missing-patch": File.Delete(Path.Combine(source.Ledger.Dir, "source", "wip.patch")); break;
            case "unnamed-patch": source.Result.WipPatch = null; break;
            case "wrong-patch": source.Result.WipPatch = "../wip.patch"; break;
            case "count": source.Result.WipFileCount = 42; break;
            case "final": File.Delete(Path.Combine(source.Ledger.Dir, "source", "final.md")); break;
        }
        source.Ledger.SaveResult(source.Result);
        source.Ledger.SaveRecord(source.Run.Record);
        var plan = await lab.PlanAsync(ct, lab.Continuation().Record.Spec);
        await Assert.That(plan.Errors).IsEquivalentTo(new[] { obstacle == "final"
            ? $"next: Could not find file '{Path.Combine(source.Ledger.Dir, "source", "final.md")}'."
            : "next: continueOf " + diagnostic }, CollectionOrdering.Matching);
    }

    [Test]
    [NotInParallel]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ContinueOf_checks_both_required_commits(bool creation, CancellationToken ct)
    {
        using var lab = new CoreLab("new-attempt");
        var source = await lab.SourceAttemptAsync(ct, "done", failure: "verify-failed");
        var missing = new string('f', 40);
        if (creation)
        {
            source.Result.Commit = missing;
            source.Ledger.Write("source", "collected", new() { ["commit"] = missing, ["files"] = 1 });
            source.Ledger.Write("source", "final", new() { ["state"] = "failed" });
        }
        else source.Run.Record.Workspace.BaseSha = missing;
        source.Ledger.SaveRecord(source.Run.Record);
        source.Ledger.SaveResult(source.Result);
        var plan = await lab.PlanAsync(ct, lab.Continuation().Record.Spec);
        await Assert.That(plan.Errors).IsEquivalentTo(new[] {
            $"next: continueOf {(creation ? "commit" : "baseSha")} {missing} does not resolve as a commit in {lab.Tree.Repository}" }, CollectionOrdering.Matching);
    }

    [Test]
    [NotInParallel]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ContinueOf_reports_every_inherited_scope_violation(bool ignoreCase, CancellationToken ct)
    {
        using var lab = new CoreLab("new-attempt");
        var source = await lab.SourceAttemptAsync(ct);
        await new LabRepo(lab.Tree).GitAsync(["config", "core.ignorecase", ignoreCase.ToString().ToLowerInvariant()], ct);
        source.Result.FilesChanged = ["z/out.txt", "src/secret.txt", ".dispatch/config.jsonc", "a/out.txt", "src/ceiling.txt", "SRC/ok.txt"];
        source.Result.WipFileCount = 6;
        source.Ledger.SaveResult(source.Result);
        source.Ledger.Write("source", "collected", new() { ["files"] = 6 });
        source.Ledger.Write("source", "final", new() { ["state"] = "failed" });
        var run = lab.Continuation();
        run.Record.Settings.Scope = new() { Value = ["src/**", "!src/secret.txt"], Ceilings = [new() { Globs = ["!src/ceiling.txt"], From = "fixture" }] };
        var errors = new List<string>();
        try { await Planner.CheckContinuationAsync(run.Record.Spec, run.Record.Settings, lab.Ledger.RunId, lab.Tree.Repository, lab.Git, ct); }
        catch (HarnessException ex) { errors.Add(ex.Message); }
        await Assert.That(errors).IsEquivalentTo(new[] { "continueOf inherited paths outside scope: .dispatch/config.jsonc, "
            + (ignoreCase ? "" : "SRC/ok.txt, ") + "a/out.txt, src/ceiling.txt, src/secret.txt, z/out.txt" }, CollectionOrdering.Matching);
        run.Record.Spec.Scope = ["src/**", "!src/secret.txt"];
        var plan = await lab.PlanAsync(ct, run.Record.Spec);
        await Assert.That(plan.Errors).IsEquivalentTo(new[] { "next: continueOf inherited paths outside scope: .dispatch/config.jsonc, "
            + (ignoreCase ? "" : "SRC/ok.txt, ") + "a/out.txt, src/secret.txt, z/out.txt" }, CollectionOrdering.Matching);
    }

    [Test]
    [NotInParallel]
    public async Task ContinueOf_prints_the_creation_commit_in_task_order(CancellationToken ct)
    {
        using var lab = new CoreLab("new-attempt");
        var source = await lab.SourceAttemptAsync(ct, "done", failure: "outcome");
        var zulu = lab.Continuation("zulu").Record.Spec;
        zulu.Scope = ["source.txt", "z.txt"];
        var alpha = lab.Continuation("alpha").Record.Spec;
        alpha.Kind = "mechanical";
        alpha.Scope = ["source.txt", "a.txt"];
        var path = Path.Combine(lab.Tree.Root, "info.tasks.jsonc");
        Json.WriteFile(path, new TaskFile { Run = "new-attempt", SharedPaths = ["source.txt"], Tasks = [zulu, alpha] }, InputJson.Read.TaskFile);
        var plan = await Planner.LoadAsync(path, lab.Tree.Repository, AllowList.From(null), ct, lab.Git);
        await Assert.That(plan.Errors).IsEmpty();
        await Assert.That(plan.Tasks.Select(x => x.Spec.Id!)).IsEquivalentTo(new[] { "zulu", "alpha" }, CollectionOrdering.Matching);
        await Assert.That(plan.Info).IsEquivalentTo(new[] { "zulu", "alpha" }.Select(id =>
            $"{id}: continues {source.Ledger.RunId}/source from {source.Result.Commit![..12]}"), CollectionOrdering.Matching);
        var output = TestContext.Current!.Output;
        var start = output.GetStandardOutput().Length;
        await Assert.That(plan.Report()).IsEqualTo(0);
        await Assert.That(output.GetStandardOutput()[start..].ReplaceLineEndings("\n")).IsEqualTo(
            $"INFO zulu: continues {source.Ledger.RunId}/source from {source.Result.Commit![..12]}\n"
            + $"INFO alpha: continues {source.Ledger.RunId}/source from {source.Result.Commit![..12]}\n"
            + $"plan ok: 2 task(s) against {lab.Tree.Repository}\n");
    }

    [Test]
    [Arguments("Base: fedcba98765432100123456789abcdef0123456789.", "fedcba98765432100123456789abcdef0123456789", "worktree")]
    [Arguments("Base: a7654321.", "a7654321", "worktree")]
    [Arguments("bAsE: FEDCBA98765432100123456789ABCDEF0123456789.", "fedcba98765432100123456789abcdef0123456789", "worktree")]
    [Arguments("Base: a7654321, a7654321.", "a7654321", "worktree")]
    [Arguments("Base: FEDCBA98-a2345678, fedcba98.\r\nBASE: a7654321!", "fedcba98,a7654321", "worktree")]
    [Arguments("base_sha: a7654321", "a7654321", "worktree")]
    [Arguments("baseSha=a7654321", "a7654321", "worktree")]
    [Arguments("Base: a7654321..fedcba98", "", "worktree")]
    [Arguments("Base: sha256:a7654321", "", "worktree")]
    [Arguments("Base: release-1234567", "", "worktree")]
    [Arguments("Base: 87654321", "", "worktree")]
    [Arguments("Base: RIGHT_SHA.", "", "worktree")]
    [Arguments("Base: RIGHT_PREFIX.", "", "worktree")]
    [Arguments("Inspect 87654321.\nBase: main.", "", "worktree")]
    [Arguments("Base: defaced.", "", "worktree")]
    [Arguments("Database: 87654321.", "", "worktree")]
    [Arguments("Base: x87654321, 87654321z.", "", "worktree")]
    [Arguments("Base: 87654321.", "", "reviewOf")]
    [Arguments("Base: 87654321.", "", "in-place")]
    public async Task Plan_warns_for_distinct_wrong_base_tokens_in_prompt_order(string prompt, string tokens, string workspace, CancellationToken ct)
    {
        using var tree = new TempTree();
        var repo = await LabRepo.CreateAsync(tree, ct);
        var sha = await repo.GitAsync(["rev-parse", "main"], ct);
        prompt = prompt.Replace("RIGHT_SHA", sha, StringComparison.Ordinal).Replace("RIGHT_PREFIX", sha[..8], StringComparison.Ordinal);
        var task = new System.Text.Json.Nodes.JsonObject { ["id"] = "inspect", ["kind"] = "research", ["route"] = "fixture/model", ["prompt"] = prompt };
        var tasks = new System.Text.Json.Nodes.JsonArray();
        if (workspace == "reviewOf")
        {
            tasks.Add(System.Text.Json.Nodes.JsonNode.Parse("""{"id":"author","kind":"code","base":"main","scope":["a.txt"],"route":"fixture/model","prompt":"Edit."}"""));
            task["kind"] = "review";
            task["reviewOf"] = "author";
        }
        else if (workspace == "in-place") task["workspace"] = "in-place";
        else task["base"] = "main";
        tasks.Add(task);
        var path = Path.Combine(tree.Root, "tasks.jsonc");
        File.WriteAllText(path, new System.Text.Json.Nodes.JsonObject { ["schemaVersion"] = 2,
            ["defaults"] = new System.Text.Json.Nodes.JsonObject { ["backend"] = "opencode" }, ["tasks"] = tasks }.ToJsonString());
        var git = new Git(Executables.Resolve("git"), Path.Combine(tree.Root, "control"), tree.ChildEnvironment().ToDictionary(x => x.Key, x => x.Value!));
        var plan = await Planner.LoadAsync(path, repo.Root, AllowList.From(null), ct, git);
        await Assert.That(plan.Errors).IsEmpty();
        await Assert.That(plan.Tasks.Select(x => x.Spec.Id!)).IsEquivalentTo(workspace == "reviewOf" ? new[] { "author", "inspect" } : new[] { "inspect" }, CollectionOrdering.Matching);
        await Assert.That(plan.Warnings).IsEquivalentTo(tokens.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(token =>
            $"inspect: the prompt names base commit {token}, but base main resolves to {sha[..12]}"), CollectionOrdering.Matching);
    }

    [Test]
    [NotInParallel]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Plan_warns_only_for_default_clean_own_prompt(bool fromFile, CancellationToken ct)
    {
        using var lab = new CoreLab("migration");
        var repo = await LabRepo.CreateAsync(lab.Tree, ct);
        var path = Path.Combine(lab.Tree.Root, "tasks.jsonc");
        var prompts = new[] { "Use a SkIlL.", "Load SKILLS!", "Invoke `a-1:B2-c`.", "skilled skillset skills2", "Use a:b, `a_b:c`, `a:b:c`, `a:`, or `:b`.", "Inspect." };
        var ids = new[] { "z-word", "a-plural", "m-token", "substring", "invalid-token", "plain" };
        var tasks = new System.Text.Json.Nodes.JsonArray();
        for (var i = 0; i < prompts.Length; i++)
        {
            var task = System.Text.Json.Nodes.JsonNode.Parse($$"""{"id":"{{ids[i]}}","kind":"code","workspace":"in-place","access":"full","route":"fixture/model","scope":["{{i}}.txt"]}""")!;
            if (fromFile)
            {
                File.WriteAllText(Path.Combine(lab.Tree.Root, $"prompt-{i}.md"), prompts[i]);
                task["promptFile"] = $"prompt-{i}.md";
            }
            else task["prompt"] = prompts[i];
            tasks.Add(task);
        }
        // Only one writer can be in-place; research keeps the own-prompt cases free of writer overlap.
        foreach (var task in tasks) task!["kind"] = "research";
        var document = new System.Text.Json.Nodes.JsonObject { ["schemaVersion"] = 2,
            ["defaults"] = new System.Text.Json.Nodes.JsonObject { ["backend"] = "opencode" }, ["tasks"] = tasks,
            ["contracts"] = new System.Text.Json.Nodes.JsonArray("contract.md"),
            ["policy"] = new System.Text.Json.Nodes.JsonObject { ["instructions"] = new System.Text.Json.Nodes.JsonArray("Load skills and `standing:tool`.") } };
        File.WriteAllText(Path.Combine(lab.Tree.Root, "contract.md"), "Contract v1\nUse a skill and `contract:tool`.");
        File.WriteAllText(path, document.ToJsonString());
        ConfigLayers.Initialize(Args.Parse(["plan"]));
        var plan = await Planner.LoadAsync(path, repo.Root, AllowList.From(null), ct, lab.Git, Args.Parse(["plan", path]));
        await Assert.That(plan.Errors).IsEmpty();
        await Assert.That(plan.Tasks.Select(x => x.Spec.Id!)).IsEquivalentTo(new[] { "z-word", "a-plural", "m-token", "substring", "invalid-token", "plain" }, CollectionOrdering.Matching);
        await Assert.That(plan.Warnings).IsEquivalentTo(new[] { "z-word", "a-plural", "m-token" }.Select(id =>
            $"{id}: the prompt mentions a skill, and the task defaults to profile clean, which loads no plugins or skills; set profile full if the worker needs them"), CollectionOrdering.Matching);
        foreach (var layer in new[] { "user", "repo", "task-file-defaults", "task", "cli" })
            foreach (var profile in new[] { "clean", "full" })
            {
                var copy = document.DeepClone();
                var config = $$$"""{"schemaVersion":2,"preferences":{"profile":"{{{profile}}}"}}""";
                Directory.CreateDirectory(Path.GetDirectoryName(Locations.UserConfigFile)!);
                File.WriteAllText(Locations.UserConfigFile, layer == "user" ? config : "{\"schemaVersion\":2}");
                Directory.CreateDirectory(Path.Combine(repo.Root, ".dispatch"));
                File.WriteAllText(Path.Combine(repo.Root, ".dispatch", "config.jsonc"), layer == "repo" ? config : "{\"schemaVersion\":2}");
                await repo.GitAsync(["add", ".dispatch/config.jsonc"], ct);
                await repo.GitAsync(["commit", "--allow-empty", "-m", "Profile fixture"], ct);
                if (layer == "task-file-defaults") copy["defaults"]!["profile"] = profile;
                if (layer == "task") foreach (var task in copy["tasks"]!.AsArray()) task!["profile"] = profile;
                File.WriteAllText(path, copy.ToJsonString());
                var args = Args.Parse(layer == "cli" ? ["plan", path, "--profile", profile] : ["plan", path]);
                ConfigLayers.Initialize(args);
                var explicitPlan = await Planner.LoadAsync(path, repo.Root, AllowList.From(null), ct, lab.Git, args);
                await Assert.That(explicitPlan.Errors).IsEmpty();
                await Assert.That(explicitPlan.Warnings).IsEmpty();
                await Assert.That(explicitPlan.Config!.Effective.Tasks["z-word"].Settings.Profile!.From).IsEqualTo(layer);
            }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Shared_review_lineage_is_info_only(bool backendRefusal, CancellationToken ct)
    {
        var plan = new Plan { File = new(), TasksPath = "tasks.jsonc", Repo = "repository" };
        foreach (var (id, reviewOf) in new (string, string?)[] { ("author", null), ("z-review", "author"), ("a-review", "author"), ("different", "author") })
            plan.Tasks.Add(new() { Spec = new() { Id = id, Kind = reviewOf is null ? "code" : "review", Backend = "opencode", Access = "full", ReviewOf = reviewOf }, PromptText = "Inspect." });
        await using var backend = new FakeBackend("opencode", new("policy", "policy", "text", true))
        {
            OnValidate = (_, _) => Task.FromResult<IReadOnlyList<LiveCheck>>([
                new("author", backendRefusal ? ["unavailable"] : [], backendRefusal ? ["caution"] : [], "shared", "clean", null),
                new("a-review", [], [], "shared", "clean", null), new("z-review", [], [], "shared", "clean", null),
                new("different", [], [], "other", "clean", null)]),
        };
        await Planner.CheckLiveAsync(plan, backend, AllowList.From(null), ct);
        var info = new[] { "z-review: reviewer and author share lineage shared", "a-review: reviewer and author share lineage shared" };
        await Assert.That(plan.Info).IsEquivalentTo(info, CollectionOrdering.Matching);
        await Assert.That(plan.Errors).IsEquivalentTo(backendRefusal ? new[] { "author: unavailable" } : [], CollectionOrdering.Matching);
        await Assert.That(plan.Warnings).IsEquivalentTo(backendRefusal ? new[] { "author: caution" } : [], CollectionOrdering.Matching);
        var output = TestContext.Current!.Output;
        var start = output.GetStandardOutput().Length;
        await Assert.That(plan.Report()).IsEqualTo(backendRefusal ? 2 : 0);
        await Assert.That(output.GetStandardOutput()[start..].ReplaceLineEndings("\n")).IsEqualTo(string.Join('\n', info.Select(x => "INFO " + x)) + "\n"
            + (backendRefusal ? "WARN author: caution\nERROR author: unavailable\nplan refused: 1 error(s)\n" : "plan ok: 4 task(s) against repository\n"));
    }

    [Test]
    [Timeout(180_000)]
    public async Task Plan_reports_and_validates_prepare(CancellationToken ct)
    {
        using var tree = new TempTree();
        var repo = await LabRepo.CreateAsync(tree, ct);
        var path = Path.Combine(tree.Root, "tasks.jsonc");
        const string document = """{"schemaVersion":2,"defaults":{"backend":"opencode"},"tasks":[{"id":"zulu","kind":"code","base":"main","scope":["z.txt"],"route":"fixture/model","prompt":"Edit.","prepare":["exit 0","exit 0"]},{"id":"alpha","kind":"research","base":"main","route":"fixture/model","prompt":"Inspect.","prepare":["exit 0"]},{"id":"empty","kind":"research","base":"main","route":"fixture/model","prompt":"Inspect.","prepare":[]},{"id":"unset","kind":"research","base":"main","route":"fixture/model","prompt":"Inspect."}]}""";
        File.WriteAllText(path, document);
        var environment = FakeOpenCode.Environment(tree, "one-task.jsonl").ToDictionary(x => x.Key, x => (string?)x.Value);
        using var process = new HarnessProcess(tree);
        var output = await process.RunAsync(["plan", path, "--repo", repo.Root], ct, environment);
        output.RequireSuccess("prepare plan");
        var shell = Verifier.ResolveShell(new TaskSpec());
        await Assert.That(Lines(output).Where(x => x.Contains(": prepare ", StringComparison.Ordinal))).IsEquivalentTo(new[] {
            $"INFO zulu: prepare 2 command(s) in {shell}", $"INFO alpha: prepare 1 command(s) in {shell}" }, CollectionOrdering.Matching);
        await Assert.That(() => Json.ReadTaskFile("""{"schemaVersion":2,"defaults":{"prepare":[]},"tasks":[{"kind":"research"}]}""")).Throws<System.Text.Json.JsonException>();
        var structured = System.Text.Json.Nodes.JsonNode.Parse(document)!;
        structured["tasks"] = new System.Text.Json.Nodes.JsonArray(System.Text.Json.Nodes.JsonNode.Parse("""{"id":"inspect","kind":"structured","route":"fixture/model","prompt":"Transform.","prepare":["exit 0"],"input":{"items":[]},"result":{"schema":"schema.json","itemsAt":""}}"""));
        File.WriteAllText(Path.Combine(tree.Root, "schema.json"), """{"type":"array"}""");
        File.WriteAllText(path, structured.ToJsonString());
        var git = new Git(Executables.Resolve("git"), Path.Combine(tree.Root, "control"), tree.ChildEnvironment().ToDictionary(x => x.Key, x => x.Value!));
        var rejected = await Planner.LoadAsync(path, repo.Root, AllowList.From(null), ct, git, executableSearchPath: tree.Root);
        await Assert.That(rejected.Errors).IsEquivalentTo(new[] { "inspect: prepare is forbidden for structured tasks" }, CollectionOrdering.Matching);
        await Assert.That(rejected.Info.Where(x => x.Contains(": prepare ", StringComparison.Ordinal))).IsEmpty();
        structured["tasks"]![0]!["prepare"] = new System.Text.Json.Nodes.JsonArray();
        File.WriteAllText(path, structured.ToJsonString());
        var accepted = await Planner.LoadAsync(path, repo.Root, AllowList.From(null), ct, git);
        await Assert.That(accepted.Errors).IsEmpty();
        await Assert.That(accepted.Tasks.Select(x => x.Spec.Id!)).IsEquivalentTo(new[] { "inspect" }, CollectionOrdering.Matching);
        await Assert.That(accepted.Info.Where(x => x.Contains(": prepare ", StringComparison.Ordinal))).IsEmpty();
        var missing = System.Text.Json.Nodes.JsonNode.Parse(document)!;
        missing["tasks"] = new System.Text.Json.Nodes.JsonArray(missing["tasks"]![1]!.DeepClone());
        File.WriteAllText(path, missing.ToJsonString());
        var unavailable = await Planner.LoadAsync(path, repo.Root, AllowList.From(null), ct, git, executableSearchPath: tree.Root);
        var name = OperatingSystem.IsWindows() ? "pwsh" : "bash";
        await Assert.That(unavailable.Errors).IsEquivalentTo(new[] { $"alpha: prepare shell: required executable is not on PATH: {name}" }, CollectionOrdering.Matching);
        missing["tasks"]![0]!["shell"] = name;
        File.WriteAllText(path, missing.ToJsonString());
        var explicitShell = await Planner.LoadAsync(path, repo.Root, AllowList.From(null), ct, git, executableSearchPath: tree.Root);
        await Assert.That(explicitShell.Errors).IsEquivalentTo(new[] { $"alpha: prepare shell: required executable is not on PATH: {name}" }, CollectionOrdering.Matching);
        var explicitValid = await Planner.LoadAsync(path, repo.Root, AllowList.From(null), ct, git);
        await Assert.That(explicitValid.Errors).IsEmpty();
        await Assert.That(explicitValid.Info.Where(x => x.Contains(": prepare ", StringComparison.Ordinal))).IsEquivalentTo(new[] { $"alpha: prepare 1 command(s) in {shell}" }, CollectionOrdering.Matching);
    }

    [Test]
    [Timeout(180_000)]
    public async Task Plan_prints_contract_paths_and_versions_in_prepend_order(CancellationToken ct)
    {
        using var tree = new TempTree();
        var repo = await LabRepo.CreateAsync(tree, ct);
        var tasks = Path.Combine(tree.Root, "tasks.jsonc");
        var zulu = Path.Combine(tree.Root, "zulu.md");
        var alpha = Path.Combine(tree.Root, "alpha.md");
        File.WriteAllText(zulu, "Contract Z v2\nKeep this.");
        File.WriteAllText(alpha, "Contract A v1\nKeep that.");
        File.WriteAllText(tasks, """{"schemaVersion":2,"contracts":["zulu.md"],"defaults":{"backend":"opencode"},"tasks":[{"id":"zulu","kind":"research","base":"main","route":"fixture/model","contracts":["alpha.md","zulu.md"],"prompt":"Inspect."},{"id":"alpha","kind":"research","base":"main","route":"fixture/model","contracts":["alpha.md"],"prompt":"Inspect."}]}""");
        using var process = new HarnessProcess(tree);
        var result = await process.RunAsync(["plan", tasks, "--repo", repo.Root], ct,
            FakeOpenCode.Environment(tree, "one-task.jsonl").ToDictionary(x => x.Key, x => (string?)x.Value));
        result.RequireSuccess("contract plan");
        await Assert.That(Lines(result).Where(line => line.Contains(": contracts ", StringComparison.Ordinal)))
            .IsEquivalentTo(new[] {
                $"INFO zulu: contracts {zulu} (Contract Z v2); {alpha} (Contract A v1)",
                $"INFO alpha: contracts {zulu} (Contract Z v2); {alpha} (Contract A v1)" }, CollectionOrdering.Matching);
    }

    [Test]
    [Arguments("code")]
    [Arguments("mechanical")]
    [Timeout(180_000)]
    public async Task Plan_preserves_unrelated_warnings_without_contract_advice(string kind, CancellationToken ct)
    {
        using var tree = new TempTree();
        var repo = await LabRepo.CreateAsync(tree, ct);
        var tasks = Path.Combine(tree.Root, "tasks.jsonc");
        File.WriteAllText(Path.Combine(tree.Root, "contract.md"), "Arbitrary contract v1\nKeep this.");
        File.WriteAllText(tasks, $$"""{"schemaVersion":2,"defaults":{"backend":"opencode"},"tasks":[{"id":"zulu","kind":"{{kind}}","base":"main","route":"fixture/model","scope":["z.txt"],"prompt":"Edit."},{"id":"alpha","kind":"{{kind}}","base":"main","route":"fixture/model","scope":["a.txt"],"contracts":["contract.md"],"prompt":"Edit."},{"id":"inspect","kind":"research","reviewOf":"zulu","route":"fixture/model","prompt":"Inspect."}]}""");
        var git = new Git(Executables.Resolve("git"), Path.Combine(tree.Root, "control"), tree.ChildEnvironment().ToDictionary(x => x.Key, x => x.Value!));
        var plan = await Planner.LoadAsync(tasks, repo.Root, AllowList.From(null), ct, git);
        await Assert.That(plan.Errors).IsEmpty();
        await Assert.That(plan.Tasks.Select(x => x.Spec.Id!)).IsEquivalentTo(new[] { "zulu", "alpha", "inspect" }, CollectionOrdering.Matching);
        await Assert.That(plan.Warnings).IsEquivalentTo(new[] { "inspect: reviewOf on a research task" }, CollectionOrdering.Matching);
    }

    [Test]
    [Arguments("plan")]
    [Arguments("run")]
    [Timeout(180_000)]
    public async Task Unavailable_opencode_fence_refuses_without_starting_an_unfenced_server(string command, CancellationToken ct)
    {
        if (Ready.FenceKind is null) Skip.Test("opencode runs unfenced on Windows.");
        using var tree = new TempTree();
        var repo = await LabRepo.CreateAsync(tree, ct);
        var tasks = Path.Combine(tree.Root, "tasks.jsonc");
        File.WriteAllText(tasks, """{"schemaVersion":2,"defaults":{"backend":"opencode"},"tasks":[{"id":"inspect","kind":"research","base":"main","route":"fixture/model","prompt":"Inspect."}]}""");
        var environment = FakeOpenCode.Environment(tree, "one-task.jsonl").ToDictionary(x => x.Key, x => (string?)x.Value);
        var shim = Directory.CreateDirectory(Path.Combine(tree.Root, "fence-shim")).FullName;
        var executable = Path.Combine(shim, Ready.FenceKind!);
        File.WriteAllText(executable, "#!/bin/sh\nprintf 'fixture fence refused' >&2\nexit 97\n");
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(executable, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        environment["PATH"] = shim + Path.PathSeparator + environment["PATH"];
        using var process = new HarnessProcess(tree);
        var result = await process.RunAsync([command, tasks, "--repo", repo.Root], ct, environment);
        var error = $"ERROR inspect: opencode fence unavailable ({Ready.FenceKind} exited 97: fixture fence refused)"
            + (Ready.FenceKind == "bwrap" ? "; install bubblewrap, and on Ubuntu 24.04 set kernel.apparmor_restrict_unprivileged_userns=0 (root)" : "");
        await Assert.That(Lines(result).Where(line => line.StartsWith("ERROR ", StringComparison.Ordinal)).ToArray())
            .IsEquivalentTo(new[] { error }, CollectionOrdering.Matching);
        await Assert.That(result.Stderr.Trim()).IsEqualTo("");
        await Assert.That(result.ExitCode).IsEqualTo(2);
        // Planning probes stay unfenced by design (they run no worker code); the refusal must stop any task session.
        var record = FakeOpenCode.Record(tree);
        await Assert.That(File.Exists(record) && File.ReadLines(record).Any(line => line.Contains("\"method\":\"POST\",\"path\":\"/api/session\"", StringComparison.Ordinal))).IsFalse();
    }

    [Test]
    [Arguments("plan", 0)]
    [Arguments("plan", 247)]
    [Arguments("plan", 248)]
    [Arguments("run", 248)]
    [Timeout(180_000)]
    public async Task Worktree_path_depth_warns_only_at_the_Windows_threshold(string command, int depth, CancellationToken ct)
    {
        using var lab = new CodexLab();
        var repo = await LabRepo.CreateAsync(lab.Tree, ct);
        await repo.GitAsync(["config", "core.longpaths", "true"], ct);
        var folder = Path.Combine(lab.Tree.WorktreeRoot, $"{DateTime.UtcNow:yyyy-MM-dd}-path-depth", "inspect");
        var file = depth == 0 ? "README.md" : new string('d', depth - folder.Length - 10) + "/leaf.txt";
        if (depth != 0)
        {
            await repo.GitAsync(["rm", "README.md"], ct);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(repo.Root, file))!);
            File.WriteAllText(Path.Combine(repo.Root, file), "tracked");
            await repo.GitAsync(["add", "--", file], ct);
            await repo.GitAsync(["commit", "-m", "Track path depth fixture"], ct);
        }
        var staged = file + ".staged-only";
        File.WriteAllText(Path.Combine(repo.Root, staged), "not at HEAD");
        await repo.GitAsync(["add", "--", staged], ct);
        File.WriteAllText(Path.Combine(lab.Environment["CODEX_HOME"], "auth.json"), "synthetic-auth");
        File.WriteAllText(lab.Tasks, """{"schemaVersion":2,"run":"path-depth","tasks":[{"id":"inspect","kind":"research","base":"main","project":"off","prompt":"Inspect."}]}""");
        lab.Write("task", CodexLab.Handshake + CodexLab.Thread.Replace("workspaceWrite", "readOnly") + CodexLab.Final);
        using var process = new HarnessProcess(lab.Tree);
        var result = await process.RunAsync([command, lab.Tasks, "--repo", repo.Root], ct,
            lab.Environment.ToDictionary(x => x.Key, x => (string?)x.Value));
        result.RequireSuccess(command);
        var length = folder.Length + 1 + file.Length;
        var expected = $"WARN inspect: worktree paths reach {length} characters ({file.Replace('/', '\\')}); builds can fail past 260 on Windows. Set locations.worktreeRoot to a short folder (for example C:\\wt) in the user config";
        await Assert.That(Lines(result).Where(line => line.Contains(": worktree paths reach ", StringComparison.Ordinal)).ToArray())
            .IsEquivalentTo(OperatingSystem.IsWindows() && length >= 248 ? new[] { expected } : [], CollectionOrdering.Matching);
        await Assert.That(await repo.GitAsync(["diff", "--cached", "--name-only"], ct)).IsEqualTo(staged);
    }

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
    [Timeout(180_000)]
    public async Task Plan_says_a_clean_task_has_no_user_persona(CancellationToken ct)
    {
        using var tree = new TempTree();
        var repo = await LabRepo.CreateAsync(tree, ct);
        var tasks = Path.Combine(tree.Root, "tasks.jsonc");
        // A research task defaults to profile clean, which loads no user agents.
        File.WriteAllText(tasks, """{"schemaVersion":2,"defaults":{"backend":"opencode"},"tasks":[{"id":"inspect","kind":"research","base":"main","route":"fixture/model","persona":"work","prompt":"Inspect."}]}""");
        using var process = new HarnessProcess(tree);
        var result = await process.RunAsync(["plan", tasks, "--repo", repo.Root], ct,
            FakeOpenCode.Environment(tree, "one-task.jsonl").ToDictionary(x => x.Key, x => (string?)x.Value));
        await Assert.That(result.ExitCode).IsEqualTo(2);
        await Assert.That(Lines(result)).Contains("ERROR inspect: persona work not found: profile clean loads no user agents; set profile full or use a built-in persona");
    }

    [Test]
    [Timeout(180_000)]
    public async Task Plan_refuses_a_tempRoot_inside_the_repository(CancellationToken ct)
    {
        using var tree = new TempTree();
        var repo = await LabRepo.CreateAsync(tree, ct);
        var tasks = Path.Combine(tree.Root, "tasks.jsonc");
        var inside = Path.Combine(repo.Root, "scratch-temp").Replace("\\", "\\\\");
        File.WriteAllText(tasks, $$"""{"schemaVersion":2,"defaults":{"backend":"opencode"},"tasks":[{"id":"inspect","kind":"research","base":"main","route":"fixture/model","prompt":"Inspect.","tempRoot":"{{inside}}"}]}""");
        using var process = new HarnessProcess(tree);
        var result = await process.RunAsync(["plan", tasks, "--repo", repo.Root], ct,
            FakeOpenCode.Environment(tree, "one-task.jsonl").ToDictionary(x => x.Key, x => (string?)x.Value));
        await Assert.That(result.ExitCode).IsEqualTo(2);
        await Assert.That(Lines(result)).Contains("ERROR inspect: tempRoot from task must be outside the repository");
    }

    [Test]
    [Timeout(180_000)]
    public async Task Plan_refuses_a_tempRoot_that_reaches_the_repository_through_a_link(CancellationToken ct)
    {
        using var tree = new TempTree();
        var repo = await LabRepo.CreateAsync(tree, ct);
        var link = Path.Combine(tree.Root, "temp-link");
        if (OperatingSystem.IsWindows())
        {
            using var command = new CapturedProcess();
            (await command.RunAsync("cmd", ["/c", "mklink", "/J", link, repo.Root], tree.Root, tree.ChildEnvironment(), ct)).RequireSuccess("junction fixture");
        }
        else Directory.CreateSymbolicLink(link, repo.Root);
        try
        {
            var tasks = Path.Combine(tree.Root, "tasks.jsonc");
            var inside = Path.Combine(link, "scratch-temp").Replace("\\", "\\\\");
            File.WriteAllText(tasks, $$"""{"schemaVersion":2,"defaults":{"backend":"opencode"},"tasks":[{"id":"inspect","kind":"research","base":"main","route":"fixture/model","prompt":"Inspect.","tempRoot":"{{inside}}"}]}""");
            using var process = new HarnessProcess(tree);
            var result = await process.RunAsync(["plan", tasks, "--repo", repo.Root], ct,
                FakeOpenCode.Environment(tree, "one-task.jsonl").ToDictionary(x => x.Key, x => (string?)x.Value));
            await Assert.That(Lines(result)).Contains("ERROR inspect: tempRoot from task must be outside the repository");
        }
        finally
        {
            // The link must go before TempTree deletes the tree, or deletion follows it.
            if (Path.Exists(link)) Directory.Delete(link);
        }
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
        // Codex's catalog lists no context window; plan says when it becomes known.
        await Assert.That(lines.Count(line => line.StartsWith("INFO inspect: context window of codex:", StringComparison.Ordinal)
            && line.EndsWith("is known after the run (result.json contextWindow)", StringComparison.Ordinal))).IsEqualTo(1);
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
        await Assert.That(lines).Contains(Ready.FenceKind is null
            ? "INFO inspect: no shell for a read-only task (readOnlyShell decline, from tool)"
            : "INFO inspect: sandboxed shell; escalations declined (readOnlyShell decline, from tool)");
        var fenceLines = lines.Where(line => line.Contains("fenced (", StringComparison.Ordinal)
            || line.Contains("opencode runs unfenced on Windows;", StringComparison.Ordinal)).ToArray();
        await Assert.That(fenceLines).IsEquivalentTo(Ready.FenceKind is { } kind
            ? new[] { $"INFO inspect: write-fenced ({kind}; accident guard)", $"INFO edit: write-fenced ({kind}; accident guard)" }
            : new[] {
                "WARN inspect: opencode runs unfenced on Windows; access read-only is enforced by permission rules only",
                "WARN edit: opencode runs unfenced on Windows; access workspace is enforced by permission rules only",
            }, CollectionOrdering.Matching);
        await Assert.That(lines.Any(line => line.StartsWith("INFO edit:", StringComparison.Ordinal) && line.Contains("shell", StringComparison.Ordinal))).IsFalse();
        // The fixture catalog's model limit is 8192 tokens.
        foreach (var task in new[] { "inspect", "edit" })
            await Assert.That(lines.Count(line => line.StartsWith($"INFO {task}: context window 8192 tokens (opencode:fixture/model", StringComparison.Ordinal))).IsEqualTo(1);
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

    [Test]
    [Timeout(180_000)]
    public async Task Plan_warns_when_path_depth_cannot_be_measured_and_caches_the_failure(CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows()) Skip.Test("Worktree path depth is Windows-only.");
        using var lab = new CodexLab();
        var repo = await LabRepo.CreateAsync(lab.Tree, ct);
        await repo.GitAsync(["symbolic-ref", "HEAD", "refs/heads/unborn"], ct);
        using var git = new CapturedProcess();
        var failed = await git.RunAsync("git", ["read-tree", "HEAD"], repo.Root, lab.Tree.ChildEnvironment(), ct);
        await Assert.That(failed.ExitCode).IsNotEqualTo(0);
        var reason = $"git read-tree HEAD failed in {repo.Root}: {failed.Stderr.Trim()}";
        File.WriteAllText(Path.Combine(lab.Environment["CODEX_HOME"], "auth.json"), "synthetic-auth");
        File.WriteAllText(lab.Tasks, """{"schemaVersion":2,"tasks":[{"id":"z-inspect","kind":"research","base":"main","project":"off","prompt":"Inspect."},{"id":"a","kind":"research","base":"main","project":"off","prompt":"Inspect."}]}""");
        lab.Environment["GIT_TRACE"] = Path.Combine(lab.Tree.Root, "git.trace");
        using var process = new HarnessProcess(lab.Tree);
        var result = await process.RunAsync(["plan", lab.Tasks, "--repo", repo.Root], ct,
            lab.Environment.ToDictionary(x => x.Key, x => (string?)x.Value));
        result.RequireSuccess("unborn HEAD path depth plan");
        await Assert.That(Lines(result).Where(line => line.Contains(": worktree path depth not measured ")))
            .IsEquivalentTo(new[] {
                $"WARN z-inspect: worktree path depth not measured ({reason})",
                $"WARN a: worktree path depth not measured ({reason})" }, CollectionOrdering.Matching);
        await Assert.That(File.ReadLines(lab.Environment["GIT_TRACE"]).Count(line => line.Contains("built-in: git ") && line.EndsWith(" read-tree HEAD")))
            .IsEqualTo(1);
        await Assert.That(Lines(result).Where(line => line.StartsWith("ERROR "))).IsEquivalentTo(Array.Empty<string>(), CollectionOrdering.Matching);
    }

    [Test]
    [Timeout(180_000)]
    public async Task Plan_measures_path_depth_once_but_reports_each_task_length(CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows()) Skip.Test("Worktree path depth is Windows-only.");
        using var lab = new CodexLab();
        var repo = await LabRepo.CreateAsync(lab.Tree, ct);
        await repo.GitAsync(["config", "core.longpaths", "true"], ct);
        var run = $"{DateTime.UtcNow:yyyy-MM-dd}-cached-depth";
        var folder = Path.Combine(lab.Tree.WorktreeRoot, run, "a");
        var file = new string('d', 248 - folder.Length - 10) + "/leaf.txt";
        Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(repo.Root, file))!);
        File.WriteAllText(Path.Combine(repo.Root, file), "tracked");
        await repo.GitAsync(["add", "--", file], ct);
        await repo.GitAsync(["commit", "-m", "Track cached depth fixture"], ct);
        File.WriteAllText(Path.Combine(lab.Environment["CODEX_HOME"], "auth.json"), "synthetic-auth");
        File.WriteAllText(lab.Tasks, """{"schemaVersion":2,"run":"cached-depth","tasks":[{"id":"z-inspect","kind":"research","base":"main","project":"off","prompt":"Inspect."},{"id":"a","kind":"research","base":"main","project":"off","prompt":"Inspect."}]}""");
        lab.Environment["GIT_TRACE"] = Path.Combine(lab.Tree.Root, "git.trace");
        using var process = new HarnessProcess(lab.Tree);
        var result = await process.RunAsync(["plan", lab.Tasks, "--repo", repo.Root], ct,
            lab.Environment.ToDictionary(x => x.Key, x => (string?)x.Value));
        result.RequireSuccess("cached path depth plan");
        await Assert.That(Lines(result).Where(line => line.Contains(": worktree paths reach "))).IsEquivalentTo(new[] { "z-inspect", "a" }.Select(id =>
            $"WARN {id}: worktree paths reach {Path.Combine(lab.Tree.WorktreeRoot, run, id).Length + 1 + file.Length} characters ({file.Replace('/', '\\')}); builds can fail past 260 on Windows. Set locations.worktreeRoot to a short folder (for example C:\\wt) in the user config"), CollectionOrdering.Matching);
        await Assert.That(File.ReadLines(lab.Environment["GIT_TRACE"]).Count(line => line.Contains("built-in: git ") && line.EndsWith(" read-tree HEAD")))
            .IsEqualTo(1);
        await Assert.That(File.ReadLines(lab.Environment["GIT_TRACE"]).Count(line => line.Contains("built-in: git ") && line.EndsWith(" ls-files -z")))
            .IsEqualTo(1);
    }

    static string[] Lines(CapturedResult result) => result.Stdout.Split('\n').Select(line => line.TrimEnd('\r')).ToArray();
}
