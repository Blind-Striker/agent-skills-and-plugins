sealed class TaskStarter(IWorkerBackend backend, Ledger ledger, string repo, AllowList allow, Policy policy, Git? git = null, TimeProvider? timeProvider = null,
    Func<TaskRun, string, IReadOnlyDictionary<string, string>, CancellationToken, Task<(VerifyResult Result, string Log, Exception? Error)>>? prepareCommand = null,
    Func<Git, string, string, bool, CancellationToken, Task<string>>? prepareSnapshot = null,
    Func<Git, string, string, bool, CancellationToken, Task<string>>? prepareHead = null)
{
    public static Modes ModesFor(TaskRecord record)
    {
        var access = record.Settings.Access?.Value ?? record.Spec.Access
            ?? (record.Spec.Kind is "review" or "research" or "structured" ? "read-only" : "workspace");
        var clean = record.Spec.Kind == "structured"
            || (record.Settings.Backend?.Value ?? record.Spec.Backend) == "codex" && access == "read-only";
        return new(record.Settings.Profile?.Value ?? record.Spec.Profile ?? (clean ? "clean" : "full"),
            record.Settings.Project?.Value ?? record.Spec.Project ?? (record.Spec.Kind == "structured" ? "off" : "on"),
            access, record.Settings.Network?.Value ?? "on", record.Settings.Workspace?.Value ?? "worktree",
            record.Settings.ReadOnlyShell?.Value ?? record.Spec.ReadOnlyShell ?? "decline");
    }

    public static WorkerRules RulesFor(Policy policy) => new(
        policy.Deny.Select(Convert).ToList(), policy.Escalate.Select(Convert).ToList());

    static EffectiveRule Convert(PolicyRule rule) => new() { Name = rule.Name!, Action = rule.Action!, Resources = rule.Resources! };

    public static (WorkerLaunch Launch, List<string> Removed) BuildLaunch(TaskRecord record, string runId, string backendName, Policy policy, string? tempRoot)
    {
        RouteRef? route;
        if (backendName == "codex") route = CodexBackend.ParseRoute(record.Spec.Route!);
        else RouteRef.TryParse(record.Spec.Route ?? "agent/placeholder", out route, out _);
        var (variables, removed) = WorkerEnvironment.Environment(record.Spec.Id!, runId, tempRoot);
        return (new(runId, record.Spec.Id!, record.Spec.Kind!, route!, record.Spec.Agent,
            record.Spec.Persona, ModesFor(record), record.Workspace.Path!, RulesFor(policy), variables,
            record.Spec.Kind == "structured" ? record.PromptInputs?.Schema as JsonObject : null,
            record.Settings.Profile?.Actual ?? record.Settings.Profile?.Value ?? "full", record.Settings.Profile?.Reason,
            record.Settings.WritableRoots?.Value), removed);
    }

    public void CheckAllowed(TaskRun run)
    {
        var spec = run.Record.Spec;
        if (spec.Agent is { } agent)
        {
            if (!allow.AllowsAgent(agent)) throw new HarnessException($"agent {agent} left the allow-list");
        }
        else if (!allow.Allows(run.Record.Settings.Route?.Value ?? $"{backend.Name}:{spec.Route}"))
            throw new HarnessException($"route {spec.Route} left the allow-list");
    }

    public async Task<TaskResult?> StartAsync(TaskRun run, CancellationToken ct)
    {
        if (run.Record.Spec.Kind == "structured" && run.Record.PromptInputs is null)
        {
            if (run.Record.Contracts is { Count: > 0 })
                throw new HarnessException("original contract text is unavailable; cannot start an unprompted task from the ledger");
            run.Record.PromptInputs = StructuredOutput.Load(ledger, run);
        }
        if (Scratch.Required(run.Record.Spec)) Scratch.Prepare(run, ledger);
        else if (run.Record.Spec.Kind == "structured")
        {
            run.Record.Workspace.Path ??= Path.GetFullPath(run.Record.Spec.Dir ?? ".", repo);
            ledger.SaveRecord(run.Record);
        }
        else await Worktree.PrepareAsync(run, ledger, repo, ct, git);
        CheckAllowed(run);
        if (await PrepareAsync(run, ct) is { } terminal) return terminal;
        var record = run.Record;
        var (launch, removed) = BuildLaunch(record, ledger.RunId, backend.Name, policy, record.Settings.TempRoot?.Value);
        var created = await backend.CreateAsync(launch, ct);
        // Persist the reference before calling PromptAsync, even if that call fails or never returns.
        run.SaveWorker(created.Worker, ledger);
        if (backend.Name == "codex")
        {
            if (record.Settings.Access is { } access) access.Enforcement = created.Applied.Access;
            if (record.Settings.Network is { } network) network.Enforcement = created.Applied.Network;
        }
        if (backend.Name == "codex" && record.Settings.Profile is { } profile)
        {
            profile.Actual = created.Worker.State["profile"]?.GetValue<string>() ?? profile.Value;
            profile.Reason = created.Worker.State["profileReason"]?.GetValue<string>();
        }
        if (created.Details is { } details)
        {
            record.Settings.Route ??= new Setting<string> { Value = details.Route, From = "task" };
            record.Agent = details.Agent;
            record.Lineage = details.Lineage;
            if (record.Spec.Agent is not null && backend.Name == "opencode") record.RouteResolved = details.Route;
            ledger.SaveRecord(record);
            ledger.Write(run.Id, "route", new JsonObject { ["route"] = record.Settings.Route.Value, ["agent"] = record.Agent, ["lineage"] = record.Lineage });
            ledger.Write(run.Id, "session", new JsonObject { ["sessionId"] = created.Worker.Id, ["projectId"] = details.ProjectId, ["rules"] = Json.Array(details.Rules) });
        }
        ledger.Write(run.Id, "environment", new JsonObject { ["variables"] = launch.Environment.Count, ["removed"] = Json.Array(removed) });
        if (record.PromptInputs is null && record.Contracts is { Count: > 0 })
            throw new HarnessException("original contract text is unavailable; cannot start an unprompted task from the ledger");
        var text = policy.Render(new PlannedOrRecord(record.Spec, record.PromptText), record.Workspace.Path!, record.PromptInputs);
        run.PromptAttempted = true;
        var prompted = await backend.PromptAsync(created.Worker, new(text, false), ct);
        run.SaveWorker(prompted, ledger);
        var data = Prompt.Event(text, prompted.Turn!, record.PromptInputs, record.Spec.RawPrompt == true);
        data["promptId"] = prompted.Turn;
        ledger.Write(run.Id, "prompt", data);
        run.Deadline = (timeProvider ?? TimeProvider.System).GetUtcNow().AddMinutes(record.Spec.TimeoutMinutes ?? Constants.DefaultTimeoutMinutes);
        run.State = TaskState.Running;
        return null;
    }

    async Task<TaskResult?> PrepareAsync(TaskRun run, CancellationToken ct)
    {
        var record = run.Record;
        if (record.Spec.Prepare is not { Count: > 0 } commands || record.PrepareDone == true || record.Session.Id is not null) return null;
        var cancellation = new CancellationTokenSource();
        using var commandCancellation = CancellationTokenSource.CreateLinkedTokenSource(ct, cancellation.Token);
        record.Prepare = [];
        record.PrepareRunning = true;
        var logPath = Path.Combine(ledger.Dir, run.Id, "prepare.log");
        Git? runner = null;
        var snapshot = prepareSnapshot ?? Snapshot.CaptureAsync;
        var indexTask = run.Id + "-prepare";
        bool checkFailed = false, commandRunning = false;
        async Task<string> Check(Func<Task<string>> operation)
        {
            try { return await operation(); }
            catch { checkFailed = true; throw; }
        }
        bool Cancelled() => run.CancelRequested || cancellation.IsCancellationRequested;
        TaskResult End(string? code = null, string? error = null, bool? checkFailed = null, bool changed = false)
        {
            ct.ThrowIfCancellationRequested();
            var cancelled = Cancelled();
            var data = new JsonObject { ["commands"] = record.Prepare.Count, ["failed"] = !cancelled && code == "prepare-failed" && checkFailed is null ? 1 : 0 };
            if (cancelled) data["cancelled"] = true;
            else if (checkFailed is { } failedCheck) data["checkFailed"] = failedCheck;
            else if (changed) data["changed"] = true;
            ledger.SaveRecord(record);
            ledger.Write(run.Id, "prepared", data);
            return new TaskResult { Task = run.Id, Run = ledger.RunId, Kind = record.Spec.Kind, Backend = backend.Name,
                State = cancelled ? "cancelled" : "failed", Outcome = cancelled ? "cancelled" : null,
                CancelRequested = cancelled, Prepare = record.Prepare, Failures = cancelled ? [] : [code!], Error = cancelled ? null : error };
        }
        try
        {
            run.PrepareCancellation = cancellation;
            ledger.SaveRecord(record);
            await File.WriteAllTextAsync(logPath, "", ct);
            runner = (git ?? Worktree.Runner(ledger, run.Id)).ForWorkspace(ledger, run.Id);
            if (Cancelled()) return End();
            var before = await Check(() => snapshot(runner, record.Workspace.Path!, indexTask, true, ct));
            if (Cancelled()) return End();
            var head = await Check(() => prepareHead?.Invoke(runner, record.Workspace.Path!, indexTask, true, ct) ?? ReadHeadAsync(runner, record.Workspace.Path!, ct));
            var environment = WorkerEnvironment.Environment(run.Id, ledger.RunId, record.Settings.TempRoot?.Value).Variables;
            environment["MSBUILDDISABLENODEREUSE"] = "1";
            environment["UseSharedCompilation"] = "false";
            foreach (var command in commands)
            {
                ct.ThrowIfCancellationRequested();
                if (Cancelled()) return End();
                (VerifyResult Result, string Log, Exception? Error) executed;
                try
                {
                    commandRunning = true;
                    executed = prepareCommand is not null
                        ? await prepareCommand(run, command, environment, commandCancellation.Token)
                        : await Verifier.RunCommandAsync(record.Spec, null, record.Workspace.Path!, environment, command, false, ct, commandCancellation.Token);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested && Cancelled()) { return End(); }
                if (ct.IsCancellationRequested) executed = Verifier.CancelledCommand(executed);
                await File.AppendAllTextAsync(logPath, executed.Log, CancellationToken.None);
                record.Prepare.Add(executed.Result);
                ledger.SaveRecord(record);
                commandRunning = false;
                ct.ThrowIfCancellationRequested();
                if (Cancelled()) return End();
                if (executed.Error is not null || Verifier.Failed(executed.Result))
                    return End("prepare-failed", executed.Result.TimedOut ? $"prepare command {record.Prepare.Count} {executed.Error!.Message}"
                        : executed.Error?.Message ?? $"prepare command {record.Prepare.Count} exited {executed.Result.ExitCode}");
            }
            if (Cancelled()) return End();
            var after = await Check(() => snapshot(runner, record.Workspace.Path!, indexTask, false, ct));
            if (Cancelled()) return End();
            var afterHead = await Check(() => prepareHead?.Invoke(runner, record.Workspace.Path!, indexTask, false, ct) ?? ReadHeadAsync(runner, record.Workspace.Path!, ct));
            if (before != after)
            {
                var diff = await Check(() => runner.CheckAsync(record.Workspace.Path!, ct, "-c", "core.quotePath=false", "diff", "--name-only", "--no-renames", before, after));
                var paths = diff.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Order(StringComparer.Ordinal).ToList();
                return End("prepare-changed-workspace", string.Join(", ", paths.Take(20)) + (paths.Count > 20 ? $", and {paths.Count - 20} more" : ""), changed: true);
            }
            if (head != afterHead) return End("prepare-changed-workspace", $"prepare moved HEAD from {head} to {afterHead}", changed: true);
            ct.ThrowIfCancellationRequested();
            if (Cancelled()) return End();
            record.PrepareRunning = false;
            record.PrepareDone = true;
            ledger.SaveRecord(record);
            ledger.Write(run.Id, "prepared", new JsonObject { ["commands"] = record.Prepare.Count, ["failed"] = 0, ["changed"] = false });
            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not LedgerWriteException)
        {
            return End("prepare-failed", ex.Message, checkFailed: checkFailed ? true : commandRunning ? null : false);
        }
        finally
        {
            run.PrepareCancellation = null;
            if (runner is not null) Snapshot.Delete(runner, indexTask);
        }
    }

    static async Task<string> ReadHeadAsync(Git git, string cwd, CancellationToken ct)
    {
        var head = await git.RunAsync(cwd, ct, "rev-parse", "--verify", "HEAD^{commit}");
        if (head.ExitCode == 0)
        {
            var id = head.Output.Trim();
            if (!Regex.IsMatch(id, "\\A[0-9a-f]{40}([0-9a-f]{24})?\\z")) throw new HarnessException("git returned an invalid object id");
            return id;
        }
        var symbolic = await git.RunAsync(cwd, ct, "symbolic-ref", "--quiet", "HEAD");
        if (symbolic.ExitCode == 0 && (await git.RunAsync(cwd, ct, "show-ref", "--verify", "--quiet", symbolic.Output.Trim())).ExitCode == 1) return "unborn";
        throw new HarnessException($"git rev-parse --verify HEAD^{{commit}} failed in {cwd}: {head.Stderr.Trim()}");
    }
}
