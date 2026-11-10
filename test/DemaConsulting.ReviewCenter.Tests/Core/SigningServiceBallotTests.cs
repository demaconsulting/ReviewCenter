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
    ///     Test that the digest comparison comes before the check of the own ballot: a reviewer whose ballot is a draft
    ///     and who signs for a record that has changed since is told that the record changed.
    /// </summary>
    [Fact]
    public async Task SigningService_SignAsync_OwnBallotDraftAndStaleDigest_ThrowsRecordChanged()
    {
        // Arrange: sam's ballot is a draft; the digest is read, then tess changes the agreed standing
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await SignedReviewWorld.CreateAsync();
        await MakeDraftAsync(world, "sam");
        var staleDigest = (await world.ApprovalAsync()).Digest;
        await DissentAsync(world, "tess");

        // Act
        var ex = await Assert.ThrowsAsync<DomainRuleException>(() => world.Signing.SignAsync(
            SignedReviewWorld.ActorOf("sam"), world.ProjectId, SignedReviewWorld.ReviewId, world.Slots[1].Id, staleDigest, Password, cancellationToken));

        // Assert
        Assert.Equal(DomainRule.RecordChanged, ex.Rule);
        Assert.Empty(await world.SignedSlotIdsAsync());
    }

    /// <summary>
    ///     Test that the check for an existing signature comes before the check of the own ballot: a slot that holds a
    ///     signature of the current record is told so, although its ballot is a draft.
    /// </summary>
    [Fact]
    public async Task SigningService_SignAsync_OwnBallotDraftAndAlreadySigned_ThrowsSigningNotAllowedAsSigned()
    {
        // Arrange: slot 2 holds a signature of the current record, then sam's ballot is a draft with the standing as it was
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await SignedReviewWorld.CreateAsync();
        await world.StoreSignatureAsync(2);
        await MakeDraftAsync(world, "sam");
        var digest = (await world.ApprovalAsync()).Digest;

        // Act
        var ex = await Assert.ThrowsAsync<DomainRuleException>(() => world.Signing.SignAsync(
            SignedReviewWorld.ActorOf("sam"), world.ProjectId, SignedReviewWorld.ReviewId, world.Slots[1].Id, digest, Password, cancellationToken));

        // Assert: the refusal is the signed one, not the ballot one
        Assert.Equal(DomainRule.SigningNotAllowed, ex.Rule);
        Assert.Contains("already has a signature", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that the check of the own ballot comes before the blockers in a review with several slots: a reviewer
    ///     whose ballot is a draft is told to hand it in, although a thread is open as well.
    /// </summary>
    [Fact]
    public async Task SigningService_SignAsync_OwnBallotDraftAndOpenThread_ThrowsBallotNotSubmitted()
    {
        // Arrange: sam's ballot is a draft and a chat thread is open
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await SignedReviewWorld.CreateAsync();
        await MakeDraftAsync(world, "sam");
        await world.Threads.StartThreadAsync(
            world.ProjectId, SignedReviewWorld.ReviewId, new ThreadAnchor("a.txt"), SignedReviewWorld.ActorOf("rita"), "Look", cancellationToken);
        var approval = await world.ApprovalAsync();

        // Act
        var ex = await Assert.ThrowsAsync<DomainRuleException>(() => world.Signing.SignAsync(
            SignedReviewWorld.ActorOf("sam"), world.ProjectId, SignedReviewWorld.ReviewId, world.Slots[1].Id, approval.Digest, Password, cancellationToken));

        // Assert: the thread is a blocker, yet the ballot is what is named
        Assert.NotEmpty(approval.Blockers);
        Assert.Equal(DomainRule.BallotNotSubmitted, ex.Rule);
    }

    /// <summary>
    ///     Test that in a review with a single slot the blockers are named before the own ballot: the reviewer whose
    ///     ballot is a draft and who has a thread open is told what blocks the signature, and told to hand the ballot
    ///     in once nothing else blocks.
    /// </summary>
    [Fact]
    public async Task SigningService_SignAsync_SingleSlotDraftAndOpenThread_NamesBlockersBeforeTheBallot()
    {
        // Arrange: rita's ballot is made a draft while there are several slots (a single slot would hand it in at
        // once), then only slot 1 is left, and a chat thread is open
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await SignedReviewWorld.CreateAsync();
        var rita = SignedReviewWorld.ActorOf("rita");
        await MakeDraftAsync(world, "rita");
        await world.Signing.RemoveSlotAsync(rita, world.ProjectId, SignedReviewWorld.ReviewId, world.Slots[1].Id, cancellationToken);
        await world.Signing.RemoveSlotAsync(rita, world.ProjectId, SignedReviewWorld.ReviewId, world.Slots[2].Id, cancellationToken);
        var thread = await world.Threads.StartThreadAsync(
            world.ProjectId, SignedReviewWorld.ReviewId, new ThreadAnchor("a.txt"), rita, "Look", cancellationToken);
        var approval = await world.ApprovalAsync();

        // Act: sign with the thread open, then again after it is resolved
        var withBlocker = await Assert.ThrowsAsync<DomainRuleException>(() => world.Signing.SignAsync(
            rita, world.ProjectId, SignedReviewWorld.ReviewId, world.Slots[0].Id, approval.Digest, Password, cancellationToken));
        await world.Threads.ResolveAsync(world.ProjectId, SignedReviewWorld.ReviewId, thread.Id, rita, cancellationToken);
        var digest = (await world.ApprovalAsync()).Digest;
        var withoutBlocker = await Assert.ThrowsAsync<DomainRuleException>(() => world.Signing.SignAsync(
            rita, world.ProjectId, SignedReviewWorld.ReviewId, world.Slots[0].Id, digest, Password, cancellationToken));

        // Assert
        Assert.Equal(DomainRule.SigningNotAllowed, withBlocker.Rule);
        Assert.Equal(DomainRule.BallotNotSubmitted, withoutBlocker.Rule);
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
    ///     Test that the refusal of a signature while a check is disputed says how many checks are disputed.
    /// </summary>
    [Fact]
    public async Task SigningService_SignAsync_Disputed_MessageNamesTheNumberOfDisputedChecks()
    {
        // Arrange: sam disagrees with the others on one check
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await SignedReviewWorld.CreateAsync();
        await DissentAsync(world, "sam");
        var approval = await world.ApprovalAsync();

        // Act
        var ex = await Assert.ThrowsAsync<DomainRuleException>(() => world.Signing.SignAsync(
            SignedReviewWorld.ActorOf("tess"), world.ProjectId, SignedReviewWorld.ReviewId, world.Slots[2].Id, approval.Digest, Password, cancellationToken));

        // Assert: the message is the one sentence about the dispute, with its count
        Assert.Equal("The review cannot be signed yet: 1 checklist check is disputed between the ballots.", ex.Message);
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
    ///     Test that the status says whether the review is concluded, and how, as the standing of the signatures and
    ///     the ballots requires: concluded as Approved only when all submitted ballots agree and every slot has signed.
    /// </summary>
    [Fact]
    public async Task SigningService_GetApprovalAsync_ConclusionAndIsConcluded_MatchTheStandingInEveryStanding()
    {
        // Arrange and act: read the status for a review with nothing signed, a part signed and all signed, and for a
        // second review with all signed while a check is disputed
        var world = await SignedReviewWorld.CreateAsync();
        var disputedWorld = await SignedReviewWorld.CreateAsync();
        var standings = new List<ReviewApproval>
        {
            await world.ApprovalAsync(),
        };
        await world.StoreSignatureAsync(1);
        standings.Add(await world.ApprovalAsync());
        await world.StoreSignatureAsync(2);
        await world.StoreSignatureAsync(3);
        standings.Add(await world.ApprovalAsync());
        await DissentAsync(disputedWorld, "sam");
        await disputedWorld.StoreSignatureAsync(1);
        await disputedWorld.StoreSignatureAsync(2);
        await disputedWorld.StoreSignatureAsync(3);
        standings.Add(await disputedWorld.ApprovalAsync());

        // Assert: nothing signed, one of three signed and a dispute are not concluded; all signed and agreed is
        Assert.Equal(
            [ReviewConclusion.None, ReviewConclusion.None, ReviewConclusion.Approved, ReviewConclusion.None],
            standings.Select(s => s.Conclusion));
        Assert.Equal([false, false, true, false], standings.Select(s => s.IsConcluded));
    }

    /// <summary>
    ///     Test that removing the slot whose ballot holds the only dissent changes the agreed standing, and so needs the
    ///     acknowledgment of the signature it makes out of date.
    /// </summary>
    [Fact]
    public async Task SigningService_RemoveSlotAsync_SubmittedBallotChangesDigest_RequiresAcknowledgment()
    {
        // Arrange: sam disagrees; a signature given before that is stored straight into the store
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await SignedReviewWorld.CreateAsync();
        await DissentAsync(world, "sam");
        await world.StoreSignatureAsync(1);
        var actor = SignedReviewWorld.ActorOf("rita");

        // Act: remove the slot of sam without, then with the acknowledgment
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
    ///     Test that removing an unsigned slot also removes the ballot of that slot, while the ballots of the other
    ///     slots stay.
    /// </summary>
    [Fact]
    public async Task SigningService_RemoveSlotAsync_SubmittedBallot_RemovesTheBallotOfTheSlot()
    {
        // Arrange: three slots, each with a submitted ballot, and nothing signed
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await SignedReviewWorld.CreateAsync();
        var removed = world.Slots[1];

        // Act: remove the second slot
        await world.Signing.RemoveSlotAsync(
            SignedReviewWorld.ActorOf("rita"), world.ProjectId, SignedReviewWorld.ReviewId, removed.Id, cancellationToken);

        // Assert: the ballot of the removed slot is gone and the ballots of the other two slots remain
        var record = (await world.Store.GetSnapshotAsync(world.ProjectId, SignedReviewWorld.ReviewId, cancellationToken))!.Record;
        Assert.DoesNotContain(record.Ballots, ballot => ballot.SlotId == removed.Id);
        Assert.Equal([world.Slots[0].Id, world.Slots[2].Id], record.Ballots.Select(ballot => ballot.SlotId).Order());
    }

    /// <summary>
    ///     Test that a reviewer leaving a slot whose submitted ballot holds the only dissent changes the agreed
    ///     standing, and so needs the acknowledgment of the signature it makes out of date, and that a departure that
    ///     leaves the standing as it is needs none.
    /// </summary>
    [Fact]
    public async Task SigningService_UpdateSlotAsync_DepartingSubmittedBallotChangesDigest_RequiresAcknowledgment()
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

        // Act: alex leaves the slot without, then with the acknowledgment
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
    ///     so the signatures of the other slots stay current and need no acknowledgment.
    /// </summary>
    [Fact]
    public async Task SigningService_UpdateSlotAsync_DepartingBallotKeepsDigest_NoAcknowledgment()
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
    ///     Test that removing a slot whose submitted ballot agrees with the others leaves the digest as it is, so the
    ///     signatures of the other slots stay current and need no acknowledgment.
    /// </summary>
    [Fact]
    public async Task SigningService_RemoveSlotAsync_AgreeingBallotKeepsDigest_NoAcknowledgment()
    {
        // Arrange: slot 2 signs while every ballot agrees
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await SignedReviewWorld.CreateAsync();
        await world.SignAsync(2);

        // Act: remove slot 3, whose ballot agrees, without any acknowledgment
        await world.Signing.RemoveSlotAsync(
            SignedReviewWorld.ActorOf("rita"), world.ProjectId, SignedReviewWorld.ReviewId, world.Slots[2].Id, cancellationToken);

        // Assert: removed, and the signature of slot 2 is still current
        Assert.Equal(2, (await world.Store.GetSnapshotAsync(world.ProjectId, SignedReviewWorld.ReviewId, cancellationToken))!.Slots.Count);
        Assert.Equal([world.Slots[1].Id], await world.SignedSlotIdsAsync());
    }

    /// <summary>
    ///     Test that a disputed check is named once, as disputed, in the next step, and not also as unanswered, while
    ///     every check is answered.
    /// </summary>
    [Fact]
    public async Task SigningService_GetApprovalAsync_Disputed_NextStepDoesNotCountItAsUnanswered()
    {
        // Arrange: every check is answered by every slot; sam disagrees on the first check
        var world = await SignedReviewWorld.CreateAsync();
        await DissentAsync(world, "sam");

        // Act
        var approval = await world.ApprovalAsync();

        // Assert: one dispute, nothing unanswered, in the structure and in the sentence
        Assert.Equal(1, approval.DisputedChecks);
        Assert.Equal(0, approval.UnansweredChecks);
        Assert.Equal("1 check disputed between the ballots.", approval.NextStep);
        Assert.DoesNotContain("unanswered", approval.NextStep, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that the next step says who has signed and whose signature is awaited once nothing blocks signing, and
    ///     is unchanged while nobody has signed.
    /// </summary>
    [Fact]
    public async Task SigningService_GetApprovalAsync_SomeSigned_NextStepNamesSignersAndWaitingSlots()
    {
        // Arrange, Act, Assert: nobody, then one, then two have signed
        var world = await SignedReviewWorld.CreateAsync();
        Assert.Equal("Ready for signatures.", (await world.ApprovalAsync()).NextStep);
        await world.SignAsync(1);
        Assert.Equal("Signed by Rita. Waiting for the signatures of Sam and Tess.", (await world.ApprovalAsync()).NextStep);
        await world.SignAsync(2);
        Assert.Equal("Signed by Rita and Sam. Waiting for the signature of Tess.", (await world.ApprovalAsync()).NextStep);
    }

    /// <summary>
    ///     Test that a signature that no longer counts is named in the next step, with everybody who is awaited.
    /// </summary>
    [Fact]
    public async Task SigningService_GetApprovalAsync_SignatureOutOfDate_NextStepNamesIt()
    {
        // Arrange: rita signs, then the conclusion text changes the record, acknowledging her signature
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await SignedReviewWorld.CreateAsync();
        await world.SignAsync(1);
        await world.Checklists.SetReviewConclusionTextAsync(
            world.ProjectId, SignedReviewWorld.ReviewId, "A note.", SignedReviewWorld.ActorOf("rita"), cancellationToken, [world.Slots[0].Id]);

        // Act
        var approval = await world.ApprovalAsync();

        // Assert
        Assert.Equal(SlotSignatureState.OutOfDate, approval.Slots[0].State);
        Assert.Empty(approval.Blockers);
        Assert.Equal(
            "Rita's signature no longer counts because the review changed. Waiting for Rita, Sam and Tess.",
            approval.NextStep);
        Assert.Equal("Rita", approval.Slots[0].LapsedSignerName);
    }

    /// <summary>
    ///     Test that a slot that was signed by its alternate names the alternate, not the primary, in the next step
    ///     when that signature no longer counts.
    /// </summary>
    [Fact]
    public async Task SigningService_GetApprovalAsync_AlternateSignatureOutOfDate_NextStepNamesTheAlternate()
    {
        // Arrange: alex, the alternate of slot 1, signs, then the conclusion text changes the record, acknowledging it
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await SignedReviewWorld.CreateAsync();
        await world.SignAsync(1, alternate: true);
        await world.Checklists.SetReviewConclusionTextAsync(
            world.ProjectId, SignedReviewWorld.ReviewId, "A note.", SignedReviewWorld.ActorOf("rita"), cancellationToken, [world.Slots[0].Id]);

        // Act
        var approval = await world.ApprovalAsync();

        // Assert: the lapsed signature is Alex's; the awaited people are still the primaries of the slots
        Assert.Equal(SlotSignatureState.OutOfDate, approval.Slots[0].State);
        Assert.Equal("Alex", approval.Slots[0].LapsedSignerName);
        Assert.Equal(
            "Alex's signature no longer counts because the review changed. Waiting for Rita, Sam and Tess.",
            approval.NextStep);
    }

    /// <summary>
    ///     Test that the next step names the actual signers when the signatures of several slots no longer count and
    ///     one slot was signed by its alternate.
    /// </summary>
    [Fact]
    public async Task SigningService_GetApprovalAsync_SeveralSignaturesOutOfDate_NextStepNamesTheSigners()
    {
        // Arrange: alex signs slot 1 and sam signs slot 2, then the conclusion text changes, acknowledging both
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await SignedReviewWorld.CreateAsync();
        await world.SignAsync(1, alternate: true);
        await world.SignAsync(2);
        await world.Checklists.SetReviewConclusionTextAsync(
            world.ProjectId, SignedReviewWorld.ReviewId, "A note.", SignedReviewWorld.ActorOf("rita"), cancellationToken,
            [world.Slots[0].Id, world.Slots[1].Id]);

        // Act
        var approval = await world.ApprovalAsync();

        // Assert: Alex and Sam, never Rita, are named as the signers whose signatures lapsed
        Assert.Equal(
            "The signatures of Alex and Sam no longer count because the review changed. Waiting for Rita, Sam and Tess.",
            approval.NextStep);
    }

    /// <summary>
    ///     Test that the newest signature of a slot decides who is named when the signature no longer counts, and that
    ///     a signed or unsigned slot and a masked status carry the name correctly.
    /// </summary>
    [Fact]
    public async Task SigningService_GetApprovalAsync_NewestSignatureOutOfDate_LapsedSignerNameIsItsSigner()
    {
        // Arrange: rita signs slot 1, the record changes, alex signs the new record, the record changes again; sam signs
        // slot 2 at the final record
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await SignedReviewWorld.CreateAsync();
        var slotOne = world.Slots[0].Id;
        await world.SignAsync(1);
        await world.Checklists.SetReviewConclusionTextAsync(
            world.ProjectId, SignedReviewWorld.ReviewId, "A note.", SignedReviewWorld.ActorOf("rita"), cancellationToken, [slotOne]);
        await world.SignAsync(1, alternate: true);
        await world.Checklists.SetReviewConclusionTextAsync(
            world.ProjectId, SignedReviewWorld.ReviewId, "Another note.", SignedReviewWorld.ActorOf("rita"), cancellationToken, [slotOne]);
        await world.SignAsync(2);

        // Act
        var approval = await world.ApprovalAsync();
        var masked = approval.Masked();

        // Assert: slot 1 names the newest signer; the signed and the unsigned slot name nobody; masking keeps the name
        Assert.Equal("Alex", approval.Slots[0].LapsedSignerName);
        Assert.Null(approval.Slots[1].LapsedSignerName);
        Assert.Null(approval.Slots[2].LapsedSignerName);
        Assert.Equal("Alex", masked.Slots[0].LapsedSignerName);
    }

    /// <summary>
    ///     Test that a masked status never carries the signature-aware sentence of the full status, only the sentence
    ///     built from the counts that stay visible, so the masked next step is the same whoever has signed.
    /// </summary>
    [Fact]
    public void ReviewApproval_Masked_NextStepLeadsWithHandInAndNamesOpenThreadsOnlyWhenThereAreSome()
    {
        // Arrange: a status with no open thread and one with an open question
        var none = new ReviewApproval(ReviewConclusion.None, "d", [], [], "Signed by Rita. Waiting for the signature of Sam.");
        var open = none with { OpenThreads = 1, OpenQuestions = 1 };

        // Act
        var maskedNone = none.Masked();
        var maskedOpen = open.Masked();

        // Assert: the hand-in sentence comes first; the open threads are added only when there are some
        Assert.Equal("Hand in your ballot to see the other reviewers' answers.", maskedNone.NextStep);
        Assert.Equal(
            "Hand in your ballot to see the other reviewers' answers. Resolve the open threads. " + ReviewApproval.HiddenNotice,
            maskedOpen.NextStep);
    }

    /// <summary>
    ///     Test the sentence that warns a signer about ballots that are not handed in: singular, plural, a longer list
    ///     and no names.
    /// </summary>
    /// <param name="names">The names, separated by a vertical bar.</param>
    /// <param name="expected">The expected sentence.</param>
    [Theory]
    [InlineData("Bob", "Bob has not handed in a ballot yet. If you sign now, you may have to sign again.")]
    [InlineData("Bob|Carol", "Bob and Carol have not handed in a ballot yet. If you sign now, you may have to sign again.")]
    [InlineData("Bob|Carol|Dave", "Bob, Carol and Dave have not handed in a ballot yet. If you sign now, you may have to sign again.")]
    [InlineData("", "")]
    public void ReviewApproval_MissingBallotsText_WordsTheNames(string names, string expected)
    {
        // Arrange
        var list = names.Length == 0 ? [] : names.Split('|');

        // Act, Assert
        Assert.Equal(expected, ReviewApproval.MissingBallotsText(list));
    }

    /// <summary>
    ///     Test that the missing-ballots sentence refuses a missing list.
    /// </summary>
    [Fact]
    public void ReviewApproval_MissingBallotsText_NullNames_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => ReviewApproval.MissingBallotsText(null!));
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
