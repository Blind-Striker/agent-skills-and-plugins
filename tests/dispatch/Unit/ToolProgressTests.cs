namespace Dispatch.Tests.Unit;

[Category("Unit")]
public sealed class ToolProgressTests
{
    [Test]
    public async Task Tool_resources_redact_credential_flags_before_shortening_to_one_line()
    {
        await Assert.That(Call("fetch --token synthetic-value").Resource).IsEqualTo("[REDACTED]");
        await Assert.That(Call("fetch?password=synthetic-value").Resource).IsEqualTo("[REDACTED]");
        await Assert.That(Call(new string('x', 210) + " https://user:synthetic-value@example.invalid").Resource).IsEqualTo("[REDACTED]");
        await Assert.That(Call("git status\r\nsecond command").Resource).IsEqualTo("git status");
        await Assert.That(Call(new string('x', 201)).Resource).IsEqualTo(new string('x', 200));
    }

    static WorkerSignal.ToolCall Call(string resource) => new("task", "turn", "call", "shell", resource,
        "start", null, DateTimeOffset.UnixEpoch);
}
