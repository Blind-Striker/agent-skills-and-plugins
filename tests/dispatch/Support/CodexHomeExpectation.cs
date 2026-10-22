namespace Dispatch.Tests.Support;

internal static class CodexHomeExpectation
{
    // The override models the volume mismatch in tests that inject a cross-volume link failure.
    public static string RootFor(string userHome, bool? sameVolume = null)
    {
        var temp = Path.GetTempPath();
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var useTemp = sameVolume ?? string.Equals(Volume(userHome), Volume(temp), comparison);
        var root = useTemp ? Path.Combine(temp, "deniz-dispatch-codex")
            : Path.Combine(Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(userHome)))!, ".dispatch-codex");
        return root + Path.DirectorySeparatorChar;
    }

    static string Volume(string path)
    {
        // Resolve aliases before finding the deepest mount, including the system temp's aliases.
        var fullPath = Path.GetFullPath(path);
        var current = Path.GetPathRoot(fullPath)!;
        foreach (var segment in fullPath[current.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            var directory = new DirectoryInfo(Path.Combine(current, segment));
            current = directory.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? directory.FullName;
        }
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return DriveInfo.GetDrives().Select(drive => drive.RootDirectory.FullName)
            .Where(root => string.Equals(current, Path.TrimEndingDirectorySeparator(root), comparison)
                || current.StartsWith(Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar, comparison))
            .OrderByDescending(root => root.Length).FirstOrDefault() ?? Path.GetPathRoot(current)!;
    }
}
