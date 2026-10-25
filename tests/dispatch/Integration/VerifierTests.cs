using System.Text.Json.Nodes;
using Dispatch.Tests.Support;
using TUnit.Assertions.Enums;

namespace Dispatch.Tests.Integration;

[Category("Integration")]
public sealed class VerifierTests
{
    [Test]
    public async Task Prepare_cancel_preserves_completed_command_exit(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var result = new VerifyResult { Command = "exit 3", ExitCode = 3, TimedOut = false };
        const string log = "$ exit 3\n[exit 3]\n";
        var executed = Verifier.CancelledCommand((result, log, null));
        await Assert.That(executed.Result.Command).IsEqualTo("exit 3");
        await Assert.That(executed.Result.ExitCode).IsEqualTo(3);
        await Assert.That(executed.Result.TimedOut).IsFalse();
        await Assert.That(executed.Log).IsEqualTo(log);
        await Assert.That(executed.Error).IsNull();
    }

    [Test]
    [Timeout(30_000)]
    public async Task Verify_continues_after_failure(CancellationToken ct)
    {
        using var tree = new TempTree();
        var (run, ledger) = VerifyFixture(tree);
        var marker = Path.Combine(tree.Root, "second").Replace("'", "''");
        var second = OperatingSystem.IsWindows() ? $"Set-Content -LiteralPath '{marker}' -Value done" : $"printf done > '{marker}'";
        run.Record.Spec.Verify = ["exit 3", second];
        var result = new TaskResult { Outcome = "succeeded" };
        await Verifier.VerifyAsync(ledger, run, result, ct);
        await Assert.That(result.Verify.Select(x => x.Command)).IsEquivalentTo(new[] { "exit 3", second }, CollectionOrdering.Matching);
        await Assert.That(result.Verify.Select(x => x.ExitCode)).IsEquivalentTo(new[] { 3, 0 }, CollectionOrdering.Matching);
        await Assert.That(File.ReadAllText(marker).Trim()).IsEqualTo("done");
        await Assert.That(ledger.Read().Single(x => x.Type == "verified").Data!.ToJsonString()).IsEqualTo("{\"commands\":2,\"failed\":1}");
    }

    [Test]
    [Timeout(30_000)]
    public async Task Verify_cancel_preserves_existing_evidence(CancellationToken ct)
    {
        foreach (var cancelDuringCommand in new[] { false, true })
        {
            using var tree = new TempTree();
            var (run, ledger) = VerifyFixture(tree);
            var marker = Path.Combine(tree.Root, "entered");
            var command = OperatingSystem.IsWindows()
                ? $"Set-Content -LiteralPath '{marker}' -Value entered; while ($true) {{ Start-Sleep -Milliseconds 20 }}"
                : $"printf entered > '{marker}'; while :; do sleep 0.02; done";
            run.Record.Spec.Verify = [command, "exit 0"];
            using var cancel = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var result = new TaskResult { Outcome = "succeeded", Verify = [new() { Command = "previous", ExitCode = 7 }] };
            await ledger.WriteArtifactAsync(run.Id, "verify.log", "existing log", ct);
            if (!cancelDuringCommand) cancel.Cancel();
            var verifying = Verifier.VerifyAsync(ledger, run, result, cancel.Token);
            if (cancelDuringCommand)
            {
                while (!File.Exists(marker) && !verifying.IsCompleted) await Task.Delay(10, ct);
                cancel.Cancel();
            }
            await Assert.That(async () => await verifying).Throws<OperationCanceledException>();
            await Assert.That(result.Verify.Select(x => x.Command)).IsEquivalentTo(cancelDuringCommand ? new[] { "previous", command } : new[] { "previous" }, CollectionOrdering.Matching);
            await Assert.That(result.Verify[0].ExitCode).IsEqualTo(7);
            if (cancelDuringCommand)
            {
                await Assert.That(result.Verify[1].TimedOut).IsTrue();
                await Assert.That(result.Verify[1].ExitCode).IsEqualTo(-1);
            }
            await Assert.That(File.ReadAllText(Path.Combine(ledger.TaskDir(run.Id), "verify.log"))).IsEqualTo("existing log");
            await Assert.That(ledger.Read().Select(x => x.Type)).DoesNotContain("verified");
        }
    }

    [Test]
    [Timeout(30_000)]
    public async Task Prepare_timeout_text_is_invariant_and_uses_singular(CancellationToken ct)
    {
        using var tree = new TempTree();
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("fr-FR");
            foreach (var (limit, text) in new (TimeSpan?, string)[] { (null, "30 minutes"), (TimeSpan.FromMinutes(1), "1 minute"), (TimeSpan.FromSeconds(1), "1 second"), (TimeSpan.FromMinutes(1.5), "1.5 minutes") })
            {
                using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
                using var reason = new CancellationTokenSource();
                var command = OperatingSystem.IsWindows() ? "while ($true) { Start-Sleep -Milliseconds 20 }" : "while :; do sleep 0.02; done";
                var running = Verifier.RunCommandAsync(new(), null, tree.Repository, WorkerEnvironment.Snapshot(), command, false, stop.Token, reason.Token, limit);
                stop.Cancel(); // Proc has started synchronously; its existing cancellation classification supplies a timeout without waiting 30 minutes.
                var executed = await running;
                await Assert.That(executed.Result.ExitCode).IsEqualTo(-1);
                await Assert.That(executed.Result.TimedOut).IsTrue();
                await Assert.That(executed.Error!.Message).IsEqualTo($"timed out after {text}");
            }
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = culture; }
    }

    static (TaskRun Run, Ledger Ledger) VerifyFixture(TempTree tree) => (new TaskRun { Record = new TaskRecord
    {
        Spec = new() { Id = "inspect", Kind = "research" }, Workspace = new() { Path = tree.Repository },
        Settings = new() { TempRoot = new() { Value = tree.WorktreeRoot } },
    } }, new Ledger(Path.Combine(tree.Root, "ledger")));

    [Test]
    [Timeout(180_000)]
    public async Task Verify_uses_configured_temp(CancellationToken ct)
    {
        using var tree = new TempTree();
        var repo = await LabRepo.CreateAsync(tree, ct);
        var root = Path.Combine(tree.Root, "private-temp");
        Directory.CreateDirectory(Path.Combine(repo.Root, ".dispatch"));
        File.WriteAllText(Path.Combine(repo.Root, ".dispatch", "config.jsonc"), new JsonObject
        {
            ["schemaVersion"] = 2, ["preferences"] = new JsonObject { ["tempRoot"] = root },
        }.ToJsonString());
        await repo.GitAsync(["add", ".dispatch/config.jsonc"], ct);
        await repo.GitAsync(["commit", "-m", "configure temp"], ct);
        var command = OperatingSystem.IsWindows()
            ? "$env:TEMP; $env:TMP; $env:TMPDIR; Set-Content -LiteralPath (Join-Path $env:TEMP 'verify-marker') -Value 'created'"
            : "printf '%s\\n' \"$TEMP\" \"$TMP\" \"$TMPDIR\"; printf 'created\\n' > \"$TEMP/verify-marker\"";
        var tasks = Path.Combine(tree.Root, "tasks.jsonc");
        File.WriteAllText(tasks, new JsonObject
        {
            ["schemaVersion"] = 2, ["defaults"] = new JsonObject { ["backend"] = "opencode" },
            ["tasks"] = new JsonArray(new JsonObject { ["id"] = "inspect", ["kind"] = "research", ["base"] = "main",
                ["route"] = "fixture/model", ["prompt"] = "Inspect.", ["verify"] = new JsonArray(command), ["advisoryVerify"] = new JsonArray(command) }),
        }.ToJsonString());
        using var process = new HarnessProcess(tree);
        (await process.RunAsync(["run", tasks, "--repo", repo.Root], ct,
            FakeOpenCode.Environment(tree, "one-task.jsonl").ToDictionary(x => x.Key, x => (string?)x.Value))).RequireSuccess("verify run");
        var ledger = Directory.GetDirectories(Path.Combine(tree.DispatchHome, "state", "ledgers")).Single();
        var temp = Path.Combine(root, Path.GetFileName(ledger), "inspect");
        var log = File.ReadAllText(Path.Combine(ledger, "inspect", "verify.log")).Replace("\r\n", "\n");
        var outputs = log.Split("[stdout]\n").Skip(1).Select(part => part.Split("[stderr]")[0].TrimEnd()).ToArray();
        await Assert.That(outputs).IsEquivalentTo(new[] { $"{temp}\n{temp}\n{temp}", $"{temp}\n{temp}\n{temp}" }, CollectionOrdering.Matching);
        await Assert.That(File.ReadAllText(Path.Combine(temp, "verify-marker")).Trim()).IsEqualTo("created");
        var record = JsonNode.Parse(File.ReadAllText(Path.Combine(ledger, "inspect", "task.json")))!;
        await Assert.That(record["settings"]!["tempRoot"]!["value"]!.GetValue<string>()).IsEqualTo(root);
        await Assert.That(record["settings"]!["tempRoot"]!["from"]!.GetValue<string>()).IsEqualTo("repo");
    }

    [Test]
    [Timeout(180_000)]
    public async Task Verify_UsesSelectedShell(CancellationToken ct)
    {
        using var tree = new TempTree();
        var (result, log, _) = await RunAsync(tree, "printf 'selected=%s\\n' \"${BASH_VERSION:+bash}\"", ct, shell: "bash");
        await Assert.That(result["verify"]![0]!["exitCode"]!.GetValue<int>()).IsEqualTo(0);
        await Assert.That(log).Contains("selected=bash");
        var shell = result["verifyShell"]!.GetValue<string>();
        await Assert.That(Path.IsPathFullyQualified(shell)).IsTrue();
        await Assert.That(Path.GetFileNameWithoutExtension(shell)).IsEqualTo("bash");
    }

    [Test]
    [Timeout(180_000)]
    public async Task Verify_SeesNoScrubbedVariable(CancellationToken ct)
    {
        using var tree = new TempTree();
        var command = OperatingSystem.IsWindows()
            ? "[Environment]::GetEnvironmentVariables().Keys | Sort-Object"
            : "env | cut -d= -f1 | sort";
        var (_, log, _) = await RunAsync(tree, command, ct, environment: new() { ["FAKE_TOKEN"] = "synthetic", ["FAKE_KEEP"] = "kept" });
        var output = log.Split("[stdout]")[1].Split("[stderr]")[0];
        await Assert.That(output).DoesNotContain("FAKE_TOKEN");
        await Assert.That(output).Contains("FAKE_KEEP");
    }

    [Test]
    [Timeout(180_000)]
    public async Task Verify_DoesNotSourceLoginProfile(CancellationToken ct)
    {
        if (OperatingSystem.IsWindows()) Skip.Test("Unix login profiles are not used on Windows");
        using var tree = new TempTree();
        var home = tree.ChildEnvironment()["HOME"]!;
        await File.WriteAllTextAsync(Path.Combine(home, ".bash_profile"), "export LEAK=1\n", ct);
        var (_, log, _) = await RunAsync(tree, "printf 'leak=%s\\n' \"${LEAK-unset}\"", ct);
        await Assert.That(log).Contains("leak=unset");
    }

    [Test]
    [Timeout(180_000)]
    public async Task Verify_DoesNotSourceBashEnv(CancellationToken ct)
    {
        if (OperatingSystem.IsWindows()) Skip.Test("Bash startup hooks are tested on Unix");
        using var tree = new TempTree();
        var hook = Path.Combine(tree.Root, "startup.sh");
        await File.WriteAllTextAsync(hook, "export LEAK=1\n", ct);
        var (_, log, _) = await RunAsync(tree, "printf 'leak=%s\\n' \"${LEAK-unset}\"", ct,
            environment: new() { ["BASH_ENV"] = hook, ["ENV"] = hook });
        await Assert.That(log).Contains("leak=unset");
    }

    [Test]
    [Timeout(180_000)]
    public async Task Verify_RunsBelowNormal(CancellationToken ct)
    {
        using var tree = new TempTree();
        var command = OperatingSystem.IsWindows()
            ? "(Get-Process -Id $PID).PriorityClass"
            : "sh -c 'ps -o nice= -p $$'";
        var (_, log, _) = await RunAsync(tree, command, ct);
        var output = log.Split("[stdout]")[1].Split("[stderr]")[0].Trim();
        if (OperatingSystem.IsWindows())
        {
            await Assert.That(output).IsEqualTo("BelowNormal");
            return;
        }
        // nice -n 10 is relative: verify runs 10 below the harness, which inherits this process's nice.
        using var ps = new CapturedProcess();
        var own = await ps.RunAsync("ps", ["-o", "nice=", "-p", Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture)],
            tree.Root, tree.ChildEnvironment(), ct);
        own.RequireSuccess("read the test host's nice");
        var baseline = int.Parse(own.Stdout.Trim(), System.Globalization.CultureInfo.InvariantCulture);
        var verify = int.Parse(output, System.Globalization.CultureInfo.InvariantCulture);
        await Assert.That(verify).IsEqualTo(Math.Min(20, baseline + 10)).Because($"test host nice {baseline}, verify nice {verify}");
    }

    [Test]
    [Timeout(180_000)]
    public async Task Verify_UsesTaskTemp(CancellationToken ct)
    {
        using var tree = new TempTree();
        var command = OperatingSystem.IsWindows()
            ? "$env:TEMP; $env:TMP; $env:TMPDIR; $env:DOTNET_CLI_USE_MSBUILD_SERVER; $env:MSBUILDDISABLENODEREUSE; $env:UseSharedCompilation"
            : "printf '%s\\n' \"$TEMP\" \"$TMP\" \"$TMPDIR\" \"$DOTNET_CLI_USE_MSBUILD_SERVER\" \"$MSBUILDDISABLENODEREUSE\" \"$UseSharedCompilation\"";
        var (result, log, workspace) = await RunAsync(tree, command, ct, environment: new()
        {
            ["DOTNET_CLI_USE_MSBUILD_SERVER"] = "1", ["MSBUILDDISABLENODEREUSE"] = "0", ["UseSharedCompilation"] = "true",
        });
        var ledger = Directory.GetDirectories(Path.Combine(tree.DispatchHome, "state", "ledgers")).Single();
        var taskTemp = Path.Combine(tree.DispatchHome, "state", "worktrees", Path.GetFileName(ledger), ".tmp", "inspect");
        await Assert.That(log.Replace("\r\n", "\n")).Contains($"[stdout]\n{taskTemp}\n{taskTemp}\n{taskTemp}\n0\n1\nfalse\n");
        await Assert.That(result["verify"]![0]!["exitCode"]!.GetValue<int>()).IsEqualTo(0);
        await Assert.That(Directory.Exists(workspace)).IsTrue();
    }

    static async Task<(JsonNode Result, string Log, string Workspace)> RunAsync(TempTree tree, string command, CancellationToken ct,
        string? shell = null, Dictionary<string, string?>? environment = null)
    {
        var repo = await LabRepo.CreateAsync(tree, ct);
        var config = Directory.CreateDirectory(Path.Combine(tree.DispatchHome, "config")).FullName;
        await File.WriteAllTextAsync(Path.Combine(config, "config.jsonc"), """{"schemaVersion":2,"restrictions":{"deny":[{"name":"no-edits","action":"edit","resources":["*"]}]}}""", ct);
        var spec = new JsonObject
        {
            ["id"] = "inspect", ["kind"] = "research", ["base"] = "main", ["route"] = "fixture/model", ["prompt"] = "Inspect.",
            ["verify"] = new JsonArray(command),
        };
        if (shell is not null) spec["shell"] = shell;
        var tasks = Path.Combine(tree.Root, "tasks.jsonc");
        await File.WriteAllTextAsync(tasks, new JsonObject
        {
            ["schemaVersion"] = 2, ["defaults"] = new JsonObject { ["backend"] = "opencode" }, ["tasks"] = new JsonArray(spec),
        }.ToJsonString(), ct);
        var variables = FakeOpenCode.Environment(tree, "one-task.jsonl").ToDictionary(p => p.Key, p => (string?)p.Value);
        foreach (var (name, value) in environment ?? []) variables[name] = value;
        using var harness = new HarnessProcess(tree);
        var outcome = await harness.RunAsync(["run", tasks, "--repo", repo.Root, "--max", "1"], ct, variables);
        outcome.RequireSuccess("verify run");
        var ledger = Directory.GetDirectories(Path.Combine(tree.DispatchHome, "state", "ledgers")).Single();
        var folder = Path.Combine(ledger, "inspect");
        var result = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(folder, "result.json"), ct))!;
        var record = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(folder, "task.json"), ct))!;
        return (result, await File.ReadAllTextAsync(Path.Combine(folder, "verify.log"), ct), record["workspace"]!["path"]!.GetValue<string>());
    }
}
