sealed class Args
{
    static readonly HashSet<string> ValueOptions = ["dir", "repo", "max", "run", "policy", "message"];

    public string Command { get; private init; } = "";
    public List<string> Positional { get; } = [];
    public Dictionary<string, string?> Options { get; } = new(StringComparer.Ordinal);

    public static Args Parse(string[] argv)
    {
        var parsed = new Args { Command = argv.Length > 0 ? argv[0] : "" };
        for (var i = 1; i < argv.Length; i++)
        {
            var token = argv[i];
            if (token.StartsWith("--", StringComparison.Ordinal))
            {
                var name = token[2..];
                if (ValueOptions.Contains(name))
                {
                    if (i + 1 >= argv.Length)
                    {
                        throw new HarnessException($"--{name} needs a value");
                    }

                    parsed.Options[name] = argv[++i];
                }
                else
                {
                    parsed.Options[name] = null;
                }
            }
            else
            {
                parsed.Positional.Add(token);
            }
        }

        return parsed;
    }

    public string? Get(string name) => Options.TryGetValue(name, out var value) ? value : null;

    public bool Has(string name) => Options.ContainsKey(name);

    public void ValidatePaths()
    {
        foreach (var name in new[] { "repo", "dir", "policy" })
        {
            if (Get(name) is { } path && !Path.IsPathFullyQualified(path))
            {
                throw new HarnessException($"--{name} must be an absolute path");
            }
        }

        if (Command is "plan" or "run" && Positional.Count > 0 && !Path.IsPathFullyQualified(Positional[0]))
        {
            throw new HarnessException("task file must be an absolute path");
        }
    }

    public string Require(int index, string what) =>
        index < Positional.Count ? Positional[index] : throw new HarnessException($"missing {what}");
}
