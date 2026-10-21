using Dispatch.Tests.Support;
using System.Text.Json;

namespace Dispatch.Tests.Unit;

[Category("Unit")]
public sealed class PlannerScopeTests
{
    [Test]
    [Arguments("src/*/one.cs", "src/two/*.cs", true)]
    [Arguments("src/*.cs", "src/*.md", false)]
    [Arguments("src/{one,two}/**", "src/other/**", false)]
    [Timeout(60_000)]
    public async Task Plan_OverlappingPositiveGlobs_Refused(string a, string b, bool overlaps, CancellationToken cancellationToken)
    {
        using var tree = new TempTree();
        var plan = await LoadAsync(tree, [Spec("first", "code", [a]), Spec("second", "mechanical", [b])], cancellationToken);

        await Assert.That(plan.Errors.Count).IsEqualTo(overlaps ? 1 : 0);
        if (overlaps)
            await Assert.That(plan.Errors[0]).Contains("first and second share scope");
    }

    [Test]
    [Timeout(60_000)]
    public async Task Plan_ExclusionsDoNotProveOverlap(CancellationToken cancellationToken)
    {
        using var tree = new TempTree();
        var plan = await LoadAsync(tree,
            [Spec("first", "code", ["src/**", "!shared/**"]), Spec("second", "mechanical", ["docs/**", "!shared/**"])], cancellationToken);

        await Assert.That(plan.Errors).IsEmpty();
    }

    [Test]
    [Arguments("review")]
    [Arguments("research")]
    [Timeout(60_000)]
    public async Task Plan_ReadingKindsDoNotConflictWithWriters(string kind, CancellationToken cancellationToken)
    {
        using var tree = new TempTree();
        var plan = await LoadAsync(tree, [Spec("first", "code", ["src/**"]), Spec("second", kind, ["src/**"])], cancellationToken);

        await Assert.That(plan.Errors).IsEmpty();
    }

    static TaskSpec Spec(string id, string kind, List<string> scope) => new()
    {
        Id = id, Kind = kind, Base = "main", Agent = "worker", Prompt = "Inspect the declared scope.", Scope = scope,
    };

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    [Timeout(60_000)]
    public async Task Shared_paths_exempt_only_the_named_overlap(bool otherOverlap, CancellationToken cancellationToken)
    {
        using var tree = new TempTree();
        var plan = await LoadAsync(tree,
            [Spec("first", "code", ["shared.txt", "src/one/**"]),
             Spec("second", "mechanical", ["shared.txt", otherOverlap ? "src/one/**" : "src/two/**"])],
            cancellationToken, ["shared.txt"]);
        await Assert.That(plan.Errors.Count).IsEqualTo(otherOverlap ? 1 : 0);
        if (otherOverlap) await Assert.That(plan.Errors[0]).Contains("src/one/**");
    }

    [Test]
    [Timeout(60_000)]
    public async Task A_shared_file_does_not_exempt_the_rest_of_a_broad_scope(CancellationToken cancellationToken)
    {
        using var tree = new TempTree();
        var plan = await LoadAsync(tree,
            [Spec("first", "code", ["src/**"]), Spec("second", "mechanical", ["src/**"])],
            cancellationToken, ["src/shared.txt"]);
        await Assert.That(plan.Errors.Count).IsEqualTo(1);
        await Assert.That(plan.Errors[0]).Contains("share scope");
    }

    static async Task<Plan> LoadAsync(TempTree tree, List<TaskSpec> tasks, CancellationToken cancellationToken, string[]? shared = null)
    {
        var repo = await LabRepo.CreateAsync(tree, cancellationToken);
        var path = Path.Combine(tree.Root, "tasks.json");
        var document = JsonSerializer.SerializeToNode(new TaskFile { Repo = repo.Root, Tasks = tasks }, InputJson.Default.TaskFile)!;
        if (shared is not null) document["sharedPaths"] = Json.Array(shared);
        await File.WriteAllTextAsync(path, document.ToJsonString(), cancellationToken);
        var allow = new AllowList();
        allow.Agents.Add("worker");
        var git = new Git(Executables.Resolve("git"), Path.Combine(tree.Root, "control"),
            tree.ChildEnvironment().ToDictionary(x => x.Key, x => x.Value!));
        return await Planner.LoadAsync(path, null, allow, cancellationToken, git);
    }
}
