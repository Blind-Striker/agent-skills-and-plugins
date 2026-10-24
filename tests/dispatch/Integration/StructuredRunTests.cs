using System.Text;
using System.Text.Json.Nodes;
using Dispatch.Tests.Support;

namespace Dispatch.Tests.Integration;

[Category("Integration")]
[NotInParallel("processes")]
public sealed class StructuredRunTests
{
    [Test]
    [Arguments("codex")]
    [Arguments("opencode")]
    [Timeout(180_000)]
    public async Task Structured_fresh_projectless_run_uses_system_temp_scratch_and_removes_it(string backend, CancellationToken ct)
    {
        using var lab = new StructuredLab(backend);
        var tasks = JsonNode.Parse(File.ReadAllText(lab.Tasks))!;
        tasks["tasks"]![0]!["project"] = "off";
        tasks["tasks"]![0]!.AsObject().Remove("base");
        var tasksPath = Path.Combine(lab.Tree.Repository, "tasks.jsonc");
        File.WriteAllText(tasksPath, tasks.ToJsonString());
        File.WriteAllText(Path.Combine(lab.Tree.Repository, "schema.json"), StructuredLab.Schema);
        using var harness = new HarnessProcess(lab.Tree);

        var scratchParent = Directory.CreateTempSubdirectory("dispatch-projectless-").FullName;
        try
        {
            foreach (var name in new[] { "TEMP", "TMP", "TMPDIR" }) lab.Environment[name] = scratchParent;
            (await harness.RunAsync(["run", tasksPath], ct, lab.Environment)).RequireSuccess("fresh projectless run");

            var record = JsonNode.Parse(File.ReadAllText(Path.Combine(lab.TaskFolder, "task.json")))!;
            var scratch = record["workspace"]!["path"]!.GetValue<string>();
            await Assert.That(Path.GetFileName(scratch)).StartsWith("dispatch-scratch-");
            // The harness allocates under the resolved temp root (macOS links /var into /private/var).
            var resolvedParent = ScratchFixture.CanonicalDirectory(scratchParent);
            await Assert.That(Path.GetRelativePath(ScratchFixture.CanonicalDirectory(Path.GetTempPath()), scratch).StartsWith("..", StringComparison.Ordinal)).IsFalse();
            await Assert.That(Path.GetDirectoryName(scratch)).IsEqualTo(resolvedParent);
            await Assert.That(Directory.Exists(scratch)).IsFalse();
            await Assert.That(lab.Result["state"]!.GetValue<string>()).IsEqualTo("done");
            await Assert.That(lab.Result["deliverable"]!.GetValue<string>()).IsEqualTo("deliverable.json");
            await Assert.That(JsonNode.DeepEquals(JsonNode.Parse(File.ReadAllText(Path.Combine(lab.TaskFolder, "deliverable.json"))), JsonNode.Parse(StructuredLab.Valid))).IsTrue();
            var directory = backend == "codex"
                ? lab.CodexInput().Single(x => x["method"]?.GetValue<string>() == "thread/start")["params"]!["cwd"]!.GetValue<string>()
                : File.ReadLines(FakeOpenCode.Record(lab.Tree)).Select(x => JsonNode.Parse(x)!)
                    .Single(x => x["method"]!.GetValue<string>() == "POST" && x["path"]!.GetValue<string>() == "/api/session")["body"]!["location"]!["directory"]!.GetValue<string>();
            await Assert.That(directory).IsEqualTo(scratch);
        }
        finally
        {
            Directory.Delete(scratchParent, recursive: true);
        }
    }

    [Test]
    [Arguments("codex", "collect", "input.json")]
    [Arguments("codex", "resume", "input.json")]
    [Arguments("codex", "collect", "result.schema.json")]
    [Arguments("codex", "resume", "result.schema.json")]
    [Arguments("opencode", "collect", "input.json")]
    [Arguments("opencode", "resume", "input.json")]
    [Arguments("opencode", "collect", "result.schema.json")]
    [Arguments("opencode", "resume", "result.schema.json")]
    [Timeout(180_000)]
    public async Task Structured_control_commands_refuse_changed_frozen_copies_before_backend_start(string backend, string command, string name, CancellationToken ct)
    {
        using var lab = new StructuredLab(backend);
        var repo = await LabRepo.CreateAsync(lab.Tree, ct);
        using var first = new HarnessProcess(lab.Tree);
        (await first.RunAsync(["run", lab.Tasks, "--repo", repo.Root], ct, lab.Environment)).RequireSuccess("initial structured run");
        var run = Path.GetFileName(lab.RunFolder);
        if (command == "resume")
            File.AppendAllText(Path.Combine(lab.RunFolder, "events.jsonl"), new JsonObject
            {
                ["schemaVersion"] = 2, ["run"] = run, ["task"] = "answer", ["type"] = "interrupted", ["time"] = Json.Timestamp(),
            }.ToJsonString() + "\n");
        File.AppendAllText(Path.Combine(lab.TaskFolder, name), " ");
        var evidence = Directory.GetFiles(lab.RunFolder, "*", SearchOption.AllDirectories).ToDictionary(x => x, File.ReadAllBytes);
        var calls = backend == "codex" ? lab.CodexInput().Length : File.ReadAllLines(FakeOpenCode.Record(lab.Tree)).Length;
        using var second = new HarnessProcess(lab.Tree);

        var result = await second.RunAsync(command == "collect" ? [command, run, "answer"] : [command, run, "--continue"], ct, lab.Environment);

        await Assert.That(result.ExitCode).IsEqualTo(2);
        await Assert.That(result.Stderr).Contains("answer");
        await Assert.That(result.Stderr).Contains(name);
        await Assert.That(result.Stderr).Contains("SHA-256 mismatch");
        foreach (var (path, bytes) in evidence) await Assert.That(File.ReadAllBytes(path).SequenceEqual(bytes)).IsTrue();
        await Assert.That(backend == "codex" ? lab.CodexInput().Length : File.ReadAllLines(FakeOpenCode.Record(lab.Tree)).Length).IsEqualTo(calls);
    }

    [Test]
    [Arguments("codex")]
    [Arguments("opencode")]
    [Timeout(180_000)]
    public async Task Structured_timeout_settles_failed_without_a_deliverable(string backend, CancellationToken ct)
    {
        using var lab = new StructuredLab(backend, null, "timeout");
        var repo = await LabRepo.CreateAsync(lab.Tree, ct);
        var tasks = JsonNode.Parse(File.ReadAllText(lab.Tasks))!;
        tasks["tasks"]![0]!["timeoutMinutes"] = 1;
        File.WriteAllText(lab.Tasks, tasks.ToJsonString());
        using var harness = new HarnessProcess(lab.Tree);
        var result = await harness.RunAsync(["run", lab.Tasks, "--repo", repo.Root], ct, lab.Environment);
        await Assert.That(result.ExitCode).IsEqualTo(1);
        await Assert.That(lab.Result["state"]!.GetValue<string>()).IsEqualTo("failed");
        await Assert.That(lab.Result["outcome"]!.GetValue<string>()).IsEqualTo("timeout");
        await Assert.That(lab.Result["timedOut"]!.GetValue<bool>()).IsTrue();
        await Assert.That(lab.Result["failures"]!.AsArray().Select(x => x!.GetValue<string>())).Contains("outcome:timeout");
        await Assert.That(lab.Result["validation"]!["ok"]!.GetValue<bool>()).IsFalse();
        await Assert.That(File.Exists(Path.Combine(lab.TaskFolder, "deliverable.json"))).IsFalse();
        await Assert.That(File.ReadAllText(Path.Combine(lab.TaskFolder, "final.md"))).Contains("timeoutMinutes: 1");
        var pids = backend == "codex"
            ? Directory.GetFiles(Path.Combine(lab.Tree.Root, "record"), "start.json", SearchOption.AllDirectories)
                .Select(x => JsonNode.Parse(File.ReadAllText(x))!["pid"]!.GetValue<int>())
            : Directory.GetFiles(Path.GetDirectoryName(FakeOpenCode.Record(lab.Tree))!, "requests.jsonl.*.host.json")
                .Select(x => JsonNode.Parse(File.ReadAllText(x))!["pid"]!.GetValue<int>());
        foreach (var pid in pids) await Assert.That(IsAlive(pid)).IsFalse();
    }

    [Test]
    [Arguments("codex", "no-final", "no-final-text")]
    [Arguments("opencode", "no-final", "no-final-text")]
    [Arguments("codex", "lost", "outcome:lost")]
    [Timeout(180_000)]
    public async Task Structured_missing_final_or_lost_backend_settles_failed(string backend, string scenario, string failure, CancellationToken ct)
    {
        using var lab = new StructuredLab(backend, null, scenario == "lost" ? "lost" : "succeeded");
        var repo = await LabRepo.CreateAsync(lab.Tree, ct);
        using var harness = new HarnessProcess(lab.Tree);
        var result = await harness.RunAsync(["run", lab.Tasks, "--repo", repo.Root], ct, lab.Environment);
        await Assert.That(result.ExitCode).IsEqualTo(1);
        await Assert.That(lab.Result["state"]!.GetValue<string>()).IsEqualTo("failed");
        await Assert.That(lab.Result["failures"]!.AsArray().Select(x => x!.GetValue<string>())).Contains(failure);
        await Assert.That(lab.Result["validation"]!["ok"]!.GetValue<bool>()).IsFalse();
        await Assert.That(File.Exists(Path.Combine(lab.TaskFolder, "deliverable.json"))).IsFalse();
    }

    [Test]
    [Arguments("codex", "missing-file")]
    [Arguments("opencode", "missing-file")]
    [Arguments("codex", "unknown-kind")]
    [Arguments("opencode", "unknown-kind")]
    [Arguments("codex", "missing-route")]
    [Arguments("opencode", "missing-route")]
    [Timeout(180_000)]
    public async Task Structured_invalid_plan_is_refused_before_starting_a_backend(string backend, string scenario, CancellationToken ct)
    {
        using var lab = new StructuredLab(backend);
        var repo = await LabRepo.CreateAsync(lab.Tree, ct);
        if (scenario == "missing-file") File.Delete(lab.Tasks);
        else
        {
            var tasks = JsonNode.Parse(File.ReadAllText(lab.Tasks))!;
            if (scenario == "unknown-kind") tasks["tasks"]![0]!["kind"] = "unknown";
            else
            {
                tasks["tasks"]![0]!.AsObject().Remove("route");
                var path = Path.Combine(lab.Tree.DispatchHome, "config", "config.jsonc");
                var config = JsonNode.Parse(File.ReadAllText(path))!;
                config["preferences"]?.AsObject().Remove("defaultRoutes");
                File.WriteAllText(path, config.ToJsonString());
            }
            File.WriteAllText(lab.Tasks, tasks.ToJsonString());
        }
        using var harness = new HarnessProcess(lab.Tree);
        var result = await harness.RunAsync(["run", lab.Tasks, "--repo", repo.Root], ct, lab.Environment);
        await Assert.That(result.ExitCode).IsEqualTo(2);
        if (scenario == "missing-route") await Assert.That(result.Stdout + result.Stderr).Contains("answer");
        await Assert.That(Directory.Exists(Path.Combine(lab.Tree.DispatchHome, "state", "ledgers"))).IsFalse();
        await Assert.That(Directory.Exists(Path.Combine(lab.Tree.Root, "record"))).IsFalse();
        await Assert.That(File.Exists(FakeOpenCode.Record(lab.Tree))).IsFalse();
    }

    [Test]
    [Arguments("codex")]
    [Arguments("opencode")]
    [Timeout(180_000)]
    public async Task Structured_recorded_run_is_refused_without_changing_evidence_or_starting_a_backend(string backend, CancellationToken ct)
    {
        using var lab = new StructuredLab(backend);
        var repo = await LabRepo.CreateAsync(lab.Tree, ct);
        using var first = new HarnessProcess(lab.Tree);
        (await first.RunAsync(["run", lab.Tasks, "--repo", repo.Root], ct, lab.Environment)).RequireSuccess("first structured run");
        var evidence = Directory.GetFiles(lab.RunFolder, "*", SearchOption.AllDirectories).ToDictionary(x => x, File.ReadAllBytes);
        var starts = backend == "codex" ? lab.CodexInput().Length : File.ReadAllLines(FakeOpenCode.Record(lab.Tree)).Length;
        using var second = new HarnessProcess(lab.Tree);
        var result = await second.RunAsync(["run", lab.Tasks, "--repo", repo.Root], ct, lab.Environment);
        await Assert.That(result.ExitCode).IsEqualTo(2);
        await Assert.That(result.Stdout + result.Stderr).Contains("new run id");
        foreach (var (path, bytes) in evidence) await Assert.That(File.ReadAllBytes(path).SequenceEqual(bytes)).IsTrue();
        await Assert.That(backend == "codex" ? lab.CodexInput().Length : File.ReadAllLines(FakeOpenCode.Record(lab.Tree)).Length).IsEqualTo(starts);
    }

    public static IEnumerable<(string Backend, string Text, string At, string Message)> InvalidAnswers()
    {
        foreach (var backend in new[] { "codex", "opencode" })
        {
            yield return (backend, "not JSON", "", "parse");
            yield return (backend, "{\"items\":[]}", "/items", "i1");
            yield return (backend, StructuredLab.Valid.Replace("i1", "unknown", StringComparison.Ordinal), "/items/0/id", "unknown");
            yield return (backend, StructuredLab.Valid.Replace("title", "changed", StringComparison.Ordinal), "/items/0/key", "immutable");
            yield return (backend, StructuredLab.Valid.Replace("\"text\":\"" + StructuredLab.Text + "\"", "\"text\":42", StringComparison.Ordinal), "/items/0/text", "type");
            var item = JsonNode.Parse(StructuredLab.Valid)!["items"]![0]!.ToJsonString();
            yield return (backend, "{\"items\":[" + item + "," + item + "]}", "/items/1/id", "duplicate");
        }
    }

    [Test]
    [MethodDataSource(nameof(InvalidAnswers))]
    [Timeout(180_000)]
    public async Task Structured_invalid_run_records_the_named_validation_failure(string backend, string text, string at, string message, CancellationToken ct)
    {
        using var lab = new StructuredLab(backend, text);
        var repo = await LabRepo.CreateAsync(lab.Tree, ct);
        using var harness = new HarnessProcess(lab.Tree);
        var result = await harness.RunAsync(["run", lab.Tasks, "--repo", repo.Root], ct, lab.Environment);
        await Assert.That(result.ExitCode).IsEqualTo(1);
        await Assert.That(lab.Result["state"]!.GetValue<string>()).IsEqualTo("failed");
        await Assert.That(lab.Result["failures"]!.AsArray().Select(x => x!.GetValue<string>())).IsEquivalentTo(new[] { "invalid-output" });
        await Assert.That(lab.Result["validation"]).IsNotNull();
        await Assert.That(lab.Result["validation"]!["ok"]!.GetValue<bool>()).IsFalse();
        await Assert.That(lab.Result["validation"]!["errors"]!.AsArray()).Contains(x => x!["at"]!.GetValue<string>() == at && x["message"]!.GetValue<string>().Contains(message, StringComparison.Ordinal));
        await Assert.That(lab.Result.ContainsKey("deliverable")).IsFalse();
        await Assert.That(File.Exists(Path.Combine(lab.TaskFolder, "deliverable.json"))).IsFalse();
        await Assert.That(File.ReadAllText(Path.Combine(lab.TaskFolder, "final.md"))).Contains(text);
        var validation = File.ReadLines(Path.Combine(lab.RunFolder, "events.jsonl")).Select(x => JsonNode.Parse(x)!)
            .Single(x => x["type"]!.GetValue<string>() == "validated")["data"]!;
        await Assert.That(JsonNode.DeepEquals(validation, lab.Result["validation"])).IsTrue();
    }

    [Test]
    [Arguments("codex")]
    [Arguments("opencode")]
    [Timeout(180_000)]
    public async Task Structured_projectless_plan_refuses_candidates_beneath_a_git_ancestor(string backend, CancellationToken ct)
    {
        using var lab = new StructuredLab(backend);
        File.WriteAllText(Path.Combine(lab.Tree.Root, ".git"), "fixture");
        var tasks = JsonNode.Parse(File.ReadAllText(lab.Tasks))!;
        tasks["tasks"]![0]!["project"] = "off";
        tasks["tasks"]![0]!.AsObject().Remove("base");
        File.WriteAllText(lab.Tasks, tasks.ToJsonString());
        using var harness = new HarnessProcess(lab.Tree);
        var result = await harness.RunAsync(["plan", lab.Tasks], ct, lab.Environment);
        await Assert.That(result.ExitCode).IsEqualTo(2);
        await Assert.That(result.Stdout + result.Stderr).Contains("scratch");
        await Assert.That(result.Stdout + result.Stderr).Contains(".git");
        await Assert.That(Directory.Exists(Path.Combine(lab.Tree.DispatchHome, "state", "ledgers"))).IsFalse();
    }

    [Test]
    [Arguments("codex")]
    [Arguments("opencode")]
    [Timeout(180_000)]
    public async Task Structured_valid_run_writes_the_validated_deliverable(string backend, CancellationToken ct)
    {
        using var lab = new StructuredLab(backend);
        var repo = await LabRepo.CreateAsync(lab.Tree, ct);
        using var harness = new HarnessProcess(lab.Tree);
        var result = await harness.RunAsync(["run", lab.Tasks, "--repo", repo.Root], ct, lab.Environment);
        result.RequireSuccess("structured run");
        await Assert.That(lab.Result["state"]!.GetValue<string>()).IsEqualTo("done");
        await Assert.That(lab.Result["kind"]!.GetValue<string>()).IsEqualTo("structured");
        await Assert.That(lab.Result["backend"]!.GetValue<string>()).IsEqualTo(backend);
        await Assert.That(lab.Result["deliverable"]!.GetValue<string>()).IsEqualTo("deliverable.json");
        await Assert.That(lab.Result["validation"]!["ok"]!.GetValue<bool>()).IsTrue();
        var document = JsonNode.Parse(File.ReadAllText(Path.Combine(lab.TaskFolder, "deliverable.json")))!;
        await Assert.That(document["items"]!.AsArray().Count).IsEqualTo(1);
        await Assert.That(Encoding.UTF8.GetBytes(document["items"]![0]!["text"]!.GetValue<string>()).SequenceEqual(Encoding.UTF8.GetBytes(StructuredLab.Text))).IsTrue();
        await Assert.That(JsonNode.DeepEquals(document, JsonNode.Parse(StructuredLab.Valid))).IsTrue();
        await Assert.That(lab.Result.ContainsKey("commit")).IsFalse();
        await Assert.That(lab.Result.ContainsKey("commitMode")).IsFalse();
        await Assert.That(lab.Result["verify"]!.AsArray()).IsEmpty();
        var events = File.ReadLines(Path.Combine(lab.RunFolder, "events.jsonl")).Select(x => JsonNode.Parse(x)!).ToArray();
        await Assert.That(events.Single(x => x["type"]!.GetValue<string>() == "validated")["data"]!["ok"]!.GetValue<bool>()).IsTrue();
        if (backend == "codex")
        {
            var turn = lab.CodexInput().Single(x => x["method"]?.GetValue<string>() == "turn/start")["params"]!;
            await Assert.That(JsonNode.DeepEquals(turn["outputSchema"], JsonNode.Parse(StructuredLab.Schema))).IsTrue();
        }
    }

    static bool IsAlive(int pid)
    {
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException) { return false; }
    }
}
