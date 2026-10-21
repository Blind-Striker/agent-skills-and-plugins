namespace Dispatch.Tests.Support;

internal static class Gates
{
    static CapturedResult? buildResult;
    static TempTree? buildTree;
    static string? harnessDll;
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
        var tree = buildTree = new TempTree();
        using var child = new CapturedProcess();
        var result = await child.RunAsync("dotnet", ["build", "dispatch.cs"], TestPaths.Harness, tree.ChildEnvironment(), cancellationToken);
        result.RequireSuccess("harness build gate");
        // runtimeconfig sits beside the runnable DLL, unlike the reference assemblies under obj/.
        var runtimeConfig = Directory.GetFiles(tree.Root, "dispatch.runtimeconfig.json", SearchOption.AllDirectories).Single();
        harnessDll = Path.Combine(Path.GetDirectoryName(runtimeConfig)!, "dispatch.dll");
        if (!File.Exists(harnessDll)) throw new FileNotFoundException("the build gate produced no runnable harness", harnessDll);
        await LabRepo.PrepareTemplateAsync(tree, cancellationToken);
        buildResult = result;
        Console.WriteLine($"Harness build gate: exit {result.ExitCode}\n{result.Stdout}{result.Stderr}");
    }

    [After(TestSession)]
    public static void CleanBuildCache() => buildTree?.Dispose();
}
