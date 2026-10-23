using Dispatch.Tests.Support;
using TUnit.Assertions.Enums;

namespace Dispatch.Tests.Integration;

[Category("Integration")]
[NotInParallel("processes")]
public sealed class PlanOutputTests
{
    [Test]
    [Arguments("plan", 0)]
    [Arguments("plan", 247)]
    [Arguments("plan", 248)]
    [Arguments("run", 248)]
    [Timeout(180_000)]
    public async Task Worktree_path_depth_warns_only_at_the_Windows_threshold(string command, int depth, CancellationToken ct)
    {
        using var lab = new CodexLab();
        var repo = await LabRepo.CreateAsync(lab.Tree, ct);
        await repo.GitAsync(["config", "core.longpaths", "true"], ct);
        var folder = Path.Combine(lab.Tree.WorktreeRoot, $"{DateTime.UtcNow:yyyy-MM-dd}-path-depth", "inspect");
        var file = depth == 0 ? "README.md" : new string('d', depth - folder.Length - 10) + "/leaf.txt";
        if (depth != 0)
        {
            await repo.GitAsync(["rm", "README.md"], ct);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(repo.Root, file))!);
            File.WriteAllText(Path.Combine(repo.Root, file), "tracked");
            await repo.GitAsync(["add", "--", file], ct);
            await repo.GitAsync(["commit", "-m", "Track path depth fixture"], ct);
        }
        var staged = file + ".staged-only";
        File.WriteAllText(Path.Combine(repo.Root, staged), "not at HEAD");
        await repo.GitAsync(["add", "--", staged], ct);
        File.WriteAllText(Path.Combine(lab.Environment["CODEX_HOME"], "auth.json"), "synthetic-auth");
        File.WriteAllText(lab.Tasks, """{"schemaVersion":2,"run":"path-depth","tasks":[{"id":"inspect","kind":"research","base":"main","project":"off","prompt":"Inspect."}]}""");
        lab.Write("task", CodexLab.Handshake + CodexLab.Thread.Replace("workspaceWrite", "readOnly") + CodexLab.Final);
        using var process = new HarnessProcess(lab.Tree);
        var result = await process.RunAsync([command, lab.Tasks, "--repo", repo.Root], ct,
            lab.Environment.ToDictionary(x => x.Key, x => (string?)x.Value));
        result.RequireSuccess(command);
        var length = folder.Length + 1 + file.Length;
        var expected = $"WARN inspect: worktree paths reach {length} characters ({file.Replace('/', '\\')}); builds can fail past 260 on Windows. Set locations.worktreeRoot to a short folder (for example C:\\wt) in the user config";
        await Assert.That(Lines(result).Where(line => line.Contains(": worktree paths reach ", StringComparison.Ordinal)).ToArray())
            .IsEquivalentTo(OperatingSystem.IsWindows() && length >= 248 ? new[] { expected } : [], CollectionOrdering.Matching);
        await Assert.That(await repo.GitAsync(["diff", "--cached", "--name-only"], ct)).IsEqualTo(staged);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    [Timeout(180_000)]
    public async Task Plan_prints_resolved_temp_paths(bool configured, CancellationToken ct)
    {
        using var tree = new TempTree();
        var repo = await LabRepo.CreateAsync(tree, ct);
        var root = Path.Combine(tree.Root, "private-temp");
        var tasks = Path.Combine(tree.Root, "tasks.jsonc");
        var document = System.Text.Json.Nodes.JsonNode.Parse("""{"schemaVersion":2,"run":"ignored","defaults":{"backend":"opencode"},"tasks":[{"id":"zulu","kind":"research","base":"main","route":"fixture/model","prompt":"Inspect."},{"id":"alpha","kind":"research","base":"main","route":"fixture/model","prompt":"Inspect."},{"id":"structured","kind":"structured","route":"fixture/model","project":"on","prompt":"Transform.","input":{"items":[{"id":"a"}]},"result":{"schema":"schema.json","itemsAt":""}}]}""")!;
        if (configured) document["tasks"]![0]!["tempRoot"] = root;
        File.WriteAllText(Path.Combine(tree.Root, "schema.json"), """{"type":"array"}""");
        File.WriteAllText(tasks, document.ToJsonString());
        using var process = new HarnessProcess(tree);
        var result = await process.RunAsync(["plan", tasks, "--repo", repo.Root, "--run", "preview"], ct,
            FakeOpenCode.Environment(tree, "one-task.jsonl").ToDictionary(x => x.Key, x => (string?)x.Value));
        result.RequireSuccess("plan");
        var run = $"{DateTime.UtcNow:yyyy-MM-dd}-preview";
        var defaultRoot = Path.Combine(tree.DispatchHome, "state", "worktrees");
        var zulu = configured ? Path.Combine(root, run, "zulu") : Path.Combine(defaultRoot, run, ".tmp", "zulu");
        await Assert.That(Lines(result).Where(line => line.Contains(": temp ", StringComparison.Ordinal)).ToArray()).IsEquivalentTo(new[]
        {
            $"INFO zulu: temp {zulu} {(configured ? "(tempRoot from task)" : "(default)")}",
            $"INFO alpha: temp {Path.Combine(defaultRoot, run, ".tmp", "alpha")} (default)",
        }, CollectionOrdering.Matching);
        await Assert.That(Directory.Exists(root)).IsFalse();
        await Assert.That(Directory.Exists(Path.Combine(defaultRoot, run))).IsFalse();
    }

    [Test]
    [Arguments("plan")]
    [Arguments("run")]
    [Timeout(180_000)]
    public async Task Each_codex_approval_gate_warning_prints_once(string command, CancellationToken ct)
    {
        using var lab = new CodexLab();
        var repo = await LabRepo.CreateAsync(lab.Tree, ct);
        File.WriteAllText(Path.Combine(lab.Environment["CODEX_HOME"], "auth.json"), "synthetic-auth");
        File.WriteAllText(lab.Tasks, """{"schemaVersion":2,"run":"gate-warnings","tasks":[{"id":"inspect","kind":"research","base":"main","project":"off","prompt":"Inspect."}]}""");
        var policy = Path.Combine(lab.Tree.Root, "policy.jsonc");
        File.WriteAllText(policy, """{"schemaVersion":2,"restrictions":{"escalate":[{"name":"push","action":"shell","resources":["git push*"]}],"deny":[{"name":"gh","action":"shell","resources":["gh *"]}]}}""");
        lab.Write("task", CodexLab.Handshake + CodexLab.Thread.Replace("workspaceWrite", "readOnly") + CodexLab.Final);
        using var process = new HarnessProcess(lab.Tree);
        var result = await process.RunAsync([command, lab.Tasks, "--repo", repo.Root, "--policy", policy], ct,
            lab.Environment.ToDictionary(x => x.Key, x => (string?)x.Value));
        result.RequireSuccess(command);
        foreach (var rule in new[] { "cli/push", "cli/gh" })
            await Assert.That(Lines(result).Count(line => line == $"WARN inspect: {rule} is enforced at the approval gate only")).IsEqualTo(1);
    }

    [Test]
    [Timeout(180_000)]
    public async Task Codex_plan_reports_a_sandboxed_shell_only_for_its_read_only_task(CancellationToken ct)
    {
        using var lab = new CodexLab();
        var repo = await LabRepo.CreateAsync(lab.Tree, ct);
        File.WriteAllText(Path.Combine(lab.Environment["CODEX_HOME"], "auth.json"), "synthetic-auth");
        File.WriteAllText(lab.Tasks, """{"schemaVersion":2,"tasks":[{"id":"inspect","kind":"research","base":"main","prompt":"Inspect."},{"id":"edit","kind":"code","base":"main","prompt":"Edit.","scope":["a.txt"]}]}""");
        using var process = new HarnessProcess(lab.Tree);
        var result = await process.RunAsync(["plan", lab.Tasks, "--repo", repo.Root], ct,
            lab.Environment.ToDictionary(x => x.Key, x => (string?)x.Value));
        result.RequireSuccess("plan");
        var lines = Lines(result);
        await Assert.That(lines).Contains("INFO inspect: sandboxed shell; escalations declined (readOnlyShell decline, from tool)");
        await Assert.That(lines.Any(line => line.StartsWith("INFO edit:", StringComparison.Ordinal) && line.Contains("shell", StringComparison.Ordinal))).IsFalse();
        await Assert.That(lines.Any(line => line.StartsWith("WARN inspect: The pinned Codex app-server", StringComparison.Ordinal))).IsTrue();
        await Assert.That(lines.Any(line => line.Contains("readOnlyShell", StringComparison.Ordinal) && !line.StartsWith("INFO ", StringComparison.Ordinal))).IsFalse();
        // Codex's catalog lists no context window; plan says when it becomes known.
        await Assert.That(lines.Count(line => line.StartsWith("INFO inspect: context window of codex:", StringComparison.Ordinal)
            && line.EndsWith("is known after the run (result.json contextWindow)", StringComparison.Ordinal))).IsEqualTo(1);
    }

    [Test]
    [Timeout(180_000)]
    public async Task Opencode_plan_reports_no_shell_only_for_its_read_only_task(CancellationToken ct)
    {
        using var tree = new TempTree();
        var repo = await LabRepo.CreateAsync(tree, ct);
        var tasks = Path.Combine(tree.Root, "tasks.jsonc");
        File.WriteAllText(tasks, """{"schemaVersion":2,"defaults":{"backend":"opencode"},"tasks":[{"id":"inspect","kind":"research","base":"main","route":"fixture/model","prompt":"Inspect."},{"id":"edit","kind":"code","base":"main","route":"fixture/model","prompt":"Edit.","scope":["a.txt"]}]}""");
        using var process = new HarnessProcess(tree);
        var result = await process.RunAsync(["plan", tasks, "--repo", repo.Root], ct,
            FakeOpenCode.Environment(tree, "one-task.jsonl").ToDictionary(x => x.Key, x => (string?)x.Value));
        result.RequireSuccess("plan");
        var lines = Lines(result);
        await Assert.That(lines).Contains("INFO inspect: no shell for a read-only task (readOnlyShell decline, from tool)");
        await Assert.That(lines.Any(line => line.StartsWith("INFO edit:", StringComparison.Ordinal) && line.Contains("shell", StringComparison.Ordinal))).IsFalse();
        // The fixture catalog's model limit is 8192 tokens.
        foreach (var task in new[] { "inspect", "edit" })
            await Assert.That(lines.Count(line => line.StartsWith($"INFO {task}: context window 8192 tokens (opencode:fixture/model", StringComparison.Ordinal))).IsEqualTo(1);
    }

    [Test]
    [Timeout(180_000)]
    public async Task Plan_errors_carry_the_ERROR_level_and_refuse_the_plan(CancellationToken ct)
    {
        using var lab = new CodexLab();
        var repo = await LabRepo.CreateAsync(lab.Tree, ct);
        File.WriteAllText(Path.Combine(lab.Environment["CODEX_HOME"], "auth.json"), "synthetic-auth");
        File.WriteAllText(lab.Tasks, """{"schemaVersion":2,"tasks":[{"id":"inspect","kind":"research","base":"missing","prompt":"Inspect."}]}""");
        using var process = new HarnessProcess(lab.Tree);
        var result = await process.RunAsync(["plan", lab.Tasks, "--repo", repo.Root], ct,
            lab.Environment.ToDictionary(x => x.Key, x => (string?)x.Value));
        await Assert.That(result.ExitCode).IsEqualTo(2);
        await Assert.That(Lines(result).Any(line => line.StartsWith("ERROR inspect: base missing does not resolve", StringComparison.Ordinal))).IsTrue();
        await Assert.That(Lines(result).Any(line => line.StartsWith("WARN inspect: The pinned Codex app-server", StringComparison.Ordinal))).IsTrue();
        await Assert.That(Lines(result)).Contains("plan refused: 1 error(s)");
    }

    [Test]
    [Timeout(180_000)]
    public async Task Plan_warns_when_path_depth_cannot_be_measured_and_caches_the_failure(CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows()) Skip.Test("Worktree path depth is Windows-only.");
        using var lab = new CodexLab();
        var repo = await LabRepo.CreateAsync(lab.Tree, ct);
        await repo.GitAsync(["symbolic-ref", "HEAD", "refs/heads/unborn"], ct);
        using var git = new CapturedProcess();
        var failed = await git.RunAsync("git", ["read-tree", "HEAD"], repo.Root, lab.Tree.ChildEnvironment(), ct);
        await Assert.That(failed.ExitCode).IsNotEqualTo(0);
        var reason = $"git read-tree HEAD failed in {repo.Root}: {failed.Stderr.Trim()}";
        File.WriteAllText(Path.Combine(lab.Environment["CODEX_HOME"], "auth.json"), "synthetic-auth");
        File.WriteAllText(lab.Tasks, """{"schemaVersion":2,"tasks":[{"id":"z-inspect","kind":"research","base":"main","project":"off","prompt":"Inspect."},{"id":"a","kind":"research","base":"main","project":"off","prompt":"Inspect."}]}""");
        lab.Environment["GIT_TRACE"] = Path.Combine(lab.Tree.Root, "git.trace");
        using var process = new HarnessProcess(lab.Tree);
        var result = await process.RunAsync(["plan", lab.Tasks, "--repo", repo.Root], ct,
            lab.Environment.ToDictionary(x => x.Key, x => (string?)x.Value));
        result.RequireSuccess("unborn HEAD path depth plan");
        await Assert.That(Lines(result).Where(line => line.Contains(": worktree path depth not measured ")))
            .IsEquivalentTo(new[] {
                $"WARN z-inspect: worktree path depth not measured ({reason})",
                $"WARN a: worktree path depth not measured ({reason})" }, CollectionOrdering.Matching);
        await Assert.That(File.ReadLines(lab.Environment["GIT_TRACE"]).Count(line => line.Contains("built-in: git ") && line.EndsWith(" read-tree HEAD")))
            .IsEqualTo(1);
        await Assert.That(Lines(result).Where(line => line.StartsWith("ERROR "))).IsEquivalentTo(Array.Empty<string>(), CollectionOrdering.Matching);
    }

    [Test]
    [Timeout(180_000)]
    public async Task Plan_measures_path_depth_once_but_reports_each_task_length(CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows()) Skip.Test("Worktree path depth is Windows-only.");
        using var lab = new CodexLab();
        var repo = await LabRepo.CreateAsync(lab.Tree, ct);
        await repo.GitAsync(["config", "core.longpaths", "true"], ct);
        var run = $"{DateTime.UtcNow:yyyy-MM-dd}-cached-depth";
        var folder = Path.Combine(lab.Tree.WorktreeRoot, run, "a");
        var file = new string('d', 248 - folder.Length - 10) + "/leaf.txt";
        Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(repo.Root, file))!);
        File.WriteAllText(Path.Combine(repo.Root, file), "tracked");
        await repo.GitAsync(["add", "--", file], ct);
        await repo.GitAsync(["commit", "-m", "Track cached depth fixture"], ct);
        File.WriteAllText(Path.Combine(lab.Environment["CODEX_HOME"], "auth.json"), "synthetic-auth");
        File.WriteAllText(lab.Tasks, """{"schemaVersion":2,"run":"cached-depth","tasks":[{"id":"z-inspect","kind":"research","base":"main","project":"off","prompt":"Inspect."},{"id":"a","kind":"research","base":"main","project":"off","prompt":"Inspect."}]}""");
        lab.Environment["GIT_TRACE"] = Path.Combine(lab.Tree.Root, "git.trace");
        using var process = new HarnessProcess(lab.Tree);
        var result = await process.RunAsync(["plan", lab.Tasks, "--repo", repo.Root], ct,
            lab.Environment.ToDictionary(x => x.Key, x => (string?)x.Value));
        result.RequireSuccess("cached path depth plan");
        await Assert.That(Lines(result).Where(line => line.Contains(": worktree paths reach "))).IsEquivalentTo(new[] { "z-inspect", "a" }.Select(id =>
            $"WARN {id}: worktree paths reach {Path.Combine(lab.Tree.WorktreeRoot, run, id).Length + 1 + file.Length} characters ({file.Replace('/', '\\')}); builds can fail past 260 on Windows. Set locations.worktreeRoot to a short folder (for example C:\\wt) in the user config"), CollectionOrdering.Matching);
        await Assert.That(File.ReadLines(lab.Environment["GIT_TRACE"]).Count(line => line.Contains("built-in: git ") && line.EndsWith(" read-tree HEAD")))
            .IsEqualTo(1);
        await Assert.That(File.ReadLines(lab.Environment["GIT_TRACE"]).Count(line => line.Contains("built-in: git ") && line.EndsWith(" ls-files -z")))
            .IsEqualTo(1);
    }

    static string[] Lines(CapturedResult result) => result.Stdout.Split('\n').Select(line => line.TrimEnd('\r')).ToArray();
}
