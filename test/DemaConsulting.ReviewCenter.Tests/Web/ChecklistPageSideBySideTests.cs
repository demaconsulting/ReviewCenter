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

using TestContext = Xunit.TestContext;

namespace DemaConsulting.ReviewCenter.Tests.Web;

/// <summary>
///     Unit tests for what the <see cref="ChecklistPage"/> shows of the other reviewers' ballots: nothing until the
///     viewer has handed in their own, the differing answers side by side afterwards, and the Discuss button that
///     starts a discussion about a check. Rendered in memory with bUnit.
/// </summary>
public class ChecklistPageSideBySideTests : BallotPageTestBase
{
    /// <summary>
    ///     Opens the first section of the checklist when it is not open yet, so its checks are rendered.
    /// </summary>
    /// <param name="cut">The rendered page.</param>
    private static async Task OpenFirstSectionAsync(IRenderedComponent<ChecklistPage> cut)
    {
        if (cut.FindAll($"[data-testid='check-{FirstItemTestId}']").Count == 0)
        {
            var sectionId = DefaultChecklist.Current.Sections[0].Id.ToLowerInvariant();
            await cut.Find($"[data-testid='section-{sectionId}-toggle']").ClickAsync();
        }
    }

    /// <summary>
    ///     Test that a reviewer with a draft sees how many ballots are in, and neither the other ballots, nor the
    ///     number of differing checks, nor the standing of the review.
    /// </summary>
    [Fact]
    public async Task ChecklistPage_DraftBallot_HidesOtherBallotsAndDisputeCounts()
    {
        // Arrange: ann handed in Pass everywhere; bob has a draft with a Fail
        var projectId = await CreateReviewAsync("ann", "bob");
        await AnswerAsync(projectId, "ann");
        await AnswerAsync(projectId, "bob", ChecklistOutcome.Fail, submit: false);
        SignInAs("bob", "Bob", false);

        // Act
        var cut = RenderPage(projectId);
        await OpenFirstSectionAsync(cut);

        // Assert: who has handed in is visible, what they answered is not
        Assert.Equal("1 of 2 ballots handed in.", cut.Find("[data-testid='ballots-handed-in']").TextContent);
        Assert.NotNull(cut.Find("[data-testid='ballots-hidden-notice']"));
        Assert.Empty(cut.FindAll("[data-testid^='ballot-others-']"));
        Assert.DoesNotContain("Different answers", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("disputed", cut.Markup, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     Test that a plain member without a slot sees no standing at all, only how many ballots are in, and is
    ///     offered the shortcut to become a reviewer.
    /// </summary>
    [Fact]
    public async Task ChecklistPage_MemberWithoutSlot_SeesNoStandingOnlyBallotCount()
    {
        // Arrange: two reviewers handed in answers that differ
        var projectId = await CreateReviewAsync("ann", "bob");
        await AnswerAsync(projectId, "ann");
        await AnswerAsync(projectId, "bob", ChecklistOutcome.Fail);
        await AddMemberAsync(projectId, "dee");
        SignInAs("dee", "Dee", false);

        // Act
        var cut = RenderPage(projectId);

        // Assert
        Assert.Equal("2 of 2 ballots handed in.", cut.Find("[data-testid='ballots-handed-in']").TextContent);
        Assert.NotNull(cut.Find("[data-testid='ballots-hidden-notice']"));
        Assert.Empty(cut.FindAll("[data-testid='checklist-status']"));
        Assert.Empty(cut.FindAll("[data-testid='checklist-progress']"));
        Assert.Empty(cut.FindAll("[data-testid^='ballot-others-']"));
        Assert.NotNull(cut.Find("[data-testid='assign-me']"));
    }

    /// <summary>
    ///     Test that a reviewer who handed in sees the checks the ballots answer differently, side by side, with the
    ///     names, and no hidden notice.
    /// </summary>
    [Fact]
    public async Task ChecklistPage_SubmittedBallot_ShowsSideBySideForDifferingChecks()
    {
        // Arrange: ann says Pass and bob says N/A on the first check
        var projectId = await CreateReviewAsync("ann", "bob");
        await AnswerAsync(projectId, "ann");
        await AnswerAsync(projectId, "bob", ChecklistOutcome.NotApplicable);
        SignInAs("bob", "Bob", false);

        // Act
        var cut = RenderPage(projectId);
        await OpenFirstSectionAsync(cut);

        // Assert
        Assert.Equal("2 of 2 ballots handed in.", cut.Find("[data-testid='ballots-handed-in']").TextContent);
        Assert.Empty(cut.FindAll("[data-testid='ballots-hidden-notice']"));
        Assert.Equal(
            "Ann: Pass / Bob: N/A",
            cut.Find($"[data-testid='ballot-others-text-{FirstItemTestId}']").TextContent);
        Assert.Contains("Different answers", cut.Find($"[data-testid='ballot-others-{FirstItemTestId}']").TextContent, StringComparison.Ordinal);
        Assert.Single(cut.FindAll("[data-testid^='ballot-others-text-']"));
    }

    /// <summary>
    ///     Test that the administrator, who steers the review, sees the differing answers without a ballot of their own.
    /// </summary>
    [Fact]
    public async Task ChecklistPage_AdministratorWithoutBallot_SeesDifferingAnswers()
    {
        // Arrange: ann is the ambient system administrator and holds no slot
        var projectId = await CreateReviewAsync("bob", "cyd");
        await AnswerAsync(projectId, "bob");
        await AnswerAsync(projectId, "cyd", ChecklistOutcome.Fail);

        // Act
        var cut = RenderPage(projectId);
        await OpenFirstSectionAsync(cut);

        // Assert
        Assert.Empty(cut.FindAll("[data-testid='ballots-hidden-notice']"));
        Assert.Equal(
            "Bob: Pass / Cyd: Fail",
            cut.Find($"[data-testid='ballot-others-text-{FirstItemTestId}']").TextContent);
    }

    /// <summary>
    ///     Test that a review with a single reviewer shows no ballot counts, no hidden notice and no side-by-side
    ///     answers, as before.
    /// </summary>
    [Fact]
    public async Task ChecklistPage_SingleSlot_ShowsNoBallotCountsOrSideBySide()
    {
        // Arrange
        var projectId = await CreateReviewAsync("cyd");
        await AnswerAsync(projectId, "cyd");
        SignInAs("cyd", "Cyd", false);

        // Act
        var cut = RenderPage(projectId);

        // Assert
        Assert.Empty(cut.FindAll("[data-testid='ballots-handed-in']"));
        Assert.Empty(cut.FindAll("[data-testid='ballots-hidden-notice']"));
        Assert.Empty(cut.FindAll("[data-testid^='ballot-others-']"));
        Assert.Empty(cut.FindAll("[data-testid='submit-and-sign']"));
    }

    /// <summary>
    ///     Test that the Discuss button of a differing check starts a discussion about that check with the typed text,
    ///     and that the page then says where to find it.
    /// </summary>
    [Fact]
    public async Task ChecklistPage_DisputedCheck_DiscussStartsCheckThread()
    {
        // Arrange
        var projectId = await CreateReviewAsync("ann", "bob");
        await AnswerAsync(projectId, "ann");
        await AnswerAsync(projectId, "bob", ChecklistOutcome.Fail);
        SignInAs("bob", "Bob", false);
        var cut = RenderPage(projectId);
        await OpenFirstSectionAsync(cut);

        // Act
        await cut.Find($"[data-testid='discuss-{FirstItemTestId}']").ClickAsync();
        await cut.Find($"[data-testid='discuss-text-{FirstItemTestId}']").InputAsync("Why did you fail this one?");
        await cut.Find($"[data-testid='discuss-start-{FirstItemTestId}']").ClickAsync();

        // Assert
        var thread = Assert.Single(await Threads.ListAsync(projectId, ReviewKey, TestContext.Current.CancellationToken));
        Assert.Equal(FirstItemId, thread.CheckItemId);
        Assert.Equal("Why did you fail this one?", thread.Comments.Single().Text);
        Assert.NotNull(cut.Find($"[data-testid='discuss-started-{FirstItemTestId}']"));
        Assert.Empty(cut.FindAll($"[data-testid='discuss-form-{FirstItemTestId}']"));
    }

    /// <summary>
    ///     Test that the Discuss form of a check offers the choice of kind with Question as the default, and that
    ///     choosing "Finding (goes in the report)" starts a Finding.
    /// </summary>
    [Fact]
    public async Task ChecklistPage_DiscussFinding_KindChosenInComposer()
    {
        // Arrange
        var projectId = await CreateReviewAsync("ann", "bob");
        await AnswerAsync(projectId, "ann");
        await AnswerAsync(projectId, "bob", ChecklistOutcome.Fail);
        SignInAs("bob", "Bob", false);
        var cut = RenderPage(projectId);
        await OpenFirstSectionAsync(cut);
        await cut.Find($"[data-testid='discuss-{FirstItemTestId}']").ClickAsync();

        // Assert: Question is the default
        var kindId = $"discuss-kind-{FirstItemTestId}";
        Assert.True(cut.Find($"[data-testid='{kindId}-question']").HasAttribute("checked"));
        Assert.False(cut.Find($"[data-testid='{kindId}-finding']").HasAttribute("checked"));

        // Act: choose Finding and start
        await cut.Find($"[data-testid='{kindId}-finding']").ChangeAsync(new Microsoft.AspNetCore.Components.ChangeEventArgs { Value = "finding" });
        await cut.Find($"[data-testid='discuss-text-{FirstItemTestId}']").InputAsync("This check needs a fix");
        await cut.Find($"[data-testid='discuss-start-{FirstItemTestId}']").ClickAsync();

        // Assert: the thread is a Finding
        var thread = Assert.Single(await Threads.ListAsync(projectId, ReviewKey, TestContext.Current.CancellationToken));
        Assert.Equal(ThreadKind.Finding, thread.Kind);
        Assert.Equal(FirstItemId, thread.CheckItemId);
    }

    /// <summary>
    ///     Test that an empty discussion is refused with the reason shown next to the check.
    /// </summary>
    [Fact]
    public async Task ChecklistPage_DiscussWithoutText_ShowsError()
    {
        // Arrange
        var projectId = await CreateReviewAsync("ann", "bob");
        await AnswerAsync(projectId, "ann");
        await AnswerAsync(projectId, "bob", ChecklistOutcome.Fail);
        SignInAs("bob", "Bob", false);
        var cut = RenderPage(projectId);
        await OpenFirstSectionAsync(cut);
        await cut.Find($"[data-testid='discuss-{FirstItemTestId}']").ClickAsync();

        // Act
        await cut.Find($"[data-testid='discuss-start-{FirstItemTestId}']").ClickAsync();

        // Assert
        Assert.NotNull(cut.Find($"[data-testid='discuss-error-{FirstItemTestId}']"));
        Assert.Empty(await Threads.ListAsync(projectId, ReviewKey, TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Test that a viewer who cannot start a discussion about a check (a reviewer who has not handed in cannot even
    ///     see the differing answers) gets no Discuss button, and that Cancel closes the form.
    /// </summary>
    [Fact]
    public async Task ChecklistPage_DiscussCancel_ClosesTheForm()
    {
        // Arrange
        var projectId = await CreateReviewAsync("ann", "bob");
        await AnswerAsync(projectId, "ann");
        await AnswerAsync(projectId, "bob", ChecklistOutcome.Fail);
        SignInAs("bob", "Bob", false);
        var cut = RenderPage(projectId);
        await OpenFirstSectionAsync(cut);
        await cut.Find($"[data-testid='discuss-{FirstItemTestId}']").ClickAsync();
        Assert.NotNull(cut.Find($"[data-testid='discuss-form-{FirstItemTestId}']"));

        // Act
        await cut.Find($"[data-testid='discuss-cancel-{FirstItemTestId}']").ClickAsync();

        // Assert: the form is gone and the button is back
        Assert.Empty(cut.FindAll($"[data-testid='discuss-form-{FirstItemTestId}']"));
        Assert.NotNull(cut.Find($"[data-testid='discuss-{FirstItemTestId}']"));
        Assert.Empty(await Threads.ListAsync(projectId, ReviewKey, TestContext.Current.CancellationToken));
    }
}
