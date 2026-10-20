using BadgeKit;
using Microsoft.Extensions.Logging.Abstractions;

namespace BadgeKit.Tests;

// End-to-end over the request samples. They run only when BADGEKIT_INTEGRATION=1; otherwise each
// test returns at once, so a plain `dotnet test` stays fast.
[Trait("Category", "Integration")]
public class IntegrationTests
{
    private static bool Enabled => Environment.GetEnvironmentVariable("BADGEKIT_INTEGRATION") == "1";

    private static string SamplesDirectory()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "samples");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new DirectoryNotFoundException("samples/ not found above the test output");
    }

    [Fact]
    public void Every_sample_except_the_bad_key_is_issued()
    {
        if (!Enabled)
        {
            return;
        }

        var issuer = new BadgeIssuer(NullLogger<BadgeIssuer>.Instance);
        var results = Directory.GetFiles(SamplesDirectory(), "*.json")
            .Order()
            .Select(file => issuer.TryIssue(NewBadgeSerializer.Deserialize(File.ReadAllText(file)), out _))
            .ToList();
        Assert.Equal([true, true, true, false], results);
    }
}
