using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Dispatch.Tests.Support;

internal sealed class TempTree : IDisposable
{
    public string Root { get; }
    public string DispatchHome { get; }
    public string WorktreeRoot { get; }
    public string Repository { get; }
    readonly Dictionary<string, string?> environment;

    public TempTree()
    {
        // Keep lab writes and child build caches inside the checkout's ignored test output.
        var path = Path.Combine(TestPaths.Tests, "bin", "labs", Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(path);
        Root = CanonicalDirectory(path);
        DispatchHome = Folder("dispatch-home");
        WorktreeRoot = Folder("worktrees");
        Repository = Folder("repo");
        var home = Folder("home");
        var temp = Folder("temp");
        var gitConfig = Path.Combine(Root, "gitconfig");
        File.WriteAllText(gitConfig, "");

        environment = new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (var name in new[] { "PATH", "SystemRoot", "WINDIR", "COMSPEC", "PATHEXT", "ProgramFiles", "ProgramFiles(x86)", "ProgramW6432", "SystemDrive", "LANG", "LC_ALL", "TZ" })
        {
            if (Environment.GetEnvironmentVariable(name) is { } value)
                environment[name] = value;
        }
        foreach (string name in Environment.GetEnvironmentVariables().Keys)
        {
            if (name.Equals("DOTNET_ROOT", StringComparison.OrdinalIgnoreCase) || name.StartsWith("DOTNET_ROOT_", StringComparison.OrdinalIgnoreCase))
                environment[name] = Environment.GetEnvironmentVariable(name);
        }
        environment["DISPATCH_HOME"] = DispatchHome;
        environment["CODEX_HOME"] = Folder("user-codex");
        environment["HOME"] = home;
        environment["XDG_CONFIG_HOME"] = Folder("xdg-config");
        environment["XDG_DATA_HOME"] = Folder("xdg-data");
        environment["XDG_STATE_HOME"] = Folder("xdg-state");
        environment["GIT_CONFIG_GLOBAL"] = gitConfig;
        environment["GIT_CONFIG_NOSYSTEM"] = "1";
        environment["GIT_TERMINAL_PROMPT"] = "0";
        if (OperatingSystem.IsWindows())
        {
            environment["USERPROFILE"] = home;
            environment["APPDATA"] = Folder("appdata");
            environment["LOCALAPPDATA"] = Folder("localappdata");
        }
        environment["TEMP"] = temp;
        environment["TMP"] = temp;
        environment["TMPDIR"] = temp;
        // Unix pipe names have a much smaller limit than filesystem paths. Only testing-platform
        // sockets use the short system temp; lab files and all other child temp stay in the checkout.
        if (!OperatingSystem.IsWindows()) environment["TESTINGPLATFORM_PIPE_DIRECTORY"] = "/tmp";
        // On Windows the SDK's first run for a new CLI home appends that home's tools folder to the
        // user's PATH in the registry, which no environment isolation undoes. Share one CLI home per
        // checkout and switch the append off.
        environment["DOTNET_CLI_HOME"] = SharedDotnetCliHome.Value;
        environment["DOTNET_ADD_GLOBAL_TOOLS_TO_PATH"] = "false";
        // Reuse installed packages without inheriting the user's NuGet configuration or credentials.
        environment["NUGET_PACKAGES"] = Environment.GetEnvironmentVariable("NUGET_PACKAGES")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");
        environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        environment["DOTNET_NOLOGO"] = "1";
        environment["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1";
        environment["DOTNET_GENERATE_ASPNET_CERTIFICATE"] = "false";
        environment["DOTNET_CLI_USE_MSBUILD_SERVER"] = "0";
        environment["MSBUILDDISABLENODEREUSE"] = "1";
        environment["UseSharedCompilation"] = "false";
    }

    static readonly Lazy<string> SharedDotnetCliHome = new(() =>
        Directory.CreateDirectory(Path.Combine(TestPaths.Tests, "bin", "dotnet-cli-home")).FullName);

    public Dictionary<string, string?> ChildEnvironment() => new(environment, environment.Comparer);

    string Folder(string name) => Directory.CreateDirectory(Path.Combine(Root, name)).FullName;

    public void Dispose()
    {
        if (!Directory.Exists(Root)) return;
        // Never walk a test-created symlink into a different tree while clearing read-only files.
        foreach (var file in Directory.EnumerateFiles(Root, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint }))
            File.SetAttributes(file, File.GetAttributes(file) & ~FileAttributes.ReadOnly);
        Directory.Delete(Root, recursive: true);
    }

    static string CanonicalDirectory(string path)
    {
        path = Path.GetFullPath(path);
        if (OperatingSystem.IsWindows())
        {
            var buffer = new char[260];
            while (true)
            {
                var length = GetLongPathNameW(path, buffer, (uint)buffer.Length);
                if (length == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
                if (length < buffer.Length) return new string(buffer, 0, (int)length);
                buffer = new char[checked((int)length + 1)];
            }
        }
        var current = Path.GetPathRoot(path) ?? throw new IOException("temporary tree has no filesystem root");
        foreach (var segment in path[current.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            var directory = new DirectoryInfo(Path.Combine(current, segment));
            current = directory.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? directory.FullName;
        }
        return current;
    }

    // DWORD / UTF-16 input / caller-owned WCHAR output buffer / DWORD capacity.
    // DllImport avoids changing the project to allow unsafe generated interop code.
    [DllImport("kernel32.dll", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
    static extern uint GetLongPathNameW(string shortPath, [Out] char[] longPath, uint bufferLength);
}

internal static class TestPaths
{
    public static string Root { get; } = FindRoot();
    public static string Tests => Path.Combine(Root, "tests", "dispatch");
    public static string Harness => Path.Combine(Root, "skills", "deniz-dispatch", "harness");

    static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "tests", "dispatch", "Dispatch.Tests.csproj"))
                && File.Exists(Path.Combine(directory.FullName, "skills", "deniz-dispatch", "harness", "dispatch.cs")))
                return directory.FullName;
        }
        throw new DirectoryNotFoundException("the test project and harness entry were not found above the test assembly");
    }
}
