using Dispatch.Tests.Support;

namespace Dispatch.Tests.Integration;

[Category("Integration")]
[NotInParallel]
public sealed class ScratchSafetyTests
{
    [Test]
    [Timeout(30_000)]
    public async Task Scratch_owned_reloaded_folder_cleans_read_only_files(CancellationToken ct)
    {
        using var lab = new CoreLab();
        var run = await lab.TaskAsync("answer", ct);
        lab.Structured(run);
        run.Record.Spec.Project = "off";
        run.Record.Workspace = new();
        Scratch.Prepare(run, lab.Ledger);
        var folder = run.Record.Workspace.Path!;
        var file = Path.Combine(Directory.CreateDirectory(Path.Combine(folder, "nested")).FullName, "read-only.txt");
        File.WriteAllText(file, "temporary");
        File.SetAttributes(file, FileAttributes.ReadOnly);
        try
        {
            var recovered = TaskRun.FromLedger(lab.Ledger.LoadRecord(run.Id), lab.Ledger);
            Scratch.Prepare(recovered, lab.Ledger);
            Scratch.Delete(recovered.Record, lab.Ledger);
            await Assert.That(Directory.Exists(folder)).IsFalse();
        }
        finally
        {
            if (File.Exists(file)) File.SetAttributes(file, FileAttributes.Normal);
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
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
        var run = await lab.TaskAsync("answer", ct);
        lab.Structured(run);
        run.Record.Spec.Project = "off";
        run.Record.Workspace = new();
        Scratch.Prepare(run, lab.Ledger);
        var original = run.Record.Workspace.Path!;
        var folder = Path.Combine(lab.Tree.Root, "dispatch-scratch-x");
        Directory.Move(original, folder);
        run.Record.Workspace.Path = folder;
        File.WriteAllText(Path.Combine(ancestor ? lab.Tree.Root : folder, ".git"), "fixture");
        var evidence = Path.Combine(folder, "keep.txt");
        File.WriteAllText(evidence, "untouched");

        await Assert.That(() => { if (prepare) Scratch.Prepare(run, lab.Ledger); else Scratch.Delete(run.Record, lab.Ledger); })
            .Throws<HarnessException>();
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
        var run = await lab.TaskAsync("answer", ct);
        lab.Structured(run);
        run.Record.Spec.Project = "off";
        run.Record.Workspace = new();
        Scratch.Prepare(run, lab.Ledger);
        var original = run.Record.Workspace.Path!;
        var parent = Directory.CreateDirectory(Path.Combine(lab.Tree.Root, "target")).FullName;
        var folder = Path.Combine(parent, "dispatch-scratch-x");
        Directory.Move(original, folder);
        File.WriteAllText(Path.Combine(folder, "keep.txt"), "untouched");
        var link = Path.Combine(lab.Tree.Root, "dispatch-scratch-link");
        if (OperatingSystem.IsWindows())
        {
            using var child = new CapturedProcess();
            (await child.RunAsync("cmd", ["/c", "mklink", "/J", link, ancestor ? parent : folder], lab.Tree.Root, lab.Tree.ChildEnvironment(), ct))
                .RequireSuccess("create junction");
        }
        else Directory.CreateSymbolicLink(link, ancestor ? parent : folder);
        run.Record.Workspace.Path = ancestor ? Path.Combine(link, "dispatch-scratch-x") : link;
        try
        {
            await Assert.That(() => { if (prepare) Scratch.Prepare(run, lab.Ledger); else Scratch.Delete(run.Record, lab.Ledger); })
                .Throws<HarnessException>();
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
        var run = await lab.TaskAsync("answer", ct);
        lab.Structured(run);
        run.Record.Spec.Project = "off";
        var folder = Directory.CreateDirectory(Path.Combine(lab.Tree.Root, "dispatch-scratch-x")).FullName;
        var evidence = Path.Combine(folder, "keep.txt");
        File.WriteAllText(evidence, "untouched");
        if (repository) File.WriteAllText(Path.Combine(folder, ".git"), "fixture");
        run.Record.Workspace = new() { Path = folder };
        lab.Ledger.SaveRecord(run.Record);
        var recovered = TaskRun.FromLedger(lab.Ledger.LoadRecord(run.Id), lab.Ledger);

        await Assert.That(() => { if (prepare) Scratch.Prepare(recovered, lab.Ledger); else Scratch.Delete(recovered.Record, lab.Ledger); })
            .Throws<HarnessException>();
        await Assert.That(File.ReadAllText(evidence)).IsEqualTo("untouched");
    }
}
