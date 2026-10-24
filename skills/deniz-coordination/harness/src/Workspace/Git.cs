sealed class Git
{
    static readonly Lazy<string> executable = new(() => Executables.Resolve("git"));
    static readonly Lazy<CommandControlFolder> commandControl = new(() => new());
    readonly string file;
    readonly string controlFolder;
    readonly Dictionary<string, string> environment;
    Action<string, JsonObject>? report;
    bool gated;
    readonly TimeSpan timeout;

    public Git(string executablePath, string controlFolder, IReadOnlyDictionary<string, string> environment, TimeSpan? timeout = null)
    {
        if (!Path.IsPathFullyQualified(executablePath)) throw new HarnessException("git executable must be an absolute path");
        if (!Path.IsPathFullyQualified(controlFolder)) throw new HarnessException("git control folder must be an absolute path");
        file = executablePath;
        this.controlFolder = controlFolder;
        this.environment = new(environment, OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        this.timeout = timeout ?? TimeSpan.FromMinutes(5);
    }

    public static IDisposable Initialize()
    {
        _ = executable.Value;
        return commandControl.Value;
    }

    public static Git ForTask(string task, string? run = null, string? control = null) => new(executable.Value,
        control ?? commandControl.Value.Path, WorkerEnvironment.Environment(task, run, RecordedTempRoot(task, run)).Variables);

    // A recorded task keeps the temp root it was planned with, also after a restart.
    public static string? RecordedTempRoot(string task, string? run)
    {
        var ledger = run is null ? null : new Ledger(run);
        return ledger is not null && File.Exists(Path.Combine(ledger.Dir, task, "task.json"))
            ? ledger.LoadRecord(task).Settings.TempRoot?.Value : null;
    }

    public string ControlFolder => controlFolder;

    public Git WithIndex(string path) => new(file, controlFolder, new Dictionary<string, string>(environment, environment.Comparer)
    {
        ["GIT_INDEX_FILE"] = Path.GetFullPath(path),
    }, timeout) { report = report, gated = gated };

    public Git ForWorkspace(Ledger ledger, string task, Action<string, JsonObject>? observer = null) =>
        new(file, controlFolder, environment, timeout) { gated = true, report = (type, data) => { ledger.Write(task, type, data); observer?.Invoke(type, data); } };

    public void Report(string type, JsonObject data) => report?.Invoke(type, data);

    public Task InitializeBaselineAsync(string repo, CancellationToken ct) => new GitConfigGate(this).InitializeAsync(repo, ct);

    public static Git Default => ForTask("git");

    public async Task<ProcessResult> RunAsync(string repo, CancellationToken ct, params string[] args)
    {
        var command = args.FirstOrDefault();
        var writesIndex = command is "add" or "commit" or "reset" or "read-tree" or "write-tree" or "update-index";
        // Include the checks and path lookup in the uncancellable write, not only the child.
        var callToken = writesIndex || command is "worktree" or "branch" ? CancellationToken.None : ct;
        if (gated && (writesIndex || command is "status" or "worktree" or "branch"))
            await new GitConfigGate(this).CheckAsync(repo, callToken);
        string? lockPath = null;
        if (writesIndex)
            lockPath = environment.TryGetValue("GIT_INDEX_FILE", out var index) ? index + ".lock"
                : Path.GetFullPath((await CheckAsync(repo, callToken, "rev-parse", "--git-path", "index.lock")).Trim(), repo);
        var hooks = "/dev/null";
        if (OperatingSystem.IsWindows())
        {
            hooks = Path.Combine(controlFolder, "hooks-" + RandomName());
            Directory.CreateDirectory(hooks);
        }
        try
        {
            if (OperatingSystem.IsWindows() && Directory.EnumerateFileSystemEntries(hooks).Any())
                throw new HarnessException("git hooks folder is not empty");
            var childEnvironment = new Dictionary<string, string>(environment, environment.Comparer)
            {
                ["GIT_TERMINAL_PROMPT"] = "0",
            };
            // Do not let an inherited setting leak into a writing call.
            childEnvironment.Remove("GIT_OPTIONAL_LOCKS");
            if (args.FirstOrDefault() is "status" or "diff" or "rev-parse" or "merge-base" or "show" or "ls-files")
                childEnvironment["GIT_OPTIONAL_LOCKS"] = "0";
            string[] commandArgs = command == "commit" && !args.Contains("--no-verify") ? ["commit", "--no-verify", .. args.Skip(1)] : args;
            var lockExisted = lockPath is not null && File.Exists(lockPath);
            var started = DateTime.UtcNow;
            var result = await Proc.RunAsync(file,
                ["-C", repo, "-c", $"core.hooksPath={hooks}", "-c", "core.fsmonitor=false", "-c", "commit.gpgSign=false",
                    "-c", "gc.auto=0", "-c", "maintenance.auto=false",
                    // Git for Windows refuses paths past 260 characters without it; config reads keep the repository's own value.
                    .. OperatingSystem.IsWindows() && commandArgs.FirstOrDefault() != "config" ? ["-c", "core.longpaths=true"] : Array.Empty<string>(), .. commandArgs],
                repo, callToken, timeout, environment: childEnvironment);
            if (result.TimedOut && result.Killed && !lockExisted && lockPath is not null && File.Exists(lockPath)
                && File.GetCreationTimeUtc(lockPath) >= started)
            {
                File.Delete(lockPath);
                Report("index-lock-removed", new JsonObject { ["path"] = lockPath });
            }
            return result;
        }
        finally
        {
            if (OperatingSystem.IsWindows()) Directory.Delete(hooks, recursive: true);
        }
    }

    public async Task<string> CheckAsync(string repo, CancellationToken ct, params string[] args) =>
        Encoding.UTF8.GetString(await CheckBytesAsync(repo, ct, args));

    public async Task<byte[]> CheckBytesAsync(string repo, CancellationToken ct, params string[] args)
    {
        var result = await RunAsync(repo, ct, args);
        return result.ExitCode == 0
            ? result.Stdout
            : throw new HarnessException($"git {string.Join(' ', args)} failed in {repo}: {result.Stderr.Trim()}");
    }

    public async Task<string> IdAsync(string repo, CancellationToken ct, params string[] args)
    {
        var id = (await CheckAsync(repo, ct, args)).Trim();
        if (!Regex.IsMatch(id, "\\A[0-9a-f]{40}([0-9a-f]{24})?\\z"))
            throw new HarnessException("git returned an invalid object id");
        return id;
    }

    static string RandomName()
    {
        const string alphabet = "abcdefghijklmnopqrstuvwxyz234567";
        var result = new StringBuilder();
        var buffer = 0;
        var bits = 0;
        foreach (var value in System.Security.Cryptography.RandomNumberGenerator.GetBytes(16))
        {
            buffer = (buffer << 8) | value;
            bits += 8;
            while (bits >= 5) { bits -= 5; result.Append(alphabet[(buffer >> bits) & 31]); }
        }
        if (bits > 0) result.Append(alphabet[(buffer << (5 - bits)) & 31]);
        return result.ToString();
    }

    sealed class CommandControlFolder : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(Locations.StateRoot, "control", $"cmd-{Environment.ProcessId}");

        public CommandControlFolder()
        {
            if (OperatingSystem.IsWindows()) Directory.CreateDirectory(Path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }
}
