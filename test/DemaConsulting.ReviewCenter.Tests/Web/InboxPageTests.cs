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

using Bunit;
using DemaConsulting.ReviewCenter.Core;
using DemaConsulting.ReviewCenter.Web;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace DemaConsulting.ReviewCenter.Tests.Web;

/// <summary>
///     Unit tests for the <see cref="InboxPage"/> component, rendered in memory with bUnit.
/// </summary>
public class InboxPageTests : InboxWebTestBase
{
    /// <summary>
    ///     Test that nobody signed in sees no inbox: the page body is replaced by the session-ended message.
    /// </summary>
    [Fact]
    public async Task InboxPage_Render_Anonymous_IsNotShown()
    {
        // Arrange: a review that would be listed, and nobody signed in
        await ArrangeProjectAsync();
        await CreateInboxReviewAsync("r1");
        await AddSlotAsync("r1", "ann");
        SignOut();

        // Act
        var cut = Render<InboxPage>();

        // Assert: no inbox, no rows
        Assert.Empty(cut.FindAll("[data-testid='inbox-page']"));
        Assert.Empty(cut.FindAll("[data-testid='inbox-row-1']"));
        Assert.NotEmpty(cut.FindAll("[data-testid='session-ended']"));
    }

    /// <summary>
    ///     Test that the page reports that it is interactive once it has rendered, and offers the button that needs the
    ///     live session of the user.
    /// </summary>
    [Fact]
    public async Task InboxPage_AfterRender_Interactive_SetsMarkerAndOffersMarkAllRead()
    {
        // Arrange: ann has a review waiting, so that the button that marks everything as read is offered
        await ArrangeProjectAsync();
        await CreateInboxReviewAsync("r1");
        await AddSlotAsync("r1", "ann");
        SignInAs("ann", "Ann", false);

        // Act: render the page (bUnit runs OnAfterRender like a live circuit does)
        var cut = Render<InboxPage>();

        // Assert: the root element reports interactivity and the button is there
        Assert.Equal("true", cut.Find("[data-testid='inbox-page']").GetAttribute("data-interactive"));
        Assert.NotNull(cut.Find("[data-testid='inbox-mark-read']"));
    }

    /// <summary>
    ///     Test that the page reports that it is interactive once it has rendered, also when nothing is listed.
    /// </summary>
    [Fact]
    public async Task InboxPage_AfterRender_NothingWaiting_SetsInteractiveMarker()
    {
        // Arrange: ann is a member of a project without reviews, so that the inbox lists nothing
        await ArrangeProjectAsync();
        SignInAs("ann", "Ann", false);

        // Act: render the page (bUnit runs OnAfterRender like a live circuit does)
        var cut = Render<InboxPage>();

        // Assert: the root element reports interactivity although no row is listed
        Assert.Equal("true", cut.Find("[data-testid='inbox-page']").GetAttribute("data-interactive"));
        Assert.Empty(cut.FindAll("[data-testid='inbox-row-1']"));
    }

    /// <summary>
    ///     Test that the Mark all as read button is offered while the inbox lists a review.
    /// </summary>
    [Fact]
    public async Task InboxPage_Render_ItemsListed_OffersMarkAllReadButton()
    {
        // Arrange: ann has a review waiting
        await ArrangeProjectAsync();
        await CreateInboxReviewAsync("r1");
        await AddSlotAsync("r1", "ann");
        SignInAs("ann", "Ann", false);

        // Act
        var cut = Render<InboxPage>();

        // Assert: a row is listed and the button says what it does
        Assert.NotEmpty(cut.FindAll("[data-testid='inbox-row-1']"));
        Assert.Equal("Mark all as read", cut.Find("[data-testid='inbox-mark-read']").TextContent);
    }

    /// <summary>
    ///     Test that a reply waiting is shown under the titled section Replies waiting, with the row of the review.
    /// </summary>
    [Fact]
    public async Task InboxPage_Render_RepliesWaiting_ShowsTitledSection()
    {
        // Arrange: ann is a reviewer of r1 who asked a question and bob answered
        await ArrangeProjectAsync();
        await CreateInboxReviewAsync("r1");
        await AddSlotAsync("r1", "ann");
        await StartAndAnswerThreadAsync("r1", "ann", "bob");
        SignInAs("ann", "Ann", false);

        // Act
        var cut = Render<InboxPage>();

        // Assert: the section has its title and holds the row of the review
        Assert.Equal("Replies waiting", cut.Find("[data-testid='inbox-section-replies-waiting']").TextContent);
        var list = cut.Find("[data-testid='inbox-list-replies-waiting']");
        Assert.Contains("Apollo / r1", list.TextContent, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that the author of a review, who took no part in a thread, finds the review under the titled section
    ///     Replies waiting when someone else wrote the latest comment of the thread.
    /// </summary>
    [Fact]
    public async Task InboxPage_Render_RepliesWaitingOnOwnReview_ShowsTitledSection()
    {
        // Arrange: ann is a reviewer of r1 who asked a question and bob answered; the author took no part in the thread
        await ArrangeProjectAsync();
        await CreateInboxReviewAsync("r1");
        await AddSlotAsync("r1", "ann");
        await StartAndAnswerThreadAsync("r1", "ann", "bob");
        SignInAs("author", "Author", false);

        // Act
        var cut = Render<InboxPage>();

        // Assert: the section has its title and holds the row of the review with the count of replies waiting
        Assert.Equal("Replies waiting", cut.Find("[data-testid='inbox-section-replies-waiting']").TextContent);
        var list = cut.Find("[data-testid='inbox-list-replies-waiting']");
        Assert.Contains("Apollo / r1", list.TextContent, StringComparison.Ordinal);
        Assert.Contains("1 waiting", list.TextContent, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that the router, which applies the authorization rules of a page, does not show the inbox to a visitor
    ///     who is not signed in and points the visitor to the login page, which returns to the inbox.
    /// </summary>
    [Fact]
    public async Task InboxPage_Render_AnonymousThroughRouter_ShowsLoginLinkAndNoInbox()
    {
        // Arrange: a review that would be listed, nobody signed in, and the router at /inbox
        await ArrangeProjectAsync();
        await CreateInboxReviewAsync("r1");
        await AddSlotAsync("r1", "ann");
        SignOut();
        Services.GetRequiredService<NavigationManager>().NavigateTo("/inbox");

        // Act: render the router
        var cut = Render<Routes>();

        // Assert: no inbox, and a link to the login page that returns to the inbox
        Assert.Empty(cut.FindAll("[data-testid='inbox-page']"));
        Assert.Empty(cut.FindAll("[data-testid='inbox-row-1']"));
        Assert.Equal(
            "/login?ReturnUrl=" + Uri.EscapeDataString("/inbox"),
            cut.Find("[data-testid='access-denied-login-link']").GetAttribute("href"));
    }

    /// <summary>
    ///     Test that a user with nothing waiting sees the empty state, a zero summary and no mark-as-read button.
    /// </summary>
    [Fact]
    public async Task InboxPage_Render_NothingWaiting_ShowsEmptyState()
    {
        // Arrange: ann is a member of a project without reviews
        await ArrangeProjectAsync();
        SignInAs("ann", "Ann", false);

        // Act
        var cut = Render<InboxPage>();

        // Assert
        Assert.Equal("Nothing needs your action.", cut.Find("[data-testid='inbox-empty']").TextContent);
        Assert.Equal("0 need your action, 0 updates", cut.Find("[data-testid='inbox-summary']").TextContent);
        Assert.Empty(cut.FindAll("[data-testid='inbox-mark-read']"));
        Assert.Empty(cut.FindAll("[data-testid='inbox-row-1']"));
    }

    /// <summary>
    ///     Test that a ballot to hand in and a reply waiting are shown in their sections, with the review, the chip and
    ///     a link to the checklist for the ballot and to the review for the reply.
    /// </summary>
    [Fact]
    public async Task InboxPage_Render_ToAnswerAndRepliesWaiting_ShowsSectionsRowsChipsAndLinks()
    {
        // Arrange: ann is a reviewer of r1 who has not started, asked a question and bob answered
        await ArrangeProjectAsync();
        await CreateInboxReviewAsync("r1");
        await AddSlotAsync("r1", "ann");
        await StartAndAnswerThreadAsync("r1", "ann", "bob");
        SignInAs("ann", "Ann", false);

        // Act
        var cut = Render<InboxPage>();

        // Assert: both sections, in order, each with one row
        Assert.NotEmpty(cut.FindAll("[data-testid='inbox-section-to-answer']"));
        Assert.NotEmpty(cut.FindAll("[data-testid='inbox-section-replies-waiting']"));
        Assert.Empty(cut.FindAll("[data-testid='inbox-section-to-sign']"));
        Assert.Empty(cut.FindAll("[data-testid='inbox-section-failed-checks']"));
        Assert.Empty(cut.FindAll("[data-testid='inbox-section-updates']"));
        Assert.Equal("Not started", cut.Find("[data-testid='inbox-chip-1']").TextContent);
        Assert.Equal("1 waiting", cut.Find("[data-testid='inbox-chip-2']").TextContent);
        Assert.Equal("Apollo / r1", cut.Find("[data-testid='inbox-review-1']").TextContent);
        Assert.Equal("Review r1", cut.Find("[data-testid='inbox-link-1']").TextContent);
        Assert.Equal($"/projects/{ProjectId}/reviews/r1/checklist", cut.Find("[data-testid='inbox-link-1']").GetAttribute("href"));
        Assert.Equal($"/projects/{ProjectId}/reviews/r1", cut.Find("[data-testid='inbox-link-2']").GetAttribute("href"));
        Assert.Equal("1 needs your action, 0 updates", cut.Find("[data-testid='inbox-summary']").TextContent);
        Assert.Empty(cut.FindAll("[data-testid='inbox-empty']"));
    }

    /// <summary>
    ///     Test that the sections for a ballot to hand in, a dispute and a signature to give are shown with their titles
    ///     and chips, and that the ballot and the dispute link to the checklist.
    /// </summary>
    [Fact]
    public async Task InboxPage_Render_NewSections_ShowTitlesChipsAndChecklistLinks()
    {
        // Arrange: r1 not started; r2 handed in by both; r3 handed in by both with a different answer
        await ArrangeProjectAsync();
        await CreateInboxReviewAsync("r1");
        await AddSlotAsync("r1", "ann");
        await CreateInboxReviewAsync("r2");
        await AddSlotAsync("r2", "ann");
        await AddSlotAsync("r2", "bob");
        await AnswerAllAsync("r2", "ann");
        await AnswerAllAsync("r2", "bob");
        await CreateInboxReviewAsync("r3");
        await AddSlotAsync("r3", "ann");
        await AddSlotAsync("r3", "bob");
        await AnswerAllAsync("r3", "ann");
        var first = DefaultChecklist.Current.Sections.SelectMany(section => section.Items).First();
        foreach (var item in DefaultChecklist.Current.Sections.SelectMany(section => section.Items))
        {
            var outcome = item.Id == first.Id ? ChecklistOutcome.Fail : ChecklistOutcome.Pass;
            await Checklists.SetAnswerAsync(
                ProjectId, "r3", item.Id, outcome, outcome == ChecklistOutcome.Pass ? null : "Wrong", ActorOf("bob"), Ct);
        }

        await Checklists.SubmitBallotAsync(ProjectId, "r3", ActorOf("bob"), Ct);
        SignInAs("ann", "Ann", false);

        // Act
        var cut = Render<InboxPage>();

        // Assert: the sections in order with their titles
        Assert.Equal("To answer", cut.Find("[data-testid='inbox-section-to-answer']").TextContent);
        Assert.Equal("Disputed", cut.Find("[data-testid='inbox-section-disputed']").TextContent);
        Assert.Equal("Ready to sign", cut.Find("[data-testid='inbox-section-to-sign']").TextContent);

        // Assert: the rows, chips and links
        Assert.Equal("Not started", cut.Find("[data-testid='inbox-chip-1']").TextContent);
        Assert.Equal($"/projects/{ProjectId}/reviews/r1/checklist", cut.Find("[data-testid='inbox-link-1']").GetAttribute("href"));
        Assert.Equal($"/projects/{ProjectId}/reviews/r3/checklist", cut.Find("[data-testid='inbox-link-2']").GetAttribute("href"));
        Assert.Equal("1 disputed", cut.Find("[data-testid='inbox-chip-2']").TextContent);
        Assert.Equal($"/projects/{ProjectId}/reviews/r2", cut.Find("[data-testid='inbox-link-3']").GetAttribute("href"));
        Assert.Equal("Not signed", cut.Find("[data-testid='inbox-chip-3']").TextContent);
        Assert.Equal("3 need your action, 0 updates", cut.Find("[data-testid='inbox-summary']").TextContent);
    }

    /// <summary>
    ///     Test that a review that waits for the other reviewers is listed for information, with the names of the
    ///     people waited for, and is not counted as work.
    /// </summary>
    [Fact]
    public async Task InboxPage_Render_WaitingForOthers_NotCountedInSummary()
    {
        // Arrange: ann handed in and signed r1; bob has not handed in
        await ArrangeProjectAsync();
        await CreateInboxReviewAsync("r1");
        var annSlot = await AddSlotAsync("r1", "ann");
        await AddSlotAsync("r1", "bob");
        await AnswerAllAsync("r1", "ann");
        await SignSlotAsync("r1", annSlot.Id, "ann");
        SignInAs("ann", "Ann", false);

        // Act
        var cut = Render<InboxPage>();

        // Assert: the section is shown, the empty state tells there is no action, the summary counts the waiting
        Assert.Equal("Waiting for others", cut.Find("[data-testid='inbox-section-waiting-for-others']").TextContent);
        Assert.Equal("Waiting for 1 reviewer", cut.Find("[data-testid='inbox-chip-1']").TextContent);
        Assert.Contains("Bob", cut.Find("[data-testid='inbox-detail-1']").TextContent, StringComparison.Ordinal);
        Assert.Equal($"/projects/{ProjectId}/reviews/r1", cut.Find("[data-testid='inbox-link-1']").GetAttribute("href"));
        Assert.NotEmpty(cut.FindAll("[data-testid='inbox-empty']"));
        Assert.Equal("0 need your action, 0 updates, 1 waiting for others", cut.Find("[data-testid='inbox-summary']").TextContent);
    }

    /// <summary>
    ///     Test that a review with a check that a reviewer marked as Fail is shown to its author under the titled
    ///     section Failed checks.
    /// </summary>
    [Fact]
    public async Task InboxPage_Render_FailedChecks_ShowsTitledSection()
    {
        // Arrange: bob holds a slot of the review of the author and marks one check as Fail
        await ArrangeProjectAsync();
        await CreateInboxReviewAsync("r1");
        await AddSlotAsync("r1", "bob");
        var first = DefaultChecklist.Current.Sections.SelectMany(section => section.Items).First();
        await Checklists.SetAnswerAsync(ProjectId, "r1", first.Id, ChecklistOutcome.Fail, "Wrong", ActorOf("bob"), Ct);
        SignInAs("author", "Author", false);

        // Act
        var cut = Render<InboxPage>();

        // Assert: the section has its title and the row of the review
        Assert.Equal("Failed checks", cut.Find("[data-testid='inbox-section-failed-checks']").TextContent);
        Assert.Equal("Apollo / r1", cut.Find("[data-testid='inbox-review-1']").TextContent);
    }

    /// <summary>
    ///     Test that a review that was approved after the user signed is shown under the titled section Updates.
    /// </summary>
    [Fact]
    public async Task InboxPage_Render_Update_ShowsTitledSection()
    {
        // Arrange: ann and bob signed r1, so the review was approved after ann signed
        await ArrangeProjectAsync();
        await CreateInboxReviewAsync("r1");
        var annSlot = await AddSlotAsync("r1", "ann");
        var bobSlot = await AddSlotAsync("r1", "bob");
        await AnswerAllAsync("r1", "ann");
        await AnswerAllAsync("r1", "bob");
        await SignSlotAsync("r1", annSlot.Id, "ann");
        await SignSlotAsync("r1", bobSlot.Id, "bob");
        SignInAs("ann", "Ann", false);

        // Act
        var cut = Render<InboxPage>();

        // Assert: the section has its title and the row of the review
        Assert.Equal("Updates", cut.Find("[data-testid='inbox-section-updates']").TextContent);
        Assert.Equal("Apollo / r1", cut.Find("[data-testid='inbox-review-1']").TextContent);
        Assert.Single(
            cut.FindAll("[data-testid^='inbox-chip-']"),
            element => element.TextContent == InboxService.ApprovedChip);
    }

    /// <summary>
    ///     Test that a review that was rejected after the author last looked is shown to its author under the titled
    ///     section Updates, with the chip Rejected, the signer and a link to the review.
    /// </summary>
    [Fact]
    public async Task InboxPage_Render_UpdateRejected_ShowsTitledSectionAndChip()
    {
        // Arrange: ann is the only reviewer of r1, answers one check with Fail, writes the conclusion and signs
        await ArrangeProjectAsync();
        await CreateInboxReviewAsync("r1");
        var slot = await AddSlotAsync("r1", "ann");
        var items = DefaultChecklist.Current.Sections.SelectMany(section => section.Items).ToList();
        foreach (var item in items)
        {
            var failed = item == items[0];
            await Checklists.SetAnswerAsync(
                ProjectId,
                "r1",
                item.Id,
                failed ? ChecklistOutcome.Fail : ChecklistOutcome.Pass,
                failed ? "Wrong" : null,
                ActorOf("ann"),
                Ct);
        }

        await Checklists.SubmitBallotAsync(ProjectId, "r1", ActorOf("ann"), Ct);
        await Checklists.SetReviewConclusionTextAsync(ProjectId, "r1", "Not ready.", ActorOf("ann"), Ct);
        await SignSlotAsync("r1", slot.Id, "ann");
        SignInAs("author", "Author", false);

        // Act
        var cut = Render<InboxPage>();

        // Assert: the section has its title and a row with the chip Rejected, the signer and a link to the review
        Assert.Equal("Updates", cut.Find("[data-testid='inbox-section-updates']").TextContent);
        var chip = cut.FindAll("[data-testid^='inbox-chip-']").Single(element => element.TextContent == InboxService.RejectedChip);
        var number = chip.GetAttribute("data-testid")!["inbox-chip-".Length..];
        Assert.Equal(
            $"/projects/{ProjectId}/reviews/r1",
            cut.Find($"[data-testid='inbox-link-{number}']").GetAttribute("href"));
        Assert.Contains(
            "Signed by Ann",
            cut.Find($"[data-testid='inbox-detail-{number}']").TextContent,
            StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a review that an administrator reopened after the author last looked is shown to its author under
    ///     the titled section Updates, with the chip Reopened and the name of the administrator.
    /// </summary>
    [Fact]
    public async Task InboxPage_Render_UpdateReopened_ShowsTitledSectionAndChip()
    {
        // Arrange: ann is the only reviewer and signed, so r1 is approved; then an administrator reopens it
        await ArrangeProjectAsync();
        await CreateInboxReviewAsync("r1");
        var slot = await AddSlotAsync("r1", "ann");
        await AnswerAllAsync("r1", "ann");
        await SignSlotAsync("r1", slot.Id, "ann");
        await Users.CreateAsync("root", "Root", true, Password, Ct);
        await Signing.ReopenReviewAsync(ActorOf("root"), ProjectId, "r1", "Missed a case.", Ct);
        SignInAs("author", "Author", false);

        // Act
        var cut = Render<InboxPage>();

        // Assert: the section has its title and a row with the chip Reopened and the name of the administrator
        Assert.Equal("Updates", cut.Find("[data-testid='inbox-section-updates']").TextContent);
        var chip = cut.FindAll("[data-testid^='inbox-chip-']").Single(element => element.TextContent == InboxService.ReopenedChip);
        var number = chip.GetAttribute("data-testid")!["inbox-chip-".Length..];
        Assert.Contains(
            "Reopened by Root",
            cut.Find($"[data-testid='inbox-detail-{number}']").TextContent,
            StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a row the user has not seen carries the New marker, and that it is gone once the review was seen.
    /// </summary>
    [Fact]
    public async Task InboxPage_Render_UnreadItem_ShowsDot()
    {
        // Arrange: ann is a reviewer of r1 and has never opened it
        await ArrangeProjectAsync();
        await CreateInboxReviewAsync("r1");
        await AddSlotAsync("r1", "ann");
        SignInAs("ann", "Ann", false);

        // Act and Assert: the marker is shown
        var first = Render<InboxPage>();
        Assert.Equal("New", first.Find("[data-testid='inbox-unread-1']").TextContent);

        // Act: ann sees the review, then the page is rendered again
        Assert.True(await Inbox.MarkSeenAsync(ActorOf("ann"), ProjectId, "r1", 0, null, Ct));
        var second = Render<InboxPage>();

        // Assert: the row is still listed, without the marker
        Assert.Equal("Not started", second.Find("[data-testid='inbox-chip-1']").TextContent);
        Assert.Empty(second.FindAll("[data-testid='inbox-unread-1']"));
    }

    /// <summary>
    ///     Test that an open review past its due date is marked overdue and one that is not yet due is not.
    /// </summary>
    [Fact]
    public async Task InboxPage_Render_OverdueReview_ShowsOverdueChip()
    {
        // Arrange: ann reviews r1 (due today) and r2 (due in ten days)
        await ArrangeProjectAsync();
        var today = DateOnly.FromDateTime(Time.GetUtcNow().UtcDateTime);
        await CreateInboxReviewAsync("r1", dueDate: today);
        await CreateInboxReviewAsync("r2", dueDate: today.AddDays(10));
        await AddSlotAsync("r1", "ann");
        await AddSlotAsync("r2", "ann");
        SignInAs("ann", "Ann", false);

        // Act: two days later
        Time.Advance(TimeSpan.FromDays(2));
        var cut = Render<InboxPage>();

        // Assert: r1 is overdue, r2 is only due
        Assert.Equal($"Overdue {today:yyyy-MM-dd}", cut.Find("[data-testid='inbox-due-1']").TextContent);
        Assert.Contains("failed", cut.Find("[data-testid='inbox-due-1']").ClassList);
        Assert.Equal($"Due {today.AddDays(10):yyyy-MM-dd}", cut.Find("[data-testid='inbox-due-2']").TextContent);
        Assert.DoesNotContain("failed", cut.Find("[data-testid='inbox-due-2']").ClassList);
    }

    /// <summary>
    ///     Test that the summary line counts the reviews that need action and the updates separately.
    /// </summary>
    [Fact]
    public async Task InboxPage_Render_SummaryLine_ShowsActionAndUpdateCounts()
    {
        // Arrange: ann must sign r1 and r2, and r3 was approved by bob after ann signed
        await ArrangeProjectAsync();
        await CreateInboxReviewAsync("r1");
        await AddSlotAsync("r1", "ann");
        await CreateInboxReviewAsync("r2");
        await AddSlotAsync("r2", "ann");
        await CreateInboxReviewAsync("r3");
        var annSlot = await AddSlotAsync("r3", "ann");
        var bobSlot = await AddSlotAsync("r3", "bob");
        await AnswerAllAsync("r3", "ann");
        await AnswerAllAsync("r3", "bob");
        await SignSlotAsync("r3", annSlot.Id, "ann");
        await SignSlotAsync("r3", bobSlot.Id, "bob");
        SignInAs("ann", "Ann", false);

        // Act
        var cut = Render<InboxPage>();

        // Assert: two need action, one update, and the update is its own section with the approval chip
        Assert.Equal("2 need your action, 1 update", cut.Find("[data-testid='inbox-summary']").TextContent);
        Assert.Equal("Approved", cut.Find("[data-testid='inbox-chip-3']").TextContent);
        Assert.Equal("Signed by Bob.", cut.Find("[data-testid='inbox-detail-3']").TextContent);
        Assert.Empty(cut.FindAll("[data-testid='inbox-due-3']"));
    }

    /// <summary>
    ///     Test that Mark all as read stores the read state of every listed review and clears the markers.
    /// </summary>
    [Fact]
    public async Task InboxPage_MarkAllRead_ClearsUnreadDots()
    {
        // Arrange: two unread rows
        await ArrangeProjectAsync();
        await CreateInboxReviewAsync("r1");
        await AddSlotAsync("r1", "ann");
        await CreateInboxReviewAsync("r2");
        await AddSlotAsync("r2", "ann");
        SignInAs("ann", "Ann", false);
        var cut = Render<InboxPage>();
        Assert.Equal(2, cut.FindAll(".badge.unread").Count);

        // Act
        await cut.Find("[data-testid='inbox-mark-read']").ClickAsync();

        // Assert: no marker is left, the rows are, and the state of both reviews was stored
        await cut.WaitForAssertionAsync(() => Assert.Empty(cut.FindAll(".badge.unread")));
        Assert.Equal(2, cut.FindAll("[data-testid^='inbox-row-']").Count);
        Assert.Equal(2, InboxStore.States.Count(state => state.UserId == UserIds["ann"]));
    }

    /// <summary>
    ///     Test that an auditor, who cannot sign, is not offered a signature to give even when named in a slot.
    /// </summary>
    [Fact]
    public async Task InboxPage_Render_Auditor_ShowsNoSignItems()
    {
        // Arrange: the auditor was named as reviewer of r1 while a member, and then became an auditor
        await ArrangeProjectAsync();
        await CreateInboxReviewAsync("r1");
        await AddSlotAsync("r1", "ann");
        await Store.SetMembershipRoleAsync(
            ProjectId,
            UserIds["ann"],
            global::DemaConsulting.ReviewCenter.Core.ProjectRole.Auditor,
            AuditTestExtensions.Draft(),
            false,
            Ct);
        SignInAs("ann", "Ann", false);

        // Act
        var cut = Render<InboxPage>();

        // Assert
        Assert.Empty(cut.FindAll("[data-testid='inbox-section-to-sign']"));
        Assert.NotEmpty(cut.FindAll("[data-testid='inbox-empty']"));
        Assert.Equal("0 need your action, 0 updates", cut.Find("[data-testid='inbox-summary']").TextContent);
    }

    /// <summary>
    ///     Test that the Question for you row is listed under To answer, is counted, names the asker, does not quote
    ///     the question, and links to the review page and not to the checklist.
    /// </summary>
    [Fact]
    public async Task InboxPage_Render_QuestionForYou_ShowsRowLinkingToReview()
    {
        // Arrange: ann handed in her ballot and bob asked a question she has not answered
        await ArrangeProjectAsync();
        await CreateInboxReviewAsync("r1");
        await AddSlotAsync("r1", "ann");
        await AnswerAllAsync("r1", "ann");
        await Threads.StartThreadAsync(ProjectId, "r1", new ThreadAnchor("a.txt"), ActorOf("bob"), "Secret words", Ct);
        SignInAs("ann", "Ann", false);

        // Act
        var cut = Render<InboxPage>();

        // Assert: the row is under To answer with its chip, text and a link to the review
        Assert.Equal("To answer", cut.Find("[data-testid='inbox-section-to-answer']").TextContent);
        Assert.Contains(
            InboxService.QuestionChip,
            cut.Find("[data-testid='inbox-list-to-answer']").TextContent,
            StringComparison.Ordinal);
        var chip = cut.FindAll("[data-testid^='inbox-chip-']").Single(element => element.TextContent == InboxService.QuestionChip);
        var number = chip.GetAttribute("data-testid")!["inbox-chip-".Length..];
        Assert.Equal(
            $"/projects/{ProjectId}/reviews/r1",
            cut.Find($"[data-testid='inbox-link-{number}']").GetAttribute("href"));
        var detail = cut.Find($"[data-testid='inbox-detail-{number}']").TextContent;
        Assert.Contains("Bob", detail, StringComparison.Ordinal);
        Assert.DoesNotContain("Secret words", detail, StringComparison.Ordinal);
        Assert.Equal(
            "1 needs your action, 0 updates, 1 waiting for others",
            cut.Find("[data-testid='inbox-summary']").TextContent);
    }

    /// <summary>
    ///     Test that a signer whose signature no longer counts finds a titled section with the chip and the reason, that
    ///     it counts as work, and that it links to the review.
    /// </summary>
    [Fact]
    public async Task InboxPage_Render_SignatureLost_ShowsSectionChipAndReason()
    {
        // Arrange: ann signed, then bob opened a finding, so her signature no longer counts and she cannot sign yet
        await ArrangeProjectAsync();
        await CreateInboxReviewAsync("r1");
        var annSlot = await AddSlotAsync("r1", "ann");
        await AddSlotAsync("r1", "bob");
        await AnswerAllAsync("r1", "ann");
        await AnswerAllAsync("r1", "bob");
        await SignSlotAsync("r1", annSlot.Id, "ann");
        var approval = await Signing.GetApprovalAsync(ProjectId, "r1", Ct);
        var signed = approval!.Slots.Where(slot => slot.State == SlotSignatureState.Signed).Select(slot => slot.SlotId).ToList();
        await Threads.StartThreadAsync(
            ProjectId, "r1", new ThreadAnchor("a.txt"), ActorOf("bob"), "Please look", Ct, signed, true);
        SignInAs("ann", "Ann", false);

        // Act
        var cut = Render<InboxPage>();

        // Assert
        Assert.Equal("Your signature no longer counts", cut.Find("[data-testid='inbox-section-signature-lost']").TextContent);
        Assert.Equal(InboxService.SignatureLostChip, cut.Find("[data-testid='inbox-chip-1']").TextContent);
        Assert.Contains("You cannot sign again yet", cut.Find("[data-testid='inbox-detail-1']").TextContent, StringComparison.Ordinal);
        Assert.Equal($"/projects/{ProjectId}/reviews/r1", cut.Find("[data-testid='inbox-link-1']").GetAttribute("href"));
        Assert.Equal("1 needs your action, 0 updates", cut.Find("[data-testid='inbox-summary']").TextContent);
    }
}
