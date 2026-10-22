// Escalated asks and operator decisions are published atomically in the run's control folder.
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
        WriteAtomic(Locations.AsksDir(ask.Run), ask.AskId, JsonSerializer.Serialize(ask, ControlJson.Files.PendingAsk));

    public static void Remove(string run, string askId)
    {
        var file = Path.Combine(Locations.AsksDir(run), $"{askId}.json");
        if (File.Exists(file)) File.Delete(file);
    }

    public static List<PendingAsk> List(string run)
    {
        return ListFolder(Locations.ControlDir(run));
    }

    public static List<PendingAsk> ListFolder(string control)
    {
        var dir = Path.Combine(control, "asks");
        if (!Directory.Exists(dir)) return [];
        var asks = new List<PendingAsk>();
        foreach (var file in Directory.GetFiles(dir, "*.json").Order(StringComparer.Ordinal))
        {
            try
            {
                if (JsonSerializer.Deserialize(File.ReadAllText(file), ControlJson.Files.PendingAsk) is { } ask) asks.Add(ask);
            }
            catch (Exception ex) when (ex is IOException or JsonException)
            {
                // Removed or replaced while listing; the next listing sees the current state.
            }
        }

        return asks.OrderBy(x => x.Time, StringComparer.Ordinal).ToList();
    }

    public static void WriteDecision(string run, AskDecision decision) =>
        WriteAtomic(Locations.DecisionsDir(run), decision.AskId, JsonSerializer.Serialize(decision, ControlJson.Files.AskDecision));

    // Decisions waiting for the supervisor; each is removed once read.
    public static List<AskDecision> TakeDecisions(string run, Action<string, string> invalid)
    {
        var dir = Locations.DecisionsDir(run);
        if (!Directory.Exists(dir)) return [];
        var decisions = new List<AskDecision>();
        foreach (var file in Directory.GetFiles(dir, "*.json"))
        {
            try
            {
                var decision = JsonSerializer.Deserialize(File.ReadAllText(file), ControlJson.Files.AskDecision)
                    ?? throw new JsonException("expected a decision");
                decisions.Add(decision);
                File.Delete(file);
            }
            catch (JsonException ex)
            {
                File.Delete(file);
                invalid(Path.GetFileName(file), ex.Message);
            }
            catch (IOException ex) { invalid(Path.GetFileName(file), $"cannot read decision: {ex.Message}"); }
        }

        return decisions;
    }

    static void WriteAtomic(string dir, string name, string json)
    {
        if (!Regex.IsMatch(name, "\\A[a-z0-9][a-z0-9-]*\\z")) throw new HarnessException("invalid ask id");
        Json.WriteBytes(Path.Combine(dir, $"{name}.json"), Encoding.UTF8.GetBytes(json + "\n"));
    }
}
