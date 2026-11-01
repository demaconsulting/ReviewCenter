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
///     Unit tests of the rejected outcome of <see cref="SigningService"/>: a Fail is no blocker, a rejection needs a
///     conclusion text, the signature records the outcome it attests, and a review in which every slot signed the
///     rejection is concluded and locked like an approved one.
/// </summary>
public class SigningServiceRejectedTests
{
    /// <summary>
    ///     The password of every reviewer of the world.
    /// </summary>
    private const string Password = "correct horse battery";

    /// <summary>
    ///     The conclusion text written for a rejection.
    /// </summary>
    private const string ConclusionText = "The first check failed; the change is not ready.";

    /// <summary>
    ///     Creates a world whose three ballots all answer the first check Fail, optionally with a conclusion text.
    /// </summary>
    /// <param name="withText"><c>true</c> to write the conclusion text.</param>
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
                world.ProjectId, SignedReviewWorld.ReviewId, ConclusionText, SignedReviewWorld.ActorOf("rita"), cancellationToken);
        }

        return world;
    }

    /// <summary>
    ///     Test that a Fail answer is no blocker: the agreed rejection with a conclusion text can be signed.
    /// </summary>
    [Fact]
    public async Task SigningService_SignAsync_RejectedWithText_StoresRejectedSignature()
    {
        // Arrange: all ballots agree on a Fail and the conclusion text is written
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await RejectedWorldAsync(withText: true);
        var before = await world.ApprovalAsync();

        // Act: rita signs
        await world.SignAsync(1);

        // Assert: the signature carries the rejected outcome and its meaning, and the audit entry names the outcome
        Assert.Equal(ReviewOutcome.Rejected, before.Outcome);
        Assert.Empty(before.Blockers);
        Assert.Equal("Ready for signatures (rejected outcome).", before.NextStep);
        var signature = Assert.Single(await world.Store.ListSignaturesAsync(world.ProjectId, SignedReviewWorld.ReviewId, cancellationToken));
        Assert.Equal(ReviewOutcome.Rejected, signature.Outcome);
        Assert.Equal(SigningService.MeaningRejectedText, signature.Meaning);
        var audit = world.Store.Audits.Single(d => d.Action == "signature.added");
        Assert.Contains("outcome Rejected", audit.Detail, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that an approval records the approved outcome and its meaning.
    /// </summary>
    [Fact]
    public async Task SigningService_SignAsync_Approved_StoresApprovedSignature()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await SignedReviewWorld.CreateAsync();

        // Act
        await world.SignAsync(1);

        // Assert
        var signature = Assert.Single(await world.Store.ListSignaturesAsync(world.ProjectId, SignedReviewWorld.ReviewId, cancellationToken));
        Assert.Equal(ReviewOutcome.Approved, signature.Outcome);
        Assert.Equal(SigningService.MeaningText, signature.Meaning);
        Assert.Contains("outcome Approved", world.Store.Audits.Single(d => d.Action == "signature.added").Detail, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that signing a rejection without a conclusion text is refused with the conclusion rule, and nothing is
    ///     stored.
    /// </summary>
    [Fact]
    public async Task SigningService_SignAsync_RejectedNoText_ThrowsConclusionRequired()
    {
        // Arrange: the ballots agree on a Fail but no text is written
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await RejectedWorldAsync(withText: false);
        var approval = await world.ApprovalAsync();

        // Act
        var ex = await Assert.ThrowsAsync<DomainRuleException>(() => world.Signing.SignAsync(
            SignedReviewWorld.ActorOf("rita"), world.ProjectId, SignedReviewWorld.ReviewId, world.Slots[0].Id, approval.Digest, Password, cancellationToken));

        // Assert: the rule is named, the status asks for the text, and nothing is stored
        Assert.Equal(DomainRule.ConclusionRequired, ex.Rule);
        Assert.True(approval.ConclusionRequired);
        Assert.Contains(SigningService.ConclusionRequiredText, approval.Blockers);
        Assert.Contains("conclusion", approval.NextStep, StringComparison.Ordinal);
        Assert.Empty(await world.SignedSlotIdsAsync());
    }

    /// <summary>
    ///     Test that a blank conclusion text counts as no text.
    /// </summary>
    [Fact]
    public async Task SigningService_SignAsync_RejectedBlankText_ThrowsConclusionRequired()
    {
        // Arrange: a text of white space is stored as none
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await RejectedWorldAsync(withText: false);
        await world.Checklists.SetReviewConclusionTextAsync(
            world.ProjectId, SignedReviewWorld.ReviewId, "   ", SignedReviewWorld.ActorOf("rita"), cancellationToken);
        var approval = await world.ApprovalAsync();

        // Act
        var ex = await Assert.ThrowsAsync<DomainRuleException>(() => world.Signing.SignAsync(
            SignedReviewWorld.ActorOf("rita"), world.ProjectId, SignedReviewWorld.ReviewId, world.Slots[0].Id, approval.Digest, Password, cancellationToken));

        // Assert
        Assert.Equal(DomainRule.ConclusionRequired, ex.Rule);
    }

    /// <summary>
    ///     Test that when the conclusion is not the only blocker, signing is refused as not allowed and the message
    ///     lists every blocker, the conclusion included.
    /// </summary>
    [Fact]
    public async Task SigningService_SignAsync_RejectedNoTextAndOpenThread_ThrowsSigningNotAllowedListingBoth()
    {
        // Arrange: a rejection without text, and an open thread
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await RejectedWorldAsync(withText: false);
        await world.Threads.StartThreadAsync(
            world.ProjectId, SignedReviewWorld.ReviewId, new ThreadAnchor("a.txt"), SignedReviewWorld.ActorOf("rita"), "Look", cancellationToken);
        var approval = await world.ApprovalAsync();

        // Act
        var ex = await Assert.ThrowsAsync<DomainRuleException>(() => world.Signing.SignAsync(
            SignedReviewWorld.ActorOf("rita"), world.ProjectId, SignedReviewWorld.ReviewId, world.Slots[0].Id, approval.Digest, Password, cancellationToken));

        // Assert
        Assert.Equal(DomainRule.SigningNotAllowed, ex.Rule);
        Assert.Contains(SigningService.ConclusionRequiredText, ex.Message, StringComparison.Ordinal);
        Assert.Contains("open", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that an agreed rejection with every slot signed is concluded: rejected, not approved, and locked.
    /// </summary>
    [Fact]
    public async Task SigningService_GetApprovalAsync_RejectedAllSigned_ConcludedAndNotApproved()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await RejectedWorldAsync(withText: true);
        await world.SignAsync(1);
        await world.SignAsync(2);
        var beforeLast = await world.ApprovalAsync();

        // Act: the last slot signs
        await world.SignAsync(3);
        var approval = await world.ApprovalAsync();

        // Assert
        Assert.False(beforeLast.IsConcluded);
        Assert.Equal(ReviewConclusion.Rejected, approval.Conclusion);
        Assert.True(approval.IsConcluded);
        Assert.False(approval.IsApproved);
        Assert.StartsWith("Rejected.", approval.NextStep, StringComparison.Ordinal);
        var locked = await Assert.ThrowsAsync<DomainRuleException>(() => world.Checklists.SetReviewConclusionTextAsync(
            world.ProjectId, SignedReviewWorld.ReviewId, "Changed", SignedReviewWorld.ActorOf("rita"), cancellationToken));
        Assert.Equal(DomainRule.ReviewLocked, locked.Rule);
    }

    /// <summary>
    ///     Test that a rejected review can be reopened like an approved one, which keeps the signatures as history and
    ///     leaves the review in review.
    /// </summary>
    [Fact]
    public async Task SigningService_ReopenReviewAsync_Rejected_ReopensTheReview()
    {
        // Arrange: a rejected, concluded review
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await RejectedWorldAsync(withText: true);
        await world.SignAsync(1);
        await world.SignAsync(2);
        await world.SignAsync(3);
        Assert.True((await world.ApprovalAsync()).IsConcluded);

        // Act
        await world.Signing.ReopenReviewAsync(
            SignedReviewWorld.ActorOf("rita"), world.ProjectId, SignedReviewWorld.ReviewId, "Rework planned.", cancellationToken);
        var approval = await world.ApprovalAsync();

        // Assert
        Assert.False(approval.IsConcluded);
        Assert.Equal(ReviewConclusion.None, approval.Conclusion);
        Assert.Single(approval.Reopens!);
    }

    /// <summary>
    ///     Test that a signature of an approval does not conclude a review whose ballots now agree on a rejection: the
    ///     digest differs, so the signature is out of date.
    /// </summary>
    [Fact]
    public async Task SigningService_GetApprovalAsync_ApprovalSignedThenRejectionAgreed_SignatureOutOfDate()
    {
        // Arrange: rita signs the approval while every ballot passes
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await SignedReviewWorld.CreateAsync();
        await world.SignAsync(1);

        // Act: every ballot turns to a Fail, acknowledging the signature
        var acknowledged = world.Slots.Select(s => s.Id).ToList();
        foreach (var owner in new[] { "rita", "sam", "tess" })
        {
            var actor = SignedReviewWorld.ActorOf(owner);
            await world.Checklists.SetAnswerAsync(
                world.ProjectId, SignedReviewWorld.ReviewId, SignedReviewWorld.FirstItemId, ChecklistOutcome.Fail, "Wrong.", actor, cancellationToken,
                acknowledged);
            await world.Checklists.SubmitBallotAsync(world.ProjectId, SignedReviewWorld.ReviewId, actor, cancellationToken, acknowledged);
        }

        var approval = await world.ApprovalAsync();

        // Assert
        Assert.Equal(SlotSignatureState.OutOfDate, approval.Slots[0].State);
        Assert.False(approval.IsConcluded);
        Assert.Equal(ReviewOutcome.Rejected, approval.Outcome);
    }
}
