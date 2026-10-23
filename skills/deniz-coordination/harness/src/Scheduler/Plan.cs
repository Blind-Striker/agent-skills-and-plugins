sealed class PlannedTask
{
    public required TaskSpec Spec { get; init; }

    public required string PromptText { get; init; }

    public RouteRef? Route { get; init; }
    public Prompt.Inputs? PromptInputs { get; init; }

}

// What the contract needs from either a planned task or a ledger record.
sealed record PlannedOrRecord(TaskSpec Spec, string PromptText);

sealed class Plan
{
    public required TaskFile File { get; init; }

    public required string TasksPath { get; init; }

    public required string Repo { get; init; }
    public ConfigLayers? Config { get; set; }

    public List<PlannedTask> Tasks { get; } = [];

    public List<string> Errors { get; } = [];

    public List<string> Warnings { get; } = [];

    public void PrintWarnings()
    {
        foreach (var warning in Warnings) Console.WriteLine($"WARN {warning}");
    }

    public int Report()
    {
        PrintWarnings();
        foreach (var error in Errors) Console.WriteLine($"ERROR {error}");
        Console.WriteLine(Errors.Count == 0
            ? $"plan ok: {Tasks.Count} task(s) against {Repo}"
            : $"plan refused: {Errors.Count} error(s)");
        return Errors.Count == 0 ? 0 : 2;
    }
}
