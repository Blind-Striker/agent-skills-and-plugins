namespace Dispatch.Tests.Support;

// Deliberately has no successful default responses: each test scripts the operations it expects.
internal sealed class FakeBackend(string name, BackendCapabilities capabilities) : IWorkerBackend
{
    readonly Channel<WorkerSignal> signals = Channel.CreateUnbounded<WorkerSignal>();
    bool disposed;

    public string Name { get; } = name;
    public BackendCapabilities Capabilities { get; } = capabilities;
    public ChannelReader<WorkerSignal> Signals => signals.Reader;
    public ConcurrentQueue<BackendCall> Calls { get; } = new();

    public Func<ProbeRequest, CancellationToken, Task<BackendProbe>>? OnProbe { get; set; }
    public Func<IReadOnlyList<WorkerPlan>, CancellationToken, Task<IReadOnlyList<LiveCheck>>>? OnValidate { get; set; }
    public Func<WorkerLaunch, CancellationToken, Task<CreateResult>>? OnCreate { get; set; }
    public Func<(WorkerRef Worker, WorkerPrompt Prompt), CancellationToken, Task<WorkerRef>>? OnPrompt { get; set; }
    public Func<(BlockerRef Blocker, BlockerAnswer Answer), CancellationToken, Task<AnswerResult>>? OnAnswer { get; set; }
    public Func<(WorkerRef Worker, InterruptKind Kind), CancellationToken, Task<InterruptResult>>? OnInterrupt { get; set; }
    public Func<IReadOnlyList<WorkerRef>, CancellationToken, Task<PollResult>>? OnPoll { get; set; }
    public Func<WorkerRef, CancellationToken, Task<WorkerReport>>? OnCollect { get; set; }
    public Func<WorkerRef, CancellationToken, Task<ReattachResult>>? OnReattach { get; set; }
    public Func<WorkerRef, CancellationToken, Task>? OnRelease { get; set; }
    public Func<OrphanQuery, CancellationToken, Task<IReadOnlyList<WorkerRef>>>? OnFindOrphans { get; set; }
    public Func<string, CancellationToken, Task<RouteListing>>? OnListRoutes { get; set; }
    public Func<ValueTask>? OnDispose { get; set; }

    public void Emit(WorkerSignal signal)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!signals.Writer.TryWrite(signal)) throw new InvalidOperationException("signal channel is closed");
    }

    public void CompleteSignals(Exception? error = null) => signals.Writer.TryComplete(error);

    public Task<BackendProbe> ProbeAsync(ProbeRequest request, CancellationToken ct) =>
        Call(nameof(ProbeAsync), request, OnProbe, ct);

    public Task<IReadOnlyList<LiveCheck>> ValidateAsync(IReadOnlyList<WorkerPlan> tasks, CancellationToken ct) =>
        Call(nameof(ValidateAsync), tasks, OnValidate, ct);

    public Task<CreateResult> CreateAsync(WorkerLaunch launch, CancellationToken ct) =>
        Call(nameof(CreateAsync), launch, OnCreate, ct);

    public Task<WorkerRef> PromptAsync(WorkerRef worker, WorkerPrompt prompt, CancellationToken ct) =>
        Call(nameof(PromptAsync), (worker, prompt), OnPrompt, ct);

    public Task<AnswerResult> AnswerAsync(BlockerRef blocker, BlockerAnswer answer, CancellationToken ct) =>
        Call(nameof(AnswerAsync), (blocker, answer), OnAnswer, ct);

    public Task<InterruptResult> InterruptAsync(WorkerRef worker, InterruptKind kind, CancellationToken ct) =>
        Call(nameof(InterruptAsync), (worker, kind), OnInterrupt, ct);

    public Task<PollResult> PollAsync(IReadOnlyList<WorkerRef> live, CancellationToken ct) =>
        Call(nameof(PollAsync), live, OnPoll, ct);

    public Task<WorkerReport> CollectAsync(WorkerRef worker, CancellationToken ct) =>
        Call(nameof(CollectAsync), worker, OnCollect, ct);

    public Task<ReattachResult> ReattachAsync(WorkerRef worker, CancellationToken ct) =>
        Call(nameof(ReattachAsync), worker, OnReattach, ct);

    public Task ReleaseAsync(WorkerRef worker, CancellationToken ct)
    {
        Record(nameof(ReleaseAsync), worker, ct);
        return (OnRelease ?? throw Unscripted(nameof(ReleaseAsync)))(worker, ct);
    }

    public Task<IReadOnlyList<WorkerRef>> FindOrphansAsync(OrphanQuery query, CancellationToken ct) =>
        Call(nameof(FindOrphansAsync), query, OnFindOrphans, ct);

    public Task<RouteListing> ListRoutesAsync(string directory, CancellationToken ct) =>
        Call(nameof(ListRoutesAsync), directory, OnListRoutes, ct);

    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        Calls.Enqueue(new(nameof(DisposeAsync), null));
        disposed = true;
        try
        {
            if (OnDispose is { } script) await script();
        }
        finally
        {
            signals.Writer.TryComplete();
        }
    }

    Task<TResult> Call<TInput, TResult>(string member, TInput input, Func<TInput, CancellationToken, Task<TResult>>? script, CancellationToken ct)
    {
        Record(member, input, ct);
        return (script ?? throw Unscripted(member))(input, ct);
    }

    void Record(string member, object? input, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ct.ThrowIfCancellationRequested();
        Calls.Enqueue(new(member, input));
    }

    static InvalidOperationException Unscripted(string member) => new($"no backend script for {member}");
}

internal sealed record BackendCall(string Member, object? Input);
