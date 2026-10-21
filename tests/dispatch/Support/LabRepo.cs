namespace Dispatch.Tests.Support;

internal sealed class LabRepo(TempTree tree)
{
    public string Root => tree.Repository;

    public static async Task<LabRepo> CreateAsync(TempTree tree, CancellationToken cancellationToken)
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
        return repo;
    }

    public async Task<string> GitAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        using var child = new CapturedProcess();
        var result = await child.RunAsync("git", arguments, Root, tree.ChildEnvironment(), cancellationToken);
        result.RequireSuccess("lab git");
        return result.Stdout.TrimEnd();
    }

    public Task BaselineAsync(string control, CancellationToken ct) => new Git(Executables.Resolve("git"), control,
        tree.ChildEnvironment().ToDictionary(x => x.Key, x => x.Value!)).InitializeBaselineAsync(Root, ct);
}
