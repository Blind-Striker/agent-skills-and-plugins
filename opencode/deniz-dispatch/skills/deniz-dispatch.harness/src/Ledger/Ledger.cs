sealed class LedgerWriteException(string message) : HarnessException(message);

sealed class Ledger(string runId)
{
    readonly Lock _gate = new();
    bool _initialized;

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

    public Task WriteArtifactAsync(string task, string name, string text, CancellationToken ct) =>
        File.WriteAllTextAsync(Path.Combine(TaskDir(task), name), text, ct);

    public Task WriteArtifactAsync(string task, string name, byte[] bytes, CancellationToken ct) =>
        File.WriteAllBytesAsync(Path.Combine(TaskDir(task), name), bytes, ct);

    public void SaveResult(TaskResult result) =>
        WriteFile(() => Json.WriteFile(Path.Combine(TaskDir(result.Task), "result.json"), result, LedgerJson.Files.TaskResult));

    public void Write(string? task, string type, JsonObject? data = null)
    {
        WriteFile(() => WriteCore(task, type, data));
    }

    void WriteCore(string? task, string type, JsonObject? data)
    {
        var line = new LedgerLine { Ts = Json.Timestamp(), Run = RunId, Task = task, Type = type, Data = data };
        lock (_gate)
        {
            Directory.CreateDirectory(Dir);
            if (!_initialized)
            {
                var text = File.Exists(EventsFile) ? ReadText() : "";
                Parse(text, out var torn);
                if (torn)
                {
                    using var source = new FileStream(EventsFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    using var copy = new MemoryStream();
                    source.CopyTo(copy);
                    var bytes = copy.ToArray();
                    var start = Array.LastIndexOf(bytes, (byte)'\n') + 1;
                    var timestamp = DateTime.UtcNow;
                    string sidecar;
                    while (true)
                    {
                        sidecar = $"events.torn-{timestamp.ToString("yyyyMMddTHHmmssZ", CultureInfo.InvariantCulture)}.jsonl";
                        var path = Path.Combine(Dir, sidecar);
                        if (File.Exists(path))
                        {
                            timestamp = timestamp.AddSeconds(1);
                            continue;
                        }
                        using var backup = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
                        backup.Write(bytes.AsSpan(start));
                        backup.Flush(flushToDisk: true);
                        break;
                    }
                    Json.WriteBytes(EventsFile, bytes.AsSpan(0, start));
                    Append(new LedgerLine
                    {
                        Ts = Json.Timestamp(), Run = RunId, Type = "ledger-repaired",
                        Data = new JsonObject { ["file"] = sidecar, ["bytes"] = bytes.Length - start },
                    });
                }
                _initialized = true;
            }
            Append(line);
        }

        Console.WriteLine($"{DateTime.Now:HH:mm:ss} {task ?? "-",-20} {type,-11} {data?.ToJsonString() ?? ""}");
    }

    public List<LedgerLine> Read()
    {
        lock (_gate)
        {
            return File.Exists(EventsFile) ? Parse(ReadText(), out _) : [];
        }
    }

    void Append(LedgerLine line)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(line, LedgerJson.Events.LedgerLine) + "\n");
        using var stream = new FileStream(EventsFile, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        stream.Write(bytes);
        stream.Flush(flushToDisk: true);
    }

    static void WriteFile(Action write)
    {
        var timer = Stopwatch.StartNew();
        while (true)
        {
            try
            {
                write();
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (timer.Elapsed >= TimeSpan.FromSeconds(2))
                    throw new LedgerWriteException($"ledger write failed: {ex.GetType().Name}: {ex.Message}");
                Thread.Sleep(25);
            }
        }
    }

    string ReadText()
    {
        using var stream = new FileStream(EventsFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    List<LedgerLine> Parse(string text, out bool torn)
    {
        var lines = text.Split('\n');
        var parsed = new List<LedgerLine>();
        for (var i = 0; i < lines.Length - 1; i++)
        {
            if (lines[i].Length == 0) continue;
            try
            {
                parsed.Add(JsonSerializer.Deserialize(lines[i], LedgerJson.Events.LedgerLine)
                    ?? throw new JsonException("expected a ledger event"));
            }
            catch (JsonException ex)
            {
                throw new HarnessException($"corrupt ledger {EventsFile} at line {i + 1}: {ex.Message}");
            }
        }
        torn = lines[^1].Length > 0;
        if (lines[^1].Length > 0)
            Console.Error.WriteLine($"ignoring torn ledger line {EventsFile}:{lines.Length}");
        return parsed;
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
                "interrupted" => "interrupted",
                "worktree" or "route" or "session" or "environment" => "dispatching",
                "permission" or "form" or "retry" or "status" or "timeout" or "cancel" or "escalation-dropped"
                    when states.GetValueOrDefault(line.Task!) == "interrupted" => "interrupted",
                "prompt" or "permission" or "form" or "retry" or "status" or "timeout" or "cancel" or "escalation-dropped" => "running",
                "escalated" => "waiting",
                "collected" or "verified" or "outcome" => "settling",
                _ => states.GetValueOrDefault(line.Task!, line.Type),
            };
        }

        return states;
    }

    public void SaveRecord(TaskRecord record) =>
        WriteFile(() => Json.WriteFile(Path.Combine(TaskDir(record.Spec.Id!), "task.json"), record, LedgerJson.Files.TaskRecord));

    static string ReadFile(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public TaskRecord LoadRecord(string task)
    {
        var path = Path.Combine(Dir, task, "task.json");
        return File.Exists(path)
            ? JsonSerializer.Deserialize(ReadFile(path), LedgerJson.Files.TaskRecord)!
            : throw new HarnessException($"no task record {path}");
    }

    public TaskResult? LoadResult(string task)
    {
        var path = Path.Combine(Dir, task, "result.json");
        return File.Exists(path) ? JsonSerializer.Deserialize(ReadFile(path), LedgerJson.Files.TaskResult) : null;
    }

    public RunInfo LoadRunInfo()
    {
        var path = Path.Combine(Dir, "run.json");
        return File.Exists(path)
            ? JsonSerializer.Deserialize(ReadFile(path), LedgerJson.Files.RunInfo)!
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
