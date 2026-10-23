namespace Dispatch.Tests.Unit;

[Category("Unit")]
public sealed class RouteCheckTests
{
    [Test]
    [Arguments("opencode:fixture/model", "fixture/model#default", true, "")]
    [Arguments("opencode:fixture/model", "fixture/model#high", true, "")]
    [Arguments("opencode:fixture/model#high", "fixture/model#high", true, "")]
    [Arguments("opencode:fixture/model#high", "fixture/model#low", false, "fixture/model#low")]
    [Arguments("opencode:fixture/model", "opencode/flint-40b-free#default", false, "opencode/flint-40b-free#default")]
    [Arguments("opencode:fixture/model", "fixture/model#default,other/model#default", false, "other/model#default")]
    [Arguments("opencode:fixture/model#default", "fixture/model#default", true, "")]
    [Arguments("opencode:fixture/model#default", "fixture/model#high", true, "")]
    [Arguments("codex:gpt-x#high", "codex:gpt-x#high", true, "")]
    [Arguments("codex:gpt-x#high", "codex:gpt-y#high", false, "codex:gpt-y#high")]
    [Arguments("codex:gpt-x", "codex:gpt-x#medium", true, "")]
    public async Task A_reported_route_of_another_model_or_variant_is_drift(string requested, string used, bool verified, string drift)
    {
        var (ok, found) = Settlement.CheckRoutes(requested, used.Split(','));

        await Assert.That(ok).IsEqualTo(verified);
        await Assert.That(found).IsEquivalentTo(drift.Split(',', StringSplitOptions.RemoveEmptyEntries));
    }

    [Test]
    [Arguments("opencode:fixture/model", "")]
    [Arguments("opencode:reviewer", "fixture/model#default")]
    [Arguments("codex:gpt-x#high", "codex:gpt-x#")]
    public async Task No_reported_route_or_no_comparable_request_is_unverified_without_drift(string requested, string used)
    {
        var (ok, found) = Settlement.CheckRoutes(requested, used.Split(',', StringSplitOptions.RemoveEmptyEntries));

        await Assert.That(ok).IsFalse();
        await Assert.That(found).IsEmpty();
    }
}
