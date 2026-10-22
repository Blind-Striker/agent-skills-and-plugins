using System.Text.Json.Nodes;
using Dispatch.Tests.Support;

namespace Dispatch.Tests.Integration;

[Category("Integration")]
public sealed class CodexBackendTests
{
    [Test]
    [Timeout(180_000)]
    public async Task Unknown_internal_access_is_refused_instead_of_requesting_full_access(CancellationToken cancellationToken)
    {
        using var lab = new CodexLab();
        lab.Write("preflight", CodexLab.Handshake + CodexLab.Thread.Replace("workspaceWrite", "dangerFullAccess"));
        await using var backend = lab.Backend();
        var modes = new Modes("full", "on", "workspace", "on", "worktree");
        // Bypass the contract guard to exercise the backend's independent fail-closed boundary.
        typeof(Modes).GetField("<Access>k__BackingField", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(modes, "unknown");
        var failure = await Assert.ThrowsAsync<HarnessException>(() => backend.CreateAsync(new("run", "edit", "code", new("codex", "model-y", "high"), null, null,
            modes, lab.Tree.Repository, new([], []), lab.Environment, null, "full", null), cancellationToken));
        await Assert.That(failure!.Message).Contains("access");
        if (Directory.Exists(lab.Records))
            await Assert.That(lab.Input().Where(x => x["method"]?.GetValue<string>() is "thread/start" or "turn/start")).IsEmpty();
    }

    [Test]
    [Arguments("routes")]
    [Arguments("selfcheck")]
    [Timeout(180_000)]
    public async Task Catalog_commands_select_Codex_from_the_committed_repository_config(string command, CancellationToken cancellationToken)
    {
        using var lab = new CodexLab();
        var repo = await LabRepo.CreateAsync(lab.Tree, cancellationToken);
        var userPath = Path.Combine(lab.Tree.DispatchHome, "config", "config.jsonc");
        var user = JsonNode.Parse(File.ReadAllText(userPath))!;
        user["preferences"]!.AsObject().Remove("backend");
        File.WriteAllText(userPath, user.ToJsonString());
        var repositoryPath = Path.Combine(Directory.CreateDirectory(Path.Combine(repo.Root, ".dispatch")).FullName, "config.jsonc");
        File.WriteAllText(repositoryPath, """{"schemaVersion":2,"preferences":{"backend":"codex"}}""");
        await repo.GitAsync(["add", ".dispatch/config.jsonc"], cancellationToken);
        await repo.GitAsync(["commit", "-qm", "Configure backend"], cancellationToken);
        File.WriteAllText(repositoryPath, """{"schemaVersion":2,"preferences":{"backend":"opencode"}}""");
        using var harness = new HarnessProcess(lab.Tree);
        var result = await harness.RunAsync([command, "--repo", repo.Root], cancellationToken,
            lab.Environment.ToDictionary(x => x.Key, x => (string?)x.Value));
        result.RequireSuccess("repository-selected catalog command");
        await Assert.That(result.Stdout).Contains("backend codex");
        await Assert.That(result.Stdout).Contains(command == "routes" ? "codex:model-y" : "Codex 0.163.0");
        await Assert.That(lab.Input().Where(x => x["method"]?.GetValue<string>() is "thread/start" or "turn/start")).IsEmpty();
    }

    [Test]
    [Timeout(180_000)]
    public async Task Stale_main_turn_approval_is_declined_without_admitting_an_ask(CancellationToken cancellationToken)
    {
        using var lab = new CodexLab();
        lab.Write("preflight", CodexLab.Handshake + CodexLab.Thread + """
            {"request":"item/commandExecution/requestApproval","params":{"threadId":"$threadId","turnId":"old-turn","itemId":"old-call","command":"old-command"},"result":{"decision":"decline"}}

            """ + CodexLab.Final);
        await using var backend = lab.Backend();
        var created = await backend.CreateAsync(new("run", "edit", "code", new("codex", "model-y", "high"), null, null,
            new("full", "on", "workspace", "on", "worktree"), lab.Tree.Repository, new([], []), lab.Environment, null, "full", null), cancellationToken);
        var worker = await backend.PromptAsync(created.Worker, new("Perform the task.", false), cancellationToken);
        WorkerSignal signal;
        do { signal = await backend.Signals.ReadAsync(cancellationToken); }
        while (signal is not (WorkerSignal.Ask or WorkerSignal.Ended));
        await Assert.That(signal).IsTypeOf<WorkerSignal.Ended>();
        await Assert.That((await backend.CollectAsync(worker, cancellationToken)).FinalText).IsEqualTo("Completed.");
        await Assert.That(lab.Input().Single(x => x.ContainsKey("result"))["result"]!["decision"]!.GetValue<string>()).IsEqualTo("decline");
        await Assert.That(lab.Input().Where(x => x["method"]?.GetValue<string>() == "turn/steer")).IsEmpty();
    }

    [Test]
    [Arguments("usage")]
    [Arguments("reroute")]
    [Arguments("error")]
    [Timeout(180_000)]
    public async Task Stale_main_turn_events_do_not_mutate_the_current_report_or_signals(string kind, CancellationToken cancellationToken)
    {
        using var lab = new CodexLab();
        var stale = kind switch
        {
            "usage" => """{"notify":"thread/tokenUsage/updated","params":{"threadId":"$threadId","turnId":"old-turn","tokenUsage":{"total":{"inputTokens":900,"outputTokens":900}}}}""",
            "reroute" => """{"notify":"model/rerouted","params":{"threadId":"$threadId","turnId":"old-turn","toModel":"stale-model","fromModel":"model-y","reason":"capacity"}}""",
            _ => """{"notify":"error","params":{"threadId":"$threadId","turnId":"old-turn","willRetry":true,"error":{"message":"stale error"}}}""",
        };
        lab.Write("preflight", CodexLab.Handshake + CodexLab.Thread + """
            {"notify":"thread/tokenUsage/updated","params":{"threadId":"$threadId","turnId":"$turnId","tokenUsage":{"total":{"inputTokens":10,"outputTokens":2}}}}

            """ + stale + "\n" + CodexLab.Final);
        await using var backend = lab.Backend();
        var created = await backend.CreateAsync(new("run", "edit", "code", new("codex", "model-y", "high"), null, null,
            new("full", "on", "workspace", "on", "worktree"), lab.Tree.Repository, new([], []), lab.Environment, null, "full", null), cancellationToken);
        var worker = await backend.PromptAsync(created.Worker, new("Perform the task.", false), cancellationToken);
        var report = await backend.CollectAsync(worker, cancellationToken);
        await Assert.That(report.Usage.Input).IsEqualTo(10);
        await Assert.That(report.Usage.Output).IsEqualTo(2);
        await Assert.That(report.RoutesUsed).IsEquivalentTo(new[] { "codex:model-y#high" });
        var signals = new List<WorkerSignal>();
        while (backend.Signals.TryRead(out var signal)) signals.Add(signal);
        await Assert.That(signals.OfType<WorkerSignal.Fault>()).IsEmpty();
        await Assert.That(signals.OfType<WorkerSignal.Retry>()).IsEmpty();
        await Assert.That(report.FinalText).IsEqualTo("Completed.");
    }

    [Test]
    [Timeout(180_000)]
    public async Task Preflight_resolves_a_configured_command_shim_with_spaces_on_Windows(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows()) Skip.Test("Windows command shims only");
        using var lab = new CodexLab();
        var folder = Directory.CreateDirectory(Path.Combine(lab.Tree.Root, "shim with spaces & punctuation")).FullName;
        var shim = Path.Combine(folder, "codex.cmd");
        File.WriteAllText(shim, $"@echo off\r\n\"{CodexLab.Executable}\" %*\r\n");
        await using var backend = new CodexBackend(new(Path.Combine(lab.Tree.Root, "profiles"), shim, new Dictionary<string, string>()), lab.Environment);
        await Assert.That((await backend.ProbeAsync(new(lab.Tree.Repository), cancellationToken)).Ready).IsTrue();
    }

    [Test]
    [Arguments("crash", "outcome:lost")]
    [Arguments("no-final", "no-final-text")]
    [Timeout(180_000)]
    public async Task Missing_final_text_or_an_app_server_crash_settles_failed(string scenario, string failure, CancellationToken cancellationToken)
    {
        using var lab = new CodexLab();
        var repo = await LabRepo.CreateAsync(lab.Tree, cancellationToken);
        lab.CodeTask();
        lab.Write("task", CodexLab.Handshake + CodexLab.Thread + (scenario == "crash" ? """
            {"stderr":"synthetic crash detail\n"}
            {"exit":17}
            """ : """
            {"notify":"turn/completed","params":{"threadId":"$threadId","turn":{"id":"$turnId","status":"completed"}}}
            """));
        using var harness = new HarnessProcess(lab.Tree);
        var result = await harness.RunAsync(["run", lab.Tasks, "--repo", repo.Root], cancellationToken,
            lab.Environment.ToDictionary(x => x.Key, x => (string?)x.Value));
        await Assert.That(result.ExitCode).IsEqualTo(1);
        await Assert.That(lab.Result["state"]!.GetValue<string>()).IsEqualTo("failed");
        await Assert.That(lab.Result["failures"]!.AsArray().Select(x => x!.GetValue<string>())).Contains(failure);
        if (scenario == "crash") await Assert.That(lab.Result["executionError"]!.GetValue<string>()).Contains("synthetic crash detail");
    }

    [Test]
    [Timeout(180_000)]
    public async Task Resolved_approval_closes_the_blocker_without_sending_a_late_response(CancellationToken cancellationToken)
    {
        using var lab = new CodexLab();
        lab.Write("preflight", CodexLab.Handshake + CodexLab.Thread + """
            {"request":"item/commandExecution/requestApproval","defer":true,"params":{"threadId":"$threadId","turnId":"$turnId","itemId":"call","command":"outside-command"}}
            {"expect":"turn/interrupt","reply":{}}
            {"notify":"serverRequest/resolved","params":{"threadId":"$threadId","requestId":"server-1"}}
            {"expect":"turn/interrupt","reply":{}}
            {"notify":"turn/completed","params":{"threadId":"$threadId","turn":{"id":"$turnId","status":"interrupted"}}}
            """);
        await using var backend = lab.Backend();
        var created = await backend.CreateAsync(new("run", "edit", "code", new("codex", "model-y", "high"), null, null,
            new("full", "on", "workspace", "on", "worktree"), lab.Tree.Repository, new([], []), lab.Environment, null, "full", null), cancellationToken);
        var worker = await backend.PromptAsync(created.Worker, new("Perform the task.", false), cancellationToken);
        WorkerSignal.Ask? ask = null;
        while (ask is null) ask = await backend.Signals.ReadAsync(cancellationToken) as WorkerSignal.Ask;
        await backend.InterruptAsync(worker, InterruptKind.Cancel, cancellationToken);
        WorkerSignal.AskClosed? closed = null;
        while (closed is null) closed = await backend.Signals.ReadAsync(cancellationToken) as WorkerSignal.AskClosed;
        await Assert.That(closed.AskId).IsEqualTo(ask.BlockerRef.AskId);
        await Assert.That((await backend.AnswerAsync(ask.BlockerRef, new BlockerAnswer.Once(), cancellationToken)).Confirmed).IsFalse();
        await backend.InterruptAsync(worker, InterruptKind.Cancel, cancellationToken);
        await Assert.That((await backend.CollectAsync(worker, cancellationToken)).Outcome).IsEqualTo("interrupted");
        await Assert.That(lab.Input().Where(x => x.ContainsKey("result") || x.ContainsKey("error"))).IsEmpty();
    }

    [Test]
    [Timeout(180_000)]
    public async Task Validate_refuses_a_kind_the_backend_does_not_serve(CancellationToken cancellationToken)
    {
        using var lab = new CodexLab();
        await using var backend = lab.Backend();
        var checks = await backend.ValidateAsync([new("edit", "unsupported", new("codex", "model-y", "high"), null, null,
            new("full", "on", "workspace", "on", "worktree"), lab.Tree.Repository)], cancellationToken);
        await Assert.That(checks.Single().Errors).Contains(x => x.Contains("unsupported", StringComparison.Ordinal));
        await Assert.That(lab.Input().Where(x => x["method"]?.GetValue<string>() is "thread/start" or "turn/start")).IsEmpty();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    [Timeout(180_000)]
    public async Task Resume_of_an_interrupted_Codex_task_settles_lost_without_a_new_prompt(bool proceed, CancellationToken cancellationToken)
    {
        using var lab = new CodexLab();
        var repo = await LabRepo.CreateAsync(lab.Tree, cancellationToken);
        lab.CodeTask();
        lab.Write("task", CodexLab.Handshake + CodexLab.Thread + """
            {"notify":"turn/completed","params":{"threadId":"$threadId","turn":{"id":"$turnId","status":"interrupted"}}}
            """);
        using var first = new HarnessProcess(lab.Tree);
        var initial = await first.RunAsync(["run", lab.Tasks, "--repo", repo.Root], cancellationToken,
            lab.Environment.ToDictionary(x => x.Key, x => (string?)x.Value));
        await Assert.That(initial.ExitCode).IsEqualTo(1);
        var runId = Path.GetFileName(lab.RunFolder);
        using var resume = new HarnessProcess(lab.Tree);
        var resumed = await resume.RunAsync(proceed ? ["resume", runId, "--continue"] : ["resume", runId], cancellationToken,
            lab.Environment.ToDictionary(x => x.Key, x => (string?)x.Value));
        await Assert.That(resumed.ExitCode).IsEqualTo(1);
        await Assert.That(File.Exists(Path.Combine(lab.RunFolder, "edit", "result.json"))).IsTrue();
        await Assert.That(lab.Result["outcome"]!.GetValue<string>()).IsEqualTo("lost");
        await Assert.That(lab.Result["state"]!.GetValue<string>()).IsEqualTo("failed");
        await Assert.That(lab.Input().Count(x => x["method"]?.GetValue<string>() == "turn/start")).IsEqualTo(1);
    }

    [Test]
    [Arguments("routes")]
    [Arguments("selfcheck")]
    [Timeout(180_000)]
    public async Task Catalog_commands_cover_the_selected_Codex_backend_without_prompting(string command, CancellationToken cancellationToken)
    {
        using var lab = new CodexLab();
        var repo = await LabRepo.CreateAsync(lab.Tree, cancellationToken);
        using var harness = new HarnessProcess(lab.Tree);
        var result = await harness.RunAsync([command, "--backend", "codex", "--repo", repo.Root], cancellationToken,
            lab.Environment.ToDictionary(x => x.Key, x => (string?)x.Value));
        result.RequireSuccess("Codex catalog command");
        await Assert.That(result.Stdout).Contains(command == "routes" ? "codex:model-y" : "Codex 0.163.0");
        await Assert.That(lab.Input().Where(x => x["method"]?.GetValue<string>() is "thread/start" or "turn/start")).IsEmpty();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    [Timeout(180_000)]
    public async Task Clean_profile_is_probed_and_its_actual_mode_is_frozen(bool wrongHome, CancellationToken cancellationToken)
    {
        using var lab = new CodexLab();
        var repo = await LabRepo.CreateAsync(lab.Tree, cancellationToken);
        File.WriteAllText(Path.Combine(lab.Environment["CODEX_HOME"], "auth.json"), "{\"synthetic\":true}");
        lab.CodeTask();
        var task = JsonNode.Parse(File.ReadAllText(lab.Tasks))!;
        task["tasks"]![0]!["profile"] = "clean";
        File.WriteAllText(lab.Tasks, task.ToJsonString());
        if (wrongHome)
        {
            var path = Path.Combine(lab.Scenarios, "probe.jsonl");
            File.WriteAllText(path, File.ReadAllText(path).Replace("\"codexHome\":\"$codexHome\"", "\"codexHome\":\"$cwd\""));
        }
        lab.Write("task", CodexLab.Handshake + CodexLab.Thread + CodexLab.Final);
        using var harness = new HarnessProcess(lab.Tree);
        (await harness.RunAsync(["run", lab.Tasks, "--repo", repo.Root], cancellationToken,
            lab.Environment.ToDictionary(x => x.Key, x => (string?)x.Value))).RequireSuccess("clean code run");
        var starts = Directory.GetFiles(lab.Records, "start.json", SearchOption.AllDirectories).Select(x => JsonNode.Parse(File.ReadAllText(x))!).ToArray();
        await Assert.That(starts.Count(x => x["scenario"]!.GetValue<string>() == "probe.jsonl")).IsEqualTo(1);
        var frozen = JsonNode.Parse(File.ReadAllText(Path.Combine(lab.RunFolder, "config.json")))!["tasks"]!["edit"]!["settings"]!["profile"]!;
        await Assert.That(frozen["actual"]!.GetValue<string>()).IsEqualTo(wrongHome ? "clean-partial" : "clean");
        var record = JsonNode.Parse(File.ReadAllText(Path.Combine(lab.RunFolder, "edit", "task.json")))!;
        await Assert.That(record["settings"]!["profile"]!["actual"]!.GetValue<string>()).IsEqualTo(wrongHome ? "clean-partial" : "clean");
        if (wrongHome) await Assert.That(frozen["reason"]!.GetValue<string>()).Contains("marker");
        var workerHome = record["backendState"]!["codexHome"]!.GetValue<string>();
        if (wrongHome) await Assert.That(workerHome).IsEqualTo(lab.Environment["CODEX_HOME"]);
        else
        {
            await Assert.That(Path.GetFileName(workerHome)).IsEqualTo("codex-edit");
            await Assert.That(Directory.Exists(workerHome)).IsFalse();
        }
    }

    [Test]
    [Timeout(180_000)]
    public async Task Notifications_keep_the_task_turn_final_and_sum_latest_usage_per_thread(CancellationToken cancellationToken)
    {
        using var lab = new CodexLab();
        lab.Write("preflight", CodexLab.Handshake + CodexLab.Thread + """
            {"notify":"turn/started","params":{"threadId":"$threadId","turn":{"id":"$turnId","status":"inProgress"}}}
            {"notify":"item/started","params":{"threadId":"$otherThreadId","turnId":"child-turn","item":{"id":"call","type":"commandExecution","command":"echo progress"},"startedAtMs":1000}}
            {"notify":"item/completed","params":{"threadId":"$otherThreadId","turnId":"child-turn","item":{"id":"call","type":"commandExecution","command":"echo progress","status":"completed","exitCode":0},"completedAtMs":2000}}
            {"notify":"error","params":{"threadId":"$threadId","turnId":"$turnId","willRetry":true,"error":{"message":"retrying"}}}
            {"notify":"thread/tokenUsage/updated","params":{"threadId":"$threadId","turnId":"$turnId","tokenUsage":{"total":{"inputTokens":10,"outputTokens":2,"reasoningOutputTokens":1,"cachedInputTokens":3,"cacheWriteInputTokens":4}}}}
            {"notify":"thread/tokenUsage/updated","params":{"threadId":"$threadId","turnId":"$turnId","tokenUsage":{"total":{"inputTokens":20,"outputTokens":4,"reasoningOutputTokens":2,"cachedInputTokens":6,"cacheWriteInputTokens":8}}}}
            {"notify":"thread/tokenUsage/updated","params":{"threadId":"$otherThreadId","turnId":"child-turn","tokenUsage":{"total":{"inputTokens":5,"outputTokens":3,"reasoningOutputTokens":1,"cachedInputTokens":2,"cacheWriteInputTokens":1}}}}
            {"notify":"model/rerouted","params":{"threadId":"$otherThreadId","turnId":"child-turn","toModel":"model-z","fromModel":"model-y","reason":"capacity"}}
            {"notify":"turn/completed","params":{"threadId":"$otherThreadId","turn":{"id":"child-turn","status":"failed"}}}
            {"notify":"turn/completed","params":{"threadId":"$threadId","turn":{"id":"old-turn","status":"failed"}}}
            {"notify":"item/completed","params":{"threadId":"$threadId","turnId":"$turnId","item":{"id":"answer","type":"agentMessage","phase":"final_answer","text":"Final answer."}}}
            {"notify":"item/completed","params":{"threadId":"$threadId","turnId":"$turnId","item":{"id":"comment","type":"agentMessage","phase":"commentary","text":"Later commentary."}}}
            {"notify":"item/completed","params":{"threadId":"$otherThreadId","turnId":"child-turn","item":{"id":"child-answer","type":"agentMessage","phase":"final_answer","text":"Child answer."}}}
            {"notify":"turn/completed","params":{"threadId":"$threadId","turn":{"id":"$turnId","status":"completed"}}}
            """);
        await using var backend = lab.Backend();
        var created = await backend.CreateAsync(new("run", "edit", "code", new("codex", "model-y", "high"), null, null,
            new("full", "on", "workspace", "on", "worktree"), lab.Tree.Repository, new([], []), lab.Environment, null, "full", null), cancellationToken);
        var worker = await backend.PromptAsync(created.Worker, new("Perform the task.", false), cancellationToken);
        var report = await backend.CollectAsync(worker, cancellationToken);
        await Assert.That(report.FinalText).IsEqualTo("Final answer.");
        await Assert.That(report.Usage.Input).IsEqualTo(25);
        await Assert.That(report.Usage.Output).IsEqualTo(7);
        await Assert.That(report.Usage.Reasoning).IsEqualTo(3);
        await Assert.That(report.Usage.CacheRead).IsEqualTo(8);
        await Assert.That(report.Usage.CacheWrite).IsEqualTo(9);
        await Assert.That(report.UsageComplete).IsTrue();
        await Assert.That(report.RoutesUsed).IsEquivalentTo(new[] { "codex:model-y#high", "codex:model-z#high" });
        var signals = new List<WorkerSignal>();
        while (backend.Signals.TryRead(out var signal)) signals.Add(signal);
        await Assert.That(signals.OfType<WorkerSignal.Ended>().Select(x => x.Outcome)).IsEquivalentTo(new[] { "succeeded" });
        await Assert.That(signals.OfType<WorkerSignal.Ended>().Single().Turn).IsEqualTo("turn-main");
        await Assert.That(signals.OfType<WorkerSignal.Retry>().Single().Message).IsEqualTo("retrying");
        await Assert.That(signals.OfType<WorkerSignal.ToolCall>().Select(x => x.Marker)).IsEquivalentTo(new[] { "start", "end" });
        await Assert.That(signals.OfType<WorkerSignal.ToolCall>().Single(x => x.Marker == "end").Outcome).IsEqualTo("succeeded");
        await Assert.That(signals.OfType<WorkerSignal.Status>().Select(x => x.Execution)).Contains("succeeded");
    }

    [Test]
    [Arguments("code", "workspace", "on", false)]
    [Arguments("mechanical", "workspace", "off", false)]
    [Arguments("review", "read-only", "on", false)]
    [Arguments("research", "read-only", "off", false)]
    [Arguments("structured", "read-only", "on", false)]
    [Arguments("structured", "read-only", "off", true)]
    [Arguments("code", "full", "on", false)]
    [Timeout(180_000)]
    public async Task Modes_forward_sandbox_network_schema_prompt_and_catalog_default_effort(string kind, string access, string network, bool contracts, CancellationToken cancellationToken)
    {
        using var lab = new CodexLab();
        var preflight = Path.Combine(lab.Scenarios, "preflight.jsonl");
        File.WriteAllText(preflight, File.ReadAllText(preflight).Replace("\"defaultReasoningEffort\":\"high\"", "\"defaultReasoningEffort\":\"low\""));
        var sandboxType = access switch { "read-only" => "readOnly", "workspace" => "workspaceWrite", _ => "dangerFullAccess" };
        var sandboxMode = access switch { "read-only" => "read-only", "workspace" => "workspace-write", _ => "danger-full-access" };
        lab.Write("task", CodexLab.Handshake + CodexLab.Thread.Replace("workspaceWrite", sandboxType) + CodexLab.Final);
        await using var backend = lab.Backend();
        var route = new RouteRef("codex", "model-y", null);
        var spec = new TaskSpec { Id = "edit", Kind = kind, Access = access == "full" ? "full" : null, Network = network,
            Input = new() { Items = JsonNode.Parse("[{\"id\":\"a\",\"text\":\"İyi günler 世界\"}]")!.AsArray() }, Result = new() { ItemsAt = "/items" } };
        var resolved = ConfigResolver.Resolve(new TaskFile { Tasks = [spec] }, new ConfigFile
        {
            Preferences = new() { Backend = "codex", DefaultRoutes = new() { ["codex"] = "model-y" } },
        }).Tasks["edit"].Settings;
        await Assert.That(resolved.Access!.From).IsEqualTo(access == "full" ? "task" : "tool");
        var modes = new Modes(resolved.Profile!.Value!, resolved.Project!.Value!, resolved.Access.Value!, resolved.Network!.Value!, resolved.Workspace!.Value!);
        var checks = await backend.ValidateAsync([new("edit", kind, route, null, null, modes, lab.Tree.Repository)], cancellationToken);
        await Assert.That(checks.Single().Errors).IsEmpty();
        var schema = JsonNode.Parse("{\"type\":\"object\",\"properties\":{\"items\":{\"type\":\"array\"}},\"required\":[\"items\"],\"additionalProperties\":false}")!.AsObject();
        var promptInputs = new Prompt.Inputs(contracts ? [new(new(), "First contract\r\n"), new(new(), "Second contract")] : [], spec.Input.Items, schema);
        var text = new Policy { Path = "config.json", Deny = [], Escalate = [], Instructions = [] }.Render(new(spec, "Perform the task."), lab.Tree.Repository, promptInputs);
        var created = await backend.CreateAsync(new("run", "edit", kind, route, null, null, modes, lab.Tree.Repository, new([], []), lab.Environment,
            kind == "structured" ? schema : null, checks.Single().Profile, checks.Single().ProfileReason), cancellationToken);
        var worker = await backend.PromptAsync(created.Worker, new(text, false), cancellationToken);
        var report = await backend.CollectAsync(worker, cancellationToken);
        await Assert.That(report.Outcome).IsEqualTo("succeeded");
        var turn = lab.Input().Single(x => x["method"]?.GetValue<string>() == "turn/start")["params"]!;
        await Assert.That(turn["effort"]!.GetValue<string>()).IsEqualTo("low");
        await Assert.That(turn["input"]!.AsArray().Count).IsEqualTo(1);
        await Assert.That(turn["input"]![0]!["text"]!.GetValue<string>()).IsEqualTo(text);
        await Assert.That(turn["cwd"]!.GetValue<string>()).IsEqualTo(lab.Tree.Repository);
        await Assert.That(JsonNode.DeepEquals(turn["outputSchema"], kind == "structured" ? schema : null)).IsTrue();
        await Assert.That(turn["sandboxPolicy"]!["type"]!.GetValue<string>()).IsEqualTo(sandboxType);
        if (access == "full") await Assert.That(turn["sandboxPolicy"]!.AsObject().ContainsKey("networkAccess")).IsFalse();
        else await Assert.That(turn["sandboxPolicy"]!["networkAccess"]!.GetValue<bool>()).IsEqualTo(network == "on");
        if (access == "workspace") await Assert.That(turn["sandboxPolicy"]!["writableRoots"]![0]!.GetValue<string>()).IsEqualTo(lab.Tree.Repository);
        var thread = lab.Input().Single(x => x["method"]?.GetValue<string>() == "thread/start")["params"]!;
        await Assert.That(thread["model"]!.GetValue<string>()).IsEqualTo("model-y");
        await Assert.That(thread["sandbox"]!.GetValue<string>()).IsEqualTo(sandboxMode);
        await Assert.That(thread["approvalPolicy"]!.GetValue<string>()).IsEqualTo("on-request");
        await Assert.That(thread["approvalsReviewer"]!.GetValue<string>()).IsEqualTo("user");
        await Assert.That(thread["ephemeral"]!.GetValue<bool>()).IsTrue();
        await Assert.That(thread.AsObject().ContainsKey("baseInstructions")).IsFalse();
        await Assert.That(thread.AsObject().ContainsKey("developerInstructions")).IsFalse();
        var trust = thread["config"]!["projects"]!.AsObject().Single().Value!["trust_level"]!.GetValue<string>();
        await Assert.That(trust).IsEqualTo(kind == "structured" ? "untrusted" : "trusted");
        await Assert.That(created.Applied.Access).IsEqualTo(access == "full" ? "none" : "sandbox");
        await Assert.That(created.Applied.Rules).IsEqualTo("approval-gate");
        await Assert.That(report.RoutesUsed).Contains("codex:model-y#low");
        if (contracts) await Assert.That(text).StartsWith("First contract\r\n\nSecond contract\n\n");
    }

    [Test]
    [Arguments("commandExecution", false)]
    [Arguments("fileChange", false)]
    [Arguments("commandExecution", true)]
    [Arguments("fileChange", true)]
    [Timeout(180_000)]
    public async Task Approvals_default_to_decline_and_steer_or_wait_for_operator_once(string tool, bool escalate, CancellationToken cancellationToken)
    {
        using var lab = new CodexLab();
        var repo = await LabRepo.CreateAsync(lab.Tree, cancellationToken);
        lab.CodeTask();
        if (escalate)
        {
            var tasks = JsonNode.Parse(File.ReadAllText(lab.Tasks))!;
            tasks["policy"] = JsonNode.Parse("{\"escalate\":[{\"name\":\"operator\",\"action\":\"*\",\"resources\":[\"*\"]}]}");
            File.WriteAllText(lab.Tasks, tasks.ToJsonString());
        }
        lab.Write("task", CodexLab.Handshake + CodexLab.Thread + """
            {"notify":"item/started","params":{"threadId":"$otherThreadId","turnId":"child-turn","item":{"id":"call","type":"fileChange","changes":[{"path":"$cwd/a.txt"}]}}}
            {"request":"item/TOOL/requestApproval","params":{"threadId":"$otherThreadId","turnId":"child-turn","itemId":"call","command":"outside-command","grantRoot":"$outside"},"result":{"decision":"DECISION"}}

            """.Replace("TOOL", tool).Replace("DECISION", escalate ? "accept" : "decline")
            + (escalate ? "" : """
                {"expect":"turn/steer","params":{"threadId":"$otherThreadId","expectedTurnId":"child-turn"},"reply":{}}

                """) + CodexLab.Final);
        using var harness = new HarnessProcess(lab.Tree);
        var running = harness.RunAsync(["run", lab.Tasks, "--repo", repo.Root], cancellationToken,
            lab.Environment.ToDictionary(x => x.Key, x => (string?)x.Value));
        if (escalate)
        {
            var asks = Array.Empty<string>();
            var controlRoot = Path.Combine(lab.Tree.DispatchHome, "state", "control");
            while (!running.IsCompleted && asks.Length == 0)
            {
                asks = Directory.Exists(controlRoot) ? Directory.GetDirectories(controlRoot)
                    .Where(path => Locations.IsControlId(Path.GetFileName(path)))
                    .Select(path => Path.Combine(path, "asks", "edit-1.json"))
                    .Where(File.Exists).ToArray() : [];
                if (asks.Length == 0) await Task.Delay(20, cancellationToken);
            }
            await Assert.That(asks.Length).IsEqualTo(1);
            var ask = JsonNode.Parse(File.ReadAllText(asks[0]))!;
            await Assert.That(lab.Input().Where(x => x.ContainsKey("result"))).IsEmpty();
            using var reply = new HarnessProcess(lab.Tree);
            var answered = await reply.RunAsync(["reply", ask["run"]!.GetValue<string>(), "edit-1", "once"], cancellationToken);
            answered.RequireSuccess("operator reply");
        }
        (await running).RequireSuccess("approval run");
        var answer = lab.Input().Single(x => x.ContainsKey("result"));
        await Assert.That(answer["result"]!["decision"]!.GetValue<string>()).IsEqualTo(escalate ? "accept" : "decline");
        await Assert.That(string.Join('\n', lab.Input().Select(x => x.ToJsonString()))).DoesNotContain("acceptForSession");
        if (!escalate)
            await Assert.That(lab.Input().Single(x => x["method"]?.GetValue<string>() == "turn/steer")["params"]!["input"]![0]!["text"]!.GetValue<string>())
                .IsEqualTo("The approval request was rejected. Rejection message:\n" + Policy.LeadDeclined);
    }

    [Test]
    [Arguments("commandExecution")]
    [Arguments("fileChange")]
    [Timeout(180_000)]
    public async Task Operator_rejection_steer_identifies_the_exact_feedback_and_task_continues(string tool, CancellationToken cancellationToken)
    {
        using var lab = new CodexLab();
        var repo = await LabRepo.CreateAsync(lab.Tree, cancellationToken);
        lab.CodeTask();
        var tasks = JsonNode.Parse(File.ReadAllText(lab.Tasks))!;
        tasks["policy"] = JsonNode.Parse("{\"escalate\":[{\"name\":\"operator\",\"action\":\"*\",\"resources\":[\"*\"]}]}");
        File.WriteAllText(lab.Tasks, tasks.ToJsonString());
        lab.Write("task", CodexLab.Handshake + CodexLab.Thread + """
            {"request":"item/TOOL/requestApproval","params":{"threadId":"$threadId","turnId":"$turnId","itemId":"call","command":"outside-command","grantRoot":"$outside"},"result":{"decision":"decline"}}
            {"expect":"turn/steer","params":{"threadId":"$threadId","expectedTurnId":"$turnId"},"reply":{}}
            {"writeFile":{"path":"$cwd/a.txt","base64":"Y29udGludWVkCg=="}}

            """.Replace("TOOL", tool) + CodexLab.Final);
        using var harness = new HarnessProcess(lab.Tree);
        var running = harness.RunAsync(["run", lab.Tasks, "--repo", repo.Root], cancellationToken,
            lab.Environment.ToDictionary(x => x.Key, x => (string?)x.Value));
        try
        {
            var controlRoot = Path.Combine(lab.Tree.DispatchHome, "state", "control");
            string? askPath = null;
            while (!running.IsCompleted && askPath is null)
            {
                askPath = Directory.Exists(controlRoot) ? Directory.GetDirectories(controlRoot)
                    .Where(path => Locations.IsControlId(Path.GetFileName(path)))
                    .Select(path => Path.Combine(path, "asks", "edit-1.json"))
                    .SingleOrDefault(File.Exists) : null;
                if (askPath is null) await Task.Delay(20, cancellationToken);
            }
            await Assert.That(askPath).IsNotNull();
            var ask = JsonNode.Parse(File.ReadAllText(askPath!))!;
            await Assert.That(lab.Input().Where(x => x.ContainsKey("result"))).IsEmpty();
            const string feedback = "Leave that action denied.\nContinue with the local report: İyi günler.";
            using var reply = new HarnessProcess(lab.Tree);
            (await reply.RunAsync(["reply", ask["run"]!.GetValue<string>(), "edit-1", "reject", "--message", feedback], cancellationToken))
                .RequireSuccess("operator rejection");
            (await running).RequireSuccess("continued rejection run");
            var wire = lab.Input();
            var decline = wire.Single(x => x.ContainsKey("result"));
            await Assert.That(decline["result"]!["decision"]!.GetValue<string>()).IsEqualTo("decline");
            var steer = wire.Single(x => x["method"]?.GetValue<string>() == "turn/steer");
            await Assert.That(steer["params"]!["input"]!.AsArray().Count).IsEqualTo(1);
            await Assert.That(steer["params"]!["input"]![0]!["text"]!.GetValue<string>())
                .IsEqualTo("The approval request was rejected. Rejection message:\n" + feedback);
            await Assert.That(Array.IndexOf(wire, steer)).IsGreaterThan(Array.IndexOf(wire, decline));
            await Assert.That(lab.Result["state"]!.GetValue<string>()).IsEqualTo("done");
            await Assert.That(await repo.GitAsync(["show", lab.Result["commit"]!.GetValue<string>() + ":a.txt"], cancellationToken)).IsEqualTo("continued");
        }
        finally { harness.Kill(); await running; }
    }

    [Test]
    [Arguments("version")]
    [Arguments("signed-out")]
    [Arguments("account")]
    [Timeout(180_000)]
    public async Task Preflight_refuses_before_claim_when_version_or_login_is_wrong(string failure, CancellationToken cancellationToken)
    {
        using var lab = new CodexLab();
        var repo = await LabRepo.CreateAsync(lab.Tree, cancellationToken);
        lab.CodeTask();
        lab.Write("task", CodexLab.Handshake + CodexLab.Thread + CodexLab.Final);
        if (failure == "version") File.WriteAllText(Path.Combine(lab.Scenarios, "version.txt"), "codex-cli 0.0.1\n");
        else if (failure == "signed-out") File.WriteAllText(Path.Combine(lab.Scenarios, "login-status.json"), "{\"exit\":1,\"stderr\":\"Signed out\"}");
        else
        {
            lab.Write("preflight", CodexLab.Handshake + "{\"expect\":\"account/read\",\"reply\":{\"account\":null,\"requiresOpenaiAuth\":true}}");
        }
        using var harness = new HarnessProcess(lab.Tree);
        var result = await harness.RunAsync(["run", lab.Tasks, "--repo", repo.Root], cancellationToken,
            lab.Environment.ToDictionary(x => x.Key, x => (string?)x.Value));
        await Assert.That(result.ExitCode).IsEqualTo(2);
        await Assert.That(result.Stderr).Contains(failure == "version" ? "0.163.0" : "codex login");
        await Assert.That(Directory.GetFiles(lab.Tree.DispatchHome, "run.json", SearchOption.AllDirectories)).IsEmpty();
        await Assert.That(lab.Input().Where(x => x["method"]?.GetValue<string>() == "turn/start")).IsEmpty();
    }

    [Test]
    [Timeout(180_000)]
    public async Task Run_code_task_commits_verifies_and_settles_done(CancellationToken cancellationToken)
    {
        using var lab = new CodexLab();
        var repo = await LabRepo.CreateAsync(lab.Tree, cancellationToken);
        lab.CodeTask();
        lab.Write("task", CodexLab.Handshake + CodexLab.Thread + """
            {"writeFile":{"path":"$cwd/a.txt","base64":"Y2hhbmdlZAo="}}

            """ + CodexLab.Final);
        using var harness = new HarnessProcess(lab.Tree);
        var result = await harness.RunAsync(["run", lab.Tasks, "--repo", repo.Root], cancellationToken,
            lab.Environment.ToDictionary(x => x.Key, x => (string?)x.Value), documentedEntry: true);
        result.RequireSuccess("Codex code run");
        await Assert.That(lab.Result["state"]!.GetValue<string>()).IsEqualTo("done");
        await Assert.That(lab.Result["backend"]!.GetValue<string>()).IsEqualTo("codex");
        await Assert.That(lab.Result["commit"]!.GetValue<string>()).IsNotEmpty();
        await Assert.That(lab.Result["verify"]![0]!["exitCode"]!.GetValue<int>()).IsEqualTo(0);
        var commit = lab.Result["commit"]!.GetValue<string>();
        await Assert.That(await repo.GitAsync(["show", $"{commit}:a.txt"], cancellationToken)).IsEqualTo("changed");
        await Assert.That(lab.Wire().Any(x => x["direction"]!.GetValue<string>() == "unexpected")).IsFalse();
    }
}
