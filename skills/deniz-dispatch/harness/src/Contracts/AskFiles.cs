sealed class PendingAsk : VersionedContract
{
    public string Run { get; set; } = "";
    public string Task { get; set; } = "";
    public string Backend { get; set; } = "";
    public string Session { get; set; } = "";
    public string RootSession { get; set; } = "";
    public string AskId { get; set; } = "";
    public JsonObject? BackendRef { get; set; }
    public string Action { get; set; } = "";
    public List<string> Resources { get; set; } = [];
    public string Rule { get; set; } = "";
    public string Time { get; set; } = "";
    public List<string> Reply { get; set; } = [];
}

sealed class AskDecision : VersionedContract, IJsonOnDeserialized
{
    [JsonRequired]
    public string AskId { get; set; } = "";
    [JsonRequired]
    public string Decision { get; set; } = "";
    public string? Message { get; set; }
    [JsonRequired]
    public string Time { get; set; } = "";
    public string By { get; set; } = "operator";

    void IJsonOnDeserialized.OnDeserialized()
    {
        Json.CheckVersion(SchemaVersion);
        if (string.IsNullOrEmpty(AskId) || !Regex.IsMatch(AskId, "\\A[a-z0-9][a-z0-9-]*\\z")) throw new JsonException("askId must be a safe ask identifier");
        if (Decision is not ("once" or "reject")) throw new JsonException("decision must be once or reject");
        if (Decision == "once" && Message is not null) throw new JsonException("message goes with reject only");
    }
}

sealed class CancelRequest : VersionedContract
{
    [JsonRequired]
    public string Run { get; set; } = "";
    [JsonRequired]
    public string Task { get; set; } = "";
    [JsonRequired]
    public string Time { get; set; } = "";
}

sealed class RunReference : VersionedContract
{
    [JsonRequired]
    public string Run { get; set; } = "";
    [JsonRequired]
    public string LedgerRoot { get; set; } = "";
}
