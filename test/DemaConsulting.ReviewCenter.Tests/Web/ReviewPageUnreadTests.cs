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

namespace DemaConsulting.ReviewCenter.Tests.Web;

/// <summary>
///     Unit tests for the unread markers of the <see cref="ReviewPage"/> component: what the viewer had seen before
///     the visit becomes the baseline of the markers, and the visit stores the new read state.
/// </summary>
public class ReviewPageUnreadTests : InboxWebTestBase
{
    /// <summary>
    ///     Test that the first visit marks the comments of others as new, in the summary, the index and the thread.
    /// </summary>
    [Fact]
    public async Task ReviewPage_Render_FirstVisit_ShowsUnreadDotsForCommentsByOthers()
    {
        // Arrange: bob starts a thread and eve replies; ann has never opened the review
        await ArrangeProjectAsync();
        await CreateInboxReviewAsync("r1");
        var thread = await StartAndAnswerThreadAsync("r1", "bob", "eve");
        SignInAs("ann", "Ann", false);

        // Act
        var cut = RenderReview("r1");

        // Assert: both comments are new, in the summary and on the thread
        Assert.Equal(
            "New 2 comments by others are new since your last visit.",
            cut.Find("[data-testid='review-unread-summary']").TextContent);
        Assert.Equal("New", cut.Find($"[data-testid='index-unread-{thread.Id}']").TextContent);

        // Act: open the thread from the index, which shows its file
        await cut.Find($"[data-testid='index-row-{thread.Id}']").ClickAsync();

        // Assert: the thread carries the marker too
        Assert.Equal("New", cut.Find($"[data-testid='thread-unread-{thread.Id}']").TextContent);
    }

    /// <summary>
    ///     Test that a live render stores the read state, and that the next visit shows no markers.
    /// </summary>
    [Fact]
    public async Task ReviewPage_Render_AfterLiveRender_UpsertsLastSeen_AndSecondVisitShowsNoDots()
    {
        // Arrange: a thread of others that ann has not seen
        await ArrangeProjectAsync();
        await CreateInboxReviewAsync("r1");
        var thread = await StartAndAnswerThreadAsync("r1", "bob", "eve");
        SignInAs("ann", "Ann", false);
        var first = RenderReview("r1");
        Assert.NotEmpty(first.FindAll("[data-testid='review-unread-summary']"));

        // Act: the first render stored the state; visit again
        await first.WaitForAssertionAsync(() => Assert.Single(InboxStore.States));
        var second = RenderReview("r1");

        // Assert: the state points at the newest comment and nothing is new any more
        var state = Assert.Single(InboxStore.States);
        Assert.Equal(UserIds["ann"], state.UserId);
        Assert.Equal("r1", state.ReviewId);
        Assert.Equal(ProjectId, state.ProjectId);
        Assert.True(state.LastSeenCommentId > 0);
        Assert.Empty(second.FindAll("[data-testid='review-unread-summary']"));
        Assert.Empty(second.FindAll($"[data-testid='index-unread-{thread.Id}']"));
        Assert.Empty(second.FindAll($"[data-testid='thread-unread-{thread.Id}']"));
    }

    /// <summary>
    ///     Test that the viewer's own comments are never marked as new.
    /// </summary>
    [Fact]
    public async Task ReviewPage_Render_OwnComments_ShowNoDot()
    {
        // Arrange: ann wrote the whole thread herself
        await ArrangeProjectAsync();
        await CreateInboxReviewAsync("r1");
        var thread = await StartAndAnswerThreadAsync("r1", "ann", "ann");
        SignInAs("ann", "Ann", false);

        // Act
        var cut = RenderReview("r1");

        // Assert: the thread is listed, with no marker anywhere
        Assert.NotEmpty(cut.FindAll($"[data-testid='index-row-{thread.Id}']"));
        Assert.Empty(cut.FindAll("[data-testid='review-unread-summary']"));
        Assert.Empty(cut.FindAll($"[data-testid='index-unread-{thread.Id}']"));
    }

    /// <summary>
    ///     Test that changing the address to another review loads that review's baseline and marks it as seen.
    /// </summary>
    [Fact]
    public async Task ReviewPage_Render_ParameterChangeToOtherReview_ReloadsBaselineAndMarksSeen()
    {
        // Arrange: both reviews have comments of others, and ann has seen neither
        await ArrangeProjectAsync();
        await CreateInboxReviewAsync("r1");
        await CreateInboxReviewAsync("r2");
        await StartAndAnswerThreadAsync("r1", "bob", "eve");
        await StartAndAnswerThreadAsync("r2", "bob", "eve");
        SignInAs("ann", "Ann", false);
        var cut = RenderReview("r1");
        await cut.WaitForAssertionAsync(() => Assert.Single(InboxStore.States));
        Assert.NotEmpty(cut.FindAll("[data-testid='review-unread-summary']"));

        // Act: navigate to the other review in the same component
        cut.Render(parameters => parameters
            .Add(page => page.ProjectId, ProjectId)
            .Add(page => page.ReviewId, "r2"));

        // Assert: r2 shows its own markers and both reviews are now stored as seen
        await cut.WaitForAssertionAsync(() => Assert.Equal(2, InboxStore.States.Count));
        Assert.NotEmpty(cut.FindAll("[data-testid='review-unread-summary']"));
        Assert.Contains(InboxStore.States, state => state.ReviewId == "r2");
    }

    /// <summary>
    ///     Test that a user who may not see the project leaves no read state behind.
    /// </summary>
    [Fact]
    public async Task ReviewPage_Render_NoViewAccess_WritesNoState()
    {
        // Arrange: out is no member of the project
        await ArrangeProjectAsync();
        await CreateInboxReviewAsync("r1");
        await StartAndAnswerThreadAsync("r1", "bob", "eve");
        await Users.CreateAsync("out", "Out", false, Password, Ct);
        SignInAs("out", "Out", false);

        // Act
        var cut = RenderReview("r1");

        // Assert: nothing of the review is shown and no row was written
        Assert.Empty(cut.FindAll("[data-testid='review-unread-summary']"));
        Assert.Empty(InboxStore.States);
    }

    /// <summary>
    ///     Renders the review page for a review of the project <c>Apollo</c>.
    /// </summary>
    /// <param name="reviewId">The review id.</param>
    /// <returns>The rendered component.</returns>
    private IRenderedComponent<ReviewPage> RenderReview(string reviewId)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        return Render<ReviewPage>(parameters => parameters
            .Add(page => page.ProjectId, ProjectId)
            .Add(page => page.ReviewId, reviewId));
    }
}
