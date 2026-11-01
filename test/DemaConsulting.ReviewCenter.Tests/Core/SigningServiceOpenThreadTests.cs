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
///     Unit tests of the open-thread rule: a review is concluded only when every slot holds a current signature and
///     no thread is open, however the thread is made (<see cref="ReviewLock"/>), and the way
///     <see cref="SigningService"/> applies that rule.
/// </summary>
public class SigningServiceOpenThreadTests
{
    /// <summary>
    ///     The instant of every time of the arranged data.
    /// </summary>
    private static readonly DateTimeOffset _time = new(2025, 3, 4, 5, 6, 7, TimeSpan.Zero);

    /// <summary>
    ///     The reviewer who removes slots in the signed-review world.
    /// </summary>
    private static readonly Actor _rita = new("rita", "Rita");

    /// <summary>
    ///     Builds a thread with one comment.
    /// </summary>
    /// <param name="id">The identity of the thread.</param>
    /// <param name="state">The state of the thread.</param>
    /// <param name="included">Whether the comment is included in the report.</param>
    /// <returns>The thread.</returns>
    private static ReviewThread Thread(int id, ReviewThreadState state, bool included)
    {
        return new ReviewThread
        {
            Id = id,
            ProjectId = 1,
            ReviewId = "rev-1",
            FilePath = "a.txt",
            State = state,
            Created = _time,
            Comments =
            [
                new ReviewComment
                {
                    Id = id,
                    ThreadId = id,
                    Author = "Ann",
                    AuthorUsername = "ann",
                    Text = "text",
                    Created = _time,
                    IncludeInReport = included
                }
            ]
        };
    }

    /// <summary>
    ///     Builds a record with the given threads.
    /// </summary>
    /// <param name="threads">The threads.</param>
    /// <returns>The record.</returns>
    private static ReviewRecord Record(params ReviewThread[] threads)
    {
        return new ReviewRecord("abc", null, [], threads, ReviewSource.Upload, 0);
    }

    /// <summary>
    ///     Builds the given number of slots.
    /// </summary>
    /// <param name="count">The number of slots.</param>
    /// <returns>The slots, numbered from 1.</returns>
    private static IReadOnlyList<ReviewerSlot> Slots(int count)
    {
        return [.. Enumerable.Range(1, count).Select(id => new ReviewerSlot { Id = id, PrimaryUserId = id })];
    }

    /// <summary>
    ///     Builds a signature of a digest for the slots with the given identities.
    /// </summary>
    /// <param name="digest">The digest that is signed.</param>
    /// <param name="slotIds">The slots that hold the signature.</param>
    /// <returns>The signatures.</returns>
    private static IReadOnlyList<ReviewSignature> Signatures(string digest, params int[] slotIds)
    {
        return [.. slotIds.Select(id => new ReviewSignature { Id = id, SlotId = id, UserId = id, RecordDigest = digest })];
    }

    /// <summary>
    ///     Test that a review whose slots are all signed and that has no thread is concluded.
    /// </summary>
    [Fact]
    public void ReviewLock_IsConcluded_AllSignedNoThreads_IsTrue()
    {
        // Arrange
        var record = Record();

        // Act, Assert
        Assert.True(ReviewLock.IsConcluded(record, Slots(2), Signatures(RecordDigest.Compute(record), 1, 2)));
    }

    /// <summary>
    ///     Test that an open thread keeps a review open even when it is chat, which the digest does not cover.
    /// </summary>
    [Fact]
    public void ReviewLock_IsConcluded_AllSignedOpenChatThread_IsFalse()
    {
        // Arrange: the chat thread leaves the digest of the empty record alone
        var record = Record(Thread(1, ReviewThreadState.Open, false));
        Assert.Equal(RecordDigest.Compute(Record()), RecordDigest.Compute(record));

        // Act, Assert
        Assert.False(ReviewLock.IsConcluded(record, Slots(1), Signatures(RecordDigest.Compute(record), 1)));
    }

    /// <summary>
    ///     Test that a review whose threads are all resolved, chat or included, is concluded once every slot is signed.
    /// </summary>
    [Fact]
    public void ReviewLock_IsConcluded_AllSignedAllResolved_IsTrue()
    {
        // Arrange
        var record = Record(Thread(1, ReviewThreadState.Resolved, false), Thread(2, ReviewThreadState.Resolved, true));

        // Act, Assert
        Assert.True(ReviewLock.IsConcluded(record, Slots(1), Signatures(RecordDigest.Compute(record), 1)));
    }

    /// <summary>
    ///     Test that a review without slots is never concluded.
    /// </summary>
    [Fact]
    public void ReviewLock_IsConcluded_NoSlots_IsFalse()
    {
        // Arrange, Act, Assert
        Assert.False(ReviewLock.IsConcluded(Record(), [], []));
    }

    /// <summary>
    ///     Test that a review with one slot unsigned, or signed for another digest, is not concluded.
    /// </summary>
    [Fact]
    public void ReviewLock_IsConcluded_OneSlotUnsigned_IsFalse()
    {
        // Arrange
        var record = Record();
        var digest = RecordDigest.Compute(record);

        // Act, Assert
        Assert.False(ReviewLock.IsConcluded(record, Slots(2), Signatures(digest, 1)));
        Assert.False(ReviewLock.IsConcluded(record, Slots(2), [.. Signatures(digest, 1), .. Signatures("other", 2)]));
    }

    /// <summary>
    ///     Test that the rule is the same as the one the inbox applies: concluded exactly when every slot is signed and no
    ///     thread is waiting.
    /// </summary>
    /// <param name="allSigned">Whether every slot holds a signature of the current digest.</param>
    /// <param name="threadState">The state of the one thread, or <c>null</c> for no thread.</param>
    /// <param name="included">Whether the comment of the thread is included in the report.</param>
    [Theory]
    [InlineData(true, null, false)]
    [InlineData(true, ReviewThreadState.Open, false)]
    [InlineData(true, ReviewThreadState.Open, true)]
    [InlineData(true, ReviewThreadState.Resolved, false)]
    [InlineData(true, ReviewThreadState.Resolved, true)]
    [InlineData(false, null, false)]
    [InlineData(false, ReviewThreadState.Open, true)]
    [InlineData(false, ReviewThreadState.Resolved, true)]
    public void ReviewLock_IsConcluded_ParityWithInboxRules_ForAllStateCombinations(
        bool allSigned,
        ReviewThreadState? threadState,
        bool included)
    {
        // Arrange: two slots, one of them signed unless all are; the viewer is not the commenter and sees every thread
        var record = threadState is { } state ? Record(Thread(1, state, included)) : Record();
        var digest = RecordDigest.Compute(record);
        var slots = Slots(2);
        var signatures = allSigned ? Signatures(digest, 1, 2) : Signatures(digest, 1);

        // Act
        var concluded = ReviewLock.IsConcluded(record, slots, signatures);

        // Assert
        var everySlotSigned = slots.All(slot => InboxRules.SlotState(slot, signatures, digest) == SlotSignatureState.Signed);
        var waiting = InboxRules.WaitingThreads(record.Threads, "viewer", true).Count;
        Assert.Equal(everySlotSigned && waiting == 0, concluded);
    }

    /// <summary>
    ///     Test that a chat thread that is open blocks a slot from signing.
    /// </summary>
    [Fact]
    public async Task SigningService_SignAsync_OpenChatThread_ThrowsSigningNotAllowed()
    {
        // Arrange: slot 1 signed, then a chat thread is opened
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await SignedReviewWorld.CreateAsync();
        await world.SignAsync(1);
        await world.Threads.StartThreadAsync(
            world.ProjectId, SignedReviewWorld.ReviewId, new ThreadAnchor("a.txt"), TestActors.Bob, "Question", cancellationToken);
        var digest = (await world.ApprovalAsync()).Digest;

        // Act
        var ex = await Assert.ThrowsAsync<DomainRuleException>(() => world.Signing.SignAsync(
            new Actor("sam", "Sam"), world.ProjectId, SignedReviewWorld.ReviewId, world.Slots[1].Id, digest, "correct horse battery", cancellationToken));

        // Assert
        Assert.Equal(DomainRule.SigningNotAllowed, ex.Rule);
        Assert.Equal([world.Slots[0].Id], await world.SignedSlotIdsAsync());
    }

    /// <summary>
    ///     Test that removing the last unsigned slot while a thread is open leaves every slot signed but the review not
    ///     concluded, and that the approval counts the open thread.
    /// </summary>
    [Fact]
    public async Task SigningService_RemoveSlotAsync_LastUnsignedSlotWithOpenThread_AllSignedButNotConcluded()
    {
        // Arrange, Act
        var world = await OpenThreadAllSignedAsync();
        var approval = await world.ApprovalAsync();

        // Assert
        Assert.All(approval.Slots, slot => Assert.Equal(SlotSignatureState.Signed, slot.State));
        Assert.False(approval.IsApproved);
        Assert.Equal(1, approval.OpenThreads);
        Assert.Contains("1 open question to resolve", approval.Blockers);
    }

    /// <summary>
    ///     Test that resolving the last open thread concludes the review without any entry in the audit trail beyond
    ///     the one of the resolution, because the conclusion is derived and not stored.
    /// </summary>
    [Fact]
    public async Task SigningService_ResolveLastThreadAfterSlotRemoval_ConcludesReview()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await OpenThreadAllSignedAsync();
        var audits = world.Store.Audits.Count;
        var thread = Assert.Single(world.Store.Threads);

        // Act
        await world.Threads.ResolveAsync(
            world.ProjectId, SignedReviewWorld.ReviewId, thread.Id, TestActors.Ann, cancellationToken);

        // Assert
        Assert.True((await world.ApprovalAsync()).IsApproved);
        Assert.Equal(["thread.resolved"], world.Store.Audits.Skip(audits).Select(audit => audit.Action));
    }

    /// <summary>
    ///     Test that a chat thread opened after some slots signed keeps their signatures valid and blocks the others
    ///     until it is resolved.
    /// </summary>
    [Fact]
    public async Task SigningService_ChatThreadAfterPartialSigning_KeepsSignaturesValidAndBlocksOthers()
    {
        // Arrange: slots 1 and 2 signed, then a chat thread is opened
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await SignedReviewWorld.CreateAsync();
        await world.SignAsync(1);
        await world.SignAsync(2);
        var thread = await world.Threads.StartThreadAsync(
            world.ProjectId, SignedReviewWorld.ReviewId, new ThreadAnchor("a.txt"), TestActors.Bob, "Question", cancellationToken);

        // Act
        var blocked = await world.ApprovalAsync();
        await world.Threads.ResolveAsync(
            world.ProjectId, SignedReviewWorld.ReviewId, thread.Id, TestActors.Bob, cancellationToken);
        var resolved = await world.ApprovalAsync();
        await world.SignAsync(3);
        var concluded = await world.ApprovalAsync();

        // Assert
        Assert.Equal(
            [SlotSignatureState.Signed, SlotSignatureState.Signed, SlotSignatureState.NotSigned],
            blocked.Slots.Select(slot => slot.State));
        Assert.Contains("1 open question to resolve", blocked.Blockers);
        Assert.Empty(resolved.Blockers);
        Assert.Equal(blocked.Digest, resolved.Digest);
        Assert.True(concluded.IsApproved);
    }

    /// <summary>
    ///     Test that the approval of a review with every slot signed and a thread open is not approved and counts the
    ///     open thread.
    /// </summary>
    [Fact]
    public async Task SigningService_GetApprovalAsync_OpenThreadAllSigned_IsNotApprovedAndCountsOpenThread()
    {
        // Arrange, Act
        var world = await OpenThreadAllSignedAsync();
        var approval = await world.ApprovalAsync();
        var all = await world.Signing.GetApprovalsAsync(
            world.ProjectId, [SignedReviewWorld.ReviewId], TestContext.Current.CancellationToken);

        // Assert
        Assert.False(approval.IsApproved);
        Assert.Equal(1, approval.OpenThreads);
        Assert.False(all[SignedReviewWorld.ReviewId].IsApproved);
    }

    /// <summary>
    ///     Test that a review with every slot signed but a thread open is not locked, so it cannot be reopened.
    /// </summary>
    [Fact]
    public async Task SigningService_ReopenReviewAsync_AllSignedButThreadOpen_ThrowsReviewNotLocked()
    {
        // Arrange
        var world = await OpenThreadAllSignedAsync();

        // Act
        var ex = await Assert.ThrowsAsync<DomainRuleException>(() => world.Signing.ReopenReviewAsync(
            _rita, world.ProjectId, SignedReviewWorld.ReviewId, "Needs another look", TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(DomainRule.ReviewNotLocked, ex.Rule);
    }

    /// <summary>
    ///     Test that an open thread about a check blocks a slot from signing, like any open thread.
    /// </summary>
    [Fact]
    public async Task SigningService_SignAsync_OpenCheckThread_IsRefused()
    {
        // Arrange: slot 1 signed, then a thread about a check is opened
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await SignedReviewWorld.CreateAsync();
        await world.SignAsync(1);
        await world.Threads.StartThreadAsync(
            world.ProjectId, SignedReviewWorld.ReviewId, ThreadAnchor.ForCheck(SignedReviewWorld.FirstItemId), TestActors.Bob, "Question", cancellationToken);
        var digest = (await world.ApprovalAsync()).Digest;

        // Act
        var ex = await Assert.ThrowsAsync<DomainRuleException>(() => world.Signing.SignAsync(
            new Actor("sam", "Sam"), world.ProjectId, SignedReviewWorld.ReviewId, world.Slots[1].Id, digest, "correct horse battery", cancellationToken));

        // Assert
        Assert.Equal(DomainRule.SigningNotAllowed, ex.Rule);
        Assert.Equal([world.Slots[0].Id], await world.SignedSlotIdsAsync());
    }

    /// <summary>
    ///     Test that a resolved thread about a check does not block signing and that resolving the last open one frees it.
    /// </summary>
    [Fact]
    public async Task SigningService_SignAsync_ResolvedCheckThread_IsAllowed()
    {
        // Arrange: a thread about a check is opened and resolved
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await SignedReviewWorld.CreateAsync();
        var thread = await world.Threads.StartThreadAsync(
            world.ProjectId, SignedReviewWorld.ReviewId, ThreadAnchor.ForCheck(SignedReviewWorld.FirstItemId), TestActors.Bob, "Question", cancellationToken);
        await world.Threads.ResolveAsync(world.ProjectId, SignedReviewWorld.ReviewId, thread.Id, TestActors.Bob, cancellationToken);

        // Act
        await world.SignAsync(1);
        await world.SignAsync(2);
        await world.SignAsync(3);

        // Assert
        Assert.True((await world.ApprovalAsync()).IsApproved);
    }

    /// <summary>
    ///     Test that a review with every slot signed and a thread about a check open is not concluded, so the
    ///     approval counts the thread and names it as a blocker, and that resolving it concludes the review.
    /// </summary>
    [Fact]
    public async Task SigningService_GetApprovalAsync_OpenCheckThread_BlocksConclusion()
    {
        // Arrange: slots 1 and 2 signed, a thread about a check opened, the unsigned slot 3 removed
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await SignedReviewWorld.CreateAsync();
        await world.SignAsync(1);
        await world.SignAsync(2);
        var thread = await world.Threads.StartThreadAsync(
            world.ProjectId, SignedReviewWorld.ReviewId, ThreadAnchor.ForCheck(SignedReviewWorld.FirstItemId), TestActors.Ann, "Question", cancellationToken);
        await world.Signing.RemoveSlotAsync(_rita, world.ProjectId, SignedReviewWorld.ReviewId, world.Slots[2].Id, cancellationToken);

        // Act
        var blocked = await world.ApprovalAsync();
        await world.Threads.ResolveAsync(world.ProjectId, SignedReviewWorld.ReviewId, thread.Id, TestActors.Ann, cancellationToken);
        var resolved = await world.ApprovalAsync();

        // Assert
        Assert.All(blocked.Slots, slot => Assert.Equal(SlotSignatureState.Signed, slot.State));
        Assert.False(blocked.IsApproved);
        Assert.Equal(1, blocked.OpenThreads);
        Assert.Contains("1 open question to resolve", blocked.Blockers);
        Assert.True(resolved.IsApproved);
    }

    /// <summary>
    ///     Arranges the signed-review world with slots 1 and 2 signed, a chat thread open and the unsigned slot 3
    ///     removed, so that every remaining slot is signed.
    /// </summary>
    /// <returns>The world.</returns>
    private static async Task<SignedReviewWorld> OpenThreadAllSignedAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await SignedReviewWorld.CreateAsync();
        await world.SignAsync(1);
        await world.SignAsync(2);
        await world.Threads.StartThreadAsync(
            world.ProjectId, SignedReviewWorld.ReviewId, new ThreadAnchor("a.txt"), TestActors.Ann, "Question", cancellationToken);
        await world.Signing.RemoveSlotAsync(_rita, world.ProjectId, SignedReviewWorld.ReviewId, world.Slots[2].Id, cancellationToken);
        return world;
    }
}
