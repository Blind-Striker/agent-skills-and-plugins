using Microsoft.Win32;

sealed class UserPathGuard
{
    public const int WarningLength = 8000;
    public const int RefusalLength = 16000;
    public const string ValueName = @"HKCU\Environment\Path";
    const string Inspect = @"Inspect it with: reg query HKCU\Environment /v Path";
    readonly Func<string?> read;
    readonly string? initial;

    public UserPathGuard(Func<string?>? reader = null)
    {
        read = reader ?? ReadUserPath;
        initial = read();
    }

    public string? SelfCheckWarning => initial?.Length > WarningLength
        ? $"warning: {ValueName} is {initial.Length} characters (warning above {WarningLength}). {Inspect}"
        : null;

    public string? RunRefusal => initial?.Length > RefusalLength
        ? $"{ValueName} is {initial.Length} characters; run refused above {RefusalLength}. {Inspect}"
        : null;

    public void ReportChanges(Ledger ledger)
    {
        var current = read();
        if (string.Equals(initial, current, StringComparison.Ordinal)) return;
        ledger.Write(null, "warning", new JsonObject
        {
            ["reason"] = "user-path-changed",
            ["value"] = ValueName,
            ["message"] = $"{ValueName} changed during run. {Inspect}",
            ["beforeLength"] = initial?.Length ?? 0,
            ["afterLength"] = current?.Length ?? 0,
            ["added"] = Json.Array(Difference(current, initial)),
            ["removed"] = Json.Array(Difference(initial, current)),
        });
    }

    static IEnumerable<string> Difference(string? left, string? right)
    {
        var remaining = Entries(right).GroupBy(x => x, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        foreach (var entry in Entries(left))
        {
            if (remaining.TryGetValue(entry, out var count) && count > 0) remaining[entry] = count - 1;
            else yield return entry;
        }
    }

    static string[] Entries(string? value) => (value ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries);

    static string? ReadUserPath()
    {
        if (!OperatingSystem.IsWindows()) return null;
        using var key = Registry.CurrentUser.OpenSubKey("Environment", writable: false);
        return key?.GetValue("Path", null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
    }
}
