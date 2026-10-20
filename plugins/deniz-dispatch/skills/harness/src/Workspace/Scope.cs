static class Scope
{
    public static string? Overlap(List<string> a, List<string> b)
    {
        foreach (var x in a)
        {
            foreach (var y in b)
            {
                var px = Prefix(x);
                var py = Prefix(y);
                if (px.StartsWith(py, StringComparison.OrdinalIgnoreCase) || py.StartsWith(px, StringComparison.OrdinalIgnoreCase))
                {
                    return $"{x} ~ {y}";
                }
            }
        }

        return null;
    }

    static string Prefix(string glob)
    {
        var g = Normalize(glob);
        var cut = g.IndexOfAny(['*', '?', '[', '{']);
        return cut < 0 ? g : g[..cut];
    }

    static string Normalize(string glob)
    {
        // Strip a leading "./" or "/" only: trimming every leading dot turned ".github/..." into
        // "github/..." and flagged in-scope dot-folder edits as out of scope.
        var g = glob.Replace('\\', '/');
        while (g.StartsWith("./", StringComparison.Ordinal))
        {
            g = g[2..];
        }

        g = g.TrimStart('/');
        return g.EndsWith('/') ? g + "**" : g;
    }

    public static bool InScope(string path, List<string>? scope)
    {
        if (scope is not { Count: > 0 })
        {
            return true;
        }

        path = path.Replace('\\', '/');
        return scope.Any(glob => ToRegex(Normalize(glob)).IsMatch(path));
    }

    static Regex ToRegex(string glob)
    {
        var sb = new StringBuilder("^");
        for (var i = 0; i < glob.Length; i++)
        {
            var c = glob[i];
            if (c == '*')
            {
                if (i + 1 < glob.Length && glob[i + 1] == '*')
                {
                    i++;
                    if (i + 1 < glob.Length && glob[i + 1] == '/')
                    {
                        i++;
                        sb.Append("(?:.*/)?");
                    }
                    else
                    {
                        sb.Append(".*");
                    }
                }
                else
                {
                    sb.Append("[^/]*");
                }
            }
            else if (c == '?')
            {
                sb.Append("[^/]");
            }
            else
            {
                sb.Append(Regex.Escape(c.ToString()));
            }
        }

        // A plain path also covers everything below it.
        sb.Append(glob.IndexOfAny(['*', '?']) < 0 ? "(?:/.*)?$" : "$");
        return new Regex(sb.ToString(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}
