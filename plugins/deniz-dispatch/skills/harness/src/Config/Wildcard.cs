// Upstream's matcher (core/src/util/wildcard.ts), so the harness reads a rule exactly as the
// server does: backslashes become slashes, '*' is any text, '?' is one character, a trailing
// " *" also matches nothing, and Windows compares case-insensitively.
static class Wildcard
{
    public static bool Match(string input, string pattern, bool? ignoreCase = null)
    {
        var escaped = new StringBuilder();
        foreach (var c in pattern.Replace('\\', '/'))
        {
            escaped.Append(c switch
            {
                '*' => ".*",
                '?' => ".",
                '.' or '+' or '^' or '$' or '{' or '}' or '(' or ')' or '|' or '[' or ']' => $"\\{c}",
                _ => c.ToString(),
            });
        }

        var regex = escaped.ToString();
        if (regex.EndsWith(" .*", StringComparison.Ordinal))
        {
            regex = regex[..^3] + "( .*)?";
        }

        var options = RegexOptions.Singleline | RegexOptions.CultureInvariant | ((ignoreCase ?? OperatingSystem.IsWindows()) ? RegexOptions.IgnoreCase : RegexOptions.None);
        return Regex.IsMatch(input.Replace('\\', '/'), $"^{regex}\\z", options);
    }
}
