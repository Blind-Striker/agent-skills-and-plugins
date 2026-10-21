using Dispatch.Tests.Support;

namespace Dispatch.Tests.Unit;

[Category("Unit")]
public sealed class ScratchTests
{
    [Test]
    public async Task Scratch_selection_skips_git_ancestors_in_candidate_order()
    {
        using var tree = new TempTree();
        File.WriteAllText(Path.Combine(tree.Root, ".git"), "fixture");
        var root = Path.GetPathRoot(tree.Root)!;
        await Assert.That(Scratch.Candidate(tree.Root, root)).IsEqualTo(root);
        await Assert.That(Scratch.Candidate(root, tree.Root)).IsEqualTo(root);
        await Assert.That(() => Scratch.Candidate(tree.Root, tree.DispatchHome)).Throws<HarnessException>();
    }
}
