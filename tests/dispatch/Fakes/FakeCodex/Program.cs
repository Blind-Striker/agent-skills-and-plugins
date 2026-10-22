using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        Wire? wire = null;
        try
        {
            // pid_t setsid(void): no pointers or owned native memory. A wrapper child is not a group leader.
            if (args is ["--child", _, _, _, "setsid"] && !OperatingSystem.IsWindows() && setsid() < 0)
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "setsid failed");
            var home = Path.GetFullPath(RequiredEnvironment("CODEX_HOME"));
            var scenarios = Path.GetFullPath(RequiredEnvironment("FAKE_CODEX_SCENARIOS"));
            var record = Path.GetFullPath(RequiredEnvironment("FAKE_CODEX_RECORD"));
            var child = args is ["--child", _, _, _, _];
            string scenario;
            string output;
            var offset = 0;
            if (child)
            {
                scenario = args[1];
                offset = int.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture);
                output = args[3];
            }
            else
            {
                var command = args switch
                {
                    ["--version"] => "version",
                    ["login", "status"] => "login-status",
                    ["app-server", ..] => "app-server",
                    _ => throw new InvalidDataException("expected --version, login status, or app-server [--role preflight|probe|task|task-<id>]"),
                };
                var order = Claim(record);
                scenario = command == "app-server" ? SelectScenario(args, home, scenarios, record) : command;
                output = Path.Combine(record, $"{order:D6}-{Path.GetFileName(scenario)}");
            }
            Directory.CreateDirectory(output);
            wire = new Wire(output);
            using var self = Process.GetCurrentProcess();
            var start = new JsonObject
            {
                ["pid"] = Environment.ProcessId,
                ["startTime"] = self.StartTime.ToUniversalTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture),
                ["argv"] = new JsonArray(args.Select(argument => (JsonNode?)JsonValue.Create(argument)).ToArray()),
                ["cwd"] = Environment.CurrentDirectory,
                ["environmentNames"] = new JsonArray(Environment.GetEnvironmentVariables().Keys.Cast<string>()
                    .Order(StringComparer.Ordinal).Select(name => (JsonNode?)JsonValue.Create(name)).ToArray()),
                ["codexHome"] = home,
                ["codexHomeEntries"] = new JsonArray(Directory.Exists(home) ? Directory.EnumerateFileSystemEntries(home)
                    .Select(path => (JsonNode?)JsonValue.Create(Path.GetFileName(path))).ToArray() : []),
                ["scenario"] = Path.GetFileName(scenario),
            };
            File.WriteAllText(Path.Combine(output, "start.json"), start.ToJsonString() + "\n", new UTF8Encoding(false));

            if (scenario == "version")
            {
                await Console.OpenStandardOutput().WriteAsync(await File.ReadAllBytesAsync(Path.Combine(scenarios, "version.txt")));
                return 0;
            }
            if (scenario == "login-status")
            {
                // Only fixture text is used: never open or report credentials.
                var status = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(scenarios, "login-status.json")))!.AsObject();
                await Console.OpenStandardOutput().WriteAsync(Encoding.UTF8.GetBytes(status["stdout"]?.GetValue<string>() ?? ""));
                await Console.OpenStandardError().WriteAsync(Encoding.UTF8.GetBytes(status["stderr"]?.GetValue<string>() ?? ""));
                return status["exit"]!.GetValue<int>();
            }
            var path = Path.Combine(scenarios, scenario);
            if (!File.Exists(path)) throw new InvalidDataException($"missing scenario: {scenario}");
            if (!child && Environment.GetEnvironmentVariable("FAKE_CODEX_AUTH_MARK") is { } marker)
            {
                // Truncation preserves a test-created hardlink; replacing the name would not.
                using var auth = new FileStream(Path.Combine(home, "auth.json"), FileMode.Truncate, FileAccess.Write, FileShare.ReadWrite);
                auth.Write(Encoding.UTF8.GetBytes(marker));
            }
            var steps = File.ReadLines(path).Where(line => !string.IsNullOrWhiteSpace(line))
                .Select(line => JsonNode.Parse(line)!.AsObject()).ToArray();
            return await new Replay(steps, wire, home, path).RunAsync(offset);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or InvalidOperationException or System.Text.Json.JsonException or FormatException or DecoderFallbackException or Win32Exception)
        {
            wire?.Unexpected(exception.Message);
            await Console.Error.WriteLineAsync(exception.Message);
            return 97;
        }
    }

    static string SelectScenario(string[] args, string home, string scenarios, string record)
    {
        if (args is ["app-server", "--role", var role])
        {
            if (role is "preflight" or "probe" or "task") return role + ".jsonl";
            if (role.StartsWith("task-", StringComparison.Ordinal) && role.All(character => char.IsAsciiLetterOrDigit(character) || character == '-'))
                return role + ".jsonl";
            throw new InvalidDataException("unknown scenario role");
        }
        if (args is not ["app-server"]) throw new InvalidDataException("unexpected app-server arguments");
        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(home));
        if (name == "probe-codex") return "probe.jsonl";
        if (name.StartsWith("codex-", StringComparison.Ordinal)) return TaskScenario(name[6..]);
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(OperatingSystem.IsWindows() ? home.ToUpperInvariant() : home)));
        var order = Claim(Path.Combine(record, "homes", key));
        return order == 1 ? "preflight.jsonl" : TaskScenario((order - 1).ToString(System.Globalization.CultureInfo.InvariantCulture));

        string TaskScenario(string id) => File.Exists(Path.Combine(scenarios, $"task-{id}.jsonl")) ? $"task-{id}.jsonl" : "task.jsonl";
    }

    // Exclusive creation assigns unique tickets even when multiple app-servers start concurrently.
    static int Claim(string directory)
    {
        Directory.CreateDirectory(directory);
        for (var index = 1; ; index++)
        {
            var path = Path.Combine(directory, $"{index:D6}.claim");
            try
            {
                using var claim = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
                return index;
            }
            catch (IOException) when (File.Exists(path))
            {
                // Another process owns this ticket; try the next one.
                continue;
            }
        }
    }

    static string RequiredEnvironment(string name) => Environment.GetEnvironmentVariable(name)
        ?? throw new InvalidDataException($"{name} is required");

    [DllImport("libc", ExactSpelling = true, SetLastError = true, CallingConvention = CallingConvention.Cdecl)]
    static extern int setsid();
}
