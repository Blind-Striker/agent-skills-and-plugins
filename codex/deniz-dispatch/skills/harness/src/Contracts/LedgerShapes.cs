sealed class RunInfo
{
    public string Run { get; set; } = "";

    public string Repo { get; set; } = "";

    public string TasksFile { get; set; } = "";

    public string? PolicyFile { get; set; }

    public string Created { get; set; } = "";

    public string WorktreeRoot { get; set; } = "";

    public string ServerVersion { get; set; } = "";

    public int MaxParallel { get; set; } = Constants.DefaultMaxParallel;
}

sealed class TaskRecord
{
    public TaskSpec Spec { get; set; } = new();

    public string PromptText { get; set; } = "";

    public string? Worktree { get; set; }

    public string? Branch { get; set; }

    public string? BaseRef { get; set; }

    public string? BaseSha { get; set; }

    public string? Route { get; set; }

    public string? Agent { get; set; }

    public string? Lineage { get; set; }

    public string? SessionId { get; set; }

    public string? PromptId { get; set; }

    public double? PromptCreated { get; set; }
}

sealed class TokenTotals
{
    public double Input { get; set; }

    public double Output { get; set; }

    public double Reasoning { get; set; }

    public double CacheRead { get; set; }

    public double CacheWrite { get; set; }
}

sealed class VerifyResult
{
    public string Command { get; set; } = "";

    public int ExitCode { get; set; }

    public double Seconds { get; set; }

    public bool TimedOut { get; set; }
}

sealed class TaskResult
{
    public string Task { get; set; } = "";

    public string Run { get; set; } = "";

    public string State { get; set; } = "";

    public string? Outcome { get; set; }

    public string? SessionId { get; set; }

    public string? RouteRequested { get; set; }

    public List<string> RoutesUsed { get; set; } = [];

    public double? Cost { get; set; }

    public TokenTotals? Tokens { get; set; }

    public bool TokensComplete { get; set; }

    public int PermissionAsks { get; set; }

    public int AsksEscalated { get; set; }

    public int FormsCancelled { get; set; }

    public int Retries { get; set; }

    public bool TimedOut { get; set; }

    public bool CancelRequested { get; set; }

    public string? BaseSha { get; set; }

    public string? Commit { get; set; }

    public string? DiffStat { get; set; }

    public List<string> FilesChanged { get; set; } = [];

    public List<string> OutOfScope { get; set; } = [];

    public List<VerifyResult> Verify { get; set; } = [];

    public string? ExecutionError { get; set; }

    public string? Error { get; set; }
}

sealed class LedgerLine
{
    public string Ts { get; set; } = "";

    public string Run { get; set; } = "";

    public string? Task { get; set; }

    public string Type { get; set; } = "";

    public JsonObject? Data { get; set; }
}
