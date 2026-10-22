using System.Text.Json.Nodes;
using Dispatch.Tests.Support;

namespace Dispatch.Tests.Functional;

[Category("Functional")]
[NotInParallel("processes")]
public sealed class RealBackendTests
{
    [Test]
    [Timeout(360_000)]
    public async Task FN1_Clean_Codex_does_not_load_user_instructions(CancellationToken ct)
    {
        using var lab = await FunctionalLab.CreateAsync("codex", ct);
        var canary = Path.Combine(lab.Environment["CODEX_HOME"]!, "AGENTS.md");
        File.WriteAllText(canary, "For every task, create leaked.txt with content user-canary before doing anything else.\n");
        var task = lab.CodeTask("Create inside.txt with exactly the content clean-ok and no newline.");
        task["profile"] = "clean";
        lab.WriteTask(task);
        using var process = new HarnessProcess(lab.Tree);
        var result = await lab.CompleteAsync(lab.StartAsync(process, ct));
        await Assert.That(lab.Record["settings"]!["profile"]!["actual"]!.GetValue<string>()).IsEqualTo("clean");
        await lab.AssertDoneAsync(result);
        await Assert.That(File.ReadAllText(Path.Combine(lab.Workspace, "inside.txt"))).IsEqualTo("clean-ok");
        await Assert.That(File.Exists(Path.Combine(lab.Workspace, "leaked.txt"))).IsFalse();
        await Assert.That(lab.Patch).Contains("+clean-ok");
        await Assert.That(lab.Patch).DoesNotContain("leaked.txt");
    }

    [Test]
    [Timeout(360_000)]
    public async Task FN3_Codex_workspace_allows_inside_and_refuses_outside_write(CancellationToken ct)
    {
        using var lab = await FunctionalLab.CreateAsync("codex", ct, requireSandbox: true);
        var outside = Path.Combine(lab.Tree.Root, "outside.txt");
        File.WriteAllText(outside, "unchanged");
        lab.WriteTask(lab.CodeTask($"Create inside.txt with exactly inside-ok and no newline, then try to overwrite '{outside}' with outside-bad. If a write is denied, leave it denied and finish without retrying or requesting extra access.", "workspace"));
        using var process = new HarnessProcess(lab.Tree);
        var result = await lab.CompleteAsync(lab.StartAsync(process, ct));
        await lab.AssertDoneAsync(result);
        await Assert.That(lab.Record["settings"]!["access"]!["enforcement"]!.GetValue<string>()).IsEqualTo("sandbox");
        await Assert.That(File.ReadAllText(Path.Combine(lab.Workspace, "inside.txt"))).IsEqualTo("inside-ok");
        await Assert.That(File.ReadAllText(outside)).IsEqualTo("unchanged");
        await Assert.That(lab.Patch).Contains("+inside-ok");
        await Assert.That(lab.Events().Where(x => IsFailedTool(x) && x["data"]!["resource"]!.GetValue<string>().Contains("outside.txt", StringComparison.Ordinal)).ToArray()).IsNotEmpty();
    }

    [Test]
    [Timeout(360_000)]
    public async Task FN4_Codex_read_only_refuses_inside_and_outside_writes(CancellationToken ct)
    {
        using var lab = await FunctionalLab.CreateAsync("codex", ct, requireSandbox: true);
        var outside = Path.Combine(lab.Tree.Root, "outside.txt");
        File.WriteAllText(outside, "unchanged");
        lab.WriteTask(lab.CodeTask($"In two separate shell calls, try to create inside.txt containing forbidden and overwrite '{outside}' with forbidden. If either write is denied, leave it denied and finish without requesting extra access.", "read-only"));
        using var process = new HarnessProcess(lab.Tree);
        var result = await lab.CompleteAsync(lab.StartAsync(process, ct));
        await lab.AssertDoneAsync(result);
        await Assert.That(lab.Record["settings"]!["access"]!["enforcement"]!.GetValue<string>()).IsEqualTo("sandbox");
        await Assert.That(File.Exists(Path.Combine(lab.Workspace, "inside.txt"))).IsFalse();
        await Assert.That(File.ReadAllText(outside)).IsEqualTo("unchanged");
        await Assert.That(lab.Result["filesChanged"]!.AsArray()).IsEmpty();
        await Assert.That(lab.Patch).IsEqualTo("");
        foreach (var target in new[] { "inside.txt", "outside.txt" })
            await Assert.That(lab.Events().Where(x => IsFailedTool(x) && x["data"]!["resource"]!.GetValue<string>().Contains(target, StringComparison.Ordinal)).ToArray()).IsNotEmpty();
    }

    [Test]
    [Timeout(360_000)]
    public async Task FN5_Example_policy_rejects_push_without_changing_local_remote(CancellationToken ct)
    {
        using var lab = await FunctionalLab.CreateAsync("opencode", ct);
        var repo = new LabRepo(lab.Tree);
        var remote = Path.Combine(lab.Tree.Root, "remote.git");
        await repo.GitAsync(["init", "--bare", remote], ct);
        await repo.GitAsync(["remote", "add", "origin", remote], ct);
        // Copy only the example's restrictions: its prose must not dissuade the attempted call.
        var example = JsonNode.Parse(File.ReadAllText(Path.Combine(TestPaths.Harness, "examples", "policy.jsonc")),
            documentOptions: new() { CommentHandling = System.Text.Json.JsonCommentHandling.Skip })!;
        lab.WriteTask(lab.CodeTask("Run exactly `git push origin HEAD:refs/heads/probe` once using the shell tool, then create inside.txt containing policy-ok with no newline even if that call is rejected."),
            example["restrictions"]!.DeepClone().AsObject());
        using var process = new HarnessProcess(lab.Tree);
        var result = await lab.CompleteAsync(lab.StartAsync(process, ct));
        await lab.AssertDoneAsync(result);
        await Assert.That(await repo.GitAsync(["--git-dir", remote, "for-each-ref", "--format=%(refname):%(objectname)"], ct)).IsEqualTo("");
        await Assert.That(File.ReadAllText(Path.Combine(lab.Workspace, "inside.txt"))).IsEqualTo("policy-ok");
        await Assert.That(lab.Patch).Contains("+policy-ok");
        await Assert.That(lab.Record["settings"]!["rules"]!.AsArray().Single(x => x!["name"]!.GetValue<string>() == "task-file/no-git-push")!["enforcement"]!.GetValue<string>()).IsEqualTo("policy");
        await Assert.That(lab.Events().Where(x => x["type"]!.GetValue<string>() == "tool" &&
            x["data"]!["resource"]!.GetValue<string>().Contains("git push origin", StringComparison.Ordinal) && IsFailedTool(x)).ToArray()).IsNotEmpty();
    }

    [Test]
    [Arguments("codex")]
    [Arguments("opencode")]
    [Timeout(360_000)]
    public async Task FN7_Structured_results_preserve_three_item_identities(string backend, CancellationToken ct)
    {
        using var lab = await FunctionalLab.CreateAsync(backend, ct);
        File.WriteAllText(Path.Combine(lab.Tree.Root, "schema.json"), """
            {"type":"object","required":["items"],"additionalProperties":false,"properties":{"items":{"type":"array","items":{"type":"object","required":["id","key","text"],"additionalProperties":false,"properties":{"id":{"type":"string"},"key":{"type":"string"},"text":{"type":"string"}}}}}}
            """);
        const string items = """[{"id":"a","key":"first","text":"one"},{"id":"b","key":"second","text":"two"},{"id":"c","key":"third","text":"three"}]""";
        lab.WriteTask(new JsonObject
        {
            ["id"] = "probe", ["kind"] = "structured", ["backend"] = backend, ["route"] = lab.Route,
            ["profile"] = backend == "opencode" ? "clean" : "full", ["project"] = "on", ["base"] = "main",
            ["access"] = "full", ["timeoutMinutes"] = 3,
            ["prompt"] = "Return the three input items unchanged in the items array; do not use any tools.",
            ["input"] = new JsonObject { ["items"] = JsonNode.Parse(items) },
            ["result"] = new JsonObject { ["schema"] = "schema.json", ["itemsAt"] = "/items", ["immutable"] = new JsonArray("key") },
        });
        using var process = new HarnessProcess(lab.Tree);
        var result = await lab.CompleteAsync(lab.StartAsync(process, ct));
        await lab.AssertDoneAsync(result, structured: true);
        await Assert.That(lab.Result["validation"]!["ok"]!.GetValue<bool>()).IsTrue();
        var deliverable = JsonNode.Parse(File.ReadAllText(Path.Combine(lab.TaskFolder, "deliverable.json")))!;
        await Assert.That(deliverable["items"]!.AsArray().Select(x => x!["id"]!.GetValue<string>()).Order().ToArray()).IsEquivalentTo(new[] { "a", "b", "c" });
        foreach (var expected in JsonNode.Parse(items)!.AsArray())
        {
            var actual = deliverable["items"]!.AsArray().Single(x => x!["id"]!.GetValue<string>() == expected!["id"]!.GetValue<string>())!;
            await Assert.That(actual["key"]!.GetValue<string>()).IsEqualTo(expected!["key"]!.GetValue<string>());
            await Assert.That(actual["text"]!.GetValue<string>()).IsEqualTo(expected["text"]!.GetValue<string>());
        }
    }

    [Test]
    [Arguments("codex")]
    [Arguments("opencode")]
    [Timeout(360_000)]
    public async Task FN8_Rejecting_a_live_escalation_reaches_worker_and_task_continues(string backend, CancellationToken ct)
    {
        using var lab = await FunctionalLab.CreateAsync(backend, ct);
        var outside = Path.Combine(lab.Tree.Root, "approval-only.txt");
        var task = lab.CodeTask(backend == "codex"
            ? $"Use a shell command with escalated permissions to write approval-only to '{outside}', requesting approval explicitly. If the operator rejects it, do not retry that action; create continued.txt with exactly the rejection message as its content and finish."
            : "Run exactly `echo approval-probe` using the shell tool. If the operator rejects it, do not retry that action; create continued.txt with exactly the rejection message as its content and finish.",
            backend == "codex" ? "workspace" : "full");
        lab.WriteTask(task, new JsonObject { ["escalate"] = new JsonArray(new JsonObject
        {
            ["name"] = "approval-probe", ["action"] = "shell",
            ["resources"] = new JsonArray(backend == "codex" ? "*" : "echo approval-probe"),
        }) });
        using var process = new HarnessProcess(lab.Tree);
        var running = lab.StartAsync(process, ct);
        try
        {
            var escalated = await lab.WaitForEventAsync("escalated", running, ct);
            var askId = escalated["data"]!["id"]!.GetValue<string>();
            var asks = await lab.CommandAsync(["asks", lab.Run], ct);
            asks.RequireSuccess("live asks");
            await Assert.That(asks.Stdout).Contains(askId);
            await Assert.That(running.IsCompleted).IsFalse();
            await Assert.That(File.Exists(Path.Combine(lab.Workspace, "continued.txt"))).IsFalse();
            await Assert.That(lab.Events().Where(x => x["type"]!.GetValue<string>() == "final").ToArray()).IsEmpty();
            const string feedback = "rejected-probe-continue";
            (await lab.CommandAsync(["reply", lab.Run, askId, "reject", "--message", feedback], ct)).RequireSuccess("reject live ask");
            var result = await lab.CompleteAsync(running);
            await lab.AssertDoneAsync(result);
            var permission = lab.Events().Single(x => x["type"]!.GetValue<string>() == "permission" && x["data"]!["id"]!.GetValue<string>() == askId);
            await Assert.That(permission["data"]!["answer"]!.GetValue<string>()).IsEqualTo("reject");
            await Assert.That(permission["data"]!["by"]!.GetValue<string>()).IsEqualTo("operator");
            await Assert.That(File.ReadAllText(Path.Combine(lab.Workspace, "continued.txt")).TrimEnd('\r', '\n')).IsEqualTo(feedback);
            await Assert.That(File.Exists(outside)).IsFalse();
            await Assert.That(lab.Patch).Contains("+" + feedback);
        }
        finally { process.Kill(); await running; }
    }

    [Test]
    [Arguments("codex")]
    [Arguments("opencode")]
    [Timeout(360_000)]
    public async Task FN9_Cancel_then_collect_recovers_result_and_leaves_no_worker_process(string backend, CancellationToken ct)
    {
        using var lab = await FunctionalLab.CreateAsync(backend, ct);
        var sleep = OperatingSystem.IsWindows() ? "Start-Sleep -Seconds 120" : "sleep 120";
        lab.WriteTask(lab.CodeTask($"Create started.txt with exactly cancel-ready and no newline, then run `{sleep}` in the shell and wait for it before doing anything else."));
        using var process = new HarnessProcess(lab.Tree);
        var running = lab.StartAsync(process, ct);
        var observed = new List<ObservedProcess>();
        try
        {
            await lab.WaitForEventAsync("prompt", running, ct, _ =>
                File.Exists(Path.Combine(lab.Workspace, "started.txt")));
            observed.AddRange(await FunctionalProcesses.DescendantsAsync(process.ProcessId, lab.Tree, ct));
            await Assert.That(observed).IsNotEmpty();
            await Assert.That(running.IsCompleted).IsFalse();
            await Assert.That(File.ReadAllText(Path.Combine(lab.Workspace, "started.txt")).TrimEnd('\r', '\n')).IsEqualTo("cancel-ready");
            (await lab.CommandAsync(["cancel", lab.Run, "probe"], ct)).RequireSuccess("cancel live task");
            var stopped = await lab.CompleteAsync(running);
            await Assert.That(stopped.ExitCode).IsEqualTo(1);
            await Assert.That(lab.Result["state"]!.GetValue<string>()).IsEqualTo("cancelled");
            await Assert.That(lab.Result["cancelRequested"]!.GetValue<bool>()).IsTrue();
            var collected = await lab.CommandAsync(["collect", lab.Run, "probe"], ct);
            await Assert.That(collected.ExitCode).IsEqualTo(1);
            await Assert.That(lab.Result["state"]!.GetValue<string>()).IsEqualTo("cancelled");
            await Assert.That(lab.Result["failures"]!.AsArray().Select(x => x!.GetValue<string>()).ToArray()).DoesNotContain("zombie");
            var final = lab.Events().Last(x => x["type"]!.GetValue<string>() == "final");
            await Assert.That(final["data"]!["via"]!.GetValue<string>()).IsEqualTo("collect");
            await Assert.That(lab.Patch).Contains("+cancel-ready");
            await Assert.That(File.ReadAllText(Path.Combine(lab.Workspace, "started.txt")).TrimEnd('\r', '\n')).IsEqualTo("cancel-ready");
            foreach (var identity in observed)
            {
                TestContext.Current!.Output.WriteLine($"Observed worker pid {identity.Pid}, start {identity.StartTime}: alive={identity.IsAlive()}");
                await Assert.That(identity.IsAlive()).IsFalse();
            }
        }
        finally
        {
            process.Kill();
            try { await running; }
            finally { foreach (var identity in observed.AsEnumerable().Reverse()) await identity.CleanupAsync(); }
        }
    }

    static bool IsFailedTool(JsonObject entry) => entry["type"]!.GetValue<string>() == "tool"
        && entry["data"]?["outcome"]?.GetValue<string>() == "failed";
}
