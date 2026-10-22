static class Executables
{
    public static string Resolve(string name) => Resolve(name, Environment.GetEnvironmentVariable("PATH") ?? "");

    public static string Resolve(string name, string path, string? windowsSystemFolder = null)
    {
        if (Path.IsPathFullyQualified(name))
            return File.Exists(name) ? name : throw new HarnessException($"executable does not exist: {name}");
        if (name != Path.GetFileName(name)) throw new HarnessException("executable must be a name or an absolute path");
        if (OperatingSystem.IsWindows() && name.Equals("bash", StringComparison.OrdinalIgnoreCase))
        {
            windowsSystemFolder ??= Environment.SystemDirectory;
            var git = Find("git.exe");
            if (git is not null)
            {
                var directory = Path.GetDirectoryName(git)!;
                var root = Path.GetDirectoryName(directory)!;
                if (Path.GetFileName(root).StartsWith("mingw", StringComparison.OrdinalIgnoreCase)) root = Path.GetDirectoryName(root)!;
                foreach (var relative in new[] { Path.Combine("bin", "bash.exe"), Path.Combine("usr", "bin", "bash.exe") })
                {
                    var candidate = Path.Combine(root, relative);
                    if (File.Exists(candidate) && !IsSystemFile(candidate)) return Path.GetFullPath(candidate);
                }
            }
            return Find("bash.exe", excludeSystem: true) ?? throw new HarnessException("required executable is not on PATH: bash (Windows system launcher is not supported)");
        }
        var filename = OperatingSystem.IsWindows() && !name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name + ".exe" : name;
        return Find(filename) ?? throw new HarnessException($"required executable is not on PATH: {name}");

        bool IsSystemFile(string candidate) => windowsSystemFolder is not null &&
            (Path.GetFullPath(candidate).StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(windowsSystemFolder)) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));

        string? Find(string file, bool excludeSystem = false)
        {
            foreach (var entry in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                var directory = entry.Trim('"');
                // Relative and empty PATH entries must never select a repository-owned executable.
                if (!Path.IsPathFullyQualified(directory)) continue;
                var candidate = Path.Combine(directory, file);
                if (!File.Exists(candidate) || (excludeSystem && IsSystemFile(candidate))) continue;
                if (!OperatingSystem.IsWindows() && (File.GetUnixFileMode(candidate)
                    & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) == 0) continue;
                return Path.GetFullPath(candidate);
            }
            return null;
        }
    }
}
