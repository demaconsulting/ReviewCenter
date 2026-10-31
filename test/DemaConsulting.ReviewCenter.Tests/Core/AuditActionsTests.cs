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

using System.Text.RegularExpressions;
using DemaConsulting.ReviewCenter.Core;

namespace DemaConsulting.ReviewCenter.Tests.Core;

/// <summary>
///     Tests for <see cref="AuditActions"/>: the invariants of the vocabulary, and a scan of the product sources
///     that fails when the code records an action the vocabulary does not list (or the list keeps one the code no
///     longer records), so the filter of the audit pages cannot drift from what is really recorded.
/// </summary>
public static partial class AuditActionsTests
{
    /// <summary>
    ///     The group of the actions the self test records into its own scratch database; they are never in the
    ///     trail of an installation.
    /// </summary>
    private const string SelfTestPrefix = "selftest.";

    /// <summary>
    ///     Test that the list is sorted, has no duplicates and every code has the <c>group.name</c> form.
    /// </summary>
    [Fact]
    public static void AuditActions_All_IsSortedUniqueAndWellFormed()
    {
        // Arrange
        var all = AuditActions.All;

        // Act / Assert
        Assert.Equal(all.Order(StringComparer.Ordinal), all);
        Assert.Equal(all.Count, all.Distinct(StringComparer.Ordinal).Count());
        Assert.All(all, action => Assert.Matches(@"^[a-z]+\.[a-z]+(-[a-z]+)*$", action));
        Assert.All(all, action => Assert.True(action.Length <= AuditText.MaxActionLength));
    }

    /// <summary>
    ///     Test that every group is the prefix of at least one action and that the groups and the lookups agree.
    /// </summary>
    [Fact]
    public static void AuditActions_Prefixes_AreTheGroupsOfTheActions()
    {
        // Arrange
        var prefixes = AuditActions.Prefixes;

        // Act / Assert
        Assert.Equal(prefixes.Order(StringComparer.Ordinal), prefixes);
        Assert.All(prefixes, prefix => Assert.EndsWith(".", prefix, StringComparison.Ordinal));
        Assert.All(prefixes, prefix => Assert.NotEmpty(AuditActions.InGroup(prefix)));
        Assert.All(AuditActions.All, action => Assert.Contains(AuditActions.PrefixOf(action), prefixes));
        Assert.Equal(AuditActions.All.Count, prefixes.Sum(prefix => AuditActions.InGroup(prefix).Count));
        Assert.Empty(AuditActions.InGroup("nothing."));
    }

    /// <summary>
    ///     Test the lookups for known, unknown and missing values.
    /// </summary>
    [Fact]
    public static void AuditActions_IsKnown_AcceptsOnlyListedValues()
    {
        // Arrange / Act / Assert
        Assert.True(AuditActions.IsKnownAction("review.created"));
        Assert.False(AuditActions.IsKnownAction("review."));
        Assert.False(AuditActions.IsKnownAction("Review.Created"));
        Assert.False(AuditActions.IsKnownAction(null));
        Assert.True(AuditActions.IsKnownPrefix("review."));
        Assert.False(AuditActions.IsKnownPrefix("review"));
        Assert.False(AuditActions.IsKnownPrefix("review.created"));
        Assert.False(AuditActions.IsKnownPrefix(null));
        Assert.Equal("review.", AuditActions.PrefixOf("review.created"));
        Assert.Equal("plain", AuditActions.PrefixOf("plain"));
        Assert.Throws<ArgumentNullException>(() => AuditActions.PrefixOf(null!));
        Assert.Throws<ArgumentNullException>(() => AuditActions.InGroup(null!));
    }

    /// <summary>
    ///     Test that every action the product sources record is in the vocabulary, and that every action of the
    ///     vocabulary is still recorded somewhere in the sources.
    /// </summary>
    [Fact]
    public static void AuditActions_All_MatchesTheActionsTheSourcesRecord()
    {
        // Arrange: every string literal that is the action of an audit draft, or that is a constant named like an
        // action, or that starts with a known group
        var sources = ReadSources();
        var groupLiteral = new Regex("\"((?:" + string.Join('|', AuditActions.Prefixes.Select(p => Regex.Escape(p))) + ")[a-z-]+)\"");
        var recorded = new HashSet<string>(StringComparer.Ordinal);
        foreach (var text in sources)
        {
            foreach (var match in DraftActionRegex().Matches(text).Concat(ActionConstantRegex().Matches(text)).Concat(groupLiteral.Matches(text)))
            {
                recorded.Add(match.Groups[1].Value);
            }
        }

        recorded.RemoveWhere(action => action.StartsWith(SelfTestPrefix, StringComparison.Ordinal));

        // Act
        var missing = recorded.Except(AuditActions.All).Order(StringComparer.Ordinal).ToList();
        var stale = AuditActions.All.Except(recorded).Order(StringComparer.Ordinal).ToList();

        // Assert
        Assert.True(recorded.Count > 40, "The scan found too few actions to be trusted.");
        Assert.Empty(missing);
        Assert.Empty(stale);
    }

    /// <summary>
    ///     Reads the text of every C# source file of the product projects.
    /// </summary>
    /// <returns>The contents of the files; generated and build output folders are skipped without being read, and
    ///     so is the vocabulary file itself.</returns>
    private static List<string> ReadSources()
    {
        var root = FindRepositoryRoot();
        var result = new List<string>();
        foreach (var project in new[] { "DemaConsulting.ReviewCenter.Core", "DemaConsulting.ReviewCenter" })
        {
            var folder = Path.Combine(root, "src", project);
            foreach (var file in Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(folder, file);
                var parts = relative.Split(Path.DirectorySeparatorChar);
                if (parts.Any(part => part is "bin" or "obj" or "generated") || Path.GetFileName(file) == "AuditActions.cs")
                {
                    continue;
                }

                result.Add(File.ReadAllText(file));
            }
        }

        Assert.NotEmpty(result);
        return result;
    }

    /// <summary>
    ///     Finds the repository root from the folder of the test binaries.
    /// </summary>
    /// <returns>The full path of the folder that holds the solution file.</returns>
    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DemaConsulting.ReviewCenter.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("The repository root was not found above the test binaries.");
    }

    /// <summary>
    ///     Matches the first string literal after <c>AuditDraft.By(</c> or <c>new AuditDraft(</c>: the action.
    /// </summary>
    /// <returns>The compiled expression.</returns>
    [GeneratedRegex("(?:AuditDraft\\.By\\(|new AuditDraft\\()(?:[^\"]|\\n)*?\"([a-z]+\\.[a-z]+(?:-[a-z]+)*)\"")]
    private static partial Regex DraftActionRegex();

    /// <summary>
    ///     Matches a constant or field whose name ends in <c>Action</c> and that is a literal action code.
    /// </summary>
    /// <returns>The compiled expression.</returns>
    [GeneratedRegex("Action\\w*\\s*=\\s*\"([a-z]+\\.[a-z]+(?:-[a-z]+)*)\"")]
    private static partial Regex ActionConstantRegex();
}
