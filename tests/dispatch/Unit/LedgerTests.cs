using Dispatch.Tests.Support;

namespace Dispatch.Tests.Unit;

[Category("Unit")]
public sealed class LedgerTests
{
    [Test]
    public async Task Write_TwoInstancesForSameRun_CanAlternate()
    {
        using var tree = new TempTree();
        var first = new Ledger(tree.Root);
        var second = new Ledger(tree.Root);

        first.Write(null, "first");
        second.Write(null, "second");
        first.Write(null, "third");
        second.Write(null, "fourth");

        await Assert.That(first.Read().Select(line => line.Type)).IsEquivalentTo(["first", "second", "third", "fourth"]);
        await Assert.That(second.Read().Select(line => line.Type)).IsEquivalentTo(["first", "second", "third", "fourth"]);
        using var exclusive = new FileStream(Path.Combine(tree.Root, "events.jsonl"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        await Assert.That(exclusive.Length).IsGreaterThan(0);
    }

    [Test]
    [Arguments("{\"type\":")]
    [Arguments("{\"type\":\"uncommitted\"}")]
    public async Task Read_TornLastLine_IsIgnored(string tail)
    {
        using var tree = new TempTree();
        var ledger = new Ledger(tree.Root);
        File.WriteAllText(Path.Combine(tree.Root, "events.jsonl"), "{\"type\":\"queued\"}\n" + tail);

        var lines = ledger.Read();

        await Assert.That(lines.Select(line => line.Type)).IsEquivalentTo(["queued"]);
    }

    [Test]
    [Arguments("{\"type\":\"final\"}\n")]
    [Arguments("{\"type\":\"ledger-repaired\",\"data\":{\"file\":\"events.jsonl\"}}\n")]
    [Arguments("")]
    public async Task Read_CorruptMiddleLine_NamesLine(string following)
    {
        using var tree = new TempTree();
        var ledger = new Ledger(tree.Root);
        var path = Path.Combine(tree.Root, "events.jsonl");
        File.WriteAllText(path, "{\"type\":\"queued\"}\nnot json\n" + following);

        var error = await Assert.That(() => ledger.Read()).Throws<HarnessException>();

        await Assert.That(error!.Message).Contains(path);
        await Assert.That(error.Message).Contains("line 2");
    }

    [Test]
    [Arguments("{\"type\":")]
    public async Task Open_TornTail_IsSealedBeforeNextWrite(string tail)
    {
        using var tree = new TempTree();
        var ledger = new Ledger(tree.Root);
        var path = Path.Combine(tree.Root, "events.jsonl");
        var prefix = "{\"type\":\"queued\"}\n";
        File.WriteAllText(path, prefix + tail);
        ledger.Write(null, "resumed");
        ledger.Write(null, "finished");

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        var text = reader.ReadToEnd();
        await Assert.That(text).StartsWith(prefix + "{\"ts\":");
        var lines = ledger.Read();
        await Assert.That(lines.Select(line => line.Type)).IsEquivalentTo(["queued", "ledger-repaired", "resumed", "finished"]);
        var sidecar = lines[1].Data!["file"]!.GetValue<string>();
        await Assert.That(sidecar).Matches("^events\\.torn-[0-9]{8}T[0-9]{6}Z\\.jsonl$");
        await Assert.That(File.ReadAllText(Path.Combine(tree.Root, sidecar))).IsEqualTo(tail);
        await Assert.That(lines[1].Data!["bytes"]!.GetValue<int>()).IsEqualTo(8);
    }
}
