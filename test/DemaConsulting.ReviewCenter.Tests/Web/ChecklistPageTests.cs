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

using System.Text.RegularExpressions;
using Bunit;
using DemaConsulting.ReviewCenter.Core;
using DemaConsulting.ReviewCenter.Tests.Core;
using DemaConsulting.ReviewCenter.Web;
using Microsoft.AspNetCore.Components;

using TestContext = Xunit.TestContext;

namespace DemaConsulting.ReviewCenter.Tests.Web;

/// <summary>
///     Unit tests for the <see cref="ChecklistPage"/> component, rendered in memory with bUnit.
/// </summary>
public partial class ChecklistPageTests : PageTestBase
{
    /// <summary>
    ///     Matches a kebab-case test identifier.
    /// </summary>
    /// <returns>The pattern.</returns>
    [GeneratedRegex("^[a-z0-9]+(?:[-.][a-z0-9]+)*$")]
    private static partial Regex KebabCaseTestId();

    /// <summary>
    ///     Creates a project with the review <c>REV-1</c>, which gets the default checklist.
    /// </summary>
    /// <returns>The project id.</returns>
    private async Task<int> CreateReviewAsync()
    {
        var project = await Projects.CreateAsync("Apollo", null, TestContext.Current.CancellationToken);
        await Reviews.CreateDraftAsync(
            project.Id, "REV-1", "First review", [new("a.txt", TestContent.A)], TestContext.Current.CancellationToken);
        return project.Id;
    }

    /// <summary>
    ///     Answers every check of the given sections as passed.
    /// </summary>
    /// <param name="projectId">The project identity.</param>
    /// <param name="sectionIds">The sections to answer.</param>
    /// <returns>A task that completes when all answers are stored.</returns>
    private async Task PassSectionsAsync(int projectId, params string[] sectionIds)
    {
        foreach (var item in DefaultChecklist.Current.Sections.Where(s => sectionIds.Contains(s.Id)).SelectMany(s => s.Items))
        {
            await Checklists.SetAnswerAsync(
                projectId, "REV-1", item.Id, ChecklistOutcome.Pass, null, TestActors.Ann, TestContext.Current.CancellationToken);
        }
    }

    /// <summary>
    ///     Renders the page for a review.
    /// </summary>
    /// <param name="projectId">The project identity.</param>
    /// <param name="reviewId">The review id.</param>
    /// <returns>The rendered component.</returns>
    private IRenderedComponent<ChecklistPage> RenderPage(int projectId, string reviewId = "REV-1")
    {
        // Choosing the next check asks the browser to scroll; the tests only check what was asked
        JSInterop.Mode = JSRuntimeMode.Loose;
        return Render<ChecklistPage>(parameters => parameters
            .Add(page => page.ProjectId, projectId)
            .Add(page => page.ReviewId, reviewId));
    }

    /// <summary>
    ///     Test that the page reports that it is interactive once it has rendered on a live connection.
    /// </summary>
    [Fact]
    public void ChecklistPage_AfterRender_Interactive_SetsMarker()
    {
        // Arrange: no setup required

        // Act: render the page
        var cut = RenderPage(1);

        // Assert: the root element reports interactivity
        Assert.Equal("true", cut.Find("[data-testid='checklist-page']").GetAttribute("data-interactive"));
    }

    /// <summary>
    ///     Test that a new review shows the overall progress, every section with its progress, and only the
    ///     first section open.
    /// </summary>
    [Fact]
    public async Task ChecklistPage_Render_NewReview_ShowsProgressAndOpensFirstSection()
    {
        // Arrange: a new review with nothing answered
        var projectId = await CreateReviewAsync();

        // Act: render the page
        var cut = RenderPage(projectId);

        // Assert: status, progress and next-step action
        Assert.Equal("58 checks unanswered.", cut.Find("[data-testid='checklist-status']").TextContent);
        Assert.Equal("0 of 58 checks answered", cut.Find("[data-testid='checklist-progress']").TextContent);
        Assert.NotNull(cut.Find("[data-testid='next-unanswered-button']"));

        // Assert: 14 sections with their progress, the first one open
        Assert.Equal(14, cut.FindAll("[data-testid$='-toggle'][data-testid^='section-']").Count);
        Assert.Equal("0 of 8", cut.Find("[data-testid='section-req-progress']").TextContent);
        Assert.Equal("0 of 5", cut.Find("[data-testid='section-des-progress']").TextContent);
        Assert.Equal("true", cut.Find("[data-testid='section-req-toggle']").GetAttribute("aria-expanded"));
        Assert.Equal("false", cut.Find("[data-testid='section-des-toggle']").GetAttribute("aria-expanded"));
        Assert.Equal(8, cut.FindAll("[data-testid^='check-req-']").Count);
        Assert.Empty(cut.FindAll("[data-testid^='check-des-']"));

        // Assert: each check's outcome group is named by the check text
        Assert.Equal("check-text-req-01", cut.Find("[data-testid='check-req-01'] [role='group']").GetAttribute("aria-labelledby"));
        Assert.Contains("REQ-01", cut.Find("#check-text-req-01").TextContent, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that an unknown review shows a not-found message.
    /// </summary>
    [Fact]
    public async Task ChecklistPage_Render_UnknownReview_ShowsNotFound()
    {
        // Arrange: a project without that review
        var project = await Projects.CreateAsync("Apollo", null, TestContext.Current.CancellationToken);

        // Act: render the page for a review that does not exist
        var cut = RenderPage(project.Id, "NOPE");

        // Assert: not-found names the review and no checklist is shown
        Assert.Contains("NOPE", cut.Find("[data-testid='checklist-not-found']").TextContent, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("[data-testid='checklist-status']"));
    }

    /// <summary>
    ///     Test that a review stored without a checklist says so.
    /// </summary>
    [Fact]
    public async Task ChecklistPage_Render_ReviewWithoutChecklist_ShowsMissing()
    {
        // Arrange: a review stored without a snapshot
        var project = await Projects.CreateAsync("Apollo", null, TestContext.Current.CancellationToken);
        await Store.AddReviewAsync(
            new Review
            {
                ProjectId = project.Id,
                Id = "REV-9",
                Title = "Old",
                Created = new DateTimeOffset(2025, 3, 4, 5, 6, 7, TimeSpan.Zero)
            },
            TestContext.Current.CancellationToken);

        // Act: render the page of that review
        var cut = RenderPage(project.Id, "REV-9");

        // Assert: the missing checklist is named
        Assert.Contains("no checklist", cut.Find("[data-testid='checklist-missing']").TextContent, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("[data-testid='checklist-status']"));
    }

    /// <summary>
    ///     Test that the first section with unanswered checks is the one open.
    /// </summary>
    [Fact]
    public async Task ChecklistPage_Render_PartlyAnswered_OpensFirstIncompleteSection()
    {
        // Arrange: the first two sections are fully answered
        var projectId = await CreateReviewAsync();
        await PassSectionsAsync(projectId, "REQ", "DES");

        // Act: render the page
        var cut = RenderPage(projectId);

        // Assert: those sections are done and the third is open
        Assert.Equal("Done", cut.Find("[data-testid='section-req-progress']").TextContent);
        Assert.Equal("Done", cut.Find("[data-testid='section-des-progress']").TextContent);
        Assert.Equal("true", cut.Find("[data-testid='section-doc-toggle']").GetAttribute("aria-expanded"));
        Assert.Equal("false", cut.Find("[data-testid='section-req-toggle']").GetAttribute("aria-expanded"));
        Assert.Equal("45 checks unanswered.", cut.Find("[data-testid='checklist-status']").TextContent);
    }

    /// <summary>
    ///     Test that opening a section closes the one that was open.
    /// </summary>
    [Fact]
    public async Task ChecklistPage_Render_OpeningSection_ClosesOthers()
    {
        // Arrange: a new review, whose first section is open
        var cut = RenderPage(await CreateReviewAsync());

        // Act: open the design section
        await cut.Find("[data-testid='section-des-toggle']").ClickAsync();

        // Assert: only the design section is open
        Assert.Equal("true", cut.Find("[data-testid='section-des-toggle']").GetAttribute("aria-expanded"));
        Assert.Equal("false", cut.Find("[data-testid='section-req-toggle']").GetAttribute("aria-expanded"));
        Assert.Empty(cut.FindAll("[data-testid^='check-req-']"));
        Assert.Equal(5, cut.FindAll("[data-testid^='check-des-']").Count);
    }

    /// <summary>
    ///     Test that the checklist of an approved review shows the locked notice and disables every control that
    ///     would change it.
    /// </summary>
    [Fact]
    public async Task ChecklistPage_Render_ApprovedReview_ShowsLockedAndDisablesControls()
    {
        // Arrange: an approved review
        var projectId = await CreateReviewAsync();
        await AuditTestExtensions.ApproveReviewAsync(Store, Time, projectId, "REV-1", cancellationToken: TestContext.Current.CancellationToken);

        // Act: render the page
        var cut = RenderPage(projectId);

        // Assert: the notice, and the answer buttons of the opened section are disabled
        Assert.Contains("locked", cut.Find("[data-testid='checklist-locked']").TextContent, StringComparison.Ordinal);
        await cut.Find("[data-testid='section-req-toggle']").ClickAsync();
        foreach (var testId in new[] { "check-pass-req-01", "check-fail-req-01", "check-na-req-01" })
        {
            Assert.True(cut.Find($"[data-testid='{testId}']").HasAttribute("disabled"), testId);
        }

        // Assert: pressing a disabled button changes nothing
        var state = await Checklists.GetAsync(projectId, "REV-1", TestContext.Current.CancellationToken);
        Assert.Equal(ChecklistOutcome.Pass, state!.AnswerFor("REQ-01")!.Outcome);
    }

    /// <summary>
    ///     Test that a pass is saved at once, with the name and time, and updates the progress.
    /// </summary>
    [Fact]
    public async Task ChecklistPage_Answer_Pass_SavesImmediately()
    {
        // Arrange: a new review, with the reviewer signed in as Ann
        var projectId = await CreateReviewAsync();
        var cut = RenderPage(projectId);

        // Act: pass the first check
        await cut.Find("[data-testid='check-pass-req-01']").ClickAsync();

        // Assert: shown as pressed, with who and when, and no justification box
        Assert.Equal("true", cut.Find("[data-testid='check-pass-req-01']").GetAttribute("aria-pressed"));
        Assert.Equal("Answered by Ann, 2025-03-04 05:06 UTC", cut.Find("[data-testid='check-answered-by-req-01']").TextContent);
        Assert.Empty(cut.FindAll("[data-testid='check-justification-req-01']"));
        Assert.Equal("1 of 58 checks answered", cut.Find("[data-testid='checklist-progress']").TextContent);
        Assert.Equal("1 of 8", cut.Find("[data-testid='section-req-progress']").TextContent);

        // Assert: stored
        var state = await Checklists.GetAsync(projectId, "REV-1", TestContext.Current.CancellationToken);
        Assert.Equal(ChecklistOutcome.Pass, state!.AnswerFor("REQ-01")!.Outcome);
    }

    /// <summary>
    ///     Test that a fail asks for a justification, and saving without one shows an error next to it and stores nothing.
    /// </summary>
    [Fact]
    public async Task ChecklistPage_Answer_Fail_ShowsJustificationBoxAndRequiresText()
    {
        // Arrange: a new review, with the reviewer signed in as Ann
        var projectId = await CreateReviewAsync();
        var cut = RenderPage(projectId);
        Assert.Empty(cut.FindAll("[data-testid='check-justification-req-01']"));

        // Act: choose Fail, then save without a justification
        await cut.Find("[data-testid='check-fail-req-01']").ClickAsync();
        Assert.Contains("required", cut.Find("[data-testid='check-req-01'] label").TextContent, StringComparison.Ordinal);
        await cut.Find("[data-testid='check-save-req-01']").ClickAsync();

        // Assert: the box is shown, the error is clear, and nothing was stored
        Assert.NotNull(cut.Find("[data-testid='check-justification-req-01']"));
        Assert.Equal("A justification is required.", cut.Find("[data-testid='check-error-req-01']").TextContent);
        var state = await Checklists.GetAsync(projectId, "REV-1", TestContext.Current.CancellationToken);
        Assert.Null(state!.AnswerFor("REQ-01"));
    }

    /// <summary>
    ///     Test that a fail with a justification is saved, kept in the box, and shows who answered.
    /// </summary>
    [Fact]
    public async Task ChecklistPage_Answer_FailWithJustification_SavesAndShowsAnsweredBy()
    {
        // Arrange: a new review, with the reviewer signed in as Ann
        var projectId = await CreateReviewAsync();
        var cut = RenderPage(projectId);

        // Act: choose Fail, type a justification and save
        await cut.Find("[data-testid='check-fail-req-01']").ClickAsync();
        await cut.Find("[data-testid='check-justification-req-01']").InputAsync("Ids are not unique");
        await cut.Find("[data-testid='check-save-req-01']").ClickAsync();

        // Assert: counted as answered, shown with the justification and name, and stored
        Assert.Equal("1 of 58 checks answered", cut.Find("[data-testid='checklist-progress']").TextContent);
        Assert.Equal("Ids are not unique", cut.Find("[data-testid='check-justification-req-01']").GetAttribute("value"));
        Assert.Empty(cut.FindAll("[data-testid='check-error-req-01']"));
        Assert.StartsWith("Answered by Ann", cut.Find("[data-testid='check-answered-by-req-01']").TextContent, StringComparison.Ordinal);
        var state = await Checklists.GetAsync(projectId, "REV-1", TestContext.Current.CancellationToken);
        Assert.Equal("Ids are not unique", state!.AnswerFor("REQ-01")!.Justification);
    }

    /// <summary>
    ///     Test that the answer is recorded under the signed-in user, whatever the page is asked to show, and
    ///     that the page has no field for a name.
    /// </summary>
    [Fact]
    public async Task ChecklistPage_Answer_SignedInUser_IsRecordedAsAnsweredBy()
    {
        // Arrange: a new review, with the reviewer signed in as Bea
        SignInAs("bea", "Bea");
        var projectId = await CreateReviewAsync();
        var cut = RenderPage(projectId);

        // Act: pass the first check
        await cut.Find("[data-testid='check-pass-req-01']").ClickAsync();

        // Assert: no name field, and the answer carries the user name and display name of the signed-in user
        Assert.Empty(cut.FindAll("[data-testid='checklist-author-input']"));
        Assert.Contains("Answered by Bea", cut.Find("[data-testid='check-answered-by-req-01']").TextContent, StringComparison.Ordinal);
        var answer = (await Checklists.GetAsync(projectId, "REV-1", TestContext.Current.CancellationToken))!.AnswerFor("REQ-01")!;
        Assert.Equal(("bea", "Bea"), (answer.AnsweredByUsername, answer.AnsweredBy));
    }

    /// <summary>
    ///     Test that marking a section as not applicable without a reason shows an error and changes nothing.
    /// </summary>
    [Fact]
    public async Task ChecklistPage_Applicability_NotApplicableWithoutReason_ShowsError()
    {
        // Arrange: the design section is open and the reviewer is signed in
        var projectId = await CreateReviewAsync();
        var cut = RenderPage(projectId);
        await cut.Find("[data-testid='section-des-toggle']").ClickAsync();

        // Act: switch the section off and save without a reason
        await cut.Find("[data-testid='section-des-applicable']").ChangeAsync(new ChangeEventArgs { Value = false });
        await cut.Find("[data-testid='section-des-save']").ClickAsync();

        // Assert: the error is shown with the reason field, and the section still counts
        Assert.Equal("A reason is required.", cut.Find("[data-testid='section-des-error']").TextContent);
        Assert.NotNull(cut.Find("[data-testid='section-des-reason']"));
        Assert.Equal("0 of 58 checks answered", cut.Find("[data-testid='checklist-progress']").TextContent);
    }

    /// <summary>
    ///     Test that a section marked as not applicable with a reason counts as done and hides its checks.
    /// </summary>
    [Fact]
    public async Task ChecklistPage_Applicability_NotApplicableWithReason_CountsAsDoneAndHidesChecks()
    {
        // Arrange: the design section is open and the reviewer is signed in
        var projectId = await CreateReviewAsync();
        var cut = RenderPage(projectId);
        await cut.Find("[data-testid='section-des-toggle']").ClickAsync();

        // Act: switch the section off, give a reason and save
        await cut.Find("[data-testid='section-des-applicable']").ChangeAsync(new ChangeEventArgs { Value = false });
        await cut.Find("[data-testid='section-des-reason']").InputAsync("No design files");
        await cut.Find("[data-testid='section-des-save']").ClickAsync();

        // Assert: not applicable, no longer part of the total, no checks to answer
        Assert.Equal("Not applicable", cut.Find("[data-testid='section-des-progress']").TextContent);
        Assert.Equal("0 of 53 checks answered - 1 section not applicable", cut.Find("[data-testid='checklist-progress']").TextContent);
        Assert.Equal("53 checks unanswered.", cut.Find("[data-testid='checklist-status']").TextContent);
        Assert.Empty(cut.FindAll("[data-testid^='check-des-']"));
        var state = await Checklists.GetAsync(projectId, "REV-1", TestContext.Current.CancellationToken);
        Assert.Equal("No design files", state!.ReasonFor("DES"));

        // Act: switch the section on again
        await cut.Find("[data-testid='section-des-applicable']").ChangeAsync(new ChangeEventArgs { Value = true });

        // Assert: the checks are back and count again
        Assert.Equal(5, cut.FindAll("[data-testid^='check-des-']").Count);
        Assert.Equal("0 of 58 checks answered", cut.Find("[data-testid='checklist-progress']").TextContent);
    }

    /// <summary>
    ///     Test that the next-unanswered action opens the section of the first unanswered check and scrolls to it.
    /// </summary>
    [Fact]
    public async Task ChecklistPage_NextUnanswered_Click_OpensSectionOfFirstUnansweredCheck()
    {
        // Arrange: the first section is answered except its last check, and another section is open
        var projectId = await CreateReviewAsync();
        foreach (var item in DefaultChecklist.Current.Sections[0].Items.Take(7))
        {
            await Checklists.SetAnswerAsync(
                projectId, "REV-1", item.Id, ChecklistOutcome.Pass, null, TestActors.Ann, TestContext.Current.CancellationToken);
        }

        var cut = RenderPage(projectId);
        await cut.Find("[data-testid='section-code-toggle']").ClickAsync();
        Assert.Equal("false", cut.Find("[data-testid='section-req-toggle']").GetAttribute("aria-expanded"));

        // Act: ask for the next unanswered check
        await cut.Find("[data-testid='next-unanswered-button']").ClickAsync();

        // Assert: its section is open and the browser was asked to scroll to it
        Assert.Equal("true", cut.Find("[data-testid='section-req-toggle']").GetAttribute("aria-expanded"));
        Assert.NotNull(cut.Find("[data-testid='check-req-08']"));
        await cut.WaitForAssertionAsync(() =>
            Assert.Equal(["check-req-08", "center"], JSInterop.Invocations["reviewCenter.scrollToTestId"][^1].Arguments));
    }

    /// <summary>
    ///     Test that the next-unanswered action skips a section that was just switched off but not yet saved.
    /// </summary>
    [Fact]
    public async Task ChecklistPage_NextUnanswered_PendingNotApplicableSection_SkipsIt()
    {
        // Arrange: the first section is answered, so the second one is open; switch it off without saving
        var projectId = await CreateReviewAsync();
        await PassSectionsAsync(projectId, "REQ");
        var cut = RenderPage(projectId);
        await cut.Find("[data-testid='section-des-applicable']").ChangeAsync(new ChangeEventArgs { Value = false });

        // Act: ask for the next unanswered check
        await cut.Find("[data-testid='next-unanswered-button']").ClickAsync();

        // Assert: the section after the switched-off one is open, with its first check as the scroll target
        Assert.Equal("true", cut.Find("[data-testid='section-doc-toggle']").GetAttribute("aria-expanded"));
        Assert.NotNull(cut.Find("[data-testid='check-doc-01']"));
        await cut.WaitForAssertionAsync(() =>
            Assert.Equal(["check-doc-01", "center"], JSInterop.Invocations["reviewCenter.scrollToTestId"][^1].Arguments));
    }

    /// <summary>
    ///     Test that the next-unanswered action opens the reason field of a section that was just switched off
    ///     but not yet saved, when that section is all that is left.
    /// </summary>
    [Fact]
    public async Task ChecklistPage_NextUnanswered_OnlyPendingNotApplicableSectionLeft_OpensItsReasonField()
    {
        // Arrange: every section but one is answered; switch the last one off without saving, then close it
        var projectId = await CreateReviewAsync();
        await PassSectionsAsync(projectId, [.. DefaultChecklist.Current.Sections.Select(s => s.Id).Where(id => id != "DES")]);
        var cut = RenderPage(projectId);
        await cut.Find("[data-testid='section-des-applicable']").ChangeAsync(new ChangeEventArgs { Value = false });
        await cut.Find("[data-testid='section-des-toggle']").ClickAsync();
        Assert.Equal("false", cut.Find("[data-testid='section-des-toggle']").GetAttribute("aria-expanded"));

        // Act: ask for the next unanswered check
        await cut.Find("[data-testid='next-unanswered-button']").ClickAsync();

        // Assert: the section is open with its reason field, and the browser is asked to scroll to it
        Assert.Equal("true", cut.Find("[data-testid='section-des-toggle']").GetAttribute("aria-expanded"));
        Assert.NotNull(cut.Find("[data-testid='section-des-reason']"));
        await cut.WaitForAssertionAsync(() =>
            Assert.Equal(["section-des-reason", "center"], JSInterop.Invocations["reviewCenter.scrollToTestId"][^1].Arguments));
    }

    /// <summary>
    ///     Test that a complete checklist says so and offers no next action.
    /// </summary>
    [Fact]
    public async Task ChecklistPage_Render_AllAnswered_ShowsCompleteAndNoNextAction()
    {
        // Arrange: every check answered
        var projectId = await CreateReviewAsync();
        await PassSectionsAsync(projectId, [.. DefaultChecklist.Current.Sections.Select(s => s.Id)]);

        // Act: render the page
        var cut = RenderPage(projectId);

        // Assert: complete, with nothing to do next
        Assert.Equal("Checklist complete.", cut.Find("[data-testid='checklist-status']").TextContent);
        Assert.Empty(cut.FindAll("[data-testid='next-unanswered-button']"));
        Assert.Equal("/projects/" + projectId + "/reviews/REV-1", cut.Find("[data-testid='back-to-review-link']").GetAttribute("href"));
    }

    /// <summary>
    ///     Test that a finished section offers the next unanswered check at its end, and the way back to the
    ///     review once the whole checklist is complete.
    /// </summary>
    [Fact]
    public async Task ChecklistPage_FinishedSection_OffersNextActionAtItsEnd()
    {
        // Arrange: the first section is answered, so the second one is open; open the first again
        var projectId = await CreateReviewAsync();
        await PassSectionsAsync(projectId, "REQ");
        var cut = RenderPage(projectId);
        await cut.Find("[data-testid='section-req-toggle']").ClickAsync();

        // Assert: the finished section offers the next unanswered check, and not the way back
        Assert.Empty(cut.FindAll("[data-testid='section-req-back']"));

        // Act: follow it
        await cut.Find("[data-testid='section-req-next']").ClickAsync();

        // Assert: the section of the next unanswered check is open and has no such action yet
        Assert.Equal("true", cut.Find("[data-testid='section-des-toggle']").GetAttribute("aria-expanded"));
        Assert.Empty(cut.FindAll("[data-testid='section-des-next']"));

        // Arrange: answer everything and open the first section
        await PassSectionsAsync(projectId, [.. DefaultChecklist.Current.Sections.Select(s => s.Id)]);
        cut = RenderPage(projectId);
        await cut.Find("[data-testid='section-req-toggle']").ClickAsync();

        // Assert: the end of the section leads back to the review instead
        Assert.Equal("/projects/" + projectId + "/reviews/REV-1", cut.Find("[data-testid='section-req-back']").GetAttribute("href"));
        Assert.Empty(cut.FindAll("[data-testid='section-req-next']"));
    }

    /// <summary>
    ///     Test that every interactive element of the page, with a section open, a Fail pending and an error
    ///     shown, has a kebab-case <c>data-testid</c>.
    /// </summary>
    [Fact]
    public async Task ChecklistPage_Render_EveryInteractiveElementHasKebabCaseTestId()
    {
        // Arrange: a Fail chosen and saved without a justification, and a not-applicable section open
        var cut = RenderPage(await CreateReviewAsync());
        await cut.Find("[data-testid='check-fail-req-01']").ClickAsync();
        await cut.Find("[data-testid='check-save-req-01']").ClickAsync();
        await cut.Find("[data-testid='section-des-toggle']").ClickAsync();
        await cut.Find("[data-testid='section-des-applicable']").ChangeAsync(new ChangeEventArgs { Value = false });
        await cut.Find("[data-testid='section-des-save']").ClickAsync();
        await cut.Find("[data-testid='section-req-toggle']").ClickAsync();

        // Act: collect the interactive elements of the whole page, then of the open requirements section
        var elements = cut.FindAll("a, button, input, select, textarea");
        await cut.Find("[data-testid='section-req-toggle']").ClickAsync();
        elements = [.. elements, .. cut.FindAll("a, button, input, select, textarea")];

        // Assert: there are many, and each has a kebab-case test id
        Assert.True(elements.Count > 30);
        foreach (var element in elements)
        {
            var testId = element.GetAttribute("data-testid");
            Assert.True(
                testId is not null && KebabCaseTestId().IsMatch(testId),
                $"A <{element.LocalName}> has no kebab-case data-testid (found '{testId}').");
        }
    }

    /// <summary>
    ///     Asserts that the page shows only the not-found message and nothing of the project, the review or its
    ///     checklist.
    /// </summary>
    /// <param name="cut">The rendered page.</param>
    private static void AssertNothingOfTheChecklistIsShown(IRenderedComponent<ChecklistPage> cut)
    {
        Assert.NotEmpty(cut.FindAll("[data-testid='checklist-not-found']"));
        Assert.Empty(cut.FindAll("[data-testid='checklist-status']"));
        Assert.Empty(cut.FindAll("[data-testid='checklist-progress']"));
        foreach (var secret in new[] { "Apollo", "First review", "REQ-01", "checks answered", "Answered by" })
        {
            Assert.DoesNotContain(secret, cut.Markup, StringComparison.Ordinal);
        }
    }

    /// <summary>
    ///     Test that a user who is no member of the project gets the same page as for a review that does not exist and
    ///     sees nothing of the checklist.
    /// </summary>
    [Fact]
    public async Task ChecklistPage_Render_NonMember_LooksLikeAMissingReview()
    {
        // Arrange: a review, opened by a user who is no member of its project
        var projectId = await CreateReviewAsync();
        await Users.CreateAsync("out", "Out", UserRole.Reviewer, "correct horse battery", TestContext.Current.CancellationToken);
        SignInAs("out", "Out", UserRole.Reviewer);

        // Act
        var cut = RenderPage(projectId);
        var missing = RenderPage(999);

        // Assert: the message is the one for a missing project and nothing of the checklist is shown
        Assert.Equal(
            missing.Find("[data-testid='checklist-not-found']").TextContent.Replace("project 999", "project #"),
            cut.Find("[data-testid='checklist-not-found']").TextContent.Replace($"project {projectId}", "project #"));
        AssertNothingOfTheChecklistIsShown(cut);
    }

    /// <summary>
    ///     Test that a member who loses the membership while the checklist is open gets the not-found page at the next
    ///     answer, which is not stored, and that nothing of the checklist stays in the page.
    /// </summary>
    [Fact]
    public async Task ChecklistPage_Choose_MembershipRevoked_ShowsNotFoundAndNoData()
    {
        // Arrange: a member has the checklist open and sees it
        var projectId = await CreateReviewAsync();
        var member = await Users.CreateAsync("mem", "Mem", UserRole.Reviewer, "correct horse battery", TestContext.Current.CancellationToken);
        await Store.AddMembershipAsync(member.Id, projectId, TestContext.Current.CancellationToken);
        SignInAs("mem", "Mem", UserRole.Reviewer);
        var cut = RenderPage(projectId);
        Assert.NotEmpty(cut.FindAll("[data-testid='checklist-status']"));
        await RevokeMembershipAsync(projectId, "mem");

        // Act: pass the first check
        await cut.Find("[data-testid='check-pass-req-01']").ClickAsync();

        // Assert: the not-found page, no data, and no answer stored
        AssertNothingOfTheChecklistIsShown(cut);
        var state = await Checklists.GetAsync(projectId, "REV-1", TestContext.Current.CancellationToken);
        Assert.Null(state!.AnswerFor("REQ-01"));
    }

    /// <summary>
    ///     Test that opening or closing a section of an open page also re-checks the access, so the data that was
    ///     loaded disappears at the first interaction after the removal.
    /// </summary>
    [Fact]
    public async Task ChecklistPage_ToggleSection_MembershipRevoked_ShowsNotFoundAndNoData()
    {
        // Arrange: a member has the checklist open
        var projectId = await CreateReviewAsync();
        var member = await Users.CreateAsync("mem", "Mem", UserRole.Reviewer, "correct horse battery", TestContext.Current.CancellationToken);
        await Store.AddMembershipAsync(member.Id, projectId, TestContext.Current.CancellationToken);
        SignInAs("mem", "Mem", UserRole.Reviewer);
        var cut = RenderPage(projectId);
        await RevokeMembershipAsync(projectId, "mem");

        // Act: open the second section
        await cut.Find("[data-testid='section-des-toggle']").ClickAsync();

        // Assert
        AssertNothingOfTheChecklistIsShown(cut);
    }
}
