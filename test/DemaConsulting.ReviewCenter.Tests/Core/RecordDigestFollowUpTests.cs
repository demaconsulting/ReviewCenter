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
///     Unit tests of the conditional lines of the record digest that belong to a follow-up review: the
///     <c>follows</c> line and the <c>carried</c> suffix of an answer, a not-applicable section and a thread.
/// </summary>
public class RecordDigestFollowUpTests
{
    /// <summary>
    ///     The instant used for every time of the records.
    /// </summary>
    private static readonly DateTimeOffset _time = new(2025, 3, 4, 5, 6, 7, TimeSpan.Zero);

    /// <summary>
    ///     Builds a record with one thread, optionally marked as carried from a review, and optionally following one.
    /// </summary>
    /// <remarks>
    ///     The carried answers and section states of a follow-up are baseline rows that belong to no ballot, so they are
    ///     not part of the record the digest covers; only the thread marker is.
    /// </remarks>
    /// <param name="follows">The review the record follows, or <c>null</c>.</param>
    /// <param name="threadFrom">The review the thread was carried from, or <c>null</c>.</param>
    /// <returns>The record.</returns>
    private static ReviewRecord Record(string? follows = null, string? threadFrom = null)
    {
        var thread = new ReviewThread
        {
            Id = 1,
            ProjectId = 1,
            ReviewId = "rev-1",
            FilePath = "a.txt",
            StartLine = 1,
            EndLine = 2,
            State = ReviewThreadState.Open,
            Created = _time,
            CarriedFromReviewId = threadFrom,
            CarriedFromThreadId = threadFrom is null ? null : 9,
            Comments =
            [
                new ReviewComment
                {
                    Id = 10,
                    ThreadId = 1,
                    Author = "Ann",
                    AuthorUsername = "ann",
                    Text = "Please fix",
                    Created = _time,
                    IncludeInReport = true
                }
            ]
        };
        return new ReviewRecord("abc", null, [], [thread], ReviewSource.Upload, 0, follows);
    }

    /// <summary>
    ///     Test that a record that does not follow another review has the digests it had before follow-up reviews existed:
    ///     the known digest of the smallest record is unchanged, and no follow-up text is written for any record
    ///     without a marker.
    /// </summary>
    [Fact]
    public void RecordDigest_Compute_NoFollowUp_EqualsLegacyDigest()
    {
        // Arrange: the smallest known record with and without the explicit null, and a full record without markers
        var legacy = new ReviewRecord("abc", null, [], [], ReviewSource.Upload);
        var explicitNone = legacy with { FollowsReviewId = null };
        var full = Record();

        // Act
        var canonical = RecordDigest.Canonicalize(full);

        // Assert: the digest is the pinned v2 value, and neither text mentions a follow-up
        Assert.Equal("4c93e63f9251787ecc5a267ee3d3f28922757b299294add1709bf8a401fad5cd", RecordDigest.Compute(legacy));
        Assert.Equal(RecordDigest.Compute(legacy), RecordDigest.Compute(explicitNone));
        Assert.StartsWith("ReviewCenter record v2\n", canonical, StringComparison.Ordinal);
        Assert.DoesNotContain("follows", canonical, StringComparison.Ordinal);
        Assert.DoesNotContain("carried", canonical, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that the follow-up lines have exactly the documented shape: the <c>follows</c> line straight after the
    ///     source block and the <c>carried</c> suffix at the end of the thread line.
    /// </summary>
    [Fact]
    public void RecordDigest_Canonicalize_FollowUpRecord_MatchesDocumentedFormat()
    {
        // Arrange: a record that follows rev-0 and carries its thread
        var record = Record("rev-0", "rev-0");
        var ticks = _time.UtcTicks;

        // Act
        var text = RecordDigest.Canonicalize(record);

        // Assert
        var expected = string.Join(
            '\n',
            "ReviewCenter record v2",
            "fingerprint 3:abc",
            "source upload",
            "follows 5:rev-0",
            "checklist -",
            "checks",
            "sections",
            "outcome none",
            "report",
            "thread file 5:a.txt 4:Head 1 2 carried 5:rev-0",
            $"claim 3:Ann 3:ann 10:Please fix {ticks}");
        Assert.Equal(expected, text);
    }

    /// <summary>
    ///     Test that each marker changes the digest: the followed review and the review a thread was carried from, so a
    ///     carried thread and a thread started in the review never share a digest.
    /// </summary>
    /// <param name="change">Which marker differs from the baseline.</param>
    [Theory]
    [InlineData("follows")]
    [InlineData("thread")]
    public void RecordDigest_Compute_FollowUpMarkerChange_ChangesDigest(string change)
    {
        // Arrange: a baseline that follows rev-0 and the same record with one marker different or absent
        var baseline = Record("rev-0", "rev-0");
        var changed = change == "follows" ? Record("rev-9", "rev-0") : Record("rev-0", null);

        // Act, Assert: the digests differ, and a record that follows nothing differs from the baseline too
        Assert.NotEqual(RecordDigest.Compute(baseline), RecordDigest.Compute(changed));
        Assert.NotEqual(RecordDigest.Compute(baseline), RecordDigest.Compute(Record()));
    }

    /// <summary>
    ///     Test that the identity of the thread in the earlier review is not covered by the digest (only the review it
    ///     was carried from), because the identity of a thread never matters to the digest.
    /// </summary>
    [Fact]
    public void RecordDigest_Compute_CarriedFromThreadId_NotCovered()
    {
        // Arrange: two records whose carried thread came from different thread identities
        var one = Record("rev-0", "rev-0");
        var other = one with
        {
            Threads = [new ReviewThread
            {
                Id = 1,
                ProjectId = 1,
                ReviewId = "rev-1",
                FilePath = "a.txt",
                StartLine = 1,
                EndLine = 2,
                State = ReviewThreadState.Open,
                Created = _time,
                CarriedFromReviewId = "rev-0",
                CarriedFromThreadId = 42,
                Comments = one.Threads[0].Comments
            }]
        };

        // Act, Assert
        Assert.Equal(RecordDigest.Compute(one), RecordDigest.Compute(other));
    }
}
