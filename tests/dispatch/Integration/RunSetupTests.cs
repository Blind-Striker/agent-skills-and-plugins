using Dispatch.Tests.Support;

namespace Dispatch.Tests.Integration;

[Category("Integration")]
[NotInParallel]
public sealed class RunSetupTests
{
    [Test]
    [Arguments("claimed")]
    [Arguments("queued")]
    [Timeout(60_000)]
    public async Task Run_LockHeldFromBeforeClaim(string stopAt, CancellationToken ct)
    {
        await using var lab = new OpenCodeLab();
        var repo = await LabRepo.CreateAsync(lab.Tree, ct);
        var tasks = Path.Combine(lab.Tree.Root, "tasks.jsonc");
        File.WriteAllText(tasks, """{"schemaVersion":2,"run":"setup","defaults":{"backend":"opencode"},"tasks":[{"id":"inspect","kind":"research","base":"main","route":"fixture/model","prompt":"Inspect."}]}""");
        lab.Write();
        var visited = false;
        var verified = false;
        var result = await Commands.RunAsync(Args.Parse(["run", tasks, "--repo", repo.Root]), ct, async (info, stage) =>
        {
            if (stage != stopAt) return;
            visited = true;
            var control = Path.Combine(lab.Tree.DispatchHome, "state", "control", info.ControlId!);
            await Assert.That(File.Exists(Path.Combine(control, "setup.done"))).IsFalse();
            foreach (var command in new[] { "resume", "collect", "clean" })
            {
                using var child = new HarnessProcess(lab.Tree);
                var refused = await child.RunAsync(command == "collect" ? [command, info.Run, "inspect"] : [command, info.Run], ct);
                await Assert.That(refused.ExitCode).IsEqualTo(2);
                await Assert.That(refused.Stderr).Contains("supervised");
            }
            using var status = new HarnessProcess(lab.Tree);
            var shown = await status.RunAsync(["status", info.Run], ct);
            shown.RequireSuccess("status during setup");
            await Assert.That(shown.Stdout).Contains("supervised now");
            verified = true;
            throw new IOException("setup interrupted");
        });
        await Assert.That(visited).IsTrue();
        await Assert.That(verified).IsTrue();
        await Assert.That(result).IsEqualTo(1);
        var run = Commands.CreateRunId("setup");
        foreach (var command in new[] { "resume", "collect" })
        {
            using var child = new HarnessProcess(lab.Tree);
            var refused = await child.RunAsync(command == "collect" ? [command, run, "inspect"] : [command, run], ct);
            await Assert.That(refused.ExitCode).IsEqualTo(2);
            await Assert.That(refused.Stderr).Contains("setup never finished");
        }
        using var clean = new HarnessProcess(lab.Tree);
        (await clean.RunAsync(["clean", run], ct)).RequireSuccess("clean unfinished run");
        var folder = Locations.ControlDir(run);
        await Assert.That(Directory.GetFileSystemEntries(folder).Select(x => Path.GetFileName(x)).Order().ToArray())
            .IsEquivalentTo(new[] { "run.ref", "supervisor.lock" });
        using (var reacquired = SupervisorLock.AcquireFolder(folder)) { }
        await Assert.That(File.Exists(Path.Combine(folder, "supervisor.lock"))).IsTrue();
    }
}
