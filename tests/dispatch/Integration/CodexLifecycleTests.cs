using System.Diagnostics;
using System.Text.Json.Nodes;
using Dispatch.Tests.Support;
using Microsoft.Extensions.Time.Testing;

namespace Dispatch.Tests.Integration;

[Category("Integration")]
[NotInParallel]
public sealed class CodexLifecycleTests
{
    [Test]
    [Timeout(180_000)]
    public async Task Task_start_forwards_the_structured_schema_to_the_native_backend(CancellationToken cancellationToken)
    {
        using var core = new CoreLab();
        using var codex = new CodexLab(core.Tree);
        var run = await core.TaskAsync("edit", cancellationToken);
        run.Record.Spec.Kind = "structured";
        run.Record.Spec.RawPrompt = false;
        run.Record.Spec.Route = "model-y#high";
        run.Record.Settings.Backend = new() { Value = "codex" };
        run.Record.Settings.Route = new() { Value = "codex:model-y#high" };
        run.Record.Settings.Access = new() { Value = "read-only" };
        var schema = JsonNode.Parse("{\"type\":\"object\",\"additionalProperties\":false,\"properties\":{},\"required\":[]}")!.AsObject();
        run.Record.PromptInputs = new([], new JsonArray(), schema);
        codex.Write("preflight", CodexLab.Handshake + CodexLab.Thread.Replace("workspaceWrite", "readOnly") + CodexLab.Final);
        await using var backend = codex.Backend();
        await new TaskStarter(backend, core.Ledger, core.Tree.Repository, AllowList.From(null), core.Policy, core.Git).StartAsync(run, cancellationToken);
        await backend.CollectAsync(run.SavedWorker(), cancellationToken);
        var input = codex.Input().Single(x => x["method"]?.GetValue<string>() == "turn/start")["params"]!;
        await Assert.That(JsonNode.DeepEquals(input["outputSchema"], schema)).IsTrue();
        var prompt = input["input"]![0]!["text"]!.GetValue<string>();
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(prompt))).ToLowerInvariant();
        await Assert.That(core.Ledger.Read().Single(x => x.Type == "prompt").Data!["sha256"]!.GetValue<string>()).IsEqualTo(hash);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    [Timeout(180_000)]
    public async Task Timeout_stops_the_task_and_wrapper_tree_before_settling(bool hardStop, CancellationToken cancellationToken)
    {
        using var core = new CoreLab();
        using var codex = new CodexLab(core.Tree);
        var run = await core.TaskAsync("edit", cancellationToken);
        run.Record.Spec.TimeoutMinutes = 1;
        run.Record.Settings.Backend = new() { Value = "codex" };
        run.Record.Settings.Route = new() { Value = "codex:model-y#high" };
        run.Record.Spec.Route = "model-y#high";
        run.Record.Settings.Access = new() { Value = "workspace" };
        codex.Write("preflight", CodexLab.Handshake + CodexLab.Thread + (hardStop ? """
            {"spawnChild":true,"setsid":true}
            {"notify":"item/started","params":{"threadId":"$threadId","turnId":"$turnId","item":{"id":"ready","type":"commandExecution","command":"wait"}}}
            {"sleep":30000}
            {"writeFile":{"path":"$cwd/late.txt","base64":"bGF0ZQ=="}}
            {"hang":true}
            """ : """
            {"expect":"turn/interrupt","params":{"threadId":"$threadId","turnId":"$turnId"},"reply":{}}
            {"notify":"turn/completed","params":{"threadId":"$threadId","turn":{"id":"$turnId","status":"interrupted"}}}
            """));
        await using var backend = codex.Backend();
        var time = new FakeTimeProvider();
        var scheduler = new Scheduler(new Dictionary<string, IWorkerBackend> { ["codex"] = backend }, core.Ledger, core.Tree.Repository,
            AllowList.From(null), core.Policy, 1, cancellationToken, core.Git, time);
        var running = scheduler.RunAsync([run]);
        try
        {
            await UntilAsync(() => run.State == TaskState.Running, cancellationToken);
            if (hardStop) await UntilAsync(() => Directory.GetFiles(codex.Records, "start.json", SearchOption.AllDirectories).Length == 2, cancellationToken);
            await backend.PollAsync([run.SavedWorker()], cancellationToken);
            await Assert.That(run.Worker!.State["descendants"] is JsonArray).IsTrue();
            var processes = Directory.GetFiles(codex.Records, "start.json", SearchOption.AllDirectories)
                .Select(x => JsonNode.Parse(File.ReadAllText(x))!["pid"]!.GetValue<int>()).ToArray();
            time.Advance(TimeSpan.FromSeconds(59));
            await Assert.That(core.Ledger.Read().Where(x => x.Type == "timeout")).IsEmpty();
            time.Advance(TimeSpan.FromSeconds(1));
            await DriveUntilAsync(() => core.Ledger.Read().Any(x => x.Type == "timeout"), time, cancellationToken);
            if (hardStop) time.Advance(TimeSpan.FromMinutes(2));
            await DriveUntilAsync(() => running.IsCompleted, time, cancellationToken);
            await Assert.That(await running).IsEqualTo(1);
            var result = core.Ledger.LoadResult("edit")!;
            await Assert.That(result.State).IsEqualTo("failed");
            await Assert.That(result.Outcome).IsEqualTo("timeout");
            await Assert.That(result.Failures).Contains("outcome:timeout");
            await Assert.That(File.Exists(Path.Combine(core.Tree.Repository, "late.txt"))).IsFalse();
            foreach (var pid in processes) await Assert.That(IsAlive(pid)).IsFalse();
            if (hardStop)
            {
                await Assert.That(result.WorkerStopped).IsTrue();
                await Assert.That(result.Failures).DoesNotContain("zombie");
                var stopped = core.Ledger.Read().Single(x => x.Type == "hard-stop");
                await Assert.That(stopped.Data!["kind"]!.GetValue<string>()).IsEqualTo("timeout");
                await Assert.That(stopped.Data["stopped"]!.GetValue<bool>()).IsTrue();
            }
        }
        finally
        {
            if (run.Worker is { } worker) await backend.InterruptAsync(worker, InterruptKind.Hard, CancellationToken.None);
            await DriveUntilAsync(() => running.IsCompleted, time, cancellationToken);
            await running;
        }
    }

    static bool IsAlive(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            if (process.HasExited) return false;
            if (!OperatingSystem.IsLinux()) return true;
            var stat = File.ReadAllText($"/proc/{pid}/stat");
            return stat[(stat.LastIndexOf(')') + 2)..].Split(' ')[0] != "Z";
        }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
    }

    static async Task UntilAsync(Func<bool> done, CancellationToken ct)
    {
        while (!done()) await Task.Delay(20, ct);
    }

    static async Task DriveUntilAsync(Func<bool> done, FakeTimeProvider time, CancellationToken ct)
    {
        while (!done())
        {
            time.Advance(TimeSpan.FromMilliseconds(20));
            await Task.Delay(20, ct);
        }
    }
}
