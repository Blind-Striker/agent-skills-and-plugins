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

    [Test]
    [Arguments("$env:OPENCODE_SDK_TESTS_BUN_MISMATCH='allow'; dotnet build OpenCode.slnx -c Release")]
    [Arguments("$Env:OPENCODE_SDK_TESTS_BUN_MISMATCH = 'allow'; dotnet test --project tests/OpenCode.Sdk.Tests -c Release --no-build")]
    [Arguments("echo $env:PATH")]
    public async Task Tool_resources_keep_commands_that_set_a_harmless_environment_variable(string resource)
    {
        await Assert.That(Call(resource).Resource).IsEqualTo(resource);
    }

    [Test]
    [Arguments("$env:GITHUB_TOKEN='synthetic-value'; dotnet build")]
    [Arguments("$Env:API_KEY = 'synthetic-value'; dotnet test")]
    [Arguments("env:NUGET_PASSWORD=synthetic-value dotnet restore")]
    public async Task Tool_resources_redact_commands_that_set_a_secret_environment_variable(string resource)
    {
        await Assert.That(Call(resource).Resource).IsEqualTo("[REDACTED]");
    }

    static WorkerSignal.ToolCall Call(string resource) => new("task", "turn", "call", "shell", resource,
        "start", null, DateTimeOffset.UnixEpoch);
}
