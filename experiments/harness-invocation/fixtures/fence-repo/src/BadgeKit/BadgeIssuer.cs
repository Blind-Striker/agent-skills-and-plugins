using Microsoft.Extensions.Logging;

namespace BadgeKit;

/// <summary>Issues badges for valid requests and logs each decision.</summary>
public sealed class BadgeIssuer(ILogger<BadgeIssuer> logger)
{
    public bool TryIssue(BadgeRequest request, out string label)
    {
        if (!KeyValidator.IsValidSpan(request.Id))
        {
            logger.LogWarning("Rejected badge request {Id}", request.Id);
            label = "";
            return false;
        }

        label = $"{request.Owner}: {request.Label}";
        logger.LogInformation("Issued badge {Id}", request.Id);
        return true;
    }
}
