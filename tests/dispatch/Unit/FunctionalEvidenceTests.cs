using System.Text.Json.Nodes;
using Dispatch.Tests.Functional;

namespace Dispatch.Tests.Unit;

[Category("Unit")]
public sealed class FunctionalEvidenceTests
{
    [Test]
    public async Task Write_evidence_matches_shell_escaped_target_not_unrelated_failures()
    {
        if (!OperatingSystem.IsWindows()) Skip.Test("Windows shell command escaping only");
        var target = Path.Combine(Path.GetTempPath(), "outside.txt");
        var command = RealBackendTests.WriteCommand(target, "forbidden");
        JsonObject Tool(string resource) => new()
        {
            ["type"] = "tool", ["data"] = new JsonObject { ["tool"] = "shell", ["resource"] = resource, ["outcome"] = "failed" },
        };
        var intended = Tool("pwsh -Command \"" + command.Replace("\\", "\\\\", StringComparison.Ordinal) + "\"");
        var unrelated = Tool("pwsh -Command \"" + RealBackendTests.WriteCommand("inside.txt", "forbidden") + "\"");
        var syntaxError = Tool("unavailable-write-command");

        var matches = RealBackendTests.MatchingWriteEvents([unrelated, syntaxError, intended], command);

        await Assert.That(matches).IsEquivalentTo(new[] { intended });
    }
}
