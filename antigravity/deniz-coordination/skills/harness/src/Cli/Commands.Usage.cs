static partial class Commands
{
    static readonly string[] UsageDimensions = ["kind", "route", "backend", "state", "outcome", "status", "repo", "run", "day", "tag"];
    static readonly string[] UsageCounters = ["tasks", "done", "failed", "cancelled", "unsettled", "stopped", "tokensIncomplete"];
    static readonly string[] UsageSums = ["seconds", "input", "cacheRead", "output", "reasoning", "cost"];

    public static int Usage(Args a)
    {
        var since = UsageDate(a, "since");
        var until = UsageDate(a, "until");
        var dimensions = (a.Get("by") ?? "kind,route").Split(',', StringSplitOptions.TrimEntries);
        foreach (var dimension in dimensions)
            if (!UsageDimensions.Contains(dimension, StringComparer.Ordinal))
                throw new HarnessException($"unknown usage dimension {dimension}");
        var repo = a.Get("repo") is { } path ? Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)) : null;
        var tasks = new List<(JsonObject Key, List<string> Tags, JsonObject Figures)>();
        var skipped = new List<(string Run, string? Task, string Reason)>();
        if (Directory.Exists(Locations.LedgerRoot))
            foreach (var dir in Directory.GetDirectories(Locations.LedgerRoot))
            {
                var id = Path.GetFileName(dir);
                if (a.Get("run") is { } pattern && !Wildcard.Match(id, pattern, OperatingSystem.IsWindows())) continue;
                var ledger = new Ledger(id);
                try
                {
                    var info = ledger.LoadRunInfo() ?? throw new JsonException("expected a run object");
                    var created = UsageTimestamp(info.Created);
                    var day = created is { } timestamp ? DateOnly.FromDateTime(timestamp.UtcDateTime) : (DateOnly?)null;
                    if (since is { } lower && (day is null || day < lower)
                        || until is { } upper && (day is null || day > upper)) continue;
                    if (repo is not null && !string.Equals(info.Repo is { } recordedRepo ? Path.TrimEndingDirectorySeparator(recordedRepo) : null, repo,
                        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) continue;
                    var states = ledger.TaskStates();
                    var events = ledger.Read().Where(line => line.Task is not null).ToLookup(line => line.Task!);
                    var ids = states.Keys.Concat(Directory.GetDirectories(dir)
                        .Where(folder => !Path.GetFileName(folder).StartsWith(".", StringComparison.Ordinal)
                            && File.Exists(Path.Combine(folder, "task.json")))
                        .Select(Path.GetFileName)).OfType<string>().Distinct(StringComparer.Ordinal);
                    foreach (var task in ids)
                    {
                        try
                        {
                            var record = ledger.LoadRecord(task) ?? throw new JsonException("expected a task object");
                            if (record.Spec?.Kind is null) throw new JsonException("expected a task spec with kind");
                            var result = ledger.LoadResult(task);
                            var state = states.GetValueOrDefault(task, "unknown");
                            var key = new JsonObject
                            {
                                ["kind"] = record.Spec.Kind,
                                ["route"] = result?.RouteRequested ?? record.Settings?.Route?.Value ?? "unknown",
                                ["backend"] = result?.Backend ?? record.Settings?.Backend?.Value ?? "unknown",
                                ["state"] = state,
                                ["outcome"] = result?.Outcome ?? "none",
                                ["status"] = result?.WorkerStatus ?? "none",
                                ["repo"] = info.Repo ?? "unknown",
                                ["run"] = id,
                                ["day"] = day?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "unknown",
                            };
                            var tags = (record.Spec.Tags ?? []).Where(tag => !string.IsNullOrEmpty(tag)).ToList();
                            tasks.Add((key, tags.Count > 0 ? tags : ["(none)"],
                                UsageFigures(state, result, events[task])));
                        }
                        catch (Exception ex) when (UsageReadError(ex)) { skipped.Add((id, task, ex.Message)); }
                    }
                }
                catch (Exception ex) when (UsageReadError(ex)) { skipped.Add((id, null, ex.Message)); }
            }

        var groups = new Dictionary<string, (JsonObject Key, List<JsonObject> Tasks)>(StringComparer.Ordinal);
        foreach (var task in tasks)
            foreach (var tag in dimensions.Contains("tag", StringComparer.Ordinal) ? task.Tags.Distinct(StringComparer.Ordinal) : ["(none)"])
            {
                var key = new JsonObject();
                foreach (var dimension in dimensions.Distinct(StringComparer.Ordinal))
                    key[dimension] = dimension == "tag" ? JsonValue.Create(tag) : task.Key[dimension]?.DeepClone();
                var encoded = key.ToJsonString();
                if (!groups.TryGetValue(encoded, out var group))
                    groups.Add(encoded, group = (key, []));
                group.Tasks.Add(task.Figures);
            }
        var rows = groups.Values.Select(group => UsageSum(group.Tasks, group.Key)).ToList();
        rows.Sort((left, right) => CompareUsageKeys(left, right, dimensions));
        var total = UsageSum(tasks.Select(task => task.Figures), new JsonObject());
        var skippedJson = new JsonArray(skipped.OrderBy(item => item.Run, StringComparer.Ordinal)
            .ThenBy(item => item.Task, StringComparer.Ordinal).Select(item => (JsonNode)new JsonObject
            {
                ["run"] = item.Run, ["task"] = item.Task,
                ["reason"] = item.Reason.Replace('\r', ' ').Replace('\n', ' '),
            }).ToArray());
        if (a.Has("json"))
            Console.WriteLine(new JsonObject { ["rows"] = new JsonArray(rows.ToArray<JsonNode>()), ["total"] = total, ["skipped"] = skippedJson }.ToJsonString());
        else
        {
            if (tasks.Count == 0) Console.WriteLine("no runs");
            else
            {
                Console.WriteLine(string.Join('\t', dimensions.Concat(UsageCounters).Concat(UsageSums)));
                foreach (var row in rows) PrintUsageRow(row, dimensions.Select(dimension => row["key"]![dimension]!.GetValue<string>()));
                PrintUsageRow(total, dimensions.Select((_, index) => index == 0 ? "total" : ""));
            }
            if (skippedJson.Count > 0)
                Console.Error.WriteLine($"skipped {skippedJson.Count.ToString(CultureInfo.InvariantCulture)} item(s): " + string.Join("; ", skippedJson.Select(item =>
                    $"{item!["run"]}{(item["task"] is { } task ? "/" + task.GetValue<string>() : "")} ({item["reason"]!.GetValue<string>()})")));
        }
        return 0;
    }

    static bool UsageReadError(Exception ex) => ex is HarnessException or JsonException or IOException or UnauthorizedAccessException or InvalidOperationException;

    static DateOnly? UsageDate(Args a, string option)
    {
        if (a.Get(option) is not { } value) return null;
        if (DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) return date;
        throw new HarnessException($"--{option} date {value} must be yyyy-MM-dd");
    }

    static DateTimeOffset? UsageTimestamp(string? value) => DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var timestamp) ? timestamp : null;

    static JsonObject UsageFigures(string state, TaskResult? result, IEnumerable<LedgerLine> events)
    {
        var prompt = UsageTimestamp(events.FirstOrDefault(line => line.Type == "prompt")?.Ts);
        var final = UsageTimestamp(events.FirstOrDefault(line => line.Type == "final")?.Ts);
        return new JsonObject
        {
            ["tasks"] = 1,
            ["done"] = state == "done" ? 1 : 0,
            ["failed"] = state == "failed" ? 1 : 0,
            ["cancelled"] = state == "cancelled" ? 1 : 0,
            ["unsettled"] = state is not ("done" or "failed" or "cancelled") ? 1 : 0,
            ["stopped"] = result?.WorkerStatus is "stopped" or "blocked" ? 1 : 0,
            ["tokensIncomplete"] = result?.TokensComplete == false ? 1 : 0,
            ["seconds"] = prompt is { } start && final is { } end && end >= start ? (end - start).TotalSeconds : (double?)null,
            ["input"] = result?.Tokens?.Input,
            ["cacheRead"] = result?.Tokens?.CacheRead,
            ["output"] = result?.Tokens?.Output,
            ["reasoning"] = result?.Tokens?.Reasoning,
            ["cost"] = result?.Cost,
        };
    }

    static JsonObject UsageSum(IEnumerable<JsonObject> source, JsonObject key)
    {
        var tasks = source.ToList();
        var row = new JsonObject { ["key"] = key };
        foreach (var counter in UsageCounters) row[counter] = tasks.Sum(task => task[counter]!.GetValue<int>());
        foreach (var sum in UsageSums)
        {
            var values = tasks.Select(task => task[sum]?.GetValue<double>()).OfType<double>().ToList();
            row[sum] = values.Count == 0 ? null : JsonValue.Create(values.Sum());
            row[sum + "Known"] = values.Count;
        }
        return row;
    }

    static int CompareUsageKeys(JsonObject left, JsonObject right, string[] dimensions)
    {
        foreach (var dimension in dimensions)
        {
            var comparison = string.Compare(left["key"]![dimension]!.GetValue<string>(), right["key"]![dimension]!.GetValue<string>(), StringComparison.Ordinal);
            if (comparison != 0) return comparison;
        }
        return 0;
    }

    static void PrintUsageRow(JsonObject row, IEnumerable<string> key)
    {
        var cells = key.Concat(UsageCounters.Select(counter => row[counter]!.GetValue<int>().ToString(CultureInfo.InvariantCulture)))
            .Concat(UsageSums.Select(sum =>
            {
                if (row[sum] is not { } value) return "unknown";
                var text = value.GetValue<double>().ToString(sum == "cost" ? "0.0000" : sum == "seconds" ? "0" : "G", CultureInfo.InvariantCulture);
                var known = row[sum + "Known"]!.GetValue<int>();
                var count = row["tasks"]!.GetValue<int>();
                return known == count ? text : $"{text} ({known.ToString(CultureInfo.InvariantCulture)}/{count.ToString(CultureInfo.InvariantCulture)})";
            }));
        Console.WriteLine(string.Join('\t', cells));
    }
}
