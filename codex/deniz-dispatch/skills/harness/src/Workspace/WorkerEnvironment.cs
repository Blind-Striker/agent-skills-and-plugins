static class WorkerEnvironment
{
    // The session's shell environment without credentials, with a per-task TEMP.
    public static (Dictionary<string, string> Variables, List<string> Removed) Environment(string task)
    {
        var secret = new Regex(@"(TOKEN|SECRET|PASSWORD|PASSWD|API_?KEY|ACCESS_?KEY|PRIVATE_?KEY|CREDENTIAL|(^|_)AUTH($|_)|(^|_)PAT$|^GH_|^GITHUB_|^NUGET_|^OPENCODE_SERVER)", RegexOptions.IgnoreCase);
        var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var removed = new List<string>();
        foreach (System.Collections.DictionaryEntry entry in System.Environment.GetEnvironmentVariables())
        {
            var name = (string)entry.Key;
            if (secret.IsMatch(name)) removed.Add(name);
            else variables[name] = entry.Value as string ?? "";
        }
        var temp = Locations.TaskTemp(task);
        Directory.CreateDirectory(temp);
        variables["TEMP"] = temp;
        variables["TMP"] = temp;
        variables["TMPDIR"] = temp;
        // A reused MSBuild server keeps the environment of whoever started it, so the per-task
        // temporary folder would not apply to its NuGet scratch work.
        variables["DOTNET_CLI_USE_MSBUILD_SERVER"] = "0";
        removed.Sort(StringComparer.Ordinal);
        return (variables, removed);
    }
}
