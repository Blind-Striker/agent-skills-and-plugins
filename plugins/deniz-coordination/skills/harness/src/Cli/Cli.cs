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
    public static async Task<int> MainAsync(string[] argv)
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
            return a.Command switch
            {
                "routes" => await Commands.RoutesAsync(a, cts.Token),
                "plan" => await Commands.PlanAsync(a, cts.Token),
                "run" => await Commands.RunAsync(a, cts.Token),
                "status" => Commands.Status(a),
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
            --contract <absolute path|@worker-contract|@<model>-worker> prepends a contract to every task; repeatable.
            --read-only-shell decline|escalate selects the read-only shell approval policy (default decline).

            Windows user PATH ({UserPathGuard.ValueName}):
              selfcheck warns above {UserPathGuard.WarningLength:N0} characters.
              run refuses above {UserPathGuard.RefusalLength:N0} characters (exit 2).
              Inspect with: reg query HKCU\Environment /v Path
              At run end, a changed value prints a warning and records added/removed entries
              in the ledger. Child .NET commands disable global tools PATH appends.
            """);
        // Nothing started: an unknown or missing command is a refusal.
        return 2;
    }
}
