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
    ///     Test that a discussion about a check does not make the row of a reviewer who has not handed in unread, while
    ///     it does for a reviewer who has handed in.
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

        // Assert
        Assert.False(bob.Items.Single(item => item.Section == InboxSection.ToSign).IsUnread);
        Assert.True(ann.Items.Single(item => item.Section == InboxSection.ToSign).IsUnread);
    }

    /// <summary>
    ///     Test that the system-caller read of the checklist (<see cref="ChecklistService.GetForSystemAsync"/>, and
    ///     <see cref="ChecklistService.GetAsync"/> without a viewer) is not masked, while the same read for a viewer
    ///     without a slot is: the unmasked path is a named choice and never reached by a page that passes the viewer.
    /// </summary>
    [Fact]
    public async Task ChecklistService_GetForSystemAsync_NoViewer_ReturnsUnmaskedStanding()
    {
        // Arrange
        var world = await BallotMaskingWorld.CreateAsync();
        await world.ArrangeDisputeAsync();

        // Act
        var system = await world.Checklists.GetForSystemAsync(world.ProjectId, BallotMaskingWorld.ReviewId, Token);
        var noViewer = await world.Checklists.GetAsync(world.ProjectId, BallotMaskingWorld.ReviewId, Token);
        var viewer = await world.Checklists.GetAsync(world.ProjectId, BallotMaskingWorld.ReviewId, Token, "cyd");

        // Assert
        Assert.False(system!.IsMasked);
        Assert.NotNull(system.Consolidation);
        Assert.False(system.NoSlot);
        Assert.False(system.AuthorBlocked);
        Assert.False(noViewer!.IsMasked);
        Assert.NotNull(noViewer.Consolidation);
        Assert.True(viewer!.IsMasked);
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
    ///     discussion about a check (a Finding here), while the lead and a reviewer who handed in do.
    /// </summary>
    [Fact]
    public async Task ThreadCounts_MaskedViewer_NeverCountsCheckThreads()
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
