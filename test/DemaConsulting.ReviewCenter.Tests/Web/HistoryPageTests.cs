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
using Microsoft.Extensions.DependencyInjection;

using TestContext = Xunit.TestContext;

namespace DemaConsulting.ReviewCenter.Tests.Web;

/// <summary>
///     Unit tests for the <see cref="HistoryPage"/> component, rendered in memory with bUnit.
/// </summary>
public class HistoryPageTests : PageTestBase
{
    /// <summary>
    ///     Creates a project with one review and returns the project id.
    /// </summary>
    /// <returns>The project id.</returns>
    private async Task<int> CreateReviewAsync()
    {
        var project = await Projects.CreateAsync("Apollo", null, TestContext.Current.CancellationToken);
        await Reviews.CreateDraftAsync(project.Id, "REV-1", "First review", [new UploadedFile("a.txt", TestContent.A)], TestContext.Current.CancellationToken);
        return project.Id;
    }

    /// <summary>
    ///     Records an entry for a review.
    /// </summary>
    /// <param name="projectId">The project id.</param>
    /// <param name="reviewId">The review id.</param>
    /// <param name="action">The action code.</param>
    /// <param name="detail">The detail.</param>
    /// <param name="actor">The actor.</param>
    private async Task RecordAsync(int projectId, string reviewId, string action, string detail, Actor? actor = null)
    {
        await Audit.RecordAsync(AuditDraft.By(actor ?? TestActors.Ann, action, detail, projectId, reviewId), TestContext.Current.CancellationToken);
    }

    /// <summary>
    ///     Renders the page.
    /// </summary>
    /// <param name="projectId">The project id.</param>
    /// <param name="reviewId">The review id.</param>
    /// <param name="before">The <c>before</c> position, or <c>null</c>.</param>
    /// <param name="after">The <c>after</c> position, or <c>null</c>.</param>
    /// <returns>The rendered component.</returns>
    private IRenderedComponent<HistoryPage> RenderPage(int projectId, string reviewId = "REV-1", long? before = null, long? after = null)
    {
        var query = string.Empty;
        if (before is { } b)
        {
            query = $"?before={b}";
        }
        else if (after is { } a)
        {
            query = $"?after={a}";
        }

        Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>()
            .NavigateTo($"/projects/{projectId}/reviews/{reviewId}/history{query}");
        return Render<HistoryPage>(parameters => parameters
            .Add(page => page.ProjectId, projectId)
            .Add(page => page.ReviewId, reviewId));
    }

    /// <summary>
    ///     Test that the entries of the review are shown newest first with time, actor, action and detail, and
    ///     entries of other reviews are not.
    /// </summary>
    [Fact]
    public async Task HistoryPage_Render_ReviewWithEntries_ShowsOnlyItsEntriesNewestFirst()
    {
        // Arrange: three entries for the review and one for another review
        var projectId = await CreateReviewAsync();
        await RecordAsync(projectId, "REV-1", "review.created", "first detail");
        await RecordAsync(projectId, "REV-2", "review.created", "other review detail");
        await RecordAsync(projectId, "REV-1", "thread.started", "second detail", TestActors.Bob);
        await RecordAsync(projectId, "REV-1", "thread.resolved", "third detail");

        // Act
        var cut = RenderPage(projectId);

        // Assert
        Assert.Equal(["thread.resolved", "thread.started", "review.created"], cut.FindAll("[data-testid='history-action']").Select(e => e.TextContent));
        Assert.Equal(["third detail", "second detail", "first detail"], cut.FindAll("[data-testid='history-detail']").Select(e => e.TextContent));
        Assert.Equal(["Ann (ann)", "Bob (bob)", "Ann (ann)"], cut.FindAll("[data-testid='history-actor']").Select(e => e.TextContent));
        Assert.All(cut.FindAll("[data-testid='history-time']"), e => Assert.Equal("2025-03-04 05:06:07", e.TextContent));
        Assert.DoesNotContain("other review detail", cut.Markup, StringComparison.Ordinal);
        Assert.Equal("First review", cut.Find("[data-testid='history-review-title']").TextContent);
        Assert.Empty(cut.FindAll("[data-testid='history-older']"));
        Assert.Empty(cut.FindAll("[data-testid='history-newer']"));
    }

    /// <summary>
    ///     Test that a review without entries says so.
    /// </summary>
    [Fact]
    public async Task HistoryPage_Render_NoEntries_ShowsEmptyMessage()
    {
        // Arrange
        var projectId = await CreateReviewAsync();

        // Act
        var cut = RenderPage(projectId);

        // Assert
        Assert.NotNull(cut.Find("[data-testid='history-empty']"));
        Assert.Empty(cut.FindAll("[data-testid='history-table']"));
    }

    /// <summary>
    ///     Test that an unknown review shows a not-found message and no entries.
    /// </summary>
    [Fact]
    public async Task HistoryPage_Render_UnknownReview_ShowsNotFound()
    {
        // Arrange
        var project = await Projects.CreateAsync("Apollo", null, TestContext.Current.CancellationToken);
        await RecordAsync(project.Id, "NOPE", "x.y", "must not be shown");

        // Act
        var cut = RenderPage(project.Id, "NOPE");

        // Assert
        Assert.Contains("NOPE", cut.Find("[data-testid='history-not-found']").TextContent, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("[data-testid='history-table']"));
        Assert.DoesNotContain("must not be shown", cut.Markup, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a long history is paged by 50 with Older and Newer links that carry the position.
    /// </summary>
    [Fact]
    public async Task HistoryPage_Render_LongHistory_PagesByFiftyWithOlderAndNewerLinks()
    {
        // Arrange: 120 entries for the review, numbered by their detail
        var projectId = await CreateReviewAsync();
        for (var i = 1; i <= 120; i++)
        {
            await RecordAsync(projectId, "REV-1", "thread.replied", $"entry {i}");
        }

        // Act and assert, page by page: newest 50 first with Older only; then both links; then the last 20 with Newer only
        var first = RenderPage(projectId);
        Assert.Equal(50, first.FindAll("[data-testid='history-row']").Count);
        Assert.Equal("entry 120", first.FindAll("[data-testid='history-detail']")[0].TextContent);
        Assert.Equal("entry 71", first.FindAll("[data-testid='history-detail']")[49].TextContent);
        Assert.Equal("/projects/" + projectId + "/reviews/REV-1/history?before=71", first.Find("[data-testid='history-older']").GetAttribute("href"));
        Assert.Empty(first.FindAll("[data-testid='history-newer']"));
        await DisposeComponentsAsync();

        var second = RenderPage(projectId, before: 71);
        Assert.Equal(50, second.FindAll("[data-testid='history-row']").Count);
        Assert.Equal("entry 70", second.FindAll("[data-testid='history-detail']")[0].TextContent);
        Assert.NotNull(second.Find("[data-testid='history-older']"));
        Assert.Equal("/projects/" + projectId + "/reviews/REV-1/history?after=70", second.Find("[data-testid='history-newer']").GetAttribute("href"));
        await DisposeComponentsAsync();

        var third = RenderPage(projectId, before: 21);
        Assert.Equal(20, third.FindAll("[data-testid='history-row']").Count);
        Assert.Equal("entry 20", third.FindAll("[data-testid='history-detail']")[0].TextContent);
        Assert.Empty(third.FindAll("[data-testid='history-older']"));
        Assert.NotNull(third.Find("[data-testid='history-newer']"));
        await DisposeComponentsAsync();

        var back = RenderPage(projectId, after: 21);
        Assert.Equal("entry 71", back.FindAll("[data-testid='history-detail']")[0].TextContent);
        Assert.Equal("entry 22", back.FindAll("[data-testid='history-detail']")[^1].TextContent);
    }

    /// <summary>
    ///     Test that markup in the actor name and the detail is shown as text and never becomes elements.
    /// </summary>
    [Fact]
    public async Task HistoryPage_Render_MarkupInEntries_IsEscaped()
    {
        // Arrange
        var projectId = await CreateReviewAsync();
        await RecordAsync(projectId, "REV-1", "thread.started", "<script>alert(1)</script> & <img src=x onerror=y>", new Actor("mal", "<b>Mal</b>"));

        // Act
        var cut = RenderPage(projectId);

        // Assert: text content is the literal text, and no such element exists
        Assert.Equal("<script>alert(1)</script> & <img src=x onerror=y>", cut.Find("[data-testid='history-detail']").TextContent);
        Assert.Equal("<b>Mal</b> (mal)", cut.Find("[data-testid='history-actor']").TextContent);
        Assert.Empty(cut.FindAll("script"));
        Assert.Empty(cut.FindAll("img"));
        Assert.Empty(cut.FindAll("td b"));
        Assert.Contains("&lt;script&gt;", cut.Markup, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that the page links back to the review and names the project in the breadcrumb.
    /// </summary>
    [Fact]
    public async Task HistoryPage_Render_ExistingReview_LinksBackToTheReview()
    {
        // Arrange
        var projectId = await CreateReviewAsync();

        // Act
        var cut = RenderPage(projectId);

        // Assert
        Assert.Equal($"/projects/{projectId}/reviews/REV-1", cut.Find("[data-testid='history-back']").GetAttribute("href"));
        Assert.Equal("Apollo", cut.Find("[data-testid='project-link']").TextContent);
    }

    /// <summary>
    ///     Test that the history page requires a signed-in user when reached through the router.
    /// </summary>
    [Fact]
    public async Task Routes_Render_NobodySignedInOpensHistoryRoute_DoesNotShowEntries()
    {
        // Arrange
        var projectId = await CreateReviewAsync();
        await RecordAsync(projectId, "REV-1", "review.created", "secret detail");
        SignOut();
        Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>()
            .NavigateTo($"/projects/{projectId}/reviews/REV-1/history");

        // Act
        var cut = Render<Routes>();

        // Assert
        Assert.DoesNotContain("secret detail", cut.Markup, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("[data-testid='history-table']"));
    }
}
