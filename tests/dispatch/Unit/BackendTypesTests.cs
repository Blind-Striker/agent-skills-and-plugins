namespace Dispatch.Tests.Unit;

[Category("Unit")]
public sealed class BackendTypesTests
{
    [Test]
    [Arguments(null, null, null, "clean")]
    [Arguments("clean", null, null, "clean")]
    [Arguments("full", null, null, "full")]
    [Arguments(null, "full", null, "full")]
    [Arguments(null, "clean", "clean-partial", "clean-partial")]
    public async Task BuildLaunch_preserves_actual_or_uses_effective_profile(string? specProfile, string? resolved, string? actual, string expected)
    {
        var record = new TaskRecord
        {
            Spec = new() { Id = "edit", Kind = "code", Backend = "codex", Route = "model", Profile = specProfile },
            Settings = new() { Profile = resolved is null ? null : new() { Value = resolved, Actual = actual, Reason = actual is null ? null : "probe unavailable" } },
            Workspace = new() { Path = Path.GetTempPath() },
        };
        var launch = TaskStarter.BuildLaunch(record, "run", "codex", new Policy { Path = "config.json", Deny = [], Escalate = [], Instructions = [] }, null).Launch;
        await Assert.That(launch.Modes.Profile).IsEqualTo(resolved ?? specProfile ?? "clean");
        await Assert.That(launch.ProfileActual).IsEqualTo(expected);
        await Assert.That(launch.ProfileReason).IsEqualTo(actual is null ? null : "probe unavailable");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Worker_prompt_is_one_unchanged_user_text(bool isContinue)
    {
        const string text = "Contract\r\n\nTask: café 日本語\n```json\n{}\n```\n";
        var prompt = new WorkerPrompt(text, isContinue);
        await Assert.That(prompt.Text).IsEqualTo(text);
        await Assert.That(prompt.IsContinue).IsEqualTo(isContinue);
        await Assert.That(typeof(WorkerPrompt).GetProperties().Select(p => p.Name)).IsEquivalentTo(["Text", "IsContinue"]);
    }

    [Test]
    public async Task Launch_carries_the_frozen_profile_and_worker_inputs()
    {
        var launch = Launch("clean-partial", "probe unavailable");
        var check = new LiveCheck("task", [], [], "openai", "clean-partial", "probe unavailable");
        await Assert.That(launch.ProfileActual).IsEqualTo(check.Profile);
        await Assert.That(launch.ProfileReason).IsEqualTo(check.ProfileReason);
        await Assert.That(launch.Modes.Profile).IsEqualTo("clean");
        await Assert.That(launch.Rules.Deny.Single().Name).IsEqualTo("repo/no-publish");
        await Assert.That(launch.Environment["TEMP"]).IsEqualTo("worker-temp");
        await Assert.That(launch.OutputSchema!["type"]!.GetValue<string>()).IsEqualTo("object");
    }

    [Test]
    [Arguments("clean-partial", null)]
    [Arguments("clean-partial", " ")]
    [Arguments("clean", "unexpected downgrade")]
    [Arguments("partial", null)]
    public async Task Frozen_profile_requires_a_reason_only_for_a_downgrade(string profile, string? reason)
    {
        await Assert.That(() => Launch(profile, reason)).Throws<ArgumentException>();
        await Assert.That(() => new LiveCheck("task", [], [], "openai", profile, reason)).Throws<ArgumentException>();
    }

    [Test]
    [Arguments("clean", "on", "workspace", "on", "worktree")]
    [Arguments("full", "off", "read-only", "off", "in-place")]
    [Arguments("full", "on", "full", "on", "worktree")]
    public async Task Modes_keep_the_independent_settings(string profile, string project, string access, string network, string workspace)
    {
        var modes = new Modes(profile, project, access, network, workspace);
        await Assert.That((modes.Profile, modes.Project, modes.Access, modes.Network, modes.Workspace))
            .IsEqualTo((profile, project, access, network, workspace));
    }

    [Test]
    [Arguments("clean-partial", "on", "workspace", "on", "worktree")]
    [Arguments("full", "yes", "workspace", "on", "worktree")]
    [Arguments("full", "on", "sandbox", "on", "worktree")]
    [Arguments("full", "on", "workspace", "none", "worktree")]
    [Arguments("full", "on", "workspace", "on", "scratch")]
    public async Task Modes_refuse_values_that_belong_to_other_layers(string profile, string project, string access, string network, string workspace)
    {
        await Assert.That(() => new Modes(profile, project, access, network, workspace)).Throws<ArgumentException>();
    }

    [Test]
    public async Task Every_task_signal_carries_its_turn_and_blockers_keep_the_origin()
    {
        var blocker = new BlockerRef("opencode", "worker", "child", "ask");
        WorkerSignal.TaskSignal[] signals = [
            new WorkerSignal.Ask("task", "turn", blocker, "shell", ["git status"]),
            new WorkerSignal.AskClosed("task", "turn", "ask", "server", new BlockerAnswer.Once()),
            new WorkerSignal.Form("task", "turn", blocker, "Question"),
            new WorkerSignal.Retry("task", "turn", 1, "busy", null, "rate-limit", "provider"),
            new WorkerSignal.Status("task", "turn", "succeeded", null),
            new WorkerSignal.Ended("task", "turn", "interrupted", "inactivity"),
        ];
        await Assert.That(signals.Select(s => s.Turn)).IsEquivalentTo(Enumerable.Repeat("turn", 6));
        await Assert.That(signals.Select(s => s.TaskId)).IsEquivalentTo(Enumerable.Repeat("task", 6));
        await Assert.That(((WorkerSignal.Ask)signals[0]).BlockerRef.OriginChildId).IsEqualTo("child");
        await Assert.That(((WorkerSignal.Ended)signals[5]).Reason).IsEqualTo("inactivity");
    }

    [Test]
    public async Task Signals_refuse_an_absent_turn_and_status_cannot_claim_lost()
    {
        await Assert.That(() => new WorkerSignal.Ended("task", "", "succeeded", null)).Throws<ArgumentException>();
        await Assert.That(() => new WorkerSignal.Status("task", "turn", "lost", null)).Throws<ArgumentException>();
        await Assert.That(() => new WorkerSignal.Ended("task", "turn", "cancelled", null)).Throws<ArgumentException>();
    }

    [Test]
    public async Task Answers_cannot_grant_a_session_and_rejections_carry_messages()
    {
        BlockerAnswer reject = new BlockerAnswer.Reject("not permitted");
        BlockerAnswer cancel = new BlockerAnswer.Cancel("no human available");
        await Assert.That(((BlockerAnswer.Reject)reject).Message).IsEqualTo("not permitted");
        await Assert.That(((BlockerAnswer.Cancel)cancel).Message).IsEqualTo("no human available");
        await Assert.That(typeof(BlockerAnswer).GetNestedTypes().Select(t => t.Name)).IsEquivalentTo(["Once", "Reject", "Cancel"]);
        await Assert.That(() => new BlockerAnswer.Reject("")).Throws<ArgumentException>();
        await Assert.That(() => new BlockerAnswer.Cancel(" ")).Throws<ArgumentException>();
    }

    [Test]
    public async Task Context_names_the_profiles_folder_without_creating_it()
    {
        var folder = Path.Combine(Dispatch.Tests.Support.TestPaths.Tests, "obj", "unused-profiles");
        var context = new BackendContext(folder, null, new Dictionary<string, string>());
        await Assert.That(context.ProfilesFolder).IsEqualTo(folder);
        await Assert.That(Directory.Exists(folder)).IsFalse();
        await Assert.That(() => new BackendContext("relative/profiles", null, new Dictionary<string, string>())).Throws<ArgumentException>();
    }

    static WorkerLaunch Launch(string profile, string? reason) => new(
        "run", "task", "structured", new RouteRef("provider", "model", "high"), null, null,
        new Modes("clean", "off", "read-only", "on", "worktree"), "workspace",
        new WorkerRules([new EffectiveRule { Name = "repo/no-publish", Action = "shell", Resources = ["npm publish*"] }], []),
        new Dictionary<string, string> { ["TEMP"] = "worker-temp" },
        new JsonObject { ["type"] = "object" }, profile, reason);

    [Test]
    [Arguments("sandbox", "approval-gate", "native", false)]
    [Arguments("policy", "policy", "text", true)]
    public async Task Capabilities_accept_the_backend_enforcement_vocabularies(string sandbox, string rules, string schemaOutput, bool personas)
    {
        var capabilities = new BackendCapabilities(sandbox, rules, schemaOutput, personas);
        await Assert.That(capabilities.Sandbox).IsEqualTo(sandbox);
        await Assert.That(capabilities.Rules).IsEqualTo(rules);
        await Assert.That(capabilities.SchemaOutput).IsEqualTo(schemaOutput);
        await Assert.That(capabilities.Personas).IsEqualTo(personas);
    }

    [Test]
    [Arguments("none", "policy", "text")]
    [Arguments("sandbox", "sandbox", "native")]
    [Arguments("sandbox", "execpolicy", "native")]
    [Arguments("policy", "policy", "json")]
    public async Task Capabilities_refuse_unknown_or_reserved_enforcement(string sandbox, string rules, string schemaOutput)
    {
        await Assert.That(() => new BackendCapabilities(sandbox, rules, schemaOutput, false)).Throws<ArgumentException>();
    }

    [Test]
    [Arguments("sandbox", "sandbox", "approval-gate")]
    [Arguments("policy", "policy", "policy")]
    [Arguments("none", "none", "approval-gate")]
    public async Task Applied_enforcement_can_report_less_than_a_backend_is_capable_of(string access, string network, string rules)
    {
        var applied = new AppliedEnforcement(access, network, rules);
        await Assert.That(applied.Access).IsEqualTo(access);
        await Assert.That(applied.Network).IsEqualTo(network);
        await Assert.That(applied.Rules).IsEqualTo(rules);
    }

    [Test]
    [Arguments("approval-gate", "none", "policy")]
    [Arguments("none", "native", "policy")]
    [Arguments("none", "none", "execpolicy")]
    public async Task Applied_enforcement_does_not_mix_access_and_rule_vocabularies(string access, string network, string rules)
    {
        await Assert.That(() => new AppliedEnforcement(access, network, rules)).Throws<ArgumentException>();
    }
}
