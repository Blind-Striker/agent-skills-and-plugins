namespace Dispatch.Tests.Support;

internal sealed class CoreLab : IDisposable
{
    public TempTree Tree { get; } = new();
    public Ledger Ledger { get; }
    public Policy Policy { get; } = new() { Path = "config.json", Deny = [], Escalate = [], Instructions = [] };
    public Git Git { get; }
    public FakeBackend Backend { get; } = new("opencode", new("policy", "policy", "text", true));
    readonly string controlFolder;

    public CoreLab(string? runId = null, bool recorded = true)
    {
        Locations.Resolve();
        Locations.Configure(new ConfigLocations { WorktreeRoot = Tree.WorktreeRoot }, runId is null ? null : Path.Combine(Tree.Root, "ledgers"));
        Ledger = new(runId ?? Tree.Root);
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
        Backend.OnRelease = (_, _) => Task.CompletedTask;
    }

    public async Task<TaskRun> TaskAsync(string id, CancellationToken ct)
    {
        var repo = Directory.Exists(Path.Combine(Tree.Repository, ".git")) ? new LabRepo(Tree) : await LabRepo.CreateAsync(Tree, ct);
        var sha = await repo.GitAsync(["rev-parse", "HEAD"], ct);
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

    public Scheduler Scheduler(CancellationToken ct, int max = 1, AllowList? allow = null, TimeProvider? time = null) =>
        new(new Dictionary<string, IWorkerBackend> { [Backend.Name] = Backend }, Ledger, Tree.Repository, allow ?? AllowList.From(null), Policy, max, ct, Git, time);

    public void End(TaskRun run, string outcome = "succeeded", string? turn = null) =>
        Backend.Emit(new WorkerSignal.Ended(run.Id, turn ?? run.Worker!.Turn!, outcome, null));

    public void Dispose()
    {
        Directory.Delete(controlFolder, recursive: true);
        Locations.Configure(null, null);
        Tree.Dispose();
    }
}
