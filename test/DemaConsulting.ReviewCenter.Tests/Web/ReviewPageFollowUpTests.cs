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
using DemaConsulting.ReviewCenter.Tests.Core;
using DemaConsulting.ReviewCenter.Web;
using TestContext = Xunit.TestContext;

namespace DemaConsulting.ReviewCenter.Tests.Web;

/// <summary>
///     Unit tests for what <see cref="ReviewPage"/> shows of follow-up reviews: the
///     "Follows" link with the carried counts, the "Followed by" list, and the mark on carried threads.
/// </summary>
public class ReviewPageFollowUpTests : PageTestBase
{
    /// <summary>
    ///     Creates the project, the earlier review <c>REV-1</c> with an open thread, and the follow-ups <c>REV-2</c> and
    ///     <c>REV-3</c> of it; <c>REV-2</c> has a thread of its own as well.
    /// </summary>
    /// <returns>The project id, the identity of the carried thread of <c>REV-2</c> and of its own thread.</returns>
    private async Task<(int ProjectId, int CarriedThreadId, int OwnThreadId)> ArrangeAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var project = await Projects.CreateAsync("Apollo", null, cancellationToken);
        await Reviews.CreateDraftAsync(
            project.Id, "REV-1", "First review", [new("a.txt", TestContent.A)], TestActors.Ann, cancellationToken);
        await Threads.StartThreadAsync(project.Id, "REV-1", new ThreadAnchor("a.txt"), TestActors.Bob, "Look", cancellationToken);
        foreach (var id in new[] { "REV-2", "REV-3" })
        {
            await Reviews.CreateDraftAsync(
                project.Id, id, "Follow-up " + id, [new("a.txt", TestContent.A)], TestActors.Ann, cancellationToken, null, "REV-1");
        }

        var own = await Threads.StartThreadAsync(project.Id, "REV-2", new ThreadAnchor("a.txt"), TestActors.Bob, "Mine", cancellationToken);
        var carried = (await Threads.ListAsync(project.Id, "REV-2", cancellationToken)).Single(thread => thread.Id != own.Id);
        return (project.Id, carried.Id, own.Id);
    }

    /// <summary>
    ///     Renders the review page for a review.
    /// </summary>
    /// <param name="projectId">The project identity.</param>
    /// <param name="reviewId">The review id.</param>
    /// <returns>The rendered component.</returns>
    private IRenderedComponent<ReviewPage> RenderPage(int projectId, string reviewId)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        return Render<ReviewPage>(parameters => parameters
            .Add(page => page.ProjectId, projectId)
            .Add(page => page.ReviewId, reviewId));
    }

    /// <summary>
    ///     Test that a follow-up names the review it follows with a link and the number of answers and threads it took
    ///     over, and that a carried thread is marked while a thread of its own is not.
    /// </summary>
    [Fact]
    public async Task ReviewPage_Render_FollowUp_ShowsFollowsLinkCountsAndCarriedThreadMark()
    {
        // Arrange: REV-2 follows REV-1 and carried its open thread
        var (projectId, carriedId, ownId) = await ArrangeAsync();

        // Act
        var cut = RenderPage(projectId, "REV-2");
        await cut.Find("[data-testid='file-select-0']").ClickAsync();

        // Assert: the follows line with its link and counts
        var link = cut.Find("[data-testid='review-follows-link']");
        Assert.Equal("REV-1", link.TextContent);
        Assert.Equal(ReviewLinks.ForReview(projectId, "REV-1"), link.GetAttribute("href"));
        Assert.Equal("0 answers and 1 thread carried", cut.Find("[data-testid='review-carried-counts']").TextContent);

        // Assert: only the carried thread carries the mark, with a link to the review it came from
        Assert.Equal(
            ReviewLinks.ForReview(projectId, "REV-1"),
            cut.Find($"[data-testid='thread-carried-link-{carriedId}']").GetAttribute("href"));
        Assert.Empty(cut.FindAll($"[data-testid='thread-carried-{ownId}']"));
    }

    /// <summary>
    ///     Test that the followed review lists its follow-ups, and that a review that follows nothing has no follows
    ///     line.
    /// </summary>
    [Fact]
    public async Task ReviewPage_Render_FollowedReview_ListsFollowUpsAndShowsNoFollowsLine()
    {
        // Arrange: REV-2 and REV-3 follow REV-1
        var (projectId, _, _) = await ArrangeAsync();

        // Act
        var cut = RenderPage(projectId, "REV-1");

        // Assert: both follow-ups are linked and there is no follows line
        Assert.Equal(
            ReviewLinks.ForReview(projectId, "REV-2"),
            cut.Find("[data-testid='review-followed-by-link-REV-2']").GetAttribute("href"));
        Assert.NotEmpty(cut.FindAll("[data-testid='review-followed-by-link-REV-3']"));
        Assert.Empty(cut.FindAll("[data-testid='review-follows']"));

        // Assert: a follow-up that nothing follows has no list
        Assert.Empty(RenderPage(projectId, "REV-3").FindAll("[data-testid='review-followed-by']"));
    }
}
