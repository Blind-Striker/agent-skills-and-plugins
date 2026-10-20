static class Git
{
    public static Task<ProcessResult> RunAsync(string repo, CancellationToken ct, params string[] args) =>
        Proc.RunAsync("git", ["-C", repo, .. args], null, ct, TimeSpan.FromMinutes(5));

    public static async Task<string> CheckAsync(string repo, CancellationToken ct, params string[] args)
    {
        var result = await RunAsync(repo, ct, args);
        return result.ExitCode == 0
            ? result.Output
            : throw new HarnessException($"git {string.Join(' ', args)} failed in {repo}: {result.Output.Trim()}");
    }
}
