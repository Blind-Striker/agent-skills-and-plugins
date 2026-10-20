using System.Text.Encodings.Web;
using System.Text.Json;

namespace BadgeKit;

/// <summary>The serializer the service has always used.</summary>
public static class OldBadgeSerializer
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static string Serialize(BadgeRequest request) => JsonSerializer.Serialize(request, Options);
}

/// <summary>The replacement serializer; it must produce the same bytes as <see cref="OldBadgeSerializer"/>.</summary>
public static class NewBadgeSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string Serialize(BadgeRequest request) => JsonSerializer.Serialize(request, Options);

    public static BadgeRequest Deserialize(string json) =>
        JsonSerializer.Deserialize<BadgeRequest>(json, Options) ?? throw new JsonException("empty request");
}
