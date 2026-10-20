using BadgeKit;

namespace BadgeKit.Tests;

public class LabelTests
{
    // Runs under whichever profile the environment selects, as the deployed service does.
    [Fact]
    public void Large_counts_follow_the_active_profile()
    {
        var profile = Profiles.FromEnvironment();
        var expected = profile == BadgeProfile.Current ? "1.5k" : "1500 badges";
        Assert.Equal(expected, BadgeLabel.Format(1500, profile));
    }

    [Fact]
    public void Small_counts_follow_the_active_profile()
    {
        var profile = Profiles.FromEnvironment();
        var expected = profile == BadgeProfile.Current ? "42" : "42 badges";
        Assert.Equal(expected, BadgeLabel.Format(42, profile));
    }

    [Theory]
    [InlineData(2000, "2k")]
    [InlineData(12000, "12k")]
    public void Current_profile_drops_a_zero_decimal(int count, string expected) =>
        Assert.Equal(expected, BadgeLabel.Format(count, BadgeProfile.Current));
}
