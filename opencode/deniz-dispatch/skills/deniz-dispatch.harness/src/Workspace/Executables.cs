static class Executables
{
    public static string Resolve(string name) => Resolve(name, Environment.GetEnvironmentVariable("PATH") ?? "");

    public static string Resolve(string name, string path)
    {
        if (Path.IsPathFullyQualified(name))
            return File.Exists(name) ? name : throw new HarnessException($"executable does not exist: {name}");
        if (name != Path.GetFileName(name)) throw new HarnessException("executable must be a name or an absolute path");
        var filename = OperatingSystem.IsWindows() && !name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name + ".exe" : name;
        foreach (var entry in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var directory = entry.Trim('"');
            // Relative and empty PATH entries must never select a repository-owned executable.
            if (!Path.IsPathFullyQualified(directory)) continue;
            var candidate = Path.Combine(directory, filename);
            if (!File.Exists(candidate)) continue;
            if (!OperatingSystem.IsWindows() && (File.GetUnixFileMode(candidate)
                & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) == 0) continue;
            return Path.GetFullPath(candidate);
        }
        throw new HarnessException($"required executable is not on PATH: {name}");
    }
}
