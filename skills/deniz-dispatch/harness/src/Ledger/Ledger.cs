sealed class Ledger(string runId)
{
    readonly Lock _gate = new();

    public string RunId { get; } = runId;

    public string Dir { get; } = Path.Combine(Locations.LedgerRoot, runId);

    string EventsFile => Path.Combine(Dir, "events.jsonl");

    public void EnsureExists()
    {
        if (!File.Exists(EventsFile)) throw new HarnessException($"no ledger for run {RunId} under {Locations.LedgerRoot}");
    }

    public string TaskDir(string task)
    {
        var dir = Path.Combine(Dir, task);
        Directory.CreateDirectory(dir);
        return dir;
    }

    public void Write(string? task, string type, JsonObject? data = null)
    {
        var line = new LedgerLine { Ts = Json.Timestamp(), Run = RunId, Task = task, Type = type, Data = data };
        var json = JsonSerializer.Serialize(line, LedgerJson.Events.LedgerLine);
        lock (_gate)
        {
            Directory.CreateDirectory(Dir);
            File.AppendAllText(EventsFile, json + "\n");
        }

        Console.WriteLine($"{DateTime.Now:HH:mm:ss} {task ?? "-",-20} {type,-11} {data?.ToJsonString() ?? ""}");
    }

    public List<LedgerLine> Read()
    {
        if (!File.Exists(EventsFile)) return [];
        string[] lines;
        lock (_gate)
        {
            lines = File.ReadAllLines(EventsFile);
        }

        return lines.Where(x => x.Length > 0).Select(x => JsonSerializer.Deserialize(x, LedgerJson.Events.LedgerLine)!).ToList();
    }

    // Per task, in queue order: done, failed, cancelled (from the final event), or the last
    // progress state.
    public Dictionary<string, string> TaskStates()
    {
        var states = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in Read().Where(x => x.Task is not null))
        {
            states[line.Task!] = line.Type switch
            {
                "final" => line.Data?["state"]?.GetValue<string>() ?? "final",
                "queued" => "queued",
                "worktree" or "route" or "session" or "environment" => "dispatching",
                "prompt" or "permission" or "form" or "retry" or "status" or "timeout" or "cancel" or "escalation-dropped" => "running",
                "escalated" => "waiting",
                "collected" or "verified" or "outcome" => "settling",
                _ => states.GetValueOrDefault(line.Task!, line.Type),
            };
        }

        return states;
    }

    public void SaveRecord(TaskRecord record) =>
        Json.WriteFile(Path.Combine(TaskDir(record.Spec.Id!), "task.json"), record, LedgerJson.Files.TaskRecord);

    public TaskRecord LoadRecord(string task)
    {
        var path = Path.Combine(Dir, task, "task.json");
        return File.Exists(path)
            ? JsonSerializer.Deserialize(File.ReadAllText(path), LedgerJson.Files.TaskRecord)!
            : throw new HarnessException($"no task record {path}");
    }

    public TaskResult? LoadResult(string task)
    {
        var path = Path.Combine(Dir, task, "result.json");
        return File.Exists(path) ? JsonSerializer.Deserialize(File.ReadAllText(path), LedgerJson.Files.TaskResult) : null;
    }

    public RunInfo LoadRunInfo()
    {
        var path = Path.Combine(Dir, "run.json");
        return File.Exists(path)
            ? JsonSerializer.Deserialize(File.ReadAllText(path), LedgerJson.Files.RunInfo)!
            : throw new HarnessException($"no run.json in {Dir}");
    }

    // The newest run whose ledger knows the task, unless the run is named.
    public static Ledger FindRunFor(string task, string? run)
    {
        if (run is not null)
        {
            var named = new Ledger(run);
            named.EnsureExists();
            return named;
        }

        var found = Directory.Exists(Locations.LedgerRoot)
            ? Directory.GetDirectories(Locations.LedgerRoot)
                .Where(d => File.Exists(Path.Combine(d, task, "task.json")))
                .OrderByDescending(d => Directory.GetCreationTimeUtc(d))
                .FirstOrDefault()
            : null;
        return found is null ? throw new HarnessException($"no run knows task {task}") : new Ledger(Path.GetFileName(found));
    }
}
