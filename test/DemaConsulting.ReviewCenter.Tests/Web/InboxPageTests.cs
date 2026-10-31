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
using DemaConsulting.ReviewCenter.Web;

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
    ///     Test that a signature to give and a reply waiting are shown in their sections, with the review, the chip
    ///     and a link to the review.
    /// </summary>
    [Fact]
    public async Task InboxPage_Render_ToSignAndRepliesWaiting_ShowsSectionsRowsChipsAndLinks()
    {
        // Arrange: ann is a reviewer of r1, asked a question and bob answered
        await ArrangeProjectAsync();
        await CreateInboxReviewAsync("r1");
        await AddSlotAsync("r1", "ann");
        await StartAndAnswerThreadAsync("r1", "ann", "bob");
        SignInAs("ann", "Ann", false);

        // Act
        var cut = Render<InboxPage>();

        // Assert: both sections, in order, each with one row
        Assert.NotEmpty(cut.FindAll("[data-testid='inbox-section-to-sign']"));
        Assert.NotEmpty(cut.FindAll("[data-testid='inbox-section-replies-waiting']"));
        Assert.Empty(cut.FindAll("[data-testid='inbox-section-failed-checks']"));
        Assert.Empty(cut.FindAll("[data-testid='inbox-section-updates']"));
        Assert.Equal("Not signed", cut.Find("[data-testid='inbox-chip-1']").TextContent);
        Assert.Equal("1 waiting", cut.Find("[data-testid='inbox-chip-2']").TextContent);
        Assert.Equal("Apollo / r1", cut.Find("[data-testid='inbox-review-1']").TextContent);
        Assert.Equal("Review r1", cut.Find("[data-testid='inbox-link-1']").TextContent);
        Assert.Equal($"/projects/{ProjectId}/reviews/r1", cut.Find("[data-testid='inbox-link-1']").GetAttribute("href"));
        Assert.Equal($"/projects/{ProjectId}/reviews/r1", cut.Find("[data-testid='inbox-link-2']").GetAttribute("href"));
        Assert.Equal("1 needs your action, 0 updates", cut.Find("[data-testid='inbox-summary']").TextContent);
        Assert.Empty(cut.FindAll("[data-testid='inbox-empty']"));
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
        Assert.Equal("Not signed", second.Find("[data-testid='inbox-chip-1']").TextContent);
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
        await AnswerAllAsync("r3");
        var annSlot = await AddSlotAsync("r3", "ann");
        var bobSlot = await AddSlotAsync("r3", "bob");
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
}
