static class Cli
{
    public static async Task<int> MainAsync(string[] argv)
    {
        // Ledger text and console numbers stay machine-neutral.
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        var a = Args.Parse(argv);
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            Console.Error.WriteLine("dispatch: stopping (the private server ends with this process)");
            cts.Cancel();
        };

        try
        {
            Locations.Resolve();
            a.ValidatePaths();
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
        catch (OpenCodeException ex)
        {
            Console.Error.WriteLine($"dispatch: the server call failed: {ex.GetType().Name}: {ex.Message}");
            return 2;
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            Console.Error.WriteLine("dispatch: cancelled");
            return 130;
        }
    }

    static int Usage()
    {
        Console.WriteLine($"""
            usage: dotnet dispatch.cs -- <command> [arguments]

              routes --dir <path>|--repo <path> [--all]
                                                   list roster agents and models at a location
              plan <tasks.jsonc> [--repo <path>] [--policy <file>]
                                                   validate a task file; sends no prompt
              run <tasks.jsonc> [--run <name>] [--max <n>] [--repo <path>] [--policy <file>]
                                                   dispatch, supervise, settle and verify
              status [<run>]                       summarize one run, or list runs
              resume <run> [--continue] [--policy <file>]
                                                   reconcile a run; --continue re-prompts
              cancel <task> [--run <run>]          interrupt a task (resume = false)
              collect <task> [--run <run>]         redo settle: messages, commit, diff, verify
              clean <run> [--force]                remove worktrees and branches
              asks [<run>]                         list escalated asks that wait for the operator
              reply <run> <askId> once|reject [--message <text>]
                                                   answer one escalated ask
              selfcheck --repo <path> [--policy <file>]
                                                   live check without any prompt

            Routes come only from {Locations.AllowListFile} (allow-list owner).
            Permissions and standing instructions come from {Locations.PolicyFile} (operator-owned).
            """);
        // Nothing started: an unknown or missing command is a refusal.
        return 2;
    }
}
