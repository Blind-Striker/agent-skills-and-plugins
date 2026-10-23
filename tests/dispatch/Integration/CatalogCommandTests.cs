using System.Text.Json.Nodes;
using Dispatch.Tests.Support;
using TUnit.Assertions.Enums;

namespace Dispatch.Tests.Integration;

[Category("Integration")]
[NotInParallel("processes")]
public sealed class CatalogCommandTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    [Timeout(180_000)]
    public async Task Selfcheck_removes_worktree_after_OpenCode_releases_directory_handles(bool transientLock, CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows()) Skip.Test("Open file handles prevent worktree deletion on Windows.");
        using var lab = new CodexLab();
        var repo = await LabRepo.CreateAsync(lab.Tree, ct);
        var environment = Configure(lab, both: true);
        environment["FAKE_OPENCODE_HOLD_DIRECTORY"] = "1";
        var calls = Path.Combine(lab.Tree.Root, "git-calls.jsonl");
        if (transientLock)
        {
            var shim = Directory.CreateDirectory(Path.Combine(lab.Tree.Root, "git-shim")).FullName;
            foreach (var source in Directory.GetFiles(Path.GetDirectoryName(FakeOpenCode.Executable)!))
                File.Copy(source, Path.Combine(shim, Path.GetFileName(source)));
            File.Copy(FakeOpenCode.Executable, Path.Combine(shim, "git.exe"));
            environment["FAKE_GIT_EXECUTABLE"] = Executables.Resolve("git");
            environment["FAKE_GIT_MODE"] = "worktree-lock";
            environment["FAKE_GIT_READY"] = Path.Combine(lab.Tree.Root, "lock-held");
            environment["FAKE_GIT_CALLS"] = calls;
            environment["PATH"] = shim + Path.PathSeparator + environment["PATH"];
        }
        using var harness = new HarnessProcess(lab.Tree);

        var result = await harness.RunAsync(["selfcheck", "--backend", "opencode", "--repo", repo.Root], ct, environment);

        result.RequireSuccess("selfcheck with directory handles");
        var held = Directory.GetFiles(lab.Tree.Root, "requests.jsonl.*.directory.json")
            .Select(path => JsonNode.Parse(File.ReadAllText(path))!.GetValue<string>()).ToArray();
        await Assert.That(held.Length).IsEqualTo(2);
        foreach (var directory in held)
        {
            await Assert.That(Path.GetDirectoryName(directory)).IsEqualTo(lab.Tree.WorktreeRoot);
            await Assert.That(Directory.Exists(directory)).IsFalse();
        }
        await Assert.That(result.Stdout).Contains("PASS  worktree removed:");
        await Assert.That(result.Stdout).Contains("selfcheck passed");
        await Assert.That(Directory.GetFileSystemEntries(lab.Tree.WorktreeRoot)).IsEmpty();
        await Assert.That((await repo.GitAsync(["worktree", "list", "--porcelain"], ct)).Split('\n')
            .Count(line => line.StartsWith("worktree ", StringComparison.Ordinal))).IsEqualTo(1);
        if (transientLock)
        {
            var gitCalls = File.ReadLines(calls).Select(line => JsonNode.Parse(line)!.AsArray()).ToArray();
            await Assert.That(gitCalls.Count(args => args[12]!.GetValue<string>() == "worktree" && args[13]!.GetValue<string>() == "remove")).IsEqualTo(1);
            await Assert.That(gitCalls.Any(args => args[12]!.GetValue<string>() == "worktree" && args[13]!.GetValue<string>() == "list")).IsTrue();
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    [Timeout(180_000)]
    public async Task Selfcheck_reports_each_configured_backend_and_cleans_up(bool both, CancellationToken ct)
    {
        using var lab = new CodexLab();
        var repo = await LabRepo.CreateAsync(lab.Tree, ct);
        await repo.GitAsync(["config", "core.longpaths", "false"], ct);
        var environment = Configure(lab, both);
        using var harness = new HarnessProcess(lab.Tree);

        var result = await harness.RunAsync(["selfcheck", "--repo", repo.Root], ct, environment);

        TestContext.Current!.Output.WriteLine(result.ToString());
        result.RequireSuccess("selfcheck");
        await Assert.That(result.Stdout).Contains("backend codex");
        await Assert.That(result.Stdout).Contains("pin: installed 0.163.0");
        await Assert.That(result.Stdout).Contains("login:");
        await Assert.That(result.Stdout).Contains("model catalog:");
        await Assert.That(result.Stdout).Contains("clean profile: clean");
        await Assert.That(result.Stdout).Contains("sandbox: workspace-write; network on");
        await Assert.That(result.Stdout).Contains($"state folder: {Path.Combine(lab.Tree.DispatchHome, "state")}");
        await Assert.That(result.Stdout).Contains($"worktree root: {lab.Tree.WorktreeRoot}");
        await Assert.That(result.Stdout).Contains($"control root: {Path.Combine(lab.Tree.DispatchHome, "state", "control")}");
        await Assert.That(result.Stdout).Contains("hardlink volume:");
        if (OperatingSystem.IsWindows())
        {
            await Assert.That(result.Stdout).Contains("warning: core.longpaths");
            await Assert.That(result.Stdout).Contains("locations.worktreeRoot");
        }
        await Assert.That(result.Stdout.Contains("backend opencode", StringComparison.Ordinal)).IsEqualTo(both);
        await Assert.That(lab.Input().Count(x => x["method"]?.GetValue<string>() == "thread/start")).IsEqualTo(1);
        await Assert.That(lab.Input().Count(x => x["method"]?.GetValue<string>() == "turn/start")).IsEqualTo(0);
        var starts = Directory.GetFiles(lab.Records, "start.json", SearchOption.AllDirectories).Select(x => JsonNode.Parse(File.ReadAllText(x))!).ToArray();
        await Assert.That(starts.Any(x => x["scenario"]!.GetValue<string>() == "probe.jsonl")).IsTrue();
        if (both)
        {
            var hosts = Directory.GetFiles(lab.Tree.Root, "requests.jsonl.*.host.json").Select(x => JsonNode.Parse(File.ReadAllText(x))!)
                .OrderBy(x => x["environment"]!["OPENCODE_DISABLE_CLAUDE_CODE"]?.GetValue<string>(), StringComparer.Ordinal).ToArray();
            await Assert.That(hosts.Any(x => x["environment"]!["OPENCODE_DISABLE_CLAUDE_CODE"] is null)).IsTrue();
            await Assert.That(hosts.Any(x => x["environment"]!["OPENCODE_DISABLE_CLAUDE_CODE"]?.GetValue<string>() == "1")).IsTrue();
            var requests = File.ReadLines(Path.Combine(lab.Tree.Root, "requests.jsonl")).Select(x => JsonNode.Parse(x)!).ToArray();
            await Assert.That(requests.Any(x => x["unexpected"]!.GetValue<bool>())).IsFalse();
            await Assert.That(requests.Any(x => x["path"]!.GetValue<string>().EndsWith("/prompt", StringComparison.Ordinal))).IsFalse();
        }
        await Assert.That(Directory.GetDirectories(Path.Combine(lab.Tree.DispatchHome, "state", "control"))).IsEmpty();
        await Assert.That((await repo.GitAsync(["worktree", "list", "--porcelain"], ct)).Split('\n').Count(x => x.StartsWith("worktree ", StringComparison.Ordinal))).IsEqualTo(1);
        await Assert.That(Directory.GetFileSystemEntries(lab.Tree.WorktreeRoot)).IsEmpty();
    }

    [Test]
    [Timeout(180_000)]
    public async Task Selfcheck_launches_and_releases_each_backend(CancellationToken ct)
    {
        using var lab = new CodexLab();
        var environment = Configure(lab, both: true);
        var repo = await LabRepo.CreateAsync(lab.Tree, ct);
        using var harness = new HarnessProcess(lab.Tree);
        var result = await harness.RunAsync(["selfcheck", "--repo", repo.Root, "--profile", "clean"], ct, environment);
        result.RequireSuccess("worker selfcheck");
        await Assert.That(result.Stdout.Split('\n').Select(x => x.TrimEnd('\r')).Where(x => x.Contains("  launch:", StringComparison.Ordinal)).ToArray())
            .IsEquivalentTo(new[] { "PASS  launch: thread-main", "PASS  launch: ses_fixture" }, CollectionOrdering.Matching);
        var wire = lab.Wire();
        await Assert.That(lab.Input().Where(x => x["method"]?.GetValue<string>() is "thread/start" or "turn/start")
            .Select(x => x["method"]!.GetValue<string>()).ToArray()).IsEquivalentTo(new[] { "thread/start" }, CollectionOrdering.Matching);
        await Assert.That(wire.Single(x => x["message"]?["result"]?["thread"] is not null)["message"]!["result"]!["thread"]!["id"]!.GetValue<string>()).IsEqualTo("thread-main");
        var requests = File.ReadLines(environment["FAKE_OPENCODE_RECORD"]!).Select(x => JsonNode.Parse(x)!).ToArray();
        var launchPid = requests.Single(x => x["method"]!.GetValue<string>() == "PUT")["pid"]!.GetValue<int>();
        await Assert.That(requests.Where(x => x["pid"]!.GetValue<int>() == launchPid && x["method"]!.GetValue<string>() != "GET")
            .Select(x => $"{x["method"]} {x["path"]}").ToArray())
            .IsEquivalentTo(new[] { "POST /api/session", "PUT /api/session/ses_fixture/environment", "DELETE /api/session/ses_fixture" }, CollectionOrdering.Matching);
        await Assert.That(requests.Where(x => x["unexpected"]!.GetValue<bool>()).ToArray()).IsEmpty();
        var variables = requests.Single(x => x["method"]!.GetValue<string>() == "PUT")["body"]!["variables"]!;
        await Assert.That(Directory.Exists(variables["TEMP"]!.GetValue<string>())).IsFalse();
        var starts = Directory.GetFiles(lab.Records, "start.json", SearchOption.AllDirectories).Select(x => JsonNode.Parse(File.ReadAllText(x))!).ToArray();
        var worker = starts.Single(x => x["scenario"]!.GetValue<string>() == "task.jsonl");
        await Assert.That(Directory.Exists(worker["codexHome"]!.GetValue<string>())).IsFalse();
        var hosts = Directory.GetFiles(lab.Tree.Root, "requests.jsonl.*.host.json").Select(x => JsonNode.Parse(File.ReadAllText(x))!);
        foreach (var start in starts.Concat(hosts))
        {
            var stopped = true;
            try { using var process = System.Diagnostics.Process.GetProcessById(start["pid"]!.GetValue<int>()); stopped = process.HasExited; }
            catch (ArgumentException) { }
            await Assert.That(stopped).IsTrue();
        }
        await Assert.That(Directory.GetDirectories(Path.Combine(lab.Tree.DispatchHome, "state", "control"))).IsEmpty();
        await Assert.That(Directory.GetFileSystemEntries(lab.Tree.WorktreeRoot)).IsEmpty();

        // Hold a successful release: PASS must not appear while the worker still exists.
        await using var gated = new OpenCodeLab(lab.Tree);
        gated.Steps.Clear();
        gated.Steps.AddRange(File.ReadLines(environment["FAKE_OPENCODE_SCENARIO"]!).Select(x => JsonNode.Parse(x)!.AsObject()));
        var arrived = Path.Combine(lab.Tree.Root, "release-arrived");
        var release = Path.Combine(lab.Tree.Root, "release-allowed");
        var deletion = gated.Steps.Single(x => x["expect"]!.GetValue<string>() == "DELETE /api/session/ses_fixture");
        deletion["arrived"] = arrived;
        deletion["waitFor"] = release;
        gated.Write();
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var watcher = new FileSystemWatcher(lab.Tree.Root, Path.GetFileName(arrived));
        watcher.Created += (_, _) => reached.TrySetResult();
        watcher.Error += (_, error) => reached.TrySetException(error.GetException());
        watcher.EnableRaisingEvents = true;
        var output = TestContext.Current!.Output;
        var running = Commands.SelfCheckAsync(Args.Parse(["selfcheck", "--backend", "opencode"]), ct);
        try
        {
            await Assert.That(await Task.WhenAny(reached.Task, running)).IsEqualTo(reached.Task);
            await reached.Task;
            await Assert.That(output.GetStandardOutput()).DoesNotContain("  launch:");
        }
        finally
        {
            File.WriteAllText(release, "go");
            await running;
        }
        await Assert.That(await running).IsEqualTo(0);
        await Assert.That(output.GetStandardOutput().Split('\n').Select(x => x.TrimEnd('\r')).Where(x => x.Contains("  launch:", StringComparison.Ordinal)).ToArray())
            .IsEquivalentTo(new[] { "PASS  launch: ses_fixture" }, CollectionOrdering.Matching);
    }

    [Test]
    [Timeout(180_000)]
    public async Task Selfcheck_launch_finds_Codex_on_Windows_Path(CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows()) Skip.Test("Windows environment names are case-insensitive.");
        using var lab = new CodexLab();
        var environment = Configure(lab, both: false);
        var configPath = Path.Combine(lab.Tree.DispatchHome, "config", "config.jsonc");
        var config = JsonNode.Parse(File.ReadAllText(configPath))!;
        config["backends"]!["codex"]!.AsObject().Remove("executable");
        File.WriteAllText(configPath, config.ToJsonString());
        var path = environment.Single(x => x.Key.Equals("PATH", StringComparison.OrdinalIgnoreCase));
        environment.Remove(path.Key);
        environment["Path"] = Path.GetDirectoryName(CodexLab.Executable) + Path.PathSeparator + path.Value;
        await Assert.That(environment.Keys.Where(x => x.Equals("PATH", StringComparison.OrdinalIgnoreCase)).ToArray())
            .IsEquivalentTo(new[] { "Path" }, CollectionOrdering.Matching);
        using var harness = new CapturedProcess();
        var result = await harness.RunAsync("dotnet", [Gates.HarnessDll, "selfcheck", "--backend", "codex"], TestPaths.Harness, environment, ct);
        result.RequireSuccess("Codex on Path");
        await Assert.That(result.Stdout.Split('\n').Select(x => x.TrimEnd('\r')).Where(x => x.Contains("  launch:", StringComparison.Ordinal)).ToArray())
            .IsEquivalentTo(new[] { "PASS  launch: thread-main" }, CollectionOrdering.Matching);
        await Assert.That(lab.Input().Count(x => x["method"]?.GetValue<string>() == "thread/start")).IsEqualTo(1);
        await Assert.That(lab.Input().Count(x => x["method"]?.GetValue<string>() == "turn/start")).IsEqualTo(0);
    }

    [Test]
    [Arguments("all")]
    [Arguments("codex")]
    [Arguments("opencode")]
    [Timeout(180_000)]
    public async Task Routes_labels_backends_and_configured_executables_win_over_PATH_decoys(string selection, CancellationToken ct)
    {
        using var lab = new CodexLab();
        var repo = await LabRepo.CreateAsync(lab.Tree, ct);
        var environment = Configure(lab, both: true);
        var decoys = Directory.CreateDirectory(Path.Combine(lab.Tree.Root, "decoys")).FullName;
        var marker = Path.Combine(decoys, "invoked");
        foreach (var name in new[] { "opencode", "codex" })
        {
            var path = Path.Combine(decoys, name);
            File.WriteAllText(path, $"#!/bin/sh\nprintf decoy > '{marker.Replace("'", "'\\''", StringComparison.Ordinal)}'\nexit 97\n");
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            File.WriteAllText(path + ".cmd", $"@echo off\r\necho decoy>\"{marker}\"\r\nexit /b 97\r\n");
        }
        environment["PATH"] = decoys + Path.PathSeparator + environment["PATH"];
        using var harness = new HarnessProcess(lab.Tree);
        string[] arguments = selection == "all" ? ["routes", "--repo", repo.Root] : ["routes", "--backend", selection, "--repo", repo.Root];

        var result = await harness.RunAsync(arguments, ct, environment);

        result.RequireSuccess("routes");
        await Assert.That(result.Stdout.Contains("codex:model-y", StringComparison.Ordinal)).IsEqualTo(selection != "opencode");
        await Assert.That(result.Stdout.Contains("opencode:fixture/model", StringComparison.Ordinal)).IsEqualTo(selection != "codex");
        await Assert.That(result.Stdout.Contains("backend codex", StringComparison.Ordinal)).IsEqualTo(selection != "opencode");
        await Assert.That(result.Stdout.Contains("backend opencode", StringComparison.Ordinal)).IsEqualTo(selection != "codex");
        await Assert.That(File.Exists(marker)).IsFalse();
    }

    [Test]
    [Timeout(180_000)]
    public async Task Selfcheck_uses_task_file_backends_and_reports_their_requested_sandbox(CancellationToken ct)
    {
        using var lab = new CodexLab();
        var repo = await LabRepo.CreateAsync(lab.Tree, ct);
        var environment = Configure(lab, both: true);
        var configPath = Path.Combine(lab.Tree.DispatchHome, "config", "config.jsonc");
        var config = JsonNode.Parse(File.ReadAllText(configPath))!;
        config["preferences"]!["defaultRoutes"]!.AsObject().Remove("codex");
        config["preferences"]!["backend"] = "opencode";
        File.WriteAllText(configPath, config.ToJsonString());
        File.WriteAllText(lab.Tasks, """
            {"schemaVersion":2,"repo":"repo","tasks":[{"id":"inspect","kind":"research","backend":"codex","route":"codex:model-y#high","prompt":"Inspect."}]}
            """);
        var scenario = Path.Combine(lab.Scenarios, "task.jsonl");
        File.WriteAllText(scenario, File.ReadAllText(scenario).Replace("workspaceWrite", "readOnly", StringComparison.Ordinal));
        using var harness = new HarnessProcess(lab.Tree);

        var result = await harness.RunAsync(["selfcheck", lab.Tasks], ct, environment);

        result.RequireSuccess("task-file selfcheck");
        await Assert.That(result.Stdout).Contains("backend codex");
        await Assert.That(result.Stdout).Contains("backend opencode");
        await Assert.That(result.Stdout).Contains("sandbox: read-only; network on (inspect)");
    }

    [Test]
    [Arguments("pin")]
    [Arguments("login")]
    [Timeout(180_000)]
    public async Task Selfcheck_reports_a_failed_Codex_probe_and_still_checks_OpenCode(string failure, CancellationToken ct)
    {
        using var lab = new CodexLab();
        var repo = await LabRepo.CreateAsync(lab.Tree, ct);
        var environment = Configure(lab, both: true);
        if (failure == "pin") File.WriteAllText(Path.Combine(lab.Scenarios, "version.txt"), "codex-cli 0.0.0\n");
        else File.WriteAllText(Path.Combine(lab.Scenarios, "login-status.json"), """{"exit":1,"stdout":"Signed out"}""");
        using var harness = new HarnessProcess(lab.Tree);

        var result = await harness.RunAsync(["selfcheck", "--repo", repo.Root], ct, environment);

        await Assert.That(result.ExitCode).IsEqualTo(1);
        await Assert.That(result.Stdout).Contains("FAIL  live: codex");
        await Assert.That(result.Stdout).Contains(failure == "pin" ? "differs from pin" : "missing a login");
        await Assert.That(result.Stdout).Contains("backend opencode");
        await Assert.That(result.Stdout).Contains("PASS  clean profile: clean");
        await Assert.That(Directory.GetDirectories(Path.Combine(lab.Tree.DispatchHome, "state", "control"))).IsEmpty();
        await Assert.That(Directory.GetFileSystemEntries(lab.Tree.WorktreeRoot)).IsEmpty();
    }

    [Test]
    [Timeout(180_000)]
    public async Task Selfcheck_without_a_repository_skips_repository_checks(CancellationToken ct)
    {
        using var lab = new CodexLab();
        var environment = Configure(lab, both: false);
        using var harness = new HarnessProcess(lab.Tree);

        var result = await harness.RunAsync(["selfcheck"], ct, environment);

        result.RequireSuccess("repository-free selfcheck");
        await Assert.That(result.Stdout).Contains("repository checks skipped");
        await Assert.That(result.Stdout).Contains("PASS  clean profile: clean");
        await Assert.That(result.Stdout).DoesNotContain("backend opencode");
        await Assert.That(Directory.GetDirectories(lab.Tree.WorktreeRoot)).IsEmpty();
        await Assert.That(Directory.GetDirectories(Path.Combine(lab.Tree.DispatchHome, "state", "control"))).IsEmpty();
    }

    internal static Dictionary<string, string?> Configure(CodexLab lab, bool both)
    {
        lab.Write("task", CodexLab.Handshake + CodexLab.Thread.Split('\n')[0] + "\n");
        var configPath = Path.Combine(lab.Tree.DispatchHome, "config", "config.jsonc");
        var config = JsonNode.Parse(File.ReadAllText(configPath))!;
        config["backends"]!["opencode"]!["executable"] = FakeOpenCode.Executable;
        if (both) config["preferences"]!["defaultRoutes"]!["opencode"] = "fixture/model";
        File.WriteAllText(configPath, config.ToJsonString());
        File.WriteAllText(Path.Combine(lab.Environment["CODEX_HOME"], "auth.json"), "{}");
        var environment = lab.Environment.ToDictionary(x => x.Key, x => (string?)x.Value);
        environment["FAKE_OPENCODE_RECORD"] = Path.Combine(lab.Tree.Root, "requests.jsonl");
        var scenario = Path.Combine(lab.Tree.Root, "selfcheck.jsonl");
        var fixture = File.ReadLines(FakeOpenCode.Fixture("one-task.jsonl")).Select(x => JsonNode.Parse(x)!).ToArray();
        var steps = fixture.Take(4).ToList();
        var session = OpenCodeLab.Session("ses_fixture");
        session["data"]!["permissions"] = new JsonArray(new JsonObject
        {
            ["action"] = "edit", ["resource"] = Path.Combine(lab.Tree.DispatchHome, "state", "control", "**").Replace('\\', '/'), ["effect"] = "deny",
        });
        var empty = JsonNode.Parse("""{"location":{"directory":"$directory"},"data":[]}""")!;
        steps.Add(new JsonObject { ["expect"] = "GET /api/mcp", ["reply"] = empty.DeepClone(), ["repeat"] = true });
        steps.Add(new JsonObject { ["expect"] = "POST /api/session", ["reply"] = session.DeepClone() });
        steps.Add(new JsonObject { ["expect"] = "GET /api/session/ses_fixture", ["reply"] = session.DeepClone() });
        steps.Add(fixture[10].DeepClone());
        foreach (var endpoint in new[] { "permission", "form" })
            steps.Add(new JsonObject { ["expect"] = $"GET /api/session/ses_fixture/{endpoint}", ["reply"] = empty.DeepClone() });
        steps.Add(new JsonObject { ["expect"] = "DELETE /api/session/ses_fixture", ["status"] = 204 });
        steps.Add(new JsonObject { ["expect"] = "GET /api/session/ses_fixture", ["status"] = 404,
            ["reply"] = new JsonObject { ["_tag"] = "SessionNotFoundError", ["sessionID"] = "ses_fixture", ["message"] = "removed" } });
        steps.Add(new JsonObject { ["expect"] = "POST /api/session", ["reply"] = OpenCodeLab.Session("ses_launch") });
        steps.Add(new JsonObject { ["expect"] = "PUT /api/session/ses_fixture/environment", ["status"] = 204 });
        steps.Add(new JsonObject { ["expect"] = "PUT /api/session/ses_launch/environment", ["status"] = 204 });
        steps.Add(new JsonObject { ["expect"] = "DELETE /api/session/ses_launch", ["status"] = 204 });
        File.WriteAllLines(scenario, steps.Select(x => x.ToJsonString()));
        environment["FAKE_OPENCODE_SCENARIO"] = scenario;
        return environment;
    }
}
