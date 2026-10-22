// Backends return values and signals, never write ledger or control files, and never decide policy.
interface IWorkerBackend : IAsyncDisposable
{
    string Name { get; }
    BackendCapabilities Capabilities { get; }

    // Preflight may start a host, but sends no worker prompt.
    Task<BackendProbe> ProbeAsync(ProbeRequest request, CancellationToken ct);
    Task<IReadOnlyList<LiveCheck>> ValidateAsync(IReadOnlyList<WorkerPlan> tasks, CancellationToken ct);

    // Create applies rules and environment without prompting. The core saves the reference first.
    Task<CreateResult> CreateAsync(WorkerLaunch launch, CancellationToken ct);
    Task<WorkerRef> PromptAsync(WorkerRef worker, WorkerPrompt prompt, CancellationToken ct);

    ChannelReader<WorkerSignal> Signals { get; }
    Task<AnswerResult> AnswerAsync(BlockerRef blocker, BlockerAnswer answer, CancellationToken ct);
    Task<InterruptResult> InterruptAsync(WorkerRef worker, InterruptKind kind, CancellationToken ct);

    // Poll repairs stream gaps, re-emitting blockers and confirmed endings, and reports host health.
    Task<PollResult> PollAsync(IReadOnlyList<WorkerRef> live, CancellationToken ct);
    Task<WorkerReport> CollectAsync(WorkerRef worker, CancellationToken ct);
    Task<ReattachResult> ReattachAsync(WorkerRef worker, CancellationToken ct);
    Task ReleaseAsync(WorkerRef worker, CancellationToken ct);
    Task<IReadOnlyList<WorkerRef>> FindOrphansAsync(OrphanQuery query, CancellationToken ct);
    Task<RouteListing> ListRoutesAsync(string directory, CancellationToken ct);
}
