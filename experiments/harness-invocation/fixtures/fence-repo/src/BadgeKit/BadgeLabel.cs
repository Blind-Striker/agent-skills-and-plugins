using System.Globalization;

namespace BadgeKit;

public static class BadgeLabel
{
    /// <summary>
    /// Formats a badge count. Legacy: "1500 badges". Current: below 1000 the bare number ("42"); from
    /// 1000 on, thousands with at most one decimal and a "k" suffix ("1.5k", "2k", "12.3k").
    /// </summary>
    public static string Format(int count, BadgeProfile profile) => profile switch
    {
        BadgeProfile.Current when count >= 1000 => $"{count / 1000}k",
        BadgeProfile.Current => count.ToString(CultureInfo.InvariantCulture),
        _ => $"{count} badges",
    };
}
