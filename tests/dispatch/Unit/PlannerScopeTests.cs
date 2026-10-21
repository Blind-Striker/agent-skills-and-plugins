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

    static async Task<Plan> LoadAsync(TempTree tree, List<TaskSpec> tasks, CancellationToken cancellationToken)
    {
        var repo = await LabRepo.CreateAsync(tree, cancellationToken);
        var path = Path.Combine(tree.Root, "tasks.json");
        await File.WriteAllTextAsync(path,
            JsonSerializer.Serialize(new TaskFile { Repo = repo.Root, Tasks = tasks }, InputJson.Default.TaskFile), cancellationToken);
        var allow = new AllowList();
        allow.Agents.Add("worker");
        return await Planner.LoadAsync(path, null, allow, cancellationToken);
    }
}
