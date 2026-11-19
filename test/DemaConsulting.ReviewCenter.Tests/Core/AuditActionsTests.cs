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

namespace DemaConsulting.ReviewCenter.Tests.Core;

/// <summary>
///     Tests for <see cref="AuditActions"/>: the invariants of the vocabulary and its lookups. That the vocabulary
///     matches the actions the product sources record is a property of the source, checked by <c>lint.ps1</c>.
/// </summary>
public static class AuditActionsTests
{
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
        for (var i = 1; i < all.Count; i++)
        {
            Assert.True(string.CompareOrdinal(all[i - 1], all[i]) < 0, $"'{all[i]}' is not after '{all[i - 1]}'.");
        }

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
}
