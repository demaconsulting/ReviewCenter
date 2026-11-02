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
///     Unit tests for <see cref="InboxService"/> over in-memory stores and the real access rules: the four inbox
///     sections, who sees what, the unread rules, the count and the read state.
/// </summary>
public class InboxServiceTests
{
    /// <summary>
    ///     Gets the cancellation token of the running test.
    /// </summary>
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    ///     Test that the primary reviewer of a slot without a signature finds the review under To sign.
    /// </summary>
    [Fact]
    public async Task InboxService_GetInboxAsync_PrimarySlotNotSigned_ShowsToSignItem()
    {
        // Arrange: an answered review with one slot, ann as primary and bob as alternate
        var world = await InboxTestWorld.CreateAsync();
        await world.CreateReviewAsync("r1", title: "Check the plan");
        await world.AnswerAllAsync("r1");
        await world.AddSlotAsync("r1", "ann", "bob");

        // Act
        var view = await world.InboxOfAsync("ann");

        // Assert: one To sign row with the chip, the project, the title and the unread marker (never opened)
        var row = Assert.Single(view.Items);
        Assert.Equal(InboxSection.ToSign, row.Section);
        Assert.Equal(InboxService.NotSignedChip, row.Chip);
        Assert.Equal("Apollo", row.ProjectName);
        Assert.Equal(world.ProjectId, row.ProjectId);
        Assert.Equal("r1", row.ReviewId);
        Assert.Equal("Check the plan", row.Title);
        Assert.True(row.IsUnread);
        Assert.Equal(1, view.ActionCount);
    }

    /// <summary>
    ///     Test that the alternate reviewer of a slot also finds the review under To sign.
    /// </summary>
    [Fact]
    public async Task InboxService_GetInboxAsync_AlternateSlotNotSigned_ShowsToSignItem()
    {
        // Arrange
        var world = await InboxTestWorld.CreateAsync();
        await world.CreateReviewAsync("r1");
        await world.AnswerAllAsync("r1");
        await world.AddSlotAsync("r1", "ann", "bob");

        // Act
        var rows = await world.RowsAsync("bob", InboxSection.ToSign);

        // Assert
        var row = Assert.Single(rows);
        Assert.Equal("r1", row.ReviewId);
        Assert.Equal(InboxService.NotSignedChip, row.Chip);
    }

    /// <summary>
    ///     Test that a signature that no longer covers the record shows the sign-again chip.
    /// </summary>
    [Fact]
    public async Task InboxService_GetInboxAsync_SignatureOutOfDate_ShowsSignAgainChip()
    {
        // Arrange: two slots, ann signs, then a finding is started and resolved, so the signature is out of date
        var world = await InboxTestWorld.CreateAsync();
        await world.CreateReviewAsync("r1");
        await world.AnswerAllAsync("r1");
        var first = await world.AddSlotAsync("r1", "ann");
        await world.AddSlotAsync("r1", "bob");
        await world.SignAsync("r1", first.Id, "ann");
        Assert.Empty(await world.RowsAsync("ann", InboxSection.ToSign));

        // Bob comments and settles it at once: the record is no longer the one ann signed
        var acknowledged = await world.Store.CurrentlySignedSlotIdsAsync(world.ProjectId, "r1");
        var thread = await world.Threads.StartThreadAsync(
            world.ProjectId, "r1", new ThreadAnchor("a.txt"), InboxTestWorld.ActorOf("bob"), "Please look", Ct, acknowledged, true);
        await world.Threads.ResolveAsync(
            world.ProjectId, "r1", thread.Id, InboxTestWorld.ActorOf("bob"), Ct, acknowledged, "Fixed");

        // Act
        var rows = await world.RowsAsync("ann", InboxSection.ToSign);

        // Assert
        var row = Assert.Single(rows);
        Assert.Equal(InboxService.SignAgainChip, row.Chip);
    }

    /// <summary>
    ///     Test that a slot with a current signature has no To sign row for its reviewer.
    /// </summary>
    [Fact]
    public async Task InboxService_GetInboxAsync_SlotSigned_HasNoToSignItem()
    {
        // Arrange: ann signed her slot, bob's slot is still open, so the review is not approved
        var world = await InboxTestWorld.CreateAsync();
        await world.CreateReviewAsync("r1");
        await world.AnswerAllAsync("r1");
        var first = await world.AddSlotAsync("r1", "ann");
        await world.AddSlotAsync("r1", "bob");
        await world.SignAsync("r1", first.Id, "ann");

        // Act
        var forAnn = await world.InboxOfAsync("ann");
        var forBob = await world.InboxOfAsync("bob");

        // Assert
        Assert.DoesNotContain(forAnn.Items, item => item.Section == InboxSection.ToSign);
        Assert.Single(forBob.Items, item => item.Section == InboxSection.ToSign);
    }

    /// <summary>
    ///     Test that an approved review is no work for anyone: only an information row for the others.
    /// </summary>
    [Fact]
    public async Task InboxService_GetInboxAsync_ApprovedReview_HasNoActionItems()
    {
        // Arrange: both slots signed, so the review is approved
        var world = await InboxTestWorld.CreateAsync();
        await world.CreateReviewAsync("r1");
        await world.AnswerAllAsync("r1");
        var first = await world.AddSlotAsync("r1", "ann");
        var second = await world.AddSlotAsync("r1", "bob");
        await world.SignAsync("r1", first.Id, "ann");
        await world.SignAsync("r1", second.Id, "bob");

        // Act
        var forAnn = await world.InboxOfAsync("ann");
        var forBob = await world.InboxOfAsync("bob");
        var forAuthor = await world.InboxOfAsync("author");

        // Assert: nobody has to act; the signer who did not complete the approval and the author are told
        Assert.Equal(0, forAnn.ActionCount);
        Assert.Equal(0, forBob.ActionCount);
        Assert.Equal(0, forAuthor.ActionCount);
        Assert.All(forAnn.Items, item => Assert.Equal(InboxSection.Updates, item.Section));
        Assert.All(forAuthor.Items, item => Assert.Equal(InboxSection.Updates, item.Section));
    }

    /// <summary>
    ///     Test that a review whose every slot is signed but that has a thread open is not concluded: the author is told
    ///     about the reply waiting, and the signer has nothing left to sign.
    /// </summary>
    [Fact]
    public async Task InboxService_GetInboxAsync_AllSignedOpenThread_ShowsRepliesWaitingNotConcluded()
    {
        // Arrange: ann signs, bob opens a chat thread, and bob's unsigned slot is removed
        var world = await InboxTestWorld.CreateAsync();
        await world.CreateReviewAsync("r1");
        await world.AnswerAllAsync("r1");
        var first = await world.AddSlotAsync("r1", "ann");
        var second = await world.AddSlotAsync("r1", "bob");
        await world.SignAsync("r1", first.Id, "ann");
        await world.StartThreadAsync("r1", "bob");
        await world.Signing.RemoveSlotAsync(
            new Actor("author", "Author"), world.ProjectId, "r1", second.Id, TestContext.Current.CancellationToken);

        // Act
        var forAuthor = await world.InboxOfAsync("author");
        var forAnn = await world.InboxOfAsync("ann");

        // Assert: the author has a reply waiting, which an approved review would not show
        var row = Assert.Single(forAuthor.Items, item => item.Section == InboxSection.RepliesWaiting);
        Assert.Equal("1 waiting", row.Chip);
        Assert.Equal(1, forAuthor.ActionCount);
        Assert.DoesNotContain(forAnn.Items, item => item.Section == InboxSection.ToSign);
    }

    /// <summary>
    ///     Test that an auditor, who may only read, gets no To sign and no Replies waiting rows even for a review it
    ///     authored and a slot it is assigned to.
    /// </summary>
    [Fact]
    public async Task InboxService_GetInboxAsync_Auditor_GetsNoSignOrReplyItems()
    {
        // Arrange: a review authored by the auditor with a slot for the auditor (arranged in the store, because
        // the services refuse an auditor as a reviewer), and a thread of ann
        var world = await InboxTestWorld.CreateAsync();
        await world.CreateReviewAsync("r1", "aud");
        await world.Store.AddSlotAsync(
            new ReviewerSlot { ProjectId = world.ProjectId, ReviewId = "r1", PrimaryUserId = world.Ids["aud"] },
            _ => AuditTestExtensions.Draft(),
            Ct);
        await world.StartThreadAsync("r1", "ann");

        // Act
        var view = await world.InboxOfAsync("aud");

        // Assert
        Assert.DoesNotContain(view.Items, item => item.Section == InboxSection.ToSign);
        Assert.DoesNotContain(view.Items, item => item.Section == InboxSection.RepliesWaiting);
    }

    /// <summary>
    ///     Test that a user who is no member, and a user whose membership was removed, get the empty view.
    /// </summary>
    [Fact]
    public async Task InboxService_GetInboxAsync_NonMemberOrRemovedMember_GetsEmptyView()
    {
        // Arrange: dee has an answered slot and then loses the membership; out authored a review but is no member
        var world = await InboxTestWorld.CreateAsync();
        await world.CreateReviewAsync("r1");
        await world.AnswerAllAsync("r1");
        await world.AddSlotAsync("r1", "dee");
        await world.CreateReviewAsync("r2", "out");
        Assert.Single(await world.RowsAsync("dee", InboxSection.ToSign));
        await world.Store.RemoveMembershipAsync(world.ProjectId, world.Ids["dee"], AuditTestExtensions.Draft(), false, Ct);

        // Act
        var removed = await world.InboxOfAsync("dee");
        var outsider = await world.InboxOfAsync("out");

        // Assert
        Assert.Empty(removed.Items);
        Assert.Equal(0, removed.ActionCount);
        Assert.Empty(outsider.Items);
        Assert.Equal(0, outsider.ActionCount);
    }

    /// <summary>
    ///     Test that a disabled user gets the empty view and no error.
    /// </summary>
    [Fact]
    public async Task InboxService_GetInboxAsync_DisabledUser_GetsEmptyView()
    {
        // Arrange: off has an answered slot, which shows until the account is disabled
        var world = await InboxTestWorld.CreateAsync();
        await world.CreateReviewAsync("r1");
        await world.AnswerAllAsync("r1");
        await world.AddSlotAsync("r1", "off");
        Assert.Single(await world.RowsAsync("off", InboxSection.ToSign));
        await world.Users.SetEnabledAsync("off", false, Actor.CommandLine, Ct);

        // Act
        var view = await world.InboxOfAsync("off");
        var count = await world.Inbox.CountAsync(InboxTestWorld.ActorOf("off"), Ct);

        // Assert
        Assert.Empty(view.Items);
        Assert.Equal(0, count);
    }

    /// <summary>
    ///     Test that a user who still has a temporary password gets the empty view.
    /// </summary>
    [Fact]
    public async Task InboxService_GetInboxAsync_TemporaryPasswordUser_GetsEmptyView()
    {
        // Arrange: tmp has an answered slot, which shows until the password becomes a temporary one
        var world = await InboxTestWorld.CreateAsync();
        await world.CreateReviewAsync("r1");
        await world.AnswerAllAsync("r1");
        await world.AddSlotAsync("r1", "tmp");
        Assert.Single(await world.RowsAsync("tmp", InboxSection.ToSign));
        var user = (await world.UserStore.GetByUsernameAsync("tmp", Ct))!;
        user.MustChangePassword = true;
        await world.UserStore.UpdateAsync(user, null, Ct);

        // Act
        var view = await world.InboxOfAsync("tmp");

        // Assert
        Assert.Empty(view.Items);
        Assert.Equal(0, view.ActionCount);
    }

    /// <summary>
    ///     Test that no actor, the command line and a name that is no user give the empty view and no error.
    /// </summary>
    [Fact]
    public async Task InboxService_GetInboxAsync_AnonymousOrTokenOnlyActor_GetsEmptyView()
    {
        // Arrange: a review with work for ann
        var world = await InboxTestWorld.CreateAsync();
        await world.CreateReviewAsync("r1");
        await world.AddSlotAsync("r1", "ann");

        // Act
        var results = new[]
        {
            await world.Inbox.GetInboxAsync(null, Ct),
            await world.Inbox.GetInboxAsync(Actor.CommandLine, Ct),
            await world.Inbox.GetInboxAsync(new Actor("ghost", "Ghost"), Ct),
            await world.Inbox.GetInboxAsync(new Actor(string.Empty, string.Empty), Ct)
        };

        // Assert
        Assert.All(results, view => Assert.Empty(view.Items));
        Assert.All(results, view => Assert.Equal(0, view.ActionCount));
        Assert.Equal(0, await world.Inbox.CountAsync(null, Ct));
    }

    /// <summary>
    ///     Test that a thread in which the user wrote and that someone else answered last is a reply waiting.
    /// </summary>
    [Fact]
    public async Task InboxService_GetInboxAsync_LatestCommentByOther_IAnswered_ShowsRepliesWaiting()
    {
        // Arrange: ann is a reviewer, starts a thread and bob answers
        var world = await InboxTestWorld.CreateAsync();
        await world.CreateReviewAsync("r1");
        await world.AddSlotAsync("r1", "ann");
        var thread = await world.StartThreadAsync("r1", "ann");
        await world.ReplyAsync("r1", thread.Id, "bob");

        // Act
        var rows = await world.RowsAsync("ann", InboxSection.RepliesWaiting);

        // Assert
        var row = Assert.Single(rows);
        Assert.Equal("1 waiting", row.Chip);
        Assert.True(row.IsUnread);
        Assert.Equal("r1", row.ReviewId);
    }

    /// <summary>
    ///     Test that the row of waiting replies names the findings and questions that wait.
    /// </summary>
    [Fact]
    public async Task InboxService_GetInboxAsync_RepliesWaiting_TextNamesFindingsAndQuestions()
    {
        // Arrange: ann started a finding and two questions, bob answered each
        var world = await InboxTestWorld.CreateAsync();
        await world.CreateReviewAsync("r1");
        await world.AddSlotAsync("r1", "ann");
        var finding = await world.Threads.StartThreadAsync(
            world.ProjectId,
            "r1",
            new ThreadAnchor("a.txt"),
            InboxTestWorld.ActorOf("ann"),
            "Needs a fix",
            Ct,
            includeInReport: true);
        var first = await world.StartThreadAsync("r1", "ann");
        var second = await world.StartThreadAsync("r1", "ann");
        await world.ReplyAsync("r1", finding.Id, "bob");
        await world.ReplyAsync("r1", first.Id, "bob");
        await world.ReplyAsync("r1", second.Id, "bob");

        // Act
        var row = Assert.Single(await world.RowsAsync("ann", InboxSection.RepliesWaiting));

        // Assert
        Assert.Equal("3 waiting", row.Chip);
        Assert.Equal("Waiting for your reply: 1 open finding, 2 open questions.", row.Detail);
    }

    /// <summary>
    ///     Test that a thread whose latest comment is the user's own is not waiting.
    /// </summary>
    [Fact]
    public async Task InboxService_GetInboxAsync_LatestCommentByMe_HasNoRepliesWaiting()
    {
        // Arrange: ann starts, bob answers, ann answers again
        var world = await InboxTestWorld.CreateAsync();
        await world.CreateReviewAsync("r1");
        var thread = await world.StartThreadAsync("r1", "ann");
        await world.ReplyAsync("r1", thread.Id, "bob");
        await world.ReplyAsync("r1", thread.Id, "ann");

        // Act
        var rows = await world.RowsAsync("ann", InboxSection.RepliesWaiting);

        // Assert
        Assert.Empty(rows);
    }

    /// <summary>
    ///     Test that a resolved thread is not waiting.
    /// </summary>
    [Fact]
    public async Task InboxService_GetInboxAsync_ResolvedThread_HasNoRepliesWaiting()
    {
        // Arrange: ann starts, bob answers, the thread is resolved
        var world = await InboxTestWorld.CreateAsync();
        await world.CreateReviewAsync("r1");
        var thread = await world.StartThreadAsync("r1", "ann");
        await world.ReplyAsync("r1", thread.Id, "bob");
        await world.Threads.ResolveAsync(world.ProjectId, "r1", thread.Id, InboxTestWorld.ActorOf("ann"), Ct);

        // Act
        var rows = await world.RowsAsync("ann", InboxSection.RepliesWaiting);

        // Assert
        Assert.Empty(rows);
    }

    /// <summary>
    ///     Test that the author of a review sees a thread of someone else that nobody answered.
    /// </summary>
    [Fact]
    public async Task InboxService_GetInboxAsync_AuthorOfReview_ThreadByOther_ShowsRepliesWaiting()
    {
        // Arrange: bob starts a thread on the review of the author, who never wrote in it
        var world = await InboxTestWorld.CreateAsync();
        await world.CreateReviewAsync("r1");
        await world.StartThreadAsync("r1", "bob");

        // Act
        var forAuthor = await world.RowsAsync("author", InboxSection.RepliesWaiting);
        var forEve = await world.RowsAsync("eve", InboxSection.RepliesWaiting);

        // Assert: the author is waited for; someone who has nothing to do with the thread is not
        Assert.Single(forAuthor);
        Assert.Empty(forEve);
    }

    /// <summary>
    ///     Test that a comment of the system counts as a comment of someone else.
    /// </summary>
    [Fact]
    public async Task InboxService_GetInboxAsync_SystemCommentLast_CountsAsOther()
    {
        // Arrange: ann is a reviewer, starts a thread and the system adds a comment last
        var world = await InboxTestWorld.CreateAsync();
        await world.CreateReviewAsync("r1");
        await world.AddSlotAsync("r1", "ann");
        var thread = await world.StartThreadAsync("r1", "ann");
        await world.Store.AddCommentAsync(
            world.ProjectId,
            "r1",
            thread.Id,
            new ReviewComment
            {
                Author = FollowUpCarry.SystemAuthor,
                AuthorUsername = FollowUpCarry.SystemUsername,
                Text = "Carried over",
                Created = InboxTestWorld.Start
            },
            _ => AuditTestExtensions.Draft(),
            Ct);

        // Act
        var rows = await world.RowsAsync("ann", InboxSection.RepliesWaiting);

        // Assert
        Assert.Single(rows);
    }

    /// <summary>
    ///     Test that the author sees the failed checks of the review with the number, and that the failures
    ///     are new to the author.
    /// </summary>
    [Fact]
    public async Task InboxService_GetInboxAsync_FailAnswersInApplicableSections_ShowsFailedChecksWithCount()
    {
        // Arrange: bob answers two checks of the review of the author as Fail
        var world = await InboxTestWorld.CreateAsync();
        await world.CreateReviewAsync("r1");
        await world.FailCheckAsync("r1", "bob", 0);
        await world.FailCheckAsync("r1", "bob", 1);

        // Act
        var forAuthor = await world.RowsAsync("author", InboxSection.FailedChecks);
        var forBob = await world.RowsAsync("bob", InboxSection.FailedChecks);

        // Assert
        var row = Assert.Single(forAuthor);
        Assert.Equal("2 failed", row.Chip);
        Assert.True(row.IsUnread);
        Assert.Empty(forBob);
    }

    /// <summary>
    ///     Test that a failed check in a section that does not apply is ignored.
    /// </summary>
    [Fact]
    public async Task InboxService_GetInboxAsync_FailInNotApplicableSection_IsIgnored()
    {
        // Arrange: bob answers a check of the first section as Fail, then the section is marked not applicable
        var world = await InboxTestWorld.CreateAsync();
        await world.CreateReviewAsync("r1");
        await world.FailCheckAsync("r1", "bob");
        Assert.Single(await world.RowsAsync("author", InboxSection.FailedChecks));
        await world.Checklists.SetSectionApplicabilityAsync(
            world.ProjectId,
            "r1",
            DefaultChecklist.Current.Sections[0].Id,
            false,
            "Not relevant here",
            InboxTestWorld.ActorOf("bob"),
            Ct);

        // Act
        var rows = await world.RowsAsync("author", InboxSection.FailedChecks);

        // Assert
        Assert.Empty(rows);
    }

    /// <summary>
    ///     Test that a failed check tells the author how many checks the reviewers marked as Fail so far, that the
    ///     review will be rejected if that stands, and that the reasons must then be written down.
    /// </summary>
    [Fact]
    public async Task InboxService_GetInboxAsync_FailedChecks_TextSaysFailSoFarAndRejectionNeedsReasons()
    {
        // Arrange: bob answers a check of the review of the author as Fail
        var world = await InboxTestWorld.CreateAsync();
        await world.CreateReviewAsync("r1");
        await world.FailCheckAsync("r1", "bob");

        // Act
        var row = Assert.Single(await world.RowsAsync("author", InboxSection.FailedChecks));

        // Assert: the text says so far, names the rejection and the reasons, and promises nothing certain
        Assert.Equal(
            "Reviewers marked 1 check as Fail so far. If that stands, the review will be rejected and its reasons "
            + "must be written down.",
            row.Detail);
    }

    /// <summary>
    ///     Test that a review rejected by every slot is shown to the other reviewer as an update with the rejected
    ///     chip, which is no work, and that the review then has no failed-checks or to-sign rows.
    /// </summary>
    [Fact]
    public async Task InboxService_GetInboxAsync_RejectedByOther_ShowsRejectedUpdateNotAction()
    {
        // Arrange: both slots reject, ann writes the conclusion text, ann signs first and bob concludes later
        var world = await InboxTestWorld.CreateAsync();
        await world.CreateReviewAsync("r1");
        await world.AnswerAllAsync("r1");
        var first = await world.AddSlotAsync("r1", "ann");
        var second = await world.AddSlotAsync("r1", "bob");
        await world.FailCheckAsync("r1", "ann");
        await world.FailCheckAsync("r1", "bob");
        await world.Checklists.SetReviewConclusionTextAsync(world.ProjectId, "r1", "Not ready.", InboxTestWorld.ActorOf("ann"), Ct);
        await world.SignAsync("r1", first.Id, "ann");
        world.Clock.Advance(TimeSpan.FromMinutes(5));
        await world.SignAsync("r1", second.Id, "bob");

        // Act
        var view = await world.InboxOfAsync("ann");

        // Assert: one information row with the rejected chip, no work
        var row = Assert.Single(view.Items);
        Assert.Equal(InboxSection.Updates, row.Section);
        Assert.Equal(InboxService.RejectedChip, row.Chip);
        Assert.Equal(0, view.ActionCount);
        Assert.Empty(await world.RowsAsync("author", InboxSection.FailedChecks));
        Assert.Empty(await world.RowsAsync("bob", InboxSection.ToSign));
    }

    /// <summary>
    ///     Test that a review approved by someone else after the user last looked is shown as an update, which
    ///     is not counted as work, and that it goes away once the user has seen it.
    /// </summary>
    [Fact]
    public async Task InboxService_GetInboxAsync_ApprovedSinceLastSeen_ShowsUpdate_NotCountedAsAction()
    {
        // Arrange: ann signs first, bob completes the approval later
        var world = await InboxTestWorld.CreateAsync();
        await world.CreateReviewAsync("r1");
        await world.AnswerAllAsync("r1");
        var first = await world.AddSlotAsync("r1", "ann");
        var second = await world.AddSlotAsync("r1", "bob");
        await world.SignAsync("r1", first.Id, "ann");
        world.Clock.Advance(TimeSpan.FromMinutes(5));
        await world.SignAsync("r1", second.Id, "bob");

        // Act
        var view = await world.InboxOfAsync("ann");

        // Assert: one information row, not counted
        var row = Assert.Single(view.Items);
        Assert.Equal(InboxSection.Updates, row.Section);
        Assert.Equal(InboxService.ApprovedChip, row.Chip);
        Assert.Equal("Signed by Bob.", row.Detail);
        Assert.True(row.IsUnread);
        Assert.Equal(0, view.ActionCount);
        Assert.Equal(1, view.UpdateCount);

        // Act: ann looks at the review later
        world.Clock.Advance(TimeSpan.FromMinutes(5));
        await world.Inbox.MarkSeenAsync(InboxTestWorld.ActorOf("ann"), world.ProjectId, "r1", 0, null, Ct);

        // Assert: the news is gone
        Assert.Empty((await world.InboxOfAsync("ann")).Items);

        // Assert: bob completed the approval himself, so it is no news to him
        Assert.Empty((await world.InboxOfAsync("bob")).Items);
    }

    /// <summary>
    ///     Test that a reopen by someone else is shown as an update.
    /// </summary>
    [Fact]
    public async Task InboxService_GetInboxAsync_ReopenedByOther_ShowsUpdate()
    {
        // Arrange: an approved review that cyd reopens
        var world = await InboxTestWorld.CreateAsync();
        await world.CreateReviewAsync("r1");
        await world.AnswerAllAsync("r1");
        var first = await world.AddSlotAsync("r1", "ann");
        var second = await world.AddSlotAsync("r1", "bob");
        await world.SignAsync("r1", first.Id, "ann");
        await world.SignAsync("r1", second.Id, "bob");
        world.Clock.Advance(TimeSpan.FromMinutes(5));
        await world.Signing.ReopenReviewAsync(InboxTestWorld.ActorOf("cyd"), world.ProjectId, "r1", "One more thing", Ct);

        // Act
        var view = await world.InboxOfAsync("ann");

        // Assert: the reopen is news, and the signatures are out of date, so ann has to sign again
        var update = Assert.Single(view.Items, item => item.Section == InboxSection.Updates);
        Assert.Equal(InboxService.ReopenedChip, update.Chip);
        Assert.Equal("Reopened by Cyd.", update.Detail);
        var toSign = Assert.Single(view.Items, item => item.Section == InboxSection.ToSign);
        Assert.Equal(InboxService.SignAgainChip, toSign.Chip);
        Assert.Equal(1, view.ActionCount);
    }

    /// <summary>
    ///     Test that a reopen by the user is no news to the user.
    /// </summary>
    [Fact]
    public async Task InboxService_GetInboxAsync_ReopenedByMe_ShowsNoUpdate()
    {
        // Arrange: an approved review that ann reopens herself (the rule that would stop her signing again is off)
        var world = await InboxTestWorld.CreateAsync();
        await TestServices.Projects(world.Store, world.Clock).SetReviewRuleOffAsync(
            world.ProjectId, ReviewRule.NonReopenerSigning, Ct);
        await world.CreateReviewAsync("r1");
        await world.AnswerAllAsync("r1");
        var first = await world.AddSlotAsync("r1", "ann");
        var second = await world.AddSlotAsync("r1", "bob");
        await world.SignAsync("r1", first.Id, "ann");
        await world.SignAsync("r1", second.Id, "bob");
        world.Clock.Advance(TimeSpan.FromMinutes(5));
        await world.Signing.ReopenReviewAsync(InboxTestWorld.ActorOf("ann"), world.ProjectId, "r1", "My mistake", Ct);

        // Act
        var forAnn = await world.InboxOfAsync("ann");
        var forBob = await world.InboxOfAsync("bob");

        // Assert
        Assert.DoesNotContain(forAnn.Items, item => item.Section == InboxSection.Updates);
        Assert.Single(forBob.Items, item => item.Section == InboxSection.Updates);
    }

    /// <summary>
    ///     Test that a past due date of an open review gives the overdue marker and that an approved review shows
    ///     no due date.
    /// </summary>
    [Fact]
    public async Task InboxService_GetInboxAsync_DueDateInPast_ShowsOverdueChip_ApprovedShowsNone()
    {
        // Arrange: an open review due tomorrow, and an approved review due tomorrow
        var world = await InboxTestWorld.CreateAsync();
        var due = DateOnly.FromDateTime(InboxTestWorld.Start.UtcDateTime).AddDays(1);
        await world.CreateReviewAsync("r1", dueDate: due);
        await world.AnswerAllAsync("r1");
        await world.AddSlotAsync("r1", "ann");
        await world.CreateReviewAsync("r2", dueDate: due);
        await world.AnswerAllAsync("r2");
        var first = await world.AddSlotAsync("r2", "ann");
        var second = await world.AddSlotAsync("r2", "bob");
        await world.SignAsync("r2", first.Id, "ann");
        await world.SignAsync("r2", second.Id, "bob");
        var before = Assert.Single(await world.RowsAsync("ann", InboxSection.ToSign));
        Assert.False(before.IsOverdue);
        Assert.Equal(due, before.DueDate);

        // Act: three days later
        world.Clock.Advance(TimeSpan.FromDays(3));
        var view = await world.InboxOfAsync("ann");

        // Assert: the open review is overdue; the approved one shows no due date at all
        var open = Assert.Single(view.Items, item => item.Section == InboxSection.ToSign);
        Assert.True(open.IsOverdue);
        Assert.Equal(due, open.DueDate);
        var approved = Assert.Single(view.Items, item => item.ReviewId == "r2");
        Assert.False(approved.IsOverdue);
        Assert.Null(approved.DueDate);
    }

    /// <summary>
    ///     Test that a review with several reasons is listed once in each section.
    /// </summary>
    [Fact]
    public async Task InboxService_GetInboxAsync_ReviewInSeveralSections_AppearsInEach()
    {
        // Arrange: ann has an answered slot and a thread that bob answered, on the same review
        var world = await InboxTestWorld.CreateAsync();
        await world.CreateReviewAsync("r1");
        await world.AnswerAllAsync("r1");
        await world.AddSlotAsync("r1", "ann");
        var thread = await world.StartThreadAsync("r1", "ann");
        await world.ReplyAsync("r1", thread.Id, "bob");

        // Act
        var view = await world.InboxOfAsync("ann");

        // Assert: two rows, one review, ordered by section; the open thread keeps the signature from being asked for
        Assert.Equal([InboxSection.RepliesWaiting, InboxSection.WaitingForOthers], view.Items.Select(item => item.Section));
        Assert.All(view.Items, item => Assert.Equal("r1", item.ReviewId));
        Assert.Equal(1, view.ActionCount);
    }

    /// <summary>
    ///     Test that a user without a state row finds every comment of others unread, and that the baseline is zero.
    /// </summary>
    [Fact]
    public async Task InboxService_GetInboxAsync_NoStateRow_AllCommentsByOthersAreUnread()
    {
        // Arrange: ann's thread with one comment of ann and two of others
        var world = await InboxTestWorld.CreateAsync();
        await world.CreateReviewAsync("r1");
        var thread = await world.StartThreadAsync("r1", "ann");
        await world.ReplyAsync("r1", thread.Id, "bob");
        await world.ReplyAsync("r1", thread.Id, "eve");
        var threads = await world.Threads.ListAsync(world.ProjectId, "r1", Ct);

        // Act
        var baseline = await world.Inbox.GetReviewUnreadAsync(InboxTestWorld.ActorOf("ann"), world.ProjectId, "r1", Ct);
        var unread = InboxService.CountUnread(threads, "ann", baseline);

        // Assert
        Assert.Equal(0, baseline);
        Assert.Equal(2, unread);
        Assert.Empty(world.InboxStore.States);
    }

    /// <summary>
    ///     Test that a user's own comments are never unread, whatever the case of the user name.
    /// </summary>
    [Fact]
    public async Task InboxService_GetInboxAsync_OwnComments_AreNeverUnread()
    {
        // Arrange: ann writes a thread and a reply herself
        var world = await InboxTestWorld.CreateAsync();
        await world.CreateReviewAsync("r1");
        var thread = await world.StartThreadAsync("r1", "ann");
        await world.ReplyAsync("r1", thread.Id, "ann");
        var threads = await world.Threads.ListAsync(world.ProjectId, "r1", Ct);

        // Act
        var own = InboxService.CountUnread(threads, "ANN", 0);
        var other = InboxService.CountUnread(threads, "bob", 0);

        // Assert
        Assert.Equal(0, own);
        Assert.Equal(2, other);
        Assert.False(InboxService.IsUnread(threads[0].Comments[0], "ann", 0));
        Assert.True(InboxService.IsUnread(threads[0].Comments[0], "bob", 0));
    }

    /// <summary>
    ///     Test that only the comments after the last seen one are unread, and that the row of the inbox follows.
    /// </summary>
    [Fact]
    public async Task InboxService_GetInboxAsync_CommentsAfterLastSeen_AreUnread()
    {
        // Arrange: ann is a reviewer, her thread gets an answer from bob, she has seen the review, then eve answers
        var world = await InboxTestWorld.CreateAsync();
        await world.CreateReviewAsync("r1");
        await world.AddSlotAsync("r1", "ann");
        var thread = await world.StartThreadAsync("r1", "ann");
        await world.ReplyAsync("r1", thread.Id, "bob");
        var ann = InboxTestWorld.ActorOf("ann");
        var seenMax = InboxService.MaxCommentId(await world.Threads.ListAsync(world.ProjectId, "r1", Ct));
        world.Clock.Advance(TimeSpan.FromMinutes(1));
        await world.Inbox.MarkSeenAsync(ann, world.ProjectId, "r1", seenMax, null, Ct);
        var seenRow = Assert.Single(await world.RowsAsync("ann", InboxSection.RepliesWaiting));
        world.Clock.Advance(TimeSpan.FromMinutes(1));
        await world.ReplyAsync("r1", thread.Id, "eve");

        // Act
        var baseline = await world.Inbox.GetReviewUnreadAsync(ann, world.ProjectId, "r1", Ct);
        var threads = await world.Threads.ListAsync(world.ProjectId, "r1", Ct);
        var row = Assert.Single(await world.RowsAsync("ann", InboxSection.RepliesWaiting));

        // Assert
        Assert.False(seenRow.IsUnread);
        Assert.Equal(seenMax, baseline);
        Assert.Equal(1, InboxService.CountUnread(threads, "ann", baseline));
        Assert.True(row.IsUnread);
    }

    /// <summary>
    ///     Test that "Mark all as read" does not mark a comment as seen that was posted after the moment it started.
    /// </summary>
    [Fact]
    public async Task InboxService_MarkAllReadAsync_CommentAfterTheStart_StaysUnread()
    {
        // Arrange: ann is a reviewer and started a thread at the start; eve replies a minute later, which is after
        // the moment at which the service that marks everything as read starts (the clock of that service is 30
        // seconds after the start of the world)
        var world = await InboxTestWorld.CreateAsync();
        await world.CreateReviewAsync("r1");
        await world.AddSlotAsync("r1", "ann");
        var thread = await world.StartThreadAsync("r1", "ann");
        world.Clock.Advance(TimeSpan.FromMinutes(1));
        var late = await world.ReplyAsync("r1", thread.Id, "eve");
        var marking = new InboxService(
            world.InboxStore,
            world.Store,
            world.Access,
            world.UserStore,
            new MovableTimeProvider(InboxTestWorld.Start.AddSeconds(30)));

        // Act
        var marked = await marking.MarkAllReadAsync(InboxTestWorld.ActorOf("ann"), Ct);
        var row = Assert.Single(await world.RowsAsync("ann", InboxSection.RepliesWaiting));

        // Assert: the state stops before the late comment, so the row is still unread
        Assert.Equal(1, marked);
        var state = Assert.Single(world.InboxStore.States);
        Assert.Equal(thread.Comments[0].Id, state.LastSeenCommentId);
        Assert.True(late.Id > state.LastSeenCommentId);
        Assert.True(row.IsUnread);
    }

    /// <summary>
    ///     Test that a To sign row of a user who has seen the review turns unread again when someone else signs it.
    /// </summary>
    [Fact]
    public async Task InboxService_GetInboxAsync_ToSign_OtherSignsAfterLastSeen_IsUnread()
    {
        // Arrange: ann and bob are reviewers of a finished review; ann has seen it
        var world = await InboxTestWorld.CreateAsync();
        await world.CreateReviewAsync("r1");
        await world.AnswerAllAsync("r1");
        await world.AddSlotAsync("r1", "ann");
        var bobSlot = await world.AddSlotAsync("r1", "bob");
        world.Clock.Advance(TimeSpan.FromMinutes(1));
        await world.Inbox.MarkSeenAsync(InboxTestWorld.ActorOf("ann"), world.ProjectId, "r1", 0, null, Ct);
        var seen = Assert.Single(await world.RowsAsync("ann", InboxSection.ToSign));
        world.Clock.Advance(TimeSpan.FromMinutes(1));

        // Act: bob signs
        await world.SignAsync("r1", bobSlot.Id, "bob");
        var after = Assert.Single(await world.RowsAsync("ann", InboxSection.ToSign));

        // Assert: the row was read before and is new now
        Assert.False(seen.IsUnread);
        Assert.True(after.IsUnread);
    }

    /// <summary>
    ///     Test that a To sign row of a user who has seen the review turns unread again when someone else comments.
    /// </summary>
    [Fact]
    public async Task InboxService_GetInboxAsync_ToSign_CommentByOtherAfterLastSeen_IsUnread()
    {
        // Arrange: ann is a reviewer with an answered ballot and has seen the review
        var world = await InboxTestWorld.CreateAsync();
        await world.CreateReviewAsync("r1");
        await world.AnswerAllAsync("r1");
        await world.AddSlotAsync("r1", "ann");
        world.Clock.Advance(TimeSpan.FromMinutes(1));
        await world.Inbox.MarkSeenAsync(InboxTestWorld.ActorOf("ann"), world.ProjectId, "r1", 0, null, Ct);
        var seen = Assert.Single(await world.RowsAsync("ann", InboxSection.ToSign));
        world.Clock.Advance(TimeSpan.FromMinutes(1));

        // Act: bob starts a thread, which he resolves at once (the review stays free to sign)
        var thread = await world.StartThreadAsync("r1", "bob");
        await world.Threads.ResolveAsync(world.ProjectId, "r1", thread.Id, InboxTestWorld.ActorOf("bob"), Ct);
        var after = Assert.Single(await world.RowsAsync("ann", InboxSection.ToSign));

        // Assert
        Assert.False(seen.IsUnread);
        Assert.True(after.IsUnread);
    }

    /// <summary>
    ///     Test that a draft ballot of another slot does not turn a read To answer row unread.
    /// </summary>
    [Fact]
    public async Task InboxService_Unread_DraftBallotOfOtherSlot_Ignored()
    {
        // Arrange: ann and bob are reviewers; ann has seen the review
        var world = await InboxTestWorld.CreateAsync();
        await world.CreateReviewAsync("r1");
        await world.AddSlotAsync("r1", "ann");
        await world.AddSlotAsync("r1", "bob");
        world.Clock.Advance(TimeSpan.FromMinutes(1));
        await world.Inbox.MarkSeenAsync(InboxTestWorld.ActorOf("ann"), world.ProjectId, "r1", 0, null, Ct);
        world.Clock.Advance(TimeSpan.FromMinutes(1));

        // Act: bob answers a check in his draft ballot without handing it in
        await world.StartBallotAsync("r1", "bob");
        var row = Assert.Single(await world.RowsAsync("ann", InboxSection.ToAnswer));

        // Assert: the draft is not news to ann
        Assert.False(row.IsUnread);
    }

    /// <summary>
    ///     Test that a submitted ballot of another slot turns a read To sign row unread.
    /// </summary>
    [Fact]
    public async Task InboxService_Unread_SubmittedBallotOfOtherSlot_IsUnread()
    {
        // Arrange: ann and bob are reviewers; ann has answered and handed in her ballot, and has seen the review
        var world = await InboxTestWorld.CreateAsync();
        await world.CreateReviewAsync("r1");
        await world.AddSlotAsync("r1", "ann");
        await world.AddSlotAsync("r1", "bob");
        await world.SubmitBallotAsync("r1", "ann");
        world.Clock.Advance(TimeSpan.FromMinutes(1));
        await world.Inbox.MarkSeenAsync(InboxTestWorld.ActorOf("ann"), world.ProjectId, "r1", 0, null, Ct);
        var seen = Assert.Single(await world.RowsAsync("ann", InboxSection.ToSign));
        world.Clock.Advance(TimeSpan.FromMinutes(1));

        // Act: bob answers every check and hands his ballot in
        await world.SubmitBallotAsync("r1", "bob");
        var after = Assert.Single(await world.RowsAsync("ann", InboxSection.ToSign));

        // Assert: the row was read before and is new now
        Assert.False(seen.IsUnread);
        Assert.True(after.IsUnread);
    }

    /// <summary>
    ///     Test that the count is the number of distinct reviews that need action, not the number of rows and
    ///     not the information rows.
    /// </summary>
    [Fact]
    public async Task InboxService_CountAsync_EqualsDistinctActionReviews()
    {
        // Arrange: ann has a slot and a waiting thread on r1 (two rows, one review), a failed check on her own review
        // r2 (one row), and an approval in r3 (an update)
        var world = await InboxTestWorld.CreateAsync();
        await world.CreateReviewAsync("r1");
        await world.AddSlotAsync("r1", "ann");
        var thread = await world.StartThreadAsync("r1", "ann");
        await world.ReplyAsync("r1", thread.Id, "bob");
        await world.CreateReviewAsync("r2", "ann");
        await world.FailCheckAsync("r2", "bob");
        await world.CreateReviewAsync("r3");
        await world.AnswerAllAsync("r3");
        var first = await world.AddSlotAsync("r3", "ann");
        var second = await world.AddSlotAsync("r3", "bob");
        await world.SignAsync("r3", first.Id, "ann");
        await world.SignAsync("r3", second.Id, "bob");
        world.Clock.Advance(TimeSpan.FromMinutes(1));

        // Act
        var view = await world.InboxOfAsync("ann");
        var count = await world.Inbox.CountAsync(InboxTestWorld.ActorOf("ann"), Ct);

        // Assert
        Assert.Equal(4, view.Items.Count);
        Assert.Equal(1, view.UpdateCount);
        Assert.Equal(2, view.ActionCount);
        Assert.Equal(2, count);
    }

    /// <summary>
    ///     Test that a system administrator who is no member gets rows only for what is their own: the review they
    ///     authored, and no sign row for a slot without membership.
    /// </summary>
    [Fact]
    public async Task InboxService_GetInboxAsync_Sysadmin_SeesOnlyOwnSlotsAndAuthoredReviews()
    {
        // Arrange: root authored r2, which has a failed check, and is the reviewer of a slot of r1 without being a
        // member (arranged in the store); ann has a thread on r1 that nobody answered
        var world = await InboxTestWorld.CreateAsync();
        await world.CreateReviewAsync("r1");
        await world.Store.AddSlotAsync(
            new ReviewerSlot { ProjectId = world.ProjectId, ReviewId = "r1", PrimaryUserId = world.Ids["root"] },
            _ => AuditTestExtensions.Draft(),
            Ct);
        await world.StartThreadAsync("r1", "ann");
        await world.CreateReviewAsync("r2", "root");
        await world.FailCheckAsync("r2", "bob");

        // Act
        var view = await world.InboxOfAsync("root");

        // Assert: only the failed checks of the own review; nothing of r1
        var row = Assert.Single(view.Items, item => item.Section == InboxSection.FailedChecks);
        Assert.Equal("r2", row.ReviewId);
        Assert.DoesNotContain(view.Items, item => item.ReviewId == "r1" && item.Section != InboxSection.Updates);
        Assert.Equal(1, view.ActionCount);
    }

    /// <summary>
    ///     Test that marking a review as seen never moves the state backwards, keeps the later time, and stores
    ///     nothing for a user who may not see the review.
    /// </summary>
    [Fact]
    public async Task InboxService_MarkSeenAsync_NeverMovesBackwards()
    {
        // Arrange
        var world = await InboxTestWorld.CreateAsync();
        await world.CreateReviewAsync("r1");
        var ann = InboxTestWorld.ActorOf("ann");
        var later = InboxTestWorld.Start.AddHours(2);
        var earlier = InboxTestWorld.Start.AddHours(1);

        // Act
        var first = await world.Inbox.MarkSeenAsync(ann, world.ProjectId, "r1", 5, later, Ct);
        var second = await world.Inbox.MarkSeenAsync(ann, world.ProjectId, "r1", 3, earlier, Ct);
        var strangerStored = await world.Inbox.MarkSeenAsync(InboxTestWorld.ActorOf("out"), world.ProjectId, "r1", 1, null, Ct);
        var missingStored = await world.Inbox.MarkSeenAsync(ann, world.ProjectId, "r-none", 1, null, Ct);
        var unknownStored = await world.Inbox.MarkSeenAsync(new Actor("ghost", "Ghost"), world.ProjectId, "r1", 1, null, Ct);

        // Assert: one row that kept the higher id and the later time; the refused calls stored nothing
        Assert.True(first);
        Assert.True(second);
        Assert.False(strangerStored);
        Assert.False(missingStored);
        Assert.False(unknownStored);
        var state = Assert.Single(world.InboxStore.States);
        Assert.Equal(world.Ids["ann"], state.UserId);
        Assert.Equal(5, state.LastSeenCommentId);
        Assert.Equal(later, state.LastSeenAt);
    }

    /// <summary>
    ///     Test that marking everything as read stores a state for every listed review, including the ones that
    ///     were never opened, with the highest comment, and that new comments are unread again.
    /// </summary>
    [Fact]
    public async Task InboxService_MarkAllReadAsync_UpsertsEveryListedReview()
    {
        // Arrange: r1 has a waiting thread, r2 only a slot, both for ann
        var world = await InboxTestWorld.CreateAsync();
        await world.CreateReviewAsync("r1");
        await world.AddSlotAsync("r1", "ann");
        var thread = await world.StartThreadAsync("r1", "ann");
        var reply = await world.ReplyAsync("r1", thread.Id, "bob");
        await world.CreateReviewAsync("r2");
        await world.AddSlotAsync("r2", "ann");
        var ann = InboxTestWorld.ActorOf("ann");
        Assert.All((await world.InboxOfAsync("ann")).Items, item => Assert.True(item.IsUnread));
        world.Clock.Advance(TimeSpan.FromMinutes(1));

        // Act
        var marked = await world.Inbox.MarkAllReadAsync(ann, Ct);

        // Assert: both reviews have a row, r1 up to the reply, and nothing is unread now
        Assert.Equal(2, marked);
        Assert.Equal(2, world.InboxStore.States.Count);
        var r1 = world.InboxStore.States.Single(state => state.ReviewId == "r1");
        Assert.Equal(reply.Id, r1.LastSeenCommentId);
        Assert.Equal(InboxTestWorld.Start.AddMinutes(1), r1.LastSeenAt);
        Assert.Equal(0, world.InboxStore.States.Single(state => state.ReviewId == "r2").LastSeenCommentId);
        Assert.All((await world.InboxOfAsync("ann")).Items, item => Assert.False(item.IsUnread));

        // Act: eve answers later
        world.Clock.Advance(TimeSpan.FromMinutes(1));
        await world.ReplyAsync("r1", thread.Id, "eve");

        // Assert: the waiting thread is new again
        var row = Assert.Single(await world.RowsAsync("ann", InboxSection.RepliesWaiting));
        Assert.True(row.IsUnread);

        // Assert: a user who is not known marks nothing
        Assert.Equal(0, await world.Inbox.MarkAllReadAsync(new Actor("ghost", "Ghost"), Ct));
    }

    /// <summary>
    ///     Arranges a review with the three reviewers ann, bob and cyd, in which ann and bob handed in their ballots and
    ///     cyd did not; bob gave a Fail on the first check when a dispute is wanted, so ann and bob answered it
    ///     differently.
    /// </summary>
    /// <param name="dispute">Whether bob answers the first check differently from ann.</param>
    /// <returns>The arranged world.</returns>
    private static async Task<InboxTestWorld> ThreeReviewersAsync(bool dispute)
    {
        var world = await InboxTestWorld.CreateAsync();
        await world.CreateReviewAsync("r1");
        await world.AddSlotAsync("r1", "ann");
        await world.AddSlotAsync("r1", "bob");
        await world.AddSlotAsync("r1", "cyd");
        await world.SubmitBallotAsync("r1", "ann");
        await world.SubmitBallotAsync("r1", "bob", dispute);
        return world;
    }

    /// <summary>
    ///     Test that a reviewer who has not started the ballot finds the review under To answer, as not started.
    /// </summary>
    [Fact]
    public async Task InboxService_GetInboxAsync_NoBallot_ShowsToAnswerNotStarted()
    {
        // Arrange: ann is seated in a review with two slots and has answered nothing
        var world = await InboxTestWorld.CreateAsync();
        await world.CreateReviewAsync("r1");
        await world.AddSlotAsync("r1", "ann");
        await world.AddSlotAsync("r1", "bob");

        // Act
        var view = await world.InboxOfAsync("ann");

        // Assert: one To answer row that says nothing is answered yet, counted as work
        var row = Assert.Single(view.Items);
        Assert.Equal(InboxSection.ToAnswer, row.Section);
        Assert.Equal(InboxService.NotStartedChip, row.Chip);
        Assert.StartsWith("0 of ", row.Detail, StringComparison.Ordinal);
        Assert.EndsWith("checks answered. Hand in your ballot when you are done.", row.Detail, StringComparison.Ordinal);
        Assert.True(row.IsUnread);
        Assert.Equal(1, view.ActionCount);
    }

    /// <summary>
    ///     Test that a reviewer with a ballot that is only started finds the review under To answer, as a draft with the
    ///     number of answered checks.
    /// </summary>
    [Fact]
    public async Task InboxService_GetInboxAsync_DraftBallot_ShowsToAnswerDraftWithProgressText()
    {
        // Arrange: ann answers the first check only
        var world = await InboxTestWorld.CreateAsync();
        await world.CreateReviewAsync("r1");
        await world.AddSlotAsync("r1", "ann");
        await world.StartBallotAsync("r1", "ann");

        // Act
        var row = Assert.Single(await world.RowsAsync("ann", InboxSection.ToAnswer));

        // Assert
        Assert.Equal(InboxService.DraftChip, row.Chip);
        Assert.StartsWith("1 of ", row.Detail, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that the alternate reviewer of a slot whose ballot is not handed in also finds the review under To answer.
    /// </summary>
    [Fact]
    public async Task InboxService_GetInboxAsync_AlternateHolder_ShowsToAnswer()
    {
        // Arrange
        var world = await InboxTestWorld.CreateAsync();
        await world.CreateReviewAsync("r1");
        await world.AddSlotAsync("r1", "ann", "bob");

        // Act
        var rows = await world.RowsAsync("bob", InboxSection.ToAnswer);

        // Assert
        Assert.Equal("r1", Assert.Single(rows).ReviewId);
    }

    /// <summary>
    ///     Test that the author who holds a slot gets no To answer row while the project forbids the author to answer,
    ///     and gets it once the rule is off.
    /// </summary>
    [Fact]
    public async Task InboxService_GetInboxAsync_AuthorBlockedViewer_ShowsNoToAnswer()
    {
        // Arrange: ann authored the review and holds a slot of it (arranged in the store, which the rules would refuse)
        var world = await InboxTestWorld.CreateAsync();
        await world.CreateReviewAsync("r1", "ann");
        await world.Store.AddSlotAsync(
            new ReviewerSlot { ProjectId = world.ProjectId, ReviewId = "r1", PrimaryUserId = world.Ids["ann"] },
            _ => AuditTestExtensions.Draft(),
            Ct);

        // Act
        var blocked = await world.InboxOfAsync("ann");
        await TestServices.Projects(world.Store, world.Clock).SetReviewRuleOffAsync(
            world.ProjectId, ReviewRule.NonAuthorChecklistAnswers, Ct);
        var allowed = await world.RowsAsync("ann", InboxSection.ToAnswer);

        // Assert
        Assert.Empty(blocked.Items);
        Assert.Single(allowed);
    }

    /// <summary>
    ///     Test that a review without a checklist has no To answer row: there is nothing to answer.
    /// </summary>
    [Fact]
    public async Task InboxService_GetInboxAsync_NoChecklist_ShowsNoToAnswer()
    {
        // Arrange: a stored review without a checklist, with a slot of ann
        var world = await InboxTestWorld.CreateAsync();
        await world.Store.AddReviewAsync(
            new Review { ProjectId = world.ProjectId, Id = "r-none", Title = "No checklist", Created = InboxTestWorld.Start },
            Ct);
        await world.Store.AddSlotAsync(
            new ReviewerSlot { ProjectId = world.ProjectId, ReviewId = "r-none", PrimaryUserId = world.Ids["ann"] },
            _ => AuditTestExtensions.Draft(),
            Ct);

        // Act
        var view = await world.InboxOfAsync("ann");

        // Assert: nothing to answer or sign; the review waits for a checklist
        Assert.DoesNotContain(view.Items, item => item.Section is InboxSection.ToAnswer or InboxSection.ToSign);
        var row = Assert.Single(view.Items);
        Assert.Equal(InboxSection.WaitingForOthers, row.Section);
        Assert.Equal("This review has no checklist yet.", row.Detail);
    }

    /// <summary>
    ///     Test that a reviewer whose handed-in ballot is changed is back under To answer, as a draft.
    /// </summary>
    [Fact]
    public async Task InboxService_GetInboxAsync_RevertedToDraft_ReturnsToToAnswer()
    {
        // Arrange: ann handed in her ballot, then changes an answer
        var world = await InboxTestWorld.CreateAsync();
        await world.CreateReviewAsync("r1");
        await world.AddSlotAsync("r1", "ann");
        await world.AddSlotAsync("r1", "bob");
        await world.SubmitBallotAsync("r1", "ann");
        Assert.Empty(await world.RowsAsync("ann", InboxSection.ToAnswer));
        var item = DefaultChecklist.Current.Sections[0].Items[0];

        // Act
        await world.Checklists.SetAnswerAsync(
            world.ProjectId, "r1", item.Id, ChecklistOutcome.Fail, "It is wrong", InboxTestWorld.ActorOf("ann"), Ct);
        var row = Assert.Single(await world.RowsAsync("ann", InboxSection.ToAnswer));

        // Assert
        Assert.Equal(InboxService.DraftChip, row.Chip);
    }

    /// <summary>
    ///     Test that a reviewer who has not handed in sees the same inbox whether the other reviewers disagree or not:
    ///     no row, chip, text, unread marker or count gives the dispute away.
    /// </summary>
    [Fact]
    public async Task InboxService_GetInboxAsync_HiddenViewerWithDispute_ShowsNoDisputedRowOrCount()
    {
        // Arrange: the same review twice, cyd has not handed in; ann and bob agree in one, disagree in the other
        var agreed = await ThreeReviewersAsync(false);
        var disagreed = await ThreeReviewersAsync(true);

        // Act
        var without = await agreed.InboxOfAsync("cyd");
        var with = await disagreed.InboxOfAsync("cyd");

        // Assert: identical, with a To answer row and no word about disputes
        Assert.Equal(
            without.Items.Select(item => (item.Section, item.Chip, item.Detail, item.IsUnread)),
            with.Items.Select(item => (item.Section, item.Chip, item.Detail, item.IsUnread)));
        Assert.Equal(without.ActionCount, with.ActionCount);
        Assert.Equal(
            await agreed.Inbox.CountAsync(InboxTestWorld.ActorOf("cyd"), Ct),
            await disagreed.Inbox.CountAsync(InboxTestWorld.ActorOf("cyd"), Ct));
        var row = Assert.Single(with.Items);
        Assert.Equal(InboxSection.ToAnswer, row.Section);
        Assert.DoesNotContain("dispute", row.Chip + row.Detail, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     Test that a reviewer who handed in a ballot finds a review whose ballots disagree under Disputed, with the
    ///     number, and not under Ready to sign.
    /// </summary>
    [Fact]
    public async Task InboxService_GetInboxAsync_SubmittedViewerWithDispute_ShowsDisputedWithCount()
    {
        // Arrange: ann and bob answered the first check differently
        var world = await ThreeReviewersAsync(true);

        // Act
        var view = await world.InboxOfAsync("ann");

        // Assert
        var row = Assert.Single(view.Items);
        Assert.Equal(InboxSection.Disputed, row.Section);
        Assert.Equal("1 disputed", row.Chip);
        Assert.Equal("Change your answer or discuss it.", row.Detail);
        Assert.Equal(1, view.ActionCount);
    }

    /// <summary>
    ///     Test that the author, who may see every ballot and holds no slot, finds the disputed review under Disputed.
    /// </summary>
    [Fact]
    public async Task InboxService_GetInboxAsync_AuthorWithDispute_ShowsDisputed()
    {
        // Arrange
        var world = await ThreeReviewersAsync(true);

        // Act
        var row = Assert.Single(await world.RowsAsync("author", InboxSection.Disputed));

        // Assert
        Assert.Equal("1 disputed", row.Chip);
        Assert.Equal("The reviewers answered differently. Discuss it with them.", row.Detail);
    }

    /// <summary>
    ///     Test that the Disputed row goes away once the reviewers agree, and the reviewer is asked to sign.
    /// </summary>
    [Fact]
    public async Task InboxService_GetInboxAsync_DisputeResolved_RowDisappears()
    {
        // Arrange: a dispute that bob settles by handing in a ballot that agrees with ann's
        var world = await ThreeReviewersAsync(true);
        Assert.Single(await world.RowsAsync("ann", InboxSection.Disputed));

        // Act
        await world.SubmitBallotAsync("r1", "bob");

        // Assert
        Assert.Empty(await world.RowsAsync("ann", InboxSection.Disputed));
        Assert.Single(await world.RowsAsync("ann", InboxSection.ToSign));
    }

    /// <summary>
    ///     Test that a Disputed row of a user who has seen the review turns unread again when another reviewer hands in
    ///     a ballot, and not before.
    /// </summary>
    [Fact]
    public async Task InboxService_GetInboxAsync_DisputedUnread_AfterOtherSubmission()
    {
        // Arrange: ann has seen the dispute
        var world = await ThreeReviewersAsync(true);
        world.Clock.Advance(TimeSpan.FromMinutes(1));
        await world.Inbox.MarkSeenAsync(InboxTestWorld.ActorOf("ann"), world.ProjectId, "r1", 0, null, Ct);
        var seen = Assert.Single(await world.RowsAsync("ann", InboxSection.Disputed));
        world.Clock.Advance(TimeSpan.FromMinutes(1));

        // Act: bob hands in his ballot again, still different
        await world.SubmitBallotAsync("r1", "bob", true);
        var after = Assert.Single(await world.RowsAsync("ann", InboxSection.Disputed));

        // Assert
        Assert.False(seen.IsUnread);
        Assert.True(after.IsUnread);
    }

    /// <summary>
    ///     Test that a reviewer whose ballot and the other ballots are in and nothing blocks finds the review under
    ///     Ready to sign.
    /// </summary>
    [Fact]
    public async Task InboxService_GetInboxAsync_SubmittedOwnBallotNothingBlocking_ShowsReadyToSign()
    {
        // Arrange: ann and bob handed in the same answers
        var world = await InboxTestWorld.CreateAsync();
        await world.CreateReviewAsync("r1");
        await world.AddSlotAsync("r1", "ann");
        await world.AddSlotAsync("r1", "bob");
        await world.SubmitBallotAsync("r1", "ann");
        await world.SubmitBallotAsync("r1", "bob");

        // Act
        var forAnn = await world.InboxOfAsync("ann");

        // Assert
        var row = Assert.Single(forAnn.Items);
        Assert.Equal(InboxSection.ToSign, row.Section);
        Assert.Equal(InboxService.NotSignedChip, row.Chip);
    }

    /// <summary>
    ///     Test that an open thread keeps a reviewer who handed in from being asked to sign: the row is a waiting row
    ///     that says open items remain, and no work.
    /// </summary>
    [Fact]
    public async Task InboxService_GetInboxAsync_OpenThread_ShowsWaitingNotReadyToSign()
    {
        // Arrange: ann handed in her one ballot and bob opens a thread
        var world = await InboxTestWorld.CreateAsync();
        await world.CreateReviewAsync("r1");
        await world.AnswerAllAsync("r1");
        await world.AddSlotAsync("r1", "ann");
        await world.StartThreadAsync("r1", "bob");

        // Act
        var view = await world.InboxOfAsync("ann");

        // Assert
        var row = Assert.Single(view.Items);
        Assert.Equal(InboxSection.WaitingForOthers, row.Section);
        Assert.Equal("Open items remain", row.Chip);
        Assert.StartsWith("Open items remain:", row.Detail, StringComparison.Ordinal);
        Assert.Equal(0, view.ActionCount);
    }

    /// <summary>
    ///     Test that a waiting row, which is no work, shows no due date and no overdue marker even when the review is
    ///     overdue.
    /// </summary>
    [Fact]
    public async Task InboxService_GetInboxAsync_WaitingForOthersOverdueReview_ShowsNoDueDateOrOverdue()
    {
        // Arrange: ann handed in her ballot, bob opens a thread, and the due date of the review has passed
        var world = await InboxTestWorld.CreateAsync();
        var due = DateOnly.FromDateTime(InboxTestWorld.Start.UtcDateTime).AddDays(1);
        await world.CreateReviewAsync("r1", dueDate: due);
        await world.AnswerAllAsync("r1");
        await world.AddSlotAsync("r1", "ann");
        await world.StartThreadAsync("r1", "bob");
        world.Clock.Advance(TimeSpan.FromDays(3));

        // Act
        var view = await world.InboxOfAsync("ann");

        // Assert: the only row is a waiting row without a due date or overdue marker
        var row = Assert.Single(view.Items);
        Assert.Equal(InboxSection.WaitingForOthers, row.Section);
        Assert.Null(row.DueDate);
        Assert.False(row.IsOverdue);
    }

    /// <summary>
    ///     Test that a reviewer who signed finds the review under Waiting for others, with the names of the people whose
    ///     signature is missing, that the row is never unread and that it is no work.
    /// </summary>
    [Fact]
    public async Task InboxService_GetInboxAsync_OwnSlotSignedOthersPending_ShowsWaitingWithNames()
    {
        // Arrange: ann and bob handed in; ann signs, bob has not
        var world = await InboxTestWorld.CreateAsync();
        await world.CreateReviewAsync("r1");
        await world.AnswerAllAsync("r1");
        var first = await world.AddSlotAsync("r1", "ann");
        await world.AddSlotAsync("r1", "bob", "cyd");
        await world.SignAsync("r1", first.Id, "ann");

        // Act
        var view = await world.InboxOfAsync("ann");
        var count = await world.Inbox.CountAsync(InboxTestWorld.ActorOf("ann"), Ct);

        // Assert: the row names the people of the open slot; it is never unread and not counted
        var row = Assert.Single(view.Items);
        Assert.Equal(InboxSection.WaitingForOthers, row.Section);
        Assert.Equal("Waiting for 1 reviewer", row.Chip);
        Assert.Equal("Waiting for Bob, Cyd.", row.Detail);
        Assert.False(row.IsUnread);
        Assert.Equal(0, view.ActionCount);
        Assert.Equal(0, count);
    }

    /// <summary>
    ///     Test that the action count counts the To answer and Disputed reviews, one each, and not the waiting ones.
    /// </summary>
    [Fact]
    public async Task InboxService_GetInboxAsync_ActionCount_IncludesToAnswerAndDisputedNotWaiting()
    {
        // Arrange: r1 is disputed for ann, r2 waits for her answers, r3 waits for others after her signature
        var world = await ThreeReviewersAsync(true);
        await world.CreateReviewAsync("r2");
        await world.AddSlotAsync("r2", "ann");
        await world.CreateReviewAsync("r3");
        await world.AnswerAllAsync("r3");
        var slot = await world.AddSlotAsync("r3", "ann");
        await world.AddSlotAsync("r3", "bob");
        await world.SignAsync("r3", slot.Id, "ann");

        // Act
        var view = await world.InboxOfAsync("ann");
        var count = await world.Inbox.CountAsync(InboxTestWorld.ActorOf("ann"), Ct);

        // Assert
        Assert.Equal(
            [InboxSection.ToAnswer, InboxSection.Disputed, InboxSection.WaitingForOthers],
            view.Items.Select(item => item.Section).OrderBy(section => (int)section));
        Assert.Equal(2, view.ActionCount);
        Assert.Equal(2, count);
    }

    /// <summary>
    ///     Test that null arguments are refused where a value is required.
    /// </summary>
    [Fact]
    public async Task InboxService_NullArguments_Throw()
    {
        // Arrange
        var world = await InboxTestWorld.CreateAsync();
        var store = world.InboxStore;

        // Act, Assert: the constructor
        Assert.Throws<ArgumentNullException>(() => new InboxService(null!, world.Store, world.Access, world.UserStore, world.Clock));
        Assert.Throws<ArgumentNullException>(() => new InboxService(store, null!, world.Access, world.UserStore, world.Clock));
        Assert.Throws<ArgumentNullException>(() => new InboxService(store, world.Store, null!, world.UserStore, world.Clock));
        Assert.Throws<ArgumentNullException>(() => new InboxService(store, world.Store, world.Access, null!, world.Clock));
        Assert.Throws<ArgumentNullException>(() => new InboxService(store, world.Store, world.Access, world.UserStore, null!));

        // Act, Assert: the pure helpers
        Assert.Throws<ArgumentNullException>(() => InboxService.IsUnread(null!, "ann", 0));
        Assert.Throws<ArgumentNullException>(() => InboxService.IsUnread(new ReviewComment(), null!, 0));
        Assert.Throws<ArgumentNullException>(() => InboxService.CountUnread(null!, "ann", 0));
        Assert.Throws<ArgumentNullException>(() => InboxService.CountUnread([], null!, 0));
        Assert.Throws<ArgumentNullException>(() => InboxService.MaxCommentId(null!));

        // Act, Assert: the operations that need a signed-in user
        await Assert.ThrowsAsync<ArgumentNullException>(() => world.Inbox.GetReviewUnreadAsync(null!, world.ProjectId, "r1", Ct));
        await Assert.ThrowsAsync<ArgumentNullException>(() => world.Inbox.MarkSeenAsync(null!, world.ProjectId, "r1", 0, null, Ct));
        await Assert.ThrowsAsync<ArgumentNullException>(() => world.Inbox.MarkAllReadAsync(null!, Ct));
    }
}
