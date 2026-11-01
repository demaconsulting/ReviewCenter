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
///     Tests the "Where are we?" status of a review with the real access rules: the reviewers, the disputed checks,
///     the open questions and findings, the next action and what changed since a reviewer signed.
/// </summary>
public class ReviewStatusServiceTests
{
    /// <summary>
    ///     The cancellation token of the running test.
    /// </summary>
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>
    ///     Gets the status of the review for a user.
    /// </summary>
    /// <param name="world">The world.</param>
    /// <param name="username">The user name of the viewer.</param>
    /// <returns>The status.</returns>
    private static async Task<ReviewStatus> StatusAsync(BallotMaskingWorld world, string username)
    {
        var status = await world.Status.GetStatusAsync(
            BallotMaskingWorld.ActorOf(username), world.ProjectId, BallotMaskingWorld.ReviewId, Token);
        Assert.NotNull(status);
        return status;
    }

    /// <summary>
    ///     Moves the audit entries the services wrote into the audit trail the status reads, stamped with the time now.
    /// </summary>
    /// <param name="world">The world.</param>
    /// <param name="from">How many drafts were moved already.</param>
    /// <returns>How many drafts have been moved now.</returns>
    private static async Task<int> FlushAsync(BallotMaskingWorld world, int from)
    {
        var drafts = world.Store.Audits.Skip(from).ToList();
        foreach (var draft in drafts)
        {
            await world.AuditStore.AppendAsync(draft, Token);
        }

        return from + drafts.Count;
    }

    /// <summary>
    ///     Appends one entry to the audit trail of the review, stamped with the time now.
    /// </summary>
    /// <param name="world">The world.</param>
    /// <param name="username">The user who did it.</param>
    /// <param name="action">The audit action.</param>
    /// <param name="detail">The detail text, as the service that audits it words it.</param>
    /// <returns>A task that completes when the entry is appended.</returns>
    private static async Task AppendAsync(BallotMaskingWorld world, string username, string action, string detail)
    {
        world.Clock.Advance(TimeSpan.FromSeconds(1));
        await world.AuditStore.AppendAsync(
            AuditDraft.By(BallotMaskingWorld.ActorOf(username), action, detail, world.ProjectId, BallotMaskingWorld.ReviewId),
            Token);
    }

    /// <summary>
    ///     Arranges a review in which ann signed a review where both ballots agree, and bob then took his ballot back
    ///     by changing an answer, so the signature of ann no longer matches.
    /// </summary>
    /// <param name="world">The world.</param>
    /// <returns>How many drafts of the services have been moved into the audit trail.</returns>
    private static async Task<int> ArrangeSignedThenChangedAsync(BallotMaskingWorld world)
    {
        var slot = await world.SeatAsync("ann");
        await world.SeatAsync("bob");
        await world.AnswerAsync("ann");
        await world.AnswerAsync("bob");
        await world.SubmitAsync("ann");
        await world.SubmitAsync("bob");
        var moved = await FlushAsync(world, 0);

        // Ann signs a minute later; everything before is already part of what she signed
        world.Clock.Advance(TimeSpan.FromMinutes(1));
        var approval = await world.Signing.GetApprovalAsync(
            BallotMaskingWorld.ActorOf("ann"), world.ProjectId, BallotMaskingWorld.ReviewId, Token);
        await world.Signing.SignAsync(
            BallotMaskingWorld.ActorOf("ann"),
            world.ProjectId,
            BallotMaskingWorld.ReviewId,
            slot.Id,
            approval!.Digest,
            BallotMaskingWorld.Password,
            Token);
        moved = await FlushAsync(world, moved);

        // Bob changes his answer and hands the ballot in again: the reviewers now disagree on a check
        world.Clock.Advance(TimeSpan.FromMinutes(1));
        await world.AnswerAsync("bob", ChecklistOutcome.Fail);
        await world.Checklists.SubmitBallotAsync(
            world.ProjectId, BallotMaskingWorld.ReviewId, BallotMaskingWorld.ActorOf("bob"), Token, [slot.Id]);
        return await FlushAsync(world, moved);
    }

    /// <summary>
    ///     Signs the review in a slot as a user, after looking at the current record.
    /// </summary>
    /// <param name="world">The world.</param>
    /// <param name="username">The user name of the signer, who holds the slot.</param>
    /// <param name="slotId">The slot identity.</param>
    /// <returns>A task that completes when the signature is stored.</returns>
    private static async Task SignAsync(BallotMaskingWorld world, string username, int slotId)
    {
        var actor = BallotMaskingWorld.ActorOf(username);
        var approval = await world.Signing.GetApprovalAsync(actor, world.ProjectId, BallotMaskingWorld.ReviewId, Token);
        await world.Signing.SignAsync(
            actor,
            world.ProjectId,
            BallotMaskingWorld.ReviewId,
            slotId,
            approval!.Digest,
            BallotMaskingWorld.Password,
            Token);
    }

    /// <summary>
    ///     Describes a status as one text, because the records of a status do not compare their lists.
    /// </summary>
    /// <param name="status">The status.</param>
    /// <returns>The text; two statuses with the same content give the same text.</returns>
    private static string Describe(ReviewStatus status)
    {
        var reviewers = string.Join(",", status.Reviewers.Select(r => $"{r.Number}:{r.Name}:{r.Ballot}:{r.Signature}:{r.IsYou}"));
        var disputes = string.Join(",", status.DisputedChecks.Select(d => $"{d.ItemId}:{d.CanDiscuss}"));
        var changes = string.Join(",", status.ChangesSinceSigning.Select(s => $"{s.Number}:{s.Changes.Count}"));
        return $"{status.IsMasked}|{status.IsConcluded}|{status.HasChecklist}|{reviewers}|{disputes}|{status.OpenQuestions}"
               + $"|{status.OpenFindings}|{status.NextAction.Kind}|{status.NextAction.Label}|{changes}";
    }

    /// <summary>
    ///     Test that every slot is listed in order with the state of its ballot and its signature, and that the viewer is
    ///     marked.
    /// </summary>
    [Fact]
    public async Task ReviewStatus_Reviewers_ShowBallotAndSignatureState()
    {
        // Arrange: ann handed in, bob has a draft, cyd holds a slot and has not started
        var world = await BallotMaskingWorld.CreateAsync();
        await world.SeatAsync("ann");
        await world.SeatAsync("bob");
        await world.SeatAsync("cyd");
        await world.AnswerAsync("ann");
        await world.SubmitAsync("ann");
        await world.AnswerAsync("bob");

        // Act
        var status = await StatusAsync(world, "ann");

        // Assert
        Assert.Equal(["ann", "bob", "cyd"], status.Reviewers.Select(r => r.Name));
        Assert.Equal([BallotState.Submitted, BallotState.Draft, BallotState.NotStarted], status.Reviewers.Select(r => r.Ballot));
        Assert.All(status.Reviewers, r => Assert.Equal(SlotSignatureState.NotSigned, r.Signature));
        Assert.Equal([true, false, false], status.Reviewers.Select(r => r.IsYou));
        Assert.True(status.HasChecklist);
        Assert.False(status.IsConcluded);
    }

    /// <summary>
    ///     Test that a viewer who handed in sees the disputed checks, can discuss them until a discussion is open, and is
    ///     asked to discuss them.
    /// </summary>
    [Fact]
    public async Task ReviewStatus_DisputedChecks_ListedWithDiscussUntilThreadIsOpen()
    {
        // Arrange: ann says Pass, bob says Fail on the first check
        var world = await BallotMaskingWorld.CreateAsync();
        await world.ArrangeDisputeAsync();

        // Act
        var before = await StatusAsync(world, "ann");
        await world.StartCheckThreadAsync("ann");
        var after = await StatusAsync(world, "ann");

        // Assert
        var dispute = Assert.Single(before.DisputedChecks);
        Assert.Equal(BallotMaskingWorld.FirstItemId, dispute.ItemId);
        Assert.False(string.IsNullOrWhiteSpace(dispute.Text));
        Assert.True(dispute.CanDiscuss);
        Assert.Equal(NextActionKind.DiscussDisputes, before.NextAction.Kind);
        Assert.False(Assert.Single(after.DisputedChecks).CanDiscuss);
        Assert.Equal(1, after.OpenQuestions);
        Assert.NotEqual(NextActionKind.DiscussDisputes, after.NextAction.Kind);
    }

    /// <summary>
    ///     Test that a viewer with a draft ballot, and a contributor without a slot, get no dispute, no count of the
    ///     discussions about checks and no word about the outcome, while the lead sees all.
    /// </summary>
    /// <param name="username">The viewer.</param>
    [Theory]
    [InlineData("bob")]
    [InlineData("cyd")]
    public async Task ReviewStatus_MaskedViewer_NoDisputedChecks(string username)
    {
        // Arrange: ann handed in, bob has a draft that differs, and ann started a discussion about the check
        var world = await BallotMaskingWorld.CreateAsync();
        await world.ArrangeDisputeAsync(bobSubmitted: false);
        await world.StartCheckThreadAsync("ann");

        // Act
        var masked = await StatusAsync(world, username);
        var lead = await StatusAsync(world, "lead");

        // Assert
        Assert.True(masked.IsMasked);
        Assert.Empty(masked.DisputedChecks);
        Assert.Equal(0, masked.OpenQuestions);
        Assert.Equal(0, masked.OpenFindings);
        Assert.NotEqual(NextActionKind.DiscussDisputes, masked.NextAction.Kind);
        Assert.False(lead.IsMasked);
        Assert.Equal(1, lead.OpenQuestions);
    }

    /// <summary>
    ///     Test that a viewer who cannot take part, an Auditor, is told there is nothing to do and cannot start a
    ///     discussion about a disputed check.
    /// </summary>
    [Fact]
    public async Task ReviewStatus_Auditor_SeesDisputesButCannotDiscuss()
    {
        // Arrange
        var world = await BallotMaskingWorld.CreateAsync();
        await world.ArrangeDisputeAsync();

        // Act
        var status = await StatusAsync(world, "aud");

        // Assert
        Assert.False(status.IsMasked);
        Assert.False(Assert.Single(status.DisputedChecks).CanDiscuss);
        Assert.Equal(NextActionKind.None, status.NextAction.Kind);
        Assert.False(status.NextAction.IsActionable);
    }

    /// <summary>
    ///     Test that a person who may not see the project is refused, and that a review that does not exist has no
    ///     status.
    /// </summary>
    [Fact]
    public async Task ReviewStatus_Outsider_IsRefusedAndMissingReviewIsNull()
    {
        // Arrange
        var world = await BallotMaskingWorld.CreateAsync();

        // Act
        var refusal = await Assert.ThrowsAsync<DomainRuleException>(
            () => world.Status.GetStatusAsync(BallotMaskingWorld.ActorOf("out"), world.ProjectId, BallotMaskingWorld.ReviewId, Token));
        var missing = await world.Status.GetStatusAsync(BallotMaskingWorld.ActorOf("lead"), world.ProjectId, "nope", Token);

        // Assert
        Assert.Equal(DomainRule.ProjectNotFound, refusal.Rule);
        Assert.Null(missing);
    }

    /// <summary>
    ///     Test that a review without a reviewer asks the lead to add the reviewers and shows an empty list of
    ///     reviewers.
    /// </summary>
    [Fact]
    public async Task ReviewStatus_NoSlots_LeadIsAskedToAddReviewers()
    {
        // Arrange
        var world = await BallotMaskingWorld.CreateAsync();

        // Act
        var status = await StatusAsync(world, "lead");

        // Assert
        Assert.Empty(status.Reviewers);
        Assert.Equal(NextActionKind.AssignReviewer, status.NextAction.Kind);
        Assert.Equal(NextActionTarget.Reviewers, status.NextAction.Target);
    }

    /// <summary>
    ///     Test that the status of a single-slot review is coherent: its reviewer is asked to hand in the ballot, and
    ///     nobody else gets a dispute or a masked view.
    /// </summary>
    [Fact]
    public async Task ReviewStatus_SingleSlot_IsNotMaskedAndAsksTheReviewer()
    {
        // Arrange
        var world = await BallotMaskingWorld.CreateAsync();
        await world.SeatAsync("ann");
        await world.AnswerAsync("ann");

        // Act
        var status = await StatusAsync(world, "ann");

        // Assert
        Assert.False(status.IsMasked);
        Assert.Single(status.Reviewers);
        Assert.Empty(status.DisputedChecks);
        Assert.Equal(NextActionKind.Sign, status.NextAction.Kind);
    }

    /// <summary>
    ///     Test that the changes since signing list the entries that alter the report, in plain words and in order, and
    ///     leave out draft answers, threads that stay out of the report, signing and slot changes.
    /// </summary>
    [Fact]
    public async Task ReviewStatus_ChangesSinceSigning_ListsDigestAffectingEntriesOnly()
    {
        // Arrange: ann signed and then the review changed in several ways, some of which do not alter the report
        var world = await BallotMaskingWorld.CreateAsync();
        await ArrangeSignedThenChangedAsync(world);
        var item = BallotMaskingWorld.FirstItemId;
        await AppendAsync(world, "bob", "thread.started", $"thread 7 on check {item}; comment 1, text sha256 ab12; in report");
        await AppendAsync(world, "bob", "thread.started", $"thread 8 on check {item}; comment 2, text sha256 cd34");
        await AppendAsync(world, "bob", "thread.replied", "thread 8, comment 3, text sha256 ef56");
        await AppendAsync(world, "bob", "thread.replied", "thread 7, comment 4, text sha256 aa11; in report");
        await AppendAsync(world, "lead", "thread.kind-changed", "thread 8: question -> finding");
        await AppendAsync(world, "lead", "thread.resolved", "thread 8; note sha256 bb22");
        await AppendAsync(world, "lead", "thread.resolved", "thread 9; note sha256 cc33");
        await AppendAsync(world, "lead", "thread.resolved", "thread 7");
        await AppendAsync(world, "lead", "thread.comment-report-changed", "thread 8, comment 3: included in report");
        await AppendAsync(world, "lead", "thread.comment-report-changed", "thread 8, comment 3: taken out of report");
        await AppendAsync(world, "lead", "thread.kind-changed", "thread 8: finding -> question");
        await AppendAsync(world, "lead", "review.conclusion-changed", "length 0 -> 12");
        await AppendAsync(world, "lead", "slot.added", "slot 3");
        await AppendAsync(world, "lead", "review.due-date-changed", "none -> 2025-04-01");

        // Act
        var status = await StatusAsync(world, "ann");

        // Assert
        var signed = Assert.Single(status.ChangesSinceSigning);
        Assert.Equal(1, signed.Number);
        Assert.Equal("ann", signed.SignerName);
        Assert.Equal(
            [
                "Took a ballot back to change it",
                "Handed in a ballot",
                "Started a finding that goes in the report",
                "Added a reply that goes in the report",
                "Turned a question into a finding",
                "Resolved a finding with a note that goes in the report",
                "Put a comment in the report",
                "Took a comment out of the report",
                "Turned a finding into a question",
                "Changed the review conclusion"
            ],
            signed.Changes.Select(c => c.Reason));
        Assert.Equal(
            ["bob (bob)", "bob (bob)", "bob (bob)", "bob (bob)", "lead (lead)", "lead (lead)", "lead (lead)", "lead (lead)", "lead (lead)", "lead (lead)"],
            signed.Changes.Select(c => c.By));
        Assert.True(signed.Changes.All(c => c.At > signed.SignedAt));
        Assert.Equal(SlotSignatureState.OutOfDate, status.Reviewers[0].Signature);
    }

    /// <summary>
    ///     Test that a signature that still matches the review has no list of changes, and neither does a review
    ///     nobody signed.
    /// </summary>
    [Fact]
    public async Task ReviewStatus_ChangesSinceSigning_EmptyWhenSignatureStillMatches()
    {
        // Arrange: ann signed and nothing changed
        var world = await BallotMaskingWorld.CreateAsync();
        var slot = await world.SeatAsync("ann");
        await world.AnswerAsync("ann");
        await world.SubmitAsync("ann");
        var approval = await world.Signing.GetApprovalAsync(
            BallotMaskingWorld.ActorOf("ann"), world.ProjectId, BallotMaskingWorld.ReviewId, Token);
        await world.Signing.SignAsync(
            BallotMaskingWorld.ActorOf("ann"),
            world.ProjectId,
            BallotMaskingWorld.ReviewId,
            slot.Id,
            approval!.Digest,
            BallotMaskingWorld.Password,
            Token);

        // Act
        var status = await StatusAsync(world, "ann");

        // Assert
        Assert.Empty(status.ChangesSinceSigning);
        Assert.Equal(SlotSignatureState.Signed, Assert.Single(status.Reviewers).Signature);
        Assert.False(status.NextAction.IsActionable);
    }

    /// <summary>
    ///     Test that a viewer who has no submitted ballot of their own sees a neutral sentence for a change whose
    ///     details are hidden, and never the details.
    /// </summary>
    [Fact]
    public async Task ReviewStatus_ChangesSinceSigning_MaskedForDraftViewer()
    {
        // Arrange: ann signed; cyd has no submitted ballot, so the details are masked; a check discussion is in the report
        var world = await BallotMaskingWorld.CreateAsync();
        await ArrangeSignedThenChangedAsync(world);
        var item = BallotMaskingWorld.FirstItemId;
        await AppendAsync(world, "ann", "thread.started", $"thread 7 on check {item}; comment 1, text sha256 ab12; in report");
        await AppendAsync(world, "ann", "thread.replied", "thread 7, comment 2, text sha256 cd34; in report");
        await AppendAsync(world, "lead", "review.conclusion-changed", "length 0 -> 12");

        // Act
        var masked = await StatusAsync(world, "cyd");
        var open = await StatusAsync(world, "lead");

        // Assert
        Assert.True(masked.IsMasked);
        var maskedChanges = Assert.Single(masked.ChangesSinceSigning).Changes;
        Assert.Equal(
            [
                "Took a ballot back to change it",
                "Handed in a ballot",
                ReviewChangeRules.HiddenReason,
                ReviewChangeRules.HiddenReason,
                "Changed the review conclusion"
            ],
            maskedChanges.Select(c => c.Reason));
        Assert.DoesNotContain(maskedChanges, c => c.Reason.Contains("check", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("Started a finding that goes in the report", Assert.Single(open.ChangesSinceSigning).Changes.Select(c => c.Reason));
    }

    /// <summary>
    ///     Test that a change after the signature is still listed when the trail is far longer than any fixed limit: the
    ///     whole trail is read, so the newest entry is not dropped.
    /// </summary>
    [Fact]
    public async Task ReviewStatus_ChangesSinceSigning_ReadsPastTwentyThousandEntries()
    {
        // Arrange: ann signed and the review changed; then more than 20,000 entries that alter nothing, and the
        // newest entry of all changes the conclusion
        var world = await BallotMaskingWorld.CreateAsync();
        await ArrangeSignedThenChangedAsync(world);
        var filler = AuditDraft.By(
            BallotMaskingWorld.ActorOf("lead"), "slot.added", "slot 3", world.ProjectId, BallotMaskingWorld.ReviewId);
        for (var i = 0; i < 20_050; i++)
        {
            await world.AuditStore.AppendAsync(filler, Token);
        }

        await AppendAsync(world, "lead", "review.conclusion-changed", "length 0 -> 12");

        // Act
        var status = await StatusAsync(world, "ann");

        // Assert
        var changes = Assert.Single(status.ChangesSinceSigning).Changes;
        Assert.Equal("Changed the review conclusion", changes[^1].Reason);
        Assert.Equal(["Took a ballot back to change it", "Handed in a ballot", "Changed the review conclusion"],
            changes.Select(c => c.Reason));
        Assert.True(world.AuditStore.MatchingReads > 100);
    }

    /// <summary>
    ///     Test that the reviewer who reopened a review is not offered the signing form while the project does not let a
    ///     reopener sign again, and is offered it, with the words for a signature that is out of date, when the project
    ///     lets a reopener sign.
    /// </summary>
    /// <param name="ruleOn">Whether the project forbids the reopener to sign again.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ReviewStatus_Reopener_SignAfterSignatureOutOfDate_DependsOnProjectRule(bool ruleOn)
    {
        // Arrange: padm (with ann as alternate) and bob signed, and padm reopened the review
        var world = await BallotMaskingWorld.CreateAsync();
        var first = await world.Signing.AddSlotAsync(
            BallotMaskingWorld.ActorOf("lead"), world.ProjectId, BallotMaskingWorld.ReviewId, world.Ids["padm"], world.Ids["ann"], Token);
        var second = await world.SeatAsync("bob");
        foreach (var name in new[] { "padm", "bob" })
        {
            await world.AnswerAsync(name);
            await world.SubmitAsync(name);
        }

        await SignAsync(world, "padm", first.Id);
        await SignAsync(world, "bob", second.Id);
        await world.Signing.ReopenReviewAsync(
            BallotMaskingWorld.ActorOf("padm"), world.ProjectId, BallotMaskingWorld.ReviewId, "Look again.", Token);
        if (!ruleOn)
        {
            await TestServices.Projects(world.Store, world.Clock).SetReviewRuleOffAsync(
                world.ProjectId, ReviewRule.NonReopenerSigning, Token);
        }

        // Act
        var reopener = await StatusAsync(world, "padm");
        var other = await StatusAsync(world, "bob");

        // Assert
        Assert.Equal(SlotSignatureState.OutOfDate, reopener.Reviewers[0].Signature);
        Assert.Equal(ruleOn ? NextActionKind.WaitForOthers : NextActionKind.Sign, reopener.NextAction.Kind);
        if (!ruleOn)
        {
            Assert.Equal("Look at the changes and sign again", reopener.NextAction.Label);
        }

        Assert.Equal(NextActionKind.Sign, other.NextAction.Kind);
        Assert.Equal("Look at the changes and sign again", other.NextAction.Label);
    }

    /// <summary>
    ///     Test that a reviewer who reopened a review and holds a slot that was never signed is offered the signing form
    ///     only when the project lets a reopener sign.
    /// </summary>
    /// <param name="ruleOn">Whether the project forbids the reopener to sign again.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ReviewStatus_Reopener_WithoutEarlierSignature_SignDependsOnProjectRule(bool ruleOn)
    {
        // Arrange: ann and bob signed, padm reopened the review and then took a new slot, handed in and has not signed
        var world = await BallotMaskingWorld.CreateAsync();
        var first = await world.SeatAsync("ann");
        var second = await world.SeatAsync("bob");
        foreach (var name in new[] { "ann", "bob" })
        {
            await world.AnswerAsync(name);
            await world.SubmitAsync(name);
        }

        await SignAsync(world, "ann", first.Id);
        await SignAsync(world, "bob", second.Id);
        await world.Signing.ReopenReviewAsync(
            BallotMaskingWorld.ActorOf("padm"), world.ProjectId, BallotMaskingWorld.ReviewId, "Look again.", Token);
        await world.SeatAsync("padm");
        await world.AnswerAsync("padm");
        await world.SubmitAsync("padm");
        if (!ruleOn)
        {
            await TestServices.Projects(world.Store, world.Clock).SetReviewRuleOffAsync(
                world.ProjectId, ReviewRule.NonReopenerSigning, Token);
        }

        // Act
        var reopener = await StatusAsync(world, "padm");

        // Assert
        Assert.Equal(SlotSignatureState.NotSigned, reopener.Reviewers[2].Signature);
        Assert.Equal(ruleOn ? NextActionKind.WaitForOthers : NextActionKind.Sign, reopener.NextAction.Kind);
        if (!ruleOn)
        {
            Assert.Equal("Sign the review", reopener.NextAction.Label);
        }
    }

    /// <summary>
    ///     Test that the status built from the approval, the threads and the capabilities a caller passes in equals the
    ///     status the service reads itself, and that the passed in capabilities are the ones used.
    /// </summary>
    [Fact]
    public async Task ReviewStatus_PassedInValues_AreUsedInsteadOfReread()
    {
        // Arrange: ann says Pass, bob says Fail on the first check
        var world = await BallotMaskingWorld.CreateAsync();
        await world.ArrangeDisputeAsync();
        await world.SeatAsync("cyd");

        // Act / Assert: the same values the service would read give the same status, for every kind of viewer
        foreach (var name in new[] { "ann", "bob", "lead", "cyd" })
        {
            var actor = BallotMaskingWorld.ActorOf(name);
            var read = await StatusAsync(world, name);
            var approval = await world.Signing.GetApprovalAsync(actor, world.ProjectId, BallotMaskingWorld.ReviewId, Token);
            var threads = await world.Threads.ListAsync(actor, world.ProjectId, BallotMaskingWorld.ReviewId, Token);
            var capabilities = await world.Threads.GetCapabilitiesAsync(actor, world.ProjectId, BallotMaskingWorld.ReviewId, Token);
            var passed = await world.Status.GetStatusAsync(
                actor, world.ProjectId, BallotMaskingWorld.ReviewId, approval!, threads, capabilities, Token);
            Assert.NotNull(passed);
            Assert.Equal(Describe(read), Describe(passed));
        }

        // Act / Assert: capabilities that allow nothing are used as given, so ann can no longer discuss the dispute
        var ann = BallotMaskingWorld.ActorOf("ann");
        var annApproval = await world.Signing.GetApprovalAsync(ann, world.ProjectId, BallotMaskingWorld.ReviewId, Token);
        var none = await world.Status.GetStatusAsync(
            ann, world.ProjectId, BallotMaskingWorld.ReviewId, annApproval!, [], ThreadCapabilities.None, Token);
        Assert.NotNull(none);
        Assert.True(Assert.Single((await StatusAsync(world, "ann")).DisputedChecks).CanDiscuss);
        Assert.False(Assert.Single(none.DisputedChecks).CanDiscuss);
    }

    /// <summary>
    ///     Test that the overload with values passed in still refuses a person who may not see the project, and still
    ///     has no status for a review that does not exist.
    /// </summary>
    [Fact]
    public async Task ReviewStatus_PassedInValues_OutsiderIsStillRefused()
    {
        // Arrange
        var world = await BallotMaskingWorld.CreateAsync();
        var lead = BallotMaskingWorld.ActorOf("lead");
        var approval = await world.Signing.GetApprovalAsync(lead, world.ProjectId, BallotMaskingWorld.ReviewId, Token);

        // Act
        var refusal = await Assert.ThrowsAsync<DomainRuleException>(() => world.Status.GetStatusAsync(
            BallotMaskingWorld.ActorOf("out"), world.ProjectId, BallotMaskingWorld.ReviewId, approval!, [], ThreadCapabilities.None, Token));
        var missing = await world.Status.GetStatusAsync(
            lead, world.ProjectId, "nope", approval!, [], ThreadCapabilities.None, Token);

        // Assert
        Assert.Equal(DomainRule.ProjectNotFound, refusal.Rule);
        Assert.Null(missing);
    }

    /// <summary>
    ///     Test that the pure change rules follow the kind of a thread from the entries before the signature, so a note
    ///     on a thread that was a finding then is listed, and that entries before the signature are not.
    /// </summary>
    [Fact]
    public void ReviewStatus_ChangeRules_FollowThreadKindFromEarlierEntries()
    {
        // Arrange: a finding that was started before the signature, then resolved with a note after it
        var at = new DateTimeOffset(2025, 1, 1, 10, 0, 0, TimeSpan.Zero);
        var entries = new List<AuditEntry>
        {
            Entry(1, at, "thread.started", "thread 3 on file a.txt:1; comment 1, text sha256 aa; in report"),
            Entry(2, at.AddMinutes(1), "ballot.submitted", "slot 1"),
            Entry(3, at.AddMinutes(2), "thread.resolved", "thread 3; note sha256 bb"),
            Entry(4, at.AddMinutes(3), "signature.added", "slot 1")
        };

        // Act
        var changes = ReviewChangeRules.Derive(entries, entries, at.AddSeconds(90));

        // Assert
        var change = Assert.Single(changes);
        Assert.Equal("Resolved a finding with a note that goes in the report", change.Reason);
    }

    /// <summary>
    ///     Builds an audit entry for the pure rules.
    /// </summary>
    /// <param name="sequence">The sequence.</param>
    /// <param name="at">The time.</param>
    /// <param name="action">The action.</param>
    /// <param name="detail">The detail.</param>
    /// <returns>The entry.</returns>
    private static AuditEntry Entry(long sequence, DateTimeOffset at, string action, string detail)
    {
        return new AuditEntry
        {
            Sequence = sequence,
            TimestampUtc = at,
            ActorName = "bob",
            Action = action,
            ProjectId = 1,
            ReviewId = "rev-1",
            Detail = detail,
            PreviousHash = AuditChain.GenesisHash
        };
    }
}
