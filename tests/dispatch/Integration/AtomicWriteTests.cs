using System.Text.Json;
using Dispatch.Tests.Support;

namespace Dispatch.Tests.Integration;

[Category("Integration")]
[NotInParallel("processes")]
public sealed class AtomicWriteTests
{
    [Test]
    [Timeout(180_000)]
    public async Task WriteFile_WhileReaderHoldsTarget_Succeeds(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows()) Skip.Test("Windows target sharing is required");
        using var tree = new TempTree();
        var path = Path.Combine(tree.Root, "result.json");
        File.WriteAllText(path, "\"old\"\n");
        var holder = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var staged = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var writing = Task.Run(() => Json.WriteFile(path, "new", InputJson.Read.String, _ => staged.SetResult()), cancellationToken);
        try
        {
            await staged.Task.WaitAsync(cancellationToken);
            // The contract requires replacement to survive a 500 ms non-delete-sharing reader.
            await Task.Delay(500, cancellationToken);
        }
        finally
        {
            holder.Dispose();
            await writing;
        }

        await Assert.That(File.ReadAllText(path)).IsEqualTo("\"new\"\n");
        await Assert.That(Directory.GetFiles(tree.Root, "*.tmp-*")).IsEmpty();
    }

    [Test]
    [Timeout(180_000)]
    public async Task WriteFile_RepeatedRewrites_ReadersSeeWholeDocuments(CancellationToken cancellationToken)
    {
        using var tree = new TempTree();
        var path = Path.Combine(tree.Root, "result.json");
        Json.WriteFile(path, "initial", InputJson.Read.String);
        string previous = "initial";
        for (var i = 0; i < 100; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var next = $"{i}:" + new string('x', i % 2 == 0 ? 65_536 : 512);
            // Hold an independent reader across replacement. Unlike a busy polling loop, this
            // guarantees an overlapping read on every rewrite without competing for disk time.
            using var snapshot = OpenShared(path);
            string? beforeReplace = null;
            Json.WriteFile(path, next, InputJson.Read.String, _ =>
            {
                using var document = JsonDocument.Parse(snapshot);
                beforeReplace = document.RootElement.GetString();
            });
            await Assert.That(beforeReplace).IsEqualTo(previous);
            snapshot.Position = 0;
            using var oldDocument = JsonDocument.Parse(snapshot);
            await Assert.That(oldDocument.RootElement.GetString()).IsEqualTo(previous);
            using var current = OpenShared(path);
            using var newDocument = JsonDocument.Parse(current);
            await Assert.That(newDocument.RootElement.GetString()).IsEqualTo(next);
            previous = next;
        }

        await Assert.That(JsonSerializer.Deserialize(File.ReadAllText(path), InputJson.Read.String)).IsEqualTo(previous);
        await Assert.That(Directory.GetFiles(tree.Root, "*.tmp-*")).IsEmpty();
    }

    static FileStream OpenShared(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
}
