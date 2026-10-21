namespace Dispatch.Tests.Unit;

[Category("Unit")]
public sealed class ToolProgressTests
{
    [Test]
    [Arguments("https://example.invalid/?token=synthetic-value")]
    [Arguments("https://example.invalid/?password=synthetic-value")]
    [Arguments("fetch https://example.invalid/?page=1&api-key=synthetic-value")]
    [Arguments("https://example.invalid/?%74oken=synthetic-value")]
    [Arguments("fetch --api-key synthetic-value")]
    [Arguments("fetch --api-key=synthetic-value")]
    [Arguments("Authorization: Bearer synthetic-value")]
    [Arguments("fetch -H 'authorization: bearer synthetic-value'")]
    public async Task Tool_resources_redact_url_queries_normalized_flags_and_authorization(string resource)
    {
        await Assert.That(Call(resource).Resource).IsEqualTo("[REDACTED]");
        await Assert.That(Call("https://example.invalid/?page=1").Resource).IsEqualTo("https://example.invalid/?page=1");
    }

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
