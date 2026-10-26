using System.Globalization;
using Bunit;
using DemaConsulting.ReviewCenter.Core;
using DemaConsulting.ReviewCenter.Web;
using Microsoft.AspNetCore.Components;
using TestContext = Xunit.TestContext;

namespace DemaConsulting.ReviewCenter.Tests.Web;

/// <summary>
///     Unit tests for the <see cref="PlanPage"/> component, rendered in memory with bUnit over a scripted git
///     provider and a scripted review-set parser: the form, the one-sentence summary, the groups of rows, the
///     creation with a result per review, and every plain-English error state.
/// </summary>
public class PlanPageTests : PageTestBase
{
    /// <summary>
    ///     The location of the registered repository, which must never reach the page.
    /// </summary>
    private const string Location = "https://secret-host.example.com/org/private-repo.git";

    /// <summary>
    ///     The commit of the revision <c>v1</c>.
    /// </summary>
    private static readonly string H1 = new('b', 40);

    /// <summary>
    ///     The commit of the revision <c>main</c>.
    /// </summary>
    private static readonly string H2 = new('c', 40);

    /// <summary>
    ///     The administrator who registers repositories.
    /// </summary>
    private static readonly Actor _admin = new("adm", "Adm");

    /// <summary>
    ///     Creates the administrator, an enabled repository and a project, scripts the revisions <c>main</c> and
    ///     <c>v1</c>, and scripts the review-sets <c>core</c> (<c>src/**</c>, two files), <c>docs</c>
    ///     (<c>docs/**</c>, one file) and <c>empty</c> (<c>nothing/**</c>, no file).
    /// </summary>
    /// <param name="withConfiguration">Whether the head has a <c>.reviewmark.yaml</c>.</param>
    /// <returns>The project identity and the repository identity.</returns>
    private async Task<(int ProjectId, int RepositoryId)> ArrangeAsync(bool withConfiguration = true)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await Users.CreateAsync("adm", "Adm", UserRole.Admin, "correct horse battery", cancellationToken);
        var repository = await Repositories.CreateAsync("main-repo", Location, "svc", "hidden-token-42", _admin, cancellationToken);
        Git.Revisions["main"] = H2;
        Git.Revisions["v1"] = H1;
        List<(string Path, ChangeKind Kind, string? Baseline, string? Head)> head =
        [
            ("docs/a.md", ChangeKind.Added, null, "doc\n"),
            ("src/a.cs", ChangeKind.Added, null, "three\n"),
            ("src/b.cs", ChangeKind.Added, null, "new\n")
        ];
        if (withConfiguration)
        {
            head.Insert(0, (".reviewmark.yaml", ChangeKind.Added, null, "reviews: []\n"));
        }

        Git.Script(null, H2, head);
        Git.Script(
            H1,
            H2,
            [
                ("src/a.cs", ChangeKind.Modified, "two\n", "three\n"),
                ("src/b.cs", ChangeKind.Added, null, "new\n")
            ]);
        SetParser.Sets =
        [
            new("core", "Core", ["src/**"]),
            new("docs", "Documentation", ["docs/**"]),
            new("empty", "Empty", ["nothing/**"])
        ];
        var project = await Projects.CreateAsync("Apollo", null, cancellationToken);
        return (project.Id, repository.Id);
    }

    /// <summary>
    ///     Renders the page for a project.
    /// </summary>
    /// <param name="projectId">The project identity.</param>
    /// <returns>The rendered component.</returns>
    private IRenderedComponent<PlanPage> RenderPage(int projectId)
    {
        return Render<PlanPage>(parameters => parameters.Add(page => page.ProjectId, projectId));
    }

    /// <summary>
    ///     Types the head revision and presses Show plan.
    /// </summary>
    /// <param name="cut">The rendered page.</param>
    /// <param name="head">The head revision.</param>
    private static async Task ShowPlanAsync(IRenderedComponent<PlanPage> cut, string head = "main")
    {
        await cut.Find("[data-testid='plan-head-input']").InputAsync(head);
        await cut.Find("[data-testid='plan-form']").SubmitAsync();
    }

    /// <summary>
    ///     Gets the text of an element found by test id.
    /// </summary>
    /// <param name="cut">The rendered page.</param>
    /// <param name="testId">The test id.</param>
    /// <returns>The trimmed text content.</returns>
    private static string TextOf(IRenderedComponent<PlanPage> cut, string testId)
    {
        return cut.Find($"[data-testid='{testId}']").TextContent.Trim();
    }

    /// <summary>
    ///     Tells whether an element with a test id is on the page.
    /// </summary>
    /// <param name="cut">The rendered page.</param>
    /// <param name="testId">The test id.</param>
    /// <returns><c>true</c> when there is such an element.</returns>
    private static bool Has(IRenderedComponent<PlanPage> cut, string testId)
    {
        return cut.FindAll($"[data-testid='{testId}']").Count > 0;
    }

    /// <summary>
    ///     Test that the page shows the form with the repository preselected when there is only one, the
    ///     last-signed-review baseline chosen, and no baseline text box.
    /// </summary>
    [Fact]
    public async Task PlanPage_Render_OneRepository_ShowsFormWithDefaults()
    {
        // Arrange: a project and one enabled repository
        var (projectId, repositoryId) = await ArrangeAsync();

        // Act: render the page
        var cut = RenderPage(projectId);

        // Assert: heading, the preselected repository, an empty head, the default baseline mode and no results
        Assert.Equal("Plan reviews", TextOf(cut, "plan-heading"));
        Assert.Equal("Apollo", TextOf(cut, "plan-breadcrumb-project"));
        Assert.Equal(
            ["Choose a repository", "main-repo"],
            cut.FindAll("[data-testid='plan-repository-select'] option").Select(option => option.TextContent));
        Assert.Equal(
            repositoryId.ToString(CultureInfo.InvariantCulture),
            cut.Find("[data-testid='plan-repository-select']").GetAttribute("value"));
        Assert.Equal(string.Empty, cut.Find("[data-testid='plan-head-input']").GetAttribute("value") ?? string.Empty);
        Assert.True(cut.Find("[data-testid='plan-baseline-last']").HasAttribute("checked"));
        Assert.False(cut.Find("[data-testid='plan-baseline-none']").HasAttribute("checked"));
        Assert.False(Has(cut, "plan-baseline-input"));
        Assert.Equal("Show plan", TextOf(cut, "plan-load-button"));
        Assert.False(Has(cut, "plan-summary"));
        Assert.False(Has(cut, "plan-error"));
    }

    /// <summary>
    ///     Test that the root element reports interactivity once the component has rendered on a live circuit.
    /// </summary>
    [Fact]
    public async Task PlanPage_AfterRender_Interactive_SetsMarker()
    {
        // Arrange: a project
        var (projectId, _) = await ArrangeAsync();

        // Act: render the page
        var cut = RenderPage(projectId);

        // Assert: the marker is set
        Assert.Equal("true", cut.Find("[data-testid='plan-page']").GetAttribute("data-interactive"));
    }

    /// <summary>
    ///     Test that an unknown project shows a not-found message and no form.
    /// </summary>
    [Fact]
    public void PlanPage_Render_UnknownProject_ShowsNotFound()
    {
        // Arrange: no projects exist

        // Act: render the page for project 99
        var cut = RenderPage(99);

        // Assert: not-found names the project, and there is no form
        Assert.Contains("99", TextOf(cut, "plan-project-not-found"), StringComparison.Ordinal);
        Assert.False(Has(cut, "plan-form"));
    }

    /// <summary>
    ///     Test that without a registered repository the page says so in plain words and shows no form.
    /// </summary>
    [Fact]
    public async Task PlanPage_Render_NoRepository_ShowsPlainMessageInsteadOfForm()
    {
        // Arrange: a project and no repository
        var project = await Projects.CreateAsync("Apollo", null, TestContext.Current.CancellationToken);

        // Act: render the page
        var cut = RenderPage(project.Id);

        // Assert: the message tells what to do, and the form is not there
        Assert.Equal("No repository is registered; ask an administrator.", TextOf(cut, "plan-no-repository"));
        Assert.False(Has(cut, "plan-form"));
    }

    /// <summary>
    ///     Test that several repositories are offered by name with nothing preselected, and that no location, user
    ///     name or token appears in the page.
    /// </summary>
    [Fact]
    public async Task PlanPage_Render_SeveralRepositories_OffersEnabledOnesWithoutSecrets()
    {
        // Arrange: two enabled repositories and a disabled one
        var (projectId, _) = await ArrangeAsync();
        await Repositories.CreateAsync(
            "second-repo", "https://example.com/two.git", null, null, _admin, TestContext.Current.CancellationToken);
        var off = await Repositories.CreateAsync(
            "old-repo", "https://example.com/old.git", null, null, _admin, TestContext.Current.CancellationToken);
        await Repositories.SetEnabledAsync(off.Id, false, _admin, TestContext.Current.CancellationToken);

        // Act: render the page
        var cut = RenderPage(projectId);

        // Assert: both enabled names and the prompt; nothing preselected; no secrets
        Assert.Equal(
            ["Choose a repository", "main-repo", "second-repo"],
            cut.FindAll("[data-testid='plan-repository-select'] option").Select(option => option.TextContent));
        Assert.Equal(string.Empty, cut.Find("[data-testid='plan-repository-select']").GetAttribute("value") ?? string.Empty);
        Assert.DoesNotContain("old-repo", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-host", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("hidden-token-42", cut.Markup, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that the baseline text box appears only for the explicit mode.
    /// </summary>
    [Fact]
    public async Task PlanPage_BaselineMode_ExplicitOnly_ShowsBaselineInput()
    {
        // Arrange: the page with its default mode
        var (projectId, _) = await ArrangeAsync();
        var cut = RenderPage(projectId);
        Assert.False(Has(cut, "plan-baseline-input"));

        // Act: choose the explicit mode
        await cut.Find("[data-testid='plan-baseline-explicit']").ChangeAsync(true);

        // Assert: the text box is there and the radio is chosen
        Assert.True(Has(cut, "plan-baseline-input"));
        Assert.True(cut.Find("[data-testid='plan-baseline-explicit']").HasAttribute("checked"));

        // Act: choose all files
        await cut.Find("[data-testid='plan-baseline-none']").ChangeAsync(true);

        // Assert: the text box is gone again
        Assert.False(Has(cut, "plan-baseline-input"));
        Assert.True(cut.Find("[data-testid='plan-baseline-none']").HasAttribute("checked"));
    }

    /// <summary>
    ///     Test that a form that is not filled in says what is missing and reads nothing from git.
    /// </summary>
    [Fact]
    public async Task PlanPage_ShowPlan_IncompleteForm_SaysWhatIsMissing()
    {
        // Arrange: two repositories, so none is preselected
        var (projectId, repositoryId) = await ArrangeAsync();
        await Repositories.CreateAsync(
            "second-repo", "https://example.com/two.git", null, null, _admin, TestContext.Current.CancellationToken);
        var cut = RenderPage(projectId);

        // Act: submit with no repository
        await cut.Find("[data-testid='plan-form']").SubmitAsync();

        // Assert: it asks for a repository
        Assert.Equal("Choose a repository.", TextOf(cut, "plan-error-message"));
        Assert.Equal("Input", cut.Find("[data-testid='plan-error']").GetAttribute("data-error-kind"));

        // Act: choose the repository and submit with no head
        await cut.Find("[data-testid='plan-repository-select']").ChangeAsync(repositoryId.ToString(CultureInfo.InvariantCulture));
        await cut.Find("[data-testid='plan-form']").SubmitAsync();

        // Assert: it asks for the head revision
        Assert.StartsWith("Enter the head revision", TextOf(cut, "plan-error-message"), StringComparison.Ordinal);

        // Act: type a head, choose the explicit mode and submit with no baseline
        await cut.Find("[data-testid='plan-head-input']").InputAsync("main");
        await cut.Find("[data-testid='plan-baseline-explicit']").ChangeAsync(true);
        await cut.Find("[data-testid='plan-form']").SubmitAsync();

        // Assert: it asks for the baseline, and git was never opened
        Assert.StartsWith("Enter the baseline revision", TextOf(cut, "plan-error-message"), StringComparison.Ordinal);
        Assert.Empty(Git.Opened);
    }

    /// <summary>
    ///     Test that the plan shows one summary sentence, then only the sets that need a review ticked by default,
    ///     with the empty sets collapsed and counted, and no group that has no rows.
    /// </summary>
    [Fact]
    public async Task PlanPage_ShowPlan_SetsNeedingReview_ShowsSummaryTickedRowsAndCollapsedEmpties()
    {
        // Arrange: two sets with files and one without, no earlier review
        var (projectId, _) = await ArrangeAsync();
        var cut = RenderPage(projectId);

        // Act: show the plan for main
        await ShowPlanAsync(cut);

        // Assert: the summary sentence and the facts line
        Assert.Equal("2 sets need review, 1 has nothing to review.", TextOf(cut, "plan-summary"));
        Assert.Equal("status", cut.Find("[data-testid='plan-summary']").GetAttribute("role"));
        Assert.Equal(
            "Repository main-repo, head main (ccccccc). Baseline: changes since the last signed review of each set.",
            TextOf(cut, "plan-facts"));
        Assert.StartsWith("Clear the tick of the sets you do not want", TextOf(cut, "plan-next-step"), StringComparison.Ordinal);

        // Assert: the actionable rows come first, are ticked and say what they will review
        Assert.Equal("Core", TextOf(cut, "plan-row-title-core"));
        Assert.Equal("Full review: no signed review of this set yet", TextOf(cut, "plan-row-note-core"));
        Assert.Equal("2 files", TextOf(cut, "plan-row-counts-core"));
        Assert.Equal("1 file", TextOf(cut, "plan-row-counts-docs"));
        Assert.True(cut.Find("[data-testid='plan-row-check-core']").HasAttribute("checked"));
        Assert.True(cut.Find("[data-testid='plan-row-check-docs']").HasAttribute("checked"));
        Assert.Equal("Create 2 reviews", TextOf(cut, "plan-create-button"));
        Assert.False(cut.Find("[data-testid='plan-create-button']").HasAttribute("disabled"));

        // Assert: the empty set is in a collapsed list with a count, and unused groups are not on the page
        Assert.Equal("Nothing to review (1)", TextOf(cut, "plan-empty-toggle"));
        Assert.False(cut.Find("[data-testid='plan-empty-list']").HasAttribute("open"));
        Assert.Contains("Empty", TextOf(cut, "plan-empty-empty"), StringComparison.Ordinal);
        Assert.False(Has(cut, "plan-problem-list"));
        Assert.False(Has(cut, "plan-filed-list"));
        Assert.False(Has(cut, "plan-row-empty"));
        Assert.False(Has(cut, "plan-select-all"));
        Assert.False(Has(cut, "plan-error"));
    }

    /// <summary>
    ///     Test that an explicit baseline is used for every set and the counts say what kind of change it is.
    /// </summary>
    [Fact]
    public async Task PlanPage_ShowPlan_ExplicitBaseline_CountsChangesSinceTheBaseline()
    {
        // Arrange: the page with the explicit mode and the baseline v1
        var (projectId, _) = await ArrangeAsync();
        var cut = RenderPage(projectId);
        await cut.Find("[data-testid='plan-baseline-explicit']").ChangeAsync(true);
        await cut.Find("[data-testid='plan-baseline-input']").InputAsync("v1");

        // Act: show the plan
        await ShowPlanAsync(cut);

        // Assert: only core changed since v1, with one modified and one added file
        Assert.Equal("1 set needs review, 2 have nothing to review.", TextOf(cut, "plan-summary"));
        Assert.Equal("2 changed files (1 added, 1 modified)", TextOf(cut, "plan-row-counts-core"));
        Assert.Equal("Changes since v1 (bbbbbbb)", TextOf(cut, "plan-row-note-core"));
        Assert.Contains("changes since v1 (bbbbbbb)", TextOf(cut, "plan-facts"), StringComparison.Ordinal);
        Assert.Equal("Create 1 review", TextOf(cut, "plan-create-button"));
        Assert.Equal("Nothing to review (2)", TextOf(cut, "plan-empty-toggle"));
    }

    /// <summary>
    ///     Test that the no-baseline mode says so in the facts line and lists all files.
    /// </summary>
    [Fact]
    public async Task PlanPage_ShowPlan_NoBaseline_SaysAllFiles()
    {
        // Arrange: the page with the all-files mode
        var (projectId, _) = await ArrangeAsync();
        var cut = RenderPage(projectId);
        await cut.Find("[data-testid='plan-baseline-none']").ChangeAsync(true);

        // Act: show the plan
        await ShowPlanAsync(cut);

        // Assert: the facts say all files
        Assert.Contains("Baseline: all files (no baseline).", TextOf(cut, "plan-facts"), StringComparison.Ordinal);
        Assert.Equal("2 sets need review, 1 has nothing to review.", TextOf(cut, "plan-summary"));
    }

    /// <summary>
    ///     Test that clearing the ticks of rows updates the create button, and that with none ticked it is disabled with a hint.
    /// </summary>
    [Fact]
    public async Task PlanPage_Ticks_Clear_UpdatesCreateButtonAndDisablesAtZero()
    {
        // Arrange: a plan with two ticked rows
        var (projectId, _) = await ArrangeAsync();
        var cut = RenderPage(projectId);
        await ShowPlanAsync(cut);

        // Act: clear one tick
        await cut.Find("[data-testid='plan-row-check-docs']").ChangeAsync(false);

        // Assert: the count follows
        Assert.Equal("Create 1 review", TextOf(cut, "plan-create-button"));

        // Act: clear the other tick
        await cut.Find("[data-testid='plan-row-check-core']").ChangeAsync(false);

        // Assert: nothing to create, the button is disabled and a hint says why
        Assert.Equal("Create 0 reviews", TextOf(cut, "plan-create-button"));
        Assert.True(cut.Find("[data-testid='plan-create-button']").HasAttribute("disabled"));
        Assert.True(Has(cut, "plan-create-hint"));

        // Act: tick one again
        await cut.Find("[data-testid='plan-row-check-core']").ChangeAsync(true);

        // Assert: enabled again, and the plan was not read again for any tick
        Assert.Equal("Create 1 review", TextOf(cut, "plan-create-button"));
        Assert.False(cut.Find("[data-testid='plan-create-button']").HasAttribute("disabled"));
        Assert.Single(Git.Opened);
    }

    /// <summary>
    ///     Test that select all and select none are offered only for more than three rows and work.
    /// </summary>
    [Fact]
    public async Task PlanPage_Ticks_ManyRows_OffersSelectAllAndNone()
    {
        // Arrange: four sets that need a review
        var (projectId, _) = await ArrangeAsync();
        SetParser.Sets = [.. Enumerable.Range(1, 4).Select(n => new ReviewSetDefinition($"s{n}", $"Set {n}", ["src/**"]))];
        var cut = RenderPage(projectId);

        // Act: show the plan
        await ShowPlanAsync(cut);

        // Assert: the bulk buttons are there and all four are ticked
        Assert.Equal("Create 4 reviews", TextOf(cut, "plan-create-button"));
        Assert.True(Has(cut, "plan-select-all"));

        // Act: select none, then all
        await cut.Find("[data-testid='plan-select-none']").ClickAsync();
        Assert.Equal("Create 0 reviews", TextOf(cut, "plan-create-button"));
        await cut.Find("[data-testid='plan-select-all']").ClickAsync();

        // Assert: all four are ticked again
        Assert.Equal("Create 4 reviews", TextOf(cut, "plan-create-button"));
        Assert.All(
            Enumerable.Range(1, 4),
            n => Assert.True(cut.Find($"[data-testid='plan-row-check-s{n}']").HasAttribute("checked")));
    }

    /// <summary>
    ///     Test that creating shows one result per review with a link to each created review, and that the
    ///     reviews belong to their review-sets.
    /// </summary>
    [Fact]
    public async Task PlanPage_Create_TickedRows_ShowsResultWithLinksAndCreatesReviews()
    {
        // Arrange: a plan with both rows ticked
        var (projectId, _) = await ArrangeAsync();
        var cut = RenderPage(projectId);
        await ShowPlanAsync(cut);

        // Act: create
        await cut.Find("[data-testid='plan-create-button']").ClickAsync();

        // Assert: the summary, a link per review, and the plan is replaced by the results
        Assert.Equal("2 reviews created.", TextOf(cut, "plan-result-summary"));
        Assert.Equal("Created", TextOf(cut, "plan-result-status-core"));
        var link = cut.Find("[data-testid='plan-result-link-core']");
        Assert.Equal($"/projects/{projectId}/reviews/core", link.GetAttribute("href"));
        Assert.Equal("Open review core", link.TextContent);
        Assert.Equal($"/projects/{projectId}/reviews/docs", cut.Find("[data-testid='plan-result-link-docs']").GetAttribute("href"));
        Assert.Equal($"/projects/{projectId}", cut.Find("[data-testid='plan-back-link']").GetAttribute("href"));
        Assert.False(Has(cut, "plan-summary"));
        Assert.False(Has(cut, "plan-form"));

        // Assert: the reviews exist, from the commit of the plan, tied to their sets
        var reviews = await Reviews.ListAsync(projectId, TestContext.Current.CancellationToken);
        Assert.Equal(["core", "docs"], reviews.Select(review => review.Id).Order());
        var core = reviews.Single(review => review.Id == "core");
        Assert.Equal("core", core.ReviewSetId);
        Assert.Equal(H2, core.HeadSha);
    }

    /// <summary>
    ///     Test that only the ticked rows are created.
    /// </summary>
    [Fact]
    public async Task PlanPage_Create_OneRowNotTicked_CreatesOnlyTheTickedRow()
    {
        // Arrange: a plan with the docs row not ticked
        var (projectId, _) = await ArrangeAsync();
        var cut = RenderPage(projectId);
        await ShowPlanAsync(cut);
        await cut.Find("[data-testid='plan-row-check-docs']").ChangeAsync(false);

        // Act: create
        await cut.Find("[data-testid='plan-create-button']").ClickAsync();

        // Assert: one review, and the result says so
        Assert.Equal("1 review created.", TextOf(cut, "plan-result-summary"));
        Assert.Equal(["core"], (await Reviews.ListAsync(projectId, TestContext.Current.CancellationToken)).Select(review => review.Id));
        Assert.False(Has(cut, "plan-result-docs"));
    }

    /// <summary>
    ///     Test that a row that fails is shown with a safe reason and the other rows are still created, and that
    ///     planning again offers the failed row once more.
    /// </summary>
    [Fact]
    public async Task PlanPage_Create_OneRowFails_ShowsReasonAndCreatesTheOthers()
    {
        // Arrange: a plan with both rows ticked, then the first listing during creation fails with a raw fault
        var (projectId, _) = await ArrangeAsync();
        var cut = RenderPage(projectId);
        await ShowPlanAsync(cut);
        Git.OnNextListChanges = () => throw new InvalidOperationException("SECRET-DB-DETAIL at C:\\private\\db.sqlite");

        // Act: create
        await cut.Find("[data-testid='plan-create-button']").ClickAsync();

        // Assert: the summary counts both, the failed row has a reason without the raw text, the other has a link
        Assert.Equal("1 review created, 1 failed.", TextOf(cut, "plan-result-summary"));
        Assert.Equal("Failed", TextOf(cut, "plan-result-status-core"));
        var reason = cut.Find("[data-testid='plan-result-message-core']");
        Assert.Equal("alert", reason.GetAttribute("role"));
        Assert.Contains("unexpected error", reason.TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain("SECRET", cut.Markup, StringComparison.Ordinal);
        Assert.False(Has(cut, "plan-result-link-core"));
        Assert.Equal("Created", TextOf(cut, "plan-result-status-docs"));
        Assert.Contains("Plan again", TextOf(cut, "plan-result-next-step"), StringComparison.Ordinal);
        Assert.Equal(["docs"], (await Reviews.ListAsync(projectId, TestContext.Current.CancellationToken)).Select(review => review.Id));

        // Act: plan again
        await cut.Find("[data-testid='plan-again-button']").ClickAsync();

        // Assert: the created set is filed and the failed one is offered again; the filed list stays collapsed
        Assert.Equal("1 set needs review, 1 already filed, 1 has nothing to review.", TextOf(cut, "plan-summary"));
        Assert.True(Has(cut, "plan-row-core"));
        Assert.False(cut.Find("[data-testid='plan-filed-list']").HasAttribute("open"));
        Assert.Equal($"/projects/{projectId}/reviews/docs", cut.Find("[data-testid='plan-filed-link-docs']").GetAttribute("href"));
    }

    /// <summary>
    ///     Test that a git access failure of one row shows only the fixed sentence, with no git text, location or
    ///     credential, while the other row is created.
    /// </summary>
    [Fact]
    public async Task PlanPage_Create_OneRowGitAccessFailed_ShowsFixedSentenceWithoutGitText()
    {
        // Arrange: a plan with both rows ticked, then the first listing during creation fails with raw git text
        var (projectId, _) = await ArrangeAsync();
        var cut = RenderPage(projectId);
        await ShowPlanAsync(cut);
        Git.OnNextListChanges = () => throw new DomainRuleException(
            DomainRule.GitAccessFailed,
            $"Repository 'main-repo' could not be read: fatal: unable to access '{Location}': token hidden-token-42 refused");

        // Act: create
        await cut.Find("[data-testid='plan-create-button']").ClickAsync();

        // Assert: the failed row shows the fixed sentence only and the other row is created
        Assert.Equal("1 review created, 1 failed.", TextOf(cut, "plan-result-summary"));
        Assert.Equal("Failed", TextOf(cut, "plan-result-status-core"));
        Assert.Equal(ReviewPlanner.GitAccessFailedMessage, TextOf(cut, "plan-result-message-core"));
        Assert.DoesNotContain("fatal", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-host", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("hidden-token-42", cut.Markup, StringComparison.Ordinal);
        Assert.Equal("Created", TextOf(cut, "plan-result-status-docs"));
    }

    /// <summary>
    ///     Test that when every set is already filed the page says nothing is left to create and opens the filed
    ///     list, with a link to each review.
    /// </summary>
    [Fact]
    public async Task PlanPage_ShowPlan_EverythingFiled_OpensFiledListWithLinksAndNoCreateButton()
    {
        // Arrange: both reviews created through the page
        var (projectId, _) = await ArrangeAsync();
        var cut = RenderPage(projectId);
        await ShowPlanAsync(cut);
        await cut.Find("[data-testid='plan-create-button']").ClickAsync();

        // Act: plan again
        await cut.Find("[data-testid='plan-again-button']").ClickAsync();

        // Assert: nothing to create, the filed list is open with its links, and there is no create button
        Assert.Equal("No set needs review, 2 already filed, 1 has nothing to review.", TextOf(cut, "plan-summary"));
        Assert.StartsWith("Nothing to create here", TextOf(cut, "plan-next-step"), StringComparison.Ordinal);
        Assert.Equal("Already filed (2)", TextOf(cut, "plan-filed-toggle"));
        Assert.True(cut.Find("[data-testid='plan-filed-list']").HasAttribute("open"));
        Assert.Equal($"/projects/{projectId}/reviews/core", cut.Find("[data-testid='plan-filed-link-core']").GetAttribute("href"));
        Assert.False(Has(cut, "plan-create-button"));
        Assert.False(Has(cut, "plan-todo-list"));
    }

    /// <summary>
    ///     Test that a set that cannot be planned is shown, not hidden, with its reason and no checkbox.
    /// </summary>
    [Fact]
    public async Task PlanPage_ShowPlan_SetCannotBePlanned_ShowsReasonInOwnGroup()
    {
        // Arrange: a set whose path is not valid
        var (projectId, _) = await ArrangeAsync();
        SetParser.Sets.Add(new("bad", "Bad set", ["../x"]));
        var cut = RenderPage(projectId);

        // Act: show the plan
        await ShowPlanAsync(cut);

        // Assert: the summary counts it, the group is visible with the reason, and it has no checkbox
        Assert.Equal("2 sets need review, 1 has nothing to review, 1 cannot be planned.", TextOf(cut, "plan-summary"));
        Assert.True(Has(cut, "plan-problem-list"));
        Assert.False(string.IsNullOrWhiteSpace(TextOf(cut, "plan-problem-reason-bad")));
        Assert.False(Has(cut, "plan-row-check-bad"));
        Assert.Equal("Create 2 reviews", TextOf(cut, "plan-create-button"));
    }

    /// <summary>
    ///     Test that changing the form discards the plan so the screen never disagrees with the form.
    /// </summary>
    [Fact]
    public async Task PlanPage_FormChange_DiscardsPlan()
    {
        // Arrange: a shown plan
        var (projectId, _) = await ArrangeAsync();
        var cut = RenderPage(projectId);
        await ShowPlanAsync(cut);
        Assert.True(Has(cut, "plan-summary"));

        // Act: change the head revision
        await cut.Find("[data-testid='plan-head-input']").InputAsync("v1");

        // Assert: the plan is gone
        Assert.False(Has(cut, "plan-summary"));
        Assert.False(Has(cut, "plan-create-button"));

        // Arrange: type the head again, show it again, then change the baseline mode
        await cut.Find("[data-testid='plan-head-input']").InputAsync("main");
        await cut.Find("[data-testid='plan-form']").SubmitAsync();
        Assert.True(Has(cut, "plan-summary"));

        // Act: choose another mode
        await cut.Find("[data-testid='plan-baseline-none']").ChangeAsync(true);

        // Assert: the plan is gone again
        Assert.False(Has(cut, "plan-summary"));
    }

    /// <summary>
    ///     Test that navigating to another project clears the form and the plan of the previous one.
    /// </summary>
    [Fact]
    public async Task PlanPage_ParametersChange_OtherProject_ClearsFormAndPlan()
    {
        // Arrange: a shown plan for the first project, and a second project
        var (projectId, _) = await ArrangeAsync();
        var second = await Projects.CreateAsync("Gemini", null, TestContext.Current.CancellationToken);
        var cut = RenderPage(projectId);
        await ShowPlanAsync(cut);

        // Act: the router reuses the component for the other project
        await cut.InvokeAsync(() => cut.Instance.SetParametersAsync(
            ParameterView.FromDictionary(new Dictionary<string, object?> { [nameof(PlanPage.ProjectId)] = second.Id })));

        // Assert: the new project is shown with an empty head and no plan
        await cut.WaitForAssertionAsync(() => Assert.Equal("Gemini", TextOf(cut, "plan-breadcrumb-project")));
        Assert.False(Has(cut, "plan-summary"));
        Assert.Equal(string.Empty, cut.Find("[data-testid='plan-head-input']").GetAttribute("value") ?? string.Empty);
    }

    /// <summary>
    ///     Test that a plan still being read for a project is ignored when the page moves to another project: it
    ///     neither shows its plan nor leaves the new project's form busy.
    /// </summary>
    [Fact]
    public async Task PlanPage_ParametersChange_WhilePlanning_IgnoresStalePlanAndFreesForm()
    {
        // Arrange: a listing that waits, a plan request in flight, and a second project
        var (projectId, _) = await ArrangeAsync();
        var second = await Projects.CreateAsync("Gemini", null, TestContext.Current.CancellationToken);
        var gate = new TaskCompletionSource();
        Git.OnNextListChanges = () => gate.Task;
        var cut = RenderPage(projectId);
        await cut.Find("[data-testid='plan-head-input']").InputAsync("main");
        var submit = cut.Find("[data-testid='plan-form']").SubmitAsync();
        await cut.WaitForAssertionAsync(() => Assert.True(Has(cut, "plan-loading")));

        // Act: the router reuses the component for the other project, then the old plan arrives
        await cut.InvokeAsync(() => cut.Instance.SetParametersAsync(
            ParameterView.FromDictionary(new Dictionary<string, object?> { [nameof(PlanPage.ProjectId)] = second.Id })));
        await cut.WaitForAssertionAsync(() => Assert.Equal("Gemini", TextOf(cut, "plan-breadcrumb-project")));
        gate.SetResult();
        await submit;

        // Assert: the new project shows no plan, no loading note, and a form that can be used
        Assert.False(Has(cut, "plan-summary"));
        Assert.False(Has(cut, "plan-loading"));
        Assert.False(Has(cut, "plan-error"));
        Assert.Equal("Show plan", TextOf(cut, "plan-load-button"));
        Assert.False(cut.Find("[data-testid='plan-load-button']").HasAttribute("disabled"));
        Assert.False(cut.Find("[data-testid='plan-head-input']").HasAttribute("disabled"));
    }

    /// <summary>
    ///     Test that results of a creation still running for a project are ignored when the page moves to another
    ///     project.
    /// </summary>
    [Fact]
    public async Task PlanPage_ParametersChange_WhileCreating_IgnoresStaleResults()
    {
        // Arrange: a shown plan, a creation that waits, and a second project
        var (projectId, _) = await ArrangeAsync();
        var second = await Projects.CreateAsync("Gemini", null, TestContext.Current.CancellationToken);
        var cut = RenderPage(projectId);
        await ShowPlanAsync(cut);
        var gate = new TaskCompletionSource();
        Git.OnNextListChanges = () => gate.Task;
        var create = cut.Find("[data-testid='plan-create-button']").ClickAsync();
        await cut.WaitForAssertionAsync(() => Assert.Equal("Creating...", TextOf(cut, "plan-create-button")));

        // Act: the router reuses the component for the other project, then the old creation finishes
        await cut.InvokeAsync(() => cut.Instance.SetParametersAsync(
            ParameterView.FromDictionary(new Dictionary<string, object?> { [nameof(PlanPage.ProjectId)] = second.Id })));
        await cut.WaitForAssertionAsync(() => Assert.Equal("Gemini", TextOf(cut, "plan-breadcrumb-project")));
        gate.SetResult();
        await create;

        // Assert: the new project shows no results and no plan, and its form can be used
        Assert.False(Has(cut, "plan-result-summary"));
        Assert.False(Has(cut, "plan-summary"));
        Assert.False(cut.Find("[data-testid='plan-load-button']").HasAttribute("disabled"));
    }

    /// <summary>
    ///     Test that the button shows that the repository is being read, and is disabled, while the plan is made.
    /// </summary>
    [Fact]
    public async Task PlanPage_ShowPlan_WhileReading_ShowsLoadingAndDisablesButton()
    {
        // Arrange: a listing that waits until the test lets it go
        var (projectId, _) = await ArrangeAsync();
        var gate = new TaskCompletionSource();
        Git.OnNextListChanges = () => gate.Task;
        var cut = RenderPage(projectId);
        await cut.Find("[data-testid='plan-head-input']").InputAsync("main");

        // Act: submit and look at the page while the listing waits
        var submit = cut.Find("[data-testid='plan-form']").SubmitAsync();
        await cut.WaitForAssertionAsync(() => Assert.True(Has(cut, "plan-loading")));

        // Assert: the button says what happens and cannot be pressed again
        Assert.Equal("Reading repository...", TextOf(cut, "plan-load-button"));
        Assert.True(cut.Find("[data-testid='plan-load-button']").HasAttribute("disabled"));

        // Act: let the listing finish
        gate.SetResult();
        await submit;

        // Assert: the plan is shown and the loading note is gone
        Assert.True(Has(cut, "plan-summary"));
        Assert.False(Has(cut, "plan-loading"));
    }

    /// <summary>
    ///     Test that every form control is disabled while the plan is read, so the inputs cannot change under it, and
    ///     enabled again when the plan is shown.
    /// </summary>
    [Fact]
    public async Task PlanPage_ShowPlan_WhileReading_DisablesEveryFormControl()
    {
        // Arrange: the explicit mode with a baseline, and a listing that waits until the test lets it go
        var (projectId, _) = await ArrangeAsync();
        var gate = new TaskCompletionSource();
        Git.OnNextListChanges = () => gate.Task;
        var cut = RenderPage(projectId);
        await cut.Find("[data-testid='plan-baseline-explicit']").ChangeAsync(true);
        await cut.Find("[data-testid='plan-baseline-input']").InputAsync("v1");
        await cut.Find("[data-testid='plan-head-input']").InputAsync("main");
        string[] controls =
        [
            "plan-repository-select",
            "plan-head-input",
            "plan-baseline-none",
            "plan-baseline-explicit",
            "plan-baseline-last",
            "plan-baseline-input"
        ];
        Assert.All(controls, id => Assert.False(cut.Find($"[data-testid='{id}']").HasAttribute("disabled"), id));

        // Act: submit and look at the page while the listing waits
        var submit = cut.Find("[data-testid='plan-form']").SubmitAsync();
        await cut.WaitForAssertionAsync(() => Assert.True(Has(cut, "plan-loading")));

        // Assert: nothing in the form can be changed
        Assert.All(controls, id => Assert.True(cut.Find($"[data-testid='{id}']").HasAttribute("disabled"), id));

        // Act: let the listing finish
        gate.SetResult();
        await submit;

        // Assert: the plan is shown and the form can be used again
        Assert.True(Has(cut, "plan-summary"));
        Assert.All(controls, id => Assert.False(cut.Find($"[data-testid='{id}']").HasAttribute("disabled"), id));
    }

    /// <summary>
    ///     Test that the form and the row controls are disabled while the reviews are created, so neither the form nor
    ///     the ticks can change under the creation.
    /// </summary>
    [Fact]
    public async Task PlanPage_Create_WhileCreating_DisablesFormAndRowControls()
    {
        // Arrange: a plan of four rows, and a creation that waits until the test lets it go
        var (projectId, _) = await ArrangeAsync();
        SetParser.Sets = [.. Enumerable.Range(1, 4).Select(n => new ReviewSetDefinition($"s{n}", $"Set {n}", ["src/**"]))];
        var cut = RenderPage(projectId);
        await ShowPlanAsync(cut);
        string[] controls =
        [
            "plan-repository-select",
            "plan-head-input",
            "plan-baseline-none",
            "plan-baseline-explicit",
            "plan-baseline-last",
            "plan-select-all",
            "plan-select-none",
            "plan-row-check-s1",
            "plan-row-check-s4",
            "plan-load-button"
        ];
        Assert.All(controls, id => Assert.False(cut.Find($"[data-testid='{id}']").HasAttribute("disabled"), id));
        var gate = new TaskCompletionSource();
        Git.OnNextListChanges = () => gate.Task;

        // Act: create and look at the page while the first review waits
        var create = cut.Find("[data-testid='plan-create-button']").ClickAsync();
        await cut.WaitForAssertionAsync(() => Assert.Equal("Creating...", TextOf(cut, "plan-create-button")));

        // Assert: neither the form nor the ticks can be changed
        Assert.All(controls, id => Assert.True(cut.Find($"[data-testid='{id}']").HasAttribute("disabled"), id));

        // Act: let the creation finish
        gate.SetResult();
        await create;

        // Assert: the results are shown and can be left for a new plan
        Assert.Equal("4 reviews created.", TextOf(cut, "plan-result-summary"));
        Assert.False(cut.Find("[data-testid='plan-again-button']").HasAttribute("disabled"));
    }

    /// <summary>
    ///     Test that a revision that does not exist is explained with what to do, and nothing is shown as a plan.
    /// </summary>
    [Fact]
    public async Task PlanPage_Error_UnknownRevision_ExplainsAndSuggestsChecking()
    {
        // Arrange: the page
        var (projectId, _) = await ArrangeAsync();
        var cut = RenderPage(projectId);

        // Act: plan a head that does not exist
        await ShowPlanAsync(cut, "no-such-branch");

        // Assert: the error names the revision, tells what to do, and has the kind for drivers
        var error = cut.Find("[data-testid='plan-error']");
        Assert.Equal("alert", error.GetAttribute("role"));
        Assert.Equal("RevisionNotFound", error.GetAttribute("data-error-kind"));
        Assert.Contains("no-such-branch", TextOf(cut, "plan-error-message"), StringComparison.Ordinal);
        Assert.Contains("Check the spelling", TextOf(cut, "plan-error-hint"), StringComparison.Ordinal);
        Assert.False(Has(cut, "plan-summary"));
        Assert.Equal("Show plan", TextOf(cut, "plan-load-button"));
    }

    /// <summary>
    ///     Test that a head without a <c>.reviewmark.yaml</c> is explained, with a way to create a review by hand.
    /// </summary>
    [Fact]
    public async Task PlanPage_Error_NoConfiguration_ExplainsAndLinksToProject()
    {
        // Arrange: a head with no .reviewmark.yaml
        var (projectId, _) = await ArrangeAsync(withConfiguration: false);
        var cut = RenderPage(projectId);

        // Act: plan it
        await ShowPlanAsync(cut);

        // Assert: the message says what is missing, the hint says what to do, and a link leads to the project page
        Assert.Equal("ReviewSetConfigMissing", cut.Find("[data-testid='plan-error']").GetAttribute("data-error-kind"));
        Assert.Contains(".reviewmark.yaml", TextOf(cut, "plan-error-message"), StringComparison.Ordinal);
        Assert.Contains("no review-sets to plan", TextOf(cut, "plan-error-message"), StringComparison.Ordinal);
        Assert.Contains("by hand", TextOf(cut, "plan-error-hint"), StringComparison.Ordinal);
        Assert.Equal($"/projects/{projectId}", cut.Find("[data-testid='plan-error-project-link']").GetAttribute("href"));
    }

    /// <summary>
    ///     Test that an invalid configuration shows the parser's message and asks to fix the file.
    /// </summary>
    [Fact]
    public async Task PlanPage_Error_InvalidConfiguration_ShowsMessageAndHint()
    {
        // Arrange: a parser that rejects the file
        var (projectId, _) = await ArrangeAsync();
        SetParser.Failure = new DomainRuleException(
            DomainRule.ReviewSetConfigInvalid, "The file .reviewmark.yaml is not valid YAML (line 3, column 5).");
        var cut = RenderPage(projectId);

        // Act: plan
        await ShowPlanAsync(cut);

        // Assert: the message with the position, and what to do
        Assert.Equal("ReviewSetConfigInvalid", cut.Find("[data-testid='plan-error']").GetAttribute("data-error-kind"));
        Assert.Contains("line 3, column 5", TextOf(cut, "plan-error-message"), StringComparison.Ordinal);
        Assert.Contains("Fix the file", TextOf(cut, "plan-error-hint"), StringComparison.Ordinal);
        Assert.False(Has(cut, "plan-error-project-link"));
    }

    /// <summary>
    ///     Test that a repository disabled while the page is open is explained, with what an administrator must do.
    /// </summary>
    [Fact]
    public async Task PlanPage_Error_RepositoryDisabledMeanwhile_ExplainsWithoutSecrets()
    {
        // Arrange: the page with a repository that is then disabled
        var (projectId, repositoryId) = await ArrangeAsync();
        var cut = RenderPage(projectId);
        await Repositories.SetEnabledAsync(repositoryId, false, _admin, TestContext.Current.CancellationToken);

        // Act: plan
        await ShowPlanAsync(cut);

        // Assert: the message names the repository, the hint names the administrator, no secret is shown
        Assert.Equal("RepositoryDisabled", cut.Find("[data-testid='plan-error']").GetAttribute("data-error-kind"));
        Assert.Contains("main-repo", TextOf(cut, "plan-error-message"), StringComparison.Ordinal);
        Assert.Contains("administrator", TextOf(cut, "plan-error-hint"), StringComparison.Ordinal);
        Assert.DoesNotContain("secret-host", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("hidden-token-42", cut.Markup, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a git access failure never shows the text git printed, only a fixed sentence.
    /// </summary>
    [Fact]
    public async Task PlanPage_Error_GitAccessFailed_ShowsFixedSentenceWithoutGitText()
    {
        // Arrange: git fails with a message that carries raw git output and the location
        var (projectId, _) = await ArrangeAsync();
        Git.OpenFailure = new DomainRuleException(
            DomainRule.GitAccessFailed,
            $"Repository 'main-repo' could not be read: fatal: unable to access '{Location}': Could not resolve host");
        var cut = RenderPage(projectId);

        // Act: plan
        await ShowPlanAsync(cut);

        // Assert: a fixed sentence and advice; nothing of git's text
        Assert.Equal("The repository could not be read.", TextOf(cut, "plan-error-message"));
        Assert.Contains("administrator", TextOf(cut, "plan-error-hint"), StringComparison.Ordinal);
        Assert.DoesNotContain("fatal", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-host", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Could not resolve", cut.Markup, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a fault that is not a rule of the domain shows a generic message with none of its text.
    /// </summary>
    [Fact]
    public async Task PlanPage_Error_UnexpectedFault_ShowsGenericMessage()
    {
        // Arrange: the repository access fails with a raw exception
        var (projectId, _) = await ArrangeAsync();
        Git.OpenFailure = new InvalidOperationException("SECRET stack detail at Foo.Bar() in C:\\src\\Foo.cs:line 12");
        var cut = RenderPage(projectId);

        // Act: plan
        await ShowPlanAsync(cut);

        // Assert: the generic message and the kind; none of the exception text
        Assert.Equal("Unexpected", cut.Find("[data-testid='plan-error']").GetAttribute("data-error-kind"));
        Assert.Contains("Something unexpected went wrong", TextOf(cut, "plan-error-message"), StringComparison.Ordinal);
        Assert.DoesNotContain("SECRET", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Foo.cs", cut.Markup, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a failure of the whole creation (for example the repository disabled after the plan was shown)
    ///     is shown next to the create button and keeps the plan, so the user can try again.
    /// </summary>
    [Fact]
    public async Task PlanPage_Create_WholeBatchRefused_ShowsErrorNextToButtonAndKeepsPlan()
    {
        // Arrange: a shown plan, then the repository is disabled
        var (projectId, repositoryId) = await ArrangeAsync();
        var cut = RenderPage(projectId);
        await ShowPlanAsync(cut);
        await Repositories.SetEnabledAsync(repositoryId, false, _admin, TestContext.Current.CancellationToken);

        // Act: create
        await cut.Find("[data-testid='plan-create-button']").ClickAsync();

        // Assert: one error box, inside the to-review group; the plan and the ticks are still there; nothing was created
        Assert.Single(cut.FindAll("[data-testid='plan-error']"));
        Assert.Single(cut.FindAll("[data-testid='plan-todo'] [data-testid='plan-error']"));
        Assert.Equal("RepositoryDisabled", cut.Find("[data-testid='plan-error']").GetAttribute("data-error-kind"));
        Assert.True(Has(cut, "plan-summary"));
        Assert.Equal("Create 2 reviews", TextOf(cut, "plan-create-button"));
        Assert.Empty(await Reviews.ListAsync(projectId, TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Test that markup in a set title or reason is shown as text, never as markup.
    /// </summary>
    [Fact]
    public async Task PlanPage_ShowPlan_MarkupInSetTitle_IsEscaped()
    {
        // Arrange: a set whose title is markup
        var (projectId, _) = await ArrangeAsync();
        SetParser.Sets = [new("core", "<b id=\"x\">Core</b>", ["src/**"])];
        var cut = RenderPage(projectId);

        // Act: show the plan
        await ShowPlanAsync(cut);

        // Assert: the title is text and there is no injected element
        Assert.Equal("<b id=\"x\">Core</b>", TextOf(cut, "plan-row-title-core"));
        Assert.Empty(cut.FindAll("#x"));
    }
}
