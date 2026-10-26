static class Cli
{
    public static WorkerEnvironment.Scrubbed? RootEnvironment { get; private set; }

    public static void RestrictEnvironment(IEnumerable<string>? patterns)
    {
        if (RootEnvironment is not { } previous || patterns is null) return;
        RootEnvironment = previous.Restrict(patterns);
        foreach (var name in RootEnvironment.Removed.Except(previous.Removed, StringComparer.Ordinal))
            Environment.SetEnvironmentVariable(name, null);
    }
    public static async Task<int> MainAsync(string[] argv, string? sourceFile = null)
    {
        // Ledger text and console numbers stay machine-neutral.
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            Console.Error.WriteLine("dispatch: stopping (the private server ends with this process)");
            cts.Cancel();
        };

        try
        {
            var a = Args.Parse(argv);
            Locations.Resolve();
            a.ValidatePaths();
            ConfigLayers.Initialize(a);
            RootEnvironment = WorkerEnvironment.Scrub(WorkerEnvironment.Snapshot(), ConfigLayers.User?.Restrictions?.Scrub);
            foreach (var name in RootEnvironment.Removed) Environment.SetEnvironmentVariable(name, null);
            using var gitControl = Git.Initialize();
            WarnPluginCache(a, sourceFile);
            return a.Command switch
            {
                "routes" => await Commands.RoutesAsync(a, cts.Token),
                "plan" => await Commands.PlanAsync(a, cts.Token),
                "run" => await Commands.RunAsync(a, cts.Token),
                "status" => Commands.Status(a),
                "wait" => await Commands.WaitAsync(a, cts.Token),
                "resume" => await Commands.ResumeAsync(a, cts.Token),
                "cancel" => Commands.Cancel(a),
                "collect" => await Commands.CollectAsync(a, cts.Token),
                "clean" => await Commands.CleanAsync(a, cts.Token),
                "asks" => Commands.Asks(a),
                "reply" => await Commands.ReplyAsync(a, cts.Token),
                "selfcheck" => await Commands.SelfCheckAsync(a, cts.Token),
                _ => Usage(),
            };
        }
        catch (HarnessException ex)
        {
            Console.Error.WriteLine($"dispatch: {ex.Message}");
            return 2;
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            Console.Error.WriteLine("dispatch: cancelled");
            return 2;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"dispatch: {ex.GetType().Name}: {ex.Message.Replace('\r', ' ').Replace('\n', ' ')}");
            return 2;
        }
    }

    static string SourceFile() => AppContext.GetData("EntryPointFilePath") is string { Length: > 0 } entry
        ? Path.Combine(Path.GetDirectoryName(entry)!, "src", "Cli", "Cli.cs") : CallerSourceFile();

    static string CallerSourceFile([System.Runtime.CompilerServices.CallerFilePath] string path = "") => path;

    static void WarnPluginCache(Args a, string? sourceFile)
    {
        if (a.Command is not ("selfcheck" or "plan" or "run")) return;
        try
        {
            var home = Environment.GetEnvironmentVariable("CODEX_HOME");
            if (string.IsNullOrEmpty(home)) home = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
            var cache = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Worktree.RealPath(Path.Combine(home, "plugins", "cache")))) + Path.DirectorySeparatorChar;
            var source = Path.GetFullPath(Worktree.RealPath(Path.Combine(Path.GetDirectoryName(sourceFile ?? SourceFile())!, "..", "..")));
            if (source.StartsWith(cache, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                Console.Error.WriteLine("this harness runs from Codex's plugin cache, which keeps one version and may replace it during a run; start it from a stable copy outside that cache");
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException or System.Security.SecurityException)
        {
            // An advisory path check must never prevent the command from running.
        }
    }

    static int Usage()
    {
        Console.WriteLine($"""
            usage: dotnet dispatch.cs -- <command> [arguments]

              routes [--backend <name>] --repo <path> [--all]
                                                   list roster agents and models at a location
              plan <tasks.jsonc> [--repo <path>] [--policy <file>]
                                                   validate a task file; sends no prompt
              run <tasks.jsonc> [--run <name>] [--max <n>] [--repo <path>] [--policy <file>]
                                                   dispatch, supervise, settle and verify
              status [<run>]                       summarize one run, or list runs
              wait <run> [--timeout <minutes>] [--ledger <dir>]
                                                   wait for final tasks (default 60 minutes)
              resume <run> [--continue] [--policy <file>]
                                                   reconcile a run; --continue re-prompts
              cancel <run> <task>                 interrupt a task (resume = false)
              collect <run> <task>                redo settle: messages, commit, diff, verify
              clean <run> [--force]                remove worktrees and branches
              asks [<run>]                         list escalated asks that wait for the operator
              reply <run> <askId> once|reject [--message <text>]
                                                   answer one escalated ask
              selfcheck [<tasks.jsonc>] [--repo <path>] [--backend <name>] [--policy <file>]
                                                   live check without any prompt

            User config: {Locations.UserConfigFile}
            Repository config: committed .dispatch/config.jsonc at HEAD.
            Preferences override user, repository, task file, then CLI; restrictions accumulate.
            --ledger <absolute path> overrides the ledger root for every command.
            --contract <absolute path> prepends a contract to every task; repeatable.
            --read-only-shell decline|escalate selects the read-only shell approval policy (default decline).

            Windows user PATH ({UserPathGuard.ValueName}):
              selfcheck warns above {UserPathGuard.WarningLength:N0} characters.
              run refuses above {UserPathGuard.RefusalLength:N0} characters (exit 2).
              Inspect with: reg query HKCU\Environment /v Path
              At run end, a changed value prints a warning and records added/removed entries
              in the ledger. Configure tool first-use behaviour through preferences.environment;
              see references/config.md and references/cookbook.md.
            """);
        // Nothing started: an unknown or missing command is a refusal.
        return 2;
    }
}
