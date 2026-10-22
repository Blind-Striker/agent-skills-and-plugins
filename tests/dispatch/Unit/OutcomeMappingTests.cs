namespace Dispatch.Tests.Unit;

[Category("Unit")]
public sealed class OutcomeMappingTests
{
    [Test]
    [Arguments("ended", "succeeded", "Cancel", false, false, "cancelled", "Settle", null, false, null)]
    [Arguments("ended", "failed", "Timeout", false, false, "timeout", "Settle", null, false, null)]
    [Arguments("ended", "succeeded", null, false, false, "succeeded", "Settle", null, false, null)]
    [Arguments("ended", "failed", null, false, false, "failed", "Settle", null, false, null)]
    [Arguments("ended", "interrupted", null, false, false, null, "Interrupted", null, false, "unknown")]
    [Arguments("ended", "lost", null, false, false, "lost", "Settle", null, false, null)]
    [Arguments("final-mark", null, "Cancel", true, false, "cancelled", "Settle", false, true, null)]
    [Arguments("reattach-ended", "lost", null, false, false, "lost", "Settle", null, false, null)]
    [Arguments("reattach-interrupted", null, null, false, false, null, "Interrupted", null, false, "unknown")]
    [Arguments("reattach-unreadable", null, null, false, false, "unknown", "Settle", null, false, "record unreadable")]
    [Arguments("shutdown", null, "Shutdown", false, false, null, "KeepState", null, false, null)]
    // Further cases pin precedence, the hard-stop evidence, and the supplied interruption reason.
    [Arguments("ended", "failed", "Cancel", false, false, "cancelled", "Settle", null, false, null)]
    [Arguments("ended", "interrupted", "Cancel", false, false, "cancelled", "Settle", null, false, null)]
    [Arguments("ended", "lost", "Cancel", true, false, "cancelled", "Settle", true, false, null)]
    [Arguments("ended", "succeeded", "Timeout", false, false, "timeout", "Settle", null, false, null)]
    [Arguments("ended", "interrupted", "Timeout", false, false, "timeout", "Settle", null, false, null)]
    [Arguments("ended", "lost", "Timeout", true, false, "timeout", "Settle", true, false, null)]
    [Arguments("ended", "succeeded", "Cancel", true, false, "cancelled", "Settle", true, false, null)]
    [Arguments("ended", "failed", "Timeout", true, false, "timeout", "Settle", true, false, null)]
    [Arguments("final-mark", null, "Cancel", true, true, "cancelled", "Settle", true, false, null)]
    [Arguments("final-mark", null, "Timeout", true, false, "timeout", "Settle", false, true, null)]
    [Arguments("final-mark", null, "Timeout", true, true, "timeout", "Settle", true, false, null)]
    [Arguments("reattach-ended", "succeeded", "Cancel", true, false, "cancelled", "Settle", true, false, null)]
    [Arguments("reattach-ended", "failed", "Timeout", false, false, "timeout", "Settle", null, false, null)]
    [Arguments("reattach-live", null, null, false, false, null, "KeepState", null, false, null)]
    [Arguments("ended-with-reason", "interrupted", null, false, false, null, "Interrupted", null, false, "inactivity")]
    public async Task OutcomeMapping_Table(
        string source, string? outcome, string? interrupt, bool hardStopSent, bool stopped,
        string? expectedOutcome, string expectedAction, bool? expectedStopped, bool expectedZombie, string? expectedReason)
    {
        var kind = interrupt is null ? (InterruptKind?)null : Enum.Parse<InterruptKind>(interrupt);
        var decision = source switch
        {
            "ended" => OutcomeMapping.FromEnded(outcome!, kind, hardStopSent),
            "ended-with-reason" => OutcomeMapping.FromEnded(outcome!, kind, hardStopSent, "inactivity"),
            "final-mark" => OutcomeMapping.AtFinalMark(kind!.Value, new InterruptResult(stopped, "interrupt")),
            "reattach-ended" => OutcomeMapping.FromReattach(new ReattachResult.Ended(outcome!), kind, hardStopSent),
            "reattach-interrupted" => OutcomeMapping.FromReattach(new ReattachResult.Interrupted()),
            "reattach-unreadable" => OutcomeMapping.FromReattach(new ReattachResult.Unreadable("record unreadable")),
            "reattach-live" => OutcomeMapping.FromReattach(new ReattachResult.Live()),
            "shutdown" => OutcomeMapping.OnShutdown(),
            _ => throw new ArgumentOutOfRangeException(nameof(source)),
        };

        await Assert.That(decision.Outcome).IsEqualTo(expectedOutcome);
        await Assert.That(decision.Action.ToString()).IsEqualTo(expectedAction);
        await Assert.That(decision.WorkerStopped).IsEqualTo(expectedStopped);
        await Assert.That(decision.Zombie).IsEqualTo(expectedZombie);
        await Assert.That(decision.Reason).IsEqualTo(expectedReason);
    }

    [Test]
    [Arguments("Hard")]
    [Arguments("Shutdown")]
    public async Task Final_mark_requires_the_original_cancel_or_timeout(string interrupt)
    {
        await Assert.That(() => OutcomeMapping.AtFinalMark(Enum.Parse<InterruptKind>(interrupt), new InterruptResult(false, "interrupt")))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task A_hard_stop_is_evidence_not_the_original_interrupt_reason()
    {
        await Assert.That(() => OutcomeMapping.FromEnded("succeeded", InterruptKind.Hard)).Throws<ArgumentException>();
        await Assert.That(() => OutcomeMapping.FromEnded("succeeded", null, hardStopSent: true)).Throws<ArgumentException>();
    }
}
