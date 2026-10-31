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
using TestContext = Xunit.TestContext;

namespace DemaConsulting.ReviewCenter.Tests.Web;

/// <summary>
///     Unit tests for what <see cref="ChecklistPage"/> shows of a follow-up review: the "Carried from" marks of the
///     carried answers and sections, and the earlier Fail of a check that must be decided again.
/// </summary>
public class ChecklistPageFollowUpTests : PageTestBase
{
    /// <summary>
    ///     Creates the project, the earlier review <c>REV-1</c> (REQ-01 failed, REQ-02 passed, section DES not
    ///     applicable) and the follow-up <c>REV-2</c>.
    /// </summary>
    /// <returns>The project id.</returns>
    private async Task<int> CreateFollowUpAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var project = await Projects.CreateAsync("Apollo", null, cancellationToken);
        await Reviews.CreateDraftAsync(
            project.Id, "REV-1", "First review", [new("a.txt", TestContent.A)], TestActors.Ann, cancellationToken);
        await Checklists.SetAnswerAsync(
            project.Id, "REV-1", "REQ-01", ChecklistOutcome.Fail, "Missing trace", TestActors.Bob, cancellationToken);
        await Checklists.SetAnswerAsync(
            project.Id, "REV-1", "REQ-02", ChecklistOutcome.Pass, null, TestActors.Bob, cancellationToken);
        await Checklists.SetSectionApplicabilityAsync(
            project.Id, "REV-1", "DES", false, "No design", TestActors.Bob, cancellationToken);
        await Reviews.CreateDraftAsync(
            project.Id,
            "REV-2",
            "Follow-up",
            [new("a.txt", TestContent.A)],
            TestActors.Ann,
            cancellationToken,
            null,
            "REV-1");
        return project.Id;
    }

    /// <summary>
    ///     Renders the page for a review.
    /// </summary>
    /// <param name="projectId">The project identity.</param>
    /// <param name="reviewId">The review id.</param>
    /// <returns>The rendered component.</returns>
    private IRenderedComponent<ChecklistPage> RenderPage(int projectId, string reviewId)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        return Render<ChecklistPage>(parameters => parameters
            .Add(page => page.ProjectId, projectId)
            .Add(page => page.ReviewId, reviewId));
    }

    /// <summary>
    ///     Test that a carried answer and a carried section are marked with a link to the review they come from, and
    ///     that a check that failed there is shown with the earlier Fail and no carried mark.
    /// </summary>
    [Fact]
    public async Task ChecklistPage_Render_FollowUp_ShowsCarriedMarksAndEarlierFail()
    {
        // Arrange: REV-2 follows REV-1
        var projectId = await CreateFollowUpAsync();

        // Act
        var cut = RenderPage(projectId, "REV-2");

        // Assert: the Pass is carried with a link to REV-1
        var link = cut.Find("[data-testid='check-carried-link-req-02']");
        Assert.Equal("REV-1", link.TextContent);
        Assert.Equal(ReviewLinks.ForReview(projectId, "REV-1"), link.GetAttribute("href"));
        Assert.Contains("Carried from", cut.Find("[data-testid='check-carried-req-02']").TextContent, StringComparison.Ordinal);

        // Assert: the section that does not apply is carried with a link
        Assert.Equal("REV-1", cut.Find("[data-testid='section-des-carried-link']").TextContent);

        // Assert: the Fail is not carried, but the earlier Fail and its reason are shown and the check is open
        Assert.Empty(cut.FindAll("[data-testid='check-carried-req-01']"));
        var fail = Regex.Replace(cut.Find("[data-testid='check-earlier-fail-req-01']").TextContent, @"\s+", " ").Trim();
        Assert.Equal("Failed in review REV-1: Missing trace Decide this check again.", fail);
        Assert.Equal(ReviewLinks.ForReview(projectId, "REV-1"), cut.Find("[data-testid='check-earlier-fail-link-req-01']").GetAttribute("href"));
        Assert.Empty(cut.FindAll("[data-testid='check-earlier-fail-req-02']"));
    }

    /// <summary>
    ///     Test that the earlier Fail goes away once the check is decided again in the follow-up, and that the earlier
    ///     review itself shows no carried mark and no earlier Fail.
    /// </summary>
    [Fact]
    public async Task ChecklistPage_Render_FollowUp_DecidedAgainOrEarlierReview_ShowsNoEarlierFail()
    {
        // Arrange: REV-2 follows REV-1 and the check is answered Pass there
        var projectId = await CreateFollowUpAsync();
        await Checklists.SetAnswerAsync(
            projectId, "REV-2", "REQ-01", ChecklistOutcome.Pass, null, TestActors.Cy, TestContext.Current.CancellationToken);

        // Act
        var follow = RenderPage(projectId, "REV-2");
        var earlier = RenderPage(projectId, "REV-1");

        // Assert: the new answer is the follow-up's own, so there is no mark and no earlier Fail
        Assert.Empty(follow.FindAll("[data-testid='check-earlier-fail-req-01']"));
        Assert.Empty(follow.FindAll("[data-testid='check-carried-req-01']"));
        Assert.NotEmpty(follow.FindAll("[data-testid='check-carried-req-02']"));

        // Assert: the followed review shows nothing of it
        Assert.Empty(earlier.FindAll("[data-testid^='check-carried-']"));
        Assert.Empty(earlier.FindAll("[data-testid^='check-earlier-fail-']"));
        Assert.Empty(earlier.FindAll("[data-testid^='section-'][data-testid$='-carried']"));
    }

    /// <summary>
    ///     Test that the earlier Fail is shown only for a check whose wording is the same in both reviews: a check that
    ///     failed in the earlier review and was reworded since shows no earlier Fail, while an unchanged one still does.
    /// </summary>
    [Fact]
    public async Task ChecklistPage_Render_FollowUp_ReworkedCheckThatFailed_ShowsNoEarlierFail()
    {
        // Arrange: the project's checklist has two checks, both fail in REV-1, then the project rewords the first one
        var cancellationToken = TestContext.Current.CancellationToken;
        var project = await Projects.CreateAsync("Apollo", null, cancellationToken);
        await SetChecklistAsync(project.Id, Definition("custom-1", "Is it right?"));
        await Reviews.CreateDraftAsync(
            project.Id, "REV-1", "First review", [new("a.txt", TestContent.A)], TestActors.Ann, cancellationToken);
        foreach (var item in new[] { "ck-01", "ck-02" })
        {
            await Checklists.SetAnswerAsync(
                project.Id, "REV-1", item, ChecklistOutcome.Fail, "Not right", TestActors.Bob, cancellationToken);
        }

        await SetChecklistAsync(project.Id, Definition("custom-2", "Is it correct?"));
        await Reviews.CreateDraftAsync(
            project.Id,
            "REV-2",
            "Follow-up",
            [new("a.txt", TestContent.A)],
            TestActors.Ann,
            cancellationToken,
            null,
            "REV-1");

        // Act
        var cut = RenderPage(project.Id, "REV-2");

        // Assert: the reworded check shows no earlier Fail, the unchanged check still does
        Assert.Empty(cut.FindAll("[data-testid='check-earlier-fail-ck-01']"));
        Assert.NotEmpty(cut.FindAll("[data-testid='check-ck-01']"));
        Assert.NotEmpty(cut.FindAll("[data-testid='check-earlier-fail-ck-02']"));
    }

    /// <summary>
    ///     Builds a checklist definition with one section of two checks.
    /// </summary>
    /// <param name="version">The version label.</param>
    /// <param name="firstText">The wording of the first check.</param>
    /// <returns>The JSON text of the definition.</returns>
    private static string Definition(string version, string firstText)
    {
        return new ChecklistDefinition(
            version,
            [new ChecklistSection("sec", "Section", string.Empty, [new ChecklistItem("ck-01", firstText), new ChecklistItem("ck-02", "Is it complete?")])]).ToJson();
    }

    /// <summary>
    ///     Replaces the checklist definition of the project.
    /// </summary>
    /// <param name="projectId">The project identity.</param>
    /// <param name="json">The new definition.</param>
    /// <returns>A task that completes when the definition is stored.</returns>
    private async Task SetChecklistAsync(int projectId, string json)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var current = await ChecklistTemplates.GetAsync(TestActors.Ann, projectId, cancellationToken);
        Assert.True(await ChecklistTemplates.SetAsync(TestActors.Ann, projectId, current.Hash, json, cancellationToken));
    }
}
