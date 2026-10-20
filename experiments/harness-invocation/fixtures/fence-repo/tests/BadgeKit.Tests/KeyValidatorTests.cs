using BadgeKit;

namespace BadgeKit.Tests;

public class KeyValidatorTests
{
    [Theory]
    [InlineData("ada-101", true)]
    [InlineData("grace-77", true)]
    [InlineData("Bad-Key-9", false)]
    [InlineData("nodash", false)]
    [InlineData("toolongownernameabc-1", false)]
    [InlineData("ok-1234567", false)]
    public void Both_validators_agree(string key, bool valid)
    {
        Assert.Equal(valid, KeyValidator.IsValidSplit(key));
        Assert.Equal(valid, KeyValidator.IsValidSpan(key));
    }
}
