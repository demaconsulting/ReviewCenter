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

using System.Text;
using DemaConsulting.ReviewCenter.ContentSources;
using DemaConsulting.ReviewCenter.Core;

namespace DemaConsulting.ReviewCenter.Tests.ContentSources;

/// <summary>
///     Unit tests for <see cref="ReviewMarkConfigParser"/>: the review-sets it reads out of a ReviewMark
///     configuration, what it ignores, and how it refuses files that are broken or too big.
/// </summary>
public class ReviewMarkConfigParserTests
{
    /// <summary>
    ///     Parses a text as UTF-8.
    /// </summary>
    /// <param name="text">The text of the file.</param>
    /// <returns>The review-sets.</returns>
    private static IReadOnlyList<ReviewSetDefinition> Parse(string text) =>
        new ReviewMarkConfigParser().Parse(Encoding.UTF8.GetBytes(text));

    /// <summary>
    ///     Finds the root of the repository by looking for the solution file above the test output folder.
    /// </summary>
    /// <returns>The root folder.</returns>
    private static string FindRoot()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "DemaConsulting.ReviewCenter.slnx")))
        {
            folder = folder.Parent;
        }

        return folder?.FullName ?? throw new InvalidOperationException("The repository root was not found.");
    }

    /// <summary>
    ///     Test that the review-sets of this repository's own ReviewMark configuration are read, each with a selection
    ///     that the selection matcher accepts, in file order with unique ids.
    /// </summary>
    [Fact]
    public void ReviewMarkConfigParser_Parse_RepositoryOwnConfiguration_ReadsEverySet()
    {
        // Arrange: the real file, which also has needs-review, evidence-source and context keys
        var bytes = File.ReadAllBytes(Path.Combine(FindRoot(), ".reviewmark.yaml"));

        // Act: parse it
        var sets = new ReviewMarkConfigParser().Parse(bytes);

        // Assert: many sets, unique ids, usable selections, and the first set as written in the file
        Assert.True(sets.Count > 10);
        Assert.Equal(sets.Count, sets.Select(set => set.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.All(sets, set => Assert.NotEmpty(SelectionMatcher.Parse(string.Join('\n', set.Paths))));
        Assert.Equal("Purpose", sets[0].Id);
        Assert.Equal("Review of user-facing capabilities and system promises", sets[0].Title);
        Assert.Equal(["README.md", "docs/user_guide/**/*.md"], sets[0].Paths);
    }

    /// <summary>
    ///     Test that only <c>reviews</c> is read, with <c>id</c>, <c>title</c> and <c>paths</c>, and everything else
    ///     is ignored at every level.
    /// </summary>
    [Fact]
    public void ReviewMarkConfigParser_Parse_UnknownKeys_AreIgnored()
    {
        // Arrange: a file with other top-level keys and extra keys in an entry
        const string text = """
            ---
            needs-review:
              - "**/*.cs"
            context:
              - docs/intro.md
            reviews:
              - id: core
                title: Core review
                context:
                  - docs/intro.md
                extra:
                  nested: [1, 2]
                paths:
                  - "src/**"
                  - "!src/gen/**"
              - id: docs
                title: Documentation
                paths:
                  - "docs/**"
            """;

        // Act: parse it
        var sets = Parse(text);

        // Assert: the two sets in file order, with ordered paths
        Assert.Equal(["core", "docs"], sets.Select(set => set.Id));
        Assert.Equal("Core review", sets[0].Title);
        Assert.Equal(["src/**", "!src/gen/**"], sets[0].Paths);
        Assert.Equal(["docs/**"], sets[1].Paths);
    }

    /// <summary>
    ///     Test that flow style, quoting, block scalars, a byte order mark and Windows line ends are understood like
    ///     ReviewMark understands them, and that values are trimmed.
    /// </summary>
    [Fact]
    public void ReviewMarkConfigParser_Parse_YamlStyles_AreUnderstood()
    {
        // Arrange: a file with a byte order mark, CRLF line ends, flow and block styles
        var text = "reviews:\r\n"
            + "  - {id: a, title: \"A: the first\", paths: [\"src/**\", '!src/gen/**']}\r\n"
            + "  - id: ' b '\r\n"
            + "    title: >-\r\n"
            + "      B is folded\r\n"
            + "      over two lines\r\n"
            + "    paths: [ x.txt , \"y z.txt\" ]\r\n"
            + "  - id: c\r\n"
            + "    title: C\r\n";
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(text)).ToArray();

        // Act: parse it
        var sets = new ReviewMarkConfigParser().Parse(bytes);

        // Assert: values as YAML defines them
        Assert.Equal(["a", "b", "c"], sets.Select(set => set.Id));
        Assert.Equal("A: the first", sets[0].Title);
        Assert.Equal(["src/**", "!src/gen/**"], sets[0].Paths);
        Assert.Equal("B is folded over two lines", sets[1].Title);
        Assert.Equal(["x.txt", "y z.txt"], sets[1].Paths);
        Assert.Empty(sets[2].Paths);
    }

    /// <summary>
    ///     Test that the block style (mappings and lists on their own lines, plain values) is read.
    /// </summary>
    [Fact]
    public void ReviewMarkConfigParser_Parse_BlockStyle_IsRead()
    {
        // Arrange: a file in block style with two sets
        const string text = "reviews:\n"
            + "  - id: a\n"
            + "    title: Block A\n"
            + "    paths:\n"
            + "      - src/**\n"
            + "      - docs/a.md\n"
            + "  - id: b\n"
            + "    title: Block B\n"
            + "    paths:\n"
            + "      - src/b.cs\n";

        // Act: parse it
        var sets = Parse(text);

        // Assert: both sets with their titles and paths
        Assert.Equal(["a", "b"], sets.Select(set => set.Id));
        Assert.Equal("Block A", sets[0].Title);
        Assert.Equal(["src/**", "docs/a.md"], sets[0].Paths);
        Assert.Equal(["src/b.cs"], sets[1].Paths);
    }

    /// <summary>
    ///     Test that the flow style (braces and brackets on one line) is read.
    /// </summary>
    [Fact]
    public void ReviewMarkConfigParser_Parse_FlowStyle_IsRead()
    {
        // Arrange: a file whose entry and path list are in flow style
        const string text = "reviews:\n  - {id: a, title: Flow A, paths: [src/**, docs/a.md]}\n";

        // Act: parse it
        var sets = Parse(text);

        // Assert: the set with its title and both paths
        var set = Assert.Single(sets);
        Assert.Equal("a", set.Id);
        Assert.Equal("Flow A", set.Title);
        Assert.Equal(["src/**", "docs/a.md"], set.Paths);
    }

    /// <summary>
    ///     Test that single-quoted and double-quoted strings are read as YAML defines them.
    /// </summary>
    [Fact]
    public void ReviewMarkConfigParser_Parse_QuotedStrings_AreRead()
    {
        // Arrange: quoted values: a colon and an escaped quote in a title, and single quotes around a leading exclamation
        const string text = "reviews:\n"
            + "  - id: \"a\"\n"
            + "    title: \"A: the \\\"first\\\" one\"\n"
            + "    paths: ['!src/gen/**', \"y z.txt\"]\n";

        // Act: parse it
        var sets = Parse(text);

        // Assert: the quotes are removed and the escapes are applied
        var set = Assert.Single(sets);
        Assert.Equal("a", set.Id);
        Assert.Equal("A: the \"first\" one", set.Title);
        Assert.Equal(["!src/gen/**", "y z.txt"], set.Paths);
    }

    /// <summary>
    ///     Test that a folded block scalar is read as one line.
    /// </summary>
    [Fact]
    public void ReviewMarkConfigParser_Parse_FoldedText_IsRead()
    {
        // Arrange: a title written as a folded block scalar over two lines
        const string text = "reviews:\n"
            + "  - id: a\n"
            + "    title: >-\n"
            + "      B is folded\n"
            + "      over two lines\n";

        // Act: parse it
        var sets = Parse(text);

        // Assert: the lines are joined with one space
        Assert.Equal("B is folded over two lines", Assert.Single(sets).Title);
    }

    /// <summary>
    ///     Test that white space around the identifier, the title and each path is removed.
    /// </summary>
    [Fact]
    public void ReviewMarkConfigParser_Parse_PaddedValues_AreTrimmed()
    {
        // Arrange: quoted values that start and end with spaces
        const string text = "reviews:\n"
            + "  - id: '  a  '\n"
            + "    title: '  Padded title  '\n"
            + "    paths: ['  src/**  ', ' docs/a.md']\n";

        // Act: parse it
        var sets = Parse(text);

        // Assert: nothing of the padding is left
        var set = Assert.Single(sets);
        Assert.Equal("a", set.Id);
        Assert.Equal("Padded title", set.Title);
        Assert.Equal(["src/**", "docs/a.md"], set.Paths);
    }

    /// <summary>
    ///     Test that a review-set without a paths key, and one with an empty paths list, get an empty list of paths.
    /// </summary>
    [Fact]
    public void ReviewMarkConfigParser_Parse_SetWithoutPaths_HasEmptyPathList()
    {
        // Arrange: one set without the key and one with an empty list
        const string text = "reviews:\n"
            + "  - id: a\n"
            + "    title: A\n"
            + "  - id: b\n"
            + "    title: B\n"
            + "    paths: []\n";

        // Act: parse it
        var sets = Parse(text);

        // Assert: both sets are read and both have no paths
        Assert.Equal(["a", "b"], sets.Select(set => set.Id));
        Assert.All(sets, set => Assert.Empty(set.Paths));
    }

    /// <summary>
    ///     Test that a file that starts with a UTF-8 byte order mark is read as if the mark was not there.
    /// </summary>
    [Fact]
    public void ReviewMarkConfigParser_Parse_ByteOrderMark_IsSkipped()
    {
        // Arrange: the bytes of a small file that start with the byte order mark
        var bytes = Encoding.UTF8.GetPreamble()
            .Concat(Encoding.UTF8.GetBytes("reviews:\n  - id: a\n    title: A\n"))
            .ToArray();

        // Act: parse it
        var sets = new ReviewMarkConfigParser().Parse(bytes);

        // Assert: the one set is read
        var set = Assert.Single(sets);
        Assert.Equal("a", set.Id);
        Assert.Equal("A", set.Title);
    }

    /// <summary>
    ///     Test that a file that uses Windows line endings is read like one with Unix line endings.
    /// </summary>
    [Fact]
    public void ReviewMarkConfigParser_Parse_WindowsLineEnds_AreUnderstood()
    {
        // Arrange: a small file with carriage return and line feed at the end of every line
        const string text = "reviews:\r\n  - id: a\r\n    title: A\r\n    paths:\r\n      - x.txt\r\n";

        // Act: parse it
        var sets = Parse(text);

        // Assert: the set and its path are read without a carriage return
        var set = Assert.Single(sets);
        Assert.Equal("a", set.Id);
        Assert.Equal("A", set.Title);
        Assert.Equal(["x.txt"], set.Paths);
    }

    /// <summary>
    ///     Test that files that are empty, have no review-sets or are broken are refused with a plain message.
    /// </summary>
    /// <param name="text">The text of the file.</param>
    /// <param name="expected">A part of the expected message.</param>
    [Theory]
    [InlineData("", "defines no review-sets")]
    [InlineData("---\n", "defines no review-sets")]
    [InlineData("needs-review:\n  - a\n", "defines no review-sets")]
    [InlineData("reviews: []\n", "defines no review-sets")]
    [InlineData("reviews:\n  - title: T\n    paths: [a]\n", "Review-set 1 has no id")]
    [InlineData("reviews:\n  - id: a\n    title: A\n  - id: b\n", "Review-set 2 has no title")]
    [InlineData("reviews:\n  -\n", "Review-set 1 has no id")]
    [InlineData("reviews:\n  - id: a\n    title: A\n  - id: a\n    title: B\n", "Review-set 2 repeats the id")]
    [InlineData("reviews: [\n", "not valid YAML (line")]
    [InlineData("reviews: text\n", "not valid YAML (line")]
    [InlineData("reviews:\n  - just text\n", "not valid YAML (line")]
    [InlineData("reviews:\n  - id: a\n    title: A\n    paths: [\"\"]\n", "A path of review-set 1")]
    [InlineData("reviews:\n  - id: a\n    title: A\n    paths: [\"a\\nb\"]\n", "A path of review-set 1")]
    [InlineData("reviews:\n  - id: a\n    title: A\n    paths:\n      -\n", "A path of review-set 1")]
    [InlineData("reviews:\n  - id: \"a\\nb\"\n    title: A\n", "The id of review-set 1")]
    [InlineData("reviews:\n  - id: a\n    title: \"A\\tB\"\n", "The title of review-set 1")]
    public void ReviewMarkConfigParser_Parse_BrokenFile_ThrowsConfigInvalid(string text, string expected)
    {
        // Arrange: the text of a file that cannot be used

        // Act: parse it
        var exception = Assert.Throws<DomainRuleException>(() => Parse(text));

        // Assert: the rule, a plain message and nothing of the library's own text
        Assert.Equal(DomainRule.ReviewSetConfigInvalid, exception.Rule);
        Assert.Contains(expected, exception.Message);
        Assert.DoesNotContain("YamlDotNet", exception.Message);
        Assert.DoesNotContain(" at ", exception.Message);
    }

    /// <summary>
    ///     Test that every length and count limit is enforced, one past the limit being refused and the limit itself accepted.
    /// </summary>
    [Fact]
    public void ReviewMarkConfigParser_Parse_Limits_AreEnforced()
    {
        // Arrange: files exactly at each limit, and one past it
        string Entry(string id, string title, int paths) =>
            $"reviews:\n  - id: {id}\n    title: {title}\n    paths: [{string.Join(", ", Enumerable.Repeat("a", paths))}]\n";
        var longPath = new string('p', SelectionMatcher.MaxPatternLength);
        var manySets = "reviews:\n" + string.Concat(Enumerable.Range(0, ReviewLimits.MaxReviewSets + 1).Select(i => $"  - {{id: s{i}, title: T}}\n"));

        // Act: parse the files at the limits
        var atLimits = Parse(Entry(new string('i', ReviewLimits.MaxReviewSetIdLength), new string('t', ReviewLimits.MaxReviewSetTitleLength), SelectionMatcher.MaxPatterns));
        var atPath = Parse($"reviews:\n  - id: a\n    title: A\n    paths: [{longPath}]\n");
        var atSets = Parse(manySets.Replace($"  - {{id: s{ReviewLimits.MaxReviewSets}, title: T}}\n", string.Empty, StringComparison.Ordinal));

        // Assert: accepted at the limits
        Assert.Equal(SelectionMatcher.MaxPatterns, atLimits[0].Paths.Count);
        Assert.Equal(longPath, atPath[0].Paths[0]);
        Assert.Equal(ReviewLimits.MaxReviewSets, atSets.Count);

        // Assert: one past each limit is refused with the rule
        var refusals = new[]
        {
            Entry(new string('i', ReviewLimits.MaxReviewSetIdLength + 1), "T", 1),
            Entry("a", new string('t', ReviewLimits.MaxReviewSetTitleLength + 1), 1),
            Entry("a", "T", SelectionMatcher.MaxPatterns + 1),
            $"reviews:\n  - id: a\n    title: A\n    paths: [{longPath}p]\n",
            manySets
        };
        foreach (var text in refusals)
        {
            Assert.Equal(DomainRule.ReviewSetConfigInvalid, Assert.Throws<DomainRuleException>(() => Parse(text)).Rule);
        }
    }

    /// <summary>
    ///     Test that a file with exactly the most review-sets that are allowed is read.
    /// </summary>
    [Fact]
    public void ReviewMarkConfigParser_Parse_SetCountAtLimit_IsRead()
    {
        // Arrange: a file with exactly the allowed number of review-sets
        var text = "reviews:\n"
            + string.Concat(Enumerable.Range(0, ReviewLimits.MaxReviewSets).Select(i => $"  - {{id: s{i}, title: T}}\n"));

        // Act: parse it
        var sets = Parse(text);

        // Assert: every set is read
        Assert.Equal(ReviewLimits.MaxReviewSets, sets.Count);
    }

    /// <summary>
    ///     Test that an identifier with exactly the most characters that are allowed is read.
    /// </summary>
    [Fact]
    public void ReviewMarkConfigParser_Parse_IdLengthAtLimit_IsRead()
    {
        // Arrange: an identifier exactly at the limit
        var id = new string('i', ReviewLimits.MaxReviewSetIdLength);

        // Act: parse it
        var sets = Parse($"reviews:\n  - id: {id}\n    title: T\n");

        // Assert: the identifier is read in full
        Assert.Equal(id, Assert.Single(sets).Id);
    }

    /// <summary>
    ///     Test that a title with exactly the most characters that are allowed is read.
    /// </summary>
    [Fact]
    public void ReviewMarkConfigParser_Parse_TitleLengthAtLimit_IsRead()
    {
        // Arrange: a title exactly at the limit
        var title = new string('t', ReviewLimits.MaxReviewSetTitleLength);

        // Act: parse it
        var sets = Parse($"reviews:\n  - id: a\n    title: {title}\n");

        // Assert: the title is read in full
        Assert.Equal(title, Assert.Single(sets).Title);
    }

    /// <summary>
    ///     Test that a review-set with exactly the most paths that are allowed is read.
    /// </summary>
    [Fact]
    public void ReviewMarkConfigParser_Parse_PathCountAtLimit_IsRead()
    {
        // Arrange: a set with exactly the allowed number of paths
        var paths = string.Join(", ", Enumerable.Repeat("a", SelectionMatcher.MaxPatterns));

        // Act: parse it
        var sets = Parse($"reviews:\n  - id: a\n    title: T\n    paths: [{paths}]\n");

        // Assert: every path is read
        Assert.Equal(SelectionMatcher.MaxPatterns, Assert.Single(sets).Paths.Count);
    }

    /// <summary>
    ///     Test that a path with exactly the most characters that are allowed is read.
    /// </summary>
    [Fact]
    public void ReviewMarkConfigParser_Parse_PathLengthAtLimit_IsRead()
    {
        // Arrange: a path exactly at the limit
        var path = new string('p', SelectionMatcher.MaxPatternLength);

        // Act: parse it
        var sets = Parse($"reviews:\n  - id: a\n    title: T\n    paths: [{path}]\n");

        // Assert: the path is read in full
        Assert.Equal([path], Assert.Single(sets).Paths);
    }

    /// <summary>
    ///     Test that a file with one review-set more than allowed is refused with a message that names the limit.
    /// </summary>
    [Fact]
    public void ReviewMarkConfigParser_Parse_TooManySets_ThrowsConfigInvalid()
    {
        // Arrange: one set more than the limit
        var text = "reviews:\n"
            + string.Concat(Enumerable.Range(0, ReviewLimits.MaxReviewSets + 1).Select(i => $"  - {{id: s{i}, title: T}}\n"));

        // Act: parse it
        var exception = Assert.Throws<DomainRuleException>(() => Parse(text));

        // Assert: the rule and the limit in the message
        Assert.Equal(DomainRule.ReviewSetConfigInvalid, exception.Rule);
        Assert.Contains("more than 500 review-sets", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that an identifier with one character more than allowed is refused with a message that names the limit.
    /// </summary>
    [Fact]
    public void ReviewMarkConfigParser_Parse_IdTooLong_ThrowsConfigInvalid()
    {
        // Arrange: an identifier one past the limit
        var text = $"reviews:\n  - id: {new string('i', ReviewLimits.MaxReviewSetIdLength + 1)}\n    title: T\n";

        // Act: parse it
        var exception = Assert.Throws<DomainRuleException>(() => Parse(text));

        // Assert: the rule and the limit in the message
        Assert.Equal(DomainRule.ReviewSetConfigInvalid, exception.Rule);
        Assert.Contains("The id of review-set 1 is longer than 100", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a title with one character more than allowed is refused with a message that names the limit.
    /// </summary>
    [Fact]
    public void ReviewMarkConfigParser_Parse_TitleTooLong_ThrowsConfigInvalid()
    {
        // Arrange: a title one past the limit
        var text = $"reviews:\n  - id: a\n    title: {new string('t', ReviewLimits.MaxReviewSetTitleLength + 1)}\n";

        // Act: parse it
        var exception = Assert.Throws<DomainRuleException>(() => Parse(text));

        // Assert: the rule and the limit in the message
        Assert.Equal(DomainRule.ReviewSetConfigInvalid, exception.Rule);
        Assert.Contains("The title of review-set 1 is longer than 200", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a review-set with one path more than allowed is refused with a message that names the limit.
    /// </summary>
    [Fact]
    public void ReviewMarkConfigParser_Parse_TooManyPaths_ThrowsConfigInvalid()
    {
        // Arrange: one path more than the limit
        var paths = string.Join(", ", Enumerable.Repeat("a", SelectionMatcher.MaxPatterns + 1));

        // Act: parse it
        var exception = Assert.Throws<DomainRuleException>(
            () => Parse($"reviews:\n  - id: a\n    title: T\n    paths: [{paths}]\n"));

        // Assert: the rule and the limit in the message
        Assert.Equal(DomainRule.ReviewSetConfigInvalid, exception.Rule);
        Assert.Contains("has more than 100 paths", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a path with one character more than allowed is refused with a message that names the path.
    /// </summary>
    [Fact]
    public void ReviewMarkConfigParser_Parse_PathTooLong_ThrowsConfigInvalid()
    {
        // Arrange: a path one past the limit
        var path = new string('p', SelectionMatcher.MaxPatternLength + 1);

        // Act: parse it
        var exception = Assert.Throws<DomainRuleException>(
            () => Parse($"reviews:\n  - id: a\n    title: T\n    paths: [{path}]\n"));

        // Assert: the rule and the review-set that holds the path
        Assert.Equal(DomainRule.ReviewSetConfigInvalid, exception.Rule);
        Assert.Contains("A path of review-set 1", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that an empty path is refused with a message that names the review-set.
    /// </summary>
    [Fact]
    public void ReviewMarkConfigParser_Parse_EmptyPath_ThrowsConfigInvalid()
    {
        // Arrange: a set with an empty path next to a valid one
        const string text = "reviews:\n  - id: a\n    title: T\n    paths: [src/**, \"\"]\n";

        // Act: parse it
        var exception = Assert.Throws<DomainRuleException>(() => Parse(text));

        // Assert: the rule and the review-set that holds the path
        Assert.Equal(DomainRule.ReviewSetConfigInvalid, exception.Rule);
        Assert.Contains("A path of review-set 1", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a path with a control character is refused with a message that names the review-set.
    /// </summary>
    [Fact]
    public void ReviewMarkConfigParser_Parse_PathWithControlCharacter_ThrowsConfigInvalid()
    {
        // Arrange: a path with a line feed in the middle
        const string text = "reviews:\n  - id: a\n    title: T\n    paths: [\"a\\nb\"]\n";

        // Act: parse it
        var exception = Assert.Throws<DomainRuleException>(() => Parse(text));

        // Assert: the rule and the review-set that holds the path
        Assert.Equal(DomainRule.ReviewSetConfigInvalid, exception.Rule);
        Assert.Contains("A path of review-set 1", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a file whose size is exactly the size limit is read.
    /// </summary>
    [Fact]
    public void ReviewMarkConfigParser_Parse_FileAtSizeLimit_IsRead()
    {
        // Arrange: a valid file followed by comment characters, exactly as large as the limit allows
        var bytes = new byte[ReviewLimits.MaxReviewSetFileBytes];
        Array.Fill(bytes, (byte)'#');
        var text = Encoding.ASCII.GetBytes("reviews:\n  - id: a\n    title: A\n");
        text.CopyTo(bytes, 0);

        // Act: parse it
        var sets = new ReviewMarkConfigParser().Parse(bytes);

        // Assert: the file is the size of the limit and the set is read
        Assert.Equal(ReviewLimits.MaxReviewSetFileBytes, bytes.Length);
        Assert.Equal("a", Assert.Single(sets).Id);
    }

    /// <summary>
    ///     Test that a file over the size limit is refused before it is read as YAML.
    /// </summary>
    [Fact]
    public void ReviewMarkConfigParser_Parse_FileTooLarge_ThrowsConfigInvalid()
    {
        // Arrange: valid YAML, one byte over the limit
        var bytes = new byte[ReviewLimits.MaxReviewSetFileBytes + 1];
        Array.Fill(bytes, (byte)'#');

        // Act: parse it
        var exception = Assert.Throws<DomainRuleException>(() => new ReviewMarkConfigParser().Parse(bytes));

        // Assert: the rule and the size in the message
        Assert.Equal(DomainRule.ReviewSetConfigInvalid, exception.Rule);
        Assert.Contains("256 KiB", exception.Message);
    }

    /// <summary>
    ///     Test that a file with an alias is refused, so a small file cannot expand into a huge document.
    /// </summary>
    [Fact]
    public void ReviewMarkConfigParser_Parse_Alias_ThrowsConfigInvalid()
    {
        // Arrange: a file that repeats a list through an alias
        const string text = """
            reviews:
              - id: a
                title: A
                paths: &p [x, y]
              - id: b
                title: B
                paths: *p
            """;

        // Act: parse it
        var exception = Assert.Throws<DomainRuleException>(() => Parse(text));

        // Assert: the rule and the line of the alias
        Assert.Equal(DomainRule.ReviewSetConfigInvalid, exception.Rule);
        Assert.Contains("alias", exception.Message);
    }

    /// <summary>
    ///     Test that a missing content argument is refused.
    /// </summary>
    [Fact]
    public void ReviewMarkConfigParser_Parse_Null_Throws()
    {
        // Arrange, Act and Assert: null content is a programming error
        Assert.Throws<ArgumentNullException>(() => new ReviewMarkConfigParser().Parse(null!));
    }
}
