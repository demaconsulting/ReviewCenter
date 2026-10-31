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
///     Unit tests for <see cref="InboxRules"/>: the pure rules of the inbox, without any store.
/// </summary>
public class InboxRulesTests
{
    /// <summary>
    ///     Gets the cancellation token of the running test.
    /// </summary>
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    ///     Test that the slot state is the one the signing service reports for a slot that was never signed, a
    ///     slot with a current signature and a slot whose signature is out of date.
    /// </summary>
    [Fact]
    public async Task InboxRules_SlotState_MatchesSigningServiceApproval()
    {
        // Arrange: three slots: ann signed, bob did not, cyd signed and then an answer changed (out of date for ann
        // and cyd, which share the changed record), so bob stays untouched
        var world = await InboxTestWorld.CreateAsync();
        await world.CreateReviewAsync("r1");
        await world.AnswerAllAsync("r1");
        var annSlot = await world.AddSlotAsync("r1", "ann");
        var bobSlot = await world.AddSlotAsync("r1", "bob");
        var cySlot = await world.AddSlotAsync("r1", "cyd");
        await world.SignAsync("r1", annSlot.Id, "ann");
        var states = new List<(string Moment, IReadOnlyDictionary<int, SlotSignatureState> Rules)>
        {
            ("signed", await RulesAsync(world))
        };
        await world.SignAsync("r1", cySlot.Id, "cyd");
        var item = DefaultChecklist.Current.Sections[0].Items[0];
        await world.Checklists.SetAnswerAsync(
            world.ProjectId, "r1", item.Id, ChecklistOutcome.NotApplicable, "Not relevant", InboxTestWorld.ActorOf("eve"), Ct);
        states.Add(("out of date", await RulesAsync(world)));

        // Act: compare with the signing service at both moments
        var approval = await world.Signing.GetApprovalAsync(world.ProjectId, "r1", Ct);
        var outOfDate = states[1].Rules;

        // Assert: the rules agree with the service now (out of date for the signers, not signed for bob) ...
        Assert.Equal(
            approval!.Slots.ToDictionary(slot => slot.SlotId, slot => slot.State),
            outOfDate.ToDictionary(pair => pair.Key, pair => pair.Value));
        Assert.Equal(SlotSignatureState.OutOfDate, outOfDate[annSlot.Id]);
        Assert.Equal(SlotSignatureState.NotSigned, outOfDate[bobSlot.Id]);
        Assert.Equal(SlotSignatureState.OutOfDate, outOfDate[cySlot.Id]);

        // Assert: ... and at the earlier moment, when ann had a current signature
        Assert.Equal(SlotSignatureState.Signed, states[0].Rules[annSlot.Id]);
        Assert.Equal(SlotSignatureState.NotSigned, states[0].Rules[bobSlot.Id]);
    }

    /// <summary>
    ///     Test that the user names are compared without regard to case, and that nothing equals a missing name.
    /// </summary>
    [Fact]
    public void InboxRules_SameUser_IgnoresCase()
    {
        // Arrange, Act, Assert
        Assert.True(InboxRules.SameUser("Ann", "ann"));
        Assert.False(InboxRules.SameUser("ann", "bob"));
        Assert.False(InboxRules.SameUser(null, "ann"));
    }

    /// <summary>
    ///     Test that a user is assigned to a slot as primary and as alternate, and not otherwise.
    /// </summary>
    [Fact]
    public void InboxRules_IsAssigned_PrimaryAndAlternate_AreAssigned()
    {
        // Arrange
        var slot = new ReviewerSlot { Id = 1, ProjectId = 1, ReviewId = "r1", PrimaryUserId = 7, AlternateUserId = 8 };

        // Act, Assert
        Assert.True(InboxRules.IsAssigned(slot, 7));
        Assert.True(InboxRules.IsAssigned(slot, 8));
        Assert.False(InboxRules.IsAssigned(slot, 9));
    }

    /// <summary>
    ///     Test that a comment is unread only when it is by someone else and newer than the last seen one.
    /// </summary>
    [Fact]
    public void InboxRules_IsUnread_OtherAndNewer_IsTheOnlyUnread()
    {
        // Arrange
        var byBob = Comment(5, "bob");
        var byAnn = Comment(6, "ann");

        // Act, Assert
        Assert.True(InboxRules.IsUnread(byBob, "ann", 4));
        Assert.False(InboxRules.IsUnread(byBob, "ann", 5));
        Assert.False(InboxRules.IsUnread(byBob, "ann", 9));
        Assert.False(InboxRules.IsUnread(byAnn, "ann", 0));
        Assert.False(InboxRules.IsUnread(byAnn, "ANN", 0));
    }

    /// <summary>
    ///     Test that the unread count and the highest comment look at the comments of all threads, and that no
    ///     comment gives zero.
    /// </summary>
    [Fact]
    public void InboxRules_CountUnreadAndMaxCommentId_AllThreads_AreCounted()
    {
        // Arrange
        var threads = new[]
        {
            Thread(1, ReviewThreadState.Open, Comment(1, "ann"), Comment(4, "bob")),
            Thread(2, ReviewThreadState.Resolved, Comment(7, "bob"), Comment(9, "ann"))
        };

        // Act, Assert
        Assert.Equal(9, InboxRules.MaxCommentId(threads));
        Assert.Equal(2, InboxRules.CountUnread(threads, "ann", 0));
        Assert.Equal(1, InboxRules.CountUnread(threads, "ann", 4));
        Assert.Equal(0, InboxRules.MaxCommentId([]));
        Assert.Equal(0, InboxRules.CountUnread([], "ann", 0));
    }

    /// <summary>
    ///     Test which threads wait for the user: open ones with the latest comment by someone else in which the user
    ///     wrote or that belong to a review the user authored.
    /// </summary>
    [Fact]
    public void InboxRules_WaitingThreads_OpenLatestByOther_AreWaiting()
    {
        // Arrange: thread 1 answered by bob, thread 2 ended by ann, thread 3 resolved, thread 4 not ann's
        var threads = new[]
        {
            Thread(1, ReviewThreadState.Open, Comment(1, "ann"), Comment(2, "bob")),
            Thread(2, ReviewThreadState.Open, Comment(3, "bob"), Comment(4, "ann")),
            Thread(3, ReviewThreadState.Resolved, Comment(5, "ann"), Comment(6, "bob")),
            Thread(4, ReviewThreadState.Open, Comment(7, "bob"))
        };

        // Act
        var forParticipant = InboxRules.WaitingThreads(threads, "ann", false);
        var forAuthor = InboxRules.WaitingThreads(threads, "ann", true);

        // Assert
        Assert.Equal([1], forParticipant.Select(thread => thread.Id));
        Assert.Equal([1, 4], forAuthor.Select(thread => thread.Id));
    }

    /// <summary>
    ///     Test that the completing signature is the latest one of the current digest, and none without one.
    /// </summary>
    [Fact]
    public void InboxRules_CompletingSignature_LatestOfCurrentDigest_IsFound()
    {
        // Arrange
        var start = InboxTestWorld.Start;
        var signatures = new[]
        {
            Signature(1, "old", start.AddMinutes(9)),
            Signature(2, "now", start.AddMinutes(1)),
            Signature(3, "now", start.AddMinutes(2))
        };

        // Act
        var completing = InboxRules.CompletingSignature(signatures, "now");

        // Assert
        Assert.Equal(3, completing!.Id);
        Assert.Null(InboxRules.CompletingSignature(signatures, "other"));
        Assert.Null(InboxRules.CompletingSignature([], "now"));
    }

    /// <summary>
    ///     Test that only the checks answered Fail in sections that apply are found.
    /// </summary>
    [Fact]
    public async Task InboxRules_FailedAnswers_OnlyFailInApplicableSections_AreFound()
    {
        // Arrange: one Fail and one Pass in the first section
        var world = await InboxTestWorld.CreateAsync();
        await world.CreateReviewAsync("r1");
        await world.FailCheckAsync("r1", "bob", 0);
        var passItem = DefaultChecklist.Current.Sections[0].Items[1];
        await world.Checklists.SetAnswerAsync(
            world.ProjectId, "r1", passItem.Id, ChecklistOutcome.Pass, null, InboxTestWorld.ActorOf("bob"), Ct);

        // Act
        var record = (await world.Store.GetRecordAsync(world.ProjectId, "r1", Ct))!;
        var failed = InboxRules.FailedAnswers(record);

        // Assert
        var answer = Assert.Single(failed);
        Assert.Equal(DefaultChecklist.Current.Sections[0].Items[0].Id, answer.ItemId);
    }

    /// <summary>
    ///     Reads the slot states by the rules for the current record of <c>r1</c>.
    /// </summary>
    /// <param name="world">The world.</param>
    /// <returns>The state of each slot by slot identity.</returns>
    private static async Task<IReadOnlyDictionary<int, SlotSignatureState>> RulesAsync(InboxTestWorld world)
    {
        var snapshot = (await world.Store.GetSnapshotAsync(world.ProjectId, "r1", Ct))!;
        var digest = RecordDigest.Compute(snapshot.Record);
        return snapshot.Slots.ToDictionary(
            slot => slot.Id, slot => InboxRules.SlotState(slot, snapshot.Signatures, digest));
    }

    /// <summary>
    ///     Builds a comment.
    /// </summary>
    /// <param name="id">The identity.</param>
    /// <param name="username">The user name of the author.</param>
    /// <returns>The comment.</returns>
    private static ReviewComment Comment(int id, string username)
    {
        return new ReviewComment { Id = id, Author = username, AuthorUsername = username, Text = "Text" };
    }

    /// <summary>
    ///     Builds a thread.
    /// </summary>
    /// <param name="id">The identity.</param>
    /// <param name="state">The state.</param>
    /// <param name="comments">The comments.</param>
    /// <returns>The thread.</returns>
    private static ReviewThread Thread(int id, ReviewThreadState state, params ReviewComment[] comments)
    {
        return new ReviewThread
        {
            Id = id,
            ProjectId = 1,
            ReviewId = "r1",
            FilePath = "a.txt",
            State = state,
            Comments = [.. comments]
        };
    }

    /// <summary>
    ///     Builds a signature.
    /// </summary>
    /// <param name="id">The identity.</param>
    /// <param name="digest">The record digest the signature covers.</param>
    /// <param name="signedAt">The time of the signature.</param>
    /// <returns>The signature.</returns>
    private static ReviewSignature Signature(int id, string digest, DateTimeOffset signedAt)
    {
        return new ReviewSignature { Id = id, SlotId = id, RecordDigest = digest, SignedAt = signedAt };
    }
}
