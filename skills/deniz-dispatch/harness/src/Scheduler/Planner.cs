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
        var repo = Path.GetFullPath(repoOverride ?? (file.Repo is { } r ? Path.Combine(baseDir, r) : throw new HarnessException("task file needs repo or --repo")));
        var plan = new Plan { File = file, TasksPath = tasksPath, Repo = repo };
        var useDefaultGit = git is null;
        git ??= Git.Default;
        var top = await git.RunAsync(repo, ct, "rev-parse", "--show-toplevel");
        if (top.ExitCode != 0)
        {
            plan.Errors.Add($"repo {repo} is not a git work tree");
            return plan;
        }
        repo = Path.GetFullPath(top.Output.Trim());
        plan = new Plan { File = file, TasksPath = tasksPath, Repo = repo };

        var specs = file.Tasks ?? [];
        if (args is not null)
        {
            plan.Config = await ConfigLayers.LoadAsync(repo, file, tasksPath, args, ct, git);
            if (useDefaultGit) git = Git.Default;
            allow = plan.Config.Allow;
            foreach (var spec in specs) ConfigLayers.Apply(spec, plan.Config.Effective.Tasks[spec.Id!].Settings);
            plan.Warnings.AddRange(plan.Config.Effective.Warnings);
        }
        var runId = Commands.CreateRunId(args?.Get("run") ?? file.Run ?? Path.GetFileNameWithoutExtension(tasksPath));
        if (specs.Any(x => x.Workspace == "in-place") && Worktree.IsInside(Locations.WorktreeRoot, repo))
            plan.Errors.Add("in-place workspace refuses a worktree root inside the repository");
        if (specs.Any(x => x.Workspace == "in-place") && Worktree.IsInside(Locations.LedgerRoot, repo))
            plan.Errors.Add("in-place workspace refuses a ledger root inside the repository");
        foreach (var path in file.SharedPaths ?? [])
            if (string.IsNullOrWhiteSpace(path) || Path.IsPathFullyQualified(path) || path.Replace('\\', '/').Split('/').Contains("..") || path.IndexOfAny(['*', '?', '!']) >= 0)
                plan.Errors.Add("sharedPaths must contain repository-relative paths, not globs or parent paths");
        if (specs.Count == 0) plan.Errors.Add("no tasks");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var contractCache = new Dictionary<string, Prompt.Contract>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (var spec in specs)
        {
            var e = new List<string>();
            Prompt.Inputs? promptInputs = null;
            try { promptInputs = Prompt.Load(file, spec, tasksPath, args, plan.Config?.Policy.Instructions ?? [], contractCache); }
            catch (Exception ex) when (ex is HarnessException or IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
            { e.Add(ex.Message); }
            if (!Regex.IsMatch(spec.Id ?? "", "^[a-z0-9][a-z0-9-]{0,39}$")) e.Add("id must match ^[a-z0-9][a-z0-9-]{0,39}$");
            else if (!ids.Add(spec.Id!)) e.Add("duplicate id");
            if (!Constants.Kinds.Contains(spec.Kind)) e.Add($"kind must be one of {string.Join(", ", Constants.Kinds)}");
            if (spec.Kind == "structured" && spec.Shell is not null) e.Add("shell is forbidden for structured tasks");
            else if (spec.Shell is not null || spec.Verify is { Count: > 0 })
            {
                try { Verifier.ResolveShell(spec, executableSearchPath); }
                catch (HarnessException ex) { e.Add($"verify shell: {ex.Message}"); }
            }

            RouteRef? route = null;
            if ((spec.Agent is null) == (spec.Route is null)) e.Add("name exactly one of agent or route");
            if (spec.Agent is not null && spec.Persona is not null) e.Add("persona goes with route, not with agent");
            if (spec.Agent is { } agent && !allow.AllowsAgent(agent)) e.Add($"agent {agent} is not on the allow-list{EmptyHint(allow)}");
            if (spec.Route is { } routeText)
            {
                if (!RouteRef.TryParse(routeText, out route, out var error)) e.Add($"route: {error}");
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
            if (spec.TimeoutMinutes is < 1 or > 720) e.Add("timeoutMinutes must be between 1 and 720");
            if (spec.Workspace == "in-place")
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
            else if (spec.ReviewOf is null)
            {
                if (spec.Base is null) e.Add("base is required (a branch, tag or commit of the repo)");
                else if ((await git.RunAsync(repo, ct, "rev-parse", "--verify", "--quiet", $"{spec.Base}^{{commit}}")).ExitCode != 0) e.Add($"base {spec.Base} does not resolve in {repo}");
            }
            else if (spec.Base is not null)
            {
                e.Add("a reviewOf task starts from the reviewed task's branch; omit base");
            }

            if (spec.Kind is "code" or "mechanical" && spec.Scope is not { Count: > 0 }) e.Add("code and mechanical tasks declare a scope");
            if (spec.Workspace != "in-place")
            {
                var path = Locations.Worktree(spec.Id ?? "", runId);
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

    // Checks that need the server: the agent or route exists at the repo location, is enabled,
    // has its variant, is not manual-class; review lineage differs from the author's.
    public static async Task CheckLiveAsync(Plan plan, IWorkerBackend backend, AllowList allow, CancellationToken ct)
    {
        var checks = await backend.ValidateAsync(plan.Tasks.Select(task => new WorkerPlan(task.Spec.Id!, task.Spec.Kind!,
            task.Route ?? new RouteRef("agent", "placeholder", null), task.Spec.Agent, task.Spec.Persona,
            TaskStarter.ModesFor(new TaskRecord { Spec = task.Spec,
                Settings = plan.Config?.Effective.Tasks[task.Spec.Id!].Settings ?? new() }), plan.Repo)).ToList(), ct);
        foreach (var check in checks)
        {
            plan.Errors.AddRange(check.Errors.Select(error => $"{check.TaskId}: {error}"));
            plan.Warnings.AddRange(check.Warnings.Select(warning => $"{check.TaskId}: {warning}"));
        }

        foreach (var task in plan.Tasks.Where(t => t.Spec.ReviewOf is not null))
        {
            var reviewer = checks.FirstOrDefault(x => x.TaskId == task.Spec.Id);
            var author = checks.FirstOrDefault(x => x.TaskId == task.Spec.ReviewOf);
            if (reviewer is not null && author is not null && reviewer.Lineage == author.Lineage)
            {
                plan.Errors.Add($"{task.Spec.Id}: reviewer and author share lineage {author.Lineage}");
            }
        }

        if (allow.IsRestricted && allow.Count == 0) plan.Warnings.Add("the allow-list is empty");
    }
}
