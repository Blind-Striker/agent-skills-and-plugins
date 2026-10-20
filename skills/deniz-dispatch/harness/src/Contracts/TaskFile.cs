sealed class TaskFile
{
    public string? Run { get; set; }

    public string? Repo { get; set; }

    public int? MaxParallel { get; set; }

    public List<TaskSpec>? Tasks { get; set; }
}

sealed class TaskSpec
{
    public string? Id { get; set; }

    public string Kind { get; set; } = "code";

    public string? Base { get; set; }

    public string? Agent { get; set; }

    public string? Persona { get; set; }

    public string? Route { get; set; }

    public List<string>? Fallback { get; set; }

    public string? Prompt { get; set; }

    public string? PromptFile { get; set; }

    public List<string>? Scope { get; set; }

    public List<string>? Verify { get; set; }

    public int? MaxMinutes { get; set; }

    public string? ReviewOf { get; set; }
}
