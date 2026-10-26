static class Verifier
{
    public static string ResolveShell(TaskSpec spec, string? path = null)
    {
        var shell = spec.Shell ?? (OperatingSystem.IsWindows() ? "pwsh" : "bash");
        if (shell is not ("bash" or "pwsh")) throw new HarnessException("shell must be bash or pwsh");
        return Executables.Resolve(shell, path ?? Environment.GetEnvironmentVariable("PATH") ?? "");
    }

    public static async Task VerifyAsync(Ledger ledger, TaskRun run, TaskResult result, CancellationToken ct)
    {
        var log = new StringBuilder();
        var commands = (run.Record.Spec.Verify ?? []).Select(command => (Command: command, Advisory: false))
            .Concat((run.Record.Spec.AdvisoryVerify ?? []).Select(command => (Command: command, Advisory: true))).ToList();
        if (result.WorkerStatus is "stopped" or "blocked")
        {
            foreach (var (command, advisory) in commands)
            {
                result.Verify.Add(new VerifyResult { Command = command, Advisory = advisory ? true : null, NotRun = "worker stopped" });
                log.AppendLine($"$ {(advisory ? "(advisory) " : "")}{command}").AppendLine("[not run: worker stopped]");
            }
        }
        else if (result.Outcome == "succeeded" && commands.Count > 0)
        {
            var shell = ResolveShell(run.Record.Spec);
            result.VerifyShell = shell;
            var environment = WorkerEnvironment.Environment(run.Id, ledger.RunId, run.Record.Settings.TempRoot?.Value, run.Record.Settings.Environment).Variables;
            foreach (var (command, advisory) in commands)
            {
                var executed = await RunCommandAsync(run.Record.Spec, shell, run.Record.Workspace.Path!, environment, command, advisory, ct);
                if (executed.Error is { } error) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
                result.Verify.Add(executed.Result);
                log.Append(executed.Log);
            }
        }
        else if (commands.Count > 0) log.AppendLine($"verification skipped: outcome {result.Outcome}");
        await ledger.WriteArtifactAsync(run.Id, "verify.log", log.ToString(), ct);
        var data = new JsonObject { ["commands"] = result.Verify.Count, ["failed"] = result.Verify.Count(x => x.Advisory != true && Failed(x)) };
        if (result.Verify.Any(x => x.Advisory == true)) data["advisoryFailed"] = result.Verify.Count(x => x.Advisory == true && Failed(x));
        ledger.Write(run.Id, "verified", data);
    }

    public static bool Failed(VerifyResult result) => result.NotRun is null && (result.ExitCode != 0 || result.TimedOut);

    public static async Task<(VerifyResult Result, string Log, Exception? Error)> RunCommandAsync(TaskSpec spec, string? shell,
        string cwd, IReadOnlyDictionary<string, string> environment, string command, bool advisory, CancellationToken ct,
        CancellationToken cancelReason = default, TimeSpan? timeout = null)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, cancelReason);
        linked.Token.ThrowIfCancellationRequested();
        var started = Stopwatch.StartNew();
        ProcessResult outcome;
        Exception? error = null;
        try
        {
            shell ??= ResolveShell(spec);
            string[] args = (spec.Shell ?? (OperatingSystem.IsWindows() ? "pwsh" : "bash")) == "pwsh"
                ? ["-NoLogo", "-NoProfile", "-NonInteractive", "-Command", command]
                : ["--noprofile", "--norc", "-c", command];
            var executable = shell;
            if (!OperatingSystem.IsWindows())
            {
                executable = Executables.Resolve("nice");
                args = ["-n", "10", shell, .. args];
            }
            outcome = await Proc.RunAsync(executable, args, cwd, linked.Token, timeout ?? Constants.VerifyTimeout, belowNormal: true, environment: environment);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            error = ex;
            outcome = new(-1, [], ex.Message, false);
        }
        if (outcome.TimedOut && cancelReason.CanBeCanceled && !cancelReason.IsCancellationRequested)
        {
            var limit = timeout ?? Constants.VerifyTimeout;
            var amount = limit.TotalMinutes >= 1 ? limit.TotalMinutes : limit.TotalSeconds;
            var unit = limit.TotalMinutes >= 1 ? "minute" : "second";
            error = new TimeoutException(FormattableString.Invariant($"timed out after {amount:g} {unit}{(amount == 1 ? "" : "s")}"));
        }
        var result = new VerifyResult { Command = command, ExitCode = outcome.ExitCode, Seconds = Math.Round(started.Elapsed.TotalSeconds, 1),
            TimedOut = outcome.TimedOut, Advisory = advisory ? true : null };
        var log = new StringBuilder().AppendLine(advisory ? $"$ (advisory) {command}" : $"$ {command}").AppendLine("[stdout]").AppendLine(outcome.Output)
            .AppendLine("[stderr]").AppendLine(outcome.Stderr).AppendLine($"[exit {outcome.ExitCode} after {started.Elapsed.TotalSeconds:0.0}s]").AppendLine().ToString();
        return outcome.TimedOut && cancelReason.IsCancellationRequested ? CancelledCommand((result, log, error)) : (result, log, error);
    }

    public static (VerifyResult Result, string Log, Exception? Error) CancelledCommand((VerifyResult Result, string Log, Exception? Error) executed)
    {
        if (!executed.Result.TimedOut) return executed;
        executed.Result.ExitCode = -1;
        executed.Result.TimedOut = false;
        const string annotation = "\n[harness] timed out";
        var at = executed.Log.LastIndexOf(annotation + Environment.NewLine + "[exit ", StringComparison.Ordinal);
        if (at >= 0) executed.Log = executed.Log.Remove(at, annotation.Length);
        return executed;
    }
}
