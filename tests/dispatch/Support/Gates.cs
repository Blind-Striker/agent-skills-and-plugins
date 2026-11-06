namespace Dispatch.Tests.Support;

internal static class Gates
{
    static CapturedResult? buildResult;
    static TempTree? buildTree;
    static string? harnessDll;
    static string? savedGitConfig;
    static string? globalGitConfig;
    public static CapturedResult BuildResult => buildResult ?? throw new InvalidOperationException("the session build gate has not completed");
    public static string HarnessDll => harnessDll ?? throw new InvalidOperationException("the session build gate has not completed");

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
        globalGitConfig = Path.GetTempFileName();
        var include = savedGitConfig is null ? "" :
            $"[include]\n\tpath = \"{Path.GetFullPath(savedGitConfig).Replace("\\", "\\\\").Replace("\"", "\\\"")}\"\n";
        File.WriteAllText(globalGitConfig, include +
            "[filter \"lfs\"]\n" +
            "\tclean = git-lfs clean -- %f\n" +
            "\tsmudge = git-lfs smudge -- %f\n" +
            "\tprocess = git-lfs filter-process\n" +
            "\trequired = true\n");
        Environment.SetEnvironmentVariable("GIT_CONFIG_GLOBAL", globalGitConfig);
        var tree = buildTree = new TempTree();
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

    [After(TestSession)]
    public static void CleanBuildCache()
    {
        try { buildTree?.Dispose(); }
        finally
        {
            Environment.SetEnvironmentVariable("GIT_CONFIG_GLOBAL", savedGitConfig);
            if (globalGitConfig is not null) File.Delete(globalGitConfig);
        }
    }
}
