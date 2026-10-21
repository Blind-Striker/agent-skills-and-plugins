using System.Text.Json.Nodes;
using Dispatch.Tests.Support;

namespace Dispatch.Tests.Integration;

[Category("Integration")]
[NotInParallel]
public sealed class ScratchSafetyTests
{
    [Test]
    [Arguments("record", false)]
    [Arguments("record", true)]
    [Arguments("marker", false)]
    [Arguments("marker", true)]
    [Arguments("both", false)]
    [Arguments("both", true)]
    [Arguments("empty", false)]
    [Arguments("empty", true)]
    [Timeout(30_000)]
    public async Task Scratch_refuses_missing_allocation_identity(string missing, bool prepare, CancellationToken ct)
    {
        using var lab = new CoreLab();
        using var scratch = new ScratchFixture();
        await ScratchFixture.AssertNoRepositoryAsync(scratch.Root);
        var run = await lab.TaskAsync("answer", ct);
        lab.Structured(run);
        run.Record.Spec.Project = "off";
        var folder = scratch.Allocate(run, lab.Ledger, "owned");
        await ScratchFixture.AssertNoRepositoryAsync(folder);
        var evidence = Path.Combine(folder, "keep.txt");
        File.WriteAllText(evidence, "untouched");
        if (missing != "marker") run.Record.Workspace.ScratchAllocationId = missing == "empty" ? "" : null;
        if (missing != "record")
        {
            var marker = Path.Combine(folder, ".dispatch-owner.json");
            var document = JsonNode.Parse(File.ReadAllText(marker))!.AsObject();
            if (missing == "empty") document["allocationId"] = "";
            else document.Remove("allocationId");
            File.WriteAllText(marker, document.ToJsonString());
        }
        lab.Ledger.SaveRecord(run.Record);
        var recovered = TaskRun.FromLedger(lab.Ledger.LoadRecord(run.Id), lab.Ledger);

        await Assert.That(() => { if (prepare) Scratch.Prepare(recovered, lab.Ledger); else Scratch.Delete(recovered.Record, lab.Ledger); })
            .Throws<HarnessException>().WithMessage("structured scratch ownership does not match the run, task and allocation");
        await Assert.That(File.ReadAllText(evidence)).IsEqualTo("untouched");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    [Timeout(30_000)]
    public async Task Scratch_same_run_and_task_in_two_ledgers_cannot_claim_another_allocation(bool prepare, CancellationToken ct)
    {
        using var lab = new CoreLab("shared-run");
        using var scratch = new ScratchFixture();
        await ScratchFixture.AssertNoRepositoryAsync(scratch.Root);
        var run = await lab.TaskAsync("answer", ct);
        lab.Structured(run);
        run.Record.Spec.Project = "off";
        scratch.Allocate(run, lab.Ledger, "a");
        var firstRoot = Locations.LedgerRoot;
        var otherRun = new TaskRun { Record = new() { Spec = run.Record.Spec } };
        string folder;
        try
        {
            Locations.Configure(null, Path.Combine(lab.Tree.Root, "other-ledger"));
            var other = new Ledger(lab.Ledger.RunId);
            await Assert.That(other.Dir).IsNotEqualTo(Path.Combine(firstRoot, lab.Ledger.RunId));
            folder = scratch.Allocate(otherRun, other, "b");
        }
        finally { Locations.Configure(null, firstRoot); }
        await ScratchFixture.AssertNoRepositoryAsync(folder);
        var evidence = Path.Combine(folder, "keep.txt");
        File.WriteAllText(evidence, "other worker is live");
        run.Record.Workspace.Path = folder;
        lab.Ledger.SaveRecord(run.Record);
        var recovered = TaskRun.FromLedger(lab.Ledger.LoadRecord(run.Id), lab.Ledger);

        await Assert.That(() => { if (prepare) Scratch.Prepare(recovered, lab.Ledger); else Scratch.Delete(recovered.Record, lab.Ledger); })
            .Throws<HarnessException>().WithMessage("structured scratch ownership does not match the run, task and allocation");
        await Assert.That(File.ReadAllText(evidence)).IsEqualTo("other worker is live");
    }

    [Test]
    [Timeout(30_000)]
    public async Task Scratch_owned_reloaded_folder_cleans_read_only_files(CancellationToken ct)
    {
        using var lab = new CoreLab();
        using var scratch = new ScratchFixture();
        await ScratchFixture.AssertNoRepositoryAsync(scratch.Root);
        var run = await lab.TaskAsync("answer", ct);
        lab.Structured(run);
        run.Record.Spec.Project = "off";
        var folder = scratch.Allocate(run, lab.Ledger, "owned");
        await ScratchFixture.AssertNoRepositoryAsync(folder);
        var file = Path.Combine(Directory.CreateDirectory(Path.Combine(folder, "nested")).FullName, "read-only.txt");
        File.WriteAllText(file, "temporary");
        File.SetAttributes(file, FileAttributes.ReadOnly);
        var recovered = TaskRun.FromLedger(lab.Ledger.LoadRecord(run.Id), lab.Ledger);
        Scratch.Prepare(recovered, lab.Ledger);
        Scratch.Delete(recovered.Record, lab.Ledger);
        await Assert.That(Directory.Exists(folder)).IsFalse();
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    [Timeout(30_000)]
    public async Task Scratch_refuses_owned_folders_with_git_on_the_path(bool ancestor, bool prepare, CancellationToken ct)
    {
        using var lab = new CoreLab();
        using var scratch = new ScratchFixture();
        await ScratchFixture.AssertNoRepositoryAsync(scratch.Root);
        var run = await lab.TaskAsync("answer", ct);
        lab.Structured(run);
        run.Record.Spec.Project = "off";
        var folder = scratch.Allocate(run, lab.Ledger, "owned");
        await ScratchFixture.AssertNoRepositoryAsync(folder);
        var git = Path.Combine(ancestor ? scratch.Root : folder, ".git");
        File.WriteAllText(git, "fixture");
        var evidence = Path.Combine(folder, "keep.txt");
        File.WriteAllText(evidence, "untouched");

        await Assert.That(() => { if (prepare) Scratch.Prepare(run, lab.Ledger); else Scratch.Delete(run.Record, lab.Ledger); })
            .Throws<HarnessException>().WithMessage($"structured scratch refuses a repository: {git}");
        await Assert.That(File.ReadAllText(evidence)).IsEqualTo("untouched");
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    [Timeout(30_000)]
    public async Task Scratch_refuses_reparse_roots_and_ancestors(bool ancestor, bool prepare, CancellationToken ct)
    {
        using var lab = new CoreLab();
        using var scratch = new ScratchFixture();
        await ScratchFixture.AssertNoRepositoryAsync(scratch.Root);
        var run = await lab.TaskAsync("answer", ct);
        lab.Structured(run);
        run.Record.Spec.Project = "off";
        var original = scratch.Allocate(run, lab.Ledger, "owned");
        var parent = Directory.CreateDirectory(Path.Combine(scratch.Root, "target")).FullName;
        var folder = Path.Combine(parent, "dispatch-scratch-x");
        Directory.Move(original, folder);
        File.WriteAllText(Path.Combine(folder, "keep.txt"), "untouched");
        var link = Path.Combine(scratch.Root, "dispatch-scratch-link");
        if (OperatingSystem.IsWindows())
        {
            using var child = new CapturedProcess();
            (await child.RunAsync("cmd", ["/c", "mklink", "/J", link, ancestor ? parent : folder], lab.Tree.Root, lab.Tree.ChildEnvironment(), ct))
                .RequireSuccess("create junction");
        }
        else Directory.CreateSymbolicLink(link, ancestor ? parent : folder);
        run.Record.Workspace.Path = ancestor ? Path.Combine(link, "dispatch-scratch-x") : link;
        await ScratchFixture.AssertNoRepositoryAsync(run.Record.Workspace.Path);
        try
        {
            await Assert.That(() => { if (prepare) Scratch.Prepare(run, lab.Ledger); else Scratch.Delete(run.Record, lab.Ledger); })
                .Throws<HarnessException>().WithMessage($"structured scratch refuses a reparse point: {link}");
            await Assert.That(File.ReadAllText(Path.Combine(folder, "keep.txt"))).IsEqualTo("untouched");
        }
        finally
        {
            if (new DirectoryInfo(link).LinkTarget is not null) Directory.Delete(link);
        }
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    [Timeout(30_000)]
    public async Task Scratch_refuses_unowned_recorded_folders_without_touching_them(bool repository, bool prepare, CancellationToken ct)
    {
        using var lab = new CoreLab();
        using var scratch = new ScratchFixture();
        await ScratchFixture.AssertNoRepositoryAsync(scratch.Root);
        var run = await lab.TaskAsync("answer", ct);
        lab.Structured(run);
        run.Record.Spec.Project = "off";
        var folder = Directory.CreateDirectory(Path.Combine(scratch.Root, "dispatch-scratch-x")).FullName;
        await ScratchFixture.AssertNoRepositoryAsync(folder);
        var evidence = Path.Combine(folder, "keep.txt");
        File.WriteAllText(evidence, "untouched");
        if (repository) File.WriteAllText(Path.Combine(folder, ".git"), "fixture");
        run.Record.Workspace = new() { Path = folder };
        lab.Ledger.SaveRecord(run.Record);
        var recovered = TaskRun.FromLedger(lab.Ledger.LoadRecord(run.Id), lab.Ledger);

        await Assert.That(() => { if (prepare) Scratch.Prepare(recovered, lab.Ledger); else Scratch.Delete(recovered.Record, lab.Ledger); })
            .Throws<HarnessException>().WithMessage(repository
                ? $"structured scratch refuses a repository: {Path.Combine(folder, ".git")}"
                : "structured scratch ownership marker is missing or unsafe");
        await Assert.That(File.ReadAllText(evidence)).IsEqualTo("untouched");
    }
}
