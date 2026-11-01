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
///     Unit tests of <see cref="AuditMasking"/>, which hides what an audit entry would give away about the answers of
///     the other reviewers.
/// </summary>
public class AuditMaskingTests
{
    /// <summary>
    ///     Creates an audit entry.
    /// </summary>
    /// <param name="action">The action code.</param>
    /// <param name="detail">The detail.</param>
    /// <param name="actorUserId">The identity of the actor, or <c>null</c>.</param>
    /// <returns>The entry.</returns>
    private static AuditEntry Entry(string action, string detail, int? actorUserId)
    {
        return new AuditEntry
        {
            Sequence = 1,
            TimestampUtc = new DateTimeOffset(2025, 3, 4, 5, 6, 7, TimeSpan.Zero),
            ActorUserId = actorUserId,
            ActorName = "Bob",
            Action = action,
            ProjectId = 1,
            ReviewId = "rev-1",
            Detail = detail,
            PreviousHash = "previous",
            Hash = "hash"
        };
    }

    /// <summary>
    ///     Test that the answer of another person is hidden but who acted, when and the action stay.
    /// </summary>
    [Fact]
    public void AuditMasking_ForViewer_OtherAnswer_HidesDetailKeepsActorAndHash()
    {
        // Arrange
        var entry = Entry("checklist.answered", "item abc: Fail; justification sha256 1234", 2);

        // Act
        var masked = AuditMasking.ForViewer([entry], false, 1).Single();

        // Assert
        Assert.Equal(AuditMasking.HiddenAnswerDetail, masked.Detail);
        Assert.DoesNotContain("Fail", masked.Detail, StringComparison.Ordinal);
        Assert.Equal("Bob", masked.ActorName);
        Assert.Equal(entry.Action, masked.Action);
        Assert.Equal(entry.TimestampUtc, masked.TimestampUtc);
        Assert.Equal(entry.Hash, masked.Hash);
        Assert.Equal(entry.PreviousHash, masked.PreviousHash);
    }

    /// <summary>
    ///     Test that what a person did themselves is never hidden from them.
    /// </summary>
    [Fact]
    public void AuditMasking_ForViewer_OwnAnswer_StaysVisible()
    {
        // Arrange
        var entry = Entry("checklist.answered", "item abc: Pass", 7);

        // Act
        var result = AuditMasking.ForViewer([entry], false, 7).Single();

        // Assert
        Assert.Equal("item abc: Pass", result.Detail);
    }

    /// <summary>
    ///     Test that applicability changes and the start of a discussion about a check are hidden, while a discussion
    ///     about a file and other actions are not.
    /// </summary>
    [Fact]
    public void AuditMasking_ForViewer_ApplicabilityAndCheckDiscussion_HiddenOthersKept()
    {
        // Arrange
        var entries = new List<AuditEntry>
        {
            Entry("checklist.applicability-changed", "section s1: not applicable; reason sha256 12", 2),
            Entry("thread.started", "thread 4 on check abc; comment 9", 2),
            Entry("thread.started", "thread 5 on a.txt; comment 10", 2),
            Entry("ballot.submitted", "slot 1", 2)
        };

        // Act
        var result = AuditMasking.ForViewer(entries, false, 1);

        // Assert
        Assert.Equal(AuditMasking.HiddenAnswerDetail, result[0].Detail);
        Assert.Equal(AuditMasking.HiddenDiscussionDetail, result[1].Detail);
        Assert.Equal("thread 5 on a.txt; comment 10", result[2].Detail);
        Assert.Equal("slot 1", result[3].Detail);
    }

    /// <summary>
    ///     Test that every later entry of a discussion about a check is hidden for somebody else, whichever kind of
    ///     entry it is, while the same entries of a discussion about a file stay.
    /// </summary>
    /// <param name="action">The action of the later entry.</param>
    /// <param name="detail">The stored detail of the entry.</param>
    [Theory]
    [InlineData("thread.replied", "thread 4, comment 11, text sha256 abcd; in report")]
    [InlineData("thread.resolved", "thread 4, resolved as finding; note sha256 abcd")]
    [InlineData("thread.reopened", "thread 4")]
    [InlineData("thread.kind-changed", "thread 4: Question -> Finding")]
    [InlineData("thread.comment-report-changed", "thread 4, comment 11: included in report")]
    public void AuditMasking_ForViewer_LaterEntryOfCheckThread_IsHiddenButFileThreadStays(string action, string detail)
    {
        // Arrange: thread 4 is about a check, thread 5 (same detail apart from the number) about a file
        var entries = new List<AuditEntry>
        {
            Entry("thread.started", "thread 4 on check abc; comment 9", 2),
            Entry("thread.started", "thread 5 on a.txt; comment 10", 2),
            Entry(action, detail, 2),
            Entry(action, detail.Replace("thread 4", "thread 5", StringComparison.Ordinal), 2)
        };

        // Act
        var result = AuditMasking.ForViewer(entries, false, 1);

        // Assert
        Assert.Equal(AuditMasking.HiddenDiscussionActivityDetail, result[2].Detail);
        Assert.Equal(action, result[2].Action);
        Assert.Equal("Bob", result[2].ActorName);
        Assert.Equal(detail.Replace("thread 4", "thread 5", StringComparison.Ordinal), result[3].Detail);
    }

    /// <summary>
    ///     Test that a later entry whose thread start is not among the entries is hidden, because the thread may be
    ///     about a check (fail closed), and that an entry in another review with the same thread number is not
    ///     mistaken for the thread of a file.
    /// </summary>
    [Fact]
    public void AuditMasking_ForViewer_ThreadOfUnknownStart_IsHidden()
    {
        // Arrange
        var other = Entry("thread.started", "thread 4 on a.txt; comment 9", 2);
        var sameNumberOtherReview = new AuditEntry
        {
            Sequence = other.Sequence,
            TimestampUtc = other.TimestampUtc,
            ActorUserId = 2,
            ActorName = "Bob",
            Action = "thread.replied",
            ProjectId = 1,
            ReviewId = "rev-2",
            Detail = "thread 4, comment 11, text sha256 abcd",
            PreviousHash = "previous",
            Hash = "hash"
        };
        var noThread = Entry("thread.replied", "unexpected detail", 2);

        // Act
        var result = AuditMasking.ForViewer([other, sameNumberOtherReview, noThread], false, 1);

        // Assert
        Assert.Equal("thread 4 on a.txt; comment 9", result[0].Detail);
        Assert.Equal(AuditMasking.HiddenDiscussionActivityDetail, result[1].Detail);
        Assert.Equal(AuditMasking.HiddenDiscussionActivityDetail, result[2].Detail);
    }

    /// <summary>
    ///     Test that the later entries a viewer made themselves in a discussion about a check stay visible to them.
    /// </summary>
    [Fact]
    public void AuditMasking_ForViewer_OwnEntryOfCheckThread_StaysVisible()
    {
        // Arrange
        var entries = new List<AuditEntry>
        {
            Entry("thread.started", "thread 4 on check abc; comment 9", 2),
            Entry("thread.replied", "thread 4, comment 11, text sha256 abcd", 1)
        };

        // Act
        var result = AuditMasking.ForViewer(entries, false, 1);

        // Assert
        Assert.Equal("thread 4, comment 11, text sha256 abcd", result[1].Detail);
    }

    /// <summary>
    ///     Test that a viewer who may see the others (a lead, an administrator, an Auditor) gets the entries unchanged.
    /// </summary>
    [Fact]
    public void AuditMasking_ForViewer_CanSeeOthers_ReturnsEntriesUnchanged()
    {
        // Arrange
        var entries = new List<AuditEntry> { Entry("checklist.answered", "item abc: Fail", 2) };

        // Act
        var result = AuditMasking.ForViewer(entries, true, 1);

        // Assert
        Assert.Same(entries, result);
        Assert.Equal("item abc: Fail", result[0].Detail);
    }

    /// <summary>
    ///     Test that a viewer who is not identified (no user id) sees the answers of everybody hidden.
    /// </summary>
    [Fact]
    public void AuditMasking_ForViewer_UnknownViewer_HidesEveryAnswer()
    {
        // Arrange
        var entry = Entry("checklist.answered", "item abc: Pass", null);

        // Act
        var result = AuditMasking.ForViewer([entry], false, null).Single();

        // Assert
        Assert.Equal(AuditMasking.HiddenAnswerDetail, result.Detail);
    }

    /// <summary>
    ///     Test that a null list is refused.
    /// </summary>
    [Fact]
    public void AuditMasking_ForViewer_NullEntries_Throws()
    {
        // Act and Assert
        Assert.Throws<ArgumentNullException>(() => AuditMasking.ForViewer(null!, false, 1));
    }
}
