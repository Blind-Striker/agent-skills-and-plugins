using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Dispatch.Tests;

/// <summary>
/// The shape ADR-0009 gives the harness: an entry file that holds every directive, a src/ tree that
/// holds none, every src/ file included by the entry, and one set of package pins.
/// </summary>
[Category("Unit")]
public sealed partial class LayoutTests
{
    static readonly string Root = RepositoryRoot();
    static readonly string Harness = Path.Combine(Root, "skills", "deniz-coordination", "harness");
    static readonly string Entry = Path.Combine(Harness, "dispatch.cs");
    static readonly string Source = Path.Combine(Harness, "src");

    [GeneratedRegex(@"^#:package\s+(?<id>[^@\s]+)@(?<version>\S+)\s*$", RegexOptions.Multiline)]
    private static partial Regex PackageDirective();

    [GeneratedRegex(@"^#:include\s+(?<path>\S+)\s*$", RegexOptions.Multiline)]
    private static partial Regex IncludeDirective();

    [Test]
    public async Task The_harness_pins_the_include_floor_on_stable_dotnet_10()
    {
        // #:include needs SDK 10.0.300; latestFeature keeps the harness on 10, never on 11.
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(Harness, "global.json")));
        var sdk = json.RootElement.GetProperty("sdk");
        await Assert.That(sdk.GetProperty("version").GetString()).IsEqualTo("10.0.300");
        await Assert.That(sdk.GetProperty("rollForward").GetString()).IsEqualTo("latestFeature");
        await Assert.That(sdk.GetProperty("allowPrerelease").GetBoolean()).IsFalse();
    }

    [Test]
    public async Task The_tests_build_on_the_sdk_the_harness_pins()
    {
        var harness = await File.ReadAllTextAsync(Path.Combine(Harness, "global.json"));
        var tests = await File.ReadAllTextAsync(Path.Combine(Root, "tests", "dispatch", "global.json"));
        await Assert.That(tests.ReplaceLineEndings()).IsEqualTo(harness.ReplaceLineEndings());
    }

    [Test]
    public async Task No_source_file_carries_a_file_based_directive()
    {
        var offenders = SourceFiles()
            .Where(file => File.ReadLines(file).Any(line => line.StartsWith("#:") || line.StartsWith("#!")))
            .Select(file => Path.GetRelativePath(Harness, file))
            .ToList();
        await Assert.That(offenders).IsEmpty();
    }

    [Test]
    public async Task The_entry_includes_every_source_file_and_nothing_else()
    {
        if (!File.Exists(Entry))
        {
            Skip.Test("the harness entry file has not been ported yet");
        }
        var included = IncludeDirective()
            .Matches(await File.ReadAllTextAsync(Entry))
            .Select(match => Normalize(match.Groups["path"].Value))
            .Order()
            .ToList();
        var present = SourceFiles().Select(file => Normalize(Path.GetRelativePath(Harness, file))).Order().ToList();
        await Assert.That(included).IsEquivalentTo(present);
    }

    [Test]
    public async Task The_test_project_repeats_the_entry_package_pins()
    {
        if (!File.Exists(Entry))
        {
            Skip.Test("the harness entry file has not been ported yet");
        }
        var pinned = PackageDirective()
            .Matches(await File.ReadAllTextAsync(Entry))
            .Select(match => $"{match.Groups["id"].Value}@{match.Groups["version"].Value}")
            .Order()
            .ToList();
        var project = XDocument.Load(Path.Combine(Root, "tests", "dispatch", "Dispatch.Tests.csproj"));
        var repeated = project
            .Descendants("ItemGroup")
            .Where(group => (string?)group.Attribute("Label") == "HarnessPackages")
            .Descendants("PackageReference")
            .Select(reference => $"{(string?)reference.Attribute("Include")}@{(string?)reference.Attribute("Version")}")
            .Order()
            .ToList();
        await Assert.That(repeated).IsEquivalentTo(pinned);
    }

    static IEnumerable<string> SourceFiles() =>
        Directory.Exists(Source)
            ? Directory.EnumerateFiles(Source, "*.cs", SearchOption.AllDirectories)
            : [];

    static string Normalize(string path) => path.Replace('\\', '/');

    static string RepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "curation")) && File.Exists(Path.Combine(dir.FullName, "AGENTS.md")))
            {
                return dir.FullName;
            }
        }
        throw new InvalidOperationException("the repository root holding curation/ and AGENTS.md was not found");
    }
}
