using Dispatch.Tests.Support;
using LateApexEarlySpeed.Json.Schema;

namespace Dispatch.Tests.Unit;

[Category("Unit")]
public sealed class CodexProtocolTests
{
    [Test, Timeout(30_000)]
    [Arguments("initialize")]
    [Arguments("initialized")]
    [Arguments("account/read")]
    [Arguments("model/list")]
    [Arguments("windowsSandbox/readiness")]
    [Arguments("thread/start")]
    [Arguments("turn/start/readOnly")]
    [Arguments("turn/start/workspaceWrite")]
    [Arguments("turn/start/dangerFullAccess")]
    [Arguments("turn/interrupt")]
    [Arguments("turn/steer")]
    [Arguments("command/accept")]
    [Arguments("command/decline")]
    [Arguments("file/accept")]
    [Arguments("file/decline")]
    [Arguments("permissions/once")]
    [Arguments("permissions/reject")]
    [Arguments("userInput")]
    [Arguments("elicitation")]
    [Arguments("execCommandApproval")]
    [Arguments("applyPatchApproval")]
    [Arguments("rpcError")]
    public async Task P1_Outgoing_messages_conform_to_the_pinned_schema(string message, CancellationToken token)
    {
        var (schema, value) = Sample(message);
        string json;
        if (value is string)
        {
            await using var pipe = new CodexPipe();
            if (message == "initialized")
            {
                await pipe.Client.NotifyAsync("initialized", token);
                json = await pipe.ReadLineAsync(token);
            }
            else if (message == "windowsSandbox/readiness")
            {
                var pending = pipe.Client.RequestAsync(message, CodexRpcTests.Element("null"), CodexJson.Wire.JsonElement, cancellationToken: token);
                json = await pipe.ReadLineAsync(token);
                using var doc = JsonDocument.Parse(json);
                await pipe.SendAsync($"{{\"id\":{doc.RootElement.GetProperty("id")},\"result\":{{}}}}", token);
                await pending;
            }
            else
            {
                await pipe.SendAsync("{\"id\":\"request\",\"method\":\"unknown\",\"params\":{}}", token);
                json = await pipe.ReadLineAsync(token);
            }
        }
        else
        {
            var info = CodexJson.Wire.GetTypeInfo(value.GetType()) ?? throw new NotSupportedException($"missing protocol metadata: {value.GetType().Name}");
            json = JsonSerializer.Serialize(value, info);
        }
        var validator = new JsonValidator(File.ReadAllText(Path.Combine(TestPaths.Tests, "fixtures", "codex-schema", "0.163.0", schema)));
        var result = validator.Validate(json);
        await Assert.That(result.IsValid).IsTrue().Because($"{message}: {json}");
        await Assert.That(json).DoesNotContain("acceptForSession");
        await Assert.That(json).DoesNotContain("jsonrpc");
    }

    static (string Schema, object Value) Sample(string message)
    {
        var input = new[] { new CodexTextInput("Görevi tamamla.\nKeep the prompt unchanged.") };
        var permissions = new JsonObject { ["fileSystem"] = new JsonObject { ["write"] = new JsonArray("/workspace/output") }, ["network"] = new JsonObject { ["enabled"] = true } };
        return message switch
        {
            "initialize" => ("v1/InitializeParams.json", new CodexInitializeParams(new("dispatch", "1.0"), new(false, ["item/agentMessage/delta"]))),
            "initialized" => ("ClientNotification.json", ""),
            "account/read" => ("v2/GetAccountParams.json", new CodexGetAccountParams()),
            "model/list" => ("v2/ModelListParams.json", new CodexModelListParams("next-page")),
            "windowsSandbox/readiness" => ("JSONRPCMessage.json", ""),
            "thread/start" => ("v2/ThreadStartParams.json", new CodexThreadStartParams("model-x", "/workspace", "workspace-write", new JsonObject
            {
                ["projects"] = new JsonObject { ["/workspace/project.with.dot"] = new JsonObject { ["trust_level"] = "trusted" } },
            })),
            var m when m.StartsWith("turn/start/", StringComparison.Ordinal) => ("v2/TurnStartParams.json", new CodexTurnStartParams("thread", input, "/workspace",
                m[11..] == "dangerFullAccess" ? new JsonObject { ["type"] = "dangerFullAccess" }
                : m[11..] == "workspaceWrite" ? new JsonObject { ["type"] = "workspaceWrite", ["writableRoots"] = new JsonArray(), ["networkAccess"] = true }
                : new JsonObject { ["type"] = "readOnly", ["networkAccess"] = false }, "high", new JsonObject
                {
                    ["type"] = "object", ["properties"] = new JsonObject { ["text"] = new JsonObject { ["type"] = "string" } },
                    ["required"] = new JsonArray("text"), ["additionalProperties"] = false,
                })),
            "turn/interrupt" => ("v2/TurnInterruptParams.json", new CodexTurnInterruptParams("thread", "turn")),
            "turn/steer" => ("v2/TurnSteerParams.json", new CodexTurnSteerParams("child-thread", "child-turn", input)),
            "command/accept" => ("CommandExecutionRequestApprovalResponse.json", new CodexCommandExecutionRequestApprovalResponse(CodexApprovalDecision.Accept)),
            "command/decline" => ("CommandExecutionRequestApprovalResponse.json", new CodexCommandExecutionRequestApprovalResponse(CodexApprovalDecision.Decline)),
            "file/accept" => ("FileChangeRequestApprovalResponse.json", new CodexFileChangeRequestApprovalResponse(CodexApprovalDecision.Accept)),
            "file/decline" => ("FileChangeRequestApprovalResponse.json", new CodexFileChangeRequestApprovalResponse(CodexApprovalDecision.Decline)),
            "permissions/once" => ("PermissionsRequestApprovalResponse.json", new CodexPermissionsRequestApprovalResponse(permissions)),
            "permissions/reject" => ("PermissionsRequestApprovalResponse.json", new CodexPermissionsRequestApprovalResponse(new())),
            "userInput" => ("ToolRequestUserInputResponse.json", new CodexToolRequestUserInputResponse(new() { ["q1"] = new(["No human is available."]) })),
            "elicitation" => ("McpServerElicitationRequestResponse.json", new CodexMcpServerElicitationRequestResponse()),
            "execCommandApproval" => ("ExecCommandApprovalResponse.json", new CodexLegacyApprovalResponse(new(new("not permitted")))),
            "applyPatchApproval" => ("ApplyPatchApprovalResponse.json", new CodexLegacyApprovalResponse(new(new("not permitted")))),
            "rpcError" => ("JSONRPCMessage.json", ""),
            _ => throw new ArgumentOutOfRangeException(nameof(message)),
        };
    }
}
