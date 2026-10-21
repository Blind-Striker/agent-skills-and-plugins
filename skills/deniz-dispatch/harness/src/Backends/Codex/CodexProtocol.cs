record CodexNotification(string Method, JsonElement Params);
record CodexServerRequest(JsonElement Id, string Method, JsonElement Params);
record CodexRpcError(int Code, string Message, JsonElement? Data = null);
record CodexReply(JsonElement? Result, CodexRpcError? Error = null)
{
    public static CodexReply Success<T>(T value, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo) =>
        new(JsonSerializer.SerializeToElement(value, typeInfo));
}

sealed class CodexRpcException(int code, string message, JsonElement? errorData) : Exception(message)
{
    public int Code { get; } = code;
    public JsonElement? ErrorData { get; } = errorData;
}

record CodexClientInfo(string Name, string Version);
record CodexClientCapabilities(bool ExperimentalApi, string[] OptOutNotificationMethods);
record CodexInitializeParams(CodexClientInfo ClientInfo, CodexClientCapabilities Capabilities);
record CodexInitializeResponse(string CodexHome, string PlatformOs, string PlatformFamily, string UserAgent);
record CodexGetAccountParams
{
    public bool RefreshToken => false;
}
record CodexGetAccountResponse(JsonElement? Account, bool RequiresOpenaiAuth);
record CodexModelListParams(string? Cursor = null)
{
    public bool IncludeHidden => true;
}
record CodexModelListResponse(CodexModel[] Data, string? NextCursor);
record CodexModel(string Id, string Model, CodexReasoningEffort[] SupportedReasoningEfforts, string DefaultReasoningEffort);
record CodexReasoningEffort(string ReasoningEffort);
record CodexWindowsSandboxReadinessResponse(string Status);
record CodexThreadStartParams(string Model, string Cwd, string Sandbox, JsonObject Config)
{
    public string ApprovalPolicy => "on-request";
    public string ApprovalsReviewer => "user";
    public bool Ephemeral => true;
}
record CodexThreadStartResponse(CodexThread Thread, string Model, string ModelProvider, JsonObject Sandbox, string ApprovalPolicy, string Cwd);
record CodexThread(string Id, string CliVersion, string? ParentThreadId, string? AgentRole);
record CodexTextInput(string Text)
{
    public string Type => "text";
}
record CodexTurnStartParams(string ThreadId, CodexTextInput[] Input, string Cwd, JsonObject SandboxPolicy, string Effort, JsonObject? OutputSchema = null)
{
    public string ApprovalPolicy => "on-request";
}
record CodexTurnStartResponse(CodexTurn Turn);
record CodexTurnInterruptParams(string ThreadId, string TurnId);
record CodexTurnInterruptResponse;
record CodexTurnSteerParams(string ThreadId, string ExpectedTurnId, CodexTextInput[] Input);
record CodexTurn(string Id, string Status, CodexTurnError? Error);
record CodexTurnError(string Message, JsonElement? CodexErrorInfo, string? AdditionalDetails);
record CodexTurnStartedNotification(string ThreadId, CodexTurn Turn);
record CodexTurnCompletedNotification(string ThreadId, CodexTurn Turn);
record CodexItemStartedNotification(string ThreadId, string TurnId, CodexItem Item, long StartedAtMs);
record CodexItemCompletedNotification(string ThreadId, string TurnId, CodexItem Item, long CompletedAtMs);
record CodexItem(string Id, string Type, string? Text, string? Phase, string? Command, string? Cwd, JsonElement? Changes);
record CodexThreadTokenUsageUpdatedNotification(string ThreadId, string TurnId, CodexTokenUsage TokenUsage);
record CodexTokenUsage(CodexTokenUsageBreakdown Total, CodexTokenUsageBreakdown Last, long? ModelContextWindow);
record CodexTokenUsageBreakdown(long TotalTokens, long InputTokens, long CachedInputTokens, long OutputTokens, long ReasoningOutputTokens, long CacheWriteInputTokens);
record CodexErrorNotification(string ThreadId, string TurnId, bool WillRetry, CodexTurnError Error);
record CodexModelReroutedNotification(string ThreadId, string TurnId, string FromModel, string ToModel, string Reason);
record CodexServerRequestResolvedNotification(string ThreadId, JsonElement RequestId);
record CodexWarningNotification(string Message, string? ThreadId);
record CodexConfigWarningNotification(string Summary, string? Details, string? Path, JsonElement? Range);

record CodexCommandExecutionRequestApprovalParams(string ThreadId, string TurnId, string ItemId, long StartedAtMs,
    string? Command, string? Cwd, string? Kind, string? Reason, string? ApprovalId);
record CodexFileChangeRequestApprovalParams(string ThreadId, string TurnId, string ItemId, long StartedAtMs, string? GrantRoot, string? Reason);
record CodexPermissionsRequestApprovalParams(string ThreadId, string TurnId, string ItemId, long StartedAtMs, string Cwd, JsonObject Permissions, string? Reason);

[JsonConverter(typeof(JsonStringEnumConverter<CodexApprovalDecision>))]
enum CodexApprovalDecision
{
    [JsonStringEnumMemberName("accept")] Accept,
    [JsonStringEnumMemberName("decline")] Decline,
}
record CodexCommandExecutionRequestApprovalResponse(CodexApprovalDecision Decision);
record CodexFileChangeRequestApprovalResponse(CodexApprovalDecision Decision);
record CodexPermissionsRequestApprovalResponse(JsonObject Permissions)
{
    public string Scope => "turn";
}
record CodexToolRequestUserInputParams(string ThreadId, string TurnId, string ItemId, bool IsBlocking, CodexQuestion[] Questions);
record CodexQuestion(string Id, string Header, string Question, bool IsOther, bool IsSecret, CodexQuestionOption[]? Options);
record CodexQuestionOption(string Label, string Description);
record CodexToolRequestUserInputResponse(Dictionary<string, CodexUserInputAnswer> Answers);
record CodexUserInputAnswer(string[] Answers);
record CodexMcpServerElicitationRequestParams(string ThreadId, string? TurnId, string ServerName, string Message, JsonElement? RequestedSchema, string? Url);
record CodexMcpServerElicitationRequestResponse
{
    public string Action => "decline";
}
record CodexLegacyApprovalResponse(CodexDeniedDecision Decision);
record CodexDeniedDecision(CodexRejection Denied);
record CodexRejection(string Rejection);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip)]
[JsonSerializable(typeof(JsonElement))]
[JsonSerializable(typeof(CodexRpcError))]
[JsonSerializable(typeof(CodexInitializeParams))]
[JsonSerializable(typeof(CodexInitializeResponse))]
[JsonSerializable(typeof(CodexGetAccountParams))]
[JsonSerializable(typeof(CodexGetAccountResponse))]
[JsonSerializable(typeof(CodexModelListParams))]
[JsonSerializable(typeof(CodexModelListResponse))]
[JsonSerializable(typeof(CodexWindowsSandboxReadinessResponse))]
[JsonSerializable(typeof(CodexThreadStartParams))]
[JsonSerializable(typeof(CodexThreadStartResponse))]
[JsonSerializable(typeof(CodexTurnStartParams))]
[JsonSerializable(typeof(CodexTurnStartResponse))]
[JsonSerializable(typeof(CodexTurnInterruptParams))]
[JsonSerializable(typeof(CodexTurnInterruptResponse))]
[JsonSerializable(typeof(CodexTurnSteerParams))]
[JsonSerializable(typeof(CodexTurnStartedNotification))]
[JsonSerializable(typeof(CodexTurnCompletedNotification))]
[JsonSerializable(typeof(CodexItemStartedNotification))]
[JsonSerializable(typeof(CodexItemCompletedNotification))]
[JsonSerializable(typeof(CodexThreadTokenUsageUpdatedNotification))]
[JsonSerializable(typeof(CodexErrorNotification))]
[JsonSerializable(typeof(CodexModelReroutedNotification))]
[JsonSerializable(typeof(CodexServerRequestResolvedNotification))]
[JsonSerializable(typeof(CodexWarningNotification))]
[JsonSerializable(typeof(CodexConfigWarningNotification))]
[JsonSerializable(typeof(CodexCommandExecutionRequestApprovalParams))]
[JsonSerializable(typeof(CodexFileChangeRequestApprovalParams))]
[JsonSerializable(typeof(CodexPermissionsRequestApprovalParams))]
[JsonSerializable(typeof(CodexCommandExecutionRequestApprovalResponse))]
[JsonSerializable(typeof(CodexFileChangeRequestApprovalResponse))]
[JsonSerializable(typeof(CodexPermissionsRequestApprovalResponse))]
[JsonSerializable(typeof(CodexToolRequestUserInputParams))]
[JsonSerializable(typeof(CodexToolRequestUserInputResponse))]
[JsonSerializable(typeof(CodexMcpServerElicitationRequestParams))]
[JsonSerializable(typeof(CodexMcpServerElicitationRequestResponse))]
[JsonSerializable(typeof(CodexLegacyApprovalResponse))]
partial class CodexJson : JsonSerializerContext
{
    public static CodexJson Wire { get; } = new(Json.Options(strict: false, indented: false));
}
