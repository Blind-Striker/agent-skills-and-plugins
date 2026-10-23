sealed record WorkerPlan(
    string TaskId, string Kind, RouteRef Route, string? Agent, string? Persona, Modes Modes, string Directory);

sealed record Modes(string Profile, string Project, string Access, string Network, string Workspace, string ReadOnlyShell = "decline")
{
    public string Profile { get; } = BackendValues.OneOf(Profile, nameof(Profile), "clean", "full");
    public string Project { get; } = BackendValues.OneOf(Project, nameof(Project), "on", "off");
    public string Access { get; } = BackendValues.OneOf(Access, nameof(Access), "read-only", "workspace", "full");
    public string Network { get; } = BackendValues.OneOf(Network, nameof(Network), "on", "off");
    public string Workspace { get; } = BackendValues.OneOf(Workspace, nameof(Workspace), "worktree", "in-place");
    public string ReadOnlyShell { get; } = BackendValues.OneOf(ReadOnlyShell, nameof(ReadOnlyShell), "decline", "escalate");
}

sealed record WorkerRules(IReadOnlyList<EffectiveRule> Deny, IReadOnlyList<EffectiveRule> Escalate);

sealed record WorkerLaunch(
    string RunId, string TaskId, string Kind, RouteRef Route, string? Agent, string? Persona,
    Modes Modes, string Directory, WorkerRules Rules, IReadOnlyDictionary<string, string> Environment,
    JsonObject? OutputSchema, string ProfileActual, string? ProfileReason)
{
    public string ProfileActual { get; } = BackendValues.Profile(ProfileActual, ProfileReason);
    public string? ProfileReason { get; } = ProfileReason;
}

// The core assembles this text. Backends send it unchanged as one user input, including on continue.
sealed record WorkerPrompt(string Text, bool IsContinue);

// State is opaque to the core; a backend owns its interpretation and persistence shape.
sealed record WorkerRef(string Backend, string TaskId, string Id, string? Turn, JsonObject State);

sealed record BlockerRef(string Backend, string WorkerId, string? OriginChildId, string AskId);

abstract record WorkerSignal
{
    private WorkerSignal() { }

    public sealed record Fault(string? TaskId, string Operation, string Message) : WorkerSignal;

    public abstract record TaskSignal : WorkerSignal
    {
        public string TaskId { get; }
        public string Turn { get; }

        private protected TaskSignal(string taskId, string turn)
        {
            TaskId = BackendValues.NonBlank(taskId, nameof(taskId));
            Turn = BackendValues.NonBlank(turn, nameof(turn));
        }
    }

    public sealed record Ask(string TaskId, string Turn, BlockerRef BlockerRef, string Action, IReadOnlyList<string> Resources)
        : TaskSignal(TaskId, Turn);

    public sealed record AskClosed(string TaskId, string Turn, string AskId, string By, BlockerAnswer? Answer)
        : TaskSignal(TaskId, Turn)
    {
        public string By { get; } = BackendValues.OneOf(By, nameof(By), "server", "gone");
    }

    public sealed record Form(string TaskId, string Turn, BlockerRef BlockerRef, string Title) : TaskSignal(TaskId, Turn);

    public sealed record Retry(string TaskId, string Turn, int Attempt, string Message, DateTimeOffset? Next, string? Reason, string? Provider)
        : TaskSignal(TaskId, Turn);

    // Call ids are unique within a turn, including calls from child sessions.
    // Codex item/started and item/completed notifications map to this same signal.
    public sealed record ToolCall(string TaskId, string Turn, string Id, string Tool, string Resource,
        string Marker, string? Outcome, DateTimeOffset Time) : TaskSignal(TaskId, Turn)
    {
        public string Resource { get; } = ToolResource.Summary(Resource);
        public string Marker { get; } = BackendValues.OneOf(Marker, nameof(Marker), "start", "end");
        public string? Outcome { get; } = Marker == "end"
            ? BackendValues.OneOf(Outcome ?? "", nameof(Outcome), "succeeded", "failed", "interrupted", "cancelled")
            : Outcome is null ? null : throw new ArgumentException("a tool start has no outcome", nameof(Outcome));
    }

    public sealed record Status(string TaskId, string Turn, string Execution, string? Detail) : TaskSignal(TaskId, Turn)
    {
        public string Execution { get; } = BackendValues.OneOf(Execution, nameof(Execution), "succeeded", "failed", "interrupted");
    }

    // Emitted only after the backend confirmed this turn ended. Delivery may be duplicated by polling.
    public sealed record Ended(string TaskId, string Turn, string Outcome, string? Reason) : TaskSignal(TaskId, Turn)
    {
        public string Outcome { get; } = BackendValues.Outcome(Outcome);
    }

    // The turn ended, but the backend could not confirm that its processes stopped: one may still write.
    // The task settles as a zombie at once (no commit, no verify) instead of waiting for its timeout.
    public sealed record StopUnconfirmed(string TaskId, string Turn, string Outcome, string Detail) : TaskSignal(TaskId, Turn)
    {
        public string Outcome { get; } = BackendValues.Outcome(Outcome);
    }

    public sealed record Host(string HostKey, string State, string Reason, int Attempt) : WorkerSignal
    {
        public string State { get; } = BackendValues.OneOf(State, nameof(State), "connected", "disconnected");
    }
}

static class ToolResource
{
    // Use the environment scrub's value and name rules, including for URL credentials and
    // credential assignments. Redact before shortening so a cut cannot hide the matching suffix.
    public static string Summary(string resource)
    {
        if (WorkerEnvironment.Scrub(new Dictionary<string, string> { ["resource"] = resource }).Removed.Count > 0)
            return "[REDACTED]";
        // Inspect URL queries independently: an assignment scan can consume the whole URL
        // as a value after its scheme and miss every credential parameter inside it.
        foreach (Match url in Regex.Matches(resource, "[A-Za-z][A-Za-z0-9+.-]*://[^\\s\"'<>]+"))
            if (Uri.TryCreate(url.Value, UriKind.Absolute, out var uri))
                foreach (var parameter in uri.Query.TrimStart('?').Split('&', ';'))
                {
                    var pair = parameter.Split('=', 2);
                    if (pair.Length == 2 && Credential(Uri.UnescapeDataString(pair[0]), Uri.UnescapeDataString(pair[1])))
                        return "[REDACTED]";
                }
        if (Regex.IsMatch(resource, @"\b(?:proxy-)?authorization\s*[:=]|\bbearer\s+\S+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            return "[REDACTED]";
        // PowerShell's env: drive qualifies the variable name; it is not the ENV variable itself.
        foreach (Match assignment in Regex.Matches(resource, @"(?i:\$?env:)?(?<name>(?!(?i:env):)[\w-]+)\s*[=:]\s*(?<value>[^\s&;]+)|--?(?<name>[\w-]+)\s+(?<value>[^\s&;]+)"))
            if (Credential(assignment.Groups["name"].Value, assignment.Groups["value"].Value))
                return "[REDACTED]";
        if (Cli.RootEnvironment is { } environment)
            foreach (var value in environment.Passthrough(environment.Removed).Values.Where(x => x.Length > 0).OrderByDescending(x => x.Length))
                resource = resource.Replace(value, "[REDACTED]", StringComparison.Ordinal);
        var lineEnd = resource.IndexOfAny(['\r', '\n']);
        if (lineEnd >= 0) resource = resource[..lineEnd];
        return resource[..Math.Min(resource.Length, 200)];
    }

    static bool Credential(string name, string value) => WorkerEnvironment.Scrub(
        new Dictionary<string, string> { [name.Replace('-', '_')] = value }).Removed.Count > 0;
}

abstract record BlockerAnswer
{
    private BlockerAnswer() { }

    public sealed record Once : BlockerAnswer;
    public sealed record Reject(string Message) : BlockerAnswer
    {
        public string Message { get; } = BackendValues.NonBlank(Message, nameof(Message));
    }
    public sealed record Cancel(string Message) : BlockerAnswer
    {
        public string Message { get; } = BackendValues.NonBlank(Message, nameof(Message));
    }
}

enum InterruptKind { Cancel, Timeout, Hard, Shutdown }

sealed record InterruptResult(bool Stopped, string Detail, bool? Interrupted = null, int? Status = null)
{
    public string Detail { get; } = BackendValues.NonBlank(Detail, nameof(Detail));
}

sealed record LiveCheck(
    string TaskId, IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings,
    string Lineage, string Profile, string? ProfileReason, IReadOnlyList<string>? Info = null)
{
    public string Profile { get; } = BackendValues.Profile(Profile, ProfileReason);
    public string? ProfileReason { get; } = ProfileReason;
}

sealed record CreateResult(WorkerRef Worker, AppliedEnforcement Applied, WorkerStartDetails? Details = null);

// A failed creation must not hide a worker whose release could not be confirmed.
sealed class WorkerCreationException(WorkerRef worker, string message) : HarnessException(message)
{
    public WorkerRef Worker { get; } = worker;
}

sealed record WorkerStartDetails(string Route, string Agent, string Lineage, string ProjectId, IReadOnlyList<string> Rules);

sealed record WorkerReport(
    string Outcome, string? FinalText, TokenTotals Usage, double? Cost, bool UsageComplete,
    IReadOnlyList<string> RoutesUsed, string? ExecutionError, bool Complete, long? ContextWindow = null,
    IReadOnlyList<TokenTotals>? Calls = null)
{
    public string Outcome { get; } = BackendValues.Outcome(Outcome);
}

abstract record ReattachResult
{
    private ReattachResult() { }

    public sealed record Ended(string Outcome) : ReattachResult
    {
        public string Outcome { get; } = BackendValues.Outcome(Outcome);
    }
    public sealed record Live : ReattachResult;
    public sealed record Interrupted : ReattachResult;
    public sealed record Unreadable(string Reason) : ReattachResult;
}

sealed record BackendCapabilities
{
    public string Sandbox { get; }
    public string Rules { get; }
    public string SchemaOutput { get; }
    public bool Personas { get; }

    public BackendCapabilities(string sandbox, string rules, string schemaOutput, bool personas)
    {
        Sandbox = BackendValues.OneOf(sandbox, nameof(sandbox), "sandbox", "policy");
        Rules = BackendValues.OneOf(rules, nameof(rules), "policy", "approval-gate");
        SchemaOutput = BackendValues.OneOf(schemaOutput, nameof(schemaOutput), "native", "text");
        Personas = personas;
    }
}

sealed record AppliedEnforcement
{
    public string Access { get; }
    public string Network { get; }
    public string Rules { get; }

    public AppliedEnforcement(string access, string network, string rules)
    {
        Access = BackendValues.OneOf(access, nameof(access), "sandbox", "policy", "none");
        Network = BackendValues.OneOf(network, nameof(network), "sandbox", "policy", "none");
        Rules = BackendValues.OneOf(rules, nameof(rules), "policy", "approval-gate");
    }
}

// The core creates and deletes this folder; only the backend writes its fixed profile subfolders.
sealed record BackendContext(string ProfilesFolder, string? Executable, IReadOnlyDictionary<string, string> Passthrough)
{
    public string ProfilesFolder { get; } = Path.IsPathFullyQualified(ProfilesFolder)
        ? ProfilesFolder
        : throw new ArgumentException("profiles folder must be absolute", nameof(ProfilesFolder));
}

sealed record ProbeRequest(string Directory, WorkerRules? CheckRules = null);
sealed record ProbeCheck(string Name, bool Ok, string Detail);
sealed record BackendProbe(bool Ready, string? Version, IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings, IReadOnlyList<ProbeCheck>? Checks = null);
sealed record AnswerResult(bool Confirmed, string? Detail, int? Status = null);
sealed record PollResult(IReadOnlyList<WorkerSignal.Host> Hosts);
sealed record OrphanQuery(string RunId, IReadOnlyList<WorkerRef> KnownWorkers, IReadOnlyList<string> Directories);
sealed record RouteListing(IReadOnlyList<RouteEntry> Routes, IReadOnlyList<AgentEntry> Agents, IReadOnlyList<RouteAllowance> AllowList);
sealed record RouteEntry(RouteRef Route, bool Enabled, IReadOnlyList<string> Variants, string Lineage);
sealed record AgentEntry(string Name, bool Usable, string? Reason, string? Model = null, string? Mode = null, bool Hidden = false, bool Manual = false);
sealed record RouteAllowance(string Entry, string State, string? Reason);

enum OutcomeAction { Settle, Interrupted, KeepState }

// Settle does not imply done: the core still owns validation, scope, verify, and commit checks.
sealed record OutcomeDecision(string? Outcome, OutcomeAction Action, string? Reason = null, bool? WorkerStopped = null, bool Zombie = false);

static class OutcomeMapping
{
    public static OutcomeDecision FromEnded(string outcome, InterruptKind? interrupt = null, bool hardStopSent = false, string? reason = null)
    {
        BackendValues.Outcome(outcome);
        if (interrupt is InterruptKind.Hard || (hardStopSent && interrupt is not (InterruptKind.Cancel or InterruptKind.Timeout)))
            throw new ArgumentException("pass the cancel or timeout that started the stop ladder", nameof(interrupt));

        if (interrupt is InterruptKind.Shutdown) return OnShutdown();
        if (interrupt is not null)
            return new(InterruptOutcome(interrupt.Value), OutcomeAction.Settle, WorkerStopped: hardStopSent ? true : null);

        return outcome == "interrupted"
            ? new(null, OutcomeAction.Interrupted, reason ?? "unknown")
            : new(outcome, OutcomeAction.Settle);
    }

    public static OutcomeDecision FromReattach(ReattachResult result, InterruptKind? interrupt = null, bool hardStopSent = false) => result switch
    {
        // A Codex backend returns Ended(lost), even for a turn previously left interrupted.
        ReattachResult.Ended ended => FromEnded(ended.Outcome, interrupt, hardStopSent),
        ReattachResult.Interrupted => FromEnded("interrupted"),
        ReattachResult.Unreadable unreadable => new("unknown", OutcomeAction.Settle, unreadable.Reason),
        ReattachResult.Live => new(null, OutcomeAction.KeepState),
        _ => throw new ArgumentOutOfRangeException(nameof(result)),
    };

    public static OutcomeDecision AtFinalMark(InterruptKind interrupt, InterruptResult hardStop) =>
        new(InterruptOutcome(interrupt), OutcomeAction.Settle, WorkerStopped: hardStop.Stopped, Zombie: !hardStop.Stopped);

    public static OutcomeDecision OnShutdown() => new(null, OutcomeAction.KeepState);

    static string InterruptOutcome(InterruptKind interrupt) => interrupt switch
    {
        InterruptKind.Cancel => "cancelled",
        InterruptKind.Timeout => "timeout",
        _ => throw new ArgumentException("only cancel or timeout starts a stop ladder", nameof(interrupt)),
    };
}

static class BackendValues
{
    public static string OneOf(string value, string parameter, params string[] allowed) =>
        allowed.Contains(value, StringComparer.Ordinal)
            ? value
            : throw new ArgumentException($"expected one of: {string.Join(", ", allowed)}", parameter);

    public static string NonBlank(string value, string parameter)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameter);
        return value;
    }

    public static string Outcome(string value) => OneOf(value, nameof(value), "succeeded", "failed", "interrupted", "lost");

    public static string Profile(string value, string? reason)
    {
        OneOf(value, nameof(value), "clean", "clean-partial", "full");
        if (value == "clean-partial") ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        else if (reason is not null) throw new ArgumentException("only clean-partial has a profile reason", nameof(reason));
        return value;
    }
}
