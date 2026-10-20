namespace BadgeKit;

/// <summary>
/// A badge key is "owner-id": a lowercase ASCII owner of 1-16 letters, a dash, and a 1-6 digit id.
/// Both methods implement the same rule; the service calls one of them on every request.
/// </summary>
public static class KeyValidator
{
    public static bool IsValidSplit(string key)
    {
        var parts = key.Split('-');
        return parts.Length == 2
            && parts[0].Length is >= 1 and <= 16 && parts[0].All(c => c is >= 'a' and <= 'z')
            && parts[1].Length is >= 1 and <= 6 && parts[1].All(char.IsAsciiDigit);
    }

    public static bool IsValidSpan(string key)
    {
        var span = key.AsSpan();
        var dash = span.IndexOf('-');
        if (dash is < 1 or > 16 || span[(dash + 1)..].Contains('-'))
        {
            return false;
        }

        var owner = span[..dash];
        var id = span[(dash + 1)..];
        if (id.Length is < 1 or > 6)
        {
            return false;
        }

        foreach (var c in owner)
        {
            if (c is < 'a' or > 'z')
            {
                return false;
            }
        }

        foreach (var c in id)
        {
            if (!char.IsAsciiDigit(c))
            {
                return false;
            }
        }

        return true;
    }
}
