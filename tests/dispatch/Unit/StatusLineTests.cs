namespace Dispatch.Tests.Unit;

[Category("Unit")]
public sealed class StatusLineTests
{
    [Test]
    [Arguments("Report\n\nStatus: done", "done")]
    [Arguments("Report\nStatus: Blocked\n", "blocked")]
    [Arguments("Report\n**Status: stopped**", "stopped")]
    [Arguments("> Status: done", "done")]
    [Arguments("I was told to end with `Status: done`.\n\nStatus: blocked", "blocked")]
    [Arguments("Status: done\nmore text\nStatus: stopped", "stopped")]
    [Arguments("Blocked: this session exposes no shell tool.", null)]
    [Arguments("Status: finished", null)]
    [Arguments("The status: done line comes later.", null)]
    [Arguments("", null)]
    public async Task The_last_status_line_wins_and_prose_is_not_a_status(string text, string? expected)
    {
        await Assert.That(Settlement.ReadStatus(text)).IsEqualTo(expected);
    }
}
