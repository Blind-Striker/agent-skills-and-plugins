using Dispatch.Tests.Support;

namespace Dispatch.Tests.Unit;

[Category("Unit")]
public sealed class UserPathGuardTests
{
    [Test]
    [Arguments(8000, false)]
    [Arguments(8001, true)]
    [Arguments(16001, true)]
    public async Task SelfCheck_WarnsAboveThreshold(int length, bool warns)
    {
        var guard = new UserPathGuard(() => new string('x', length));
        var warning = guard.SelfCheckWarning;
        await Assert.That(warning is not null).IsEqualTo(warns);
        if (warns)
        {
            await Assert.That(warning!).Contains(@"HKCU\Environment\Path");
            await Assert.That(warning!).Contains(length.ToString());
            await Assert.That(warning!).Contains("reg query");
        }
    }

    [Test]
    [Arguments(16000, false)]
    [Arguments(16001, true)]
    public async Task Run_RefusesAboveThreshold(int length, bool refuses)
    {
        var guard = new UserPathGuard(() => new string('x', length));
        var refusal = guard.RunRefusal;
        await Assert.That(refusal is not null).IsEqualTo(refuses);
        if (refuses)
        {
            await Assert.That(refusal!).Contains(@"HKCU\Environment\Path");
            await Assert.That(refusal!).Contains(length.ToString());
            await Assert.That(refusal!).Contains("reg query");
        }
    }

    [Test]
    public async Task MissingUserPath_DoesNotWarnOrRefuse()
    {
        var guard = new UserPathGuard(() => null);
        await Assert.That(guard.SelfCheckWarning).IsNull();
        await Assert.That(guard.RunRefusal).IsNull();
    }

    [Test]
    [Arguments("keep;old;keep", "keep;new", "new", "old;keep")]
    [Arguments(null, "new", "new", "")]
    [Arguments("old", null, "", "old")]
    [Arguments("lower", "LOWER", "LOWER", "lower")]
    [Arguments("a;b", "b;a", "", "")]
    [Arguments(null, "", "", "")]
    public async Task ChangedUserPath_RecordsExactEntryDiff(string? before, string? after, string added, string removed)
    {
        using var tree = new TempTree();
        var ledger = new Ledger(tree.Root);
        var reads = 0;
        var guard = new UserPathGuard(() => ++reads == 1 ? before : after);
        guard.ReportChanges(ledger);
        var warning = ledger.Read().Single();
        await Assert.That(reads).IsEqualTo(2);
        await Assert.That(warning.Type).IsEqualTo("warning");
        await Assert.That(warning.Task).IsNull();
        await Assert.That(warning.Data!["value"]!.GetValue<string>()).IsEqualTo(@"HKCU\Environment\Path");
        await Assert.That(warning.Data["added"]!.AsArray().Select(x => x!.GetValue<string>()).SequenceEqual(Entries(added))).IsTrue();
        await Assert.That(warning.Data["removed"]!.AsArray().Select(x => x!.GetValue<string>()).SequenceEqual(Entries(removed))).IsTrue();
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("same;path")]
    public async Task UnchangedUserPath_RecordsNoWarning(string? value)
    {
        using var tree = new TempTree();
        var ledger = new Ledger(tree.Root);
        ledger.Write(null, "run");
        new UserPathGuard(() => value).ReportChanges(ledger);
        await Assert.That(ledger.Read().Where(x => x.Type == "warning")).IsEmpty();
    }

    static string[] Entries(string value) => value.Split(';', StringSplitOptions.RemoveEmptyEntries);
}
