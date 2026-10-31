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
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using TestContext = Xunit.TestContext;

namespace DemaConsulting.ReviewCenter.Tests.Web;

/// <summary>
///     Unit tests for the progress overview of the <see cref="ProjectPage"/> component: the columns, the summary
///     strip, the filters, the sort order and the paging, all driven by the query string, rendered with bUnit.
/// </summary>
/// <remarks>The clock of the pages is fixed at 2025-03-04, so a due date of 2025-03-03 is overdue.</remarks>
public class ProjectPageOverviewTests : PageTestBase
{
    /// <summary>
    ///     The password of the users the tests create.
    /// </summary>
    private const string Password = "correct horse battery";

    /// <summary>
    ///     Renders the project page for a project with a query string, as a browser would address it.
    /// </summary>
    /// <param name="projectId">The project identity.</param>
    /// <param name="query">The query string including the question mark, or empty.</param>
    /// <returns>The rendered component.</returns>
    private IRenderedComponent<ProjectPage> RenderAt(int projectId, string query = "")
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/projects/{projectId}{query}");
        return Render<ProjectPage>(parameters => parameters.Add(page => page.ProjectId, projectId));
    }

    /// <summary>
    ///     Renders the project page with a query string and reads the review identifiers of the table at once, as
    ///     a later navigation updates the pages that were rendered before.
    /// </summary>
    /// <param name="projectId">The project identity.</param>
    /// <param name="query">The query string including the question mark, or empty.</param>
    /// <returns>The review identifiers in the order shown.</returns>
    private List<string> RowsAt(int projectId, string query)
    {
        return RowIds(RenderAt(projectId, query));
    }

    /// <summary>
    ///     Gets the review identifiers of the rows of the table in the order shown.
    /// </summary>
    /// <param name="cut">The rendered page.</param>
    /// <returns>The review identifiers.</returns>
    private static List<string> RowIds(IRenderedComponent<ProjectPage> cut)
    {
        const string prefix = "review-row-";
        return cut.FindAll("[data-testid^='review-row-']")
            .Select(row => row.GetAttribute("data-testid")![prefix.Length..])
            .ToList();
    }

    /// <summary>
    ///     Arranges a project of four reviews: REV-A approved, REV-B in review (two slots of which one is signed, an
    ///     open thread, a Fail answer, overdue), REV-C in setup (no slots, due in the future) and REV-D in review
    ///     (one unsigned slot, no due date).
    /// </summary>
    /// <returns>The project identity.</returns>
    private async Task<int> SeedAsync()
    {
        var token = TestContext.Current.CancellationToken;
        var ann = await Users.CreateAsync("ann", "Ann", UserRole.Reviewer, Password, token);
        var bob = await Users.CreateAsync("bob", "Bob", UserRole.Reviewer, Password, token);
        var project = await Projects.CreateAsync("Apollo", null, token);
        await Store.AddMembershipAsync(ann.Id, project.Id, token);
        await Store.AddMembershipAsync(bob.Id, project.Id, token);
        foreach (var (id, title) in new[] { ("REV-A", "Alpha design"), ("REV-B", "Beta code"), ("REV-C", "Gamma tests"), ("REV-D", "Delta docs") })
        {
            await Reviews.CreateDraftAsync(project.Id, id, title, [new("a.txt", TestContent.A)], token);
        }

        await AnswerAllAsync(project.Id, "REV-A");
        var slotA = await Signing.AddSlotAsync(TestActors.Ann, project.Id, "REV-A", ann.Id, null, token);
        var approvalA = await Signing.GetApprovalAsync(project.Id, "REV-A", token);
        await Signing.SignAsync(TestActors.Ann, project.Id, "REV-A", slotA.Id, approvalA!.Digest, Password, token);

        await AnswerAllAsync(project.Id, "REV-B");
        var slotB = await Signing.AddSlotAsync(TestActors.Ann, project.Id, "REV-B", ann.Id, null, token);
        await Signing.AddSlotAsync(TestActors.Ann, project.Id, "REV-B", bob.Id, null, token);
        var approvalB = await Signing.GetApprovalAsync(project.Id, "REV-B", token);
        await Signing.SignAsync(TestActors.Ann, project.Id, "REV-B", slotB.Id, approvalB!.Digest, Password, token);

        // After the signature the record changes, so the signature of Ann is out of date
        var first = DefaultChecklist.Current.Sections.SelectMany(section => section.Items).First();
        await Checklists.SetAnswerAsync(
            project.Id, "REV-B", first.Id, ChecklistOutcome.Fail, "Wrong", TestActors.Ann, token);
        await Threads.StartThreadAsync(project.Id, "REV-B", new ThreadAnchor("a.txt"), TestActors.Bob, "Look", token);
        await Reviews.SetDueDateAsync(project.Id, "REV-B", new DateOnly(2025, 3, 3), TestActors.Ann, token);

        await Reviews.SetDueDateAsync(project.Id, "REV-C", new DateOnly(2025, 4, 1), TestActors.Ann, token);
        await Signing.AddSlotAsync(TestActors.Ann, project.Id, "REV-D", bob.Id, null, token);
        return project.Id;
    }

    /// <summary>
    ///     Answers every checklist item of a review with Pass.
    /// </summary>
    /// <param name="projectId">The project identity.</param>
    /// <param name="reviewId">The review identifier.</param>
    /// <returns>A task that completes when all items are answered.</returns>
    private async Task AnswerAllAsync(int projectId, string reviewId)
    {
        foreach (var item in DefaultChecklist.Current.Sections.SelectMany(section => section.Items))
        {
            await Checklists.SetAnswerAsync(
                projectId, reviewId, item.Id, ChecklistOutcome.Pass, null, TestActors.Ann, TestContext.Current.CancellationToken);
        }
    }

    /// <summary>
    ///     Test that every review row shows its stage, signatures, blockers, who is waited on and the due date with
    ///     the overdue badge, and that the summary strip counts the stages and the overdue reviews.
    /// </summary>
    [Fact]
    public async Task ProjectPage_Render_Overview_ShowsStageSignaturesBlockersWaitingOnAndSummary()
    {
        // Arrange
        var projectId = await SeedAsync();

        // Act
        var cut = RenderAt(projectId);

        // Assert: the rows, in the default order by review identifier
        Assert.Equal(["REV-A", "REV-B", "REV-C", "REV-D"], RowIds(cut));
        Assert.Equal("Approved", cut.Find("[data-testid='review-approval-REV-A']").TextContent);
        Assert.Equal("1/1", cut.Find("[data-testid='review-progress-REV-A']").TextContent);
        Assert.Equal("In review", cut.Find("[data-testid='review-approval-REV-B']").TextContent);
        Assert.Equal("0/2", cut.Find("[data-testid='review-progress-REV-B']").TextContent);
        Assert.Equal("1 out of date", cut.Find("[data-testid='review-outofdate-REV-B']").TextContent);
        Assert.Equal("Ann, Bob", cut.Find("[data-testid='review-waiting-REV-B']").TextContent);
        Assert.Equal("1 open thread, 1 Fail", cut.Find("[data-testid='review-blockers-REV-B']").TextContent);
        Assert.Equal("2025-03-03", cut.Find("[data-testid='review-due-REV-B']").TextContent);
        Assert.Equal("Overdue", cut.Find("[data-testid='review-overdue-REV-B']").TextContent);
        Assert.Empty(cut.FindAll("[data-testid='review-overdue-REV-A']"));
        Assert.Empty(cut.FindAll("[data-testid='review-overdue-REV-C']"));
        Assert.Equal("Setup", cut.Find("[data-testid='review-approval-REV-C']").TextContent);
        Assert.Equal("-", cut.Find("[data-testid='review-progress-REV-C']").TextContent);
        Assert.Equal("Bob", cut.Find("[data-testid='review-waiting-REV-D']").TextContent);
        Assert.Equal("-", cut.Find("[data-testid='review-due-REV-D']").TextContent);

        // Assert: the summary strip
        Assert.Equal("1 in setup", cut.Find("[data-testid='project-summary-setup']").TextContent);
        Assert.Equal("2 in review", cut.Find("[data-testid='project-summary-in-review']").TextContent);
        Assert.Equal("1 approved", cut.Find("[data-testid='project-summary-approved']").TextContent);
        Assert.Equal("1 overdue", cut.Find("[data-testid='project-summary-overdue']").TextContent);
    }

    /// <summary>
    ///     Test that the stage, overdue, blocked and text filters of the query string narrow the table, separately
    ///     and together, while the summary strip keeps counting the whole project.
    /// </summary>
    [Fact]
    public async Task ProjectPage_Render_QueryFilters_NarrowTheTable()
    {
        // Arrange
        var projectId = await SeedAsync();

        // Act: render once per query and read the rows at once (a later navigation updates the pages rendered before)
        var approved = RowsAt(projectId, "?stage=approved");
        var inReview = RowsAt(projectId, "?stage=in-review");
        var setup = RowsAt(projectId, "?stage=setup");
        var overdue = RowsAt(projectId, "?overdue=1");
        var blocked = RowsAt(projectId, "?blocked=1&stage=in-review");
        var byTitle = RowsAt(projectId, "?q=gamma");
        var byId = RowsAt(projectId, "?q=rev-d");
        var none = RenderAt(projectId, "?q=nothing-like-this");

        // Assert
        Assert.Equal(["REV-A"], approved);
        Assert.Equal(["REV-B", "REV-D"], inReview);
        Assert.Equal(["REV-C"], setup);
        Assert.Equal(["REV-B"], overdue);
        Assert.Equal(["REV-B", "REV-D"], blocked);
        Assert.Equal(["REV-C"], byTitle);
        Assert.Equal(["REV-D"], byId);
        Assert.Empty(none.FindAll("[data-testid='reviews-list']"));
        Assert.NotEmpty(none.FindAll("[data-testid='reviews-no-match']"));
        Assert.Equal("1 overdue", none.Find("[data-testid='project-summary-overdue']").TextContent);
        var selected = RenderAt(projectId, "?stage=approved");
        Assert.NotNull(selected.Find("[data-testid='filter-stage'] option[value='approved']").GetAttribute("selected"));
    }

    /// <summary>
    ///     Test that the sort and dir query values order the table (by stage, signatures and due date, with the
    ///     reviews without a due date last) and that the header links toggle the direction of the sorted column.
    /// </summary>
    [Fact]
    public async Task ProjectPage_Render_QuerySort_OrdersTheTableAndTheHeaderLinksToggle()
    {
        // Arrange
        var projectId = await SeedAsync();

        // Act: render once per query and read the rows at once
        var byStage = RowsAt(projectId, "?sort=stage");
        var byStageDescending = RowsAt(projectId, "?sort=stage&dir=desc");
        var bySignatures = RowsAt(projectId, "?sort=signatures&dir=desc");
        var byDue = RowsAt(projectId, "?sort=due");
        var byDueDescending = RowsAt(projectId, "?sort=due&dir=desc");
        var byIdDescending = RowsAt(projectId, "?sort=id&dir=desc");
        var ascending = RenderAt(projectId, "?sort=stage");
        var ascendingLink = ascending.Find("[data-testid='sort-stage']").GetAttribute("href");
        var ascendingSort = ascending.Find("[data-testid='sort-stage']").ParentElement!.GetAttribute("aria-sort");
        var otherLink = ascending.Find("[data-testid='sort-due']").GetAttribute("href");
        var descending = RenderAt(projectId, "?sort=stage&dir=desc");
        var descendingLink = descending.Find("[data-testid='sort-stage']").GetAttribute("href");
        var descendingSort = descending.Find("[data-testid='sort-stage']").ParentElement!.GetAttribute("aria-sort");

        // Assert: setup (0) before in review (1) before approved (2), ties by identifier
        Assert.Equal(["REV-C", "REV-B", "REV-D", "REV-A"], byStage);
        Assert.Equal(["REV-A", "REV-B", "REV-D", "REV-C"], byStageDescending);

        // Assert: the share of signatures, the most complete first; reviews without slots are the least complete
        Assert.Equal(["REV-A", "REV-B", "REV-D", "REV-C"], bySignatures);

        // Assert: due dates ascending and descending, the review without a date always last
        Assert.Equal(["REV-B", "REV-C", "REV-A", "REV-D"], byDue);
        Assert.Equal(["REV-C", "REV-B", "REV-A", "REV-D"], byDueDescending);
        Assert.Equal(["REV-D", "REV-C", "REV-B", "REV-A"], byIdDescending);

        // Assert: the header link of the sorted column flips the direction, the others start ascending
        Assert.Contains("sort=stage", ascendingLink, StringComparison.Ordinal);
        Assert.Contains("dir=desc", ascendingLink, StringComparison.Ordinal);
        Assert.DoesNotContain("dir=desc", descendingLink, StringComparison.Ordinal);
        Assert.DoesNotContain("dir=desc", otherLink, StringComparison.Ordinal);
        Assert.Equal("ascending", ascendingSort);
        Assert.Equal("descending", descendingSort);
    }

    /// <summary>
    ///     Test that the table shows 25 reviews per page, that the page query value selects the page, that a page
    ///     beyond the end shows the last one, and that the links keep the filters.
    /// </summary>
    [Fact]
    public async Task ProjectPage_Render_ManyReviews_PagesBy25AndKeepsTheFilters()
    {
        // Arrange: 30 reviews, 27 of them with the word "bulk" in the title
        var token = TestContext.Current.CancellationToken;
        var project = await Projects.CreateAsync("Apollo", null, token);
        for (var i = 1; i <= 30; i++)
        {
            await Reviews.CreateDraftAsync(
                project.Id, $"R-{i:00}", i <= 27 ? $"Bulk {i}" : $"Other {i}", [new("a.txt", TestContent.A)], token);
        }

        // Act: render once per query and read at once (a later navigation updates the pages rendered before)
        var first = RenderAt(project.Id);
        var firstRows = RowIds(first);
        var firstRange = first.Find("[data-testid='reviews-range']").TextContent;
        var firstPrevious = first.FindAll("[data-testid='reviews-prev']").Count;
        var firstNext = first.Find("[data-testid='reviews-next']").GetAttribute("href");
        var firstSummary = first.Find("[data-testid='project-summary-setup']").TextContent;
        var secondPage = RenderAt(project.Id, "?page=2");
        var secondRows = RowIds(secondPage);
        var secondRange = secondPage.Find("[data-testid='reviews-range']").TextContent;
        var secondNext = secondPage.FindAll("[data-testid='reviews-next']").Count;
        var beyondRows = RowsAt(project.Id, "?page=9");
        var filteredRows = RowsAt(project.Id, "?q=bulk&page=2");
        var filteredPage = RenderAt(project.Id, "?q=bulk");
        var filteredNext = filteredPage.Find("[data-testid='reviews-next']").GetAttribute("href");

        // Assert
        Assert.Equal(25, firstRows.Count);
        Assert.Equal("R-01", firstRows[0]);
        Assert.Equal("Showing 1-25 of 30", firstRange);
        Assert.Equal(0, firstPrevious);
        Assert.Contains("page=2", firstNext, StringComparison.Ordinal);
        Assert.Equal("30 in setup", firstSummary);
        Assert.Equal(["R-26", "R-27", "R-28", "R-29", "R-30"], secondRows);
        Assert.Equal("Showing 26-30 of 30", secondRange);
        Assert.Equal(0, secondNext);
        Assert.Equal(secondRows, beyondRows);
        Assert.Equal(["R-26", "R-27"], filteredRows);
        Assert.Contains("q=bulk", filteredNext, StringComparison.Ordinal);
        Assert.Contains("page=2", filteredNext, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that query values that are not understood (an unknown stage, sort, or direction) are ignored and give
    ///     the default view, and that the filters that need exactly one still work next to them.
    /// </summary>
    [Fact]
    public async Task ProjectPage_Render_InvalidQueryValues_AreIgnoredAndShowTheDefaultView()
    {
        // Arrange
        var projectId = await SeedAsync();
        var defaultRows = RowsAt(projectId, string.Empty);

        // Act: render once per query and read the rows at once
        var badStage = RowsAt(projectId, "?stage=bogus");
        var badSort = RowsAt(projectId, "?sort=bogus");
        var badDirection = RowsAt(projectId, "?dir=bogus");
        var allBad = RowsAt(projectId, "?stage=bogus&sort=bogus&dir=bogus&overdue=yes&blocked=true&page=abc");
        var badDirectionWithSort = RowsAt(projectId, "?sort=due&dir=sideways");

        // Assert: the default is every review by identifier; a bad direction falls back to the column default
        Assert.Equal(["REV-A", "REV-B", "REV-C", "REV-D"], defaultRows);
        Assert.Equal(defaultRows, badStage);
        Assert.Equal(defaultRows, badSort);
        Assert.Equal(defaultRows, badDirection);
        Assert.Equal(defaultRows, allBad);
        Assert.Equal(["REV-B", "REV-C", "REV-A", "REV-D"], badDirectionWithSort);
    }

    /// <summary>
    ///     Test that a page query value that is not a positive whole number inside the range (text, zero, a negative
    ///     number, a number too large for an integer, a decimal) shows a valid page, and that one beyond the last page
    ///     shows the last page.
    /// </summary>
    [Fact]
    public async Task ProjectPage_Render_InvalidPageValues_ShowAValidPage()
    {
        // Arrange: 30 reviews, so there are two pages
        var token = TestContext.Current.CancellationToken;
        var project = await Projects.CreateAsync("Apollo", null, token);
        for (var i = 1; i <= 30; i++)
        {
            await Reviews.CreateDraftAsync(project.Id, $"R-{i:00}", $"Title {i}", [new("a.txt", TestContent.A)], token);
        }

        // Act: render once per query and read the range of the page at once
        var ranges = new Dictionary<string, string>();
        foreach (var page in new[] { "abc", "0", "-3", "99999999999", "1.5", "", "2", "9" })
        {
            var cut = RenderAt(project.Id, $"?page={page}");
            ranges[page] = cut.Find("[data-testid='reviews-range']").TextContent;
        }

        // Assert: anything that is not a number inside the range gives the first page, a number beyond the end the last
        foreach (var page in new[] { "abc", "0", "-3", "99999999999", "1.5", string.Empty })
        {
            Assert.Equal("Showing 1-25 of 30", ranges[page]);
        }

        Assert.Equal("Showing 26-30 of 30", ranges["2"]);
        Assert.Equal("Showing 26-30 of 30", ranges["9"]);
    }

    /// <summary>
    ///     Test that markup in the search text of the query string and in the title of a review is shown as text,
    ///     encoded, and never becomes an element of the page.
    /// </summary>
    [Fact]
    public async Task ProjectPage_Render_HostileMarkup_IsRenderedEncoded()
    {
        // Arrange: a review whose title and identifier carry markup
        var token = TestContext.Current.CancellationToken;
        var project = await Projects.CreateAsync("Apollo", null, token);
        const string hostile = "<script>alert(1)</script><img src=x onerror=alert(2)>";
        await Reviews.CreateDraftAsync(project.Id, "REV-X", hostile, [new("a.txt", TestContent.A)], token);

        // Act: the page with the review, and one whose search text is the markup (it matches the title) and whose
        // other values carry markup too
        var withTitle = RenderAt(project.Id);
        var withQuery = RenderAt(
            project.Id, $"?q={Uri.EscapeDataString(hostile)}&stage={Uri.EscapeDataString("\"'><b>x</b>")}");

        // Assert: no script, image or bold element exists, and the markup serialized to text is encoded
        foreach (var cut in new[] { withTitle, withQuery })
        {
            Assert.Empty(cut.FindAll("script"));
            Assert.Empty(cut.FindAll("img"));
            Assert.Empty(cut.FindAll("main b, form b, table b"));
        }

        // The search text only ever sits in the value of its input (an attribute), and the title in a text node
        Assert.Equal(hostile, withQuery.Find("[data-testid='filter-search']").GetAttribute("value"));
        Assert.Contains("&lt;script&gt;alert(1)", withTitle.Markup, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;alert(1)", withQuery.Markup, StringComparison.Ordinal);
        Assert.Equal("1 in setup", withTitle.Find("[data-testid='project-summary-setup']").TextContent);
    }

    /// <summary>
    ///     Test that an auditor of the project, who may only read, sees the same overview table as a member, and
    ///     that a user who is no member gets the not-found state without any table.
    /// </summary>
    [Fact]
    public async Task ProjectPage_Render_AuditorAndStranger_AuditorSeesTheTableStrangerNothing()
    {
        // Arrange: the seeded project, an auditor and a stranger
        var token = TestContext.Current.CancellationToken;
        var projectId = await SeedAsync();
        var auditor = await Users.CreateAsync("aud", "Aud", UserRole.Reviewer, Password, token);
        await Store.AddMembershipAsync(auditor.Id, projectId, ProjectRole.Auditor, token);
        await Users.CreateAsync("str", "Str", UserRole.Reviewer, Password, token);
        var reference = RowsAt(projectId, string.Empty);

        // Act
        SignInAs("aud", "Aud", false);
        var asAuditor = RenderAt(projectId);
        var auditorRows = RowIds(asAuditor);
        var auditorWaiting = asAuditor.Find("[data-testid='review-waiting-REV-B']").TextContent;
        var auditorOverdue = asAuditor.Find("[data-testid='project-summary-overdue']").TextContent;
        var auditorNewReview = asAuditor.FindAll("[data-testid='new-review-button']").Count;
        var auditorPlanHint = asAuditor.FindAll("[data-testid='plan-reviews-unavailable']").Count;
        SignInAs("str", "Str", false);
        var asStranger = RenderAt(projectId);

        // Assert
        Assert.Equal(reference, auditorRows);
        Assert.Equal(0, auditorNewReview + auditorPlanHint);
        Assert.Equal("Ann, Bob", auditorWaiting);
        Assert.Equal("1 overdue", auditorOverdue);
        Assert.NotEmpty(asStranger.FindAll("[data-testid='project-not-found']"));
        Assert.Empty(asStranger.FindAll("[data-testid='reviews-list']"));
        Assert.Empty(asStranger.FindAll("[data-testid='project-summary']"));
        Assert.DoesNotContain("Alpha design", asStranger.Markup, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a project without reviews invites a contributor to add the first one, and that a read-only
    ///     member, who does not see the New review button, gets a neutral message without the invitation.
    /// </summary>
    [Fact]
    public async Task ProjectPage_Render_EmptyProject_InvitesOnlyContributors()
    {
        // Arrange: a project without reviews, a contributor and an auditor (who may only read)
        var token = TestContext.Current.CancellationToken;
        var project = await Projects.CreateAsync("Apollo", null, token);
        var contributor = await Users.CreateAsync("ann", "Ann", UserRole.Reviewer, Password, token);
        await Store.AddMembershipAsync(contributor.Id, project.Id, token);
        var auditor = await Users.CreateAsync("aud", "Aud", UserRole.Reviewer, Password, token);
        await Store.AddMembershipAsync(auditor.Id, project.Id, ProjectRole.Auditor, token);

        // Act
        SignInAs("ann", "Ann", false);
        var asContributor = RenderAt(project.Id);
        var contributorButtons = asContributor.FindAll("[data-testid='new-review-button']").Count;
        SignInAs("aud", "Aud", false);
        var asAuditor = RenderAt(project.Id);

        // Assert
        Assert.Equal(1, contributorButtons);
        Assert.Contains("New review", asContributor.Find("[data-testid='reviews-empty']").TextContent, StringComparison.Ordinal);
        Assert.Empty(asAuditor.FindAll("[data-testid='new-review-button']"));
        Assert.Equal("This project has no reviews yet.", asAuditor.Find("[data-testid='reviews-empty']").TextContent.Trim());
        Assert.DoesNotContain("New review", asAuditor.Markup, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that the filter form keeps a descending order of the default column (the review identifier) as hidden
    ///     inputs, so applying a filter does not lose it, and that the default order adds no hidden input.
    /// </summary>
    [Fact]
    public async Task ProjectPage_Render_IdDescending_FilterFormKeepsTheSortState()
    {
        // Arrange
        var projectId = await SeedAsync();

        // Act
        var descending = RenderAt(projectId, "?sort=id&dir=desc");
        var rows = RowIds(descending);
        var sort = descending.FindAll("[data-testid='review-filter'] input[type='hidden'][name='sort']");
        var direction = descending.FindAll("[data-testid='review-filter'] input[type='hidden'][name='dir']");
        var defaultOrder = RenderAt(projectId);

        // Assert: the order is descending and the form carries it
        Assert.Equal(["REV-D", "REV-C", "REV-B", "REV-A"], rows);
        Assert.Equal("id", Assert.Single(sort).GetAttribute("value"));
        Assert.Equal("desc", Assert.Single(direction).GetAttribute("value"));

        // Assert: id ascending, the default, adds nothing
        Assert.Empty(defaultOrder.FindAll("[data-testid='review-filter'] input[type='hidden']"));
    }
}
