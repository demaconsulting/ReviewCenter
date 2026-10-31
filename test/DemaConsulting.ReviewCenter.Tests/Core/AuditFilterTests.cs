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
///     Tests for <see cref="AuditFilter"/>: the normalization and validation of a filter and the instants of its
///     date range.
/// </summary>
public class AuditFilterTests
{
    /// <summary>
    ///     Test that validation trims every text and turns an empty text into <c>null</c>.
    /// </summary>
    [Fact]
    public void AuditFilter_Validated_TextsAreTrimmedAndEmptyBecomesNull()
    {
        // Arrange
        var filter = new AuditFilter(
            ProjectId: 3,
            ReviewId: "  rev-1  ",
            ActorText: "   ",
            Action: " review.created ",
            ActionPrefix: string.Empty);

        // Act
        var valid = filter.Validated();

        // Assert
        Assert.Equal(new AuditFilter(ProjectId: 3, ReviewId: "rev-1", Action: "review.created"), valid);
        Assert.Null(valid.ActorText);
        Assert.Null(valid.ActionPrefix);
    }

    /// <summary>
    ///     Test that the filter that matches everything stays so.
    /// </summary>
    [Fact]
    public void AuditFilter_None_ValidatesToItself()
    {
        // Arrange / Act
        var valid = AuditFilter.None.Validated();

        // Assert
        Assert.Equal(AuditFilter.None, valid);
        Assert.Null(valid.FromUtc);
        Assert.Null(valid.ToExclusiveUtc);
    }

    /// <summary>
    ///     Test that a text longer than the field it is compared with is refused with the invalid-filter rule.
    /// </summary>
    [Fact]
    public void AuditFilter_Validated_TextTooLong_IsRefused()
    {
        // Arrange
        var filters = new[]
        {
            new AuditFilter(ReviewId: new string('r', ReviewLimits.MaxReviewIdLength + 1)),
            new AuditFilter(ActorText: new string('a', AuditText.MaxActorNameLength + 1)),
            new AuditFilter(Action: new string('x', AuditText.MaxActionLength + 1)),
            new AuditFilter(ActionPrefix: new string('x', AuditText.MaxActionLength + 1))
        };

        // Act / Assert
        foreach (var filter in filters)
        {
            var ex = Assert.Throws<DomainRuleException>(() => filter.Validated());
            Assert.Equal(DomainRule.AuditFilterInvalid, ex.Rule);
        }

        // The longest allowed texts are fine
        var longest = new AuditFilter(
            ReviewId: new string('r', ReviewLimits.MaxReviewIdLength),
            ActorText: new string('a', AuditText.MaxActorNameLength),
            Action: new string('x', AuditText.MaxActionLength));
        Assert.Equal(longest, longest.Validated());
    }

    /// <summary>
    ///     Test that a range whose start is after its end is refused, and that a one-day range is fine.
    /// </summary>
    [Fact]
    public void AuditFilter_Validated_ReversedRange_IsRefusedAndSingleDayAccepted()
    {
        // Arrange
        var reversed = new AuditFilter(From: new DateOnly(2025, 3, 5), To: new DateOnly(2025, 3, 4));
        var oneDay = new AuditFilter(From: new DateOnly(2025, 3, 4), To: new DateOnly(2025, 3, 4));

        // Act
        var ex = Assert.Throws<DomainRuleException>(() => reversed.Validated());

        // Assert
        Assert.Equal(DomainRule.AuditFilterInvalid, ex.Rule);
        Assert.Equal(oneDay, oneDay.Validated());
    }

    /// <summary>
    ///     Test the instants of the date range: the start of the From day, inclusive, and the start of the day after
    ///     To, exclusive, both in UTC; the last representable day has no upper bound.
    /// </summary>
    [Fact]
    public void AuditFilter_Range_StartOfFromDayAndStartOfDayAfterTo()
    {
        // Arrange
        var filter = new AuditFilter(From: new DateOnly(2025, 3, 4), To: new DateOnly(2025, 3, 4));
        var open = new AuditFilter(To: DateOnly.MaxValue);

        // Act / Assert
        Assert.Equal(new DateTimeOffset(2025, 3, 4, 0, 0, 0, TimeSpan.Zero), filter.FromUtc);
        Assert.Equal(new DateTimeOffset(2025, 3, 5, 0, 0, 0, TimeSpan.Zero), filter.ToExclusiveUtc);
        Assert.True(filter.FromUtc?.Offset == TimeSpan.Zero);
        Assert.Null(open.ToExclusiveUtc);
    }
}
