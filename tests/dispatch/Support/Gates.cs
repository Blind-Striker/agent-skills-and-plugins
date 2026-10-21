namespace Dispatch.Tests.Support;

internal static class Gates
{
    static CapturedResult? buildResult;
    public static CapturedResult BuildResult => buildResult ?? throw new InvalidOperationException("the session build gate has not completed");

    [Before(TestSession)]
    [Timeout(900_000)]
    public static async Task BuildHarness(CancellationToken cancellationToken)
    {
        using var tree = new TempTree();
        using var child = new CapturedProcess();
        var result = await child.RunAsync("dotnet", ["build", "dispatch.cs"], TestPaths.Harness, tree.ChildEnvironment(), cancellationToken);
        result.RequireSuccess("harness build gate");
        buildResult = result;
        Console.WriteLine($"Harness build gate: exit {result.ExitCode}\n{result.Stdout}{result.Stderr}");
    }
}
