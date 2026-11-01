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
///     Unit tests of the ballot rules of <see cref="SigningService"/>: the signer's own ballot must be handed in,
///     other slots need not have, a dispute blocks signing, and a change of the agreed standing makes a signature out
///     of date.
/// </summary>
public class SigningServiceBallotTests
{
    /// <summary>
    ///     The password of every reviewer of the world.
    /// </summary>
    private const string Password = "correct horse battery";

    /// <summary>
    ///     Makes a ballot of the world a draft again without changing what it says: the owner answers the first check
    ///     with the value it has.
    /// </summary>
    /// <param name="world">The world.</param>
    /// <param name="owner">The user name of the slot owner.</param>
    /// <returns>A task that completes when the ballot is a draft.</returns>
    private static Task MakeDraftAsync(SignedReviewWorld world, string owner)
    {
        return world.Checklists.SetAnswerAsync(
            world.ProjectId, SignedReviewWorld.ReviewId, SignedReviewWorld.FirstItemId, ChecklistOutcome.Pass, null,
            SignedReviewWorld.ActorOf(owner), TestContext.Current.CancellationToken);
    }

    /// <summary>
    ///     Hands in a ballot that disagrees on the first check, acknowledging every signature.
    /// </summary>
    /// <param name="world">The world.</param>
    /// <param name="owner">The user name of the slot owner.</param>
    /// <returns>A task that completes when the ballot is handed in.</returns>
    private static async Task DissentAsync(SignedReviewWorld world, string owner)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var actor = SignedReviewWorld.ActorOf(owner);
        await world.Checklists.SetAnswerAsync(
            world.ProjectId, SignedReviewWorld.ReviewId, SignedReviewWorld.FirstItemId, ChecklistOutcome.Fail, "Wrong.", actor, cancellationToken);
        await world.Checklists.SubmitBallotAsync(
            world.ProjectId, SignedReviewWorld.ReviewId, actor, cancellationToken, world.Slots.Select(s => s.Id).ToList());
    }

    /// <summary>
    ///     Test that a reviewer whose own ballot is a draft cannot sign.
    /// </summary>
    [Fact]
    public async Task SigningService_SignAsync_OwnBallotDraft_ThrowsBallotNotSubmitted()
    {
        // Arrange: sam's ballot is a draft again, which leaves the agreed standing, and so the digest, as it was
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await SignedReviewWorld.CreateAsync();
        await MakeDraftAsync(world, "sam");
        var digest = (await world.ApprovalAsync()).Digest;

        // Act
        var ex = await Assert.ThrowsAsync<DomainRuleException>(() => world.Signing.SignAsync(
            SignedReviewWorld.ActorOf("sam"), world.ProjectId, SignedReviewWorld.ReviewId, world.Slots[1].Id, digest, Password, cancellationToken));

        // Assert
        Assert.Equal(DomainRule.BallotNotSubmitted, ex.Rule);
        Assert.Empty(await world.SignedSlotIdsAsync());
    }

    /// <summary>
    ///     Test that a reviewer whose ballot is handed in may sign while the ballot of another slot is still a draft.
    /// </summary>
    [Fact]
    public async Task SigningService_SignAsync_EarlySign_OtherSlotNotSubmitted_Succeeds()
    {
        // Arrange: tess has not handed her ballot in again
        var world = await SignedReviewWorld.CreateAsync();
        await MakeDraftAsync(world, "tess");

        // Act
        await world.SignAsync(1);

        // Assert: the signature of rita is current, and the review is not approved
        Assert.Equal([world.Slots[0].Id], await world.SignedSlotIdsAsync());
        Assert.False((await world.ApprovalAsync()).IsApproved);
    }

    /// <summary>
    ///     Test that a ballot of another slot that disagrees makes the signature out of date.
    /// </summary>
    [Fact]
    public async Task SigningService_SignAsync_OtherSlotDisputes_SignatureOutOfDate()
    {
        // Arrange: rita signs
        var world = await SignedReviewWorld.CreateAsync();
        await world.SignAsync(1);

        // Act: sam hands in a ballot that disagrees
        await DissentAsync(world, "sam");
        var approval = await world.ApprovalAsync();

        // Assert
        Assert.Equal(SlotSignatureState.OutOfDate, approval.Slots[0].State);
        Assert.False(approval.IsApproved);
    }

    /// <summary>
    ///     Test that a disputed check blocks signing and is named as a blocker.
    /// </summary>
    [Fact]
    public async Task SigningService_SignAsync_Disputed_BlocksSigning()
    {
        // Arrange: sam disagrees with the others
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await SignedReviewWorld.CreateAsync();
        await DissentAsync(world, "sam");
        var approval = await world.ApprovalAsync();

        // Act
        var ex = await Assert.ThrowsAsync<DomainRuleException>(() => world.Signing.SignAsync(
            SignedReviewWorld.ActorOf("tess"), world.ProjectId, SignedReviewWorld.ReviewId, world.Slots[2].Id, approval.Digest, Password, cancellationToken));

        // Assert
        Assert.Equal(DomainRule.SigningNotAllowed, ex.Rule);
        Assert.Contains("disputed", ex.Message, StringComparison.Ordinal);
        Assert.Contains(approval.Blockers, b => b.Contains("disputed", StringComparison.Ordinal));
        Assert.Equal(1, approval.DisputedChecks);
    }

    /// <summary>
    ///     Test that the review is approved only when every slot has handed in, nothing is disputed and every slot
    ///     holds a current signature.
    /// </summary>
    [Fact]
    public async Task SigningService_GetApprovalAsync_AllSubmittedAgreedSigned_Approved()
    {
        // Arrange: rita and sam sign, tess has a draft
        var world = await SignedReviewWorld.CreateAsync();
        await MakeDraftAsync(world, "tess");
        await world.SignAsync(1);
        await world.SignAsync(2);
        var beforeSubmit = await world.ApprovalAsync();

        // Act: tess hands in again (the agreed standing is as it was) and signs
        await world.Checklists.SubmitBallotAsync(
            world.ProjectId, SignedReviewWorld.ReviewId, SignedReviewWorld.ActorOf("tess"), TestContext.Current.CancellationToken);
        await world.SignAsync(3);
        var after = await world.ApprovalAsync();

        // Assert
        Assert.False(beforeSubmit.IsApproved);
        Assert.True(after.IsApproved);
    }

    /// <summary>
    ///     Test that removing the slot whose ballot holds the only dissent changes the agreed standing, and so needs the
    ///     acknowledgement of the signature it makes out of date.
    /// </summary>
    [Fact]
    public async Task SigningService_RemoveSlotAsync_SubmittedBallotChangesDigest_RequiresAcknowledgement()
    {
        // Arrange: sam disagrees; a signature given before that is stored straight into the store
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await SignedReviewWorld.CreateAsync();
        await DissentAsync(world, "sam");
        await world.StoreSignatureAsync(1);
        var actor = SignedReviewWorld.ActorOf("rita");

        // Act: remove the slot of sam without, then with the acknowledgement
        var ex = await Assert.ThrowsAsync<DomainRuleException>(() => world.Signing.RemoveSlotAsync(
            actor, world.ProjectId, SignedReviewWorld.ReviewId, world.Slots[1].Id, cancellationToken));
        var stillThere = (await world.Store.GetSnapshotAsync(world.ProjectId, SignedReviewWorld.ReviewId, cancellationToken))!.Slots.Count;
        await world.Signing.RemoveSlotAsync(
            actor, world.ProjectId, SignedReviewWorld.ReviewId, world.Slots[1].Id, cancellationToken, [world.Slots[0].Id]);

        // Assert
        Assert.Equal(DomainRule.SignaturesWouldBecomeOutOfDate, ex.Rule);
        Assert.Equal(3, stillThere);
        Assert.Equal(2, (await world.Store.GetSnapshotAsync(world.ProjectId, SignedReviewWorld.ReviewId, cancellationToken))!.Slots.Count);
    }

    /// <summary>
    ///     Test that a reviewer leaving a slot whose submitted ballot holds the only dissent changes the agreed
    ///     standing, and so needs the acknowledgement of the signature it makes out of date, and that a departure that
    ///     leaves the standing as it is needs none.
    /// </summary>
    [Fact]
    public async Task SigningService_UpdateSlotAsync_DepartingSubmittedBallotChangesDigest_RequiresAcknowledgement()
    {
        // Arrange: alex, the alternate of slot 1, hands in a ballot that disagrees; a signature given at that
        // standing is stored straight into the store
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await SignedReviewWorld.CreateAsync();
        var alex = SignedReviewWorld.ActorOf("alex");
        await world.Checklists.SetAnswerAsync(
            world.ProjectId, SignedReviewWorld.ReviewId, SignedReviewWorld.FirstItemId, ChecklistOutcome.Fail, "Wrong.", alex, cancellationToken);
        await world.Checklists.SubmitBallotAsync(world.ProjectId, SignedReviewWorld.ReviewId, alex, cancellationToken);
        await world.StoreSignatureAsync(2);
        var rita = SignedReviewWorld.ActorOf("rita");
        var slot = world.Slots[0];

        // Act: alex leaves the slot without, then with the acknowledgement
        var ex = await Assert.ThrowsAsync<DomainRuleException>(() => world.Signing.UpdateSlotAsync(
            rita, world.ProjectId, SignedReviewWorld.ReviewId, slot.Id, slot.PrimaryUserId, null, cancellationToken));
        var stillThere = (await world.Store.GetSnapshotAsync(world.ProjectId, SignedReviewWorld.ReviewId, cancellationToken))!.Slots[0].AlternateUserId;
        await world.Signing.UpdateSlotAsync(
            rita, world.ProjectId, SignedReviewWorld.ReviewId, slot.Id, slot.PrimaryUserId, null, cancellationToken, [world.Slots[1].Id]);

        // Assert: refused and nothing changed, then stored and the signature of slot 2 is out of date
        Assert.Equal(DomainRule.SignaturesWouldBecomeOutOfDate, ex.Rule);
        Assert.Equal(world.Slots[0].AlternateUserId, stillThere);
        Assert.Null((await world.Store.GetSnapshotAsync(world.ProjectId, SignedReviewWorld.ReviewId, cancellationToken))!.Slots[0].AlternateUserId);
        Assert.Equal(SlotSignatureState.OutOfDate, (await world.ApprovalAsync()).Slots[1].State);
    }

    /// <summary>
    ///     Test that a reviewer leaving a slot whose submitted ballot agrees with the others leaves the digest as it is,
    ///     so the signatures of the other slots stay current and need no acknowledgement.
    /// </summary>
    [Fact]
    public async Task SigningService_UpdateSlotAsync_DepartingBallotKeepsDigest_NoAcknowledgement()
    {
        // Arrange: slot 2 signs while every ballot agrees
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await SignedReviewWorld.CreateAsync();
        await world.SignAsync(2);
        var slot = world.Slots[0];

        // Act: alex leaves slot 1, which sends its ballot back to a draft; the others still agree
        await world.Signing.UpdateSlotAsync(
            SignedReviewWorld.ActorOf("rita"), world.ProjectId, SignedReviewWorld.ReviewId, slot.Id, slot.PrimaryUserId, null, cancellationToken);

        // Assert: stored, and the signature of slot 2 is still current
        Assert.Null((await world.Store.GetSnapshotAsync(world.ProjectId, SignedReviewWorld.ReviewId, cancellationToken))!.Slots[0].AlternateUserId);
        Assert.Equal([world.Slots[1].Id], await world.SignedSlotIdsAsync());
    }

    /// <summary>
    ///     Test that a review without a slot has no submitted ballot, so its next step is the open checklist.
    /// </summary>
    [Fact]
    public async Task SigningService_NextStep_NoSlots_PointsToTheUnansweredChecks()
    {
        // Arrange: a review without a slot
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await SignedReviewWorld.CreateAsync();
        foreach (var slot in world.Slots)
        {
            await world.Store.RemoveSlotAsync(slot.ProjectId, slot.ReviewId, slot.Id, cancellationToken);
        }

        // Act
        var approval = await world.ApprovalAsync();

        // Assert
        Assert.False(approval.IsApproved);
        Assert.EndsWith("checklist checks unanswered.", approval.NextStep, StringComparison.Ordinal);
    }
}
