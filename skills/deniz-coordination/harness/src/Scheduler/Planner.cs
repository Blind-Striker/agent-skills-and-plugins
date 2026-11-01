static class Planner
{
    public static async Task<Plan> LoadAsync(string tasksPath, string? repoOverride, AllowList allow, CancellationToken ct, Git? git = null, Args? args = null, string? executableSearchPath = null)
    {
        tasksPath = Path.GetFullPath(tasksPath);
        if (!File.Exists(tasksPath))
        {
            throw new HarnessException($"task file not found: {tasksPath}");
        }

        TaskFile file;
        try
        {
            file = Json.ReadTaskFile(File.ReadAllText(tasksPath));
        }
        catch (JsonException ex)
        {
            throw new HarnessException($"{tasksPath} (task-file): {ex.Message}");
        }

        var baseDir = Path.GetDirectoryName(tasksPath)!;
        var repoPath = repoOverride ?? (file.Repo is { } r ? Path.Combine(baseDir, r) : null);
        var repo = Path.GetFullPath(repoPath ?? baseDir);
        var plan = new Plan { File = file, TasksPath = tasksPath, Repo = repo };
        var useDefaultGit = git is null;
        git ??= Git.Default;
        if (repoPath is not null)
        {
            var top = await git.RunAsync(repo, ct, "rev-parse", "--show-toplevel");
            if (top.ExitCode != 0)
            {
                plan.Errors.Add($"repo {repo} is not a git work tree");
                return plan;
            }
            repo = Path.GetFullPath(top.Output.Trim());
        }
        plan = new Plan { File = file, TasksPath = tasksPath, Repo = repo };

        var specs = file.Tasks ?? [];
        if (args is not null)
        {
            plan.Config = await ConfigLayers.LoadAsync(repoPath is null ? null : repo, file, tasksPath, args, ct, git);
            if (useDefaultGit) git = Git.Default;
            allow = plan.Config.Allow;
            foreach (var spec in specs) ConfigLayers.Apply(spec, plan.Config.Effective.Tasks[spec.Id!].Settings);
            plan.Warnings.AddRange(plan.Config.Effective.Warnings);
        }
        var runId = Commands.CreateRunId(Commands.RunName(args, file, tasksPath));
        if (specs.Any(x => !Scratch.Required(x) && x.Workspace == "in-place") && Worktree.IsInside(Locations.WorktreeRoot, repo))
            plan.Errors.Add("in-place workspace refuses a worktree root inside the repository");
        if (specs.Any(x => !Scratch.Required(x) && x.Workspace == "in-place") && Worktree.IsInside(Locations.LedgerRoot, repo))
            plan.Errors.Add("in-place workspace refuses a ledger root inside the repository");
        foreach (var spec in specs)
            if (plan.Config?.Effective.Tasks[spec.Id!].Settings.TempRoot is { Value: { } tempRoot } setting && (Worktree.IsInside(tempRoot, repo) || Worktree.IsInside(Worktree.RealPath(tempRoot), Worktree.RealPath(repo))))
                plan.Errors.Add($"{spec.Id}: tempRoot from {setting.From} must be outside the repository");
        foreach (var path in file.SharedPaths ?? [])
            if (string.IsNullOrWhiteSpace(path) || Path.IsPathFullyQualified(path) || path.Replace('\\', '/').Split('/').Contains("..") || path.IndexOfAny(['*', '?', '!']) >= 0)
                plan.Errors.Add("sharedPaths must contain repository-relative paths, not globs or parent paths");
        if (specs.Count == 0) plan.Errors.Add("no tasks");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var contractCache = new Dictionary<string, Prompt.Contract>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        var fetches = new Dictionary<(string Remote, string Branch), ProcessResult>();
        string[]? remotes = null;
        var depths = new Dictionary<(string Repo, string Head), Task<(int Length, string Detail)>>();
        foreach (var spec in specs)
        {
            var e = new List<string>();
            Prompt.Inputs? promptInputs = null;
            try { promptInputs = Prompt.Load(file, spec, tasksPath, args, plan.Config?.Policy.Instructions ?? [], contractCache); }
            catch (Exception ex) when (ex is HarnessException or IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
            { e.Add(ex.Message); }
            if (promptInputs is { Contracts: var loaded })
            {
                if (loaded.Count > 0)
                    plan.Info.Add($"{spec.Id}: contracts {string.Join("; ", loaded.Select(x => $"{x.Record.Path} ({x.Record.Version})"))}");
            }
            if (!Regex.IsMatch(spec.Id ?? "", "^[a-z0-9][a-z0-9-]{0,39}$")) e.Add("id must match ^[a-z0-9][a-z0-9-]{0,39}$");
            else if (!ids.Add(spec.Id!)) e.Add("duplicate id");
            foreach (var tag in spec.Tags ?? [])
                if (!Regex.IsMatch(tag ?? "", "^[a-z0-9][a-z0-9-]{0,39}$", RegexOptions.CultureInvariant))
                    e.Add($"tag {tag} must match ^[a-z0-9][a-z0-9-]{{0,39}}$");
            if (!Constants.Kinds.Contains(spec.Kind)) e.Add($"kind must be one of {string.Join(", ", Constants.Kinds)}");
            if (spec.Kind == "structured" && spec.Shell is not null) e.Add("shell is forbidden for structured tasks");
            else if (spec.Shell is not null || spec.Verify is { Count: > 0 } || spec.AdvisoryVerify is { Count: > 0 } || spec.Kind != "structured" && spec.Prepare is { Count: > 0 })
            {
                try
                {
                    var shell = Verifier.ResolveShell(spec, executableSearchPath);
                    if (spec.Prepare is { Count: > 0 }) plan.Info.Add($"{spec.Id}: prepare {spec.Prepare.Count} command(s) in {shell}");
                }
                catch (HarnessException ex) { e.Add($"{(spec.Verify is not { Count: > 0 } && spec.AdvisoryVerify is not { Count: > 0 } ? "prepare" : "verify")} shell: {ex.Message}"); }
            }

            RouteRef? route = null;
            if ((spec.Agent is null) == (spec.Route is null)) e.Add("name exactly one of agent or route");
            if (spec.Agent is not null && spec.Persona is not null) e.Add("persona goes with route, not with agent");
            if (spec.Agent is { } agent && !allow.AllowsAgent(agent)) e.Add($"agent {agent} is not on the allow-list{EmptyHint(allow)}");
            if (spec.Route is { } routeText)
            {
                if (spec.Backend == "codex")
                {
                    try { route = CodexBackend.ParseRoute(routeText); }
                    catch (HarnessException ex) { e.Add($"route: {ex.Message}"); }
                }
                else if (!RouteRef.TryParse(routeText, out route, out var error)) e.Add($"route: {error}");
                else if (!allow.AllowsRoute(route!)) e.Add($"route {route} is not on the allow-list{EmptyHint(allow)}");
            }

            var prompt = spec.Prompt;
            if ((spec.Prompt is null) == (spec.PromptFile is null)) e.Add("name exactly one of prompt or promptFile");
            else if (spec.PromptFile is { } promptFile)
            {
                var path = Path.GetFullPath(Path.Combine(baseDir, promptFile));
                if (File.Exists(path)) prompt = File.ReadAllText(path);
                else e.Add($"promptFile not found: {path}");
            }

            if (prompt is not null && string.IsNullOrWhiteSpace(prompt)) e.Add("prompt is empty");
            if (plan.Config?.Effective.Tasks[spec.Id!].Settings.Profile is { Value: "clean", From: "tool" }
                && prompt is not null && Regex.IsMatch(prompt, @"\bskills?\b|`[A-Za-z0-9-]+:[A-Za-z0-9-]+`", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                plan.Warnings.Add($"{spec.Id}: the prompt mentions a skill, and the task defaults to profile clean, which loads no plugins or skills; set profile full if the worker needs them");
            if (spec.TimeoutMinutes is < 1 or > 720) e.Add("timeoutMinutes must be between 1 and 720");
            if (spec.ContinueOf is not null)
            {
                if (spec.Kind is not ("code" or "mechanical")) e.Add("continueOf requires kind code or mechanical");
                if (spec.Base is not null) e.Add("continueOf refuses base");
                if (spec.ReviewOf is not null) e.Add("continueOf refuses reviewOf");
                if (spec.Workspace == "in-place") e.Add("continueOf refuses in-place workspace");
            }
            if (Scratch.Required(spec))
            {
                try { _ = Scratch.Candidate(); }
                catch (HarnessException error) { e.Add(error.Message); }
            }
            else if (repoPath is null) e.Add("task needs repo or --repo");
            else if (spec.Kind == "structured")
            {
                // Project-enabled structured tasks use the named folder without creating a Git workspace.
                if (spec.Dir is { } dir && !Directory.Exists(Path.GetFullPath(dir, repo))) e.Add("structured project dir does not exist");
            }
            else if (spec.Workspace == "in-place")
            {
                try { await Worktree.FolderAsync(git, repo, spec.Dir, ct); }
                catch (HarnessException ex) { e.Add(ex.Message); }
                if (spec.Base is not null || spec.ReviewOf is not null) e.Add("in-place workspace refuses base and reviewOf");
                if (spec.Commit == "harness")
                {
                    if (await Worktree.DirtyAsync(git, repo, ct)) e.Add("workspace-dirty: in-place commit: harness requires a clean working tree");
                    if ((await git.RunAsync(repo, ct, "rev-parse", "--verify", "HEAD^{commit}")).ExitCode != 0) e.Add("in-place commit: harness requires a HEAD commit");
                }
            }
            else if (spec.ContinueOf is not null)
            {
                if (e.Count == 0)
                {
                    try
                    {
                        var source = await CheckContinuationAsync(spec, plan.Config?.Effective.Tasks[spec.Id!].Settings ?? new(), runId, repo, git, ct);
                        plan.Info.Add($"{spec.Id}: continues {source.Source.RunId}/{source.Task} from {source.CreationSha[..12]}");
                    }
                    catch (Exception ex) when (ex is HarnessException or IOException or UnauthorizedAccessException or JsonException)
                    { e.Add(ex.Message); }
                }
            }
            else if (spec.ReviewOf is null)
            {
                if (spec.Base is null) e.Add("base is required (a branch, tag or commit of the repo)");
                else
                {
                    var reference = Regex.Replace(spec.Base, @"(?:~[0-9]*|\^(?:[0-9]*|\{[^}]*\}))+$", "");
                    var symbolic = await git.RunAsync(repo, ct, "rev-parse", "--symbolic-full-name", reference);
                    if (symbolic.ExitCode == 0) reference = symbolic.Output.Trim();
                    if (reference.StartsWith("refs/remotes/", StringComparison.Ordinal)) reference = reference["refs/remotes/".Length..];
                    remotes ??= (await git.CheckAsync(repo, ct, "remote")).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    var remote = remotes.OrderByDescending(name => name.Length)
                        .FirstOrDefault(name => reference.StartsWith(name + "/", StringComparison.Ordinal) && reference.Length > name.Length + 1);
                    ProcessResult? fetch = null;
                    if (remote is not null)
                    {
                        var branch = reference[(remote.Length + 1)..];
                        var key = (remote, branch);
                        if (!fetches.TryGetValue(key, out fetch))
                        {
                            var environment = WorkerEnvironment.Snapshot();
                            // The CLI removes credentials from its own environment before planning; restore them only for this operator call.
                            if (Cli.RootEnvironment is { } root)
                                foreach (var (name, value) in root.Passthrough(root.Removed)) environment[name] = value;
                            var operatorGit = new Git(Executables.Resolve("git"), git.ControlFolder, environment);
                            fetch = await operatorGit.RunAsync(repo, ct, "fetch", "--quiet", remote, branch);
                            fetches.Add(key, fetch);
                        }
                    }
                    if (fetch is { ExitCode: not 0 })
                        e.Add($"base {spec.Base}: fetch from {remote} failed: {fetch.Stderr.Split('\n')[0].TrimEnd('\r')}");
                    else
                    {
                        var resolved = await git.RunAsync(repo, ct, "rev-parse", "--verify", "--quiet", $"{spec.Base}^{{commit}}");
                        if (resolved.ExitCode != 0) e.Add($"base {spec.Base} does not resolve in {repo}");
                        else
                        {
                            var sha = resolved.Output.Trim();
                            plan.Info.Add($"{spec.Id}: base {spec.Base} resolves to {sha[..12]}"
                                + (remote is null ? "" : $" (fetched from {remote})"));
                            using var reader = new StringReader(prompt ?? "");
                            var seen = new HashSet<string>(StringComparer.Ordinal);
                            while (reader.ReadLine() is { } line)
                            {
                                if (!Regex.IsMatch(line, @"(?<![0-9A-Za-z])base(?:[_-]?(?:sha|commit))?(?![0-9A-Za-z])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) continue;
                                foreach (Match match in Regex.Matches(line, @"(?<![0-9A-Za-z./:_-])[0-9a-fA-F]{7,64}(?![0-9A-Za-z])"))
                                {
                                    var token = match.Value.ToLowerInvariant();
                                    if (token.Any(c => c is >= '0' and <= '9') && token.Any(c => c is >= 'a' and <= 'f')
                                        && !line.AsSpan(match.Index + match.Length).StartsWith("..", StringComparison.Ordinal)
                                        && !sha.StartsWith(token, StringComparison.Ordinal) && seen.Add(token))
                                        plan.Warnings.Add($"{spec.Id}: the prompt names base commit {token}, but base {spec.Base} resolves to {sha[..12]}");
                                }
                            }
                        }
                    }
                }
            }
            else if (spec.Base is not null)
            {
                e.Add("a reviewOf task starts from the reviewed task's branch; omit base");
            }

            if (spec.Kind is "code" or "mechanical" && spec.Scope is not { Count: > 0 }) e.Add("code and mechanical tasks declare a scope");
            if (spec.Kind != "structured" && spec.Workspace != "in-place")
            {
                var path = Locations.Worktree(spec.Id ?? "", runId);
                if (OperatingSystem.IsWindows() && repoPath is not null)
                {
                    try
                    {
                        var head = await git.RunAsync(repo, ct, "rev-parse", "--verify", "--quiet", "HEAD");
                        var key = (repo, head.Output.Trim());
                        if (!depths.TryGetValue(key, out var measurement))
                        {
                            // Cache the tracked-path measurement, including failures, not the task's folder length.
                            measurement = Worktree.PathDepthAsync(git, repo, Locations.WorktreeRoot, ct);
                            depths.Add(key, measurement);
                        }
                        var depth = await measurement;
                        var length = depth.Length + Path.TrimEndingDirectorySeparator(path).Length - Path.TrimEndingDirectorySeparator(Locations.WorktreeRoot).Length;
                        if (length >= 248) plan.Warnings.Add($"{spec.Id}: {depth.Detail.Replace($"reach {depth.Length} characters", $"reach {length} characters", StringComparison.Ordinal)}");
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        plan.Warnings.Add($"{spec.Id}: worktree path depth not measured ({ex.Message})");
                    }
                }
                if (Path.Exists(path)) e.Add($"worktree {path} already exists; clean it first");
                if ((await git.RunAsync(repo, ct, "show-ref", "--verify", "--quiet", $"refs/heads/dispatch/{runId}/{spec.Id}")).ExitCode == 0)
                    e.Add($"branch dispatch/{runId}/{spec.Id} already exists; clean it first");
            }

            foreach (var error in e) plan.Errors.Add($"{spec.Id ?? "<no id>"}: {error}");
            if (e.Count == 0)
            {
                plan.Tasks.Add(new PlannedTask { Spec = spec, PromptText = prompt!, Route = route, PromptInputs = promptInputs });
            }
        }

        foreach (var task in plan.Tasks.Where(t => t.Spec.ReviewOf is not null))
        {
            var target = specs.FirstOrDefault(x => x.Id == task.Spec.ReviewOf);
            if (target is null || target.Id == task.Spec.Id) plan.Errors.Add($"{task.Spec.Id}: reviewOf names no other task");
            else if (target.ReviewOf is not null) plan.Errors.Add($"{task.Spec.Id}: reviewOf chains are not supported");
            if (task.Spec.Kind != "review") plan.Warnings.Add($"{task.Spec.Id}: reviewOf on a {task.Spec.Kind} task");
        }

        // Parallel writers must not share a declared file scope.
        var writers = plan.Tasks.Where(t => t.Spec.Kind is "code" or "mechanical").ToList();
        for (var i = 0; i < writers.Count; i++)
        {
            for (var j = i + 1; j < writers.Count; j++)
            {
                if (writers[i].Spec.Workspace == "in-place" && writers[j].Spec.Workspace == "in-place")
                {
                    plan.Errors.Add($"{writers[i].Spec.Id} and {writers[j].Spec.Id} write in the same in-place working tree");
                    continue;
                }
                var overlap = Scope.Overlap(writers[i].Spec.Scope!, writers[j].Spec.Scope!, file.SharedPaths);
                if (overlap is not null) plan.Errors.Add($"{writers[i].Spec.Id} and {writers[j].Spec.Id} share scope ({overlap})");
            }
        }

        return plan;
    }

    static string EmptyHint(AllowList allow) =>
        allow.IsRestricted && allow.Count == 0 ? " (the effective allow-list allows no opencode routes)" : "";

    public static async Task<(Ledger Source, string Task, TaskRecord Record, string CreationSha, string Final, string? Patch)> CheckContinuationAsync(
        TaskSpec spec, EffectiveSettings settings, string currentRun, string repo, Git git, CancellationToken ct)
    {
        var (source, task) = Ledger.OpenContinuation(spec.ContinueOf!, currentRun);
        var record = source.LoadRecord(task);
        var result = source.LoadResult(task);
        var lines = source.Read().Where(x => x.Task == task).ToList();
        if (source.TaskStates().GetValueOrDefault(task) != "failed" || result is not { State: "failed" }
            || result.Run != source.RunId || result.Task != task || record.Spec.Id != task)
            throw new HarnessException("continueOf source must have a consistent final failed result");
        var restored = TaskRun.FromLedger(record, source);
        if (result.Failures.Contains("zombie") || result.WorkerStopped == false || restored.Zombie || restored.WorkerStopped == false
            || lines.Skip(lines.FindLastIndex(x => x.Type == "prompt") + 1).LastOrDefault(x => x.Type is "stop-unconfirmed" or "hard-stop"
                || x.Type == "outcome" && x.Data?["workerStopped"] is not null) is { } stop
                && (stop.Type == "stop-unconfirmed" || stop.Data?[stop.Type == "hard-stop" ? "stopped" : "workerStopped"]?.GetValue<bool>() != true))
            throw new HarnessException("continueOf refuses a zombie or unconfirmed worker");
        if (Worktree.InPlace(record) || Worktree.CommitMode(record) != "harness" || result.CommitMode != "harness"
            || record.Workspace.Path is null || record.Workspace.Branch != $"dispatch/{source.RunId}/{task}" || record.Workspace.BaseRef is null)
            throw new HarnessException("continueOf requires a harness-managed worktree with harness commit mode");
        var stopped = result.WorkerStatus is "stopped" or "blocked";
        if (!stopped && result.Commit is null)
            throw new HarnessException("continueOf requires a stopped/blocked or committed failed target");
        var finalIndex = lines.FindLastIndex(x => x.Type == "final");
        if (finalIndex < 0) throw new HarnessException("continueOf source must have a consistent final failed result");
        var previous = lines.FindLastIndex(finalIndex - 1, x => x.Type is "final" or "prompt");
        var collected = lines.Skip(previous + 1).Take(finalIndex - previous - 1).LastOrDefault(x => x.Type == "collected");
        var patch = Path.Combine(source.Dir, task, "wip.patch");
        if (collected is null || collected.Data?["commit"]?.GetValue<string>() != result.Commit
            || collected.Data?["files"]?.GetValue<int>() != result.FilesChanged.Count
            || (result.WipPatch is not null
                ? !stopped || result.WipPatch != "wip.patch" || !File.Exists(patch) || result.WipFileCount != result.FilesChanged.Count || result.FilesChanged.Count == 0
                : File.Exists(patch) || result.WipFileCount is not null || stopped && result.FilesChanged.Count > 0))
            throw new HarnessException("continueOf source collection evidence is incomplete or inconsistent");
        var final = await File.ReadAllTextAsync(Path.Combine(source.Dir, task, "final.md"), ct);
        async Task<string> CommitAsync(string? sha, string label)
        {
            if (sha is null || !Regex.IsMatch(sha, "\\A[0-9a-f]{40}([0-9a-f]{24})?\\z")
                || (await git.RunAsync(repo, ct, "cat-file", "-t", sha)) is not { ExitCode: 0, Output: var type } || type.Trim() != "commit")
                throw new HarnessException($"continueOf {label} {sha ?? "(missing)"} does not resolve as a commit in {repo}");
            return sha;
        }
        var baseline = await CommitAsync(record.Workspace.BaseSha, "baseSha");
        var creation = stopped ? baseline : await CommitAsync(result.Commit, "commit");
        var ignoreCase = await Scope.IgnoreCaseAsync(git, repo, ct);
        var scope = settings.Scope ?? new EffectiveScope { Value = spec.Scope ?? [] };
        var outside = result.FilesChanged.Where(path => Commit.IsDispatch(path) || !Scope.InScope(path, scope, ignoreCase)).Order(StringComparer.Ordinal).ToList();
        if (outside.Count > 0) throw new HarnessException($"continueOf inherited paths outside scope: {string.Join(", ", outside)}");
        return (source, task, record, creation, final, result.WipPatch is null ? null : patch);
    }

    // Checks that need the server: the agent or route exists at the repo location, is enabled,
    // has its variant, is not manual-class; report shared review lineage as information only.
    public static async Task CheckLiveAsync(Plan plan, IWorkerBackend backend, AllowList allow, CancellationToken ct)
    {
        var checks = await backend.ValidateAsync(plan.Tasks.Where(task => (task.Spec.Backend ?? "opencode") == backend.Name).Select(task => new WorkerPlan(task.Spec.Id!, task.Spec.Kind!,
            task.Route ?? new RouteRef("agent", "placeholder", null), task.Spec.Agent, task.Spec.Persona,
            TaskStarter.ModesFor(new TaskRecord { Spec = task.Spec,
                Settings = plan.Config?.Effective.Tasks[task.Spec.Id!].Settings ?? new() }),
            Scratch.Required(task.Spec) ? Scratch.Candidate() : plan.Repo)).ToList(), ct);
        foreach (var check in checks)
        {
            plan.Errors.AddRange(check.Errors.Select(error => $"{check.TaskId}: {error}"));
            plan.Warnings.AddRange(check.Warnings.Select(warning => $"{check.TaskId}: {warning}"));
            plan.Info.AddRange((check.Info ?? []).Select(info => $"{check.TaskId}: {info}"));
            if (plan.Config is { } config && config.Effective.Tasks[check.TaskId].Settings.Profile is { } profile)
            {
                profile.Actual = check.Profile;
                profile.Reason = check.ProfileReason;
            }
            if (backend.Name == "codex" && plan.Config?.Effective.Tasks[check.TaskId].Settings is { } settings)
            {
                var enforcement = settings.Access?.Value == "full" ? "none" : "sandbox";
                if (settings.Access is { } access) access.Enforcement = enforcement;
                if (settings.Network is { } network) network.Enforcement = enforcement;
                foreach (var note in ConfigResolver.CapabilityNotes(settings))
                {
                    var warning = $"{check.TaskId}: {note}";
                    if (!plan.Warnings.Contains(warning)) plan.Warnings.Add(warning);
                    if (!plan.Config.Effective.Warnings.Contains(warning)) plan.Config.Effective.Warnings.Add(warning);
                }
            }
            if (backend.Name == "opencode" && plan.Config?.Effective.Tasks[check.TaskId].Settings is { } opencode
                && opencode.Access?.Value is "read-only" or "workspace")
            {
                if (Ready.FenceKind is { } kind)
                {
                    opencode.Access.Enforcement = "sandbox";
                    if (check.Errors.Count == 0) plan.Info.Add($"{check.TaskId}: write-fenced ({kind}; accident guard)");
                }
                else plan.Warnings.Add($"{check.TaskId}: opencode runs unfenced on Windows; access {opencode.Access.Value} is enforced by permission rules only");
            }
        }

        foreach (var task in plan.Tasks.Where(t => t.Spec.ReviewOf is not null))
        {
            var reviewer = checks.FirstOrDefault(x => x.TaskId == task.Spec.Id);
            var author = checks.FirstOrDefault(x => x.TaskId == task.Spec.ReviewOf);
            if (reviewer is not null && author is not null && reviewer.Lineage == author.Lineage)
            {
                plan.Info.Add($"{task.Spec.Id}: reviewer and author share lineage {author.Lineage}");
            }
        }

        if (allow.IsRestricted && allow.Count == 0) plan.Warnings.Add("the allow-list is empty");
    }
}
