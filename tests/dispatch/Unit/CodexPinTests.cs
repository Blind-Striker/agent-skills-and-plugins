using System.Text.RegularExpressions;
using Dispatch.Tests.Support;

namespace Dispatch.Tests.Unit;

[Category("Unit")]
public sealed class CodexPinTests
{
    [Test]
    public async Task S5_CI_installs_the_harness_Codex_pin(CancellationToken token)
    {
        var workflow = await File.ReadAllTextAsync(Path.Combine(TestPaths.Root, ".github", "workflows", "dispatch.yml"), token);
        var installs = Regex.Matches(workflow,
            @"^\s*run:\s*npm\s+install\b[^\r\n]*@openai/codex@(?<version>[^\s]+)", RegexOptions.Multiline);
        await Assert.That(installs.Count).IsEqualTo(1).Because("the workflow must install one literal Codex CLI version");
        await Assert.That(installs[0].Groups["version"].Value).IsEqualTo(CodexBackend.PinnedVersion);
    }
}
