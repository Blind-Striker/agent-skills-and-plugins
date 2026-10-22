namespace Dispatch.Tests.Unit;

[Category("Unit")]
public sealed class ScopeTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Scope_CaseFollowsIgnoreCase(bool ignoreCase)
    {
        await Assert.That(Scope.InScope("SRC/File.cs", ["src/file.cs"], ignoreCase)).IsEqualTo(ignoreCase);
        await Assert.That(Scope.Overlap(["SRC/File.cs"], ["src/file.cs"], ignoreCase: ignoreCase) is not null).IsEqualTo(ignoreCase);
        await Assert.That(Scope.InScope("src/file.cs", ["src/file.cs"])).IsTrue();
        await Assert.That(Scope.Overlap(["src/file.cs"], ["src/file.cs"])).IsNotNull();
    }

    [Test]
    [Arguments("src/k.cs", "src/\u212a.cs", true)]
    [Arguments("src/\u03c3.cs", "src/\u03c2.cs", false)]
    public async Task Overlap_CaseMatchesScope(string path, string glob, bool matchesIgnoringCase)
    {
        await Assert.That(Scope.InScope(path, [glob], ignoreCase: true)).IsEqualTo(matchesIgnoringCase);
        await Assert.That(Scope.Overlap([path], [glob], ignoreCase: true) is not null).IsEqualTo(matchesIgnoringCase);
    }

    [Test]
    [Arguments("src/{one,two}/file.cs", "src/{one,two}/**", true)]
    [Arguments("src/one/file.cs", "src/{one,two}/**", false)]
    [Arguments("src/[ab]/file.cs", "src/[ab]/**", true)]
    [Arguments("src/a/file.cs", "src/[ab]/**", false)]
    public async Task Scope_BracesLiteral(string path, string glob, bool expected)
    {
        await Assert.That(Scope.InScope(path, [glob])).IsEqualTo(expected);
    }

    [Test]
    [Arguments("src/{one,two}/**", "src/other/**", false)]
    [Arguments("src/[ab]/**", "src/other/**", false)]
    [Arguments("src/{one,two}/**", "src/{one,two}/file.cs", true)]
    [Arguments("src/[ab]/**", "src/[ab]/file.cs", true)]
    public async Task Overlap_BracesLiteral(string a, string b, bool expected)
    {
        await Assert.That(Scope.Overlap([a], [b]) is not null).IsEqualTo(expected);
        await Assert.That(Scope.Overlap([b], [a]) is not null).IsEqualTo(expected);
    }

    [Test]
    [Arguments("src/*.cs", "src/*.md", false)]
    [Arguments("src", "src-other/file.cs", false)]
    [Arguments("src/*/one.cs", "src/two/*.cs", true)]
    [Arguments("src/**/one.cs", "src/one.cs", true)]
    [Arguments("src/**/one.cs", "src/deep/nested/one.cs", true)]
    [Arguments("src/*/one.cs", "src/one.?s", false)]
    [Arguments("src/?/one.cs", "src/ab/one.cs", false)]
    [Arguments("src/?/one.cs", "src/a/one.cs", true)]
    [Arguments("**/a", "b/**", true)]
    [Arguments("**/a", "za?", false)]
    [Arguments("a*b", "a*c", false)]
    [Arguments("src/", "src/nested/file.cs", true)]
    [Arguments("./src\\**", "/src/file.cs", true)]
    [Arguments(".github/", "github/", false)]
    [Arguments("src", "src/file.cs", true)]
    [Arguments("a", "a\n", true)]
    public async Task Overlap_MatchesCommonPaths(string a, string b, bool expected)
    {
        await Assert.That(Scope.Overlap([a], [b]) is not null).IsEqualTo(expected);
        await Assert.That(Scope.Overlap([b], [a]) is not null).IsEqualTo(expected);
    }

    [Test]
    public async Task Overlap_UsesPositiveGlobsOnly()
    {
        await Assert.That(Scope.Overlap(["src/**", "!shared/**"], ["docs/**", "!shared/**"])).IsNull();
        await Assert.That(Scope.Overlap(["!src/**"], ["src/**"])).IsNull();
        await Assert.That(Scope.Overlap(["src/**", "!src/shared/**"], ["src/shared/**"])).IsNotNull();
    }

    [Test]
    [Arguments("**/a", "za", "za/a")]
    [Arguments("src/*/one.cs", "src/one.cs", "src/one.cs/one.cs")]
    [Arguments("src/*/one.cs", "src/two/*.cs", "src/two/one.cs")]
    [Arguments("src/**/one.cs", "src/*.cs", "src/one.cs")]
    public async Task Overlap_CommonPathWitness(string a, string b, string path)
    {
        await Assert.That(Scope.InScope(path, [a])).IsTrue();
        await Assert.That(Scope.InScope(path, [b])).IsTrue();
        await Assert.That(Scope.Overlap([a], [b])).IsNotNull();
    }

    [Test]
    [Arguments("src/one.cs", "src/*.cs", true)]
    [Arguments("src/deep/one.cs", "src/*.cs", false)]
    [Arguments("src/one.cs", "src/**/one.cs", true)]
    [Arguments("src/deep/one.cs", "src/**/one.cs", true)]
    [Arguments("src/zone.cs", "src/**/one.cs", false)]
    [Arguments("src/deep/one.cs", "src", true)]
    [Arguments("src-other/one.cs", "src", false)]
    public async Task Scope_GlobLanguage(string path, string glob, bool expected)
    {
        await Assert.That(Scope.InScope(path, [glob])).IsEqualTo(expected);
    }
}
