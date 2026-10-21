enum TaskState { Pending, Dispatching, Running, Settling, Interrupted, Done, Failed, Cancelled }

sealed class TaskRun
{
    public required TaskRecord Record { get; init; }

    public string Id => Record.Spec.Id!;

    public TaskState State { get; set; } = TaskState.Pending;

    public HashSet<string> Answered { get; } = new(StringComparer.Ordinal);

    public int Asks { get; set; }

    public int Escalated { get; set; }

    public int Forms { get; set; }

    public int Retries { get; set; }

    public int AbsentChecks { get; set; }

    public DateTimeOffset? Deadline { get; set; }

    public DateTimeOffset? NextCheck { get; set; }

    public bool TimedOut { get; set; }

    public bool CancelRequested { get; set; }

    public bool CancelSent { get; set; }

    public DateTimeOffset? StopStarted { get; set; }
    public DateTimeOffset? NextInterrupt { get; set; }
    public InterruptKind? StopKind { get; set; }
    public bool HardStopSent { get; set; }
    public Task? InterruptWork { get; set; }
    public CancellationTokenSource? InterruptCancellation { get; set; }
    public bool? WorkerStopped { get; set; }
    public bool Zombie { get; set; }
    public bool PromptAttempted { get; set; }
    public string? StartFailure { get; set; }

    public Task? Work { get; set; }

    public WorkerRef? Worker { get; set; }
    public bool WorkerEnded { get; set; }
    // Stop supervising a failed start without claiming it ended; backend disposal and resume own recovery.
    public bool ReleaseUnconfirmed { get; set; }

    public WorkerRef SavedWorker() => Worker ??= new(
        Record.Settings.Backend?.Value ?? "opencode", Id, Record.Session.Id!,
        Record.Session.Turn ?? Record.BackendState.PromptId,
        JsonSerializer.SerializeToNode(Record.BackendState, LedgerJson.Files.BackendState)!.AsObject());

    public void SaveWorker(WorkerRef worker, Ledger ledger)
    {
        Worker = worker;
        Record.Session.Id = worker.Id;
        Record.Session.Turn = worker.Turn;
        Record.BackendState = worker.State.Deserialize(LedgerJson.Files.BackendState)!;
        ledger.SaveRecord(Record);
    }

    public bool IsFinal => State is TaskState.Done or TaskState.Failed or TaskState.Cancelled;

    public static TaskRun FromLedger(TaskRecord record, Ledger ledger)
    {
        var lines = ledger.Read().Where(x => x.Task == record.Spec.Id).ToList();
        var run = new TaskRun
        {
            Record = record,
            Escalated = lines.Count(x => x.Type == "escalated"),
            Forms = lines.Count(x => x.Type == "form"),
            Retries = lines.Count(x => x.Type == "retry"),
            TimedOut = lines.Any(x => x.Type == "timeout"),
            CancelRequested = lines.Any(x => x.Type == "cancel"),
        };

        // An escalated ask is logged twice (escalated, then the operator's answer); count it once.
        foreach (var line in lines.Where(x => x.Type is "permission" or "escalated"))
        {
            if (line.Data?["id"]?.GetValue<string>() is { } id) run.Answered.Add(id);
        }

        run.Asks = run.Answered.Count;
        if (lines.FirstOrDefault(x => x.Type is "timeout" or "cancel") is { } stop)
        {
            run.StopKind = stop.Type == "timeout" ? InterruptKind.Timeout : InterruptKind.Cancel;
            run.StopStarted = DateTimeOffset.Parse(stop.Ts, CultureInfo.InvariantCulture);
            run.CancelSent = run.StopKind == InterruptKind.Cancel;
            if (lines.FirstOrDefault(x => x.Type == "hard-stop") is { } hard)
            {
                run.HardStopSent = true;
                run.WorkerStopped = hard.Data?["stopped"]?.GetValue<bool>() ?? false;
            }
        }
        return run;
    }
}
