// Command-level coordinator; scheduling, starting, escalation and settlement have separate owners.
sealed class Supervisor(IWorkerBackend backend, Ledger ledger, string repo, AllowList allow, Policy policy, int max, CancellationToken ct)
{
    readonly Scheduler scheduler = new(new Dictionary<string, IWorkerBackend> { [backend.Name] = backend }, ledger, repo, allow, policy, max, ct);
    public async Task<int> RunAsync(List<TaskRun> runs) => Report(await scheduler.RunAsync(runs));
    public async Task<int> ResumeAsync(List<TaskRun> runs, bool proceed) => Report(await scheduler.ResumeAsync(runs, proceed));

    int Report(int exit)
    {
        Console.WriteLine();
        Commands.Status(Args.Parse(["status", ledger.RunId]));
        return exit;
    }
}
