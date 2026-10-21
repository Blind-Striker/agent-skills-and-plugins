using Dispatch.Tests.Support;

namespace Dispatch.Tests.Unit;

[Category("Unit")]
[NotInParallel]
public sealed class VerifyShellTests
{
    [Test]
    public async Task Shell_WindowsBash_NeverSystemFolder()
    {
        if (!OperatingSystem.IsWindows()) Skip.Test("Git for Windows shell selection is Windows-only");
        using var tree = new TempTree();
        var system = Directory.CreateDirectory(Path.Combine(tree.Root, "system")).FullName;
        var git = Directory.CreateDirectory(Path.Combine(tree.Root, "git", "cmd")).FullName;
        var bin = Directory.CreateDirectory(Path.Combine(tree.Root, "git", "bin")).FullName;
        File.WriteAllText(Path.Combine(system, "bash.exe"), "");
        File.WriteAllText(Path.Combine(git, "git.exe"), "");
        var expected = Path.Combine(bin, "bash.exe");
        File.WriteAllText(expected, "");

        var path = string.Join(Path.PathSeparator, system, git);
        await Assert.That(Executables.Resolve("bash", path, system)).IsEqualTo(expected);
        File.Delete(expected);
        await Assert.That(() => Executables.Resolve("bash", path, system)).Throws<HarnessException>();
    }

    [Test]
    [Timeout(30_000)]
    public async Task Shell_ResolvedAndRecorded(CancellationToken ct)
    {
        using var lab = new CoreLab();
        var run = await lab.TaskAsync("inspect", ct);
        var shell = OperatingSystem.IsWindows() ? "pwsh" : "bash";
        var bin = Directory.CreateDirectory(Path.Combine(lab.Tree.Root, "shells")).FullName;
        var fakeShell = Path.Combine(bin, OperatingSystem.IsWindows() ? "pwsh.exe" : "bash");
        File.WriteAllText(fakeShell, "");
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(fakeShell, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        await Assert.That(Verifier.ResolveShell(new TaskSpec { Shell = shell }, bin)).IsEqualTo(fakeShell);
        await Assert.That(Verifier.ResolveShell(new TaskSpec(), bin)).IsEqualTo(fakeShell);
        run.Record.Spec.Shell = shell;
        run.Record.Spec.Verify = [OperatingSystem.IsWindows() ? "Write-Output shell-selected" : "printf 'shell-selected\\n'"];
        var result = new TaskResult { Task = run.Id, Outcome = "succeeded" };

        await Verifier.VerifyAsync(lab.Ledger, run, result, ct);
        lab.Ledger.SaveResult(result);

        await Assert.That(lab.Ledger.LoadResult(run.Id)!.VerifyShell).IsEqualTo(Executables.Resolve(shell));
        await Assert.That(result.Verify.Single().ExitCode).IsEqualTo(0);
        await Assert.That(File.ReadAllText(Path.Combine(lab.Ledger.RunId, run.Id, "verify.log"))).Contains("shell-selected");
    }

    [Test]
    [Arguments("bash")]
    [Arguments("pwsh")]
    [Arguments("missing-shell")]
    [Arguments("")]
    [Timeout(30_000)]
    public async Task Plan_MissingShell_Refuses(string shell, CancellationToken ct)
    {
        using var lab = new CoreLab();
        await lab.TaskAsync("inspect", ct);
        var tasks = Path.Combine(lab.Tree.Root, "tasks.jsonc");
        var document = System.Text.Json.Nodes.JsonNode.Parse("""{"schemaVersion":2,"tasks":[{"id":"inspect","kind":"research","base":"main","route":"fixture/model","prompt":"Inspect.","verify":["exit 0"]}]}""")!;
        document["tasks"]![0]!["shell"] = shell;
        await File.WriteAllTextAsync(tasks, document.ToJsonString(), ct);

        var plan = await Planner.LoadAsync(tasks, lab.Tree.Repository, AllowList.From(null), ct, lab.Git, executableSearchPath: lab.Tree.Root);

        await Assert.That(plan.Errors.Any(error => error.Contains("shell", StringComparison.Ordinal))).IsTrue();
        await Assert.That(plan.Tasks).IsEmpty();
    }
}
