// One escalated ask, as the operator's file monitor sees it under <wt-root>/.dispatch/<run>/asks/.
sealed class PendingAsk
{
    public string Run { get; set; } = "";

    public string Task { get; set; } = "";

    public string Session { get; set; } = "";

    public string RootSession { get; set; } = "";

    public string AskId { get; set; } = "";

    public string Action { get; set; } = "";

    public List<string> Resources { get; set; } = [];

    public string Rule { get; set; } = "";

    public string Time { get; set; } = "";

    public string Reply { get; set; } = "";
}

sealed class AskDecision
{
    public string AskId { get; set; } = "";

    public string Decision { get; set; } = "";

    public string? Message { get; set; }

    public string Time { get; set; } = "";
}
