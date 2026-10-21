using System.Text.Json;
using Dispatch.Tests.Support;

namespace Dispatch.Tests.Integration;

[Category("Integration")]
public sealed class AtomicWriteTests
{
    [Test]
    [Timeout(15_000)]
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
    [Timeout(30_000)]
    public async Task WriteFile_RepeatedRewrites_ReadersSeeWholeDocuments(CancellationToken cancellationToken)
    {
        using var tree = new TempTree();
        var path = Path.Combine(tree.Root, "result.json");
        Json.WriteFile(path, "initial", InputJson.Read.String);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var firstRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reads = 0;
        var polling = Task.Run(() =>
        {
            while (!stop.IsCancellationRequested)
            {
                using var stream = OpenShared(path);
                using var document = JsonDocument.Parse(stream);
                if (document.RootElement.ValueKind != JsonValueKind.String)
                    throw new InvalidDataException("reader observed an incomplete document");
                Interlocked.Increment(ref reads);
                firstRead.TrySetResult();
            }
        }, cancellationToken);
        string previous = "initial";
        try
        {
            await firstRead.Task.WaitAsync(cancellationToken);
            for (var i = 0; i < 100; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var next = $"{i}:" + new string('x', i % 2 == 0 ? 65_536 : 512);
                string? beforeReplace = null;
                Json.WriteFile(path, next, InputJson.Read.String, _ =>
                {
                    // Read at the pre-rename boundary, without holding a Windows handle open
                    // across the move. In-place truncation fails this deterministic check.
                    using var snapshot = OpenShared(path);
                    using var document = JsonDocument.Parse(snapshot);
                    beforeReplace = document.RootElement.GetString();
                });
                await Assert.That(beforeReplace).IsEqualTo(previous);
                previous = next;
            }
        }
        finally
        {
            stop.Cancel();
            await polling;
        }

        await Assert.That(reads).IsGreaterThan(0);
        await Assert.That(JsonSerializer.Deserialize(File.ReadAllText(path), InputJson.Read.String)).IsEqualTo(previous);
        await Assert.That(Directory.GetFiles(tree.Root, "*.tmp-*")).IsEmpty();
    }

    static FileStream OpenShared(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
}
