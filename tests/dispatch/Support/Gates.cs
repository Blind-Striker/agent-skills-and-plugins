namespace Dispatch.Tests.Support;

internal static class Gates
{
    static CapturedResult? buildResult;
    static TempTree? buildTree;
    static string? harnessDll;
    static string? savedGitConfig;
    static string? globalGitConfig;
    static Dictionary<string, string>? savedGitOverrides;
    public static CapturedResult BuildResult => buildResult ?? throw new InvalidOperationException("the session build gate has not completed");
    public static string HarnessDll => harnessDll ?? throw new InvalidOperationException("the session build gate has not completed");
    public static string SessionGitConfig => globalGitConfig ?? throw new InvalidOperationException("the session git config has not been created");

    public static void UseBuildCache(IDictionary<string, string?> environment)
    {
        var cache = (buildTree ?? throw new InvalidOperationException("the session build gate has not completed")).ChildEnvironment();
        foreach (var name in new[] { "TEMP", "TMP", "TMPDIR" }) environment[name] = cache[name];
    }

    [Before(TestSession)]
    [Timeout(900_000)]
    public static async Task BuildHarness(CancellationToken cancellationToken)
    {
        savedGitConfig = Environment.GetEnvironmentVariable("GIT_CONFIG_GLOBAL");
        savedGitOverrides = ClearGitOverrides();
        var tree = buildTree = new TempTree();
        globalGitConfig = Path.Combine(tree.Root, "session.gitconfig");
        WriteSessionGitConfig(globalGitConfig, savedGitConfig);
        Environment.SetEnvironmentVariable("GIT_CONFIG_GLOBAL", globalGitConfig);
        using var child = new CapturedProcess();
        var result = await child.RunAsync("dotnet", ["build", "dispatch.cs"], TestPaths.Harness, tree.ChildEnvironment(), cancellationToken);
        result.RequireSuccess("harness build gate");
        // The build names its runnable DLL: the runfile cache follows TEMP on Windows and Linux but
        // lives under the user's Application Support folder on macOS.
        using var query = new CapturedProcess();
        var target = await query.RunAsync("dotnet", ["build", "dispatch.cs", "-getProperty:TargetPath"], TestPaths.Harness,
            tree.ChildEnvironment(), cancellationToken);
        target.RequireSuccess("harness build gate target path");
        harnessDll = target.Stdout.Trim();
        if (!File.Exists(harnessDll)) throw new FileNotFoundException("the build gate produced no runnable harness", harnessDll);
        await LabRepo.PrepareTemplateAsync(tree, cancellationToken);
        buildResult = result;
        Console.WriteLine($"Harness build gate: exit {result.ExitCode}\n{result.Stdout}{result.Stderr}");
    }

    internal static Dictionary<string, string> ClearGitOverrides()
    {
        string[] names = ["GIT_DIR", "GIT_WORK_TREE", "GIT_INDEX_FILE", "GIT_OBJECT_DIRECTORY",
            "GIT_ALTERNATE_OBJECT_DIRECTORIES", "GIT_COMMON_DIR", "GIT_CONFIG_PARAMETERS", "GIT_CONFIG_COUNT"];
        var saved = Environment.GetEnvironmentVariables().Keys.Cast<string>()
            .Where(name => names.Contains(name, StringComparer.OrdinalIgnoreCase)
                || name.StartsWith("GIT_CONFIG_KEY_", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("GIT_CONFIG_VALUE_", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(name => name, name => Environment.GetEnvironmentVariable(name)!);
        foreach (var name in saved.Keys) Environment.SetEnvironmentVariable(name, null);
        return saved;
    }

    internal static void RestoreGitOverrides(IReadOnlyDictionary<string, string> saved)
    {
        foreach (var (name, value) in saved) Environment.SetEnvironmentVariable(name, value);
    }

    internal static void WriteSessionGitConfig(string path, string? developerConfig)
    {
        var ignore = Path.Combine(Path.GetDirectoryName(path)!, "git-ignore");
        var attributes = Path.Combine(Path.GetDirectoryName(path)!, "git-attributes");
        File.WriteAllText(ignore, "");
        File.WriteAllText(attributes, "");
        // Without a developer GIT_CONFIG_GLOBAL, ~/.gitconfig is not read during tests, on purpose.
        var include = string.IsNullOrEmpty(developerConfig) ? "" :
            $"[include]\n\tpath = {QuoteGitPath(Path.GetFullPath(developerConfig))}\n";
        File.WriteAllText(path, $"[core]\n\texcludesFile = {QuoteGitPath(ignore)}\n\tattributesFile = {QuoteGitPath(attributes)}\n" + include +
            "[filter \"lfs\"]\n" +
            "\tclean = git-lfs clean -- %f\n" +
            "\tsmudge = git-lfs smudge -- %f\n" +
            "\tprocess = git-lfs filter-process\n" +
            "\trequired = true\n");
    }

    static string QuoteGitPath(string path) => "\"" + path.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n") + "\"";

    [After(TestSession)]
    public static void CleanBuildCache()
    {
        try { buildTree?.Dispose(); }
        finally
        {
            Environment.SetEnvironmentVariable("GIT_CONFIG_GLOBAL", savedGitConfig);
            if (savedGitOverrides is not null) RestoreGitOverrides(savedGitOverrides);
        }
    }
}
