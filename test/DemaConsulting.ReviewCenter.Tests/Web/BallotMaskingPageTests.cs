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
///     Unit tests for what the review, project and history pages leave out for a viewer who has not handed in their
///     own ballot of a review with several reviewers: the standing of the ballots, the conclusion text, the
///     discussions about checks and the details of the answers in the history. Rendered in memory with bUnit.
/// </summary>
public class BallotMaskingPageTests : BallotPageTestBase
{
    /// <summary>
    ///     Creates a review with three reviewers whose handed-in ballots differ in the first check, while bob keeps a
    ///     draft, and signs in as bob.
    /// </summary>
    /// <returns>The project id.</returns>
    private async Task<int> ArrangeDisputedReviewWithDraftViewerAsync()
    {
        var projectId = await CreateReviewAsync("ann", "cyd", "bob");
        await AnswerAsync(projectId, "ann");
        await AnswerAsync(projectId, "cyd", ChecklistOutcome.Fail);
        await AnswerAsync(projectId, "bob", submit: false);
        SignInAs("bob", "Bob", false);
        return projectId;
    }

    /// <summary>
    ///     Test that the review page of a viewer without a handed-in ballot says the conclusion and the checklist
    ///     standing are hidden, and shows no discussion about a check.
    /// </summary>
    [Fact]
    public async Task ReviewPage_HiddenViewer_ShowsNoStandingConclusionOrCheckThreads()
    {
        // Arrange: a discussion about a check was started by the administrator
        var projectId = await ArrangeDisputedReviewWithDraftViewerAsync();
        await Threads.StartThreadAsync(
            projectId, ReviewKey, ThreadAnchor.ForCheck(FirstItemId), TestActors.Ann, "Why Fail?", TestContext.Current.CancellationToken);
        JSInterop.Mode = JSRuntimeMode.Loose;

        // Act
        var cut = Render<ReviewPage>(parameters => parameters
            .Add(page => page.ProjectId, projectId)
            .Add(page => page.ReviewId, ReviewKey));

        // Assert
        Assert.NotNull(cut.Find("[data-testid='review-conclusion-hidden']"));
        Assert.Contains("0 open", cut.Find("[data-testid='summary-threads']").TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain("Why Fail?", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("disputed", cut.Markup, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     Test that the review page of a member without a slot says the checklist standing is hidden.
    /// </summary>
    [Fact]
    public async Task ReviewPage_MemberWithoutSlot_ShowsChecklistStandingHidden()
    {
        // Arrange
        var projectId = await CreateReviewAsync("ann", "cyd");
        await AnswerAsync(projectId, "ann");
        await AnswerAsync(projectId, "cyd", ChecklistOutcome.Fail);
        await AddMemberAsync(projectId, "dee");
        SignInAs("dee", "Dee", false);
        JSInterop.Mode = JSRuntimeMode.Loose;

        // Act
        var cut = Render<ReviewPage>(parameters => parameters
            .Add(page => page.ProjectId, projectId)
            .Add(page => page.ReviewId, ReviewKey));

        // Assert
        Assert.Equal("Hidden until you answer", cut.Find("[data-testid='checklist-progress-summary']").TextContent);
        Assert.NotNull(cut.Find("[data-testid='review-conclusion-hidden']"));
    }

    /// <summary>
    ///     Test that the administrator sees the discussion about a check and no hidden notice on the review page.
    /// </summary>
    [Fact]
    public async Task ReviewPage_Administrator_SeesCheckThreadsAndConclusion()
    {
        // Arrange: ann is the ambient system administrator
        var projectId = await CreateReviewAsync("bob", "cyd");
        await AnswerAsync(projectId, "bob");
        await AnswerAsync(projectId, "cyd", ChecklistOutcome.Fail);
        await Threads.StartThreadAsync(
            projectId, ReviewKey, ThreadAnchor.ForCheck(FirstItemId), TestActors.Ann, "Why Fail?", TestContext.Current.CancellationToken);
        JSInterop.Mode = JSRuntimeMode.Loose;

        // Act
        var cut = Render<ReviewPage>(parameters => parameters
            .Add(page => page.ProjectId, projectId)
            .Add(page => page.ReviewId, ReviewKey));

        // Assert
        Assert.Empty(cut.FindAll("[data-testid='review-conclusion-hidden']"));
        Assert.Contains("1 open", cut.Find("[data-testid='summary-threads']").TextContent, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that the project page of a viewer without a handed-in ballot names no disputed checks as a blocker.
    /// </summary>
    [Fact]
    public async Task ProjectPage_HiddenViewer_ShowsNoDisputedCountAsBlocker()
    {
        // Arrange
        var projectId = await ArrangeDisputedReviewWithDraftViewerAsync();

        // Act
        var cut = Render<ProjectPage>(parameters => parameters.Add(page => page.ProjectId, projectId));

        // Assert
        var text = cut.Find($"[data-testid='review-blockers-{ReviewKey}']").TextContent;
        Assert.DoesNotContain("disputed", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("failed", text, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     Test that the history of a viewer without a handed-in ballot hides the details of the answers and of the
    ///     discussions about checks, and keeps the other entries as they are.
    /// </summary>
    [Fact]
    public async Task HistoryPage_HiddenViewer_HidesAnswerDetailsOnly()
    {
        // Arrange
        var projectId = await ArrangeDisputedReviewWithDraftViewerAsync();
        await Audit.RecordAsync(
            AuditDraft.By(TestActors.Ann, "checklist.answered", "item REQ-01: Fail", projectId, ReviewKey),
            TestContext.Current.CancellationToken);
        await Audit.RecordAsync(
            AuditDraft.By(TestActors.Ann, "thread.started", "thread 7 on check REQ-01; kind: question", projectId, ReviewKey),
            TestContext.Current.CancellationToken);
        await Audit.RecordAsync(
            AuditDraft.By(TestActors.Ann, "review.created", "plain detail", projectId, ReviewKey),
            TestContext.Current.CancellationToken);

        // Act
        var cut = RenderHistory(projectId);

        // Assert
        var details = cut.FindAll("[data-testid='history-detail']").Select(e => e.TextContent).ToList();
        Assert.Contains("plain detail", details);
        Assert.Contains(AuditMasking.HiddenAnswerDetail, details);
        Assert.Contains(AuditMasking.HiddenDiscussionDetail, details);
        Assert.DoesNotContain(details, detail => detail.Contains("REQ-01", StringComparison.Ordinal));
    }

    /// <summary>
    ///     Test that the administrator sees the full details of the answers in the history.
    /// </summary>
    [Fact]
    public async Task HistoryPage_Administrator_SeesFullAnswerDetails()
    {
        // Arrange: ann is the ambient system administrator
        var projectId = await CreateReviewAsync("bob", "cyd");
        await Audit.RecordAsync(
            AuditDraft.By(TestActors.Bob, "checklist.answered", "item REQ-01: Fail", projectId, ReviewKey),
            TestContext.Current.CancellationToken);

        // Act
        var cut = RenderHistory(projectId);

        // Assert
        Assert.Equal(["item REQ-01: Fail"], cut.FindAll("[data-testid='history-detail']").Select(e => e.TextContent));
    }

    /// <summary>
    ///     Renders the history page of the review <c>REV-1</c>.
    /// </summary>
    /// <param name="projectId">The project identity.</param>
    /// <returns>The rendered component.</returns>
    private IRenderedComponent<HistoryPage> RenderHistory(int projectId)
    {
        Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>()
            .NavigateTo($"/projects/{projectId}/reviews/{ReviewKey}/history");
        return Render<HistoryPage>(parameters => parameters
            .Add(page => page.ProjectId, projectId)
            .Add(page => page.ReviewId, ReviewKey));
    }
}
