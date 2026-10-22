using Dispatch.Tests.Support;

namespace Dispatch.Tests.Unit;

[Category("Unit")]
public sealed class AtomicWriteTests
{
    [Test]
    public async Task WriteFile_CrashBetweenTempAndMove_KeepsOldContent()
    {
        using var tree = new TempTree();
        var path = Path.Combine(tree.Root, "result.json");
        File.WriteAllText(path, "\"old\"\n");
        string? stagedPath = null;
        string? stagedContent = null;

        await Assert.That(() => Json.WriteFile(path, "new", InputJson.Read.String, temp =>
        {
            stagedPath = temp;
            stagedContent = File.ReadAllText(temp);
            throw new IOException("interrupted before replacement");
        })).Throws<IOException>();

        await Assert.That(File.ReadAllText(path)).IsEqualTo("\"old\"\n");
        await Assert.That(stagedContent).IsEqualTo("\"new\"\n");
        await Assert.That(Path.GetDirectoryName(stagedPath)).IsEqualTo(tree.Root);
        await Assert.That(Path.GetFileName(stagedPath)).StartsWith($"result.json.tmp-{Environment.ProcessId}-");
        await Assert.That(Directory.GetFiles(tree.Root, "*.tmp-*")).IsEmpty();
    }
}
