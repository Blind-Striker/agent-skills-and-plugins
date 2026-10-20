namespace BadgeKit;

/// <summary>A request to issue one badge.</summary>
public sealed record BadgeRequest(string Id, string Owner, string Label, IReadOnlyList<string> Tags);
