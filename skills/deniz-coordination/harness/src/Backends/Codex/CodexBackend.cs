sealed class CodexBackend(BackendContext context, IReadOnlyDictionary<string, string>? environment = null) : IWorkerBackend
{
    public const string PinnedVersion = "0.163.0";
    readonly IReadOnlyDictionary<string, string> environment = environment ?? WorkerEnvironment.Snapshot();
    readonly Channel<WorkerSignal> signals = Channel.CreateUnbounded<WorkerSignal>();
    readonly ConcurrentDictionary<string, Connection> workers = new(StringComparer.Ordinal);
    readonly ConcurrentDictionary<Connection, byte> connections = new();
    readonly ConcurrentDictionary<string, InterruptResult> recovered = new(StringComparer.Ordinal);
    readonly ConcurrentDictionary<string, byte> released = new(StringComparer.Ordinal);
    Connection? probe;
    CodexModel[] models = [];
    (string Actual, string? Reason)? cleanProfile;
    string UserHome => environment.GetValueOrDefault("CODEX_HOME") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");

    public string Name => "codex";
    public BackendCapabilities Capabilities { get; } = new("sandbox", "approval-gate", "native", false);
    public ChannelReader<WorkerSignal> Signals => signals.Reader;
    public string? WindowsSandboxReadiness => probe?.Readiness;
    internal Func<int, Process>? OpenProcess { get; set; }
    internal Func<int, DateTime?>? QueryLimitedStartTime { get; set; }
    internal Func<Func<string, string>?, Dictionary<int, (int Parent, DateTime? CreationTime)>>? ReadSnapshot { get; set; }
    internal IEnumerable<(WorkerRef Worker, CodexProcess Process)> WorkerProcesses => connections.Keys
        .Where(connection => connection.Worker is not null).Select(connection => (connection.Worker!, connection.Process));
    internal bool HasResources => !connections.IsEmpty;
    public List<(WorkerRef Worker, InterruptResult Stop)> CleanupResults { get; } = [];

    public static RouteRef ParseRoute(string text)
    {
        if (text.StartsWith("codex:", StringComparison.Ordinal)) text = text[6..];
        _ = AllowList.Normalize("codex:" + text);
        var parts = text.Split('#');
        return new("codex", parts[0], parts.Length == 2 && parts[1] != "default" ? parts[1] : null);
    }

    async Task<Connection> ProbeConnectionAsync(string directory, CancellationToken ct)
    {
        if (probe is not null) return probe;
        var version = await CodexProcess.CommandAsync(context, directory, environment, version: true, ct);
        if (version.ExitCode != 0 || version.Output.Trim() != $"codex-cli {PinnedVersion}")
            throw new HarnessException($"Codex installed version '{version.Output.Trim()}' differs from pin {PinnedVersion}; install with npm install -g @openai/codex@{PinnedVersion}");
        var login = await CodexProcess.CommandAsync(context, directory, environment, version: false, ct);
        if (login.ExitCode != 0) throw new HarnessException("Codex is missing a login; run codex login. Environment keys are scrubbed unless listed in backends.codex.env");
        var connection = await ConnectAsync(directory, environment, null, ct);
        try
        {
            models = await InspectAsync(connection, ct);
            return probe = connection;
        }
        catch { await DisposeConnectionAsync(connection); throw; }
    }

    static async Task<CodexModel[]> InspectAsync(Connection connection, CancellationToken ct)
    {
        var account = (await connection.Rpc.RequestAsync("account/read", new CodexGetAccountParams(), CodexJson.Wire.CodexGetAccountParams,
            cancellationToken: ct)).Deserialize(CodexJson.Wire.CodexGetAccountResponse)!;
        if (account.Account is null || account.Account.Value.ValueKind == JsonValueKind.Null)
            throw new HarnessException("Codex is signed out; run codex login. Environment keys are scrubbed unless listed in backends.codex.env");
        var catalog = new List<CodexModel>();
        var cursors = new HashSet<string>(StringComparer.Ordinal);
        string? cursor = null;
        do
        {
            var page = (await connection.Rpc.RequestAsync("model/list", new CodexModelListParams(cursor), CodexJson.Wire.CodexModelListParams,
                cancellationToken: ct)).Deserialize(CodexJson.Wire.CodexModelListResponse)!;
            catalog.AddRange(page.Data);
            cursor = page.NextCursor;
            if (cursor is not null && !cursors.Add(cursor)) throw new HarnessException("Codex model/list repeated a cursor");
        } while (cursor is not null);
        if (OperatingSystem.IsWindows())
            connection.Readiness = (await connection.Rpc.RequestAsync("windowsSandbox/readiness", JsonSerializer.Deserialize("null", CodexJson.Wire.JsonElement), CodexJson.Wire.JsonElement,
                cancellationToken: ct)).Deserialize(CodexJson.Wire.CodexWindowsSandboxReadinessResponse)!.Status;
        return catalog.ToArray();
    }

    async Task<(string Actual, string? Reason)> ProbeCleanAsync(string directory, CancellationToken ct)
    {
        if (cleanProfile is { } cached) return cached;
        var home = CodexHome.CreateProbe(context, UserHome, directory, windowsSandbox: CodexHome.ReadWindowsSandbox(UserHome));
        if (!home.Temporary) return (cleanProfile = (home.Profile, home.Reason)).Value;
        try
        {
            var variables = WorkerEnvironment.Copy(environment);
            variables["CODEX_HOME"] = home.Home;
            var connection = await ConnectAsync(directory, variables, home, ct);
            try
            {
                await InspectAsync(connection, ct);
                var reason = !home.MatchesReportedHome(connection.ReportedHome) ? "temporary Codex home marker missing from reported home"
                    : OperatingSystem.IsWindows() && connection.Readiness != "ready" ? $"temporary Windows sandbox is {connection.Readiness}" : null;
                return (cleanProfile = reason is null ? ("clean", null) : ("clean-partial", reason)).Value;
            }
            finally { await DisposeConnectionAsync(connection); }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        { return (cleanProfile = ("clean-partial", $"temporary Codex home probe failed: {ex.Message}")).Value; }
    }

    public async Task<BackendProbe> ProbeAsync(ProbeRequest request, CancellationToken ct)
    {
        await ProbeConnectionAsync(request.Directory, ct);
        return new(true, PinnedVersion, [], [], [new("codex", true, $"Codex {PinnedVersion}; {models.Length} models")]);
    }

    public async Task<IReadOnlyList<LiveCheck>> ValidateAsync(IReadOnlyList<WorkerPlan> tasks, CancellationToken ct)
    {
        if (tasks.Count == 0) return [];
        var connection = await ProbeConnectionAsync(tasks[0].Directory, ct);
        var clean = tasks.Any(x => x.Modes.Profile == "clean") ? await ProbeCleanAsync(tasks[0].Directory, ct) : (Actual: "full", Reason: (string?)null);
        return tasks.Select(task =>
        {
            var errors = new List<string>();
            if (!Constants.Kinds.Contains(task.Kind)) errors.Add($"Codex does not serve kind {task.Kind}");
            var model = models.FirstOrDefault(x => x.Id == task.Route.Model || x.Model == task.Route.Model);
            if (model is null) errors.Add($"route {task.Route.Model} is not offered by Codex");
            else if (task.Route.Variant is { } effort && !model.SupportedReasoningEfforts.Any(x => x.ReasoningEffort == effort))
                errors.Add($"effort {effort} is not offered for {task.Route.Model}");
            if (task.Agent is not null || task.Persona is not null) errors.Add("Codex does not support agents or personas");
            if (OperatingSystem.IsWindows() && task.Modes.Access != "full" && connection.Readiness != "ready")
                errors.Add($"Windows sandbox is {connection.Readiness}; setup is the operator's responsibility");
            // Codex's model catalog lists no context window; its token usage reports one during the run.
            return new LiveCheck(task.TaskId, errors, [], task.Route.Model, task.Modes.Profile == "clean" ? clean.Actual : "full",
                task.Modes.Profile == "clean" ? clean.Reason : null, [$"context window of codex:{task.Route.Model} is known after the run (result.json contextWindow)"]);
        }).ToList();
    }

    async Task<Connection> ConnectAsync(string directory, IReadOnlyDictionary<string, string> variables, CodexHome? home, CancellationToken ct,
        IReadOnlyDictionary<string, string>? configuredEnvironment = null, WorkerLaunch? launch = null)
    {
        CodexProcess process;
        try { process = CodexProcess.Start(context, directory, variables, configuredEnvironment); }
        catch { home?.Dispose(); throw; }
        var connection = new Connection(process, home) { Launch = launch };
        process.DescendantUnobservable = (pid, message) => signals.Writer.TryWrite(new WorkerSignal.Fault(connection.Launch?.TaskId,
            "descendant-unobservable", $"pid={pid}; {message}"));
        connections[connection] = 0;
        connection.Rpc = new(process.Stdin, process.Stdout,
            (request, token) => RequestAsync(connection, request, token),
            protocolError: message => signals.Writer.TryWrite(new WorkerSignal.Fault(connection.Launch?.TaskId, "protocol", message)),
            exitDetail: () => $"exit={process.ExitCode}; stderr: {process.StderrTail}",
            notificationReceived: notification =>
            {
                if (notification.Method != "item/started") return;
                var started = notification.Params.Deserialize(CodexJson.Wire.CodexItemStartedNotification)!;
                connection.Items[(started.ThreadId, started.TurnId, started.Item.Id)] = started.Item;
            });
        try
        {
            var initialized = (await connection.Rpc.RequestAsync("initialize", new CodexInitializeParams(new("dispatch", Constants.HarnessVersion),
                new(false, ["item/agentMessage/delta", "item/reasoning/textDelta", "item/commandExecution/outputDelta"])),
                CodexJson.Wire.CodexInitializeParams, cancellationToken: ct)).Deserialize(CodexJson.Wire.CodexInitializeResponse)!;
            connection.ReportedHome = initialized.CodexHome;
            await connection.Rpc.NotifyAsync("initialized", ct);
            return connection;
        }
        catch (Exception exception)
        {
            await FailedCreationAsync(connection, exception);
            throw;
        }
    }

    WorkerRef Reference(Connection connection) => (connection.Worker ?? new(Name, connection.Launch!.TaskId,
        connection.Process.Id.ToString(CultureInfo.InvariantCulture), null, new())) with { State = State(connection) };

    async Task FailedCreationAsync(Connection connection, Exception exception)
    {
        var stopped = await DisposeConnectionAsync(connection);
        if (!stopped.Stopped && connection.Launch is not null)
        {
            var worker = Reference(connection);
            connection.Worker = worker;
            workers[worker.Id] = connection;
            throw new WorkerCreationException(worker, exception.Message);
        }
    }

    public async Task<CreateResult> CreateAsync(WorkerLaunch launch, CancellationToken ct)
    {
        var sandbox = launch.Modes.Access switch
        {
            "read-only" => "read-only", "workspace" => "workspace-write", "full" => "danger-full-access",
            _ => throw new HarnessException($"unknown Codex access: {launch.Modes.Access}"),
        };
        var userHome = UserHome;
        var home = CodexHome.Create(context, launch, userHome, CodexHome.ReadWindowsSandbox(userHome));
        var variables = WorkerEnvironment.Copy(launch.Environment);
        // Resolve supervisor-launched programs before the configured process overlay applies.
        foreach (var name in new[] { "PATH", "PATHEXT" })
        {
            variables.Remove(name);
            if (environment.TryGetValue(name, out var value)) variables[name] = value;
        }
        variables["CODEX_HOME"] = home.Home;
        Connection connection;
        connection = await ConnectAsync(launch.Directory, variables, home, ct, launch.ConfiguredEnvironment, launch);
        try
        {
            if (home.Temporary && !home.MatchesReportedHome(connection.ReportedHome))
            {
                var fallback = home.Fallback(userHome, "temporary Codex home marker missing from reported home");
                if (!(await DisposeConnectionAsync(connection)).Stopped)
                    throw new WorkerCreationException(Reference(connection), "Codex home fallback stop unconfirmed");
                home = fallback;
                variables["CODEX_HOME"] = home.Home;
                connection = await ConnectAsync(launch.Directory, variables, home, ct, launch.ConfiguredEnvironment, launch);
            }
            connection.Launch = launch;
            var model = models.FirstOrDefault(x => x.Id == launch.Route.Model || x.Model == launch.Route.Model);
            if (launch.Route.Variant is null && model is null)
            {
                string? cursor = null;
                do
                {
                    var page = (await connection.Rpc.RequestAsync("model/list", new CodexModelListParams(cursor), CodexJson.Wire.CodexModelListParams,
                        cancellationToken: ct)).Deserialize(CodexJson.Wire.CodexModelListResponse)!;
                    model = page.Data.FirstOrDefault(x => x.Id == launch.Route.Model || x.Model == launch.Route.Model);
                    cursor = page.NextCursor;
                } while (model is null && cursor is not null);
            }
            connection.Effort = launch.Route.Variant ?? model?.DefaultReasoningEffort
                ?? throw new HarnessException($"Codex model {launch.Route.Model} has no catalog default effort");
            var thread = (await connection.Rpc.RequestAsync("thread/start", new CodexThreadStartParams(launch.Route.Model, launch.Directory,
                sandbox, home.RuntimeConfig),
                CodexJson.Wire.CodexThreadStartParams, cancellationToken: ct)).Deserialize(CodexJson.Wire.CodexThreadStartResponse)!;
            if (thread.Sandbox["type"]?.GetValue<string>() != SandboxPolicy(launch)["type"]!.GetValue<string>())
                throw new HarnessException($"Codex sandbox mismatch: requested {SandboxPolicy(launch)["type"]}, applied {thread.Sandbox["type"]}");
            connection.Worker = new(Name, launch.TaskId, thread.Thread.Id, null, State(connection));
            workers[thread.Thread.Id] = connection;
            connection.Routes.Add($"codex:{thread.Model}#{connection.Effort}");
            return new(connection.Worker, new(launch.Modes.Access == "full" ? "none" : "sandbox",
                launch.Modes.Access == "full" ? "none" : "sandbox", "approval-gate"),
                new($"codex:{thread.Model}#{connection.Effort}", "", thread.Model, "", []));
        }
        catch (Exception exception)
        {
            await FailedCreationAsync(connection, exception);
            throw;
        }
    }

    static JsonObject SandboxPolicy(WorkerLaunch launch) => launch.Modes.Access switch
    {
        "read-only" => new() { ["type"] = "readOnly", ["networkAccess"] = launch.Modes.Network == "on" },
        "workspace" => new()
        {
            ["type"] = "workspaceWrite", ["networkAccess"] = launch.Modes.Network == "on",
            ["writableRoots"] = Json.Array([launch.Directory, .. launch.Environment.TryGetValue("TEMP", out var temp) ? [temp] : Array.Empty<string>(),
                .. launch.WritableRoots ?? []]),
        },
        "full" => new() { ["type"] = "dangerFullAccess" },
        _ => throw new HarnessException($"unknown Codex access: {launch.Modes.Access}"),
    };

    static JsonObject State(Connection connection) => new()
    {
        ["pid"] = connection.Process.Id, ["processStartTime"] = connection.Process.StartTime.ToString("O", CultureInfo.InvariantCulture),
        ["linuxBootId"] = connection.Process.Identity.LinuxBootId, ["linuxStartTicks"] = connection.Process.Identity.LinuxStartTicks,
        ["processGroup"] = connection.Process.ProcessGroup is not null, ["codexHome"] = connection.Home?.Home,
        ["codexHomeTemporary"] = connection.Home?.Temporary,
        ["codexHomeMarkerName"] = connection.Home?.MarkerName, ["codexHomeMarkerText"] = connection.Home?.MarkerText,
        ["profile"] = connection.Home?.Profile, ["profileReason"] = connection.Home?.Reason,
        ["descendants"] = new JsonArray(connection.Process.Descendants.Select(identity => (JsonNode?)new JsonObject
        {
            ["pid"] = identity.Pid, ["startTime"] = identity.StartTime?.ToString("O", CultureInfo.InvariantCulture),
            ["linuxBootId"] = identity.LinuxBootId, ["linuxStartTicks"] = identity.LinuxStartTicks,
        }).ToArray()),
    };

    static void SaveState(Connection connection, WorkerRef worker)
    {
        lock (connection.StateGate)
            foreach (var (name, value) in State(connection)) worker.State[name] = value?.DeepClone();
    }

    public async Task<WorkerRef> PromptAsync(WorkerRef worker, WorkerPrompt prompt, CancellationToken ct)
    {
        if (prompt.IsContinue) throw new HarnessException("Codex tasks cannot be continued; use a new run id");
        var connection = workers[worker.Id];
        var launch = connection.Launch!;
        var turn = (await connection.Rpc.RequestAsync("turn/start", new CodexTurnStartParams(worker.Id, [new(prompt.Text)], launch.Directory,
            SandboxPolicy(launch), connection.Effort, launch.Kind == "structured" ? launch.OutputSchema : null),
            CodexJson.Wire.CodexTurnStartParams, cancellationToken: ct)).Deserialize(CodexJson.Wire.CodexTurnStartResponse)!;
        connection.Worker = worker with { Turn = turn.Turn.Id };
        connection.Admitted.TrySetResult(connection.Worker);
        connection.Pump = PumpAsync(connection);
        return connection.Worker;
    }

    async Task PumpAsync(Connection connection)
    {
        var worker = connection.Worker!;
        try
        {
            await foreach (var notification in connection.Rpc.Notifications.ReadAllAsync())
            {
                try
                {
                    var data = notification.Params;
                    var thread = data.TryGetProperty("threadId", out var threadId) ? threadId.GetString() : null;
                    var taskThread = thread == worker.Id;
                    var taskTurn = taskThread && data.TryGetProperty("turnId", out var turnId) && turnId.GetString() == worker.Turn;
                    if (taskThread && (data.TryGetProperty("turnId", out var eventTurn) && eventTurn.GetString() != worker.Turn
                        || data.TryGetProperty("turn", out var nestedTurn) && nestedTurn.GetProperty("id").GetString() != worker.Turn))
                        continue;
                    switch (notification.Method)
                    {
                        case "item/started":
                        case "item/completed":
                            var item = notification.Method == "item/started"
                                ? data.Deserialize(CodexJson.Wire.CodexItemStartedNotification)!.Item
                                : data.Deserialize(CodexJson.Wire.CodexItemCompletedNotification)!.Item;
                            if (notification.Method == "item/completed" && taskTurn && item.Type == "agentMessage")
                            {
                                lock (connection.StateGate)
                                {
                                    connection.LastText = item.Text;
                                    if (item.Phase == "final_answer") connection.FinalText = item.Text;
                                }
                            }
                            if (item.Type is "commandExecution" or "fileChange" && (!taskThread || taskTurn))
                            {
                                var start = notification.Method == "item/started";
                                var raw = data.GetProperty("item");
                                var outcome = raw.TryGetProperty("status", out var status) ? status.GetString() : null;
                                var failed = raw.TryGetProperty("exitCode", out var exit) && exit.ValueKind == JsonValueKind.Number && exit.GetInt32() != 0;
                                var resource = item.Command ?? (item.Changes is { ValueKind: JsonValueKind.Array } changes
                                    ? string.Join(", ", changes.EnumerateArray().Select(x => x.GetProperty("path").GetString())) : "");
                                var timestamp = data.TryGetProperty(start ? "startedAtMs" : "completedAtMs", out var time) ? time.GetInt64() : 0;
                                signals.Writer.TryWrite(new WorkerSignal.ToolCall(worker.TaskId, worker.Turn!, $"{thread}/{item.Id}",
                                    item.Type == "commandExecution" ? "shell" : "edit", resource, start ? "start" : "end",
                                    start ? null : failed || outcome is "failed" or "declined" ? "failed" : outcome == "interrupted" ? "interrupted" : "succeeded",
                                    DateTimeOffset.FromUnixTimeMilliseconds(timestamp)));
                            }
                            break;
                        case "thread/tokenUsage/updated":
                            var usage = data.Deserialize(CodexJson.Wire.CodexThreadTokenUsageUpdatedNotification)!;
                            lock (connection.StateGate)
                            {
                                var changed = !connection.Usage.TryGetValue(usage.ThreadId, out var previous) || previous != usage.TokenUsage.Total;
                                connection.Usage[usage.ThreadId] = usage.TokenUsage.Total;
                                connection.UsageComplete |= taskTurn;
                                if (taskThread && usage.TokenUsage.ModelContextWindow is { } contextWindow)
                                    connection.ContextWindow = contextWindow;
                                if (taskTurn && changed && usage.TokenUsage.Last is { } last)
                                    connection.Calls.Add(new() { Input = last.InputTokens, CacheRead = last.CachedInputTokens,
                                        Output = last.OutputTokens, Reasoning = last.ReasoningOutputTokens });
                            }
                            break;
                        case "model/rerouted":
                            var model = data.GetProperty("toModel").GetString();
                            lock (connection.StateGate) connection.Routes.Add($"codex:{model}#{connection.Effort}");
                            break;
                        case "error":
                            var error = data.Deserialize(CodexJson.Wire.CodexErrorNotification)!;
                            if (taskTurn && error.WillRetry)
                                signals.Writer.TryWrite(new WorkerSignal.Retry(worker.TaskId, worker.Turn!, ++connection.Retries, error.Error.Message, null, null, null));
                            else signals.Writer.TryWrite(new WorkerSignal.Fault(worker.TaskId, "error", $"{thread}: {error.Error.Message}"));
                            break;
                        case "serverRequest/resolved":
                            CloseAsk(connection, data.GetProperty("requestId").GetRawText());
                            break;
                        case "turn/completed":
                            var completed = data.Deserialize(CodexJson.Wire.CodexTurnCompletedNotification)!;
                            var execution = completed.Turn.Status switch { "completed" => "succeeded", "interrupted" => "interrupted", _ => "failed" };
                            if (taskThread && completed.Turn.Id == worker.Turn)
                            {
                                connection.Outcome = execution;
                                connection.ExecutionError = completed.Turn.Error is { } failure
                                    ? $"{failure.Message}; {failure.CodexErrorInfo}; {failure.AdditionalDetails}" : null;
                                signals.Writer.TryWrite(new WorkerSignal.Status(worker.TaskId, worker.Turn!, execution,
                                    execution == "failed" ? new JsonObject { ["message"] = connection.ExecutionError }.ToJsonString() : connection.ExecutionError));
                                connection.TurnEnded.TrySetResult();
                                // A process that survives the stop can hold stdout open, so this loop may never
                                // reach its finally: report the unconfirmed stop here.
                                var stop = await connection.Process.StopAsync();
                                connection.StopResult = stop;
                                if (!stop.Stopped) signals.Writer.TryWrite(new WorkerSignal.StopUnconfirmed(worker.TaskId, worker.Turn!, execution, stop.Detail));
                            }
                            else if (!taskThread)
                                signals.Writer.TryWrite(new WorkerSignal.Status(worker.TaskId, worker.Turn!, execution,
                                    new JsonObject { ["threadId"] = thread, ["turnId"] = completed.Turn.Id }.ToJsonString()));
                            break;
                        case "warning":
                        case "configWarning":
                        case "windows/worldWritableWarning":
                            signals.Writer.TryWrite(new WorkerSignal.Fault(worker.TaskId, notification.Method, data.ToString()));
                            break;
                    }
                }
                catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
                { signals.Writer.TryWrite(new WorkerSignal.Fault(worker.TaskId, "notification", ex.Message)); }
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        { connection.ExecutionError = ex.Message; }
        finally
        {
            var stopped = await connection.Process.StopAsync();
            connection.StopResult = stopped;
            connection.Stopped = stopped.Stopped;
            if (connection.Outcome is null)
            {
                connection.Outcome = "lost";
                connection.ExecutionError = stopped.Detail;
            }
            foreach (var key in connection.Pending.Keys) CloseAsk(connection, key);
            if (stopped.Stopped)
            {
                connection.Ended = new(worker.TaskId, worker.Turn!, connection.Outcome, connection.ExecutionError);
                signals.Writer.TryWrite(connection.Ended);
            }
            else signals.Writer.TryWrite(new WorkerSignal.StopUnconfirmed(worker.TaskId, worker.Turn!, connection.Outcome, stopped.Detail));
        }
    }

    async Task<CodexReply> RequestAsync(Connection connection, CodexServerRequest request, CancellationToken ct)
    {
        if (request.Method is "execCommandApproval" or "applyPatchApproval")
        {
            signals.Writer.TryWrite(new WorkerSignal.Fault(connection.Launch?.TaskId, "request", $"unexpected legacy request: {request.Method}"));
            return CodexReply.Success(new CodexLegacyApprovalResponse(new(new(Policy.LeadDeclined))), CodexJson.Wire.CodexLegacyApprovalResponse);
        }
        var form = request.Method is "item/tool/requestUserInput" or "mcpServer/elicitation/request";
        if (!form && request.Method is not ("item/commandExecution/requestApproval" or "item/fileChange/requestApproval" or "item/permissions/requestApproval"))
        {
            signals.Writer.TryWrite(new WorkerSignal.Fault(connection.Launch?.TaskId, "request", $"unexpected request: {request.Method}"));
            return new(null, new(-32601, $"method not found: {request.Method}"));
        }
        var worker = await connection.Admitted.Task.WaitAsync(ct);
        var data = request.Params;
        var thread = data.GetProperty("threadId").GetString()!;
        if (!form && thread == worker.Id && data.GetProperty("turnId").GetString() != worker.Turn)
            return request.Method switch
            {
                "item/commandExecution/requestApproval" => CodexReply.Success(new CodexCommandExecutionRequestApprovalResponse(CodexApprovalDecision.Decline), CodexJson.Wire.CodexCommandExecutionRequestApprovalResponse),
                "item/fileChange/requestApproval" => CodexReply.Success(new CodexFileChangeRequestApprovalResponse(CodexApprovalDecision.Decline), CodexJson.Wire.CodexFileChangeRequestApprovalResponse),
                _ => CodexReply.Success(new CodexPermissionsRequestApprovalResponse(new()), CodexJson.Wire.CodexPermissionsRequestApprovalResponse),
            };
        var key = request.Id.GetRawText();
        var blocker = new BlockerRef(Name, worker.Id, thread == worker.Id ? null : thread, key);
        var held = new HeldRequest(request, blocker);
        lock (connection.RequestGate)
        {
            if (connection.Resolved.Contains(key))
            {
                request.Resolve();
                return new(null);
            }
            if (!connection.Pending.TryAdd(key, held)) return new(null, new(-32600, "duplicate request id"));
            if (form)
                signals.Writer.TryWrite(new WorkerSignal.Form(worker.TaskId, worker.Turn!, blocker,
                    request.Method == "item/tool/requestUserInput" ? "User input requested" : data.GetProperty("message").GetString() ?? "MCP input requested"));
            else
            {
                connection.Items.TryGetValue((thread, data.GetProperty("turnId").GetString()!, data.GetProperty("itemId").GetString()!), out var item);
                var action = request.Method switch { "item/commandExecution/requestApproval" => "shell", "item/fileChange/requestApproval" => "edit", _ => "permissions" };
                var resources = action switch
                {
                    "shell" => new[] { data.TryGetProperty("command", out var command) && command.ValueKind == JsonValueKind.String ? command.GetString()! : item?.Command ?? "" },
                    "edit" => item?.Changes is { ValueKind: JsonValueKind.Array } changes
                        ? changes.EnumerateArray().Select(x => x.GetProperty("path").GetString()!).ToArray()
                        : new[] { data.TryGetProperty("grantRoot", out var root) && root.ValueKind == JsonValueKind.String ? root.GetString()! : "" },
                    _ => PermissionResources(data.GetProperty("permissions")).ToArray(),
                };
                signals.Writer.TryWrite(new WorkerSignal.Ask(worker.TaskId, worker.Turn!, blocker, action, resources));
            }
        }
        return await held.Reply.Task.WaitAsync(ct);
    }

    static IEnumerable<string> PermissionResources(JsonElement value, string path = "")
    {
        if (value.ValueKind == JsonValueKind.String)
        {
            var resource = value.GetString()!;
            yield return resource;
            // Keep paths matchable while retaining the access intent for the core's ceiling.
            if (path == "fileSystem.write") yield return "fileSystem.write:" + resource;
        }
        else if (value.ValueKind == JsonValueKind.Object)
        {
            if (path == "fileSystem.entries" && value.TryGetProperty("access", out var access) && access.GetString() == "write")
                yield return "fileSystem.write:" + value.GetProperty("path").GetRawText();
            foreach (var property in value.EnumerateObject())
                foreach (var resource in PermissionResources(property.Value, path.Length == 0 ? property.Name : path + "." + property.Name)) yield return resource;
        }
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var element in value.EnumerateArray())
                foreach (var resource in PermissionResources(element, path)) yield return resource;
    }

    void CloseAsk(Connection connection, string key)
    {
        lock (connection.RequestGate)
        {
            connection.Resolved.Add(key);
            if (!connection.Pending.TryRemove(key, out var held)) return;
            held.Request.Resolve();
            held.Reply.TrySetCanceled();
            var worker = connection.Worker!;
            signals.Writer.TryWrite(new WorkerSignal.AskClosed(worker.TaskId, worker.Turn!, key, "gone", null));
        }
    }

    public async Task<AnswerResult> AnswerAsync(BlockerRef blocker, BlockerAnswer answer, CancellationToken ct)
    {
        if (blocker.Backend != Name || !workers.TryGetValue(blocker.WorkerId, out var connection)
            || !connection.Pending.TryGetValue(blocker.AskId, out var held) || held.Blocker != blocker)
            return new(false, "unknown request");
        var request = held.Request;
        var accept = answer is BlockerAnswer.Once;
        var message = answer switch { BlockerAnswer.Reject reject => reject.Message, BlockerAnswer.Cancel cancel => cancel.Message, _ => null };
        CodexReply reply;
        switch (request.Method)
        {
            case "item/commandExecution/requestApproval":
                reply = CodexReply.Success(new CodexCommandExecutionRequestApprovalResponse(accept ? CodexApprovalDecision.Accept : CodexApprovalDecision.Decline), CodexJson.Wire.CodexCommandExecutionRequestApprovalResponse);
                break;
            case "item/fileChange/requestApproval":
                reply = CodexReply.Success(new CodexFileChangeRequestApprovalResponse(accept ? CodexApprovalDecision.Accept : CodexApprovalDecision.Decline), CodexJson.Wire.CodexFileChangeRequestApprovalResponse);
                break;
            case "item/permissions/requestApproval":
                reply = CodexReply.Success(new CodexPermissionsRequestApprovalResponse(accept ? JsonNode.Parse(request.Params.GetProperty("permissions").GetRawText())!.AsObject() : new()), CodexJson.Wire.CodexPermissionsRequestApprovalResponse);
                break;
            case "item/tool/requestUserInput":
                var questions = request.Params.Deserialize(CodexJson.Wire.CodexToolRequestUserInputParams)!.Questions;
                reply = CodexReply.Success(new CodexToolRequestUserInputResponse(questions.ToDictionary(x => x.Id, _ => new CodexUserInputAnswer([message ?? Policy.NoHuman]))), CodexJson.Wire.CodexToolRequestUserInputResponse);
                break;
            default:
                reply = CodexReply.Success(new CodexMcpServerElicitationRequestResponse(), CodexJson.Wire.CodexMcpServerElicitationRequestResponse);
                break;
        }
        if (!held.Reply.TrySetResult(reply)) return new(false, "request already answered or closed");
        var confirmed = await request.ResponseWritten.Task.WaitAsync(ct);
        connection.Pending.TryRemove(blocker.AskId, out _);
        if (!confirmed) return new(false, "app-server response write failed");
        if (!accept && message is not null && request.Method.EndsWith("/requestApproval", StringComparison.Ordinal))
        {
            try
            {
                await connection.Rpc.RequestAsync("turn/steer", new CodexTurnSteerParams(request.Params.GetProperty("threadId").GetString()!,
                    request.Params.GetProperty("turnId").GetString()!, [new("The approval request was rejected. Rejection message:\n" + message)]), CodexJson.Wire.CodexTurnSteerParams, cancellationToken: ct);
            }
            catch (Exception ex) when (ex is CodexRpcException or IOException or TimeoutException)
            { return new(true, $"decline sent; steer failed: {ex.Message}"); }
        }
        return new(true, request.Method switch { "item/tool/requestUserInput" => "no-human-answers", "mcpServer/elicitation/request" => "decline", _ => accept ? "accept" : "decline" });
    }
    public async Task<InterruptResult> InterruptAsync(WorkerRef worker, InterruptKind kind, CancellationToken ct)
    {
        if (!workers.TryGetValue(worker.Id, out var connection))
            return kind is InterruptKind.Hard or InterruptKind.Shutdown ? await StopRecordedAsync(worker, ct)
                : new(false, "no live connection: a recorded worker stops only by a hard stop");
        if (kind is InterruptKind.Hard or InterruptKind.Shutdown) return await connection.Process.StopAsync();
        await connection.Rpc.RequestAsync("turn/interrupt", new CodexTurnInterruptParams(worker.Id, worker.Turn!), CodexJson.Wire.CodexTurnInterruptParams, cancellationToken: ct);
        return new(false, "turn/interrupt sent");
    }
    public Task<PollResult> PollAsync(IReadOnlyList<WorkerRef> live, CancellationToken ct)
    {
        foreach (var worker in live)
        {
            ct.ThrowIfCancellationRequested();
            if (!workers.TryGetValue(worker.Id, out var connection)) continue;
            connection.Process.RefreshDescendants();
            SaveState(connection, worker);
            if (connection.Ended is { } ended) signals.Writer.TryWrite(ended);
        }
        return Task.FromResult(new PollResult([]));
    }
    public async Task<WorkerReport> CollectAsync(WorkerRef worker, CancellationToken ct)
    {
        if (released.ContainsKey(worker.Id)) throw new HarnessException($"Codex worker {worker.Id} was released and cannot be collected");
        if (!workers.TryGetValue(worker.Id, out var connection))
        {
            if (await ReattachAsync(worker, ct) is ReattachResult.Unreadable unreadable) throw new HarnessException(unreadable.Reason);
            var recovery = recovered[worker.Id];
            return new("lost", null, new(), null, false, [], recovery.Detail, recovery.Stopped, StopResult: recovery);
        }
        // Prefer the pump's end, which records the stop. A process that survives the stop can keep the
        // pump open after the turn ended: then the turn's report is complete without it.
        await Task.WhenAny(connection.Pump, Task.WhenAll(connection.TurnEnded.Task, Task.Delay(TimeSpan.FromSeconds(10), ct))).WaitAsync(ct);
        SaveState(connection, worker);
        lock (connection.StateGate)
        {
            var error = connection.ExecutionError;
            if (connection.Pump.Exception is { } pumpError)
                error = error is null ? pumpError.ToString() : $"{error}\n{pumpError}";
            var usage = connection.Usage.Values;
            var totals = usage.Count == 0 ? new TokenTotals() : new TokenTotals
            {
                Input = usage.Sum(x => x.InputTokens), Output = usage.Sum(x => x.OutputTokens), Reasoning = usage.Sum(x => x.ReasoningOutputTokens),
                CacheRead = usage.Sum(x => x.CachedInputTokens), CacheWrite = usage.Sum(x => x.CacheWriteInputTokens),
            };
            return new(connection.Outcome ?? "lost", connection.FinalText ?? connection.LastText, totals, null, connection.UsageComplete,
                connection.Routes.ToArray(), error, connection.Stopped, ContextWindow: connection.ContextWindow, Calls: connection.Calls.ToArray(), StopResult: connection.StopResult);
        }
    }
    public async Task<ReattachResult> ReattachAsync(WorkerRef worker, CancellationToken ct)
    {
        if (!recovered.ContainsKey(worker.Id))
        {
            var stopped = workers.TryGetValue(worker.Id, out var connection) ? await connection.Process.StopAsync()
                : await StopRecordedAsync(worker, ct);
            if (stopped.Stopped && connection is not null)
            {
                await DisposeConnectionAsync(connection);
                workers.TryRemove(worker.Id, out _);
            }
            if (stopped.Stopped) CodexHome.DeleteRecorded(context, worker);
            recovered[worker.Id] = stopped;
        }
        return new ReattachResult.Ended("lost", recovered[worker.Id]);
    }
    public async Task<InterruptResult?> ReleaseAsync(WorkerRef worker, CancellationToken ct)
    {
        if (released.ContainsKey(worker.Id)) return recovered[worker.Id];
        InterruptResult stopped;
        if (workers.TryGetValue(worker.Id, out var connection))
        {
            stopped = await DisposeConnectionAsync(connection);
            SaveState(connection, worker);
            if (stopped.Stopped) workers.TryRemove(worker.Id, out _);
        }
        else
        {
            stopped = recovered.TryGetValue(worker.Id, out var previous) && previous.Stopped ? previous : await StopRecordedAsync(worker, ct);
            if (stopped.Stopped) CodexHome.DeleteRecorded(context, worker);
        }
        recovered[worker.Id] = stopped;
        if (stopped.Stopped) released[worker.Id] = 0;
        return stopped;
    }
    public Task<IReadOnlyList<WorkerRef>> FindOrphansAsync(OrphanQuery query, CancellationToken ct) => Task.FromResult<IReadOnlyList<WorkerRef>>([]);
    Task<InterruptResult> StopRecordedAsync(WorkerRef worker, CancellationToken ct) =>
        CodexProcess.StopRecordedAsync(worker.State, ct, OpenProcess, QueryLimitedStartTime, readSnapshot: ReadSnapshot);
    public async Task<RouteListing> ListRoutesAsync(string directory, CancellationToken ct)
    {
        await ProbeConnectionAsync(directory, ct);
        return new(models.Select(model => new RouteEntry(new("codex", model.Model, null), true,
            model.SupportedReasoningEfforts.Select(x => x.ReasoningEffort).ToList(), model.Model)).ToList(), [], []);
    }
    public async Task<IReadOnlyList<(WorkerRef Worker, InterruptResult Stop)>> CleanupAsync()
    {
        var failures = new List<Exception>();
        CleanupResults.Clear();
        foreach (var connection in connections.Keys)
        {
            try
            {
                var stopped = await DisposeConnectionAsync(connection);
                var worker = connection.Launch is null ? null : Reference(connection);
                if (worker is not null) CleanupResults.Add((worker, stopped));
                if (!stopped.Stopped) continue;
                if (connection.Worker is { } releasedWorker) workers.TryRemove(releasedWorker.Id, out _);
                if (ReferenceEquals(probe, connection)) probe = null;
            }
            catch (Exception ex) { failures.Add(ex); }
        }
        if (failures.Count > 0) throw new AggregateException("Codex cleanup unconfirmed", failures);
        if (!HasResources) signals.Writer.TryComplete();
        return CleanupResults;
    }

    public async ValueTask DisposeAsync() => await CleanupAsync();

    async Task<InterruptResult> DisposeConnectionAsync(Connection connection)
    {
        var stopped = await connection.CleanupAsync();
        if (stopped.Stopped) connections.TryRemove(connection, out _);
        return stopped;
    }

    sealed class Connection(CodexProcess process, CodexHome? home) : IAsyncDisposable
    {
        public CodexProcess Process { get; } = process;
        public CodexHome? Home { get; } = home;
        public CodexRpc Rpc { get; set; } = null!; // Assigned before any asynchronous initialization.
        public WorkerLaunch? Launch { get; set; }
        public WorkerRef? Worker { get; set; }
        public object StateGate { get; } = new();
        public object RequestGate { get; } = new();
        public HashSet<string> Resolved { get; } = new(StringComparer.Ordinal);
        public TaskCompletionSource<WorkerRef> Admitted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ConcurrentDictionary<string, HeldRequest> Pending { get; } = new(StringComparer.Ordinal);
        public ConcurrentDictionary<(string Thread, string Turn, string Item), CodexItem> Items { get; } = new();
        public string ReportedHome { get; set; } = "";
        public string? Readiness { get; set; }
        public string? Outcome { get; set; }
        public string? FinalText { get; set; }
        public string? LastText { get; set; }
        public string? ExecutionError { get; set; }
        public bool Stopped { get; set; }
        public InterruptResult? StopResult { get; set; }
        public WorkerSignal.Ended? Ended { get; set; }
        public int Retries { get; set; }
        public Dictionary<string, CodexTokenUsageBreakdown> Usage { get; } = new(StringComparer.Ordinal);
        public List<TokenTotals> Calls { get; } = [];
        public bool UsageComplete { get; set; }
        public long? ContextWindow { get; set; }
        public string Effort { get; set; } = "";
        public HashSet<string> Routes { get; } = new(StringComparer.Ordinal);
        public Task Pump { get; set; } = Task.CompletedTask;
        public TaskCompletionSource TurnEnded { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        bool disposed;
        public async Task<InterruptResult> CleanupAsync()
        {
            if (disposed) return StopResult!;
            var stopped = await Process.StopAsync();
            StopResult = stopped;
            if (!stopped.Stopped) return stopped;
            await Rpc.DisposeAsync();
            await Pump;
            Home?.Dispose();
            await Process.CleanupAsync();
            disposed = true;
            return stopped;
        }
        public async ValueTask DisposeAsync() => await CleanupAsync();
    }

    sealed record HeldRequest(CodexServerRequest Request, BlockerRef Blocker)
    {
        public TaskCompletionSource<CodexReply> Reply { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
