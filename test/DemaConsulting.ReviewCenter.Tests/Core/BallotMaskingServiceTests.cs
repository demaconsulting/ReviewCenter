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
///     Tests that the services hide what the other reviewers answered from a person who has not handed in their own
///     ballot yet, with the real access rules: the ballot view, the checklist state, the approval status and its
///     blockers, the progress overview, the inbox and the check threads.
/// </summary>
public class BallotMaskingServiceTests
{
    /// <summary>
    ///     The cancellation token of the running test.
    /// </summary>
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>
    ///     Test that a reviewer who handed in sees the checks the submitted ballots answer differently, with each
    ///     reviewer's answer in slot order.
    /// </summary>
    [Fact]
    public async Task ChecklistService_GetBallotView_SubmittedViewer_ListsDifferingChecksInSlotOrder()
    {
        // Arrange: ann says Pass and bob says Fail on the first check
        var world = await BallotMaskingWorld.CreateAsync();
        await world.ArrangeDisputeAsync();

        // Act
        var view = await world.Checklists.GetBallotViewAsync(
            BallotMaskingWorld.ActorOf("ann"), world.ProjectId, BallotMaskingWorld.ReviewId, Token);

        // Assert
        Assert.NotNull(view);
        Assert.False(view.IsMasked);
        var answers = Assert.Single(view.Differing).Value;
        Assert.Equal(BallotMaskingWorld.FirstItemId, view.Differing.Keys.Single());
        Assert.Equal(["ann", "bob"], answers.Select(a => a.ReviewerName));
        Assert.Equal([1, 2], answers.Select(a => a.SlotNumber));
        Assert.Equal([ChecklistOutcome.Pass, ChecklistOutcome.Fail], answers.Select(a => a.Outcome));
        Assert.Equal(2, view.BallotsSubmitted);
        Assert.Equal(2, view.BallotsTotal);
    }

    /// <summary>
    ///     Test that a reviewer with a draft and a plain contributor without a slot see no difference, while the number
    ///     of handed-in ballots stays visible.
    /// </summary>
    /// <param name="username">The viewer.</param>
    [Theory]
    [InlineData("bob")]
    [InlineData("cyd")]
    public async Task ChecklistService_GetBallotView_ViewerWithoutSubmittedBallot_HidesDifferences(string username)
    {
        // Arrange: ann handed in; bob has a draft that differs
        var world = await BallotMaskingWorld.CreateAsync();
        await world.ArrangeDisputeAsync(bobSubmitted: false);

        // Act
        var view = await world.Checklists.GetBallotViewAsync(
            BallotMaskingWorld.ActorOf(username), world.ProjectId, BallotMaskingWorld.ReviewId, Token);

        // Assert
        Assert.NotNull(view);
        Assert.True(view.IsMasked);
        Assert.Empty(view.Differing);
        Assert.Equal(1, view.BallotsSubmitted);
        Assert.Equal(2, view.BallotsTotal);
    }

    /// <summary>
    ///     Test that the lead, a project administrator, an Auditor and a system administrator see the differences
    ///     without a ballot of their own.
    /// </summary>
    /// <param name="username">The viewer.</param>
    [Theory]
    [InlineData("lead")]
    [InlineData("padm")]
    [InlineData("aud")]
    [InlineData("root")]
    public async Task ChecklistService_GetBallotView_LeadAdminAuditor_SeeDifferencesWithoutBallot(string username)
    {
        // Arrange
        var world = await BallotMaskingWorld.CreateAsync();
        await world.ArrangeDisputeAsync();

        // Act
        var view = await world.Checklists.GetBallotViewAsync(
            BallotMaskingWorld.ActorOf(username), world.ProjectId, BallotMaskingWorld.ReviewId, Token);

        // Assert
        Assert.NotNull(view);
        Assert.False(view.IsMasked);
        Assert.Single(view.Differing);
    }

    /// <summary>
    ///     Test that only a member without a slot is offered the shortcut to become a reviewer: not a reviewer who
    ///     holds a slot, not the author while independent reviewers are required, not an Auditor, and not a person who
    ///     is no member.
    /// </summary>
    /// <param name="username">The viewer.</param>
    /// <param name="expected">Whether the shortcut is offered.</param>
    [Theory]
    [InlineData("cyd", true)]
    [InlineData("ann", false)]
    [InlineData("lead", false)]
    [InlineData("aud", false)]
    [InlineData("out", false)]
    public async Task ChecklistService_GetBallotView_CanAssignMe_OnlyForMemberWithoutSlot(string username, bool expected)
    {
        // Arrange
        var world = await BallotMaskingWorld.CreateAsync();
        await world.SeatAsync("ann");
        await world.SeatAsync("bob");

        // Act
        var view = await world.Checklists.GetBallotViewAsync(
            BallotMaskingWorld.ActorOf(username), world.ProjectId, BallotMaskingWorld.ReviewId, Token);

        // Assert
        Assert.NotNull(view);
        Assert.Equal(expected, view.CanAssignMe);
    }

    /// <summary>
    ///     Test that "Submit and sign" is offered for a complete draft when nothing blocks signing, not once the ballot
    ///     is in, not for a draft with a Fail the viewer cannot weigh yet, and not in a review with a single slot.
    /// </summary>
    [Fact]
    public async Task ChecklistService_GetBallotView_CompleteDraft_OffersSubmitAndSignOnlyWhenNothingBlocks()
    {
        // Arrange: ann handed in Pass; bob answered Pass in a draft; cyd is seated with a Fail draft
        var world = await BallotMaskingWorld.CreateAsync();
        await world.SeatAsync("ann");
        await world.SeatAsync("bob");
        await world.SeatAsync("cyd");
        await world.AnswerAsync("ann");
        await world.SubmitAsync("ann");
        await world.AnswerAsync("bob");
        await world.AnswerAsync("cyd", ChecklistOutcome.Fail);

        // Act
        var bob = await world.Checklists.GetBallotViewAsync(
            BallotMaskingWorld.ActorOf("bob"), world.ProjectId, BallotMaskingWorld.ReviewId, Token);
        var ann = await world.Checklists.GetBallotViewAsync(
            BallotMaskingWorld.ActorOf("ann"), world.ProjectId, BallotMaskingWorld.ReviewId, Token);
        var cyd = await world.Checklists.GetBallotViewAsync(
            BallotMaskingWorld.ActorOf("cyd"), world.ProjectId, BallotMaskingWorld.ReviewId, Token);

        // Assert
        Assert.True(bob!.CanSubmitAndSign);
        Assert.False(ann!.CanSubmitAndSign);
        Assert.False(cyd!.CanSubmitAndSign);
    }

    /// <summary>
    ///     Test that every viewer who may view the project is told which other reviewers have not handed in, by name and
    ///     in slot order, whether the answers of the others are hidden or not, that the own slot is left out, and that a
    ///     stranger gets no names.
    /// </summary>
    [Fact]
    public async Task ChecklistService_GetBallotView_OtherBallotMissing_NamesThemForViewersOfTheProject()
    {
        // Arrange: ann handed in; bob has a draft; cyd holds no slot; "out" is no member
        var world = await BallotMaskingWorld.CreateAsync();
        await world.ArrangeDisputeAsync(bobSubmitted: false);

        // Act
        var ann = await world.Checklists.GetBallotViewAsync(
            BallotMaskingWorld.ActorOf("ann"), world.ProjectId, BallotMaskingWorld.ReviewId, Token);
        var bob = await world.Checklists.GetBallotViewAsync(
            BallotMaskingWorld.ActorOf("bob"), world.ProjectId, BallotMaskingWorld.ReviewId, Token);
        var cyd = await world.Checklists.GetBallotViewAsync(
            BallotMaskingWorld.ActorOf("cyd"), world.ProjectId, BallotMaskingWorld.ReviewId, Token);
        var stranger = await world.Checklists.GetBallotViewAsync(
            BallotMaskingWorld.ActorOf("out"), world.ProjectId, BallotMaskingWorld.ReviewId, Token);

        // Assert: names only, and never the own slot
        Assert.Equal(["bob"], ann!.MissingNames);
        Assert.Empty(bob!.MissingNames);
        Assert.Equal(["bob"], cyd!.MissingNames);
        Assert.Empty(stranger!.MissingNames);
        Assert.True(bob.IsMasked);
    }

    /// <summary>
    ///     Test that a reviewer who handed in gets the other reviewer's reason and their own slot number, and that the
    ///     number of agreed failed checks is the number of checks all submitted ballots failed.
    /// </summary>
    [Fact]
    public async Task ChecklistService_GetBallotView_SubmittedViewer_GetsReasonsOwnSlotAndFailedCount()
    {
        // Arrange: ann Pass, bob Fail with a reason; then a second world where both fail
        var disputed = await BallotMaskingWorld.CreateAsync();
        await disputed.ArrangeDisputeAsync();
        var agreed = await BallotMaskingWorld.CreateAsync();
        await agreed.SeatAsync("ann");
        await agreed.SeatAsync("bob");
        await agreed.AnswerAsync("ann", ChecklistOutcome.Fail);
        await agreed.SubmitAsync("ann");
        await agreed.AnswerAsync("bob", ChecklistOutcome.Fail);
        await agreed.SubmitAsync("bob");

        // Act
        var ann = await disputed.Checklists.GetBallotViewAsync(
            BallotMaskingWorld.ActorOf("ann"), disputed.ProjectId, BallotMaskingWorld.ReviewId, Token);
        var bothFailed = await agreed.Checklists.GetBallotViewAsync(
            BallotMaskingWorld.ActorOf("ann"), agreed.ProjectId, BallotMaskingWorld.ReviewId, Token);

        // Assert
        var answers = Assert.Single(ann!.Differing).Value;
        Assert.Null(answers[0].Justification);
        Assert.Equal("A reason", answers[1].Justification);
        Assert.Equal(1, ann.OwnSlotNumber);
        Assert.Equal(0, ann.FailedChecks);
        Assert.Empty(ann.MissingNames);
        Assert.Equal(1, bothFailed!.FailedChecks);
        Assert.Empty(bothFailed.Differing);
    }

    /// <summary>
    ///     Test that a viewer who has not handed in, an API token, a member without a slot and a stranger learn no reason
    ///     of another reviewer, no disputed check and no failed check from the view.
    /// </summary>
    [Fact]
    public async Task ChecklistService_GetBallotView_HiddenViewers_LearnNoReasonDisputeOrFailedCheck()
    {
        // Arrange: ann failed the first check with a reason and handed in; bob answered Fail too, as a draft
        var world = await BallotMaskingWorld.CreateAsync();
        await world.SeatAsync("ann");
        await world.SeatAsync("bob");
        await world.AnswerAsync("ann", ChecklistOutcome.Fail);
        await world.SubmitAsync("ann");
        await world.AnswerAsync("bob", ChecklistOutcome.Fail);
        var viewers = new[]
        {
            BallotMaskingWorld.ActorOf("bob"),
            BallotMaskingWorld.ActorOf("cyd"),
            new Actor("cyd", "cyd") { ApiTokenId = "tok-1" },
            BallotMaskingWorld.ActorOf("out")
        };

        foreach (var viewer in viewers)
        {
            // Act
            var view = await world.Checklists.GetBallotViewAsync(viewer, world.ProjectId, BallotMaskingWorld.ReviewId, Token);

            // Assert: nothing about the answers of ann, whoever looks
            Assert.NotNull(view);
            Assert.Empty(view.Differing);
            Assert.Equal(0, view.FailedChecks);
            Assert.DoesNotContain("A reason", System.Text.Json.JsonSerializer.Serialize(view), StringComparison.Ordinal);
        }
    }

    /// <summary>
    ///     Test that an API token whose owner has handed in is still masked in the ballot view: it learns no reason, no
    ///     disputed check and no failed check, although it may read the consolidated standing through the API read.
    /// </summary>
    [Fact]
    public async Task ChecklistService_GetBallotView_SubmittedApiToken_LearnsNoReasonDisputeOrFailedCheck()
    {
        // Arrange: ann Pass and bob Fail with a reason, both handed in
        var world = await BallotMaskingWorld.CreateAsync();
        await world.ArrangeDisputeAsync();
        var token = new Actor("ann", "ann") { ApiTokenId = "tok-1" };

        // Act: read the ballot view as the token of ann, who has handed in, and the consolidated standing
        var view = await world.Checklists.GetBallotViewAsync(token, world.ProjectId, BallotMaskingWorld.ReviewId, Token);
        var consolidated = await world.Checklists.GetConsolidatedAsync(token, world.ProjectId, BallotMaskingWorld.ReviewId, Token);

        // Assert: the view is masked and holds nothing of the answers; the consolidated read is not masked for the token
        Assert.NotNull(view);
        Assert.NotNull(consolidated);
        Assert.Multiple(
            () => Assert.True(view.IsMasked),
            () => Assert.Empty(view.Differing),
            () => Assert.Equal(0, view.FailedChecks),
            () => Assert.DoesNotContain("A reason", System.Text.Json.JsonSerializer.Serialize(view), StringComparison.Ordinal),
            () => Assert.False(consolidated.IsMasked));
    }

    /// <summary>
    ///     Test that a stranger gets no differing answers and no failed count even where the ballots are open to every
    ///     viewer (a review with a single slot).
    /// </summary>
    [Fact]
    public async Task ChecklistService_GetBallotView_StrangerOnSingleSlotReview_LearnsNothingOfTheAnswers()
    {
        // Arrange: ann is the only reviewer and failed the first check
        var world = await BallotMaskingWorld.CreateAsync();
        await world.SeatAsync("ann");
        await world.AnswerAsync("ann", ChecklistOutcome.Fail);

        // Act
        var ann = await world.Checklists.GetBallotViewAsync(
            BallotMaskingWorld.ActorOf("ann"), world.ProjectId, BallotMaskingWorld.ReviewId, Token);
        var stranger = await world.Checklists.GetBallotViewAsync(
            BallotMaskingWorld.ActorOf("out"), world.ProjectId, BallotMaskingWorld.ReviewId, Token);

        // Assert
        Assert.Equal(1, ann!.FailedChecks);
        Assert.Equal(0, stranger!.FailedChecks);
        Assert.Empty(stranger.Differing);
    }

    /// <summary>
    ///     Test that a review without a checklist has no ballot view.
    /// </summary>
    [Fact]
    public async Task ChecklistService_GetBallotView_UnknownReview_ReturnsNull()
    {
        // Arrange
        var world = await BallotMaskingWorld.CreateAsync();

        // Act
        var view = await world.Checklists.GetBallotViewAsync(
            BallotMaskingWorld.ActorOf("ann"), world.ProjectId, "nope", Token);

        // Assert
        Assert.Null(view);
    }

    /// <summary>
    ///     Test that a plain contributor without a slot gets an empty masked checklist state, and the lead gets the
    ///     standing of the ballots.
    /// </summary>
    [Fact]
    public async Task ChecklistService_GetAsync_ViewerWithoutSlotAndBallot_ReturnsMaskedState()
    {
        // Arrange
        var world = await BallotMaskingWorld.CreateAsync();
        await world.ArrangeDisputeAsync();

        // Act
        var masked = await world.Checklists.GetAsync(world.ProjectId, BallotMaskingWorld.ReviewId, Token, "cyd");
        var lead = await world.Checklists.GetAsync(world.ProjectId, BallotMaskingWorld.ReviewId, Token, "lead");

        // Assert
        Assert.True(masked!.IsMasked);
        Assert.Null(masked.Consolidation);
        Assert.Null(masked.AnswerFor(BallotMaskingWorld.FirstItemId));
        Assert.False(lead!.IsMasked);
        Assert.NotNull(lead.Consolidation);
    }

    /// <summary>
    ///     Test that a masked viewer's approval status names no dispute, no outcome and no failed check, and says that
    ///     details are hidden, while the lead and a reviewer who handed in see the dispute.
    /// </summary>
    [Fact]
    public async Task SigningService_GetApproval_MaskedViewer_ShowsNoDisputeCountOrOutcome()
    {
        // Arrange
        var world = await BallotMaskingWorld.CreateAsync();
        await world.ArrangeDisputeAsync();

        // Act
        var masked = await world.Signing.GetApprovalAsync(
            BallotMaskingWorld.ActorOf("cyd"), world.ProjectId, BallotMaskingWorld.ReviewId, Token);
        var reviewer = await world.Signing.GetApprovalAsync(
            BallotMaskingWorld.ActorOf("ann"), world.ProjectId, BallotMaskingWorld.ReviewId, Token);
        var lead = await world.Signing.GetApprovalAsync(
            BallotMaskingWorld.ActorOf("lead"), world.ProjectId, BallotMaskingWorld.ReviewId, Token);

        // Assert
        Assert.True(masked!.IsMasked);
        Assert.Equal(0, masked.DisputedChecks);
        Assert.Equal(0, masked.FailedChecks);
        Assert.Null(masked.Outcome);
        Assert.Contains(ReviewApproval.HiddenNotice, masked.Blockers);
        Assert.DoesNotContain(masked.Blockers, b => b.Contains("disputed", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("disputed", masked.NextStep, StringComparison.OrdinalIgnoreCase);
        Assert.False(reviewer!.IsMasked);
        Assert.Equal(1, reviewer.DisputedChecks);
        Assert.Contains(reviewer.Blockers, b => b.Contains("disputed", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(1, lead!.DisputedChecks);
    }

    /// <summary>
    ///     Test that the failed check identifiers, the rejection note and the next step never name a provisional
    ///     rejection to a viewer who has not handed in a ballot (a member without one, a reviewer with a draft, a user
    ///     without membership, an unknown viewer and an API token of the lead or of a member), through the single
    ///     and the batch read, while the lead and a reviewer who handed in see them.
    /// </summary>
    [Fact]
    public async Task SigningService_GetApproval_MaskedViewersAndTokens_NeverSeeFailedCheckIdsOrRejectionNote()
    {
        // Arrange: bob handed in a Fail on the first check, ann answered Fail but has only a draft
        var world = await BallotMaskingWorld.CreateAsync();
        await world.SeatAsync("ann");
        await world.SeatAsync("bob");
        await world.AnswerAsync("ann", ChecklistOutcome.Fail);
        await world.AnswerAsync("bob", ChecklistOutcome.Fail);
        await world.SubmitAsync("bob");
        var id = BallotMaskingWorld.FirstItemId;
        var hidden = new List<(string Name, Actor? Viewer)>
        {
            ("member without ballot", BallotMaskingWorld.ActorOf("cyd")),
            ("reviewer with a draft", BallotMaskingWorld.ActorOf("ann")),
            ("user without membership", BallotMaskingWorld.ActorOf("out")),
            ("unknown viewer", null),
            ("token of the lead", new Actor("lead", "lead") { ApiTokenId = "tok-1" }),
            ("token of a member", new Actor("cyd", "cyd") { ApiTokenId = "tok-2" })
        };

        // Act
        var lead = await world.Signing.GetApprovalAsync(BallotMaskingWorld.ActorOf("lead"), world.ProjectId, BallotMaskingWorld.ReviewId, Token);
        var reviewer = await world.Signing.GetApprovalAsync(BallotMaskingWorld.ActorOf("bob"), world.ProjectId, BallotMaskingWorld.ReviewId, Token);

        // Assert: the lead and the reviewer who handed in see it, worded for the ballot that is still out
        var note = $"The reviewers who handed in a ballot failed {id}, so this review will be Rejected.";
        Assert.Equal([id], lead!.FailedCheckIds);
        Assert.Equal(note, lead.RejectionNote);
        Assert.Equal(note, reviewer!.RejectionNote);
        foreach (var (name, viewer) in hidden)
        {
            var single = await world.Signing.GetApprovalAsync(viewer, world.ProjectId, BallotMaskingWorld.ReviewId, Token);
            Assert.True(single!.IsMasked, name);
            AssertNoRejection(single, id, name);
            if (viewer is not null)
            {
                var batch = (await world.Signing.GetApprovalsAsync(viewer, world.ProjectId, [BallotMaskingWorld.ReviewId], Token))[BallotMaskingWorld.ReviewId];
                AssertNoRejection(batch, id, name + " (batch)");
            }
        }
    }

    /// <summary>
    ///     Asserts that a status carries nothing about a failed check or a provisional rejection.
    /// </summary>
    /// <param name="approval">The status of the viewer.</param>
    /// <param name="itemId">The identifier of the failed check.</param>
    /// <param name="who">Who the viewer is, for the failure message.</param>
    private static void AssertNoRejection(ReviewApproval approval, string itemId, string who)
    {
        Assert.True(approval.FailedCheckIds is null or { Count: 0 }, who);
        Assert.Equal(0, approval.FailedChecks);
        Assert.Null(approval.RejectionNote);
        Assert.Null(approval.Outcome);
        Assert.DoesNotContain(itemId, approval.NextStep, StringComparison.Ordinal);
        Assert.DoesNotContain("Rejected", approval.NextStep, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("failed", approval.NextStep, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(approval.Blockers, b => b.Contains(itemId, StringComparison.Ordinal));
        Assert.StartsWith("Hand in your ballot to see the other reviewers' answers.", approval.NextStep, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a viewer who has not handed in a ballot gets the signature-aware next step as progress only: the
    ///     masked next step is the same whether or not another reviewer has signed.
    /// </summary>
    [Fact]
    public async Task SigningService_GetApproval_MaskedViewer_NextStepDoesNotDependOnSignatures()
    {
        // Arrange: ann and bob agree on Pass and have handed in; cyd looks, then ann signs
        var world = await BallotMaskingWorld.CreateAsync();
        await world.SeatAsync("ann");
        await world.SeatAsync("bob");
        await world.SeatAsync("cyd");
        await world.AnswerAsync("ann");
        await world.SubmitAsync("ann");
        await world.AnswerAsync("bob");
        await world.SubmitAsync("bob");
        var before = await world.Signing.GetApprovalAsync(
            BallotMaskingWorld.ActorOf("cyd"), world.ProjectId, BallotMaskingWorld.ReviewId, Token);

        // Act: ann signs; cyd looks again
        await world.Signing.SignAsync(
            BallotMaskingWorld.ActorOf("ann"), world.ProjectId, BallotMaskingWorld.ReviewId,
            (await world.Signing.GetApprovalAsync(world.ProjectId, BallotMaskingWorld.ReviewId, Token))!.Slots[0].SlotId,
            (await world.Signing.GetApprovalAsync(world.ProjectId, BallotMaskingWorld.ReviewId, Token))!.Digest,
            BallotMaskingWorld.Password, Token);
        var after = await world.Signing.GetApprovalAsync(
            BallotMaskingWorld.ActorOf("cyd"), world.ProjectId, BallotMaskingWorld.ReviewId, Token);

        // Assert
        Assert.True(before!.IsMasked);
        Assert.True(after!.IsMasked);
        Assert.Equal(before.NextStep, after.NextStep);
    }

    /// <summary>
    ///     Test that a viewer who is not known is masked in a review that has to hide its standing (fail closed), and
    ///     that a review with a single slot is never masked.
    /// </summary>
    [Fact]
    public async Task SigningService_GetApproval_NullViewerAndSingleSlot_FailsClosedOnlyForSeveralSlots()
    {
        // Arrange: one review with a single slot, one with two
        var world = await BallotMaskingWorld.CreateAsync();
        await world.SeatAsync("ann");
        var single = await world.Signing.GetApprovalAsync(null, world.ProjectId, BallotMaskingWorld.ReviewId, Token);
        await world.SeatAsync("bob");

        // Act
        var several = await world.Signing.GetApprovalAsync(null, world.ProjectId, BallotMaskingWorld.ReviewId, Token);

        // Assert
        Assert.False(single!.IsMasked);
        Assert.True(several!.IsMasked);
    }

    /// <summary>
    ///     Test that the batch read masks exactly as the single read does.
    /// </summary>
    [Fact]
    public async Task SigningService_GetApprovals_MaskedViewer_MatchesSingleRead()
    {
        // Arrange
        var world = await BallotMaskingWorld.CreateAsync();
        await world.ArrangeDisputeAsync();

        // Act
        var many = await world.Signing.GetApprovalsAsync(
            BallotMaskingWorld.ActorOf("cyd"), world.ProjectId, [BallotMaskingWorld.ReviewId], Token);
        var single = await world.Signing.GetApprovalAsync(
            BallotMaskingWorld.ActorOf("cyd"), world.ProjectId, BallotMaskingWorld.ReviewId, Token);
        var lead = await world.Signing.GetApprovalsAsync(
            BallotMaskingWorld.ActorOf("lead"), world.ProjectId, [BallotMaskingWorld.ReviewId], Token);

        // Assert
        Assert.Equal(single!.Blockers, many[BallotMaskingWorld.ReviewId].Blockers);
        Assert.True(many[BallotMaskingWorld.ReviewId].IsMasked);
        Assert.False(lead[BallotMaskingWorld.ReviewId].IsMasked);
    }

    /// <summary>
    ///     Test that the batch read looks the viewer's role up a constant number of times, however many reviews it
    ///     covers, that the creator of a review sees all of it, and that an API token of the creator does not.
    /// </summary>
    [Fact]
    public async Task SigningService_GetApprovals_ManyReviews_LooksRoleUpOncePerBatch()
    {
        // Arrange: three reviews of the lead, each with two slots, so each one hides the answers from a viewer
        var world = await BallotMaskingWorld.CreateAsync();
        await world.SeatAsync("ann");
        await world.SeatAsync("bob");
        var ids = new List<string> { BallotMaskingWorld.ReviewId };
        foreach (var id in new[] { "rev-2", "rev-3" })
        {
            await world.Reviews.CreateDraftAsync(
                world.ProjectId, id, id, [new UploadedFile("a.txt", [1, 2, 3])], BallotMaskingWorld.ActorOf("lead"), Token);
            foreach (var user in new[] { "ann", "bob" })
            {
                await world.Signing.AddSlotAsync(BallotMaskingWorld.ActorOf("lead"), world.ProjectId, id, world.Ids[user], null, Token);
            }

            ids.Add(id);
        }

        // Act
        var before = world.Store.MembershipReads;
        var one = await world.Signing.GetApprovalsAsync(BallotMaskingWorld.ActorOf("lead"), world.ProjectId, ids.Take(1).ToList(), Token);
        var readsForOne = world.Store.MembershipReads - before;
        before = world.Store.MembershipReads;
        var three = await world.Signing.GetApprovalsAsync(BallotMaskingWorld.ActorOf("lead"), world.ProjectId, ids, Token);
        var readsForThree = world.Store.MembershipReads - before;
        var token = new Actor("lead", "lead") { ApiTokenId = "tok-1" };
        var viaToken = await world.Signing.GetApprovalsAsync(token, world.ProjectId, ids, Token);

        // Assert
        Assert.Equal(readsForOne, readsForThree);
        Assert.Single(one);
        Assert.Equal(3, three.Count);
        Assert.All(three.Values, approval => Assert.False(approval.IsMasked));
        Assert.All(viaToken.Values, approval => Assert.True(approval.IsMasked));
    }

    /// <summary>
    ///     Test that the progress overview gives no count of disputed or failed checks to a masked viewer, and gives it
    ///     to a reviewer who handed in.
    /// </summary>
    [Fact]
    public async Task ReviewProgressService_GetProgress_MaskedViewer_HidesDisputedAndFailedCounts()
    {
        // Arrange
        var world = await BallotMaskingWorld.CreateAsync();
        await world.ArrangeDisputeAsync();

        // Act
        var masked = (await world.Progress.GetProgressAsync(
            BallotMaskingWorld.ActorOf("cyd"), world.ProjectId, Token)).Single();
        var reviewer = (await world.Progress.GetProgressAsync(
            BallotMaskingWorld.ActorOf("ann"), world.ProjectId, Token)).Single();

        // Assert
        Assert.Equal(0, masked.DisputedChecks);
        Assert.Equal(0, masked.FailedChecks);
        Assert.Equal(1, reviewer.DisputedChecks);
    }

    /// <summary>
    ///     Test that the progress overview gives a viewer who has not handed in the number of ballots that are in and
    ///     the names of the people who have not handed in, with no dispute count, and marks the progress as masked.
    /// </summary>
    [Fact]
    public async Task ReviewProgressService_GetProgress_MaskedViewer_ShowsBallotProgressAndIsMasked()
    {
        // Arrange: ann and bob handed in and differ; cyd holds a slot and has not handed in
        var world = await BallotMaskingWorld.CreateAsync();
        await world.ArrangeDisputeAsync();
        await world.SeatAsync("cyd");

        // Act
        var masked = (await world.Progress.GetProgressAsync(
            BallotMaskingWorld.ActorOf("cyd"), world.ProjectId, Token)).Single();
        var reviewer = (await world.Progress.GetProgressAsync(
            BallotMaskingWorld.ActorOf("ann"), world.ProjectId, Token)).Single();

        // Assert: the ballot progress is the same for both, the dispute only for the one who handed in
        Assert.True(masked.IsMasked);
        Assert.False(reviewer.IsMasked);
        Assert.Equal((2, 3), (masked.BallotsSubmitted, masked.BallotsRequired));
        Assert.Equal(masked.WaitingOnBallots, reviewer.WaitingOnBallots);
        Assert.Single(masked.WaitingOnBallots!);
        Assert.Equal(0, masked.DisputedChecks);
        Assert.Equal(1, reviewer.DisputedChecks);
    }

    /// <summary>
    ///     Test that the section of the inbox row of a reviewer is the one the next action on the status page of the
    ///     review names, for a reviewer with nothing to answer, a draft ballot, nothing blocking, a dispute and a
    ///     signature that is in.
    /// </summary>
    /// <param name="scenario">The arrangement of the review.</param>
    [Theory]
    [InlineData("untouched")]
    [InlineData("twoSubmitted")]
    [InlineData("dispute")]
    [InlineData("annSigned")]
    public async Task InboxService_GetInboxAsync_SectionMatchesReviewStatusNextAction_ForAllStandings(string scenario)
    {
        // Arrange
        var world = await BallotMaskingWorld.CreateAsync();
        await world.SeatAsync("ann");
        await world.SeatAsync("bob");
        await world.SeatAsync("cyd");
        if (scenario != "untouched")
        {
            await world.AnswerAsync("ann");
            await world.SubmitAsync("ann");
            await world.AnswerAsync("bob", scenario == "dispute" ? ChecklistOutcome.Fail : ChecklistOutcome.Pass);
            await world.SubmitAsync("bob");
        }

        if (scenario == "annSigned")
        {
            var approval = (await world.Signing.GetApprovalAsync(world.ProjectId, BallotMaskingWorld.ReviewId, Token))!;
            await world.Signing.SignAsync(
                BallotMaskingWorld.ActorOf("ann"),
                world.ProjectId,
                BallotMaskingWorld.ReviewId,
                approval.Slots[0].SlotId,
                approval.Digest,
                BallotMaskingWorld.Password,
                Token);
        }

        // Act, Assert: for every reviewer the single standing row is the one of the next action
        var standing = new[]
        {
            InboxSection.ToAnswer, InboxSection.Disputed, InboxSection.ToSign, InboxSection.WaitingForOthers
        };
        foreach (var user in new[] { "ann", "bob", "cyd" })
        {
            var actor = BallotMaskingWorld.ActorOf(user);
            var status = (await world.Status.GetStatusAsync(actor, world.ProjectId, BallotMaskingWorld.ReviewId, Token))!;
            var expected = status.NextAction.Kind switch
            {
                NextActionKind.AnswerChecks or NextActionKind.SubmitBallot or NextActionKind.SubmitAndSign
                    => InboxSection.ToAnswer,
                NextActionKind.DiscussDisputes => InboxSection.Disputed,
                NextActionKind.Sign => InboxSection.ToSign,
                NextActionKind.WaitForOthers => InboxSection.WaitingForOthers,
                _ => throw new InvalidOperationException("Unexpected next action " + status.NextAction.Kind)
            };

            var inbox = await world.Inbox.GetInboxAsync(actor, Token);
            var sections = inbox.Items.Select(item => item.Section).Where(standing.Contains).ToList();
            Assert.Equal([expected], sections);
        }
    }

    /// <summary>
    ///     Test that the inbox of a reviewer who has not handed in shows no row about disputed or failed checks, and
    ///     that a reviewer who handed in still gets the replies waiting.
    /// </summary>
    [Fact]
    public async Task InboxService_GetInbox_MaskedViewer_ShowsNoDisputeOrFailureText()
    {
        // Arrange
        var world = await BallotMaskingWorld.CreateAsync();
        await world.ArrangeDisputeAsync(bobSubmitted: false);

        // Act
        var bob = await world.Inbox.GetInboxAsync(BallotMaskingWorld.ActorOf("bob"), Token);

        // Assert: bob has a row (to sign), and none of the rows tells about answers
        Assert.NotEmpty(bob.Items);
        Assert.All(bob.Items, item =>
        {
            Assert.DoesNotContain("dispute", item.Chip + item.Detail, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("failed", item.Chip + item.Detail, StringComparison.OrdinalIgnoreCase);
        });
    }

    /// <summary>
    ///     Test that an API token of the creator of a review never gets the creator's full view of the inbox: the
    ///     failed-checks row is shown to the creator signed in with a password and not to the same user's token.
    /// </summary>
    [Fact]
    public async Task InboxService_GetInbox_CreatorViaApiToken_IsMaskedLikeAnyViewer()
    {
        // Arrange: ann and bob handed in the same Fail, so the creator has a failed-checks row
        var world = await BallotMaskingWorld.CreateAsync();
        await world.SeatAsync("ann");
        await world.SeatAsync("bob");
        foreach (var user in new[] { "ann", "bob" })
        {
            await world.AnswerAsync(user, ChecklistOutcome.Fail);
            await world.SubmitAsync(user);
        }

        var token = new Actor("lead", "lead") { ApiTokenId = "tok-1" };

        // Act
        var person = await world.Inbox.GetInboxAsync(BallotMaskingWorld.ActorOf("lead"), Token);
        var viaToken = await world.Inbox.GetInboxAsync(token, Token);

        // Assert
        Assert.Contains(person.Items, item => item.Section == InboxSection.FailedChecks);
        Assert.DoesNotContain(viaToken.Items, item => item.Section == InboxSection.FailedChecks);
    }

    /// <summary>
    ///     Test that a discussion about a check does not make the To answer row of a reviewer who has not handed in
    ///     unread.
    /// </summary>
    [Fact]
    public async Task InboxService_GetInbox_HiddenCheckThread_DoesNotMakeRowUnreadForMaskedViewer()
    {
        // Arrange: ann handed in, bob has a draft; both have seen the review, then the lead starts a check discussion
        var world = await BallotMaskingWorld.CreateAsync();
        await world.ArrangeDisputeAsync(bobSubmitted: false);
        await world.Inbox.MarkSeenAsync(BallotMaskingWorld.ActorOf("bob"), world.ProjectId, BallotMaskingWorld.ReviewId, 0, null, Token);
        await world.Inbox.MarkSeenAsync(BallotMaskingWorld.ActorOf("ann"), world.ProjectId, BallotMaskingWorld.ReviewId, 0, null, Token);
        await world.StartCheckThreadAsync("lead");

        // Act
        var bob = await world.Inbox.GetInboxAsync(BallotMaskingWorld.ActorOf("bob"), Token);
        var ann = await world.Inbox.GetInboxAsync(BallotMaskingWorld.ActorOf("ann"), Token);

        // Assert: the masked reviewer's To answer row stays read; the reviewer who handed in waits for bob
        Assert.False(bob.Items.Single(item => item.Section == InboxSection.ToAnswer).IsUnread);
        Assert.DoesNotContain(bob.Items, item => item.Section == InboxSection.ToSign);
        Assert.Single(ann.Items, item => item.Section == InboxSection.WaitingForOthers);
    }

    /// <summary>
    ///     Test that the consolidated read of an API token is masked in a review with several slots that is not
    ///     concluded, even for a token whose owner has not handed in, and is not masked for a person who handed in.
    /// </summary>
    [Fact]
    public async Task ChecklistService_GetConsolidatedAsync_TokenViewer_IsMasked()
    {
        // Arrange
        var world = await BallotMaskingWorld.CreateAsync();
        await world.ArrangeDisputeAsync();
        var token = new Actor("cyd", "cyd") { ApiTokenId = "tok-1" };

        // Act
        var viaToken = await world.Checklists.GetConsolidatedAsync(token, world.ProjectId, BallotMaskingWorld.ReviewId, Token);

        // Assert: no answers and no consolidation, so no failed check can be counted
        Assert.True(viaToken!.IsMasked);
        Assert.Null(viaToken.Consolidation);
        Assert.Null(viaToken.AnswerFor(BallotMaskingWorld.FirstItemId));
    }

    /// <summary>
    ///     Test that a token of the lead of the review, an administrator or an auditor is masked like any other token,
    ///     because a token is never the lead, an administrator or an auditor, while the same people signed in are not.
    /// </summary>
    /// <param name="username">The owner of the token.</param>
    [Theory]
    [InlineData("lead")]
    [InlineData("padm")]
    [InlineData("aud")]
    [InlineData("root")]
    public async Task ChecklistService_GetConsolidatedAsync_LeadAdminAuditorViaToken_IsMasked(string username)
    {
        // Arrange
        var world = await BallotMaskingWorld.CreateAsync();
        await world.ArrangeDisputeAsync();
        var token = new Actor(username, username) { ApiTokenId = "tok-1" };

        // Act
        var viaToken = await world.Checklists.GetConsolidatedAsync(token, world.ProjectId, BallotMaskingWorld.ReviewId, Token);
        var person = await world.Checklists.GetConsolidatedAsync(
            BallotMaskingWorld.ActorOf(username), world.ProjectId, BallotMaskingWorld.ReviewId, Token);

        // Assert
        Assert.True(viaToken!.IsMasked);
        Assert.False(person!.IsMasked);
        Assert.NotNull(person.Consolidation);
    }

    /// <summary>
    ///     Test that a token of a person who handed in a ballot sees the consolidated standing of the submitted ballots,
    ///     and not the own ballot of that person (a draft answer is never shown).
    /// </summary>
    [Fact]
    public async Task ChecklistService_GetConsolidatedAsync_SubmittedSlotOwner_SeesConsolidatedNotOwnBallot()
    {
        // Arrange: ann and bob handed in and differ; cyd holds a slot and has a draft with a Fail on the second check
        var world = await BallotMaskingWorld.CreateAsync();
        await world.ArrangeDisputeAsync();
        await world.SeatAsync("cyd");
        var second = DefaultChecklist.Current.Sections.SelectMany(section => section.Items).ElementAt(1).Id;
        await world.Checklists.SetAnswerAsync(
            world.ProjectId,
            BallotMaskingWorld.ReviewId,
            second,
            ChecklistOutcome.Fail,
            "A draft reason",
            BallotMaskingWorld.ActorOf("cyd"),
            Token);
        var token = new Actor("ann", "ann") { ApiTokenId = "tok-1" };

        // Act
        var viaToken = await world.Checklists.GetConsolidatedAsync(token, world.ProjectId, BallotMaskingWorld.ReviewId, Token);
        var cydToken = new Actor("cyd", "cyd") { ApiTokenId = "tok-2" };
        var draftOwner = await world.Checklists.GetConsolidatedAsync(cydToken, world.ProjectId, BallotMaskingWorld.ReviewId, Token);

        // Assert: ann sees the consolidated standing (the first check is disputed) and not cyd's draft; cyd has not
        // handed in, so cyd's token is masked
        Assert.False(viaToken!.IsMasked);
        Assert.NotNull(viaToken.Consolidation);
        Assert.NotEqual((ChecklistOutcome?)ChecklistOutcome.Fail, viaToken.AnswerFor(second)?.Outcome);
        Assert.True(draftOwner!.IsMasked);
    }

    /// <summary>
    ///     Test that the consolidated read of a token shows the standing of a concluded review.
    /// </summary>
    [Fact]
    public async Task ChecklistService_GetConsolidatedAsync_ConcludedReview_Visible()
    {
        // Arrange: ann and bob hand in the same answers and both sign
        var world = await BallotMaskingWorld.CreateAsync();
        await world.SeatAsync("ann");
        await world.SeatAsync("bob");
        foreach (var user in new[] { "ann", "bob" })
        {
            await world.AnswerAsync(user);
            await world.SubmitAsync(user);
        }

        var approval = (await world.Signing.GetApprovalAsync(world.ProjectId, BallotMaskingWorld.ReviewId, Token))!;
        foreach (var slot in approval.Slots)
        {
            var user = slot.Primary.Username;
            await world.Signing.SignAsync(
                BallotMaskingWorld.ActorOf(user),
                world.ProjectId,
                BallotMaskingWorld.ReviewId,
                slot.SlotId,
                approval.Digest,
                BallotMaskingWorld.Password,
                Token);
        }

        var token = new Actor("cyd", "cyd") { ApiTokenId = "tok-1" };

        // Act
        var viaToken = await world.Checklists.GetConsolidatedAsync(token, world.ProjectId, BallotMaskingWorld.ReviewId, Token);

        // Assert
        Assert.False(viaToken!.IsMasked);
        Assert.Equal(ChecklistOutcome.Pass, viaToken.AnswerFor(BallotMaskingWorld.FirstItemId)!.Outcome);
    }

    /// <summary>
    ///     Test that the consolidated read of a token shows the standing of a review with a single slot.
    /// </summary>
    [Fact]
    public async Task ChecklistService_GetConsolidatedAsync_SingleSlot_Visible()
    {
        // Arrange
        var world = await BallotMaskingWorld.CreateAsync();
        await world.SeatAsync("ann");
        await world.AnswerAsync("ann", ChecklistOutcome.Fail);
        var token = new Actor("cyd", "cyd") { ApiTokenId = "tok-1" };

        // Act
        var viaToken = await world.Checklists.GetConsolidatedAsync(token, world.ProjectId, BallotMaskingWorld.ReviewId, Token);

        // Assert
        Assert.False(viaToken!.IsMasked);
        Assert.Equal(ChecklistOutcome.Fail, viaToken.AnswerFor(BallotMaskingWorld.FirstItemId)!.Outcome);
    }

    /// <summary>
    ///     Test that a read without a viewer is refused instead of being treated as a system read.
    /// </summary>
    [Fact]
    public async Task ChecklistService_GetConsolidatedAsync_NullViewer_Throws()
    {
        // Arrange
        var world = await BallotMaskingWorld.CreateAsync();

        // Act, Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => world.Checklists.GetConsolidatedAsync(null!, world.ProjectId, BallotMaskingWorld.ReviewId, Token));
    }

    /// <summary>
    ///     Test that a review that does not exist gives nothing, and that a viewer who may not see the project is
    ///     refused as for every other read.
    /// </summary>
    [Fact]
    public async Task ChecklistService_GetConsolidatedAsync_UnknownReviewAndStranger_ReturnsNullAndRefuses()
    {
        // Arrange
        var world = await BallotMaskingWorld.CreateAsync();
        var member = new Actor("ann", "ann") { ApiTokenId = "tok-1" };
        var stranger = new Actor("out", "out") { ApiTokenId = "tok-2" };

        // Act
        var unknown = await world.Checklists.GetConsolidatedAsync(member, world.ProjectId, "no-such-review", Token);
        var refused = await Assert.ThrowsAsync<DomainRuleException>(
            () => world.Checklists.GetConsolidatedAsync(stranger, world.ProjectId, BallotMaskingWorld.ReviewId, Token));

        // Assert
        Assert.Null(unknown);
        Assert.Equal(DomainRule.ProjectNotFound, refused.Rule);
    }

    /// <summary>
    ///     Test that the progress overview masks an API token like any viewer who is not the lead, an administrator or
    ///     an auditor: it gives no count of disputed or failed checks and says that it is masked, while the progress of
    ///     the ballots is shown.
    /// </summary>
    [Fact]
    public async Task ReviewProgressService_GetProgress_TokenActor_IsMaskedLikeAnyViewer()
    {
        // Arrange: ann and bob handed in and differ
        var world = await BallotMaskingWorld.CreateAsync();
        await world.ArrangeDisputeAsync();
        var token = new Actor("lead", "lead") { ApiTokenId = "tok-1" };

        // Act
        var viaToken = (await world.Progress.GetProgressAsync(token, world.ProjectId, Token)).Single();
        var person = (await world.Progress.GetProgressAsync(
            BallotMaskingWorld.ActorOf("lead"), world.ProjectId, Token)).Single();

        // Assert
        Assert.True(viaToken.IsMasked);
        Assert.Equal(0, viaToken.DisputedChecks);
        Assert.Equal(0, viaToken.FailedChecks);
        Assert.Equal((2, 2), (viaToken.BallotsSubmitted, viaToken.BallotsRequired));
        Assert.False(person.IsMasked);
        Assert.Equal(1, person.DisputedChecks);
    }

    /// <summary>
    ///     Test that a discussion about a check is left out of the thread list of a viewer who has not handed in, and
    ///     is in the list of the lead, of a reviewer who handed in, and of a system administrator.
    /// </summary>
    [Fact]
    public async Task ThreadService_List_MaskedViewer_LeavesOutCheckThreads()
    {
        // Arrange: the lead starts a discussion about a check; ann handed in, bob has a draft
        var world = await BallotMaskingWorld.CreateAsync();
        await world.ArrangeDisputeAsync(bobSubmitted: false);
        await world.StartCheckThreadAsync("lead");
        var projectId = world.ProjectId;

        // Act
        var bob = await world.Threads.ListAsync(BallotMaskingWorld.ActorOf("bob"), projectId, BallotMaskingWorld.ReviewId, Token);
        var anonymous = await world.Threads.ListAsync(null, projectId, BallotMaskingWorld.ReviewId, Token);
        var ann = await world.Threads.ListAsync(BallotMaskingWorld.ActorOf("ann"), projectId, BallotMaskingWorld.ReviewId, Token);
        var lead = await world.Threads.ListAsync(BallotMaskingWorld.ActorOf("lead"), projectId, BallotMaskingWorld.ReviewId, Token);
        var root = await world.Threads.ListAsync(BallotMaskingWorld.ActorOf("root"), projectId, BallotMaskingWorld.ReviewId, Token);

        // Assert
        Assert.Empty(bob);
        Assert.Empty(anonymous);
        Assert.Single(ann);
        Assert.Single(lead);
        Assert.Single(root);
    }

    /// <summary>
    ///     Test that the thread list fails closed when the review vanishes between the thread read and the visibility
    ///     check: the threads about a check are left out, the others stay.
    /// </summary>
    [Fact]
    public async Task ThreadService_List_ReviewVanishesBetweenReads_LeavesOutCheckThreads()
    {
        // Arrange: one discussion about a check and one about the review as a whole
        var world = await BallotMaskingWorld.CreateAsync();
        await world.ArrangeDisputeAsync(bobSubmitted: false);
        await world.StartCheckThreadAsync("lead");
        await world.Threads.StartThreadAsync(
            world.ProjectId, BallotMaskingWorld.ReviewId, new ThreadAnchor("a.txt"), BallotMaskingWorld.ActorOf("lead"), "General", Token);
        world.Store.AfterListThreads = () =>
        {
            world.Store.AfterListThreads = null;
            world.Store.VanishReview(world.ProjectId, BallotMaskingWorld.ReviewId);
        };

        // Act
        var list = await world.Threads.ListAsync(BallotMaskingWorld.ActorOf("lead"), world.ProjectId, BallotMaskingWorld.ReviewId, Token);

        // Assert
        Assert.Single(list);
        Assert.False(list[0].IsCheckThread);
    }

    /// <summary>
    ///     Test that the thread capabilities offer a discussion about a check exactly where the rules would accept it:
    ///     after the own ballot is in, to the lead, and never to a person who cannot contribute.
    /// </summary>
    [Fact]
    public async Task ThreadService_GetCapabilities_CanStartCheckThread_MatchesCheckThreadRules()
    {
        // Arrange
        var world = await BallotMaskingWorld.CreateAsync();
        await world.ArrangeDisputeAsync(bobSubmitted: false);

        // Act
        var ann = await world.Threads.GetCapabilitiesAsync(BallotMaskingWorld.ActorOf("ann"), world.ProjectId, BallotMaskingWorld.ReviewId, Token);
        var bob = await world.Threads.GetCapabilitiesAsync(BallotMaskingWorld.ActorOf("bob"), world.ProjectId, BallotMaskingWorld.ReviewId, Token);
        var cyd = await world.Threads.GetCapabilitiesAsync(BallotMaskingWorld.ActorOf("cyd"), world.ProjectId, BallotMaskingWorld.ReviewId, Token);
        var lead = await world.Threads.GetCapabilitiesAsync(BallotMaskingWorld.ActorOf("lead"), world.ProjectId, BallotMaskingWorld.ReviewId, Token);
        var outsider = await world.Threads.GetCapabilitiesAsync(BallotMaskingWorld.ActorOf("out"), world.ProjectId, BallotMaskingWorld.ReviewId, Token);
        var nobody = await world.Threads.GetCapabilitiesAsync(null, world.ProjectId, BallotMaskingWorld.ReviewId, Token);

        // Assert
        Assert.Equal(new ThreadCapabilities(true, true), ann);
        Assert.Equal(new ThreadCapabilities(true, false), bob);
        Assert.Equal(new ThreadCapabilities(true, false), cyd);
        Assert.Equal(new ThreadCapabilities(true, true), lead);
        Assert.Equal(ThreadCapabilities.None, outsider);
        Assert.Equal(ThreadCapabilities.None, nobody);
    }

    /// <summary>
    ///     Test that a viewer who has not handed in never gets a count, a blocker or a per-thread action for a
    ///     discussion about a check (a Finding here), while the lead and a reviewer who handed in do. The primary
    ///     subject is the progress count of <see cref="ReviewProgressService"/>; the approval blockers and the thread
    ///     capabilities are checked with it.
    /// </summary>
    [Fact]
    public async Task ReviewProgressService_GetProgress_MaskedViewer_DoesNotCountCheckThreads()
    {
        // Arrange: the lead starts a discussion about a check and promotes it; bob has not handed in
        var world = await BallotMaskingWorld.CreateAsync();
        await world.ArrangeDisputeAsync(bobSubmitted: false);
        var check = await world.StartCheckThreadAsync("lead");
        await world.Threads.PromoteAsync(world.ProjectId, BallotMaskingWorld.ReviewId, check.Id, BallotMaskingWorld.ActorOf("lead"), Token);
        await world.Threads.StartThreadAsync(
            world.ProjectId, BallotMaskingWorld.ReviewId, new ThreadAnchor("a.txt"), BallotMaskingWorld.ActorOf("lead"), "General", Token);

        // Act
        var bobProgress = (await world.Progress.GetProgressAsync(BallotMaskingWorld.ActorOf("bob"), world.ProjectId, Token)).Single();
        var annProgress = (await world.Progress.GetProgressAsync(BallotMaskingWorld.ActorOf("ann"), world.ProjectId, Token)).Single();
        var bobApproval = (await world.Signing.GetApprovalAsync(
            BallotMaskingWorld.ActorOf("bob"), world.ProjectId, BallotMaskingWorld.ReviewId, Token))!;
        var bobCapabilities = await world.Threads.GetCapabilitiesAsync(
            BallotMaskingWorld.ActorOf("bob"), world.ProjectId, BallotMaskingWorld.ReviewId, Token);
        var annCapabilities = await world.Threads.GetCapabilitiesAsync(
            BallotMaskingWorld.ActorOf("ann"), world.ProjectId, BallotMaskingWorld.ReviewId, Token);

        // Assert
        Assert.Equal(1, bobProgress.OpenQuestions);
        Assert.Equal(0, bobProgress.OpenFindings);
        Assert.Equal(1, bobApproval.OpenQuestions);
        Assert.Equal(0, bobApproval.OpenFindings);
        Assert.Contains("1 open question to resolve", bobApproval.Blockers);
        Assert.DoesNotContain(bobApproval.Blockers, text => text.Contains("finding", StringComparison.Ordinal));
        Assert.Single(bobCapabilities.Threads);
        Assert.False(bobCapabilities.CanDemote(check.Id));
        Assert.Equal(1, annProgress.OpenQuestions);
        Assert.Equal(1, annProgress.OpenFindings);
        Assert.Equal(2, annCapabilities.Threads.Count);
    }
}
