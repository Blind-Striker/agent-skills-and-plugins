static class ConfigResolver
{
    public static EffectiveConfig Resolve(TaskFile file, ConfigFile? user = null, ConfigFile? repo = null,
        ConfigPreferences? cli = null, ConfigFile? policy = null)
    {
        Validate(user, "user");
        Validate(repo, "repo");
        Validate(policy, "cli");
        var config = new EffectiveConfig
        {
            MaxParallel = new() { Value = Constants.DefaultMaxParallel, From = "tool" },
            Restrictions = new() { MaxAccess = new() { Value = "full", From = "tool" }, NetworkAllowed = new() { Value = true, From = "tool" } },
        };
        foreach (var (name, value) in new (string, int?)[] { ("user", user?.Preferences?.MaxParallel), ("repo", repo?.Preferences?.MaxParallel),
                     ("task-file-defaults", file.MaxParallel), ("cli", cli?.MaxParallel) })
            if (value is { } max) config.MaxParallel = new() { Value = max, From = name };
        if (config.MaxParallel.Value is < 1 or > 8) throw new HarnessException($"maxParallel from {config.MaxParallel.From} must be between 1 and 8");
        foreach (var (name, restrictions, instructions) in new (string, RestrictionValues?, List<string>?)[]
                 { ("user", user?.Restrictions, user?.Instructions), ("repo", repo?.Restrictions, repo?.Instructions),
                   ("task-file", file.Policy, file.Policy?.Instructions), ("cli", policy?.Restrictions, policy?.Instructions) })
            Accumulate(config, name, restrictions, instructions);
        foreach (var (name, routes) in new[] { ("user", user?.Restrictions?.Routes), ("repo", repo?.Restrictions?.Routes) })
        {
            if (routes is null) continue;
            var normalized = routes.Select(AllowList.Normalize).Distinct(StringComparer.Ordinal).ToList();
            if (config.Restrictions.Routes is null) config.Restrictions.Routes = new() { Value = normalized };
            else config.Restrictions.Routes.Value = config.Restrictions.Routes.Value.Intersect(normalized, StringComparer.Ordinal).ToList();
            config.Restrictions.Routes.From.Add(name);
        }
        foreach (var task in file.Tasks ?? [])
        {
            var settings = ResolveTask(task, file.Defaults, user?.Preferences, repo?.Preferences, cli, config);
            if (!config.Tasks.TryAdd(task.Id ?? throw new HarnessException("task id is required"), new() { Settings = settings }))
                throw new HarnessException($"{task.Id}: duplicate task id");
        }
        return config;
    }

    static EffectiveSettings ResolveTask(TaskSpec task, TaskDefaults? defaults, ConfigPreferences? user,
        ConfigPreferences? repo, ConfigPreferences? cli, EffectiveConfig config)
    {
        var layers = new (string Name, TaskPreferences? Preferences)[] { ("user", user), ("repo", repo),
            ("task-file-defaults", defaults), ("task", task), ("cli", cli) };
        Setting<T> Pick<T>(T? fallback, Func<TaskPreferences, T?> select) where T : class
        {
            var setting = new Setting<T> { Value = fallback, From = "tool" };
            foreach (var (name, prefs) in layers)
                if (prefs is not null && select(prefs) is { } value) setting = new() { Value = value, From = name };
            return setting;
        }
        var structured = task.Kind == "structured";
        var settings = new EffectiveSettings
        {
            Backend = Pick("", p => p.Backend),
            Project = Pick(structured ? "off" : "on", p => p.Project),
            Access = Pick(task.Kind is "code" or "mechanical" ? "workspace" : "read-only", p => p.Access),
            ReadOnlyShell = Pick("decline", p => p.ReadOnlyShell),
            Network = Pick("on", p => p.Network),
            Workspace = Pick("worktree", p => p.Workspace),
            TempRoot = Pick<string>(null, p => p.TempRoot),
            WritableRoots = new()
            {
                Value = layers.SelectMany(x => x.Preferences?.WritableRoots ?? []).Distinct(StringComparer.Ordinal).ToList(),
                From = string.Join(",", layers.Where(x => x.Preferences?.WritableRoots is { Count: > 0 }).Select(x => x.Name)) is { Length: > 0 } from ? from : "tool",
            },
            PrivateFolders = new()
            {
                Value = layers.SelectMany(x => x.Preferences?.PrivateFolders ?? []).Distinct(StringComparer.Ordinal).ToList(),
                From = string.Join(",", layers.Where(x => x.Preferences?.PrivateFolders is { Count: > 0 }).Select(x => x.Name)) is { Length: > 0 } privateFrom ? privateFrom : "tool",
            },
            Environment = new(WorkerEnvironment.NameComparer),
            Commit = Pick("none", p => p.Commit),
            Dir = new() { Value = task.Dir ?? defaults?.Dir ?? ".", From = task.Dir is not null ? "task" : defaults?.Dir is not null ? "task-file-defaults" : "tool" },
            RawPrompt = new() { Value = false, From = "tool" },
            TimeoutMinutes = new() { Value = Constants.DefaultTimeoutMinutes, From = "tool" },
            Scope = new() { Value = task.Scope?.ToList() ?? [], Ceilings = config.Restrictions!.Scope.ToList() },
        };
        foreach (var (name, prefs) in layers)
        {
            ValidateEnvironment(prefs, name);
            if (prefs?.Environment is { ValueKind: JsonValueKind.Object } environment)
                foreach (var variable in environment.EnumerateObject())
                    settings.Environment[variable.Name] = new() { Value = variable.Value.GetString(), From = name };
            if (prefs?.RawPrompt is { } raw) settings.RawPrompt = new() { Value = raw, From = name };
            if (prefs?.TimeoutMinutes is { } timeout) settings.TimeoutMinutes = new() { Value = timeout, From = name };
        }
        void Choice(Setting<string> setting, params string[] choices)
        {
            if (!choices.Contains(setting.Value, StringComparer.Ordinal))
                throw new HarnessException($"{task.Id}: {setting.Value} from {setting.From} must be one of {string.Join(", ", choices)}");
        }
        Choice(settings.Backend, "opencode", "codex");
        Choice(settings.Project, "on", "off");
        Choice(settings.Access, "read-only", "workspace", "full");
        if (settings.ReadOnlyShell.Value is not ("decline" or "escalate"))
            throw new HarnessException($"{task.Id}: readOnlyShell from {settings.ReadOnlyShell.From} must be decline or escalate");
        if (structured && settings.ReadOnlyShell.Value != "decline")
            throw new HarnessException($"{task.Id}: structured tasks require readOnlyShell decline; escalate came from {settings.ReadOnlyShell.From}");
        Choice(settings.Network, "on", "off");
        Choice(settings.Workspace, "worktree", "in-place");
        if (settings.TempRoot.Value is { } tempRoot && !Path.IsPathFullyQualified(tempRoot))
            throw new HarnessException("tempRoot must be an absolute path");
        foreach (var root in settings.WritableRoots.Value!)
        {
            if (!Path.IsPathFullyQualified(root)) throw new HarnessException($"{task.Id}: writableRoots from {settings.WritableRoots.From} must hold absolute paths: {root}");
            // A writable root must not open the harness's own folders to the worker, also through a link.
            foreach (var (folder, name) in new[] { (Locations.ControlRoot, "control root"), (Locations.LedgerRoot, "ledger root") })
                if (Worktree.IsInside(Worktree.RealPath(root), Worktree.RealPath(folder)) || Worktree.IsInside(Worktree.RealPath(folder), Worktree.RealPath(root)))
                    throw new HarnessException($"{task.Id}: writableRoots from {settings.WritableRoots.From} must not contain or lie inside the {name}: {root}");
        }
        foreach (var folder in settings.PrivateFolders.Value!)
        {
            if (!Path.IsPathFullyQualified(folder)) throw new HarnessException($"{task.Id}: privateFolders from {settings.PrivateFolders.From} must hold absolute paths: {folder}");
            var real = Worktree.RealPath(folder);
            foreach (var (root, name) in new[] { (Locations.ControlRoot, "control root"), (Locations.LedgerRoot, "ledger root") })
                if (Worktree.IsInside(real, Worktree.RealPath(root)) || Worktree.IsInside(Worktree.RealPath(root), real))
                    throw new HarnessException($"{task.Id}: privateFolders from {settings.PrivateFolders.From} must not contain or lie inside the {name}: {folder}");
            if (settings.WritableRoots.Value.Any(root => Locations.Same(real, Worktree.RealPath(root))))
                throw new HarnessException($"{task.Id}: privateFolders from {settings.PrivateFolders.From} must not equal writableRoots: {folder}");
            foreach (var (root, name) in new[] { (Locations.WorktreeRoot, "worktree root"), (settings.TempRoot.Value ?? Locations.WorktreeRoot, "temp root") })
                if (Worktree.IsInside(Worktree.RealPath(root), real))
                    throw new HarnessException($"{task.Id}: privateFolders from {settings.PrivateFolders.From} must not contain or equal the {name}: {folder}");
        }
        if (settings.PrivateFolders.Value.Count > 0 && !(OperatingSystem.IsLinux() && settings.Backend.Value == "opencode" && settings.Access.Value is "read-only" or "workspace"))
            config.Warnings.Add($"{task.Id}: privateFolders is enforced only by the opencode fence on Linux");
        if (settings.TempRoot.Value is { } absoluteTempRoot)
            foreach (var (root, name) in new[] { (Locations.WorktreeRoot, "worktree root"), (Locations.LedgerRoot, "ledger root") })
                if (Worktree.IsInside(absoluteTempRoot, root))
                    throw new HarnessException($"{task.Id}: tempRoot from {settings.TempRoot.From} must be outside the {name}");
        Choice(settings.Commit, "none", "harness");
        if (settings.TimeoutMinutes.Value is < 1 or > 720) throw new HarnessException($"{task.Id}: timeoutMinutes from {settings.TimeoutMinutes.From} must be between 1 and 720");
        if (settings.Workspace.Value == "worktree")
        {
            if (task.Commit is not null) throw new HarnessException($"{task.Id}: task commit is only valid in-place");
            settings.Commit = new() { Value = "harness", From = "worktree" };
        }
        var ceiling = config.Restrictions.MaxAccess!;
        if (AccessRank(settings.Access.Value!) > AccessRank(ceiling.Value!))
        {
            if (settings.Access.From != "tool") throw new HarnessException($"{task.Id}: access from {settings.Access.From} exceeds maxAccess from {ceiling.From}");
            settings.Access.Value = ceiling.Value;
            settings.Access.From = $"tool, clamped by {ceiling.From}";
        }
        if (!config.Restrictions.NetworkAllowed!.Value && settings.Network.Value == "on")
        {
            if (settings.Network.From != "tool") throw new HarnessException($"{task.Id}: network from {settings.Network.From} is forbidden by {config.Restrictions.NetworkAllowed.From}");
            settings.Network.Value = "off";
            settings.Network.From = $"tool, clamped by {config.Restrictions.NetworkAllowed.From}";
        }
        settings.Profile = Pick("clean", p => p.Profile);
        Choice(settings.Profile, "clean", "full");
        if (settings.Backend.Value == "codex" && (task.Agent is not null || task.Persona is not null))
            throw new HarnessException($"{task.Id}: codex refuses agent and persona");
        if (settings.Backend.Value == "codex" && settings.Access.Value == "full" && settings.Network.Value == "off")
            throw new HarnessException($"{task.Id}: codex cannot express access full from {settings.Access.From} with network off from {settings.Network.From}");
        string? model = null;
        var routeFrom = "tool";
        foreach (var (name, prefs) in layers)
        {
            var routes = prefs switch { ConfigPreferences c => c.DefaultRoutes, TaskDefaults d => d.DefaultRoutes, _ => null };
            var text = name == "task" ? task.Route : routes?.GetValueOrDefault(settings.Backend.Value!);
            string? variant = null;
            if (text is not null)
            {
                var colon = text.IndexOf(':');
                if (colon >= 0 && text[..colon] is "opencode" or "codex")
                {
                    var routeBackend = text[..colon];
                    if (routeBackend != settings.Backend.Value)
                        throw new HarnessException($"{task.Id}: route backend {routeBackend} from {name} differs from task backend {settings.Backend.Value} from {settings.Backend.From}");
                    text = text[(colon + 1)..];
                }
                var split = text.Split('#');
                if (split.Length > 2 || split[0].Length == 0 || (split.Length == 2 && split[1].Length == 0))
                    throw new HarnessException($"{task.Id}: invalid route from {name}: {text}");
                model = split[0];
                variant = split.Length == 2 ? split[1] : null;
                routeFrom = name;
            }
            if (variant is not null && prefs?.Effort is { } effort && effort != variant)
                throw new HarnessException($"{task.Id}: route variant and effort disagree in {name}");
            if (variant is not null || prefs?.Effort is not null)
                settings.Effort = new() { Value = variant ?? prefs!.Effort, From = name };
        }
        if (settings.Effort?.Value is { } effortValue && string.IsNullOrWhiteSpace(effortValue))
            throw new HarnessException($"{task.Id}: effort from {settings.Effort.From} must be non-empty");
        if ((model is null) == (task.Agent is null)) throw new HarnessException($"{task.Id}: name exactly one of route or agent after merging");
        var effectiveRoute = $"{settings.Backend.Value}:{model ?? task.Agent}{(settings.Effort is { Value: not null } && model is not null ? "#" + settings.Effort.Value : "")}";
        effectiveRoute = AllowList.Normalize(effectiveRoute);
        if (!AllowList.From(config.Restrictions.Routes).Allows(effectiveRoute))
            throw new HarnessException($"{task.Id}: route {effectiveRoute} is not on the allow-list from {string.Join(", ", config.Restrictions.Routes!.From)}");
        settings.Route = new() { Value = effectiveRoute, From = task.Agent is not null ? "task" : routeFrom };
        settings.Rules = config.Restrictions.Escalate.Concat(config.Restrictions.Deny).Select(rule =>
            new RuleEnforcement { Name = rule.Name, Enforcement = rule.Enforcement[settings.Backend.Value!] }).ToList();
        if (settings.Backend.Value == "codex")
            config.Warnings.AddRange(settings.Rules.Select(r => $"{task.Id}: {r.Name} is enforced at the approval gate only"));
        if (settings.Backend.Value == "codex") config.Warnings.AddRange(CapabilityNotes(settings).Select(note => $"{task.Id}: {note}"));
        if (settings.RawPrompt.Value && (config.Instructions.Count > 0 || task.Contracts is { Count: > 0 }))
            throw new HarnessException($"{task.Id}: rawPrompt from {settings.RawPrompt.From} conflicts with contracts or instructions from {string.Join(", ", config.Instructions.Select(i => i.From).Distinct())}");
        return settings;
    }

    public static List<string> CapabilityNotes(EffectiveSettings settings)
    {
        if (OperatingSystem.IsWindows() && settings.Backend?.Value == "opencode" && settings.Access?.Value is "read-only" or "workspace")
            return ["opencode runs unfenced on Windows"];
        if (settings.Backend?.Value != "codex" || settings.Access?.Value != "read-only") return [];
        var layers = (settings.Profile?.Actual ?? settings.Profile?.Value) == "clean"
            ? "only system-level" : "user and system-level";
        return [$"The pinned Codex app-server cannot disable inherited exec-policy rules; with this profile, {layers} explicit allow rules can bypass the read-only sandbox without approval."];
    }

    static void Accumulate(EffectiveConfig config, string layer, RestrictionValues? restrictions, List<string>? instructions)
    {
        var result = config.Restrictions!;
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (rules, target) in new[] { (restrictions?.Deny, result.Deny), (restrictions?.Escalate, result.Escalate) })
        {
            foreach (var rule in rules ?? [])
            {
                if (string.IsNullOrWhiteSpace(rule.Name) || !names.Add(rule.Name) || string.IsNullOrWhiteSpace(rule.Action)
                    || rule.Resources is not { Count: > 0 } || rule.Resources.Any(string.IsNullOrWhiteSpace))
                    throw new HarnessException($"{layer}: every rule needs a unique name, action and non-empty resources");
                target.Add(new() { Name = $"{layer}/{rule.Name}", Action = rule.Action, Resources = rule.Resources.ToList(),
                    Enforcement = new() { ["opencode"] = "policy", ["codex"] = "approval-gate" } });
            }
        }
        if (restrictions?.Scope is { } scope) result.Scope.Add(new() { Globs = scope.ToList(), From = layer });
        if (restrictions?.MaxAccess is { } access && AccessRank(access) < AccessRank(result.MaxAccess!.Value!))
            result.MaxAccess = new() { Value = access, From = layer };
        if (restrictions?.NetworkAllowed == false && result.NetworkAllowed!.Value)
            result.NetworkAllowed = new() { Value = false, From = layer };
        if (restrictions is ConfigRestrictions { Scrub: { } scrub })
            result.Scrub = (result.Scrub ?? []).Concat(scrub).Distinct(StringComparer.Ordinal).ToList();
        foreach (var instruction in instructions ?? [])
        {
            if (string.IsNullOrWhiteSpace(instruction)) throw new HarnessException($"{layer}: instructions must be non-empty");
            if (config.Instructions.All(i => i.Text != instruction)) config.Instructions.Add(new() { Text = instruction, From = layer });
        }
    }

    public static bool InScope(string path, EffectiveScope? scope)
    {
        bool Pass(List<string> globs) => (!globs.Any(g => !g.StartsWith('!')) || Scope.InScope(path, globs.Where(g => !g.StartsWith('!')).ToList()))
            && !globs.Where(g => g.StartsWith('!')).Any(g => Scope.InScope(path, [g[1..]]));
        return scope is null || (Pass(scope.Value) && scope.Ceilings.All(c => Pass(c.Globs)));
    }

    static int AccessRank(string value) => value switch { "read-only" => 0, "workspace" => 1, "full" => 2,
        _ => throw new HarnessException($"invalid maxAccess: {value}") };

    public static void Validate(ConfigFile? file, string layer)
    {
        if (file is null) return;
        if (layer != "user" && (file.Locations is not null || file.Backends is not null))
            throw new HarnessException($"{layer}: {(file.Backends is not null ? "backends" : "locations")} is user-layer-only");
        if (layer == "repo" && file.Preferences?.Access == "full") throw new HarnessException("repo: preferences.access full is forbidden");
        if (layer == "cli" && (file.Preferences is not null || file.Restrictions?.Routes is not null || file.Restrictions?.Scrub is not null))
            throw new HarnessException("cli: --policy refuses preferences, restrictions.routes and restrictions.scrub; only restrictions and instructions are allowed");
        ValidateEnvironment(file.Preferences, layer);
        foreach (var backend in new[] { file.Backends?.Opencode, file.Backends?.Codex })
        {
            if (backend?.Executable is { } executable && !Path.IsPathFullyQualified(executable))
                throw new HarnessException($"{layer}: backends executable must be an absolute path");
            if (backend?.Env?.Any(n => !Regex.IsMatch(n, "^[A-Za-z_][A-Za-z0-9_]*$")) == true)
                throw new HarnessException($"{layer}: backends env must contain environment variable names");
        }
        foreach (var pattern in file.Restrictions?.Scrub ?? [])
        {
            try { _ = new Regex(pattern, RegexOptions.IgnoreCase); }
            catch (ArgumentException ex) { throw new HarnessException($"{layer}: invalid scrub pattern: {ex.Message}"); }
        }
        if (file.Preferences?.DefaultRoutes?.Keys.Any(k => k is not ("opencode" or "codex")) == true)
            throw new HarnessException($"{layer}: defaultRoutes keys must be opencode or codex");
    }

    public static void ValidateEnvironment(TaskPreferences? preferences, string layer)
    {
        if (preferences is null || preferences.Environment.ValueKind == JsonValueKind.Undefined) return;
        var environment = preferences.Environment;
        if (environment.ValueKind != JsonValueKind.Object)
            throw new HarnessException($"{layer}: environment must be an object of variable names to strings");
        foreach (var variable in environment.EnumerateObject())
        {
            if (!Regex.IsMatch(variable.Name, "\\A[A-Za-z_][A-Za-z0-9_]*\\z"))
                throw new HarnessException($"{layer}: environment has an invalid variable name: {variable.Name}");
            if (variable.Value.ValueKind != JsonValueKind.String)
                throw new HarnessException($"{layer}: environment value for {variable.Name} must be a string");
            if (new[] { "TEMP", "TMP", "TMPDIR", "XDG_RUNTIME_DIR", "XDG_CACHE_HOME", "GIT_TERMINAL_PROMPT", "GIT_INDEX_FILE", "GIT_OPTIONAL_LOCKS",
                    "CODEX_HOME", "OPENCODE_CONFIG_DIR", "OPENCODE_CONFIG", "OPENCODE_CONFIG_CONTENT", "OPENCODE_DISABLE_CLAUDE_CODE",
                    "OPENCODE_CONFIG_PROJECT_DISABLE", "OPENCODE_DISABLE_PROJECT_CONFIG", "OPENCODE_FILEWATCHER_DISABLE", "OPENCODE_DISABLE_FILEWATCHER" }
                .Contains(variable.Name, WorkerEnvironment.NameComparer)
                || new[] { "OPENCODE_", "CODEX_" }.Any(prefix => variable.Name.StartsWith(prefix,
                    OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)))
                throw new HarnessException($"{layer}: environment may not set {variable.Name}: the harness owns it");
        }
    }
}
