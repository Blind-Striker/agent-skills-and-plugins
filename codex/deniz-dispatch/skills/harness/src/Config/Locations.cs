static class Locations
{
    public static string ConfigRoot { get; private set; } = "";

    public static string StateRoot { get; private set; } = "";

    public static void Resolve()
    {
        if (Environment.GetEnvironmentVariable("DISPATCH_HOME") is { Length: > 0 } root)
        {
            if (!Path.IsPathFullyQualified(root))
                throw new HarnessException($"DISPATCH_HOME must be an absolute path: {root}");
            ConfigRoot = Path.Combine(root, "config");
            StateRoot = Path.Combine(root, "state");
            return;
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        ConfigRoot = Path.Combine(
            OperatingSystem.IsWindows()
                ? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
                : XdgHome("XDG_CONFIG_HOME", Path.Combine(home, ".config")),
            "deniz-dispatch");
        StateRoot = Path.Combine(
            OperatingSystem.IsWindows()
                ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
                : XdgHome("XDG_STATE_HOME", Path.Combine(home, ".local", "state")),
            "deniz-dispatch");
        if (!Path.IsPathFullyQualified(ConfigRoot) || !Path.IsPathFullyQualified(StateRoot))
            throw new HarnessException("cannot resolve the user config and state folders; set DISPATCH_HOME to an absolute path");
    }

    static string XdgHome(string name, string fallback) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } value && Path.IsPathFullyQualified(value)
            ? value
            : fallback;

    public static string AllowListFile => Path.Combine(ConfigRoot, "routes.allow.jsonc");

    public static string PolicyFile => Path.Combine(ConfigRoot, "policy.jsonc");

    public static string LedgerRoot => Path.Combine(StateRoot, "ledgers");

    public static string WorktreeRoot => Path.Combine(StateRoot, "worktrees");

    public static string Worktree(string task) => Path.Combine(WorktreeRoot, task);

    public static string TaskTemp(string task) => Path.Combine(WorktreeRoot, ".dispatch-tmp", task);

    public static string ControlRoot => Path.Combine(WorktreeRoot, ".dispatch");

    public static string ControlDir(string run) => Path.Combine(ControlRoot, run);

    // Escalated asks wait here as one JSON file each; the operator's decisions arrive next to them.
    public static string AsksDir(string run) => Path.Combine(ControlDir(run), "asks");

    public static string DecisionsDir(string run) => Path.Combine(ControlDir(run), "decisions");

    public static bool Same(string a, string b) =>
        string.Equals(
            Path.GetFullPath(a).TrimEnd('\\', '/'),
            Path.GetFullPath(b).TrimEnd('\\', '/'),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
