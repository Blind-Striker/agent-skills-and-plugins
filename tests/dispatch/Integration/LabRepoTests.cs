using Dispatch.Tests.Support;

namespace Dispatch.Tests.Integration;

[Category("Integration")]
[NotInParallel("processes")]
public sealed class LabRepoTests
{
    [Test]
    [Timeout(180_000)]
    public async Task Repository_setup_starts_no_git_processes(CancellationToken cancellationToken)
    {
        using var tree = new TempTree();
        var repo = await LabRepo.CreateAsync(tree, cancellationToken);

        await Assert.That(repo.GitProcessCount).IsEqualTo(0);
    }

    [Test]
    [Timeout(180_000)]
    public async Task A_lab_repository_has_one_clean_commit_and_local_identity(CancellationToken cancellationToken)
    {
        using var tree = new TempTree();
        var repo = await LabRepo.CreateAsync(tree, cancellationToken);

        await Assert.That(await repo.GitAsync(["rev-list", "--count", "HEAD"], cancellationToken)).IsEqualTo("1");
        await Assert.That(await repo.GitAsync(["status", "--porcelain"], cancellationToken)).IsEmpty();
        await Assert.That(await repo.GitAsync(["log", "-1", "--format=%an <%ae>"], cancellationToken)).IsEqualTo("Test <test@example.invalid>");
        await Assert.That(await repo.GitAsync(["config", "--local", "core.autocrlf"], cancellationToken)).IsEqualTo("false");
        await Assert.That(await repo.GitAsync(["rev-parse", "--show-toplevel"], cancellationToken)).IsEqualTo(repo.Root.Replace('\\', '/'));

        var original = await repo.GitAsync(["rev-parse", "HEAD"], cancellationToken);
        await repo.GitAsync(["config", "--local", "user.name", "Changed"], cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(repo.Root, "README.md"), "Changed\n", cancellationToken);
        await repo.GitAsync(["add", "README.md"], cancellationToken);
        await repo.GitAsync(["commit", "-m", "Changed tree"], cancellationToken);
        var hook = Path.Combine(repo.Root, ".git", "hooks", "pre-commit");
        await File.WriteAllTextAsync(hook, "test-owned hook", cancellationToken);
        var objectFile = Directory.EnumerateFiles(Path.Combine(repo.Root, ".git", "objects"), "*", SearchOption.AllDirectories).First();
        File.SetAttributes(objectFile, File.GetAttributes(objectFile) & ~FileAttributes.ReadOnly);
        File.Delete(objectFile);

        using var otherTree = new TempTree();
        var other = await LabRepo.CreateAsync(otherTree, cancellationToken);
        await Assert.That(await other.GitAsync(["rev-parse", "HEAD"], cancellationToken)).IsEqualTo(original);
        await Assert.That(await other.GitAsync(["config", "--local", "user.name"], cancellationToken)).IsEqualTo("Test");
        await Assert.That(await other.GitAsync(["status", "--porcelain"], cancellationToken)).IsEmpty();
        await Assert.That(await File.ReadAllTextAsync(Path.Combine(other.Root, "README.md"), cancellationToken)).IsEqualTo("# Lab repository\n");
        await Assert.That(File.Exists(Path.Combine(other.Root, ".git", "hooks", "pre-commit"))).IsFalse();
        await Assert.That(await other.GitAsync(["fsck", "--full"], cancellationToken)).IsEmpty();
    }
}
