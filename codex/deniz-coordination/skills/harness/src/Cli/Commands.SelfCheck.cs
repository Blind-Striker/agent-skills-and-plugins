static partial class Commands
{
    public static async Task<int> SelfCheckAsync(Args a, CancellationToken ct, Func<string?>? userPathReader = null)
    {
        var userPath = new UserPathGuard(userPathReader);
        if (userPath.SelfCheckWarning is { } warning) Console.Error.WriteLine(warning);
        var checks = new List<ProbeCheck>();
        var warnings = 0;
        void Check(string name, bool ok, string detail, bool warn = false)
        {
            checks.Add(new(name, ok, detail));
            if (ok && warn) warnings++;
            Console.WriteLine($"{(!ok ? "FAIL" : warn ? "WARN" : "PASS")}  {name}: {detail}");
        }

        var tasksPath = a.Positional.FirstOrDefault();
        TaskFile file;
        try { file = tasksPath is null ? new TaskFile() : Json.ReadTaskFile(File.ReadAllText(tasksPath)); }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        { throw new HarnessException($"{tasksPath} (task-file): {ex.Message}"); }
        var repo = a.Get("repo") ?? (file.Repo is { } taskRepo ? Path.GetFullPath(taskRepo, Path.GetDirectoryName(tasksPath!)!) : null);
        await using var commandScope = new BackendScope([]);
        var commandFolder = commandScope.OwnedFolder!;
        var temp = Directory.CreateDirectory(Path.Combine(commandFolder, "tmp")).FullName;
        var environment = WorkerEnvironment.Scrub(WorkerEnvironment.Snapshot()).Variables;
        foreach (var name in new[] { "TEMP", "TMP", "TMPDIR" }) environment[name] = temp;
        var git = new Git(Executables.Resolve("git"), commandFolder, environment);
        var layers = await ConfigLayers.LoadAsync(repo, file, tasksPath, a, ct, git);
        layers.PrintWarnings();
        var selectedBackends = BackendScope.Selected(a, layers);
        if (a.Get("backend") is null && a.Positional.Count == 0)
        {
            if (selectedBackends.Count == 0) selectedBackends = InstalledBackends();
            // A backend the configuration does not name is not checked, but selfcheck says so.
            else foreach (var name in new[] { "opencode", "codex" }.Where(x => !selectedBackends.Contains(x) && Installed(x)))
                Console.WriteLine($"SKIP  backend {name}: installed but not configured; selfcheck --backend {name} checks it");
        }
        var allow = layers.Allow;
        Check("allow-list", true, allow.IsRestricted ? "route restrictions apply per backend" : "no route restriction");
        Check("policy", true, layers.Policy.Describe());
        Console.WriteLine($"state folder: {Locations.StateRoot}");
        Console.WriteLine($"worktree root: {Locations.WorktreeRoot}");
        Console.WriteLine($"control root: {Locations.ControlRoot}");
        Directory.CreateDirectory(Locations.StateRoot);
        if (OperatingSystem.IsWindows())
        {
            var longpaths = await git.RunAsync(repo ?? Locations.StateRoot, ct, "config", "--type=bool", "--get", "core.longpaths");
            if (longpaths.ExitCode != 0 || longpaths.Output.Trim() != "true")
                Console.WriteLine("warning: core.longpaths is not true; set core.longpaths to true or locations.worktreeRoot to a short folder for deep trees");
            else Check("core.longpaths", true, "true");
        }

        string? worktree = null;
        if (repo is not null)
        {
            if (OperatingSystem.IsWindows())
            {
                try
                {
                    var depth = await Worktree.PathDepthAsync(git, repo,
                        Locations.Worktree("selfcheck", $"{DateTime.UtcNow:yyyy-MM-dd}-selfcheck"), ct);
                    Check("worktree path depth", true, depth.Length >= 248 ? depth.Detail : depth.Detail[..depth.Detail.IndexOf("; builds", StringComparison.Ordinal)], warn: depth.Length >= 248);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                { Check("worktree path depth", true, ex.Message, warn: true); }
            }
            worktree = Path.Combine(Locations.WorktreeRoot, $"selfcheck-{Guid.NewGuid():N}");
            var add = await git.RunAsync(repo, ct, "worktree", "add", "--detach", worktree, "HEAD");
            Check("worktree", add.ExitCode == 0, add.ExitCode == 0 ? worktree : add.Stderr.Trim());
            if (add.ExitCode != 0) return 1;
        }
        else Console.WriteLine("repository checks skipped (no --repo or task-file repo)");

        // Launch folders outlive their backend scope: an unfenced opencode server keeps its project
        // directory open until it stops, and Windows refuses to delete an open directory.
        var launchFolders = new List<string>();
        try
        {
            foreach (var selected in selectedBackends)
            {
                Console.WriteLine($"backend {selected}");
                if (selected == "opencode")
                {
                    var refusal = await Ready.FenceAsync(ct);
                    Check("opencode fence", refusal is null, refusal ?? Ready.FenceKind ?? "unfenced on Windows", warn: Ready.FenceKind is null);
                }
                try
                {
                    await using var scope = new BackendScope([selected]);
                    Console.WriteLine($"control folder: {scope.OwnedFolder}");
                    var directory = worktree ?? scope.OwnedFolder!;
                    var backend = scope.Backends[selected];
                    if (selected == "codex")
                    {
                        var home = Environment.GetEnvironmentVariable("CODEX_HOME")
                            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
                        Console.WriteLine($"hardlink volume: clean Codex home under {Locations.ControlRoot} and user Codex home {home} must share one volume; otherwise clean-partial is used");
                    }
                    var probe = await backend.ProbeAsync(new(directory, repo is null ? null : TaskStarter.RulesFor(layers.Policy)), ct);
                    foreach (var check in probe.Checks ?? []) Check(check.Name, check.Ok, check.Detail);
                    if (OperatingSystem.IsWindows() && backend is CodexBackend codex && probe.Ready)
                    {
                        var home = Environment.GetEnvironmentVariable("CODEX_HOME")
                            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
                        var mode = CodexHome.ReadWindowsSandbox(home) ?? "not set";
                        Check("windows sandbox", true, $"mode {mode}; readiness {codex.WindowsSandboxReadiness}",
                            warn: codex.WindowsSandboxReadiness != "ready");
                    }
                    var pin = selected == "codex" ? CodexBackend.PinnedVersion : Constants.PinnedUpstream;
                    Check("pin", probe.Version == pin, $"installed {probe.Version ?? "unknown"}, pin {pin}");
                    Check("ready", probe.Ready, probe.Ready ? "backend probe passed" : "backend probe failed");
                    foreach (var error in probe.Errors) Check("probe", false, error);
                    foreach (var detail in probe.Warnings) Console.WriteLine($"warning: {detail}");
                    var listing = await backend.ListRoutesAsync(directory, ct);
                    Check("login", probe.Ready && (selected == "codex" || listing.Routes.Any(x => x.Enabled)),
                        selected == "codex" ? "login status and account/read confirmed signed in" : "enabled provider models available; login is provider-specific");
                    Check("model catalog", listing.Routes.Any(x => x.Enabled), $"{listing.Routes.Count} models, {listing.Routes.Count(x => x.Enabled)} enabled");
                    var allowedRoutes = selected == "codex"
                        ? (layers.Effective.Restrictions?.Routes?.Value ?? []).Where(x => x.StartsWith("codex:", StringComparison.Ordinal)).Select(CodexBackend.ParseRoute)
                        : allow.Routes;
                    foreach (var route in allowedRoutes)
                    {
                        var model = listing.Routes.FirstOrDefault(x => x.Route.Provider == route.Provider && x.Route.Model == route.Model);
                        var manual = listing.Agents.Any(x => x.Manual && RouteRef.TryParse(x.Model ?? "", out var pinned, out _) && pinned!.Matches(route));
                        var reason = model is null ? "not listed at this location" : !model.Enabled ? "listed but disabled"
                            : route.VariantOrDefault != "default" && !model.Variants.Contains(route.Variant!) ? "variant not listed" : manual ? "manual-class" : null;
                        Check($"allowed route {route}", reason is null, reason ?? "listed");
                    }
                    foreach (var agent in selected == "opencode" ? allow.Agents : [])
                    {
                        var entry = listing.Agents.FirstOrDefault(x => x.Name == agent);
                        Check($"allowed agent {agent}", entry?.Usable == true, entry?.Reason ?? (entry is null ? "agent not found at this location" : "listed"));
                    }

                    if (listing.Routes.FirstOrDefault(x => x.Enabled) is not { } candidate)
                    {
                        Check("clean profile", false, "no enabled model available for the smoke check");
                        continue;
                    }
                    var tasks = layers.Effective.Tasks.Where(x => x.Value.Settings.Backend?.Value == selected).ToArray();
                    var settings = tasks.Length > 0 ? tasks[0].Value.Settings : SelfCheckSettings(a, layers, selected, candidate.Route);
                    if (selected == "codex")
                    {
                        void Sandbox(EffectiveSettings effective, string suffix)
                        {
                            var mode = effective.Access?.Value switch
                            { "read-only" => "read-only", "workspace" => "workspace-write", "full" => "danger-full-access", _ => throw new HarnessException("invalid Codex access") };
                            Console.WriteLine($"sandbox: {mode}; network {effective.Network?.Value}{suffix}");
                        }
                        if (tasks.Length == 0) Sandbox(settings, "");
                        else foreach (var (id, task) in tasks) Sandbox(task.Settings, $" ({id})");
                    }
                    var clean = await backend.ValidateAsync([new("selfcheck", "code", candidate.Route, null, null,
                        new("clean", settings.Project!.Value!, settings.Access!.Value!, settings.Network!.Value!, "worktree"), directory)], ct);
                    foreach (var result in clean)
                    {
                        Check("clean profile", result.Errors.Count == 0, result.Profile + (result.ProfileReason is null ? "" : $"; {result.ProfileReason}"),
                            warn: result.Profile == "clean-partial");
                        foreach (var error in result.Errors) Check("clean profile validation", false, error);
                        foreach (var detail in result.Warnings) Console.WriteLine($"warning: {detail}");
                    }
                    var runId = Path.GetFileName(scope.OwnedFolder!);
                    var record = new TaskRecord
                    {
                        Spec = new() { Id = "selfcheck", Kind = "code", Route = selected == "codex" ? candidate.Route.Model : candidate.Route.ToString(), Backend = selected },
                        // Without a repository the launch needs a folder outside the control root, which the fence never grants.
                        Settings = settings, Workspace = new() { Path = worktree ?? Directory.CreateDirectory(Path.Combine(Locations.WorktreeRoot, runId, "workspace")).FullName },
                    };
                    WorkerRef? worker = null;
                    try
                    {
                        try
                        {
                            var (launch, _) = TaskStarter.BuildLaunch(record, runId, selected, layers.Policy, tempRoot: null);
                            worker = (await backend.CreateAsync(launch, ct)).Worker;
                        }
                        catch (WorkerCreationException ex) { worker = ex.Worker; throw; }
                        finally
                        {
                            try
                            {
                                if (worker is not null) await backend.ReleaseAsync(worker, CancellationToken.None);
                            }
                            finally
                            {
                                var folder = Path.Combine(Locations.WorktreeRoot, runId);
                                if (Directory.Exists(folder)) launchFolders.Add(folder);
                            }
                        }
                        Check("launch", true, worker.Id);
                    }
                    catch (Exception ex) when (!ct.IsCancellationRequested)
                    { Check("launch", false, ex.Message); }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                { Check("live", false, $"{selected}: {ex.GetType().Name}: {ex.Message}"); }
            }
        }
        finally
        {
            if (worktree is not null)
            {
                // Every backend scope above has been disposed. Windows can briefly retain a
                // process's directory handles after exit; retry only a failed filesystem deletion.
                var remove = await git.RunAsync(repo!, CancellationToken.None, "worktree", "remove", "--force", worktree);
                var removed = remove.ExitCode == 0;
                var detail = remove.Stderr.Trim();
                if (OperatingSystem.IsWindows() && !removed && Directory.Exists(worktree)
                    && remove.Stderr.Contains("failed to delete", StringComparison.OrdinalIgnoreCase)
                    && new[] { "Permission denied", "Invalid argument", "Directory not empty", "Device or resource busy" }
                        .Any(error => remove.Stderr.Contains(error, StringComparison.OrdinalIgnoreCase)))
                {
                    for (var retry = 0; retry < 3 && !removed; retry++)
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(100 * (retry + 1)), CancellationToken.None);
                        // Git can unregister a worktree even when deleting its directory fails.
                        // No -z: it needs Git 2.36, and Ubuntu 22.04 ships 2.34.
                        var listing = await git.RunAsync(repo!, CancellationToken.None, "worktree", "list", "--porcelain");
                        if (listing.ExitCode != 0) { detail = listing.Stderr.Trim(); break; }
                        var registered = listing.Output.ReplaceLineEndings("\n").Split('\n').Any(field => field.StartsWith("worktree ", StringComparison.Ordinal)
                            && Locations.Same(field[9..], worktree));
                        if (registered)
                        {
                            remove = await git.RunAsync(repo!, CancellationToken.None, "worktree", "remove", "--force", worktree);
                            removed = remove.ExitCode == 0;
                            detail = remove.Stderr.Trim();
                        }
                        else
                        {
                            try
                            {
                                if (Directory.Exists(worktree)) Directory.Delete(worktree, recursive: true);
                                removed = true;
                            }
                            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { detail = ex.Message; }
                        }
                    }
                }
                Check("worktree removed", removed, removed ? worktree : detail);
            }
        }
        foreach (var folder in launchFolders)
        {
            // Every backend scope has been disposed; Windows can still hold a directory handle briefly.
            string? left = null;
            for (var attempt = 0; attempt < 4; attempt++)
            {
                try { if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true); left = null; break; }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                { left = ex.Message; await Task.Delay(TimeSpan.FromMilliseconds(250 * (attempt + 1)), CancellationToken.None); }
            }
            if (left is not null) Check("launch folder removed", true, $"{folder} is left: {left}", warn: true);
        }
        var failed = checks.Count(x => !x.Ok);
        Console.WriteLine(failed == 0 ? $"selfcheck passed ({warnings} warning(s))" : $"selfcheck: {failed} check(s) failed");
        return failed == 0 ? 0 : 1;
    }

    // The configured backends in their order, then every other installed one (opencode, then codex).
    static bool Installed(string name)
    {
        if (name == "opencode") return OpenCodeInstalled(HarnessServer.Command(null)[0]);
        try
        {
            _ = CodexProcess.ResolveExecutable(null, WorkerEnvironment.Snapshot());
            return true;
        }
        catch (HarnessException ex)
        {
            // A resolved but unusable executable must reach the live check for its diagnostic.
            return !ex.Message.StartsWith("cannot start codex: required executable is not on PATH: codex.", StringComparison.Ordinal);
        }
    }

    static IReadOnlyList<string> InstalledBackends()
    {
        var installed = new List<string>();
        foreach (var name in new[] { "opencode", "codex" })
        {
            if (Installed(name)) installed.Add(name);
            else Console.WriteLine($"SKIP  backend {name}: not installed ({name} on PATH)");
        }
        if (installed.Count == 0)
            throw new HarnessException("no backend installed; install opencode or Codex, or set backends.<name>.executable");
        return installed;
    }

    static bool OpenCodeInstalled(string command)
    {
        if (command != Path.GetFileName(command)) return File.Exists(command);
        // OpenCodeDotNet.Sdk 0.9.0-preview.14 resolves bare commands in the parent environment,
        // retaining relative PATH entries and probing PATHEXT in order (not just .exe).
        var extensions = new[] { "" };
        if (OperatingSystem.IsWindows() && !command.AsSpan(1).Contains('.'))
        {
            var configured = Environment.GetEnvironmentVariable("PATHEXT");
            extensions = (string.IsNullOrWhiteSpace(configured) ? ".COM;.EXE;.BAT;.CMD" : configured)
                .Split(';', StringSplitOptions.RemoveEmptyEntries);
        }
        foreach (var entry in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var directory = Path.IsPathRooted(entry) ? entry : Path.Combine(Environment.CurrentDirectory, entry);
            foreach (var extension in extensions)
                if (File.Exists(Path.Combine(directory, command + extension))) return true;
        }
        return false;
    }

    static EffectiveSettings SelfCheckSettings(Args args, ConfigLayers layers, string backend, RouteRef route)
    {
        ConfigFile? Copy(string name, string layer)
        {
            if (!layers.Copies.TryGetValue(name + ".jsonc", out var bytes)) return null;
            var config = ConfigLayers.Read(bytes, layer, name + " config");
            // This synthetic task resolves modes only. Allowed routes are checked against the catalog separately.
            if (config.Restrictions is not null) config.Restrictions.Routes = null;
            return config;
        }
        var cli = ConfigLayers.Preferences(args);
        cli.Backend = backend;
        var file = new TaskFile { Tasks = [new() { Id = "selfcheck", Kind = "code", Route = backend == "codex" ? route.Model : $"{route.Provider}/{route.Model}" }] };
        return ConfigResolver.Resolve(file, Copy("user", "user"), Copy("repo", "repo"), cli, Copy("policy", "cli")).Tasks["selfcheck"].Settings;
    }
}
