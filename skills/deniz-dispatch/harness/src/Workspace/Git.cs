sealed class Git
{
    static readonly Lazy<string> executable = new(() => Executables.Resolve("git"));
    static readonly Lazy<CommandControlFolder> commandControl = new(() => new());
    readonly string file;
    readonly string controlFolder;
    readonly Dictionary<string, string> environment;

    public Git(string executablePath, string controlFolder, IReadOnlyDictionary<string, string> environment)
    {
        if (!Path.IsPathFullyQualified(executablePath)) throw new HarnessException("git executable must be an absolute path");
        if (!Path.IsPathFullyQualified(controlFolder)) throw new HarnessException("git control folder must be an absolute path");
        file = executablePath;
        this.controlFolder = controlFolder;
        this.environment = new(environment, OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    }

    public static IDisposable Initialize()
    {
        _ = executable.Value;
        return commandControl.Value;
    }

    public static Git ForTask(string task) => new(executable.Value,
        commandControl.Value.Path, Policy.Environment(task).Variables);

    public static Git Default => ForTask("git");

    public async Task<ProcessResult> RunAsync(string repo, CancellationToken ct, params string[] args)
    {
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
            var command = args.FirstOrDefault();
            string[] commandArgs = command == "commit" && !args.Contains("--no-verify") ? ["commit", "--no-verify", .. args.Skip(1)] : args;
            return await Proc.RunAsync(file,
                ["-C", repo, "-c", $"core.hooksPath={hooks}", "-c", "core.fsmonitor=false", "-c", "commit.gpgSign=false",
                    "-c", "gc.auto=0", "-c", "maintenance.auto=false", .. commandArgs],
                repo, command is "add" or "commit" ? CancellationToken.None : ct, TimeSpan.FromMinutes(5), environment: childEnvironment);
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
