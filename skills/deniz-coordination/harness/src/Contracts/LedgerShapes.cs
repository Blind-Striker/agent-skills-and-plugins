sealed class RunInfo : VersionedContract
{
    public string Run { get; set; } = "";
    public string? ControlId { get; set; }
    public HarnessInfo Harness { get; set; } = new();
    public string Created { get; set; } = "";
    public string? Repo { get; set; }
    public string TasksFile { get; set; } = "";
    public int MaxParallel { get; set; } = Constants.DefaultMaxParallel;
    public Dictionary<string, BackendInfo> Backends { get; set; } = [];
    public HostInfo Host { get; set; } = new();
}

sealed class HarnessInfo
{
    public string Version { get; set; } = Constants.HarnessVersion;
}

sealed class BackendInfo
{
    public string Version { get; set; } = "";
}

sealed class HostInfo
{
    public string Os { get; set; } = OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "macos" : "linux";
}

sealed class EffectiveConfig : VersionedContract
{
    public List<ConfigSource> Sources { get; set; } = [];
    public Setting<int>? MaxParallel { get; set; }
    public ConfigLocations? Locations { get; set; }
    public EffectiveRestrictions? Restrictions { get; set; }
    public List<InstructionRecord> Instructions { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
    public Dictionary<string, FrozenTask> Tasks { get; set; } = [];
}

sealed class ConfigSource
{
    public string Layer { get; set; } = "";
    public string? Path { get; set; }
    public string? Copy { get; set; }
    public string? Head { get; set; }
    public bool? Dirty { get; set; }
    public List<string>? Args { get; set; }
}

sealed class InstructionRecord
{
    public string Text { get; set; } = "";
    public string From { get; set; } = "";
}

sealed class EffectiveRestrictions
{
    public RouteRestriction? Routes { get; set; }
    public List<EffectiveRule> Deny { get; set; } = [];
    public List<EffectiveRule> Escalate { get; set; } = [];
    public List<ScopeCeiling> Scope { get; set; } = [];
    public Setting<string>? MaxAccess { get; set; }
    public Setting<bool>? NetworkAllowed { get; set; }
    public List<string>? Scrub { get; set; }
}

sealed class RouteRestriction
{
    public List<string> Value { get; set; } = [];
    public List<string> From { get; set; } = [];
}

sealed class EffectiveRule
{
    public string Name { get; set; } = "";
    public string Action { get; set; } = "";
    public List<string> Resources { get; set; } = [];
    public Dictionary<string, string> Enforcement { get; set; } = [];
}

sealed class FrozenTask
{
    public EffectiveSettings Settings { get; set; } = new();
    public List<ContractRecord>? Contracts { get; set; }
    public string? InputSha256 { get; set; }
    public string? ResultSchemaSha256 { get; set; }
}

class Setting<T>
{
    public T? Value { get; set; }
    public string From { get; set; } = "";
    public string? Actual { get; set; }
    public string? Reason { get; set; }
    public string? Enforcement { get; set; }
}

sealed class EffectiveSettings
{
    public Setting<string>? Backend { get; set; }
    public Setting<string>? Route { get; set; }
    public Setting<string>? Effort { get; set; }
    public Setting<int>? TimeoutMinutes { get; set; }
    public Setting<string>? Profile { get; set; }
    public Setting<string>? Project { get; set; }
    public Setting<string>? Access { get; set; }
    public Setting<string>? ReadOnlyShell { get; set; }
    public Setting<string>? Network { get; set; }
    public Setting<string>? Workspace { get; set; }
    public Setting<string>? TempRoot { get; set; }
    public Setting<List<string>>? WritableRoots { get; set; }
    public Setting<List<string>>? PrivateFolders { get; set; }
    public Dictionary<string, Setting<string>>? Environment { get; set; }
    public Setting<string>? Dir { get; set; }
    public Setting<string>? Commit { get; set; }
    public Setting<bool>? RawPrompt { get; set; }
    public EffectiveScope? Scope { get; set; }
    public List<RuleEnforcement>? Rules { get; set; }
}

sealed class EffectiveScope
{
    public List<string> Value { get; set; } = [];
    public List<ScopeCeiling> Ceilings { get; set; } = [];
}

sealed class ScopeCeiling
{
    public List<string> Globs { get; set; } = [];
    public string From { get; set; } = "";
}

sealed class RuleEnforcement
{
    public string Name { get; set; } = "";
    public string Enforcement { get; set; } = "";
}

sealed class ContractRecord
{
    public string Path { get; set; } = "";
    public string From { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public string Version { get; set; } = "";
}

sealed class TaskRecord : VersionedContract
{
    [JsonIgnore]
    public Prompt.Inputs? PromptInputs { get; set; }
    public TaskSpec Spec { get; set; } = new();
    public string PromptText { get; set; } = "";
    public EffectiveSettings Settings { get; set; } = new();
    public WorkspaceRecord Workspace { get; set; } = new();
    public List<ContractRecord>? Contracts { get; set; }
    public string? Agent { get; set; }
    public string? Lineage { get; set; }
    // The model an opencode roster agent pinned when the task started; its settings route names the agent.
    public string? RouteResolved { get; set; }
    public SessionRecord Session { get; set; } = new();
    public BackendState BackendState { get; set; } = new();
    public List<VerifyResult>? Prepare { get; set; }
    public bool? PrepareRunning { get; set; }
    public bool? PrepareDone { get; set; }
}

sealed class WorkspaceRecord
{
    public string? Path { get; set; }
    public string? ScratchAllocationId { get; set; }
    public string? Branch { get; set; }
    public string? BaseRef { get; set; }
    public string? BaseSha { get; set; }
}

sealed class SessionRecord
{
    public string? Id { get; set; }
    public string? Turn { get; set; }
}

sealed class BackendState
{
    public string? PromptId { get; set; }
    public double? PromptCreated { get; set; }
    public string? ProjectId { get; set; }
    public string? Host { get; set; }
    public int? Pid { get; set; }
    public bool? ProcessGroup { get; set; }
    public string? ProcessStartTime { get; set; }
    public string? LinuxBootId { get; set; }
    public long? LinuxStartTicks { get; set; }
    public List<ProcessIdentity>? Descendants { get; set; }
    public string? CodexHome { get; set; }
    public bool? CodexHomeTemporary { get; set; }
    public string? CodexHomeMarkerName { get; set; }
    public string? CodexHomeMarkerText { get; set; }
    public string? Profile { get; set; }
    public string? ProfileReason { get; set; }
}

sealed class ProcessIdentity
{
    public int Pid { get; set; }
    public string StartTime { get; set; } = "";
    public string? LinuxBootId { get; set; }
    public long? LinuxStartTicks { get; set; }
}

sealed class TokenTotals
{
    public double? Input { get; set; }
    public double? Output { get; set; }
    public double? Reasoning { get; set; }
    public double? CacheRead { get; set; }
    public double? CacheWrite { get; set; }
}

sealed class VerifyResult
{
    public string Command { get; set; } = "";
    public int ExitCode { get; set; }
    public double Seconds { get; set; }
    public bool TimedOut { get; set; }
    public bool? Advisory { get; set; }
    public string? NotRun { get; set; }
}

sealed class TaskResult : VersionedContract
{
    public string Task { get; set; } = "";
    public string Run { get; set; } = "";
    public string? Kind { get; set; }
    public string? Backend { get; set; }
    public string State { get; set; } = "";
    public List<string> Failures { get; set; } = [];
    public List<string>? Warnings { get; set; }
    public List<string>? CapabilityNotes { get; set; }
    public string? Outcome { get; set; }
    public SessionRecord? Session { get; set; }
    public string? RouteRequested { get; set; }
    public string? RouteResolved { get; set; }
    public List<string> RoutesUsed { get; set; } = [];
    public bool RouteVerified { get; set; }
    public List<string>? RouteDrift { get; set; }
    public string? WorkerStatus { get; set; }
    public double? Cost { get; set; }
    public TokenTotals? Tokens { get; set; }
    public bool? TokensComplete { get; set; }
    public long? ContextWindow { get; set; }
    public int Asks { get; set; }
    public int AsksEscalated { get; set; }
    public int FormsCancelled { get; set; }
    public int Retries { get; set; }
    public bool TimedOut { get; set; }
    public bool CancelRequested { get; set; }
    public bool? WorkerStopped { get; set; }
    public string? BaseSha { get; set; }
    public string? CommitMode { get; set; }
    public string? Commit { get; set; }
    public string? DiffStat { get; set; }
    public string? WipPatch { get; set; }
    public int? WipFileCount { get; set; }
    public List<string> FilesChanged { get; set; } = [];
    public List<string> OutOfScope { get; set; } = [];
    public List<VerifyResult> Verify { get; set; } = [];
    public List<VerifyResult>? Prepare { get; set; }
    public string? VerifyShell { get; set; }
    public string? Deliverable { get; set; }
    public ValidationResult? Validation { get; set; }
    public string? ExecutionError { get; set; }
    public List<string>? RecoveredErrors { get; set; }
    public string? Error { get; set; }
}

sealed class ValidationResult
{
    public bool Ok { get; set; }
    public List<ValidationError> Errors { get; set; } = [];
}

sealed class ValidationError
{
    public string At { get; set; } = "";
    public string Message { get; set; } = "";
}

// Tool completions use seconds, like the other ledger durations. Task identity is on LedgerLine.
sealed record ToolEvent(string Tool, string Resource, string Outcome, double Duration)
{
    public JsonObject Data() => new()
    {
        ["tool"] = Tool, ["resource"] = ToolResource.Summary(Resource),
        ["outcome"] = Outcome, ["duration"] = Duration,
    };
}

sealed class LedgerLine : IJsonOnDeserialized
{
    int? _schemaVersion;

    [JsonPropertyOrder(-10)]
    public int? SchemaVersion
    {
        get => Type == "run" ? _schemaVersion ?? Constants.SchemaVersion : null;
        set => _schemaVersion = value;
    }
    public string Ts { get; set; } = "";
    public string Run { get; set; } = "";
    public string? Task { get; set; }
    public string Type { get; set; } = "";
    public JsonObject? Data { get; set; }

    void IJsonOnDeserialized.OnDeserialized()
    {
        if (Type == "run") Json.CheckVersion(_schemaVersion ?? 1);
        else if (_schemaVersion is { } version) Json.CheckVersion(version);
    }
}
