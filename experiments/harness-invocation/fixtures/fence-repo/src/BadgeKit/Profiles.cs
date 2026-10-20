namespace BadgeKit;

/// <summary>The label style a deployment uses, chosen by the BADGEKIT_PROFILE environment variable.</summary>
public enum BadgeProfile
{
    Legacy,
    Current,
}

public static class Profiles
{
    /// <summary>"current" (any case) selects <see cref="BadgeProfile.Current"/>; anything else is legacy.</summary>
    public static BadgeProfile FromEnvironment() =>
        string.Equals(Environment.GetEnvironmentVariable("BADGEKIT_PROFILE"), "current", StringComparison.OrdinalIgnoreCase)
            ? BadgeProfile.Current
            : BadgeProfile.Legacy;
}
