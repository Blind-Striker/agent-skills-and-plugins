using System.ComponentModel;
using System.Runtime.InteropServices;

// Dispose only after the app-server and its descendants have stopped.
sealed class CodexHome : IDisposable
{
    CodexHome() { }

    string? markerName;
    string? markerText;
    public string Home { get; private init; } = "";
    public bool Temporary { get; private init; }
    public string Profile { get; private init; } = "";
    public string? Reason { get; private init; }
    public JsonObject RuntimeConfig { get; private init; } = new();
    // These are the values of individual -c arguments, not shell command text. Never flatten
    // descendants: the app-server splits dotted override names even inside quoted path keys.
    public IReadOnlyList<string> ConfigOverrides => RuntimeConfig.Select(pair => $"{pair.Key}={TomlValue(pair.Value!)}").ToArray();

    // The random marker is looked up through the reported folder, so symlinks, case aliases
    // and short path names work without comparing the server's canonical path as text.
    public bool MatchesReportedHome(string reportedHome)
    {
        if (!Temporary || markerName is null || !Path.IsPathFullyQualified(reportedHome)) return false;
        try { return File.ReadAllText(Path.Combine(reportedHome, markerName)) == markerText; }
        catch (IOException) { return false; } // Missing or unreadable marker is not proof of the home.
        catch (UnauthorizedAccessException) { return false; }
    }

    public static CodexHome CreateProbe(BackendContext context, string userHome, string directory,
        string project = "off", string? windowsSandbox = null, Action<string, string>? createHardLink = null) =>
        CreateClean(context, "probe-codex", "preflight", "probe", RuntimeOverrides(directory, project, "clean"),
            userHome, windowsSandbox, createHardLink);

    public static CodexHome Create(BackendContext context, WorkerLaunch launch, string userHome,
        string? windowsSandbox = null, Action<string, string>? createHardLink = null)
    {
        if (!Regex.IsMatch(launch.TaskId, "^[a-z0-9][a-z0-9-]{0,39}$"))
            throw new ArgumentException("task id must be a safe profile folder suffix", nameof(launch));
        var config = RuntimeOverrides(launch.Directory, launch.Modes.Project, launch.Modes.Profile);
        if (launch.Modes.Profile == "full")
            return new() { Home = userHome, Profile = "full", RuntimeConfig = config };
        if (launch.ProfileActual == "clean-partial")
            return new() { Home = userHome, Profile = "clean-partial", Reason = launch.ProfileReason, RuntimeConfig = config };
        return CreateClean(context, "codex-" + launch.TaskId, launch.RunId, launch.TaskId, config, userHome, windowsSandbox, createHardLink);
    }

    static JsonObject RuntimeOverrides(string directory, string project, string profile)
    {
        var config = new JsonObject
        {
            ["projects"] = new JsonObject
            {
                [TrustKey(directory)] = new JsonObject
                {
                    ["trust_level"] = project == "on" ? "trusted" : "untrusted",
                },
            },
        };
        if (project == "off") config["project_doc_max_bytes"] = 0;
        if (profile == "clean")
        {
            config["include_skill_instructions"] = false;
            config["features"] = new JsonObject { ["apps"] = false, ["plugins"] = false, ["remote_plugin"] = false };
        }
        return config;
    }

    static CodexHome CreateClean(BackendContext context, string folderName, string runId, string taskId,
        JsonObject config, string userHome, string? windowsSandbox, Action<string, string>? createHardLink)
    {
        if (File.Exists(Path.Combine(userHome, "auth.json")))
        {
            var folder = Path.Combine(context.ProfilesFolder, folderName);
            if (Path.Exists(folder))
                return new() { Home = userHome, Profile = "clean-partial", Reason = "temporary Codex home already exists", RuntimeConfig = config };
            if (OperatingSystem.IsWindows()) Directory.CreateDirectory(folder);
            else Directory.CreateDirectory(folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var complete = false;
            try
            {
                try { (createHardLink ?? CreateHardLink)(Path.Combine(folder, "auth.json"), Path.Combine(userHome, "auth.json")); }
                catch (Exception exception) when (exception is Win32Exception or IOException or UnauthorizedAccessException)
                {
                    var detail = exception is Win32Exception native
                        ? $"OS error {native.NativeErrorCode.ToString(CultureInfo.InvariantCulture)}"
                        : exception.GetType().Name;
                    return new()
                    {
                        Home = userHome, Profile = "clean-partial", Reason = $"auth.json hardlink failed ({detail})", RuntimeConfig = config,
                    };
                }
                var fileConfig = (JsonObject)config.DeepClone();
                fileConfig["cli_auth_credentials_store"] = "file";
                if (OperatingSystem.IsWindows() && windowsSandbox is not null)
                    fileConfig["windows"] = new JsonObject { ["sandbox"] = windowsSandbox };
                Json.WriteBytes(Path.Combine(folder, "config.toml"), Encoding.UTF8.GetBytes(
                    string.Join("\n", fileConfig.Select(pair => $"{pair.Key} = {TomlValue(pair.Value!)}")) + "\n"));
                var marker = new JsonObject { ["run"] = runId, ["task"] = taskId };
                var name = $".dispatch-home-{Guid.NewGuid():N}";
                var text = marker.ToJsonString() + "\n";
                Json.WriteBytes(Path.Combine(folder, name), Encoding.UTF8.GetBytes(text));
                var home = new CodexHome { Home = folder, Temporary = true, Profile = "clean", RuntimeConfig = config, markerName = name, markerText = text };
                complete = true;
                return home;
            }
            finally
            {
                if (!complete) Directory.Delete(folder, recursive: true);
            }
        }
        return new()
        {
            Home = userHome, Profile = "clean-partial", Reason = "user home has no auth.json", RuntimeConfig = config,
        };
    }

    static string TrustKey(string directory)
    {
        var path = Path.GetFullPath(directory);
        // Codex folds ASCII case, not Unicode case, for Windows project trust lookup.
        return OperatingSystem.IsWindows()
            ? string.Concat(path.Select(character => character is >= 'A' and <= 'Z' ? (char)(character + ('a' - 'A')) : character))
            : path;
    }

    static string TomlValue(JsonNode node) => node switch
    {
        JsonObject table => "{ " + string.Join(", ", table.Select(pair => $"{Quote(pair.Key)} = {TomlValue(pair.Value!)}")) + " }",
        JsonValue value when value.TryGetValue<string>(out var text) => Quote(text),
        JsonValue value when value.TryGetValue<bool>(out var flag) => flag ? "true" : "false",
        JsonValue value when value.TryGetValue<int>(out var number) => number.ToString(CultureInfo.InvariantCulture),
        _ => throw new ArgumentException("unsupported clean-home config value", nameof(node)),
    };

    static string Quote(string text)
    {
        var result = new StringBuilder("\"");
        foreach (var character in text)
            result.Append(character switch
            {
                '"' => "\\\"", '\\' => "\\\\", '\b' => "\\b", '\t' => "\\t", '\n' => "\\n", '\f' => "\\f", '\r' => "\\r",
                < ' ' or '\u007f' => "\\u" + ((int)character).ToString("X4", CultureInfo.InvariantCulture),
                _ => character.ToString(),
            });
        return result.Append('"').ToString();
    }

    static void CreateHardLink(string destination, string source)
    {
        if (OperatingSystem.IsWindows()
            ? !Native.CreateHardLinkW(destination, source, IntPtr.Zero)
            : Native.link(source, destination) != 0)
            throw new Win32Exception(Marshal.GetLastPInvokeError());
    }

    public void Dispose()
    {
        if (Temporary && Directory.Exists(Home)) Directory.Delete(Home, recursive: true);
    }

    static class Native
    {
        // BOOL CreateHardLinkW(LPCWSTR newName, LPCWSTR existingName, LPSECURITY_ATTRIBUTES reserved).
        // Both strings are borrowed UTF-16 inputs; reserved must be null. No native allocation escapes.
        [DllImport("kernel32.dll", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CreateHardLinkW(string newName, string existingName, IntPtr reserved);

        // int link(const char *existingName, const char *newName): borrowed UTF-8 paths, errno on -1.
        [DllImport("libc", ExactSpelling = true, SetLastError = true, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int link([MarshalAs(UnmanagedType.LPUTF8Str)] string existingName,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string newName);
    }
}
