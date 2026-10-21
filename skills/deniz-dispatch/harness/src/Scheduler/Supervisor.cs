sealed class Supervisor(HarnessServer host, Ledger ledger, string repo, AllowList allow, Policy policy, int max, CancellationToken ct)
{
    // Task root sessions only: their execution events end a task.
    readonly ConcurrentDictionary<string, TaskRun> _bySession = new(StringComparer.Ordinal);

    // Any session the supervisor has seen, subagent children included, and the task that owns
    // it; null for a session no task owns.
    readonly ConcurrentDictionary<string, TaskRun?> _owners = new(StringComparer.Ordinal);

    readonly ConcurrentDictionary<string, Escalation> _escalated = new(StringComparer.Ordinal);
    List<TaskRun> _runs = [];
    bool _forceWatchdog;
    int _watchdogFailures;

    OpenCodeClient Client => host.Client;

    public async Task<int> RunAsync(List<TaskRun> runs)
    {
        _runs = runs;
        return await LoopAsync();
    }

    public async Task<int> ResumeAsync(List<TaskRun> runs, bool proceed)
    {
        _runs = runs;
        foreach (var run in runs.Where(x => x.Record.Session.Id is not null))
        {
            try
            {
                await ReattachAsync(run, proceed);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Finish(run, TaskState.Failed, null, $"resume failed: {ex.GetType().Name}: {ex.Message}");
            }
        }

        return await LoopAsync();
    }

    // A restarted server runs nothing: settle what ended, or re-prompt an interrupted session.
    async Task ReattachAsync(TaskRun run, bool proceed)
    {
        var session = Client.Sessions.GetSessionClient(run.Record.Session.Id!);
        var read = await session.GetAsync(OpenCodeRequestOptions.NoThrow, ct);
        if (read.IsError)
        {
            Finish(run, TaskState.Failed, null, $"session {run.Record.Session.Id} unreadable: HTTP {read.Status} {read.Error?.Tag}");
            return;
        }

        _bySession[run.Record.Session.Id!] = run;
        _owners[run.Record.Session.Id!] = run;
        var outcome = read.Session.Outcome;
        if (!proceed || (outcome == SessionInfoOutcome.Succeeded || outcome == SessionInfoOutcome.Failed) || run.CancelRequested)
        {
            BeginSettle(run, null);
            return;
        }

        var allowed = run.Record.Spec.Agent is { } agent
            ? allow.AllowsAgent(agent)
            : RouteRef.TryParse(run.Record.Settings.Route?.Value ?? "", out var route, out _) && allow.AllowsRoute(route!);
        if (!allowed)
        {
            Finish(run, TaskState.Failed, null, $"{run.Record.Spec.Agent ?? run.Record.Settings.Route?.Value} is no longer on the allow-list");
            return;
        }

        // Re-apply the scrubbed environment: whether it outlives a server restart is unverified.
        var (variables, removed) = Policy.Environment(run.Id);
        await session.SetEnvironmentAsync(new SessionEnvironmentRequest { Variables = variables }, cancellationToken: ct);
        ledger.Write(run.Id, "environment", new JsonObject { ["variables"] = variables.Count, ["removed"] = Json.Array(removed) });
        var prompt = await session.PromptAsync(new SessionPromptRequest
        {
            Text = policy.RawPrompt
                ? "Continue the task from where you stopped."
                : "Continue the task from where you stopped. The standing instructions and the task above still apply.",
        }, cancellationToken: ct);
        run.Record.BackendState.PromptId = prompt.Prompt.Id;
        run.Record.BackendState.PromptCreated = prompt.Prompt.Time.Created;
        ledger.SaveRecord(run.Record);
        var resumed = new JsonObject { ["promptId"] = prompt.Prompt.Id, ["resume"] = true };
        if (policy.RawPrompt)
        {
            resumed["rawPrompt"] = true;
        }

        ledger.Write(run.Id, "prompt", resumed);
        run.Deadline = DateTimeOffset.UtcNow.AddMinutes(run.Record.Spec.TimeoutMinutes ?? Constants.DefaultTimeoutMinutes);
        run.State = TaskState.Running;
        await SweepBlockersAsync(run);
    }

    async Task<int> LoopAsync()
    {
        try
        {
            return await SuperviseAsync();
        }
        catch (Exception ex) when (ex is OperationCanceledException or HarnessException)
        {
            // Disposing the server next interrupts every running session (reason shutdown);
            // `resume` reconciles from this ledger.
            ledger.Write(null, "aborted", new JsonObject { ["reason"] = ex.Message, ["running"] = Json.Array(_runs.Where(x => !x.IsFinal).Select(x => x.Id)) });
            throw;
        }
    }

    async Task<int> SuperviseAsync()
    {
        var lastWatchdog = DateTimeOffset.UtcNow;
        while (_runs.Any(x => !x.IsFinal))
        {
            ct.ThrowIfCancellationRequested();
            StartReady();
            CheckCancelRequests();
            await CheckDecisionsAsync();

            using (var tick = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                tick.CancelAfter(Constants.BusRead);
                try
                {
                    var item = await host.Events.ReadAsync(tick.Token);
                    await HandleAsync(item);
                    while (host.Events.TryRead(out var more))
                    {
                        await HandleAsync(more);
                    }
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    // No event within the read window; fall through to the timers.
                }
            }

            if (_forceWatchdog || DateTimeOffset.UtcNow - lastWatchdog >= Constants.Watchdog)
            {
                _forceWatchdog = false;
                lastWatchdog = DateTimeOffset.UtcNow;
                await WatchdogAsync();
            }

            foreach (var run in _runs.Where(x => x.State == TaskState.Running).ToList())
            {
                try
                {
                    // A cancel can arrive while the task is still dispatching; deliver it once running.
                    if (run.CancelRequested && !run.CancelSent)
                    {
                        run.CancelSent = true;
                        await InterruptAsync(run, "cancel");
                    }

                    if (run.NextCheck <= DateTimeOffset.UtcNow)
                    {
                        run.NextCheck = null;
                        await CheckDoneAsync(run, await ActiveAsync());
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    ledger.Write(run.Id, "error", new JsonObject { ["while"] = "check", ["message"] = ex.Message });
                }
            }
        }

        var failed = _runs.Count(x => x.State != TaskState.Done);
        Console.WriteLine();
        Commands.Status(Args.Parse(["status", ledger.RunId]));
        return failed == 0 ? 0 : 1;
    }

    void StartReady()
    {
        foreach (var run in _runs.Where(x => x.State == TaskState.Pending).ToList())
        {
            if (_runs.Count(x => x.State is TaskState.Dispatching or TaskState.Running or TaskState.Settling) >= max)
            {
                return;
            }

            if (run.CancelRequested)
            {
                Finish(run, TaskState.Cancelled, null, "cancelled before dispatch");
                continue;
            }

            if (run.Record.Spec.ReviewOf is { } authorId)
            {
                var author = _runs.FirstOrDefault(x => x.Id == authorId);
                var authorState = author?.State ?? StateFromLedger(authorId);
                if (authorState is TaskState.Pending or TaskState.Dispatching or TaskState.Running or TaskState.Settling)
                {
                    continue;
                }

                var authorResult = ledger.LoadResult(authorId);
                if (authorState != TaskState.Done || authorResult?.Outcome != "succeeded" || authorResult.Commit is null)
                {
                    Finish(run, TaskState.Cancelled, null, $"reviewed task {authorId} produced no successful commit");
                    continue;
                }
            }

            run.State = TaskState.Dispatching;
            run.Work = Task.Run(() => DispatchAsync(run), CancellationToken.None);
        }
    }

    TaskState StateFromLedger(string task) => ledger.TaskStates().GetValueOrDefault(task) switch
    {
        "done" => TaskState.Done,
        "failed" => TaskState.Failed,
        "cancelled" => TaskState.Cancelled,
        _ => TaskState.Pending,
    };

    async Task DispatchAsync(TaskRun run)
    {
        var record = run.Record;
        var spec = record.Spec;
        try
        {
            // 1. Worktree on the harness's own branch.
            if (record.Workspace.Path is null)
            {
                var baseRef = spec.ReviewOf is { } author ? ledger.LoadRecord(author).Workspace.Branch! : spec.Base!;
                var git = Git.ForTask(run.Id);
                var baseSha = await git.IdAsync(repo, ct, "rev-parse", "--verify", $"{baseRef}^{{commit}}");
                var worktree = Locations.Worktree(run.Id);
                var branch = $"dispatch/{ledger.RunId}/{run.Id}";
                await git.CheckAsync(repo, ct, "worktree", "add", "-b", branch, worktree, baseSha);
                record.Workspace.Path = worktree;
                record.Workspace.Branch = branch;
                record.Workspace.BaseRef = baseRef;
                record.Workspace.BaseSha = baseSha;
                ledger.SaveRecord(record);
                ledger.Write(run.Id, "worktree", new JsonObject { ["path"] = worktree, ["branch"] = branch, ["base"] = baseRef, ["baseSha"] = baseSha });
            }

            var where = new LocationSelector { Directory = record.Workspace.Path };

            // 2. Route: an agent's pinned model is passed explicitly, because session create
            //    does not apply the agent's model.
            var catalog = await Catalog.LoadAsync(Client, record.Workspace.Path, TimeSpan.FromSeconds(60), ct);
            RouteRef route;
            string agent;
            if (spec.Agent is { } agentId)
            {
                if (!allow.AllowsAgent(agentId)) throw new HarnessException($"agent {agentId} left the allow-list");
                if (catalog.Manual.Contains(agentId)) throw new HarnessException($"agent {agentId} is manual-class");
                var info = await Client.Agents.GetAgentAsync(agentId, new AgentRequest { Location = where }, cancellationToken: ct);
                route = info.Agent.Model is { } model ? RouteRef.From(model) : throw new HarnessException($"agent {agentId} pins no model");
                agent = agentId;
            }
            else
            {
                if (!RouteRef.TryParse(spec.Route!, out var parsed, out _) || !allow.AllowsRoute(parsed!)) throw new HarnessException($"route {spec.Route} left the allow-list");
                if (catalog.IsManualRoute(parsed!)) throw new HarnessException($"route {parsed} equals a manual-class agent's model");
                route = parsed!;
                agent = spec.Persona ?? Constants.DefaultPersona;
                if (catalog.Manual.Contains(agent)) throw new HarnessException($"persona {agent} is manual-class");
            }

            await WaitForRouteAsync(route, record.Workspace.Path);
            record.Settings.Route = new Setting<string> { Value = route.ToString(), From = "task" };
            record.Agent = agent;
            record.Lineage = catalog.Lineage(route);
            ledger.SaveRecord(record);
            ledger.Write(run.Id, "route", new JsonObject { ["route"] = record.Settings.Route.Value, ["agent"] = agent, ["lineage"] = record.Lineage });

            // 3. Session with the policy's rules (subagent children inherit them). Agent always
            //    goes with Model.
            var rules = policy.Rules();
            var created = await Client.Sessions.CreateSessionAsync(new SessionCreateRequest
            {
                Title = $"dispatch {ledger.RunId}/{run.Id}",
                Agent = agent,
                Model = route.ToModelRef(),
                Location = new LocationPublicRef { Directory = record.Workspace.Path },
                Permissions = rules,
                Metadata = Json.Metadata(ledger.RunId, run.Id),
            }, cancellationToken: ct);
            var sessionId = created.Session.Id;
            record.Session.Id = sessionId;
            ledger.SaveRecord(record);
            _bySession[sessionId] = run;
            _owners[sessionId] = run;
            ledger.Write(run.Id, "session", new JsonObject
            {
                ["sessionId"] = sessionId,
                ["projectId"] = created.Session.ProjectId,
                ["rules"] = new JsonArray(rules.Select(x => (JsonNode?)JsonValue.Create($"{x.Action} {x.Resource} {x.Effect.ToString().ToLowerInvariant()}")).ToArray()),
            });

            var session = Client.Sessions.GetSessionClient(sessionId);
            var (variables, removed) = Policy.Environment(run.Id);
            await session.SetEnvironmentAsync(new SessionEnvironmentRequest { Variables = variables }, cancellationToken: ct);
            ledger.Write(run.Id, "environment", new JsonObject { ["variables"] = variables.Count, ["removed"] = Json.Array(removed) });

            // 4. Prompt (standing instructions first), then close the admission race.
            var prompt = await session.PromptAsync(new SessionPromptRequest
            {
                Text = policy.Prompt(new PlannedOrRecord(spec, record.PromptText), record.Workspace.Path),
            }, cancellationToken: ct);
            record.BackendState.PromptId = prompt.Prompt.Id;
            record.BackendState.PromptCreated = prompt.Prompt.Time.Created;
            ledger.SaveRecord(record);
            var prompted = new JsonObject { ["promptId"] = prompt.Prompt.Id, ["chars"] = record.PromptText.Length };
            if (policy.RawPrompt)
            {
                prompted["rawPrompt"] = true;
            }

            ledger.Write(run.Id, "prompt", prompted);
            run.Deadline = DateTimeOffset.UtcNow.AddMinutes(spec.TimeoutMinutes ?? Constants.DefaultTimeoutMinutes);
            run.State = TaskState.Running;
            await SweepBlockersAsync(run);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Any failure ends this task; an escaped exception would leave it dispatching forever.
            Finish(run, TaskState.Failed, null, $"dispatch failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    async Task WaitForRouteAsync(RouteRef route, string directory)
    {
        var deadline = DateTimeOffset.UtcNow + Constants.RouteWait;
        while (true)
        {
            string why;
            try
            {
                var catalog = await Catalog.LoadAsync(Client, directory, TimeSpan.Zero, ct);
                if (catalog.CheckRoute(route) is not { } reason) return;
                why = reason;
            }
            catch (NotReadyException ex)
            {
                why = ex.Message;
            }

            if (DateTimeOffset.UtcNow >= deadline) throw new HarnessException($"route {route} at {directory}: {why}");
            await Task.Delay(TimeSpan.FromSeconds(3), ct);
        }
    }

    async Task HandleAsync(object item)
    {
        try
        {
            switch (item)
            {
                case BusConnected connected:
                    if (connected.Attempt > 0)
                    {
                        ledger.Write(null, "bus", new JsonObject { ["state"] = "reconnected" });
                        _forceWatchdog = true;
                    }

                    break;
                case BusDisconnected gone:
                    ledger.Write(null, "bus", new JsonObject { ["state"] = "disconnected", ["reason"] = gone.Reason });
                    _forceWatchdog = true;
                    break;
                case PermissionAsked asked:
                    if (await OwnerAsync(asked.Data.SessionId) is { } asker)
                    {
                        await AnswerAsync(asker, asked.Data.SessionId, asked.Data.Id, asked.Data.Action, asked.Data.Resources);
                    }

                    break;
                case PermissionReplied replied when _escalated.TryRemove(replied.Data.RequestId, out var gone):
                    // Answered by someone else, or by the server: a reject answers the session's
                    // whole pending batch.
                    AskQueue.Remove(ledger.RunId, gone.Ask.AskId);
                    ledger.Write(gone.Ask.Task, "permission", new JsonObject
                    {
                        ["id"] = gone.Ask.AskId,
                        ["action"] = gone.Ask.Action,
                        ["answer"] = replied.Data.Reply.ToString().ToLowerInvariant(),
                        ["by"] = "server",
                    });
                    break;
                case FormCreated form:
                    if (await OwnerAsync(form.Data.Form.SessionId) is { } former)
                    {
                        await CancelFormAsync(former, form.Data.Form.SessionId, form.Data.Form.Id, form.Data.Form.Title);
                    }

                    break;
                case SessionStatusUpdated { Data.Status: SessionStatusRetry retry } status when _bySession.TryGetValue(status.Data.SessionId, out var run):
                    run.Retries++;
                    ledger.Write(run.Id, "retry", new JsonObject
                    {
                        ["attempt"] = retry.Attempt,
                        ["message"] = retry.Message,
                        ["next"] = retry.Next,
                        ["reason"] = retry.Action?.Reason,
                        ["provider"] = retry.Action?.Provider,
                    });
                    break;
                case SessionExecutionSucceeded done when _bySession.TryGetValue(done.Data.SessionId, out var run):
                    ledger.Write(run.Id, "status", new JsonObject { ["execution"] = "succeeded" });
                    run.NextCheck = DateTimeOffset.UtcNow;
                    break;
                case SessionExecutionFailed failed when _bySession.TryGetValue(failed.Data.SessionId, out var run):
                    ledger.Write(run.Id, "status", new JsonObject { ["execution"] = "failed", ["error"] = Json.Error(failed.Data.Error) });
                    run.NextCheck = DateTimeOffset.UtcNow;
                    break;
                case SessionExecutionInterrupted interrupted when _bySession.TryGetValue(interrupted.Data.SessionId, out var run):
                    ledger.Write(run.Id, "status", new JsonObject { ["execution"] = "interrupted", ["reason"] = interrupted.Data.Reason.ToString() });
                    run.NextCheck = DateTimeOffset.UtcNow;
                    break;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ledger.Write(null, "error", new JsonObject { ["while"] = item.GetType().Name, ["message"] = ex.Message });
        }
    }

    // The task that owns a session: a task's root session, or a subagent child of one (children
    // inherit the root's session rules and run at the same location).
    async Task<TaskRun?> OwnerAsync(string sessionId)
    {
        if (_owners.TryGetValue(sessionId, out var known)) return known;
        var chain = new List<string> { sessionId };
        var current = sessionId;
        for (var depth = 0; depth < 8; depth++)
        {
            var read = await Client.Sessions.GetSessionClient(current).GetAsync(OpenCodeRequestOptions.NoThrow, ct);
            if (read.IsError)
            {
                // Unknown for now; the next event or sweep asks again.
                return null;
            }

            if (read.Session.ParentId is not { } parent)
            {
                break;
            }

            if (_owners.TryGetValue(parent, out var owner))
            {
                foreach (var id in chain) _owners.TryAdd(id, owner);
                return owner;
            }

            chain.Add(parent);
            current = parent;
        }

        // A root no task owns.
        foreach (var id in chain) _owners.TryAdd(id, null);
        return null;
    }

    // Every ask the policy does not escalate gets "once": upstream `opencode run --auto`, which
    // approves whatever is not explicitly denied (denied calls never become asks). Never
    // "always": it stores a standing grant for the whole project, which every worktree shares.
    async Task AnswerAsync(TaskRun run, string sessionId, string requestId, string action, IReadOnlyList<string> resources)
    {
        lock (run.Answered)
        {
            if (!run.Answered.Add(requestId)) return;
        }

        run.Asks++;
        var child = sessionId == run.Record.Session.Id ? null : sessionId;
        if (policy.EscalationFor(action, resources) is { } rule)
        {
            run.Escalated++;
            var ask = new PendingAsk
            {
                Run = ledger.RunId,
                Task = run.Id,
                Backend = "opencode",
                Session = sessionId,
                RootSession = run.Record.Session.Id!,
                AskId = requestId,
                BackendRef = new JsonObject { ["requestId"] = requestId },
                Action = action,
                Resources = resources.ToList(),
                Rule = rule.Name!,
                Time = Json.Timestamp(),
                Reply = ["reply", ledger.RunId, requestId, "once|reject"],
            };
            _escalated[requestId] = new Escalation(ask, DateTimeOffset.UtcNow);
            AskQueue.Write(ask);
            ledger.Write(run.Id, "escalated", new JsonObject
            {
                ["id"] = requestId,
                ["session"] = child,
                ["action"] = action,
                ["resources"] = Json.Array(resources),
                ["rule"] = rule.Name,
                ["file"] = Path.Combine(Locations.AsksDir(ledger.RunId), $"{requestId}.json"),
            });
            return;
        }

        var reply = await Client.Sessions.GetSessionClient(sessionId).ReplyToPermissionAsync(requestId, new SessionPermissionReplyRequest
        {
            Decision = PermissionReply.Once,
        }, OpenCodeRequestOptions.NoThrow, ct);
        ledger.Write(run.Id, "permission", new JsonObject
        {
            ["id"] = requestId,
            ["session"] = child,
            ["action"] = action,
            ["resources"] = Json.Array(resources),
            ["answer"] = "once",
            ["by"] = "harness",
            ["status"] = reply.Status,
        });
    }

    // The operator's decisions, written by `dispatch reply` next to the pending asks.
    async Task CheckDecisionsAsync()
    {
        foreach (var decision in AskQueue.TakeDecisions(ledger.RunId))
        {
            try
            {
                if (!_escalated.TryRemove(decision.AskId, out var held))
                {
                    ledger.Write(null, "decision", new JsonObject { ["id"] = decision.AskId, ["decision"] = decision.Decision, ["result"] = "no such pending ask" });
                    continue;
                }

                var ask = held.Ask;
                var reject = decision.Decision == "reject";
                var request = reject
                    ? new SessionPermissionReplyRequest { Decision = PermissionReply.Reject, Message = decision.Message ?? Policy.LeadDeclined }
                    : new SessionPermissionReplyRequest { Decision = PermissionReply.Once };
                var reply = await Client.Sessions.GetSessionClient(ask.Session).ReplyToPermissionAsync(ask.AskId, request, OpenCodeRequestOptions.NoThrow, ct);
                AskQueue.Remove(ledger.RunId, ask.AskId);
                ledger.Write(ask.Task, "permission", new JsonObject
                {
                    ["id"] = ask.AskId,
                    ["session"] = ask.Session == ask.RootSession ? null : ask.Session,
                    ["action"] = ask.Action,
                    ["resources"] = Json.Array(ask.Resources),
                    ["answer"] = reject ? "reject" : "once",
                    ["by"] = "operator",
                    ["message"] = reject ? decision.Message ?? Policy.LeadDeclined : null,
                    ["waitedSeconds"] = Math.Round((DateTimeOffset.UtcNow - held.Queued).TotalSeconds, 1),
                    ["status"] = reply.Status,
                });
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                ledger.Write(null, "error", new JsonObject { ["while"] = "decision", ["id"] = decision.AskId, ["message"] = ex.Message });
            }
        }
    }

    // An escalated ask that can no longer be answered: its tool call ended, or its task did.
    void DropEscalations(Func<Escalation, bool> which, string reason)
    {
        foreach (var held in _escalated.Values.Where(which).ToList())
        {
            if (!_escalated.TryRemove(held.Ask.AskId, out _)) continue;
            AskQueue.Remove(ledger.RunId, held.Ask.AskId);
            ledger.Write(held.Ask.Task, "escalation-dropped", new JsonObject { ["id"] = held.Ask.AskId, ["action"] = held.Ask.Action, ["reason"] = reason });
        }
    }

    async Task CancelFormAsync(TaskRun run, string sessionId, string formId, string title)
    {
        lock (run.Answered)
        {
            if (!run.Answered.Add(formId)) return;
        }

        run.Forms++;
        var reply = await Client.Sessions.GetSessionClient(sessionId).CancelFormAsync(formId, new SessionFormCancelRequest { Message = Policy.NoHuman }, OpenCodeRequestOptions.NoThrow, ct);
        ledger.Write(run.Id, "form", new JsonObject
        {
            ["id"] = formId,
            ["session"] = sessionId == run.Record.Session.Id ? null : sessionId,
            ["title"] = title,
            ["answer"] = "cancel",
            ["status"] = reply.Status,
        });
    }

    // Pending permissions and forms the bus did not deliver (admission race, disconnects). The
    // listing is per location, so it also covers the task's subagent children.
    async Task SweepBlockersAsync(TaskRun run)
    {
        var listed = DateTimeOffset.UtcNow;
        var where = new LocationSelector { Directory = run.Record.Workspace.Path };
        var requests = (await Client.Permissions.ListRequestsAsync(new PermissionRequestListRequest { Location = where }, cancellationToken: ct)).Requests;
        foreach (var request in requests)
        {
            if (await OwnerAsync(request.SessionId) == run)
            {
                await AnswerAsync(run, request.SessionId, request.Id, request.Action, request.Resources);
            }
        }

        // Interrupted tool calls drop their pending ask without a reply event.
        var live = requests.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        DropEscalations(x => x.Ask.Task == run.Id && x.Queued < listed && !live.Contains(x.Ask.AskId), "no longer pending on the server");

        foreach (var form in (await Client.Forms.ListFormsAsync(new FormListRequest { Location = where }, cancellationToken: ct)).Forms)
        {
            if (await OwnerAsync(form.SessionId) == run)
            {
                await CancelFormAsync(run, form.SessionId, form.Id, form.Title);
            }
        }
    }

    async Task<IReadOnlyDictionary<string, SessionActive>> ActiveAsync() =>
        (await Client.Sessions.GetActiveAsync(cancellationToken: ct)).Active;

    async Task WatchdogAsync()
    {
        IReadOnlyDictionary<string, SessionActive> active;
        try
        {
            active = await ActiveAsync();
            _watchdogFailures = 0;
        }
        catch (OpenCodeException ex)
        {
            ledger.Write(null, "error", new JsonObject { ["while"] = "watchdog", ["message"] = ex.Message });
            if (++_watchdogFailures >= 3)
            {
                throw new HarnessException($"the private server stopped answering; run `resume {ledger.RunId}` after checking it");
            }

            return;
        }

        foreach (var run in _runs.Where(x => x.State == TaskState.Running).ToList())
        {
            try
            {
                if (run.Deadline <= DateTimeOffset.UtcNow && !run.TimedOut)
                {
                    run.TimedOut = true;
                    await InterruptAsync(run, "timeout");
                }

                if (active.ContainsKey(run.Record.Session.Id!))
                {
                    await SweepBlockersAsync(run);
                }

                await CheckDoneAsync(run, active);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                ledger.Write(run.Id, "error", new JsonObject { ["while"] = "watchdog", ["message"] = ex.Message });
            }
        }
    }

    // Done means: absent from the active map, and the session reports an outcome for an
    // execution that went idle after this task's prompt.
    async Task CheckDoneAsync(TaskRun run, IReadOnlyDictionary<string, SessionActive> active)
    {
        if (active.ContainsKey(run.Record.Session.Id!))
        {
            run.AbsentChecks = 0;
            return;
        }

        var info = (await Client.Sessions.GetSessionClient(run.Record.Session.Id!).GetAsync(cancellationToken: ct)).Session;
        var idleAfterPrompt = info.Time.Idle is null || run.Record.BackendState.PromptCreated is null || info.Time.Idle >= run.Record.BackendState.PromptCreated;
        if (info.Outcome is not null && idleAfterPrompt)
        {
            BeginSettle(run, null);
            return;
        }

        if (++run.AbsentChecks >= 4)
        {
            BeginSettle(run, "lost");
        }
    }

    void CheckCancelRequests()
    {
        var control = Locations.ControlDir(ledger.RunId);
        if (!Directory.Exists(control)) return;
        foreach (var run in _runs.Where(x => !x.IsFinal && !x.CancelRequested))
        {
            var file = Path.Combine(control, $"{run.Id}.cancel");
            if (!File.Exists(file)) continue;
            File.Delete(file);
            run.CancelRequested = true;
            if (run.State != TaskState.Running)
            {
                // Pending tasks end in StartReady; dispatching ones are interrupted once running.
                ledger.Write(run.Id, "cancel", new JsonObject { ["state"] = run.State.ToString().ToLowerInvariant() });
            }
        }
    }

    async Task InterruptAsync(TaskRun run, string type)
    {
        var response = await Client.Sessions.GetSessionClient(run.Record.Session.Id!).InterruptAsync(new SessionInterruptRequest { Resume = false }, OpenCodeRequestOptions.NoThrow, ct);
        ledger.Write(run.Id, type, new JsonObject { ["interrupted"] = response.IsError ? null : response.Interrupt.Interrupted, ["status"] = response.Status });
        run.NextCheck = DateTimeOffset.UtcNow.AddSeconds(2);
    }

    void BeginSettle(TaskRun run, string? outcomeOverride)
    {
        if (run.State == TaskState.Settling || run.IsFinal) return;
        run.State = TaskState.Settling;
        run.Work = Task.Run(async () =>
        {
            try
            {
                var result = await Settlement.SettleAsync(Client, ledger, run, outcomeOverride, ct);
                Finish(run, result.State switch { "done" => TaskState.Done, "cancelled" => TaskState.Cancelled, _ => TaskState.Failed }, result.Outcome, null);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Finish(run, TaskState.Failed, null, $"settle failed: {ex.GetType().Name}: {ex.Message}");
            }
        }, CancellationToken.None);
    }

    void Finish(TaskRun run, TaskState state, string? outcome, string? reason)
    {
        if (reason is not null && state == TaskState.Failed) ledger.Write(run.Id, "error", new JsonObject { ["message"] = reason });
        DropEscalations(x => x.Ask.Task == run.Id, $"task ended ({state.ToString().ToLowerInvariant()})");
        run.State = state;
        ledger.Write(run.Id, "final", new JsonObject { ["state"] = state.ToString().ToLowerInvariant(), ["outcome"] = outcome, ["reason"] = reason });
    }
}
