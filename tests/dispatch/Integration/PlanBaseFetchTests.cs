using Dispatch.Tests.Support;
using TUnit.Assertions.Enums;

namespace Dispatch.Tests.Integration;

[Category("Integration")]
[NotInParallel("processes")]
public sealed class PlanBaseFetchTests
{
    [Test]
    [Timeout(180_000)]
    public async Task Plan_fetches_new_remote_commit_once_and_reports_each_base(CancellationToken ct)
    {
        using var lab = new CodexLab();
        var repo = await LabRepo.CreateAsync(lab.Tree, ct);
        var remote = await RemoteAsync(lab, repo, ct);
        var clone = Path.Combine(lab.Tree.Root, "second");
        await repo.GitAsync(["clone", remote, clone], ct);
        await repo.GitAsync(["-C", clone, "-c", "user.name=Test", "-c", "user.email=test@example.invalid",
            "commit", "--allow-empty", "-m", "New remote commit"], ct);
        await repo.GitAsync(["-C", clone, "push", "origin", "main"], ct);
        var sha = await repo.GitAsync(["-C", clone, "rev-parse", "HEAD"], ct);
        lab.Environment["GIT_TRACE"] = Path.Combine(lab.Tree.Root, "git.trace");
        var result = await PlanAsync(lab, repo, "origin/main", ct, "z-inspect", "a-inspect");
        result.RequireSuccess("remote base plan");
        await Assert.That(BaseInfo(result)).IsEquivalentTo(new[] {
            $"INFO z-inspect: base origin/main resolves to {sha[..12]} (fetched from origin)",
            $"INFO a-inspect: base origin/main resolves to {sha[..12]} (fetched from origin)" }, CollectionOrdering.Matching);
        await Assert.That(Fetches(lab))
            .IsEquivalentTo(new[] { "fetch --quiet origin main" }, CollectionOrdering.Matching);
        await Assert.That(result.Stdout.IndexOf("INFO z-inspect: base", StringComparison.Ordinal))
            .IsLessThan(result.Stdout.IndexOf("WARN ", StringComparison.Ordinal));
    }

    [Test]
    [Timeout(180_000)]
    public async Task Plan_refuses_failed_fetch_with_first_stderr_line(CancellationToken ct)
    {
        using var lab = new CodexLab();
        var repo = await LabRepo.CreateAsync(lab.Tree, ct);
        await RemoteAsync(lab, repo, ct);
        await repo.GitAsync(["remote", "set-url", "origin", Path.Combine(lab.Tree.Root, "missing.git")], ct);
        using var git = new CapturedProcess();
        var failed = await git.RunAsync("git", ["fetch", "--quiet", "origin", "main"], repo.Root, lab.Tree.ChildEnvironment(), ct);
        await Assert.That(failed.ExitCode).IsNotEqualTo(0);
        lab.Environment["GIT_TRACE"] = Path.Combine(lab.Tree.Root, "git.trace");
        var result = await PlanAsync(lab, repo, "origin/main", ct, "z-inspect", "a-inspect");
        await Assert.That(result.ExitCode).IsEqualTo(2);
        var error = $"base origin/main: fetch from origin failed: {failed.Stderr.Split('\n')[0].TrimEnd('\r')}";
        await Assert.That(result.Stdout.Split('\n').Select(x => x.TrimEnd('\r')).Where(x => x.StartsWith("ERROR ")))
            .IsEquivalentTo(new[] { $"ERROR z-inspect: {error}", $"ERROR a-inspect: {error}" }, CollectionOrdering.Matching);
        await Assert.That(result.Stdout).Contains("plan refused: 2 error(s)");
        await Assert.That(Fetches(lab))
            .IsEquivalentTo(new[] { "fetch --quiet origin main" }, CollectionOrdering.Matching);
    }

    [Test]
    [Arguments("main")]
    [Arguments("tag")]
    [Arguments("sha")]
    [Timeout(180_000)]
    public async Task Plan_reports_non_remote_base_without_fetch(string reference, CancellationToken ct)
    {
        using var lab = new CodexLab();
        var repo = await LabRepo.CreateAsync(lab.Tree, ct);
        await repo.GitAsync(["remote", "add", "origin", Path.Combine(lab.Tree.Root, "missing.git")], ct);
        var sha = await repo.GitAsync(["rev-parse", "main"], ct);
        if (reference == "tag") await repo.GitAsync(["tag", "release"], ct);
        var basis = reference == "sha" ? sha : reference == "tag" ? "release" : "main";
        lab.Environment["GIT_TRACE"] = Path.Combine(lab.Tree.Root, "git.trace");
        var result = await PlanAsync(lab, repo, basis, ct, "inspect");
        result.RequireSuccess("non-remote base plan");
        await Assert.That(BaseInfo(result)).IsEquivalentTo(new[] { $"INFO inspect: base {basis} resolves to {sha[..12]}" }, CollectionOrdering.Matching);
        await Assert.That(string.Join('\n', Fetches(lab))).IsEqualTo("");
    }

    static async Task<string> RemoteAsync(CodexLab lab, LabRepo repo, CancellationToken ct)
    {
        var remote = Path.Combine(lab.Tree.Root, "remote.git");
        await repo.GitAsync(["init", "--bare", "--initial-branch=main", remote], ct);
        await repo.GitAsync(["remote", "add", "origin", remote], ct);
        await repo.GitAsync(["push", "origin", "main"], ct);
        await repo.GitAsync(["fetch", "origin"], ct);
        return remote;
    }

    static async Task<CapturedResult> PlanAsync(CodexLab lab, LabRepo repo, string basis, CancellationToken ct, params string[] ids)
    {
        var tasks = ids.Select(id => new { id, kind = "research", @base = basis, project = "off", prompt = "Inspect." });
        File.WriteAllText(lab.Tasks, System.Text.Json.JsonSerializer.Serialize(new { schemaVersion = 2, tasks }));
        File.WriteAllText(Path.Combine(lab.Environment["CODEX_HOME"], "auth.json"), "synthetic-auth");
        using var process = new HarnessProcess(lab.Tree);
        return await process.RunAsync(["plan", lab.Tasks, "--repo", repo.Root], ct,
            lab.Environment.ToDictionary(x => x.Key, x => (string?)x.Value));
    }

    static IEnumerable<string> BaseInfo(CapturedResult result) => result.Stdout.Split('\n')
        .Select(x => x.TrimEnd('\r')).Where(x => x.StartsWith("INFO ") && x.Contains(": base "));

    static IEnumerable<string> Fetches(CodexLab lab) => File.ReadLines(lab.Environment["GIT_TRACE"])
        .Where(x => x.Contains("built-in: git ") && x.Contains(" fetch --quiet "))
        .Select(x => x[x.IndexOf("fetch --quiet ", StringComparison.Ordinal)..].TrimEnd());
}
