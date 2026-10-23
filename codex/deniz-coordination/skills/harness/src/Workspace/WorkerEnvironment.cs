static class WorkerEnvironment
{
    public sealed class Scrubbed(Dictionary<string, string> variables, Dictionary<string, string> removed)
    {
        public Dictionary<string, string> Variables { get; } = variables;
        public List<string> Removed { get; } = removed.Keys.Order(StringComparer.Ordinal).ToList();
        public Dictionary<string, string> Passthrough(IEnumerable<string> names) => removed
            .Where(p => names.Contains(p.Key, StringComparer.OrdinalIgnoreCase))
            .ToDictionary(p => p.Key, p => p.Value, removed.Comparer);

        public Scrubbed Restrict(IEnumerable<string> patterns)
        {
            var next = Scrub(Variables, patterns);
            var allRemoved = new Dictionary<string, string>(removed, removed.Comparer);
            foreach (var name in next.Removed) allRemoved[name] = Variables[name];
            return new(next.Variables, allRemoved);
        }
    }

    // Windows names variables case-insensitively (its PATH is spelled "Path"); every copy keeps that.
    public static StringComparer NameComparer => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    public static Dictionary<string, string> Copy(IEnumerable<KeyValuePair<string, string>> variables) =>
        variables.ToDictionary(x => x.Key, x => x.Value, NameComparer);

    public static Dictionary<string, string> Snapshot() => System.Environment.GetEnvironmentVariables()
        .Cast<System.Collections.DictionaryEntry>().ToDictionary(e => (string)e.Key, e => e.Value as string ?? "", NameComparer);

    public static Scrubbed Scrub(IReadOnlyDictionary<string, string> snapshot, IEnumerable<string>? patterns = null)
    {
        var secret = new Regex(@"(TOKEN|SECRET|PASSWORD|PASSWD|API_?KEY|ACCESS_?KEY|PRIVATE_?KEY|CREDENTIAL|(^|_)AUTH($|_)|(^|_)PAT$|^GH_|^GITHUB_|^NUGET_|^OPENCODE_SERVER|_PWD$|(^|_)PASS$|PASSPHRASE|(^|_)KEY$|(^|_)DSN$|DATABASE_URL|CONNECTION_?STRING|^ConnectionStrings__|COOKIE|^KUBECONFIG$|^NETRC$|^BASH_ENV$|^ENV$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var valuePattern = new Regex(@"[A-Za-z][A-Za-z0-9+.-]*://[^/\s:@]+:[^/\s@]+@");
        var extras = (patterns ?? []).Select(p => new Regex(p, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)).ToArray();
        var proxies = new HashSet<string>(["HTTP_PROXY", "HTTPS_PROXY", "ALL_PROXY", "NO_PROXY"], StringComparer.OrdinalIgnoreCase);
        var variables = new Dictionary<string, string>(NameComparer);
        var removed = new Dictionary<string, string>(NameComparer);
        foreach (var (name, value) in snapshot)
        {
            if (secret.IsMatch(name) || extras.Any(p => p.IsMatch(name)) || (!proxies.Contains(name) && valuePattern.IsMatch(value))) removed[name] = value;
            else variables[name] = value;
        }
        return new(variables, removed);
    }

    // The session's shell environment without credentials, with a per-task TEMP.
    public static (Dictionary<string, string> Variables, List<string> Removed) Environment(string task, string? run = null, string? tempRoot = null)
    {
        var scrubbed = Scrub(Snapshot());
        var variables = scrubbed.Variables;
        var removed = scrubbed.Removed;
        var temp = Locations.TaskTemp(task, run, tempRoot);
        Directory.CreateDirectory(temp);
        variables["TEMP"] = temp;
        variables["TMP"] = temp;
        variables["TMPDIR"] = temp;
        // A reused MSBuild server keeps the environment of whoever started it, so the per-task
        // temporary folder would not apply to its NuGet scratch work.
        variables["DOTNET_CLI_USE_MSBUILD_SERVER"] = "0";
        // A worker that points DOTNET_CLI_HOME at a new folder would otherwise make the SDK's first
        // run append that folder's tools path to the user's PATH in the Windows registry and
        // install an HTTPS development certificate in the user's certificate store.
        variables["DOTNET_ADD_GLOBAL_TOOLS_TO_PATH"] = "false";
        variables["DOTNET_GENERATE_ASPNET_CERTIFICATE"] = "false";
        variables["GIT_TERMINAL_PROMPT"] = "0";
        removed.Sort(StringComparer.Ordinal);
        return (variables, removed);
    }
}
