using BadgeKit;

namespace BadgeKit.Tests;

public class SerializerTests
{
    [Fact]
    public void New_serializer_matches_old_on_a_plain_request()
    {
        var request = new BadgeRequest("ada-101", "Ada", "Platform Team", ["eng"]);
        Assert.Equal(OldBadgeSerializer.Serialize(request), NewBadgeSerializer.Serialize(request));
    }
}
