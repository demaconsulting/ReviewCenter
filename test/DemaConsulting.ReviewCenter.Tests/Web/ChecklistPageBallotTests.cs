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
///     Unit tests for the ballot controls of the <see cref="ChecklistPage"/> component, rendered in memory with bUnit.
/// </summary>
public class ChecklistPageBallotTests : PageTestBase
{
    /// <summary>
    ///     The number of projects created so far, which keeps the project names apart.
    /// </summary>
    private int _projectCount;

    /// <summary>
    ///     Creates a project with the review <c>REV-1</c> and one reviewer slot for each of the given users.
    /// </summary>
    /// <param name="seated">The user names that hold a slot each.</param>
    /// <returns>The project id.</returns>
    private async Task<int> CreateReviewAsync(params string[] seated)
    {
        var project = await Projects.CreateAsync($"Apollo {++_projectCount}", null, TestContext.Current.CancellationToken);
        await Reviews.CreateDraftAsync(
            project.Id, "REV-1", "First review", [new("a.txt", TestContent.A)], TestContext.Current.CancellationToken);
        foreach (var name in seated)
        {
            await SeatAsync(project.Id, "REV-1", name);
        }

        return project.Id;
    }

    /// <summary>
    ///     Renders the page for the review <c>REV-1</c>.
    /// </summary>
    /// <param name="projectId">The project identity.</param>
    /// <returns>The rendered component.</returns>
    private IRenderedComponent<ChecklistPage> RenderPage(int projectId)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        return Render<ChecklistPage>(parameters => parameters
            .Add(page => page.ProjectId, projectId)
            .Add(page => page.ReviewId, "REV-1"));
    }

    /// <summary>
    ///     Test that a complete draft ballot offers the submit button, that the button hands the ballot in, and that
    ///     the status line follows.
    /// </summary>
    [Fact]
    public async Task ChecklistPage_SubmitButton_ShownWhenDraftComplete()
    {
        // Arrange: two slots, and ann has answered every check, so her ballot is a complete draft
        var projectId = await CreateReviewAsync("ann", "bob");
        await AuditTestExtensions.AnswerAllAsync(Checklists, projectId, "REV-1", TestActors.Ann, TestContext.Current.CancellationToken);
        SignInAs("ann", "Ann");

        // Act
        var cut = RenderPage(projectId);

        // Assert: a draft that can be handed in
        Assert.Contains("draft", cut.Find("[data-testid='ballot-status']").TextContent, StringComparison.Ordinal);
        Assert.NotNull(cut.Find("[data-testid='submit-ballot']"));

        // Act: hand the ballot in
        await cut.Find("[data-testid='submit-ballot']").ClickAsync();

        // Assert: submitted, and nothing left to hand in
        Assert.Contains("submitted", cut.Find("[data-testid='ballot-status']").TextContent, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("[data-testid='submit-ballot']"));
        Assert.Equal(BallotState.Submitted, (await Checklists.GetAsync(projectId, "REV-1", TestContext.Current.CancellationToken, "ann"))!.BallotState);
    }

    /// <summary>
    ///     Test that an incomplete ballot, and the only ballot of a single-slot review, show no submit button.
    /// </summary>
    [Fact]
    public async Task ChecklistPage_SubmitButton_HiddenWhenIncompleteOrSingleSlot()
    {
        // Arrange: two slots with one answer of ann only
        var projectId = await CreateReviewAsync("ann", "bob");
        await Checklists.SetAnswerAsync(
            projectId, "REV-1", DefaultChecklist.Current.Sections[0].Items[0].Id, ChecklistOutcome.Pass, null, TestActors.Ann, TestContext.Current.CancellationToken);
        SignInAs("ann", "Ann");

        // Act
        var incomplete = RenderPage(projectId);

        // Assert
        Assert.Empty(incomplete.FindAll("[data-testid='submit-ballot']"));

        // Arrange: a review with one slot, answered completely (which hands the ballot in)
        var single = await CreateReviewAsync("cyd");
        await AuditTestExtensions.AnswerAllAsync(
            Checklists, single, "REV-1", new Actor("cyd", "Cyd"), TestContext.Current.CancellationToken);
        SignInAs("cyd", "Cyd");

        // Act
        var cut = RenderPage(single);

        // Assert: nothing to hand in, and the ballot counts
        Assert.Empty(cut.FindAll("[data-testid='submit-ballot']"));
        Assert.Contains("submitted", cut.Find("[data-testid='ballot-status']").TextContent, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that somebody who holds no slot is told so, and gets no submit button.
    /// </summary>
    [Fact]
    public async Task ChecklistPage_NoSlot_ShowsHint()
    {
        // Arrange: a review whose only slot is bob's, viewed by ann
        var projectId = await CreateReviewAsync("bob");
        SignInAs("ann", "Ann");

        // Act
        var cut = RenderPage(projectId);

        // Assert
        Assert.NotNull(cut.Find("[data-testid='no-slot-notice']"));
        Assert.Empty(cut.FindAll("[data-testid='submit-ballot']"));
        Assert.Empty(cut.FindAll("[data-testid='ballot-status']"));
    }
}
