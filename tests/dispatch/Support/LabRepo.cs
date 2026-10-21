namespace Dispatch.Tests.Support;

internal sealed class LabRepo(TempTree tree)
{
    static string? template;
    public string Root => tree.Repository;
    public int GitProcessCount { get; private set; }

    public static Task<LabRepo> CreateAsync(TempTree tree, CancellationToken cancellationToken)
    {
        var source = template ?? throw new InvalidOperationException("the session repository fixture has not completed");
        // Copy, never clone with shared objects or hardlinks: refs, config, hooks and objects are test-owned.
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(Path.Combine(tree.Repository, Path.GetRelativePath(source, directory)));
        }
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            File.Copy(file, Path.Combine(tree.Repository, Path.GetRelativePath(source, file)));
        }
        return Task.FromResult(new LabRepo(tree));
    }

    public static async Task PrepareTemplateAsync(TempTree tree, CancellationToken cancellationToken)
    {
        var repo = new LabRepo(tree);
        await repo.GitAsync(["init", "--initial-branch=main"], cancellationToken);
        await repo.GitAsync(["config", "--local", "user.name", "Test"], cancellationToken);
        await repo.GitAsync(["config", "--local", "user.email", "test@example.invalid"], cancellationToken);
        await repo.GitAsync(["config", "--local", "core.autocrlf", "false"], cancellationToken);
        await repo.GitAsync(["config", "--local", "commit.gpgsign", "false"], cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(repo.Root, "README.md"), "# Lab repository\n", cancellationToken);
        await repo.GitAsync(["add", "--", "README.md"], cancellationToken);
        await repo.GitAsync(["commit", "-m", "Initial test tree"], cancellationToken);
        template = repo.Root;
    }

    public async Task<string> GitAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        GitProcessCount++;
        using var child = new CapturedProcess();
        var result = await child.RunAsync("git", arguments, Root, tree.ChildEnvironment(), cancellationToken);
        result.RequireSuccess("lab git");
        return result.Stdout.TrimEnd();
    }

    public Task BaselineAsync(string control, CancellationToken ct) => new Git(Executables.Resolve("git"), control,
        tree.ChildEnvironment().ToDictionary(x => x.Key, x => x.Value!)).InitializeBaselineAsync(Root, ct);
}
