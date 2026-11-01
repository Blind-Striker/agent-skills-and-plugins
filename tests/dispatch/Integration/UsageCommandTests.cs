using System.Globalization;
using System.Text.Json.Nodes;
using Dispatch.Tests.Support;
using TUnit.Assertions.Enums;

namespace Dispatch.Tests.Integration;

[Category("Integration")]
[NotInParallel]
public sealed class UsageCommandTests
{
    [Test]
    public Task Default_grouping_and_total_preserve_unknown_measurements() => InLedger(async root =>
    {
        Mixed(root);
        var document = ReadUsage();
        await Assert.That(Keys(document, "kind", "route")).IsEquivalentTo(new[]
        {
            "code|a-route", "mechanical|unknown", "research|unknown", "research|z-route", "review|z-route", "structured|a-route",
        }, CollectionOrdering.Matching);
        await Figures(document["total"]!, [6, 1, 2, 1, 2, 2], [11, 300, 20, 47, 5, .125], [2, 2, 2, 3, 2, 1]);
        await Figures(document["rows"]![0]!, [1, 1, 0, 0, 0, 0], [4, 100, 20, 10, 5, null], [1, 1, 1, 1, 1, 0]);
        await Assert.That(document["skipped"]!.AsArray().Count).IsEqualTo(0);
        var text = InvokeUsage();
        await Assert.That(text).Contains("code\ta-route\t1\t1\t0\t0\t0\t0\t0\t4\t100\t20\t10\t5\tunknown");
        await Assert.That(text).Contains("total\t\t6\t1\t2\t1\t2\t2\t0\t11 (2/6)\t300 (2/6)\t20 (2/6)\t47 (3/6)\t5 (2/6)\t0.1250 (1/6)");
    });

    [Test]
    [Arguments("kind", "code,mechanical,research,review,structured", "1,1,2,1,1")]
    [Arguments("route", "a-route,unknown,z-route", "2,2,2")]
    [Arguments("backend", "codex,opencode,unknown", "2,2,2")]
    [Arguments("state", "cancelled,done,failed,queued,unknown", "1,1,2,1,1")]
    [Arguments("outcome", "none,succeeded,timeout", "3,2,1")]
    [Arguments("status", "blocked,done,none,stopped", "1,1,3,1")]
    [Arguments("run", "Alpha,zeta", "3,3")]
    [Arguments("day", "2026-10-31,2026-11-01", "3,3")]
    public Task Each_dimension_groups_and_sums(string dimension, string keys, string counts) => InLedger(async root =>
    {
        Mixed(root);
        var document = ReadUsage("--by", dimension);
        await Assert.That(Keys(document, dimension)).IsEquivalentTo(keys.Split(','), CollectionOrdering.Matching);
        await Assert.That(document["rows"]!.AsArray().Select(row => row!["tasks"]!.GetValue<int>()))
            .IsEquivalentTo(counts.Split(',').Select(value => int.Parse(value, CultureInfo.InvariantCulture)), CollectionOrdering.Matching);
        (int[] Counters, double?[] Sums, int[] Known) code = ([1, 1, 0, 0, 0, 0], [4, 100, 20, 10, 5, null], [1, 1, 1, 1, 1, 0]);
        (int[] Counters, double?[] Sums, int[] Known) cancelled = ([1, 0, 0, 1, 0, 0], [null, null, null, null, null, null], [0, 0, 0, 0, 0, 0]);
        (int[] Counters, double?[] Sums, int[] Known) unsettled = ([1, 0, 0, 0, 1, 0], [null, null, null, null, null, null], [0, 0, 0, 0, 0, 0]);
        (int[] Counters, double?[] Sums, int[] Known) research = ([2, 0, 0, 0, 2, 0], [null, null, null, null, null, null], [0, 0, 0, 0, 0, 0]);
        (int[] Counters, double?[] Sums, int[] Known) review = ([1, 0, 1, 0, 0, 1], [7, 200, 0, 30, 0, .125], [1, 1, 1, 1, 1, 1]);
        (int[] Counters, double?[] Sums, int[] Known) partial = ([1, 0, 1, 0, 0, 1], [null, null, null, 7, null, null], [0, 0, 0, 1, 0, 0]);
        (int[] Counters, double?[] Sums, int[] Known) open = ([2, 0, 2, 0, 0, 2], [7, 200, 0, 37, 0, .125], [1, 1, 1, 2, 1, 1]);
        (int[] Counters, double?[] Sums, int[] Known) codex = ([2, 1, 0, 0, 1, 0], [4, 100, 20, 10, 5, null], [1, 1, 1, 1, 1, 0]);
        (int[] Counters, double?[] Sums, int[] Known) unknown = ([2, 0, 0, 1, 1, 0], [null, null, null, null, null, null], [0, 0, 0, 0, 0, 0]);
        (int[] Counters, double?[] Sums, int[] Known) none = ([3, 0, 0, 1, 2, 0], [null, null, null, null, null, null], [0, 0, 0, 0, 0, 0]);
        (int[] Counters, double?[] Sums, int[] Known) succeeded = ([2, 1, 1, 0, 0, 1], [11, 300, 20, 40, 5, .125], [2, 2, 2, 2, 2, 1]);
        (int[] Counters, double?[] Sums, int[] Known) alpha = ([3, 0, 1, 1, 1, 1], [null, null, null, 7, null, null], [0, 0, 0, 1, 0, 0]);
        (int[] Counters, double?[] Sums, int[] Known) zeta = ([3, 1, 1, 0, 1, 1], [11, 300, 20, 40, 5, .125], [2, 2, 2, 2, 2, 1]);
        (int[] Counters, double?[] Sums, int[] Known) routeA = ([2, 1, 1, 0, 0, 1], [4, 100, 20, 17, 5, null], [1, 1, 1, 2, 1, 0]);
        (int[] Counters, double?[] Sums, int[] Known) routeZ = ([2, 0, 1, 0, 1, 1], [7, 200, 0, 30, 0, .125], [1, 1, 1, 1, 1, 1]);
        (int[] Counters, double?[] Sums, int[] Known)[] expected = dimension switch
        {
            "kind" => [code, cancelled, research, review, partial],
            "route" => [routeA, unknown, routeZ],
            "backend" => [codex, open, unknown],
            "state" => [cancelled, code, open, unsettled, unsettled],
            "outcome" => [none, succeeded, partial],
            "status" => [partial, code, none, review],
            "run" or "day" => [alpha, zeta],
            _ => throw new InvalidOperationException(dimension),
        };
        for (var i = 0; i < expected.Length; i++)
            await Figures(document["rows"]![i]!, expected[i].Counters, expected[i].Sums, expected[i].Known);
        // Every dimension partitions the same six tasks, including each nullable figure independently.
        foreach (var name in new[] { "tasks", "done", "failed", "cancelled", "unsettled", "stopped", "secondsKnown", "inputKnown", "cacheReadKnown", "outputKnown", "reasoningKnown", "costKnown" })
            await Assert.That(document["rows"]!.AsArray().Sum(row => row![name]!.GetValue<int>())).IsEqualTo(document["total"]![name]!.GetValue<int>());
        foreach (var (name, expectedSum) in new[] { ("seconds", 11d), ("input", 300d), ("cacheRead", 20d), ("output", 47d), ("reasoning", 5d), ("cost", .125) })
            await Assert.That(document["rows"]!.AsArray().Sum(row => row![name]?.GetValue<double>() ?? 0)).IsEqualTo(expectedSum);
        await Figures(document["total"]!, [6, 1, 2, 1, 2, 2], [11, 300, 20, 47, 5, .125], [2, 2, 2, 3, 2, 1]);
    });

    [Test]
    public Task Repo_dimension_sorts_ordinally() => InLedger(async root =>
    {
        Mixed(root);
        var document = ReadUsage("--by", "repo,run");
        await Assert.That(Keys(document, "repo", "run")).IsEquivalentTo(new[]
        {
            Path.Combine(root, "a-repo") + "|Alpha", Path.Combine(root, "z-repo") + "|zeta",
        }, CollectionOrdering.Matching);
        await Figures(document["rows"]![0]!, [3, 0, 1, 1, 1, 1], [null, null, null, 7, null, null], [0, 0, 0, 1, 0, 0]);
        await Figures(document["rows"]![1]!, [3, 1, 1, 0, 1, 1], [11, 300, 20, 40, 5, .125], [2, 2, 2, 2, 2, 1]);
    });

    [Test]
    public Task Rows_sort_by_requested_values_ordinally_not_culturally() => InLedger(async root =>
    {
        Run(root, "order", "2026-11-01T00:00:00Z", null);
        Record(root, "order", "first", "review", "opencode", "a-route");
        Record(root, "order", "second", "code", "codex", "Z-route");
        Record(root, "order", "third", "mechanical", "codex", "Z-route");
        var document = ReadUsage("--by", "route,kind");
        await Assert.That(Keys(document, "route", "kind")).IsEquivalentTo(new[]
            { "Z-route|code", "Z-route|mechanical", "a-route|review" }, CollectionOrdering.Matching);
        foreach (var row in document["rows"]!.AsArray())
            await Figures(row!, [1, 0, 0, 0, 1, 0], [null, null, null, null, null, null], [0, 0, 0, 0, 0, 0]);
    });

    [Test]
    public Task Tags_expand_rows_but_not_total() => InLedger(async root =>
    {
        Mixed(root);
        var document = ReadUsage("--by", "tag");
        await Assert.That(Keys(document, "tag")).IsEquivalentTo(new[] { "(none)", "scan-again", "scan-first", "work-item" }, CollectionOrdering.Matching);
        await Figures(document["rows"]![0]!, [4, 0, 1, 1, 2, 1], [null, null, null, 7, null, null], [0, 0, 0, 1, 0, 0]);
        await Figures(document["rows"]![1]!, [1, 0, 1, 0, 0, 1], [7, 200, 0, 30, 0, .125], [1, 1, 1, 1, 1, 1]);
        foreach (var row in document["rows"]!.AsArray().Skip(2))
            await Figures(row!, [1, 1, 0, 0, 0, 0], [4, 100, 20, 10, 5, null], [1, 1, 1, 1, 1, 0]);
        await Figures(document["total"]!, [6, 1, 2, 1, 2, 2], [11, 300, 20, 47, 5, .125], [2, 2, 2, 3, 2, 1]);
    });

    [Test]
    public Task Partial_tokens_and_event_state_are_independent_of_result_state() => InLedger(async root =>
    {
        Mixed(root);
        var document = ReadUsage("--by", "state,kind");
        var partial = document["rows"]!.AsArray().Single(row => row!["key"]!["kind"]!.GetValue<string>() == "structured")!;
        await Assert.That(partial["key"]!["state"]!.GetValue<string>()).IsEqualTo("failed");
        await Figures(partial, [1, 0, 1, 0, 0, 1], [null, null, null, 7, null, null], [0, 0, 0, 1, 0, 0]);
        var cancelled = document["rows"]!.AsArray().Single(row => row!["key"]!["state"]!.GetValue<string>() == "cancelled")!;
        await Figures(cancelled, [1, 0, 0, 1, 0, 0], [null, null, null, null, null, null], [0, 0, 0, 0, 0, 0]);
        var queued = document["rows"]!.AsArray().Single(row => row!["key"]!["state"]!.GetValue<string>() == "queued")!;
        await Figures(queued, [1, 0, 0, 0, 1, 0], [null, null, null, null, null, null], [0, 0, 0, 0, 0, 0]);
    });

    [Test]
    public Task Duration_uses_first_prompt_and_first_final_for_both_backends() => InLedger(async root =>
    {
        Mixed(root);
        var document = ReadUsage("--by", "backend");
        await Figures(document["rows"]![0]!, [2, 1, 0, 0, 1, 0], [4, 100, 20, 10, 5, null], [1, 1, 1, 1, 1, 0]);
        await Figures(document["rows"]![1]!, [2, 0, 2, 0, 0, 2], [7, 200, 0, 37, 0, .125], [1, 1, 1, 2, 1, 1]);
    });

    [Test]
    public Task Filters_are_inclusive_and_use_UTC_metadata() => InLedger(async root =>
    {
        Mixed(root);
        Run(root, "missing", "", null);
        Record(root, "missing", "inspect", "research");
        Run(root, "bad-date", "not-a-date", null);
        Record(root, "bad-date", "inspect", "research");
        var missingCreated = Path.Combine(root, "missing", "run.json");
        var info = JsonNode.Parse(File.ReadAllText(missingCreated))!;
        info.AsObject().Remove("created");
        File.WriteAllText(missingCreated, info.ToJsonString());
        foreach (var options in new[]
        {
            new[] { "--since", "2026-11-01", "--until", "2026-11-01" },
            new[] { "--since", "2026-11-01" },
            new[] { "--repo", OperatingSystem.IsWindows() ? Path.Combine(root, "z-repo").ToUpperInvariant() : Path.Combine(root, "z-repo") },
            new[] { "--run", OperatingSystem.IsWindows() ? "Z?T*" : "z?t*" },
        })
        {
            var document = ReadUsage(["--by", "run", .. options]);
            await Assert.That(Keys(document, "run")).IsEquivalentTo(new[] { "zeta" }, CollectionOrdering.Matching);
            await Figures(document["total"]!, [3, 1, 1, 0, 1, 1], [11, 300, 20, 40, 5, .125], [2, 2, 2, 2, 2, 1]);
        }
        var until = ReadUsage("--by", "run", "--until", "2026-10-31");
        await Assert.That(Keys(until, "run")).IsEquivalentTo(new[] { "Alpha" }, CollectionOrdering.Matching);
        await Figures(until["total"]!, [3, 0, 1, 1, 1, 1], [null, null, null, 7, null, null], [0, 0, 0, 1, 0, 0]);
        var unknownDay = ReadUsage("--by", "day", "--run", "bad-date");
        await Assert.That(Keys(unknownDay, "day")).IsEquivalentTo(new[] { "unknown" }, CollectionOrdering.Matching);
        await Figures(unknownDay["total"]!, [1, 0, 0, 0, 1, 0], [null, null, null, null, null, null], [0, 0, 0, 0, 0, 0]);
    });

    [Test]
    public Task Culture_is_invariant_when_invoked_in_process() => InLedger(async root =>
    {
        Run(root, "costs", "2026-11-01T00:00:00Z", null);
        foreach (var (id, cost) in new[] { ("z", .125), ("a", .25) })
        {
            Record(root, "costs", id, "code");
            Result(root, "costs", id, new TaskResult { Cost = cost });
        }
        var previous = CultureInfo.CurrentCulture;
        var previousUi = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("tr-TR");
            var json = InvokeUsage("--json");
            await Assert.That(json).Contains("\"cost\":0.375");
            var document = JsonNode.Parse(json)!;
            await Assert.That(document["total"]!["cost"]!.GetValue<double>()).IsEqualTo(.375);
            await Assert.That(document["total"]!["costKnown"]!.GetValue<int>()).IsEqualTo(2);
            await Assert.That(InvokeUsage()).Contains("0.3750");
            var dated = ReadUsage("--since", "2026-11-01", "--until", "2026-11-01");
            await Assert.That(dated["total"]!["tasks"]!.GetValue<int>()).IsEqualTo(2);
        }
        finally { CultureInfo.CurrentCulture = previous; CultureInfo.CurrentUICulture = previousUi; }
    });

    [Test]
    [Arguments("bad json")]
    [Arguments("null")]
    [Arguments("{\"schemaVersion\":2,\"spec\":null}")]
    [Timeout(180_000)]
    public Task Unreadable_entries_are_sorted_and_siblings_still_count(string taskRecord, CancellationToken ct) => InLedger(async root =>
    {
        Mixed(root);
        Run(root, "z-newer", "2026-11-01T00:00:00Z", null);
        File.WriteAllText(Path.Combine(root, "z-newer", "run.json"), "{\"schemaVersion\":999}");
        Run(root, "a-corrupt", "2026-11-01T00:00:00Z", null);
        File.WriteAllText(Path.Combine(root, "a-corrupt", "events.jsonl"), "bad json\n");
        Record(root, "Alpha", "z-broken", "code");
        File.WriteAllText(Path.Combine(root, "Alpha", "z-broken", "task.json"), taskRecord);
        Event(root, "Alpha", "a-missing", "queued", "2026-11-01T00:00:00Z");
        var document = ReadUsage();
        await Assert.That(document["skipped"]!.AsArray().Select(item => item!["run"]!.GetValue<string>() + "/" + item["task"]?.GetValue<string>()))
            .IsEquivalentTo(new[] { "Alpha/a-missing", "Alpha/z-broken", "a-corrupt/", "z-newer/" }, CollectionOrdering.Matching);
        await Assert.That(document["skipped"]![2]!["reason"]!.GetValue<string>()).Contains("corrupt ledger");
        await Assert.That(document["skipped"]![3]!["reason"]!.GetValue<string>()).IsEqualTo("written by a newer dispatch; update the plugin");
        await Figures(document["total"]!, [6, 1, 2, 1, 2, 2], [11, 300, 20, 47, 5, .125], [2, 2, 2, 3, 2, 1]);
        using var process = new CapturedProcess();
        var result = await process.RunAsync("dotnet", [Gates.HarnessDll, "usage", "--ledger", root], TestPaths.Harness, ChildEnvironment(root), ct);
        result.RequireSuccess("usage skips unreadable entries");
        var expected = "skipped 4 item(s): " + string.Join("; ", document["skipped"]!.AsArray().Select(item =>
            item!["run"]!.GetValue<string>() + (item["task"] is { } task ? "/" + task.GetValue<string>() : "") + " (" + item["reason"]!.GetValue<string>() + ")"));
        await Assert.That(result.Stderr.Trim()).IsEqualTo(expected);
        await Assert.That(result.Stdout).Contains("total\t\t6\t1\t2\t1\t2\t2");
    });

    [Test]
    [Timeout(180_000)]
    public Task Child_process_never_changes_ledger_or_creates_state(CancellationToken ct) => InLedger(async root =>
    {
        Mixed(root);
        var before = Snapshot(root);
        using var process = new CapturedProcess();
        var result = await process.RunAsync("dotnet", [Gates.HarnessDll, "usage", "--json", "--ledger", root], TestPaths.Harness, ChildEnvironment(root), ct);
        result.RequireSuccess("read-only usage");
        await Figures(JsonNode.Parse(result.Stdout)!["total"]!, [6, 1, 2, 1, 2, 2], [11, 300, 20, 47, 5, .125], [2, 2, 2, 3, 2, 1]);
        await Assert.That(Snapshot(root)).IsEquivalentTo(before, CollectionOrdering.Matching);
        await Assert.That(Directory.Exists(ChildEnvironment(root)["DISPATCH_HOME"]!)).IsFalse();
        await Assert.That(result.Stderr).IsEqualTo("");
    });

    [Test]
    [Arguments("--since", "2026-02-30", "--since date 2026-02-30 must be yyyy-MM-dd")]
    [Arguments("--until", "01/11/2026", "--until date 01/11/2026 must be yyyy-MM-dd")]
    [Arguments("--by", "kind,bogus", "unknown usage dimension bogus")]
    [Timeout(180_000)]
    public Task Invalid_arguments_name_value_and_exit_two(string flag, string value, string message, CancellationToken ct) => InLedger(async root =>
    {
        using var process = new CapturedProcess();
        var result = await process.RunAsync("dotnet", [Gates.HarnessDll, "usage", flag, value, "--ledger", root], TestPaths.Harness, ChildEnvironment(root), ct);
        await Assert.That(result.ExitCode).IsEqualTo(2);
        await Assert.That(result.Stderr.Trim()).IsEqualTo("dispatch: " + message);
        await Assert.That(result.Stdout).IsEqualTo("");
    });

    [Test]
    public Task Empty_selection_has_zero_counters_and_null_sums() => InLedger(async root =>
    {
        Mixed(root);
        var document = ReadUsage("--run", "nothing*");
        await Assert.That(document["rows"]!.AsArray().Count).IsEqualTo(0);
        await Figures(document["total"]!, [0, 0, 0, 0, 0, 0], [null, null, null, null, null, null], [0, 0, 0, 0, 0, 0]);
        await Assert.That(document["skipped"]!.AsArray().Count).IsEqualTo(0);
        await Assert.That(InvokeUsage("--run", "nothing*").Trim()).IsEqualTo("no runs");
        Locations.Configure(null, Path.Combine(root, "absent"));
        var absent = ReadUsage();
        await Assert.That(absent["rows"]!.AsArray().Count).IsEqualTo(0);
        await Figures(absent["total"]!, [0, 0, 0, 0, 0, 0], [null, null, null, null, null, null], [0, 0, 0, 0, 0, 0]);
        await Assert.That(Directory.Exists(Path.Combine(root, "absent"))).IsFalse();
    });

    [Test]
    public Task Planner_validates_tags_and_records_them_unchanged(CancellationToken ct) => InLedger(async root =>
    {
        var path = Path.Combine(root, "tasks.jsonc");
        File.WriteAllText(path, """{"schemaVersion":2,"tasks":[{"id":"inspect","kind":"structured","backend":"codex","route":"model","prompt":"Transform.","input":{"items":[{"id":"a"}]},"result":{"schema":"schema.json","itemsAt":""},"tags":["Bad_tag","-bad","12345678901234567890123456789012345678901",""]}]}""");
        File.WriteAllText(Path.Combine(root, "schema.json"), """{"type":"array"}""");
        var git = new Git(Executables.Resolve("git"), root, new Dictionary<string, string>());
        var refused = await Planner.LoadAsync(path, null, AllowList.From(null), ct, git);
        await Assert.That(refused.Errors).IsEquivalentTo(new[]
        {
            "inspect: tag Bad_tag must match ^[a-z0-9][a-z0-9-]{0,39}$",
            "inspect: tag -bad must match ^[a-z0-9][a-z0-9-]{0,39}$",
            "inspect: tag 12345678901234567890123456789012345678901 must match ^[a-z0-9][a-z0-9-]{0,39}$",
            "inspect: tag  must match ^[a-z0-9][a-z0-9-]{0,39}$",
        }, CollectionOrdering.Matching);
        var file = JsonNode.Parse(File.ReadAllText(path))!;
        file["tasks"]![0]!["tags"] = new JsonArray("work-item", "scan-first", "1234567890123456789012345678901234567890");
        File.WriteAllText(path, file.ToJsonString());
        var accepted = await Planner.LoadAsync(path, null, AllowList.From(null), ct, git);
        await Assert.That(accepted.Errors).IsEmpty();
        await Assert.That(accepted.Tasks.Select(task => task.Spec.Id!)).IsEquivalentTo(new[] { "inspect" }, CollectionOrdering.Matching);
        var ledger = new Ledger("tagged");
        ledger.SaveRecord(new TaskRecord { Spec = accepted.Tasks.Single().Spec });
        await Assert.That(ledger.LoadRecord("inspect").Spec.Tags!).IsEquivalentTo(new[]
            { "work-item", "scan-first", "1234567890123456789012345678901234567890" }, CollectionOrdering.Matching);
    });

    [Test]
    [Arguments("backend,route,kind")]
    public Task Total_figures_align_with_the_dimension_columns(string dimensions) => InLedger(async root =>
    {
        Mixed(root);
        var lines = InvokeUsage("--by", dimensions).ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var header = lines[0].Split('\t');
        var total = lines.Single(line => line.StartsWith("total\t", StringComparison.Ordinal)).Split('\t');
        await Assert.That(total.Length).IsEqualTo(header.Length);
        await Assert.That(total.Take(dimensions.Split(',').Length)).IsEquivalentTo(new[] { "total", "", "" }, CollectionOrdering.Matching);
        await Assert.That(total[Array.IndexOf(header, "tasks")]).IsEqualTo("6");
        await Assert.That(total[Array.IndexOf(header, "input")]).IsEqualTo("300 (2/6)");
    });

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public Task Repository_filter_ignores_trailing_separator_on_either_value(bool recordedSeparator) => InLedger(async root =>
    {
        var repo = Path.Combine(root, "repo");
        Run(root, "match", "2026-11-01T00:00:00Z", repo + (recordedSeparator ? Path.DirectorySeparatorChar : ""));
        Record(root, "match", "inspect", "research");
        Run(root, "other", "2026-11-01T00:00:00Z", repo + "-other");
        Record(root, "other", "inspect", "code");
        var document = ReadUsage("--by", "run", "--repo", repo + (recordedSeparator ? "" : Path.DirectorySeparatorChar));
        await Assert.That(Keys(document, "run")).IsEquivalentTo(new[] { "match" }, CollectionOrdering.Matching);
        await Figures(document["total"]!, [1, 0, 0, 0, 1, 0], [null, null, null, null, null, null], [0, 0, 0, 0, 0, 0]);
    });

    [Test]
    public Task Incomplete_token_results_count_and_still_contribute_all_known_figures() => InLedger(async root =>
    {
        Run(root, "partial", "2026-11-01T00:00:00Z", null);
        Record(root, "partial", "a", "code");
        Result(root, "partial", "a", new TaskResult { TokensComplete = false,
            Tokens = new() { Input = 120, CacheRead = 30, Output = 14, Reasoning = 4 } });
        Record(root, "partial", "b", "code");
        Result(root, "partial", "b", new TaskResult { TokensComplete = true,
            Tokens = new() { Input = 80, CacheRead = 10, Output = 6, Reasoning = 2 } });
        var document = ReadUsage("--by", "kind");
        await Assert.That(Keys(document, "kind")).IsEquivalentTo(new[] { "code" }, CollectionOrdering.Matching);
        foreach (var row in new[] { document["rows"]![0]!, document["total"]! })
        {
            await Assert.That(row["tokensIncomplete"]?.GetValue<int>()).IsEqualTo(1);
            await Figures(row, [2, 0, 0, 0, 2, 0], [null, 200, 40, 20, 6, null], [0, 2, 2, 2, 2, 0]);
        }
        await Assert.That(InvokeUsage("--by", "kind").ReplaceLineEndings("\n")).IsEqualTo(
            "kind\ttasks\tdone\tfailed\tcancelled\tunsettled\tstopped\ttokensIncomplete\tseconds\tinput\tcacheRead\toutput\treasoning\tcost\n"
            + "code\t2\t0\t0\t0\t2\t0\t1\tunknown\t200\t40\t20\t6\tunknown\n"
            + "total\t2\t0\t0\t0\t2\t0\t1\tunknown\t200\t40\t20\t6\tunknown\n");
    });

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public Task Final_before_prompt_is_unknown_and_text_seconds_are_invariant_whole_numbers(bool earlyFinal) => InLedger(async root =>
    {
        Run(root, "duration", "2026-11-01T00:00:00Z", null);
        Record(root, "duration", "early", "review");
        if (earlyFinal)
        {
            Event(root, "duration", "early", "final", "2026-11-01T00:00:01Z", "done");
            Event(root, "duration", "early", "prompt", "2026-11-01T00:00:02Z");
        }
        Record(root, "duration", "normal", "code");
        Event(root, "duration", "normal", "prompt", "2026-11-01T00:00:10Z");
        Event(root, "duration", "normal", "final", "2026-11-01T00:00:12.750Z", "done");
        var document = ReadUsage("--by", "kind");
        await Assert.That(Keys(document, "kind")).IsEquivalentTo(new[] { "code", "review" }, CollectionOrdering.Matching);
        await Figures(document["rows"]![1]!, [1, 0, 0, 0, 1, 0], [null, null, null, null, null, null], [0, 0, 0, 0, 0, 0]);
        await Figures(document["total"]!, [2, 1, 0, 0, 1, 0], [2.75, null, null, null, null, null], [1, 0, 0, 0, 0, 0]);
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            var lines = InvokeUsage("--by", "kind").ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var column = Array.IndexOf(lines[0].Split('\t'), "seconds");
            await Assert.That(lines.Single(line => line.StartsWith("code\t", StringComparison.Ordinal)).Split('\t')[column]).IsEqualTo("3");
            await Assert.That(lines.Single(line => line.StartsWith("total\t", StringComparison.Ordinal)).Split('\t')[column]).IsEqualTo("3 (1/2)");
        }
        finally { CultureInfo.CurrentCulture = previous; }
    });

    [Test]
    [Arguments(null)]
    [Arguments("")]
    public Task Null_and_empty_tags_are_absent(string? tag) => InLedger(async root =>
    {
        Run(root, "tags", "2026-11-01T00:00:00Z", null);
        Record(root, "tags", "untagged", "research", tags: [tag!]);
        var document = ReadUsage("--by", "tag");
        await Assert.That(document["rows"]!.AsArray().Select(row => row!["key"]!["tag"]?.GetValue<string>()))
            .IsEquivalentTo(new string?[] { "(none)" }, CollectionOrdering.Matching);
        foreach (var row in document["rows"]!.AsArray())
            await Figures(row!, [1, 0, 0, 0, 1, 0], [null, null, null, null, null, null], [0, 0, 0, 0, 0, 0]);
        await Figures(document["total"]!, [1, 0, 0, 0, 1, 0], [null, null, null, null, null, null], [0, 0, 0, 0, 0, 0]);
    });

    static async Task InLedger(Func<string, Task> test)
    {
        var temp = Path.Combine(Path.GetTempPath(), "dispatch-usage-" + Guid.NewGuid().ToString("N"));
        var root = Path.Combine(temp, "ledgers");
        Directory.CreateDirectory(root);
        var ledger = Locations.LedgerRoot;
        var worktree = Locations.WorktreeRoot;
        Locations.Configure(null, root);
        try { await test(root); }
        finally
        {
            Locations.Configure(new ConfigLocations { WorktreeRoot = Path.IsPathFullyQualified(worktree) ? worktree : null }, Path.IsPathFullyQualified(ledger) ? ledger : null);
            Directory.Delete(temp, recursive: true);
        }
    }

    static string InvokeUsage(params string[] options)
    {
        var output = TestContext.Current!.Output;
        var start = output.GetStandardOutput().Length;
        var exit = Commands.Usage(Args.Parse(["usage", .. options]));
        if (exit != 0) throw new InvalidOperationException($"usage exited {exit}");
        return output.GetStandardOutput()[start..];
    }

    static JsonNode ReadUsage(params string[] options) => JsonNode.Parse(InvokeUsage(["--json", .. options]))!;

    static IEnumerable<string> Keys(JsonNode document, params string[] dimensions) => document["rows"]!.AsArray()
        .Select(row => string.Join('|', dimensions.Select(dimension => row!["key"]![dimension]!.GetValue<string>())));

    static async Task Figures(JsonNode row, int[] counters, double?[] sums, int[] known)
    {
        var counterNames = new[] { "tasks", "done", "failed", "cancelled", "unsettled", "stopped" };
        var sumNames = new[] { "seconds", "input", "cacheRead", "output", "reasoning", "cost" };
        for (var i = 0; i < counterNames.Length; i++)
            await Assert.That(row[counterNames[i]]!.GetValue<int>()).IsEqualTo(counters[i]);
        for (var i = 0; i < sumNames.Length; i++)
        {
            await Assert.That(row.AsObject().ContainsKey(sumNames[i])).IsTrue();
            await Assert.That(row[sumNames[i]]?.GetValue<double>()).IsEqualTo(sums[i]);
            await Assert.That(row[sumNames[i] + "Known"]!.GetValue<int>()).IsEqualTo(known[i]);
        }
    }

    static Dictionary<string, string?> ChildEnvironment(string root)
    {
        var environment = Environment.GetEnvironmentVariables().Cast<System.Collections.DictionaryEntry>()
            .ToDictionary(entry => (string)entry.Key, entry => (string?)entry.Value, OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        environment["DISPATCH_HOME"] = Path.Combine(Path.GetDirectoryName(root)!, "isolated-home");
        return environment;
    }

    static string[] Snapshot(string root) => Directory.GetFileSystemEntries(root, "*", SearchOption.AllDirectories)
        .Select(path => Path.GetRelativePath(root, path) + (File.Exists(path) ? ":" + Convert.ToHexString(File.ReadAllBytes(path)) : "/"))
        .Order(StringComparer.Ordinal).ToArray();

    static void Run(string root, string id, string created, string? repo) => Json.WriteFile(Path.Combine(root, id, "run.json"),
        new RunInfo { Run = id, Created = created, Repo = repo }, LedgerJson.Files.RunInfo);

    static void Record(string root, string run, string id, string kind, string? backend = null, string? route = null, List<string>? tags = null) =>
        Json.WriteFile(Path.Combine(root, run, id, "task.json"), new TaskRecord
        {
            Spec = new TaskSpec { Id = id, Kind = kind, Tags = tags },
            Settings = new EffectiveSettings
            {
                Backend = backend is null ? null : new() { Value = backend },
                Route = route is null ? null : new() { Value = route },
            },
        }, LedgerJson.Files.TaskRecord);

    static void Result(string root, string run, string id, TaskResult result) =>
        Json.WriteFile(Path.Combine(root, run, id, "result.json"), result, LedgerJson.Files.TaskResult);

    static void Event(string root, string run, string id, string type, string timestamp, string? state = null) =>
        File.AppendAllText(Path.Combine(root, run, "events.jsonl"), new JsonObject
        {
            ["run"] = run, ["task"] = id, ["type"] = type, ["ts"] = timestamp,
            ["data"] = state is null ? null : new JsonObject { ["state"] = state },
        }.ToJsonString() + "\n");

    static void Mixed(string root)
    {
        Run(root, "zeta", "2026-10-31T23:30:00-02:00", Path.Combine(root, "z-repo"));
        // Insert no dimension's ordinal winner first, including tag's untagged bucket.
        Record(root, "zeta", "a-review", "review", "opencode", "z-route", ["scan-again"]);
        Result(root, "zeta", "a-review", new TaskResult { State = "done", Outcome = "succeeded", WorkerStatus = "stopped", Cost = .125,
            Tokens = new TokenTotals { Input = 200, CacheRead = 0, Output = 30, Reasoning = 0 } });
        Event(root, "zeta", "a-review", "prompt", "2026-11-01T00:00:20Z");
        Event(root, "zeta", "a-review", "final", "2026-11-01T00:00:27Z", "failed");
        Record(root, "zeta", "z-code", "code", "wrong-backend", "wrong-route", ["work-item", "scan-first"]);
        Result(root, "zeta", "z-code", new TaskResult { Kind = "wrong-kind", Backend = "codex", RouteRequested = "a-route", State = "failed", Outcome = "succeeded", WorkerStatus = "done",
            Tokens = new TokenTotals { Input = 100, CacheRead = 20, Output = 10, Reasoning = 5 } });
        Event(root, "zeta", "z-code", "prompt", "2026-11-01T00:00:10Z");
        Event(root, "zeta", "z-code", "prompt", "2026-11-01T00:00:12Z");
        Event(root, "zeta", "z-code", "final", "2026-11-01T00:00:14Z", "done");
        Event(root, "zeta", "z-code", "collected", "2026-11-01T00:01:00Z");
        Event(root, "zeta", "z-code", "final", "2026-11-01T00:01:40Z", "done");
        Record(root, "zeta", "m-queued", "research", "codex", "z-route");
        Event(root, "zeta", "m-queued", "prompt", "2026-11-01T00:00:00Z");
        Event(root, "zeta", "m-queued", "queued", "2026-11-01T00:00:00Z");
        Run(root, "Alpha", "2026-10-31T23:59:59Z", Path.Combine(root, "a-repo"));
        Record(root, "Alpha", "c-cancel", "mechanical");
        Event(root, "Alpha", "c-cancel", "final", "2026-11-01T00:00:30Z", "cancelled");
        Record(root, "Alpha", "b-partial", "structured", "opencode", "a-route");
        Result(root, "Alpha", "b-partial", new TaskResult { State = "done", Outcome = "timeout", WorkerStatus = "blocked", Tokens = new TokenTotals { Output = 7 } });
        Event(root, "Alpha", "b-partial", "final", "2026-11-01T00:00:40Z", "failed");
        Record(root, "Alpha", "f-folder", "research");
        Record(root, "Alpha", ".hidden", "code");
    }
}
