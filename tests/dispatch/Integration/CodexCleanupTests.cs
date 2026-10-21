using Dispatch.Tests.Support;

namespace Dispatch.Tests.Integration;

[Category("Integration")]
[NotInParallel]
public sealed class CodexCleanupTests
{
    [Test]
    [Timeout(180_000)]
    public async Task Unconfirmed_release_keeps_the_connection_and_clean_home_for_a_retry(CancellationToken cancellationToken)
    {
        using var lab = new CodexLab();
        File.WriteAllText(Path.Combine(lab.Environment["CODEX_HOME"], "auth.json"), "{\"synthetic\":true}");
        lab.Write("task-edit", CodexLab.Handshake + CodexLab.Thread.Split("{\"expect\":\"turn/start\"")[0]);
        await using var backend = lab.Backend();
        var created = await backend.CreateAsync(Launch(lab, "edit"), cancellationToken);
        var home = created.Worker.State["codexHome"]!.GetValue<string>();
        var stop = HoldStop(backend, created.Worker);
        var release = backend.ReleaseAsync(created.Worker, cancellationToken);
        stop.SetResult(new(false, "process stop not confirmed"));

        var failure = await Assert.ThrowsAsync<HarnessException>(() => release);
        await Assert.That(failure!.Message).Contains("unconfirmed");
        await Assert.That(Directory.Exists(home)).IsTrue();
        await Assert.That((await backend.InterruptAsync(created.Worker, InterruptKind.Hard, cancellationToken)).Stopped).IsTrue();
        await backend.ReleaseAsync(created.Worker, cancellationToken);
        await Assert.That(Directory.Exists(home)).IsFalse();
    }

    [Test]
    [Timeout(180_000)]
    public async Task Unconfirmed_scope_cleanup_keeps_its_folder_and_still_stops_other_connections(CancellationToken cancellationToken)
    {
        using var core = new CoreLab();
        using var lab = new CodexLab(core.Tree);
        File.WriteAllText(Path.Combine(lab.Environment["CODEX_HOME"], "auth.json"), "{\"synthetic\":true}");
        await using var unrelatedScope = new BackendScope([]);
        await using var scope = new BackendScope([]);
        var folder = scope.OwnedFolder!;
        var profiles = Path.Combine(folder, "profiles");
        var backend = new CodexBackend(new(profiles, CodexLab.Executable, new Dictionary<string, string>()), lab.Environment);
        ((IDictionary<string, IWorkerBackend>)scope.Backends).Add("codex", backend);
        var workers = new List<WorkerRef>();
        foreach (var id in new[] { "first", "second" })
        {
            lab.Write("task-" + id, CodexLab.Handshake + CodexLab.Thread.Split("{\"expect\":\"turn/start\"")[0]
                .Replace("$threadId", "thread-" + id));
            workers.Add((await backend.CreateAsync(Launch(lab, id), cancellationToken)).Worker);
        }
        var failedWorker = backend.WorkerProcesses.First().Worker;
        var otherWorker = workers.Single(worker => worker.Id != failedWorker.Id);
        var stop = HoldStop(backend, failedWorker);
        var disposing = scope.DisposeAsync().AsTask();
        stop.SetResult(new(false, "process stop not confirmed"));

        var failures = await Assert.ThrowsAsync<AggregateException>(() => disposing);
        await Assert.That(failures!.Flatten().InnerExceptions.Count).IsEqualTo(1);
        await Assert.That(Directory.Exists(folder)).IsTrue();
        await Assert.That(Directory.Exists(failedWorker.State["codexHome"]!.GetValue<string>())).IsTrue();
        await Assert.That(Directory.Exists(otherWorker.State["codexHome"]!.GetValue<string>())).IsFalse();
        // Each failed stop must be retryable; the second disposal confirms both exits before deletion.
        await scope.DisposeAsync();
        await Assert.That(Directory.Exists(folder)).IsFalse();
        await Assert.That(Directory.Exists(unrelatedScope.OwnedFolder)).IsTrue();
    }

    static WorkerLaunch Launch(CodexLab lab, string id) => new("run", id, "code", new("codex", "model-y", "high"), null, null,
        new("clean", "on", "workspace", "on", "worktree"), lab.Tree.Repository, new([], []), lab.Environment, null, "clean", null);

    static TaskCompletionSource<InterruptResult> HoldStop(CodexBackend backend, WorkerRef worker)
    {
        // Fault injection only: the real process still owns and stops its OS resources on retry.
        var process = backend.WorkerProcesses.Single(connection => connection.Worker.Id == worker.Id).Process;
        var completion = new TaskCompletionSource<InterruptResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        process.SetPendingStop(completion.Task);
        return completion;
    }
}
