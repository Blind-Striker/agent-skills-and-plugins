using System.Text.Json.Nodes;
using Dispatch.Tests.Support;

namespace Dispatch.Tests.Integration;

[Category("Integration")]
[NotInParallel]
public sealed class OpenCodeModesTests
{
    [Test]
    public async Task Missing_fenced_host_is_lost_on_reattach_and_collection_without_reconstruction(CancellationToken ct)
    {
        await using var backend = new OpenCodeBackend();
        var worker = new WorkerRef("opencode", "task", "session", "turn", new JsonObject { ["host"] = "fence=run/task" });
        var reattached = await backend.ReattachAsync(worker, ct);
        await Assert.That(reattached).IsTypeOf<ReattachResult.Ended>();
        await Assert.That(((ReattachResult.Ended)reattached).Outcome).IsEqualTo("lost");
        var report = await backend.CollectAsync(worker, ct);
        await Assert.That(report.Outcome).IsEqualTo("lost");
        await Assert.That(report.ExecutionError).IsEqualTo("fenced opencode server is no longer live");
        await Assert.That(report.Complete).IsTrue();
        await backend.ReleaseAsync(worker, ct);
    }

    [Test]
    [Timeout(30_000)]
    public async Task Fenced_tasks_have_private_keys_and_release_disposes_only_their_server(CancellationToken ct)
    {
        if (Ready.FenceKind is null) Skip.Test("opencode runs unfenced on Windows.");
        if (await Ready.FenceAsync(ct) is { } refusal) Skip.Test(refusal);
        await using var lab = new OpenCodeLab();
        lab.Add("POST /api/session", OpenCodeLab.Session("worker"), repeat: true);
        lab.Add("PUT /api/session/worker/environment", status: 204, repeat: true);
        lab.Add("DELETE /api/session/worker", status: 204, repeat: true);
        lab.Write();
        var first = (await lab.Backend.CreateAsync(Launch(lab, "first"), ct)).Worker;
        var firstPid = lab.Hosts().Single()["pid"]!.GetValue<int>();
        var second = (await lab.Backend.CreateAsync(Launch(lab, "second"), ct)).Worker;
        await Assert.That(new[] { first.State["host"]!.GetValue<string>(), second.State["host"]!.GetValue<string>() })
            .IsEquivalentTo(new[] { "fence=run/first", "fence=run/second" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(lab.Hosts().Count).IsEqualTo(2);
        var secondPid = lab.Hosts().Single(x => x["pid"]!.GetValue<int>() != firstPid)["pid"]!.GetValue<int>();
        var firstServer = ObservedProcess.Capture(firstPid);
        var secondServer = ObservedProcess.Capture(secondPid);
        await lab.Backend.ReleaseAsync(first, ct);
        await Assert.That(firstServer.IsAlive()).IsFalse();
        await Assert.That(secondServer.IsAlive()).IsTrue();
        await lab.Backend.ReleaseAsync(second, ct);
        await Assert.That(secondServer.IsAlive()).IsFalse();
    }

    [Test]
    [Timeout(30_000)]
    public async Task Clean_profile_server_uses_generated_root_and_overrides_inherited_config(CancellationToken ct)
    {
        await using var lab = new OpenCodeLab();
        var global = Directory.CreateDirectory(Path.Combine(lab.Tree.Root, "global-config")).FullName;
        File.WriteAllText(Path.Combine(global, "opencode.json"), """
            {"providers":{"fixture":{"settings":{"endpoint":"fixture"},"models":{"model":{"name":"old"},"unused":{}}},"unused":{}},"instructions":["ignore"],"mcp":{"servers":{}}}
            """);
        File.WriteAllText(Path.Combine(global, "opencode.jsonc"), """
            { // Higher-priority model definition.
              "providers":{"fixture":{"models":{"model":{"name":"selected"},"other":{}}}},
              "shell":"fixture-shell",
            }
            """);
        lab.SetEnvironment("OPENCODE_CONFIG_DIR", global);
        lab.SetEnvironment("OPENCODE_CONFIG", "inherited-config");
        lab.SetEnvironment("OPENCODE_CONFIG_CONTENT", "{\"instructions\":[\"inherited\"]}");
        lab.Create("worker");
        lab.Write();

        var launch = Launch(lab, "worker", "clean");
        var variables = new Dictionary<string, string>(launch.Environment)
        {
            ["OPENCODE_CONFIG_DIR"] = global, ["OPENCODE_CONFIG"] = "inherited-config",
            ["OPENCODE_CONFIG_CONTENT"] = "{\"instructions\":[\"inherited\"]}",
            ["OPENCODE_DISABLE_FILEWATCHER"] = "0",
        };
        await lab.Backend.CreateAsync(launch with { Environment = variables }, ct);

        var host = lab.Hosts().Single();
        var root = host["configRoot"]?.GetValue<string>();
        await Assert.That(root).IsNotEqualTo(global);
        await Assert.That(root).IsNotNull();
        await Assert.That(root!.EndsWith(Path.Combine("profiles", "opencode"), StringComparison.Ordinal)).IsTrue();
        await Assert.That(host["directory"]!.GetValue<string>()).IsEqualTo(root);
        var env = host["environment"]!;
        await Assert.That(env["OPENCODE_CONFIG"]!.GetValue<string>()).IsEqualTo("");
        await Assert.That(env["OPENCODE_CONFIG_CONTENT"]!.GetValue<string>()).IsEqualTo("{}");
        await Assert.That(env["OPENCODE_DISABLE_CLAUDE_CODE"]!.GetValue<string>()).IsEqualTo("1");
        await Assert.That(env["OPENCODE_FILEWATCHER_DISABLE"]!.GetValue<string>()).IsEqualTo("1");
        await Assert.That(env["OPENCODE_DISABLE_FILEWATCHER"]!.GetValue<string>()).IsEqualTo("1");
        var config = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "opencode.json")))!;
        var expected = JsonNode.Parse("""
            {"$schema":"https://opencode.ai/config.json","claude":false,"plugins":["-opencode.config.compatibility"],"providers":{"fixture":{"settings":{"endpoint":"fixture"},"models":{"model":{"name":"selected"}}}},"shell":"fixture-shell"}
            """);
        await Assert.That(JsonNode.DeepEquals(config, expected)).IsTrue();
    }

    [Test]
    [Timeout(30_000)]
    public async Task Project_off_server_overrides_both_project_config_variables(CancellationToken ct)
    {
        await using var lab = new OpenCodeLab();
        lab.SetEnvironment("OPENCODE_CONFIG_PROJECT_DISABLE", "0");
        lab.SetEnvironment("OPENCODE_DISABLE_PROJECT_CONFIG", "0");
        lab.Create("worker");
        lab.Write();

        await lab.Backend.CreateAsync(Launch(lab, "worker", project: "off"), ct);

        var env = lab.Hosts().Single()["environment"]!;
        await Assert.That(env["OPENCODE_CONFIG_PROJECT_DISABLE"]!.GetValue<string>()).IsEqualTo("1");
        await Assert.That(env["OPENCODE_DISABLE_PROJECT_CONFIG"]!.GetValue<string>()).IsEqualTo("1");
    }

    [Test]
    [Arguments("clean", "off", "full", "on")]
    [Arguments("full", "off", "full", "on")]
    [Arguments("clean", "off", "clean", "on")]
    [Timeout(30_000)]
    public async Task Distinct_profile_project_pairs_get_private_hosts_and_matching_pairs_reuse_them(
        string firstProfile, string firstProject, string secondProfile, string secondProject, CancellationToken ct)
    {
        await using var lab = new OpenCodeLab();
        lab.SetEnvironment("OPENCODE_CONFIG_DIR", Path.Combine(lab.Tree.Root, "global-config"));
        lab.SetEnvironment("OPENCODE_CONFIG", "inherited-config");
        lab.SetEnvironment("OPENCODE_CONFIG_CONTENT", "{}");
        lab.SetEnvironment("OPENCODE_DISABLE_CLAUDE_CODE", "0");
        lab.SetEnvironment("OPENCODE_CONFIG_PROJECT_DISABLE", "0");
        lab.SetEnvironment("OPENCODE_DISABLE_PROJECT_CONFIG", "0");
        lab.Write();
        var first = Launch(lab, "first", firstProfile, firstProject);
        var second = Launch(lab, "second", secondProfile, secondProject);
        WorkerPlan Plan(WorkerLaunch launch) => new(launch.TaskId, launch.Kind, launch.Route, launch.Agent,
            launch.Persona, launch.Modes, launch.Directory);

        await lab.Backend.ValidateAsync([Plan(first), Plan(second), Plan(first)], ct);

        var hosts = lab.Hosts();
        await Assert.That(hosts.Count).IsEqualTo(2);
        await Assert.That(hosts.Select(x => x["pid"]!.GetValue<int>()).Distinct().Count()).IsEqualTo(2);
        foreach (var launch in new[] { first, second })
        {
            var host = hosts.Single(x => x["environment"]!["OPENCODE_DISABLE_CLAUDE_CODE"]!.GetValue<string>() == (launch.Modes.Profile == "clean" ? "1" : "0")
                && x["environment"]!["OPENCODE_DISABLE_PROJECT_CONFIG"]!.GetValue<string>() == (launch.Modes.Project == "off" ? "1" : "0"));
            if (launch.Modes.Profile == "full")
                await Assert.That(host["environment"]!["OPENCODE_CONFIG"]!.GetValue<string>()).IsEqualTo("inherited-config");
        }
    }

    [Test]
    [Timeout(30_000)]
    public async Task Mode_denies_follow_escalations_and_report_only_the_enforcement_applied(CancellationToken ct)
    {
        await using var lab = new OpenCodeLab();
        var cases = new[]
        {
            (Access: "read-only", Network: "off", AccessEnforcement: "policy", NetworkEnforcement: "policy"),
            (Access: "read-only", Network: "on", AccessEnforcement: "policy", NetworkEnforcement: "none"),
            (Access: "workspace", Network: "off", AccessEnforcement: "none", NetworkEnforcement: "policy"),
            (Access: "workspace", Network: "on", AccessEnforcement: "none", NetworkEnforcement: "none"),
            (Access: "full", Network: "off", AccessEnforcement: "none", NetworkEnforcement: "policy"),
            (Access: "full", Network: "on", AccessEnforcement: "none", NetworkEnforcement: "none"),
        };
        foreach (var item in cases) lab.Create(item.Access + "-" + item.Network);
        lab.Write();
        foreach (var item in cases)
        {
            var launch = Launch(lab, item.Access + "-" + item.Network, access: item.Access, network: item.Network) with
            {
                Rules = new([], new[] { "shell", "edit", "webfetch", "websearch" }.Select(action =>
                    new EffectiveRule { Name = "task/ask-" + action, Action = action, Resources = ["*"] }).ToList()),
            };
            var created = await lab.Backend.CreateAsync(launch, ct);

            await Assert.That(created.Applied.Access).IsEqualTo(Ready.Fenced(launch.Modes) ? "sandbox" : item.AccessEnforcement);
            await Assert.That(created.Applied.Network).IsEqualTo(item.NetworkEnforcement);
            await Assert.That(created.Applied.Rules).IsEqualTo("policy");
            foreach (var action in new[] { "edit", "webfetch", "websearch" })
            {
                var modeDeny = action == "edit" ? item.Access == "read-only" : item.Network == "off";
                var matching = created.Details!.Rules.Where(x => x.StartsWith(action + " * ", StringComparison.Ordinal)).ToList();
                await Assert.That(matching.Last()).IsEqualTo($"{action} * {(modeDeny ? "deny" : "ask")}");
            }
        }
    }

    [Test]
    [Timeout(30_000)]
    public async Task Hard_stop_of_an_unshared_host_kills_server_children_without_removing_session(CancellationToken ct)
    {
        await using var lab = new OpenCodeLab();
        var records = Path.GetDirectoryName(FakeOpenCode.Record(lab.Tree))!;
        lab.SetEnvironment("FAKE_OPENCODE_CHILD_RECORD", Path.Combine(records, "children"));
        lab.Create("worker");
        lab.Add("POST /api/session/worker/interrupt", new JsonObject { ["interrupted"] = false }, repeat: true);
        lab.Write();
        try
        {
            var worker = (await lab.Backend.CreateAsync(Launch(lab, "worker"), ct)).Worker;
            var pid = lab.Hosts().Single()["pid"]!.GetValue<int>();
            var descendants = JsonNode.Parse(File.ReadAllText(Path.Combine(records, $"children.{pid}.json")))!;
            using var server = System.Diagnostics.Process.GetProcessById(pid);
            var (child, grandchild) = ObservedProcess.Descendants(descendants);
            await lab.Backend.InterruptAsync(worker, InterruptKind.Timeout, ct);
            var stopped = await lab.Backend.InterruptAsync(worker, InterruptKind.Hard, ct);

            await Assert.That(stopped.Stopped).IsTrue();
            await Assert.That(stopped.Detail).IsEqualTo("tree-kill");
            await Assert.That(server.HasExited).IsTrue();
            var deadline = System.Diagnostics.Stopwatch.StartNew();
            while ((child.IsAlive() || grandchild.IsAlive()) && deadline.Elapsed < TimeSpan.FromSeconds(5))
                await Task.Delay(20, ct);
            await Assert.That(child.IsAlive()).IsFalse();
            await Assert.That(grandchild.IsAlive()).IsFalse();
            var requests = File.ReadLines(FakeOpenCode.Record(lab.Tree)).Select(x => JsonNode.Parse(x)!).ToList();
            await Assert.That(requests.Count(x => x["method"]!.GetValue<string>() == "DELETE")).IsEqualTo(0);
        }
        finally
        {
            await ObservedProcess.CleanupRecordedChildrenAsync(records);
        }
    }

    [Test]
    [Arguments(200)]
    [Arguments(500)]
    [Timeout(30_000)]
    public async Task Hard_stop_of_a_shared_host_interrupts_without_killing_other_live_workers(int status, CancellationToken ct)
    {
        await using var lab = new OpenCodeLab();
        lab.Create("worker");
        lab.Create("peer");
        lab.Add("POST /api/session/worker/interrupt", status == 200 ? new JsonObject { ["interrupted"] = false }
            : new JsonObject { ["_tag"] = "InternalError", ["message"] = "interrupt failed" }, status, repeat: true);
        lab.Prompt("peer");
        lab.Write();
        var worker = (await lab.Backend.CreateAsync(Launch(lab, "worker", access: "full"), ct)).Worker;
        var peer = (await lab.Backend.CreateAsync(Launch(lab, "peer", access: "full"), ct)).Worker;
        using var server = System.Diagnostics.Process.GetProcessById(lab.Hosts().Single()["pid"]!.GetValue<int>());

        await lab.Backend.InterruptAsync(worker, InterruptKind.Timeout, ct);
        var stopped = await lab.Backend.InterruptAsync(worker, InterruptKind.Hard, ct);

        await Assert.That(stopped.Stopped).IsFalse();
        await Assert.That(stopped.Detail).IsEqualTo("interrupt");
        await Assert.That(server.HasExited).IsFalse();
        var prompted = await lab.Backend.PromptAsync(peer, new("Inspect.", false), ct);
        await Assert.That(prompted.Turn).IsEqualTo("turn");
        var requests = File.ReadLines(FakeOpenCode.Record(lab.Tree)).Select(x => JsonNode.Parse(x)!).ToList();
        await Assert.That(requests.Count(x => x["path"]!.GetValue<string>() == "/api/session/worker/interrupt")).IsEqualTo(2);
        await Assert.That(requests.Count(x => x["method"]!.GetValue<string>() == "DELETE")).IsEqualTo(0);
    }

    [Test]
    [Timeout(30_000)]
    public async Task Concurrent_hard_stops_confirm_the_same_kill_without_restarting_the_host(CancellationToken ct)
    {
        await using var lab = new OpenCodeLab();
        lab.Create("worker");
        lab.Add("POST /api/session/worker/interrupt", new JsonObject { ["interrupted"] = false }, repeat: true);
        lab.Write();
        var worker = (await lab.Backend.CreateAsync(Launch(lab, "worker"), ct)).Worker;

        var results = await Task.WhenAll(lab.Backend.InterruptAsync(worker, InterruptKind.Hard, ct),
            lab.Backend.InterruptAsync(worker, InterruptKind.Hard, ct));

        await Assert.That(results.Select(x => x.Stopped)).IsEquivalentTo([true, true]);
        await Assert.That(lab.Hosts().Count).IsEqualTo(1);
    }

    static WorkerLaunch Launch(OpenCodeLab lab, string id, string profile = "full", string project = "on",
        string access = "workspace", string network = "on") => new("run", id, "research", new("fixture", "model", null),
        null, "build", new(profile, project, access, network, "worktree"), lab.Tree.Repository,
        new([], []), lab.Tree.ChildEnvironment().ToDictionary(x => x.Key, x => x.Value!), null, profile, null);
}
