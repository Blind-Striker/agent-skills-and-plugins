using System.Text.Json.Nodes;

namespace Dispatch.Tests.Support;

internal static class LedgerSnapshot
{
    // Keep every event, field and its order. Only unstable identity/path/time data is replaced.
    // The OS/runtime-dependent environment count is checked against the wire before normalization.
    public static string Normalize(JsonObject[] events, string root, string run, string baseSha)
    {
        foreach (var entry in events)
        {
            entry["ts"] = "$timestamp";
            if (entry["type"]!.GetValue<string>() == "environment") entry["data"]!["variables"] = "$environment-count";
            // Prompt text includes the temporary workspace path; its measurements are checked on the wire.
            if (entry["type"]!.GetValue<string>() == "prompt")
            {
                entry["data"]!["chars"] = "$prompt-chars";
                entry["data"]!["sha256"] = "$prompt-sha256";
            }
            ReplaceStrings(entry);
        }
        return string.Join('\n', events.Select(entry => entry.ToJsonString())) + "\n";

        void ReplaceStrings(JsonNode node)
        {
            if (node is JsonObject obj)
            {
                foreach (var (key, value) in obj.ToArray())
                {
                    if (value is JsonValue scalar && scalar.TryGetValue<string>(out var text)) obj[key] = NormalizeText(text);
                    else if (value is not null) ReplaceStrings(value);
                }
            }
            else if (node is JsonArray array)
            {
                for (var i = 0; i < array.Count; i++)
                {
                    if (array[i] is JsonValue scalar && scalar.TryGetValue<string>(out var text)) array[i] = NormalizeText(text);
                    else if (array[i] is { } child) ReplaceStrings(child);
                }
            }
        }

        string NormalizeText(string text) => text.Replace(root, "$lab", StringComparison.Ordinal)
            .Replace(run, "$run", StringComparison.Ordinal).Replace(baseSha, "$base", StringComparison.Ordinal)
            .Replace("ses_fixture", "$session", StringComparison.Ordinal).Replace("proj_fixture", "$project", StringComparison.Ordinal)
            .Replace("msg_prompt", "$prompt", StringComparison.Ordinal).Replace("per_fixture", "$permission", StringComparison.Ordinal)
            .Replace('\\', '/');
    }
}
