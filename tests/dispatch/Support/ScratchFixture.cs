namespace Dispatch.Tests.Support;

internal sealed class ScratchFixture : IDisposable
{
    public string Root { get; } = CanonicalDirectory(Directory.CreateTempSubdirectory("dispatch-safety-").FullName);

    public static async Task AssertNoRepositoryAsync(string path)
    {
        for (var directory = new DirectoryInfo(Path.GetFullPath(path)); directory is not null; directory = directory.Parent)
            await Assert.That(Path.Exists(Path.Combine(directory.FullName, ".git"))).IsFalse();
    }

    public string Allocate(TaskRun run, Ledger ledger, string name)
    {
        run.Record.Workspace = new();
        Scratch.Prepare(run, ledger);
        var folder = Path.Combine(Root, "dispatch-scratch-" + name);
        Directory.Move(run.Record.Workspace.Path!, folder);
        run.Record.Workspace.Path = folder;
        ledger.SaveRecord(run.Record);
        return folder;
    }

    public static string CanonicalDirectory(string path)
    {
        path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var current = Path.GetPathRoot(path)!;
        foreach (var segment in path[current.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            var directory = new DirectoryInfo(Path.Combine(current, segment));
            current = directory.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? directory.FullName;
        }
        return current;
    }

    public void Dispose()
    {
        if (!Directory.Exists(Root)) return;
        foreach (var file in Directory.EnumerateFiles(Root, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint }))
            File.SetAttributes(file, File.GetAttributes(file) & ~FileAttributes.ReadOnly);
        Directory.Delete(Root, recursive: true);
    }
}
