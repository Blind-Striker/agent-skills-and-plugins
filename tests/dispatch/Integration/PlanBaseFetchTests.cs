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

    [Test]
    [Arguments("~1", true)]
    [Arguments("^", true)]
    [Arguments("^1", true)]
    [Arguments("^{commit}", false)]
    [Arguments("~1^{commit}", true)]
    [Timeout(180_000)]
    public async Task Plan_fetches_remote_base_before_resolving_revision_suffix(string suffix, bool parent, CancellationToken ct)
    {
        using var lab = new CodexLab();
        var repo = await LabRepo.CreateAsync(lab.Tree, ct);
        var remote = await RemoteAsync(lab, repo, ct);
        var commits = await AdvanceRemoteAsync(lab, repo, remote, "main", ct);
        lab.Environment["GIT_TRACE"] = Path.Combine(lab.Tree.Root, "git.trace");
        var result = await PlanAsync(lab, repo, "origin/main" + suffix, ct, "inspect");
        result.RequireSuccess("suffixed remote base plan");
        var sha = parent ? commits.Parent : commits.Tip;
        await Assert.That(BaseInfo(result)).IsEquivalentTo(new[] {
            $"INFO inspect: base origin/main{suffix} resolves to {sha[..12]} (fetched from origin)" }, CollectionOrdering.Matching);
        await Assert.That(Fetches(lab)).IsEquivalentTo(new[] { "fetch --quiet origin main" }, CollectionOrdering.Matching);
    }

    [Test]
    [Timeout(180_000)]
    public async Task Plan_fetches_configured_remote_branch_without_a_tracking_ref(CancellationToken ct)
    {
        using var lab = new CodexLab();
        var repo = await LabRepo.CreateAsync(lab.Tree, ct);
        var remote = await RemoteAsync(lab, repo, ct);
        await repo.GitAsync(["remote", "rename", "origin", "upstream"], ct);
        var commits = await AdvanceRemoteAsync(lab, repo, remote, "feature/topic", ct);
        await Assert.That(await repo.GitAsync(["for-each-ref", "refs/remotes/upstream/feature/topic"], ct)).IsEqualTo("");
        lab.Environment["GIT_TRACE"] = Path.Combine(lab.Tree.Root, "git.trace");
        var result = await PlanAsync(lab, repo, "upstream/feature/topic", ct, "inspect");
        result.RequireSuccess("new remote branch plan");
        await Assert.That(BaseInfo(result)).IsEquivalentTo(new[] {
            $"INFO inspect: base upstream/feature/topic resolves to {commits.Tip[..12]} (fetched from upstream)" }, CollectionOrdering.Matching);
        await Assert.That(Fetches(lab)).IsEquivalentTo(new[] { "fetch --quiet upstream feature/topic" }, CollectionOrdering.Matching);
        await Assert.That(await repo.GitAsync(["rev-parse", "refs/remotes/upstream/feature/topic"], ct)).IsEqualTo(commits.Tip);
    }

    [Test]
    [Timeout(180_000)]
    public async Task Plan_fetch_uses_operator_credentials_without_prompts_and_keeps_git_overrides(CancellationToken ct)
    {
        using var lab = new CodexLab();
        var repo = await LabRepo.CreateAsync(lab.Tree, ct);
        var remote = await RemoteAsync(lab, repo, ct);
        var sha = await repo.GitAsync(["rev-parse", "main"], ct);
        var script = Path.Combine(lab.Tree.Root, "ssh.sh");
        var observed = Path.Combine(lab.Tree.Root, "ssh-environment.txt");
        File.WriteAllText(script, $"#!/bin/sh\nprintf '%s\\n' \"$SSH_AUTH_SOCK\" \"$GH_TEST\" \"$GITHUB_TEST\" \"$GIT_TERMINAL_PROMPT\" > '{observed.Replace('\\', '/')}'\nexec git-upload-pack '{remote.Replace('\\', '/')}'\n");
        await repo.GitAsync(["config", "core.sshCommand", $"sh '{script.Replace('\\', '/')}'"], ct);
        await repo.GitAsync(["config", "ssh.variant", "simple"], ct);
        await repo.GitAsync(["remote", "set-url", "origin", "ssh://fixture/remote.git"], ct);
        lab.Environment["SSH_AUTH_SOCK"] = "synthetic-agent-socket";
        lab.Environment["GH_TEST"] = "synthetic-gh-credential";
        lab.Environment["GITHUB_TEST"] = "synthetic-github-credential";
        lab.Environment["GIT_TERMINAL_PROMPT"] = "1";
        lab.Environment["GIT_TRACE2_EVENT"] = Path.Combine(lab.Tree.Root, "git-events.jsonl");
        var result = await PlanAsync(lab, repo, "origin/main", ct, "inspect");
        result.RequireSuccess("operator fetch environment plan");
        await Assert.That(File.ReadAllLines(observed)).IsEquivalentTo(new[] {
            "synthetic-agent-socket", "synthetic-gh-credential", "synthetic-github-credential", "0" }, CollectionOrdering.Matching);
        var fetch = File.ReadLines(lab.Environment["GIT_TRACE2_EVENT"]).Select(line => System.Text.Json.Nodes.JsonNode.Parse(line)!)
            .Where(node => node["event"]!.GetValue<string>() == "start").Select(node => node["argv"]!.AsArray().Select(arg => arg!.GetValue<string>()).ToArray())
            .Single(argv => argv.Contains("fetch"));
        foreach (var setting in new[] { "core.fsmonitor=false", "commit.gpgSign=false", "gc.auto=0", "maintenance.auto=false" })
            await Assert.That(fetch).Contains(setting);
        var hooks = fetch.Single(arg => arg.StartsWith("core.hooksPath=", StringComparison.Ordinal))["core.hooksPath=".Length..];
        // Windows has no /dev/null for git, so the harness points hooks at an empty control-folder directory there.
        if (OperatingSystem.IsWindows())
            await Assert.That(Directory.GetParent(Path.GetDirectoryName(hooks)!)!.FullName).IsEqualTo(Path.Combine(lab.Tree.DispatchHome, "state", "control"));
        else await Assert.That(hooks).IsEqualTo("/dev/null");
        await Assert.That(BaseInfo(result)).IsEquivalentTo(new[] {
            $"INFO inspect: base origin/main resolves to {sha[..12]} (fetched from origin)" }, CollectionOrdering.Matching);
    }

    static async Task<(string Parent, string Tip)> AdvanceRemoteAsync(CodexLab lab, LabRepo repo, string remote, string branch, CancellationToken ct)
    {
        var clone = Path.Combine(lab.Tree.Root, "second");
        await repo.GitAsync(["clone", remote, clone], ct);
        await repo.GitAsync(["-C", clone, "-c", "user.name=Test", "-c", "user.email=test@example.invalid",
            "commit", "--allow-empty", "-m", "Parent remote commit"], ct);
        var parent = await repo.GitAsync(["-C", clone, "rev-parse", "HEAD"], ct);
        await repo.GitAsync(["-C", clone, "-c", "user.name=Test", "-c", "user.email=test@example.invalid",
            "commit", "--allow-empty", "-m", "Tip remote commit"], ct);
        var tip = await repo.GitAsync(["-C", clone, "rev-parse", "HEAD"], ct);
        await repo.GitAsync(["-C", clone, "push", "origin", $"HEAD:refs/heads/{branch}"], ct);
        return (parent, tip);
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
