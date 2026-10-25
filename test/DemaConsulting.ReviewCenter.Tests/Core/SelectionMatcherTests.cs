// Copyright (c) DEMA Consulting
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

using DemaConsulting.ReviewCenter.Core;
using Microsoft.Extensions.FileSystemGlobbing;

namespace DemaConsulting.ReviewCenter.Tests.Core;

/// <summary>
///     Unit tests for <see cref="SelectionMatcher"/>: parsing, the matching rules, the rejected inputs and a
///     differential test against Microsoft.Extensions.FileSystemGlobbing.
/// </summary>
public class SelectionMatcherTests
{
    /// <summary>
    ///     The paths used by the matching table and the differential test.
    /// </summary>
    private static readonly string[] _corpus =
    [
        "README.md",
        "a.cs",
        "src/a.cs",
        "src/b.cs",
        "src/a.txt",
        "src/deep/c.cs",
        "src/deep/deeper/d.cs",
        "src/deep/deeper/d.test.cs",
        "test/a.cs",
        "test/sub/x.cs",
        "docs/guide.md",
        "docs/api/ref.md",
        "docs/api/v2/ref.md",
        "src.cs",
        "abc",
        "a/b/c/d/e"
    ];

    /// <summary>
    ///     Selects the paths of the corpus that a selection text matches.
    /// </summary>
    /// <param name="text">The selection text.</param>
    /// <returns>The selected paths in corpus order.</returns>
    private static string[] Select(string text)
    {
        var patterns = SelectionMatcher.Parse(text);
        return _corpus.Where(path => SelectionMatcher.IsMatch(path, patterns)).ToArray();
    }

    /// <summary>
    ///     Test that the patterns select the expected paths, covering literals, folders, wildcards and exclusions.
    /// </summary>
    /// <param name="text">The selection text.</param>
    /// <param name="expected">The expected paths, separated by a vertical bar.</param>
    [Theory]
    [InlineData("README.md", "README.md")]
    [InlineData("./README.md", "README.md")]
    [InlineData("/README.md", "README.md")]
    [InlineData("*.cs", "a.cs|src.cs")]
    [InlineData("src/*.cs", "src/a.cs|src/b.cs")]
    [InlineData("src/**", "src/a.cs|src/b.cs|src/a.txt|src/deep/c.cs|src/deep/deeper/d.cs|src/deep/deeper/d.test.cs")]
    [InlineData("src/", "src/a.cs|src/b.cs|src/a.txt|src/deep/c.cs|src/deep/deeper/d.cs|src/deep/deeper/d.test.cs")]
    [InlineData("**/*.cs", "a.cs|src/a.cs|src/b.cs|src/deep/c.cs|src/deep/deeper/d.cs|src/deep/deeper/d.test.cs|test/a.cs|test/sub/x.cs|src.cs")]
    [InlineData("src/**/d.cs", "src/deep/deeper/d.cs")]
    [InlineData("src/**/c.cs", "src/deep/c.cs")]
    [InlineData("a/**/e", "a/b/c/d/e")]
    [InlineData("a/**/b/c/d/e", "a/b/c/d/e")]
    [InlineData("src/?.cs", "src/a.cs|src/b.cs")]
    [InlineData("???", "abc")]
    [InlineData("docs/**/ref.md", "docs/api/ref.md|docs/api/v2/ref.md")]
    [InlineData("src/*.cs\n!src/b.cs", "src/a.cs")]
    [InlineData("src/**\n!src/deep/**", "src/a.cs|src/b.cs|src/a.txt")]
    [InlineData("src/**\n!src/deep/**\nsrc/deep/c.cs", "src/a.cs|src/b.cs|src/a.txt|src/deep/c.cs")]
    [InlineData("!src/b.cs\nsrc/*.cs", "src/a.cs|src/b.cs")]
    [InlineData("nothing/here", "")]
    [InlineData("  src/a.cs  \r\n\r\n  test/a.cs  ", "src/a.cs|test/a.cs")]
    [InlineData("SRC/a.cs", "")]
    [InlineData("[ab].cs", "")]
    public void SelectionMatcher_IsMatch_Patterns_SelectExpectedPaths(string text, string expected)
    {
        // Arrange: the expected paths in corpus order
        var wanted = expected.Length == 0 ? [] : expected.Split('|');

        // Act: select from the corpus
        var selected = Select(text);

        // Assert: exactly the expected paths, regardless of order
        Assert.Equal(wanted.OrderBy(p => p, StringComparer.Ordinal), selected.OrderBy(p => p, StringComparer.Ordinal));
    }

    /// <summary>
    ///     Test that a trailing double star needs at least one segment, so a file is not selected by its own name
    ///     used as a folder.
    /// </summary>
    [Fact]
    public void SelectionMatcher_IsMatch_TrailingDoubleStar_NeedsAtLeastOneSegment()
    {
        // Arrange: a folder pattern
        var patterns = SelectionMatcher.Parse("src/**");

        // Act: match the folder name itself and a file below it
        var folderItself = SelectionMatcher.IsMatch("src", patterns);
        var below = SelectionMatcher.IsMatch("src/a.cs", patterns);

        // Assert: only the file below is selected
        Assert.False(folderItself);
        Assert.True(below);
    }

    /// <summary>
    ///     Test that a star never crosses a path separator.
    /// </summary>
    [Fact]
    public void SelectionMatcher_IsMatch_Star_DoesNotCrossSeparators()
    {
        // Arrange: a one-level pattern
        var patterns = SelectionMatcher.Parse("src/*");

        // Act: match a direct child and a grandchild
        var child = SelectionMatcher.IsMatch("src/a.cs", patterns);
        var grandchild = SelectionMatcher.IsMatch("src/deep/c.cs", patterns);

        // Assert: only the direct child matches
        Assert.True(child);
        Assert.False(grandchild);
    }

    /// <summary>
    ///     Test that a pattern with many double stars is matched in bounded time (no exponential backtracking).
    /// </summary>
    [Fact]
    public void SelectionMatcher_IsMatch_ManyDoubleStars_FinishesQuickly()
    {
        // Arrange: a worst-case pattern against a deep path that does not match
        var patterns = SelectionMatcher.Parse(string.Join("/", Enumerable.Repeat("**", 40)) + "/never.cs");
        var path = string.Join("/", Enumerable.Repeat("d", 60)) + "/x.cs";

        // Act: match under a time limit
        var timer = System.Diagnostics.Stopwatch.StartNew();
        var matched = SelectionMatcher.IsMatch(path, patterns);
        timer.Stop();

        // Assert: no match, found quickly
        Assert.False(matched);
        Assert.True(timer.Elapsed < TimeSpan.FromSeconds(5), $"Matching took {timer.Elapsed}.");
    }

    /// <summary>
    ///     Test that patterns are normalized when parsed.
    /// </summary>
    [Fact]
    public void SelectionMatcher_Parse_Lines_AreTrimmedAndNormalized()
    {
        // Arrange: a text with blank lines, padding, an exclusion and a folder
        var text = "\n  ./src/*.cs  \n\n  ! docs/  \r\n/README.md\n";

        // Act: parse it
        var patterns = SelectionMatcher.Parse(text);

        // Assert: three patterns with the marks and the folder turned into a recursive pattern
        Assert.Equal(3, patterns.Count);
        Assert.Equal("src/*.cs", patterns[0].Text);
        Assert.False(patterns[0].Exclude);
        Assert.Equal("docs/", patterns[1].Text);
        Assert.True(patterns[1].Exclude);
        Assert.Equal(["docs", "**"], patterns[1].Segments);
        Assert.Equal("README.md", patterns[2].Text);
    }

    /// <summary>
    ///     Test that invalid selections are rejected with the selection rule.
    /// </summary>
    /// <param name="text">The invalid selection text.</param>
    [Theory]
    [InlineData("")]
    [InlineData("   \n \n")]
    [InlineData("!")]
    [InlineData("  !   ")]
    [InlineData("/")]
    [InlineData("./")]
    [InlineData("..")]
    [InlineData("../secret")]
    [InlineData("src/../x")]
    [InlineData("src/./x")]
    [InlineData("src//x")]
    [InlineData("src\\x")]
    [InlineData("a\u0001b")]
    [InlineData("a\tb")]
    public void SelectionMatcher_Parse_InvalidText_ThrowsSelectionInvalid(string text)
    {
        // Arrange: nothing beyond the text

        // Act: parse it
        var exception = Assert.Throws<DomainRuleException>(() => SelectionMatcher.Parse(text));

        // Assert: the rule is named
        Assert.Equal(DomainRule.SelectionInvalid, exception.Rule);
    }

    /// <summary>
    ///     Test that the pattern count and the pattern length have inclusive limits.
    /// </summary>
    [Fact]
    public void SelectionMatcher_Parse_Limits_AreInclusive()
    {
        // Arrange: texts at and beyond the limits
        var atCount = string.Join("\n", Enumerable.Range(0, SelectionMatcher.MaxPatterns).Select(i => $"f{i}.cs"));
        var overCount = atCount + "\nextra.cs";
        var atLength = new string('a', SelectionMatcher.MaxPatternLength);
        var overLength = new string('a', SelectionMatcher.MaxPatternLength + 1);

        // Act: parse them
        var counted = SelectionMatcher.Parse(atCount);
        var lengthOk = SelectionMatcher.Parse(atLength);
        var tooMany = Assert.Throws<DomainRuleException>(() => SelectionMatcher.Parse(overCount));
        var tooLong = Assert.Throws<DomainRuleException>(() => SelectionMatcher.Parse(overLength));

        // Assert: the limits are inclusive and the rule is named
        Assert.Equal(SelectionMatcher.MaxPatterns, counted.Count);
        Assert.Single(lengthOk);
        Assert.Equal(DomainRule.SelectionInvalid, tooMany.Rule);
        Assert.Equal(DomainRule.SelectionInvalid, tooLong.Rule);
    }

    /// <summary>
    ///     Test that null arguments are refused.
    /// </summary>
    [Fact]
    public void SelectionMatcher_NullArguments_ThrowArgumentNull()
    {
        // Arrange: a valid pattern list
        var patterns = SelectionMatcher.Parse("a");

        // Act and Assert: null text, path and patterns are refused
        Assert.Throws<ArgumentNullException>(() => SelectionMatcher.Parse(null!));
        Assert.Throws<ArgumentNullException>(() => SelectionMatcher.IsMatch(null!, patterns));
        Assert.Throws<ArgumentNullException>(() => SelectionMatcher.IsMatch("a", null!));
    }

    /// <summary>
    ///     Test that include patterns, followed by exclusions, select the same paths as the globbing library that
    ///     ReviewMark uses, for the syntax both support (literals, star and double star).
    /// </summary>
    /// <param name="includes">The include patterns, separated by a vertical bar.</param>
    /// <param name="excludes">The exclude patterns, separated by a vertical bar, or empty.</param>
    [Theory]
    [InlineData("README.md", "")]
    [InlineData("*.cs", "")]
    [InlineData("src/*.cs", "")]
    [InlineData("src/**", "")]
    [InlineData("src/**/*.cs", "")]
    [InlineData("**/*.cs", "")]
    [InlineData("**/*.cs", "test/**")]
    [InlineData("**/*.cs", "**/d.test.cs")]
    [InlineData("src/**/d.cs", "")]
    [InlineData("a/**/e", "")]
    [InlineData("a/**/b/c/d/e", "")]
    [InlineData("docs/**/ref.md", "")]
    [InlineData("docs/**", "docs/api/**")]
    [InlineData("src/**|test/**", "**/*.txt")]
    [InlineData("src/a.*", "")]
    [InlineData("*c", "")]
    [InlineData("a*", "")]
    [InlineData("**/deep/*.cs", "")]
    [InlineData("nothing/here", "")]
    public void SelectionMatcher_IsMatch_CommonSyntax_AgreesWithFileSystemGlobbing(string includes, string excludes)
    {
        // Arrange: the same patterns for both implementations (the library applies the exclusions last, so the
        // text lists them last)
        var includeList = includes.Split('|');
        var excludeList = excludes.Length == 0 ? [] : excludes.Split('|');
        var matcher = new Matcher(StringComparison.Ordinal);
        foreach (var include in includeList)
        {
            matcher.AddInclude(include);
        }

        foreach (var exclude in excludeList)
        {
            matcher.AddExclude(exclude);
        }

        var text = string.Join("\n", includeList.Concat(excludeList.Select(e => "!" + e)));

        // Act: select with both
        var expected = matcher.Match("root", _corpus).Files.Select(f => f.Path).OrderBy(p => p, StringComparer.Ordinal).ToArray();
        var actual = Select(text).OrderBy(p => p, StringComparer.Ordinal).ToArray();

        // Assert: the same paths
        Assert.Equal(expected, actual);
    }
}
