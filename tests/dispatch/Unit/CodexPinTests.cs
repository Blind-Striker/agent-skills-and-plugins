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

    // Trigger for an upstream known limit (docs/ROADMAP.md Known Gaps). A Codex pin move must re-check
    // openai/codex#56396 before this expectation moves with it.
    [Test]
    public async Task S6_a_Codex_pin_move_rechecks_the_sandbox_denied_exec_limit()
    {
        await Assert.That(CodexBackend.PinnedVersion).IsEqualTo("0.163.0").Because(
            "openai/codex#56396 (no command item for a sandbox-denied code-mode exec on macOS and Linux) was "
            + "open at 0.163.0: before moving the pin, check whether the new Codex fixes it; if it does, restore "
            + "the one-event FN3/FN4 assertions on every OS in Functional/RealBackendTests.cs and drop the known "
            + "limit in harness references/backends.md and docs/ROADMAP.md; then update this version");
    }
}
