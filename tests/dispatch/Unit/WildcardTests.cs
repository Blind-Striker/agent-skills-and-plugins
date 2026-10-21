namespace Dispatch.Tests.Unit;

[Category("Unit")]
public sealed class WildcardTests
{
    [Test]
    [Arguments("", "", true)]
    [Arguments("text", "", false)]
    [Arguments("", "*", true)]
    [Arguments("text", "*", true)]
    [Arguments("git push", "git push", true)]
    [Arguments("git push origin", "git push", false)]
    [Arguments("prefix git push", "git push", false)]
    [Arguments("git push\n", "git push", false)]
    [Arguments("ab", "a*b", true)]
    [Arguments("a/path/to/b", "a*b", true)]
    [Arguments("a\nb", "a*b", true)]
    [Arguments("acb", "a?b", true)]
    [Arguments("a/b", "a?b", true)]
    [Arguments("a\nb", "a?b", true)]
    [Arguments("ab", "a?b", false)]
    [Arguments("accb", "a?b", false)]
    [Arguments("abc", "a**?", true)]
    [Arguments("abc", "a*d", false)]
    public async Task Patterns_match_the_whole_input(string input, string pattern, bool expected)
    {
        await Assert.That(Wildcard.Match(input, pattern)).IsEqualTo(expected);
    }

    [Test]
    [Arguments("git push", true)]
    [Arguments("git push ", true)]
    [Arguments("git push origin main", true)]
    [Arguments("git push\norigin", false)]
    [Arguments("git push \norigin", true)]
    [Arguments("git pushes", false)]
    public async Task A_trailing_space_star_also_matches_no_suffix_Today(string input, bool expected)
    {
        await Assert.That(Wildcard.Match(input, "git push *")).IsEqualTo(expected);
    }

    [Test]
    [Arguments("git", "git * *", false)]
    [Arguments("git ", "git * *", true)]
    [Arguments("git push", "git * *", true)]
    [Arguments("git", "git **", false)]
    [Arguments("git ", "git **", true)]
    public async Task Only_the_final_space_single_star_is_optional_Today(string input, string pattern, bool expected)
    {
        await Assert.That(Wildcard.Match(input, pattern)).IsEqualTo(expected);
    }

    [Test]
    [Arguments(@"src\nested\file.cs", "src/*/file.cs")]
    [Arguments("src/nested/file.cs", @"src\*\file.cs")]
    [Arguments(@"src\nested/file.cs", @"src/nested\file.cs")]
    public async Task Backslashes_in_both_input_and_pattern_are_normalized(string input, string pattern)
    {
        await Assert.That(Wildcard.Match(input, pattern)).IsTrue();
    }

    [Test]
    [Arguments(".")]
    [Arguments("+")]
    [Arguments("^")]
    [Arguments("$")]
    [Arguments("{")]
    [Arguments("}")]
    [Arguments("(")]
    [Arguments(")")]
    [Arguments("|")]
    [Arguments("[")]
    [Arguments("]")]
    public async Task Regex_metacharacters_are_literal(string literal)
    {
        await Assert.That(Wildcard.Match($"a{literal}b", $"a{literal}b")).IsTrue();
        await Assert.That(Wildcard.Match("axb", $"a{literal}b")).IsFalse();
    }

    [Test]
    [Arguments("SRC/File.cs", "src/file.cs")]
    [Arguments("GIT PUSH origin", "git push *")]
    public async Task Case_is_ignored_only_on_Windows_Today(string input, string pattern)
    {
        await Assert.That(Wildcard.Match(input, pattern)).IsEqualTo(OperatingSystem.IsWindows());
    }
}
