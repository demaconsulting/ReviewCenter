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
using DemaConsulting.ReviewCenter.Web;

namespace DemaConsulting.ReviewCenter.Tests.Web;

/// <summary>
///     Unit tests for <see cref="AuditFilterState"/>: reading the filter of an audit page from hostile query
///     values without ever throwing, and writing it back into the paging links.
/// </summary>
public class AuditFilterStateTests
{
    /// <summary>
    ///     Test that no query values give the default, unfiltered state and an empty query string.
    /// </summary>
    [Fact]
    public void AuditFilterState_FromQuery_NoValues_IsTheDefaultState()
    {
        // Arrange / Act
        var state = AuditFilterState.FromQuery(null, null, null, null, null, null, null);

        // Assert
        Assert.False(state.IsFiltered);
        Assert.False(state.Narrows);
        Assert.Equal(AuditService.DefaultPageSize, state.PageSize);
        Assert.Null(state.Message);
        Assert.Equal(string.Empty, state.ToQuery());
        Assert.Equal(AuditFilter.None, state.ToFilter(null));
    }

    /// <summary>
    ///     Test that good values are read, an exact action and a group are told apart, and the filter and the query
    ///     string carry them.
    /// </summary>
    [Fact]
    public void AuditFilterState_FromQuery_GoodValues_AreReadAndWrittenBack()
    {
        // Arrange / Act
        var exact = AuditFilterState.FromQuery(" Ann ", "review.created", "REV-1", "2025-01-02", "2025-02-03", "100", "7");
        var group = AuditFilterState.FromQuery(null, "review.", null, null, null, null, null);

        // Assert
        Assert.Equal("Ann", exact.Actor);
        Assert.Equal(new DateOnly(2025, 1, 2), exact.From);
        Assert.Equal(new DateOnly(2025, 2, 3), exact.To);
        Assert.Equal(100, exact.PageSize);
        Assert.Equal(7, exact.ProjectId);
        Assert.Equal(
            new AuditFilter(7, "REV-1", null, "Ann", "review.created", null, new DateOnly(2025, 1, 2), new DateOnly(2025, 2, 3)),
            exact.ToFilter(null));
        Assert.Equal(
            "?project=7&actor=Ann&action=review.created&review=REV-1&from=2025-01-02&to=2025-02-03&size=100&before=9",
            exact.ToQuery(before: 9));
        Assert.Equal("?action=review.&after=4", group.ToQuery(after: 4));
        Assert.Equal(new AuditFilter(ActionPrefix: "review."), group.ToFilter(null));
    }

    /// <summary>
    ///     Test that a project page cannot be pointed at another project through the query string.
    /// </summary>
    [Fact]
    public void AuditFilterState_ToFilter_ProjectPageProject_WinsOverTheQueryProject()
    {
        // Arrange
        var state = AuditFilterState.FromQuery(null, null, null, null, null, null, "7");

        // Act
        var filter = state.ToFilter(3);

        // Assert
        Assert.Equal(3, filter.ProjectId);
    }

    /// <summary>
    ///     Test that values that are not usable are dropped instead of raising: unknown actions, bad dates, bad
    ///     sizes, bad projects and huge texts.
    /// </summary>
    [Theory]
    [InlineData("nope.nothing", "2025-13-40", "7", "-5")]
    [InlineData("<script>alert(1)</script>", "tomorrow", "-100", "0")]
    [InlineData("review", "01/02/2025", "0", "abc")]
    [InlineData("", "2025-1-2", "75", "99999999999999999999")]
    public void AuditFilterState_FromQuery_BadValues_FallBackToDefaults(string action, string date, string size, string project)
    {
        // Arrange / Act
        var state = AuditFilterState.FromQuery(null, action, null, date, date, size, project);

        // Assert
        Assert.Equal(string.Empty, state.Action);
        Assert.Null(state.From);
        Assert.Null(state.To);
        Assert.Equal(AuditService.DefaultPageSize, state.PageSize);
        Assert.Null(state.ProjectId);
        Assert.False(state.IsFiltered);
    }

    /// <summary>
    ///     Test that a huge text is cut to the width of the field it is matched with, or ignored when absurd, and
    ///     that the resulting filter is valid.
    /// </summary>
    [Fact]
    public void AuditFilterState_FromQuery_HugeTexts_AreBoundedAndTheFilterIsValid()
    {
        // Arrange
        var huge = new string('x', 100_000);

        // Act
        var state = AuditFilterState.FromQuery(huge, huge, huge, huge, huge, huge, huge);
        var filter = state.ToFilter(null);

        // Assert
        Assert.True(state.Actor.Length <= AuditText.MaxActorNameLength);
        Assert.True(state.Review.Length <= ReviewLimits.MaxReviewIdLength);
        Assert.Equal(filter, filter.Validated());
    }

    /// <summary>
    ///     Test that a start after the end drops both dates and says so, and that the page size alone counts as
    ///     filtered without narrowing anything.
    /// </summary>
    [Fact]
    public void AuditFilterState_FromQuery_ReversedDatesAndSize_DropDatesAndSizeIsNotNarrowing()
    {
        // Arrange / Act
        var reversed = AuditFilterState.FromQuery(null, null, null, "2025-03-05", "2025-03-04", null, null);
        var sized = AuditFilterState.FromQuery(null, null, null, null, null, "25", null);

        // Assert
        Assert.Null(reversed.From);
        Assert.Null(reversed.To);
        Assert.NotNull(reversed.Message);
        Assert.True(sized.IsFiltered);
        Assert.False(sized.Narrows);
        Assert.Equal("?size=25", sized.ToQuery());
    }

    /// <summary>
    ///     Test that values written into a query string are escaped so that they cannot add parameters or markup.
    /// </summary>
    [Fact]
    public void AuditFilterState_ToQuery_TextWithSpecialCharacters_IsEscaped()
    {
        // Arrange
        var state = AuditFilterState.FromQuery("a&b=c <i>%", null, "x y", null, null, null, null);

        // Act
        var query = state.ToQuery(before: 5);

        // Assert
        Assert.Equal("?actor=a%26b%3Dc%20%3Ci%3E%25&review=x%20y&before=5", query);
    }
}
