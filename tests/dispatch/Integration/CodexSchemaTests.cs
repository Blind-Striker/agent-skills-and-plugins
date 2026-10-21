using Dispatch.Tests.Support;

namespace Dispatch.Tests.Integration;

[Category("Integration")]
public sealed class CodexSchemaTests
{
    static readonly string[] SchemaFiles =
    [
        "v1/InitializeParams.json", "v1/InitializeResponse.json", "ClientNotification.json",
        "JSONRPCMessage.json", "RequestId.json", "ServerRequest.json",
        "CommandExecutionRequestApprovalParams.json", "CommandExecutionRequestApprovalResponse.json",
        "FileChangeRequestApprovalParams.json", "FileChangeRequestApprovalResponse.json",
        "PermissionsRequestApprovalParams.json", "PermissionsRequestApprovalResponse.json",
        "ToolRequestUserInputParams.json", "ToolRequestUserInputResponse.json",
        "McpServerElicitationRequestResponse.json", "ExecCommandApprovalResponse.json", "ApplyPatchApprovalResponse.json",
        "v2/GetAccountParams.json", "v2/GetAccountResponse.json", "v2/ModelListParams.json", "v2/ModelListResponse.json",
        "v2/WindowsSandboxReadinessResponse.json", "v2/ThreadStartParams.json", "v2/ThreadStartResponse.json",
        "v2/TurnStartParams.json", "v2/TurnStartResponse.json", "v2/TurnInterruptParams.json", "v2/TurnInterruptResponse.json",
        "v2/TurnSteerParams.json", "v2/TurnStartedNotification.json", "v2/TurnCompletedNotification.json",
        "v2/ItemStartedNotification.json", "v2/ItemCompletedNotification.json", "v2/ThreadTokenUsageUpdatedNotification.json",
        "v2/ErrorNotification.json", "v2/ModelReroutedNotification.json", "v2/ServerRequestResolvedNotification.json",
        "v2/WarningNotification.json", "v2/ConfigWarningNotification.json",
    ];

    [Test, Timeout(120_000)]
    public async Task P2_Generated_schema_matches_the_pinned_snapshot(CancellationToken token)
    {
        if (Environment.GetEnvironmentVariable("DISPATCH_CONTRACT") != "1")
            Skip.Test("set DISPATCH_CONTRACT=1 and DISPATCH_CONTRACT_CODEX to the pinned CLI to check schema drift");
        var executable = Environment.GetEnvironmentVariable("DISPATCH_CONTRACT_CODEX");
        await Assert.That(string.IsNullOrWhiteSpace(executable)).IsFalse()
            .Because("DISPATCH_CONTRACT=1 requires DISPATCH_CONTRACT_CODEX; no PATH fallback is allowed");
        await Assert.That(Path.IsPathFullyQualified(executable!)).IsTrue()
            .Because("DISPATCH_CONTRACT_CODEX must be the installed CLI path");
        await Assert.That(File.Exists(executable)).IsTrue().Because($"contract CLI does not exist: {executable}");

        using var tree = new TempTree();
        var temporary = Directory.CreateTempSubdirectory("dispatch-schema-");
        try
        {
            var home = Directory.CreateDirectory(Path.Combine(temporary.FullName, "codex-home")).FullName;
            var generated = Path.Combine(temporary.FullName, "schema");
            var environment = tree.ChildEnvironment().Where(pair => pair.Value is not null)
                .ToDictionary(pair => pair.Key, pair => pair.Value!);
            environment["CODEX_HOME"] = home;
            foreach (var name in new[] { "TEMP", "TMP", "TMPDIR" }) environment[name] = temporary.FullName;
            var result = await Proc.RunAsync(executable!, ["app-server", "generate-json-schema", "--out", generated],
                temporary.FullName, token, TimeSpan.FromSeconds(90), environment: environment);
            Console.WriteLine($"Schema generation: exit {result.ExitCode}\n{result.Output}{result.Stderr}");
            await Assert.That(result.ExitCode).IsEqualTo(0).Because("the signed-out CLI must generate its schema");

            var snapshot = Path.Combine(TestPaths.Tests, "fixtures", "codex-schema", CodexBackend.PinnedVersion);
            var differences = new List<string>();
            foreach (var file in SchemaFiles)
            {
                var expected = Path.Combine(snapshot, file);
                var actual = Path.Combine(generated, file);
                try
                {
                    if (!File.Exists(expected)) differences.Add($"{file}: missing snapshot");
                    else if (!File.Exists(actual)) differences.Add($"{file}: missing generated file");
                    else if (!JsonNode.DeepEquals(Normalize(JsonNode.Parse(await File.ReadAllTextAsync(expected, token))),
                        Normalize(JsonNode.Parse(await File.ReadAllTextAsync(actual, token)))))
                        differences.Add($"{file}: schema differs");
                }
                catch (JsonException error)
                {
                    differences.Add($"{file}: invalid schema JSON: {error.Message}");
                }
            }
            CheckMethods("ClientRequest.json", ["initialize", "account/read", "model/list", "windowsSandbox/readiness",
                "thread/start", "turn/start", "turn/interrupt", "turn/steer"]);
            CheckMethods("ServerRequest.json", ["item/commandExecution/requestApproval", "item/fileChange/requestApproval",
                "item/permissions/requestApproval", "item/tool/requestUserInput", "item/tool/call", "account/chatgptAuthTokens/refresh",
                "mcpServer/elicitation/request", "execCommandApproval", "applyPatchApproval", "attestation/generate"]);
            CheckMethods("ServerNotification.json", ["turn/started", "turn/completed", "item/started", "item/completed",
                "thread/tokenUsage/updated", "error", "model/rerouted", "serverRequest/resolved", "warning", "configWarning",
                "windows/worldWritableWarning", "item/agentMessage/delta", "item/plan/delta", "item/reasoning/summaryTextDelta",
                "item/reasoning/summaryPartAdded", "item/reasoning/textDelta", "item/commandExecution/outputDelta",
                "item/fileChange/outputDelta", "item/fileChange/patchUpdated", "turn/diff/updated", "command/exec/outputDelta",
                "process/outputDelta"]);
            Console.WriteLine($"Compared {SchemaFiles.Length} snapshot files; {differences.Count} differences.\n{string.Join('\n', differences)}");
            await Assert.That(differences).IsEmpty().Because(string.Join('\n', differences));

            void CheckMethods(string file, string[] required)
            {
                var path = Path.Combine(generated, file);
                if (!File.Exists(path)) { differences.Add($"{file}: missing generated envelope"); return; }
                var schema = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
                var methods = schema["oneOf"]!.AsArray()
                    .SelectMany(branch => branch!["properties"]!["method"]!["enum"]!.AsArray())
                    .Select(method => method!.GetValue<string>()).ToHashSet(StringComparer.Ordinal);
                foreach (var method in required)
                    if (!methods.Contains(method)) differences.Add($"{file}: missing method {method}");
            }
        }
        finally
        {
            temporary.Delete(recursive: true);
        }
    }

    static JsonNode? Normalize(JsonNode? node) => node switch
    {
        JsonObject value => new JsonObject(value.Where(pair => pair.Key != "description")
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new KeyValuePair<string, JsonNode?>(pair.Key, Normalize(pair.Value)))),
        JsonArray value => new JsonArray(value.Select(Normalize).ToArray()),
        _ => node?.DeepClone(),
    };
}
