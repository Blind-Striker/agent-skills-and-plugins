// Escalated asks, one JSON file each, under <wt-root>/.dispatch/<run>/asks/, and the operator's
// decisions under .../decisions/. Files are written to a temporary name and then renamed, so a
// file monitor never sees a half-written file.
static class AskQueue
{
    // A new supervisor owns a new server: asks left by a dead one cannot be answered anymore.
    public static void Reset(string run)
    {
        foreach (var dir in new[] { Locations.AsksDir(run), Locations.DecisionsDir(run) })
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    public static void Write(PendingAsk ask) =>
        WriteAtomic(Locations.AsksDir(ask.Run), ask.AskId, JsonSerializer.Serialize(ask, DispatchJsonIndented.Default.PendingAsk));

    public static void Remove(string run, string askId)
    {
        var file = Path.Combine(Locations.AsksDir(run), $"{askId}.json");
        if (File.Exists(file)) File.Delete(file);
    }

    public static List<PendingAsk> List(string run)
    {
        var dir = Locations.AsksDir(run);
        if (!Directory.Exists(dir)) return [];
        var asks = new List<PendingAsk>();
        foreach (var file in Directory.GetFiles(dir, "*.json").Order(StringComparer.Ordinal))
        {
            try
            {
                if (JsonSerializer.Deserialize(File.ReadAllText(file), DispatchJson.Default.PendingAsk) is { } ask) asks.Add(ask);
            }
            catch (Exception ex) when (ex is IOException or JsonException)
            {
                // Removed or replaced while listing; the next listing sees the current state.
            }
        }

        return asks.OrderBy(x => x.Time, StringComparer.Ordinal).ToList();
    }

    public static void WriteDecision(string run, AskDecision decision) =>
        WriteAtomic(Locations.DecisionsDir(run), decision.AskId, JsonSerializer.Serialize(decision, DispatchJsonIndented.Default.AskDecision));

    // Decisions waiting for the supervisor; each is removed once read.
    public static List<AskDecision> TakeDecisions(string run)
    {
        var dir = Locations.DecisionsDir(run);
        if (!Directory.Exists(dir)) return [];
        var decisions = new List<AskDecision>();
        foreach (var file in Directory.GetFiles(dir, "*.json"))
        {
            try
            {
                if (JsonSerializer.Deserialize(File.ReadAllText(file), DispatchJson.Default.AskDecision) is { } decision) decisions.Add(decision);
                File.Delete(file);
            }
            catch (Exception ex) when (ex is IOException or JsonException)
            {
                // Locked or unreadable for now; the next tick tries again.
            }
        }

        return decisions;
    }

    static void WriteAtomic(string dir, string name, string json)
    {
        Directory.CreateDirectory(dir);
        var temp = Path.Combine(dir, $"{name}.tmp");
        File.WriteAllText(temp, json + "\n");
        File.Move(temp, Path.Combine(dir, $"{name}.json"), overwrite: true);
    }
}
