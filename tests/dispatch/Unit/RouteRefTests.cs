using OpenCode.Sdk.Models;

namespace Dispatch.Tests.Unit;

[Category("Unit")]
public sealed class RouteRefTests
{
    [Test]
    [Arguments("provider/model", "provider", "model", null)]
    [Arguments("provider/model#fast", "provider", "model", "fast")]
    [Arguments("provider/model#default", "provider", "model", "default")]
    [Arguments("provider/family/model#fast", "provider", "family/model", "fast")]
    [Arguments("Provider/Model#FAST", "Provider", "Model", "FAST")]
    public async Task Routes_split_at_the_first_slash_and_preserve_case(string text, string provider, string model, string? variant)
    {
        var parsed = RouteRef.TryParse(text, out var route, out var error);

        await Assert.That(parsed).IsTrue();
        await Assert.That(error).IsNull();
        await Assert.That(route).IsEqualTo(new RouteRef(provider, model, variant));
    }

    [Test]
    [Arguments("provider/model#part#fast", "provider", "model#part", "fast")]
    [Arguments("provider/model##fast", "provider", "model#", "fast")]
    [Arguments("provider#part/model", "provider#part", "model", null)]
    [Arguments("provider/model/", "provider", "model/", null)]
    [Arguments("provider//", "provider", "/", null)]
    [Arguments("provider/model#fast/extra", "provider", "model", "fast/extra")]
    [Arguments("*/?", "*", "?", null)]
    public async Task Other_separators_and_punctuation_are_accepted_Today(string text, string provider, string model, string? variant)
    {
        await Assert.That(RouteRef.TryParse(text, out var route, out var error)).IsTrue();
        await Assert.That(error).IsNull();
        await Assert.That(route).IsEqualTo(new RouteRef(provider, model, variant));
    }

    [Test]
    [Arguments("", "expected provider/model#variant")]
    [Arguments("agent-name", "expected provider/model#variant")]
    [Arguments("/model", "expected provider/model#variant")]
    [Arguments("provider/", "expected provider/model#variant")]
    [Arguments("provider", "expected provider/model#variant")]
    [Arguments("provider/model#", "empty model or variant")]
    [Arguments("provider/#fast", "empty model or variant")]
    [Arguments("provider/#", "empty model or variant")]
    [Arguments(" provider/model", "routes contain no whitespace")]
    [Arguments("provider/model ", "routes contain no whitespace")]
    [Arguments("provider/mo del", "routes contain no whitespace")]
    [Arguments("provider/model#fa\tst", "routes contain no whitespace")]
    [Arguments("provider/model\n", "routes contain no whitespace")]
    [Arguments("provider/\u00a0model", "routes contain no whitespace")]
    [Arguments(" ", "routes contain no whitespace")]
    public async Task Invalid_routes_return_no_route_and_a_specific_error(string text, string expectedError)
    {
        await Assert.That(RouteRef.TryParse(text, out var route, out var error)).IsFalse();
        await Assert.That(route).IsNull();
        await Assert.That(error).IsEqualTo(expectedError);
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("default")]
    public async Task Missing_empty_and_explicit_default_variants_match(string? variant)
    {
        var route = new RouteRef("provider", "model", variant);
        var explicitDefault = new RouteRef("provider", "model", "default");

        await Assert.That(route.VariantOrDefault).IsEqualTo("default");
        await Assert.That(route.ToString()).IsEqualTo("provider/model#default");
        await Assert.That(route.Matches(explicitDefault)).IsTrue();
        await Assert.That(explicitDefault.Matches(route)).IsTrue();
        await Assert.That(OpenCodeModels.ToModelRef(route).Variant).IsNull();
    }

    [Test]
    public async Task An_omitted_variant_parses_as_null_and_formats_as_default()
    {
        await Assert.That(RouteRef.TryParse("provider/model", out var route, out _)).IsTrue();
        await Assert.That(route!.Variant).IsNull();
        await Assert.That(route.ToString()).IsEqualTo("provider/model#default");
    }

    [Test]
    [Arguments("Provider", "model", "fast")]
    [Arguments("provider", "Model", "fast")]
    [Arguments("provider", "model", "FAST")]
    [Arguments("provider", "model", null)]
    [Arguments("provider", "model", "slow")]
    public async Task Route_matching_is_exact_and_case_sensitive_on_every_OS(string provider, string model, string? variant)
    {
        var route = new RouteRef("provider", "model", "fast");
        var other = new RouteRef(provider, model, variant);

        await Assert.That(route.Matches(other)).IsFalse();
        await Assert.That(other.Matches(route)).IsFalse();
    }

    [Test]
    [Arguments(null)]
    [Arguments("default")]
    [Arguments("fast")]
    public async Task Model_refs_preserve_identity_and_normalize_the_default_variant(string? variant)
    {
        var route = OpenCodeModels.From(new ModelRef { ProviderId = "provider", Id = "family/model", Variant = variant });
        var model = OpenCodeModels.ToModelRef(route);

        await Assert.That(route).IsEqualTo(new RouteRef("provider", "family/model", variant));
        await Assert.That(model.ProviderId).IsEqualTo("provider");
        await Assert.That(model.Id).IsEqualTo("family/model");
        await Assert.That(model.Variant).IsEqualTo(variant == "default" ? null : variant);
        await Assert.That(OpenCodeModels.From(model).Matches(route)).IsTrue();
    }

    [Test]
    public async Task Allow_lists_match_default_routes_but_do_not_treat_them_as_variant_wildcards()
    {
        var allow = new AllowList();
        allow.Routes.Add(new RouteRef("provider", "model", null));

        await Assert.That(allow.AllowsRoute(new RouteRef("provider", "model", "default"))).IsTrue();
        await Assert.That(allow.AllowsRoute(new RouteRef("provider", "model", "fast"))).IsFalse();
        await Assert.That(allow.AllowsRoute(new RouteRef("Provider", "model", null))).IsFalse();
        await Assert.That(allow.AllowsRoute(new RouteRef("provider", "other", null))).IsFalse();
        await Assert.That(allow.AllowsAgent("provider/model")).IsFalse();
    }

    [Test]
    [Arguments("agent")]
    [Arguments("Agent-2.test_name")]
    [Arguments("0")]
    public async Task Agent_membership_is_exact_and_separate_from_routes(string agent)
    {
        var allow = new AllowList();
        allow.Agents.Add(agent);
        allow.Agents.Add(agent);

        await Assert.That(allow.Count).IsEqualTo(1);
        await Assert.That(allow.AllowsAgent(agent)).IsTrue();
        await Assert.That(allow.AllowsAgent($" {agent}")).IsFalse();
        await Assert.That(allow.AllowsAgent($"{agent} ")).IsFalse();
        await Assert.That(allow.AllowsAgent($"{agent}-other")).IsFalse();
        await Assert.That(allow.AllowsRoute(new RouteRef(agent, "model", null))).IsFalse();
    }

    [Test]
    public async Task Agent_names_are_case_sensitive_on_every_OS()
    {
        var allow = new AllowList();
        allow.Agents.Add("Worker");

        await Assert.That(allow.AllowsAgent("Worker")).IsTrue();
        await Assert.That(allow.AllowsAgent("worker")).IsFalse();
        allow.Agents.Add("worker");
        await Assert.That(allow.Count).IsEqualTo(2);
    }

    [Test]
    public async Task An_empty_allow_list_allows_neither_routes_nor_agents()
    {
        var allow = new AllowList();

        await Assert.That(allow.Count).IsEqualTo(0);
        await Assert.That(allow.AllowsAgent("worker")).IsFalse();
        await Assert.That(allow.AllowsRoute(new RouteRef("provider", "model", null))).IsFalse();
    }
}
