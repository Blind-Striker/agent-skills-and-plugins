using Dispatch.Tests.Support;

namespace Dispatch.Tests.Integration;

[Category("Integration")]
[NotInParallel("processes")]
public sealed class LabRepoTests
{
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
    }
}
