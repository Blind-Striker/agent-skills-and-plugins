// Command-level coordinator; scheduling, starting, escalation and settlement have separate owners.
sealed class Supervisor(IReadOnlyDictionary<string, IWorkerBackend> backends, Ledger ledger, string repo, AllowList allow, Policy policy, int max, CancellationToken ct)
{
    public Supervisor(IWorkerBackend backend, Ledger ledger, string repo, AllowList allow, Policy policy, int max, CancellationToken ct)
        : this(new Dictionary<string, IWorkerBackend> { [backend.Name] = backend }, ledger, repo, allow, policy, max, ct) { }
    readonly Scheduler scheduler = new(backends, ledger, repo, allow, policy, max, ct);
    public async Task<int> RunAsync(List<TaskRun> runs)
    {
        StructuredOutput.Freeze(ledger, runs);
        return Report(await scheduler.RunAsync(runs));
    }
    public async Task<int> ResumeAsync(List<TaskRun> runs, bool proceed) => Report(await scheduler.ResumeAsync(runs, proceed));

    int Report(int exit)
    {
        Console.WriteLine();
        Commands.Status(Args.Parse(["status", ledger.RunId]));
        return exit;
    }
}
