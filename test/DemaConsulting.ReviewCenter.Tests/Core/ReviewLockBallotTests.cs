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
///     Unit tests of the ballot rules of <see cref="ReviewLock.IsConcluded"/>: a review is concluded only when every
///     slot has handed its ballot in, the ballots agree on approval and every slot holds a current signature.
/// </summary>
public class ReviewLockBallotTests
{
    /// <summary>
    ///     Stores a current signature for every slot of the world, straight into the store.
    /// </summary>
    /// <param name="world">The world.</param>
    /// <param name="outcome">The outcome the signatures attest; approved by default.</param>
    /// <returns>A task that completes when the signatures are stored.</returns>
    private static async Task StoreAllSignaturesAsync(SignedReviewWorld world, ReviewOutcome outcome = ReviewOutcome.Approved)
    {
        for (var slot = 1; slot <= 3; slot++)
        {
            await world.StoreSignatureAsync(slot, outcome);
        }
    }

    /// <summary>
    ///     Tells whether the review of the world is concluded as it is stored now.
    /// </summary>
    /// <param name="world">The world.</param>
    /// <returns>The answer of <see cref="ReviewLock.IsConcluded"/>.</returns>
    private static async Task<bool> IsConcludedAsync(SignedReviewWorld world)
    {
        var snapshot = (await world.Store.GetSnapshotAsync(
            world.ProjectId, SignedReviewWorld.ReviewId, TestContext.Current.CancellationToken))!;
        return ReviewLock.IsConcluded(snapshot.Record, snapshot.Slots, snapshot.Signatures);
    }

    /// <summary>
    ///     Test that a review is not concluded while a ballot has not been handed in, even if every slot is signed.
    /// </summary>
    [Fact]
    public async Task ReviewLock_IsConcluded_UnsubmittedBallot_False()
    {
        // Arrange: all three slots are signed, then tess's ballot is sent back to a draft straight in the store (the
        // agreed standing, and so the digest, is unchanged)
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await SignedReviewWorld.CreateAsync();
        await StoreAllSignaturesAsync(world);
        world.Store.IgnoreLockForBallots = true;
        var snapshot = (await world.Store.GetSnapshotAsync(world.ProjectId, SignedReviewWorld.ReviewId, cancellationToken))!;
        var tess = snapshot.Record.Ballots.Single(b => b.SlotId == world.Slots[2].Id);
        await world.Store.SetBallotAnswerAsync(tess.Answers[0], AuditTestExtensions.Draft(), null, cancellationToken);

        // Act and assert
        Assert.False(await IsConcludedAsync(world));
    }

    /// <summary>
    ///     Test that a review with every ballot handed in and agreeing is not concluded while a slot has no signature.
    /// </summary>
    [Fact]
    public async Task ReviewLock_IsConcluded_AgreedBallotsOneSlotUnsigned_False()
    {
        // Arrange: only two of the three slots are signed
        var world = await SignedReviewWorld.CreateAsync();
        await world.StoreSignatureAsync(1);
        await world.StoreSignatureAsync(2);

        // Act and assert
        Assert.False(await IsConcludedAsync(world));
    }

    /// <summary>
    ///     Test that a review with every ballot handed in, agreeing and signed is not concluded while a thread is open.
    /// </summary>
    [Fact]
    public async Task ReviewLock_IsConcluded_AgreedSignedBallotsOpenThread_False()
    {
        // Arrange: slots 1 and 2 sign, a thread is opened, and the unsigned slot 3 is removed, so that every remaining
        // slot is signed with its ballot handed in and agreeing
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await SignedReviewWorld.CreateAsync();
        await world.SignAsync(1);
        await world.SignAsync(2);
        await world.Threads.StartThreadAsync(
            world.ProjectId, SignedReviewWorld.ReviewId, new ThreadAnchor("a.txt"), SignedReviewWorld.ActorOf("rita"), "Please look", cancellationToken);
        await world.Signing.RemoveSlotAsync(
            SignedReviewWorld.ActorOf("rita"), world.ProjectId, SignedReviewWorld.ReviewId, world.Slots[2].Id, cancellationToken);

        // Act and assert
        Assert.False(await IsConcludedAsync(world));
    }

    /// <summary>
    ///     Test that a review whose ballots disagree is not concluded.
    /// </summary>
    [Fact]
    public async Task ReviewLock_IsConcluded_Disputed_False()
    {
        // Arrange: sam disagrees and all three slots are signed
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await SignedReviewWorld.CreateAsync();
        var sam = SignedReviewWorld.ActorOf("sam");
        await world.Checklists.SetAnswerAsync(
            world.ProjectId, SignedReviewWorld.ReviewId, SignedReviewWorld.FirstItemId, ChecklistOutcome.Fail, "Wrong.", sam, cancellationToken);
        await world.Checklists.SubmitBallotAsync(world.ProjectId, SignedReviewWorld.ReviewId, sam, cancellationToken);
        await StoreAllSignaturesAsync(world);

        // Act and assert
        Assert.False(await IsConcludedAsync(world));
    }

    /// <summary>
    ///     Test that a review with every ballot handed in, agreeing, and every slot signed is concluded.
    /// </summary>
    [Fact]
    public async Task ReviewLock_IsConcluded_AllSubmittedAgreedSigned_True()
    {
        // Arrange
        var world = await SignedReviewWorld.CreateAsync();
        await StoreAllSignaturesAsync(world);

        // Act and assert
        Assert.True(await IsConcludedAsync(world));
    }

    /// <summary>
    ///     Test that a review whose ballots agree on a Fail, with a conclusion text and every slot signed for the
    ///     rejection, is concluded.
    /// </summary>
    [Fact]
    public async Task ReviewLock_IsConcluded_RejectedAllSignedWithText_True()
    {
        // Arrange
        var world = await RejectedWorldAsync(withText: true);
        await StoreAllSignaturesAsync(world, ReviewOutcome.Rejected);

        // Act and assert
        Assert.True(await IsConcludedAsync(world));
        Assert.Equal(ReviewConclusion.Rejected, await ConcludedOutcomeAsync(world));
    }

    /// <summary>
    ///     Test that a rejected review is not concluded while the conclusion text is blank.
    /// </summary>
    [Fact]
    public async Task ReviewLock_IsConcluded_RejectedNoText_False()
    {
        // Arrange
        var world = await RejectedWorldAsync(withText: false);
        await StoreAllSignaturesAsync(world, ReviewOutcome.Rejected);

        // Act and assert
        Assert.False(await IsConcludedAsync(world));
        Assert.Equal(ReviewConclusion.None, await ConcludedOutcomeAsync(world));
    }

    /// <summary>
    ///     Test that a signature made for another outcome than the agreed one does not conclude the review.
    /// </summary>
    [Fact]
    public async Task ReviewLock_IsConcluded_RejectedSignedForApproval_False()
    {
        // Arrange: the ballots agree on a rejection, but every signature says approved
        var world = await RejectedWorldAsync(withText: true);
        world.Store.ValidateSignatureOutcome = false;
        await StoreAllSignaturesAsync(world, ReviewOutcome.Approved);

        // Act and assert
        Assert.False(await IsConcludedAsync(world));
    }

    /// <summary>
    ///     Test that a rejected review is not concluded while one slot has no signature.
    /// </summary>
    [Fact]
    public async Task ReviewLock_IsConcluded_RejectedOneSlotUnsigned_False()
    {
        // Arrange
        var world = await RejectedWorldAsync(withText: true);
        await world.StoreSignatureAsync(1, ReviewOutcome.Rejected);
        await world.StoreSignatureAsync(2, ReviewOutcome.Rejected);

        // Act and assert
        Assert.False(await IsConcludedAsync(world));
    }

    /// <summary>
    ///     Test that a review with an approved conclusion reports the approved conclusion.
    /// </summary>
    [Fact]
    public async Task ReviewLock_ConcludedOutcome_AllSubmittedAgreedSigned_Approved()
    {
        // Arrange
        var world = await SignedReviewWorld.CreateAsync();
        await StoreAllSignaturesAsync(world);

        // Act and assert
        Assert.Equal(ReviewConclusion.Approved, await ConcludedOutcomeAsync(world));
    }

    /// <summary>
    ///     Test that a review whose ballots are disputed or pending has no concluded outcome.
    /// </summary>
    [Fact]
    public async Task ReviewLock_ConcludedOutcome_Disputed_None()
    {
        // Arrange: sam disagrees, all slots are signed
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await SignedReviewWorld.CreateAsync();
        var sam = SignedReviewWorld.ActorOf("sam");
        await world.Checklists.SetAnswerAsync(
            world.ProjectId, SignedReviewWorld.ReviewId, SignedReviewWorld.FirstItemId, ChecklistOutcome.Fail, "Wrong.", sam, cancellationToken);
        await world.Checklists.SubmitBallotAsync(world.ProjectId, SignedReviewWorld.ReviewId, sam, cancellationToken);
        await StoreAllSignaturesAsync(world);

        // Act and assert
        Assert.Equal(ReviewConclusion.None, await ConcludedOutcomeAsync(world));
    }

    /// <summary>
    ///     Creates a world whose three ballots all answer the first check Fail, optionally with a conclusion text.
    /// </summary>
    /// <param name="withText"><c>true</c> to write a review conclusion text.</param>
    /// <returns>The world; nobody has signed.</returns>
    private static async Task<SignedReviewWorld> RejectedWorldAsync(bool withText)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await SignedReviewWorld.CreateAsync();
        foreach (var owner in new[] { "rita", "sam", "tess" })
        {
            var actor = SignedReviewWorld.ActorOf(owner);
            await world.Checklists.SetAnswerAsync(
                world.ProjectId, SignedReviewWorld.ReviewId, SignedReviewWorld.FirstItemId, ChecklistOutcome.Fail, "Wrong.", actor, cancellationToken);
            await world.Checklists.SubmitBallotAsync(world.ProjectId, SignedReviewWorld.ReviewId, actor, cancellationToken);
        }

        if (withText)
        {
            await world.Checklists.SetReviewConclusionTextAsync(
                world.ProjectId,
                SignedReviewWorld.ReviewId,
                "The first check failed.",
                SignedReviewWorld.ActorOf("rita"),
                cancellationToken);
        }

        return world;
    }

    /// <summary>
    ///     Reads the concluded outcome of the review as it is stored now.
    /// </summary>
    /// <param name="world">The world.</param>
    /// <returns>The answer of <see cref="ReviewLock.ConcludedOutcome"/>.</returns>
    private static async Task<ReviewConclusion> ConcludedOutcomeAsync(SignedReviewWorld world)
    {
        var snapshot = (await world.Store.GetSnapshotAsync(
            world.ProjectId, SignedReviewWorld.ReviewId, TestContext.Current.CancellationToken))!;
        return ReviewLock.ConcludedOutcome(snapshot.Record, snapshot.Slots, snapshot.Signatures);
    }
}
