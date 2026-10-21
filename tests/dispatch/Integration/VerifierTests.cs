using System.Text.Json.Nodes;
using Dispatch.Tests.Support;

namespace Dispatch.Tests.Integration;

[Category("Integration")]
public sealed class VerifierTests
{
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
            ? "& (Join-Path $PSHOME 'pwsh.exe') -NoLogo -NoProfile -NonInteractive -Command '(Get-Process -Id $PID).PriorityClass'"
            : "sh -c 'ps -o nice= -p $$'";
        var (_, log, _) = await RunAsync(tree, command, ct);
        var output = log.Split("[stdout]")[1].Split("[stderr]")[0].Trim();
        if (OperatingSystem.IsWindows()) await Assert.That(output).IsEqualTo("BelowNormal");
        else await Assert.That(int.Parse(output, System.Globalization.CultureInfo.InvariantCulture)).IsGreaterThanOrEqualTo(10);
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
