using System.Text.Json.Nodes;

namespace Dispatch.Tests.Support;

internal sealed class CoreLab : IDisposable
{
    public TempTree Tree { get; } = new();
    public Ledger Ledger { get; }
    public Policy Policy { get; } = new() { Path = "config.json", Deny = [], Escalate = [], Instructions = [] };
    public Git Git { get; }
    public FakeBackend Backend { get; } = new("opencode", new("policy", "policy", "text", true));
    readonly string controlFolder;
    readonly string? savedDispatchHome = Environment.GetEnvironmentVariable("DISPATCH_HOME");

    public CoreLab(string? runId = null, bool recorded = true)
    {
        Environment.SetEnvironmentVariable("DISPATCH_HOME", Tree.DispatchHome);
        Locations.Resolve();
        Locations.Configure(new ConfigLocations { WorktreeRoot = Tree.WorktreeRoot }, runId is null ? null : Path.Combine(Tree.Root, "ledgers"));
        Ledger = new(runId ?? Path.Combine(Tree.Root, "ledger"));
        var controlId = Locations.NewControlId();
        controlFolder = Locations.CreateControlFolder(controlId);
        if (recorded)
            Json.WriteFile(Path.Combine(Ledger.Dir, "run.json"), new RunInfo { Run = Ledger.RunId, ControlId = controlId }, LedgerJson.Files.RunInfo);
        Git = new(Executables.Resolve("git"), controlFolder,
            Tree.ChildEnvironment().Where(x => x.Value is not null).ToDictionary(x => x.Key, x => x.Value!));
        Backend.OnCreate = (launch, _) => Task.FromResult(new CreateResult(new("opencode", launch.TaskId, "worker-" + launch.TaskId, null, new()), new("none", "none", "policy")));
        Backend.OnPrompt = (input, _) => Task.FromResult(input.Worker with { Turn = "turn-" + input.Worker.TaskId });
        Backend.OnPoll = (_, _) => Task.FromResult(new PollResult([]));
        Backend.OnCollect = (_, _) => Task.FromResult(new WorkerReport("succeeded", "Report", new(), null, true, [], null, true));
        Backend.OnAnswer = (_, _) => Task.FromResult(new AnswerResult(true, null));
        Backend.OnInterrupt = (_, _) => Task.FromResult(new InterruptResult(false, "interrupt"));
        Backend.OnRelease = (_, _) => Task.FromResult<InterruptResult?>(null);
    }

    public async Task<TaskRun> TaskAsync(string id, CancellationToken ct)
    {
        var repo = Directory.Exists(Path.Combine(Tree.Repository, ".git")) ? new LabRepo(Tree) : await LabRepo.CreateAsync(Tree, ct);
        var sha = await repo.GitAsync(["rev-parse", "HEAD"], ct);
        await Git.InitializeBaselineAsync(repo.Root, ct);
        var run = new TaskRun
        {
            Record = new TaskRecord
            {
                Spec = new() { Id = id, Kind = "research", Route = "fixture/model", RawPrompt = true },
                PromptText = "Inspect.",
                Settings = new() { Backend = new() { Value = "opencode" }, Route = new() { Value = "opencode:fixture/model" } },
                Workspace = new() { Path = repo.Root, BaseSha = sha },
            }
        };
        Ledger.SaveRecord(run.Record);
        return run;
    }

    public Scheduler Scheduler(CancellationToken ct, int max = 1, AllowList? allow = null, TimeProvider? time = null,
        Func<TaskRun, string, IReadOnlyDictionary<string, string>, CancellationToken, Task<(VerifyResult Result, string Log, Exception? Error)>>? prepareCommand = null,
        Func<Git, string, string, bool, CancellationToken, Task<string>>? prepareSnapshot = null,
        Func<Git, string, string, bool, CancellationToken, Task<string>>? prepareHead = null) =>
        new(new Dictionary<string, IWorkerBackend> { [Backend.Name] = Backend }, Ledger, Tree.Repository, allow ?? AllowList.From(null), Policy, max, ct, Git, time, prepareCommand, prepareSnapshot, prepareHead);

    public void Structured(TaskRun run)
    {
        run.Record.Spec.Kind = "structured";
        run.Record.Spec.Project = "on";
        run.Record.Spec.RawPrompt = false;
        run.Record.Spec.Input = new() { Items = JsonNode.Parse(StructuredLab.Valid)!["items"]!.AsArray() };
        run.Record.Spec.Result = new() { Schema = "schema.json", ItemsAt = "/items", Immutable = ["key"] };
        run.Record.PromptInputs = new([], run.Record.Spec.Input.Items, JsonNode.Parse(StructuredLab.Schema));
        Json.WriteFile(Path.Combine(Ledger.Dir, "tasks.jsonc"), new TaskFile { Tasks = [run.Record.Spec] }, InputJson.Read.TaskFile);
        Json.WriteFile(Path.Combine(Ledger.Dir, "config.json"), new EffectiveConfig
        {
            Tasks = new() { [run.Id] = new() { Settings = run.Record.Settings } },
        }, LedgerJson.Files.EffectiveConfig);
        StructuredOutput.Freeze(Ledger, [run]);
        Ledger.SaveRecord(run.Record);
    }

    public void End(TaskRun run, string outcome = "succeeded", string? turn = null) =>
        Backend.Emit(new WorkerSignal.Ended(run.Id, turn ?? run.Worker!.Turn!, outcome, null));

    public async Task<(Ledger Ledger, TaskRun Run, TaskResult Result, Git Git)> SourceAttemptAsync(CancellationToken ct,
        string status = "stopped", Func<string, Task>? edit = null, bool ownCommit = false, string? failure = null)
    {
        if (!Directory.Exists(Path.Combine(Tree.Repository, ".git"))) await LabRepo.CreateAsync(Tree, ct);
        var source = new Ledger("2026-10-31-source");
        var sourceControl = Locations.NewControlId();
        var sourceGit = new Git(Executables.Resolve("git"), Locations.CreateControlFolder(sourceControl),
            Tree.ChildEnvironment().ToDictionary(x => x.Key, x => x.Value!));
        Json.WriteFile(Path.Combine(source.Dir, "run.json"), new RunInfo { Run = source.RunId, Repo = Tree.Repository, ControlId = sourceControl }, LedgerJson.Files.RunInfo);
        source.Write(null, "run");
        var run = Continuation("source");
        run.Record.Spec.ContinueOf = null;
        run.Record.Spec.Base = "main";
        run.Record.Session.Id = "old-worker";
        source.Write(run.Id, "queued");
        await Worktree.PrepareAsync(run, source, Tree.Repository, ct, sourceGit);
        var path = run.Record.Workspace.Path!;
        if (edit is null) await File.WriteAllTextAsync(Path.Combine(path, "source.txt"), "source work\n", ct);
        else await edit(path);
        if (ownCommit)
        {
            await sourceGit.CheckAsync(path, ct, "add", "-A");
            await sourceGit.CheckAsync(path, ct, "commit", "-m", "Worker commit");
            await File.WriteAllTextAsync(Path.Combine(path, "uncommitted.txt"), "uncommitted work\n", ct);
        }
        if (failure == "verify-failed") run.Record.Spec.Verify = ["exit 7"];
        source.SaveRecord(run.Record);
        await using var backend = new FakeBackend("opencode", new("policy", "policy", "text", true))
        {
            OnCollect = (_, _) => Task.FromResult(new WorkerReport(failure == "outcome" ? "failed" : "succeeded",
                $"Earlier report.\r\nStatus: {status}", new(), null, true, [], null, true)),
        };
        var result = await Settlement.SettleAsync(backend, source, run, null, ct, sourceGit);
        source.Write(run.Id, "final", new() { ["state"] = result.State });
        return (source, run, result, sourceGit);
    }

    public TaskRun Continuation(string id = "next", string reference = "2026-10-31-source/source", string amendment = "Amend the earlier work.") => new()
    {
        Record = new()
        {
            Spec = new() { Id = id, Kind = "code", Route = "fixture/model", ContinueOf = reference, Scope = ["**"], Prompt = amendment },
            PromptText = amendment,
            Settings = new() { Backend = new() { Value = "opencode" }, Route = new() { Value = "opencode:fixture/model" } },
        },
    };

    public Task<Plan> PlanAsync(CancellationToken ct, params TaskSpec[] specs)
    {
        var path = Path.Combine(Tree.Root, "continuation.tasks.jsonc");
        Json.WriteFile(path, new TaskFile { Run = Ledger.RunId, Tasks = [.. specs] }, InputJson.Read.TaskFile);
        return Planner.LoadAsync(path, Tree.Repository, AllowList.From(null), ct, Git);
    }

    public void Dispose()
    {
        try { Directory.Delete(controlFolder, recursive: true); }
        finally
        {
            Environment.SetEnvironmentVariable("DISPATCH_HOME", savedDispatchHome);
            Locations.Resolve();
            Locations.Configure(null, null);
            Tree.Dispose();
        }
    }
}
