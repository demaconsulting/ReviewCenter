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
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using TestContext = Xunit.TestContext;

namespace DemaConsulting.ReviewCenter.Tests.Web;

/// <summary>
///     Unit tests for the repository source of the new-review form of <see cref="ProjectPage"/>.
/// </summary>
public class NewReviewPageRepositoryTests : PageTestBase
{
    /// <summary>
    ///     The location of the registered repository, which must never reach the page.
    /// </summary>
    private const string Location = "https://secret-host.example.com/org/private-repo.git";

    /// <summary>
    ///     A commit identifier used as the baseline.
    /// </summary>
    private const string BaseSha = "1111111111111111111111111111111111111111";

    /// <summary>
    ///     A commit identifier used as the head.
    /// </summary>
    private const string HeadSha = "2222222222222222222222222222222222222222";

    /// <summary>
    ///     The administrator who registers repositories.
    /// </summary>
    private static readonly Actor _admin = new("adm", "Adm");

    /// <summary>
    ///     Creates the administrator, an enabled repository and a project, and scripts the revisions
    ///     <c>main</c> and <c>v1</c> with one added, one modified and one deleted file.
    /// </summary>
    /// <returns>The project identity and the repository identity.</returns>
    private async Task<(int ProjectId, int RepositoryId)> ArrangeAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await Users.CreateAsync("adm", "Adm", UserRole.Admin, "correct horse battery", cancellationToken);
        var project = await Projects.CreateAsync("Apollo", null, cancellationToken);
        var repository = await Repositories.CreateAsync("main-repo", Location, "svc", "hidden-token-42", _admin, cancellationToken);
        Git.Revisions["main"] = HeadSha;
        Git.Revisions["v1"] = BaseSha;
        Git.Script(
            BaseSha,
            HeadSha,
            [
                ("docs/gone.md", ChangeKind.Deleted, "old doc\n", null),
                ("src/mod.txt", ChangeKind.Modified, "world\n", "hello\n"),
                ("src/new.txt", ChangeKind.Added, null, "world\n")
            ]);
        return (project.Id, repository.Id);
    }

    /// <summary>
    ///     Renders the page for a project.
    /// </summary>
    /// <param name="projectId">The project identity.</param>
    /// <returns>The rendered component.</returns>
    private IRenderedComponent<NewReviewPage> RenderPage(int projectId)
    {
        return Render<NewReviewPage>(parameters => parameters.Add(page => page.ProjectId, projectId));
    }

    /// <summary>
    ///     Chooses the repository source and fills in the form.
    /// </summary>
    /// <param name="cut">The rendered page.</param>
    /// <param name="repositoryId">The repository to pick.</param>
    /// <param name="head">The head revision.</param>
    /// <param name="baseline">The baseline revision.</param>
    /// <param name="selection">The selection, or <c>null</c> to keep the default.</param>
    private static async Task FillAsync(
        IRenderedComponent<NewReviewPage> cut,
        int repositoryId,
        string head,
        string baseline,
        string? selection = null)
    {
        await cut.Find("[data-testid='review-id-input']").InputAsync("REV-1");
        await cut.Find("[data-testid='review-title-input']").InputAsync("From git");
        await cut.Find("[data-testid='source-repository-radio']").ChangeAsync(true);
        await cut.Find("[data-testid='repository-select']").ChangeAsync(repositoryId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        await cut.Find("[data-testid='head-revision-input']").InputAsync(head);
        await cut.Find("[data-testid='baseline-revision-input']").InputAsync(baseline);
        if (selection is not null)
        {
            await cut.Find("[data-testid='selection-input']").InputAsync(selection);
        }
    }

    /// <summary>
    ///     Test that the form offers the upload source by default and shows the repository fields only after the
    ///     repository source is chosen, with the default selection.
    /// </summary>
    [Fact]
    public async Task NewReviewPage_SourceChoice_RepositoryChosen_ShowsRepositoryFieldsInsteadOfUpload()
    {
        // Arrange: a project and an enabled repository
        var (projectId, _) = await ArrangeAsync();

        // Act, Assert: upload is the default source
        var cut = RenderPage(projectId);
        Assert.True(cut.Find("[data-testid='source-upload-radio']").HasAttribute("checked"));
        Assert.NotEmpty(cut.FindAll("[data-testid='review-files-input']"));
        Assert.Empty(cut.FindAll("[data-testid='repository-select']"));
        Assert.Empty(cut.FindAll("[data-testid='no-repository-hint']"));

        // Act: choose the repository source
        await cut.Find("[data-testid='source-repository-radio']").ChangeAsync(true);

        // Assert: the repository fields replace the upload input and the selection has its default
        Assert.Empty(cut.FindAll("[data-testid='review-files-input']"));
        Assert.NotEmpty(cut.FindAll("[data-testid='repository-select']"));
        Assert.NotEmpty(cut.FindAll("[data-testid='head-revision-input']"));
        Assert.NotEmpty(cut.FindAll("[data-testid='baseline-revision-input']"));
        Assert.Equal("**/*", cut.Find("[data-testid='selection-input']").GetAttribute("value"));

        // Act: go back to upload
        await cut.Find("[data-testid='source-upload-radio']").ChangeAsync(true);

        // Assert: the upload input is back
        Assert.NotEmpty(cut.FindAll("[data-testid='review-files-input']"));
        Assert.Empty(cut.FindAll("[data-testid='repository-select']"));
    }

    /// <summary>
    ///     Test that only enabled repositories are offered, by name, and that neither the location nor the user
    ///     name nor the token appears anywhere in the page.
    /// </summary>
    [Fact]
    public async Task NewReviewPage_RepositorySelect_OffersOnlyEnabledRepositoriesWithoutLocationOrToken()
    {
        // Arrange: an enabled and a disabled repository
        var (projectId, _) = await ArrangeAsync();
        var disabled = await Repositories.CreateAsync(
            "old-repo", "https://example.com/old.git", null, null, _admin, TestContext.Current.CancellationToken);
        await Repositories.SetEnabledAsync(disabled.Id, false, _admin, TestContext.Current.CancellationToken);
        var cut = RenderPage(projectId);

        // Act: choose the repository source
        await cut.Find("[data-testid='source-repository-radio']").ChangeAsync(true);

        // Assert: one real option plus the prompt, and no secrets in the markup
        var options = cut.FindAll("[data-testid='repository-select'] option");
        Assert.Equal(["Choose a repository", "main-repo"], options.Select(option => option.TextContent));
        Assert.DoesNotContain("old-repo", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-host", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("hidden-token-42", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("svc", cut.Markup, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that without an enabled repository the repository source cannot be chosen and a hint says why.
    /// </summary>
    [Fact]
    public async Task NewReviewPage_SourceChoice_NoEnabledRepository_DisablesRepositorySourceWithHint()
    {
        // Arrange: a project and one repository that is disabled
        var (projectId, repositoryId) = await ArrangeAsync();
        await Repositories.SetEnabledAsync(repositoryId, false, _admin, TestContext.Current.CancellationToken);

        // Act: render the page and try to choose the repository source anyway
        var cut = RenderPage(projectId);
        await cut.Find("[data-testid='source-repository-radio']").ChangeAsync(true);

        // Assert: the radio is disabled, the hint is shown and the form stays on upload
        Assert.True(cut.Find("[data-testid='source-repository-radio']").HasAttribute("disabled"));
        Assert.NotEmpty(cut.FindAll("[data-testid='no-repository-hint']"));
        Assert.Empty(cut.FindAll("[data-testid='repository-select']"));
        Assert.NotEmpty(cut.FindAll("[data-testid='review-files-input']"));
    }

    /// <summary>
    ///     Test that a valid repository form creates the review with its source and navigates to it.
    /// </summary>
    [Fact]
    public async Task NewReviewPage_CreateForm_FromRepository_CreatesReviewWithSourceAndNavigates()
    {
        // Arrange: the repository form filled in with a selection that leaves out the deleted file
        var (projectId, repositoryId) = await ArrangeAsync();
        var cut = RenderPage(projectId);
        await FillAsync(cut, repositoryId, "main", "v1", "src/**\n!src/new.txt");

        // Act: submit
        await cut.Find("[data-testid='new-review-form']").SubmitAsync();

        // Assert: the review has the source, only the selected change, and the browser was sent to it
        var review = await Reviews.GetAsync(projectId, "REV-1", TestContext.Current.CancellationToken);
        Assert.NotNull(review);
        Assert.Equal("main-repo", review.RepositoryName);
        Assert.Equal(BaseSha, review.BaselineSha);
        Assert.Equal(HeadSha, review.HeadSha);
        Assert.Equal("src/**\n!src/new.txt", review.Selection);
        Assert.Equal(["src/mod.txt"], review.Files.Select(file => file.Path));
        var navigation = Services.GetRequiredService<NavigationManager>();
        Assert.EndsWith($"/projects/{projectId}/reviews/REV-1", navigation.Uri, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a due date typed into the optional field is stored with the new review, and that the form also
    ///     works without one.
    /// </summary>
    [Fact]
    public async Task NewReviewPage_CreateForm_DueDate_IsStoredWithTheReview()
    {
        // Arrange: the repository form filled in, with a due date (the page's clock is 2025-03-04)
        var (projectId, repositoryId) = await ArrangeAsync();
        var cut = RenderPage(projectId);
        await FillAsync(cut, repositoryId, "main", "v1");
        Assert.Contains("UTC", cut.Markup, StringComparison.Ordinal);
        await cut.Find("[data-testid='due-date-input']").InputAsync("2025-04-01");

        // Act: submit
        await cut.Find("[data-testid='new-review-form']").SubmitAsync();

        // Assert: the review carries the due date
        var review = await Reviews.GetAsync(projectId, "REV-1", TestContext.Current.CancellationToken);
        Assert.Equal(new DateOnly(2025, 4, 1), review!.DueDate);
    }

    /// <summary>
    ///     Test that a due date in the past is refused with a message and creates nothing, and that an empty field
    ///     means no due date.
    /// </summary>
    [Fact]
    public async Task NewReviewPage_CreateForm_DueDateInThePast_ShowsErrorAndCreatesNothing()
    {
        // Arrange: the repository form filled in, with yesterday as the due date
        var (projectId, repositoryId) = await ArrangeAsync();
        var cut = RenderPage(projectId);
        await FillAsync(cut, repositoryId, "main", "v1");
        await cut.Find("[data-testid='due-date-input']").InputAsync("2025-03-03");

        // Act: submit
        await cut.Find("[data-testid='new-review-form']").SubmitAsync();

        // Assert: the error names the due date and nothing was created
        Assert.Contains("due date", cut.Find("[data-testid='review-error']").TextContent, StringComparison.OrdinalIgnoreCase);
        Assert.Null(await Reviews.GetAsync(projectId, "REV-1", TestContext.Current.CancellationToken));

        // Act: clear the field and submit again
        await cut.Find("[data-testid='due-date-input']").InputAsync(string.Empty);
        await cut.Find("[data-testid='new-review-form']").SubmitAsync();

        // Assert: the review is created without a due date
        var review = await Reviews.GetAsync(projectId, "REV-1", TestContext.Current.CancellationToken);
        Assert.NotNull(review);
        Assert.Null(review.DueDate);
    }

    /// <summary>
    ///     Test that an empty baseline and the default selection review every file at the head.
    /// </summary>
    [Fact]
    public async Task NewReviewPage_CreateForm_FromRepositoryWithoutBaseline_ReviewsAllFilesAtHead()
    {
        // Arrange: no baseline scripted, the default selection kept
        var (projectId, repositoryId) = await ArrangeAsync();
        Git.Script(null, HeadSha, [("a.txt", ChangeKind.Added, null, "a\n"), ("b.txt", ChangeKind.Added, null, "b\n")]);
        var cut = RenderPage(projectId);
        await FillAsync(cut, repositoryId, "main", string.Empty);

        // Act: submit
        await cut.Find("[data-testid='new-review-form']").SubmitAsync();

        // Assert: both files are in, as additions, with no baseline recorded
        var review = await Reviews.GetAsync(projectId, "REV-1", TestContext.Current.CancellationToken);
        Assert.Equal(["a.txt", "b.txt"], review!.Files.Select(file => file.Path));
        Assert.Null(review.BaselineSha);
        Assert.Equal("**/*", review.Selection);
    }

    /// <summary>
    ///     Test that a selection with no changed files shows the nothing-to-review message and creates nothing.
    /// </summary>
    [Fact]
    public async Task NewReviewPage_CreateForm_EmptySelection_ShowsNothingToReviewAndCreatesNothing()
    {
        // Arrange: a selection that matches none of the changed files
        var (projectId, repositoryId) = await ArrangeAsync();
        var cut = RenderPage(projectId);
        await FillAsync(cut, repositoryId, "main", "v1", "nothing/**");

        // Act: submit
        await cut.Find("[data-testid='new-review-form']").SubmitAsync();

        // Assert: the message names both revisions, nothing exists and the form stays
        Assert.Equal(
            "Nothing to review: no files matching the selection changed between v1 and main",
            cut.Find("[data-testid='review-error']").TextContent);
        Assert.Empty(await Reviews.ListAsync(projectId, TestContext.Current.CancellationToken));
        Assert.NotEmpty(cut.FindAll("[data-testid='new-review-form']"));
    }

    /// <summary>
    ///     Test that a revision that does not exist shows the git error and creates nothing.
    /// </summary>
    [Fact]
    public async Task NewReviewPage_CreateForm_BadRevision_ShowsErrorAndCreatesNothing()
    {
        // Arrange: a head revision the repository does not know
        var (projectId, repositoryId) = await ArrangeAsync();
        var cut = RenderPage(projectId);
        await FillAsync(cut, repositoryId, "no-such-branch", "v1");

        // Act: submit
        await cut.Find("[data-testid='new-review-form']").SubmitAsync();

        // Assert: the error names the revision, nothing was created
        Assert.Contains("no-such-branch", cut.Find("[data-testid='review-error']").TextContent, StringComparison.Ordinal);
        Assert.Empty(await Reviews.ListAsync(projectId, TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Test that submitting without choosing a repository asks for one.
    /// </summary>
    [Fact]
    public async Task NewReviewPage_CreateForm_NoRepositoryChosen_AsksForOne()
    {
        // Arrange: the repository source with everything but the repository filled in
        var (projectId, _) = await ArrangeAsync();
        var cut = RenderPage(projectId);
        await cut.Find("[data-testid='review-id-input']").InputAsync("REV-1");
        await cut.Find("[data-testid='source-repository-radio']").ChangeAsync(true);
        await cut.Find("[data-testid='head-revision-input']").InputAsync("main");

        // Act: submit
        await cut.Find("[data-testid='new-review-form']").SubmitAsync();

        // Assert: the page asks for a repository and nothing was created
        Assert.Equal("Choose a repository.", cut.Find("[data-testid='review-error']").TextContent);
        Assert.Empty(await Reviews.ListAsync(projectId, TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Test that a repository disabled after the page was opened is refused when the form is submitted.
    /// </summary>
    [Fact]
    public async Task NewReviewPage_CreateForm_RepositoryDisabledMeanwhile_ShowsErrorAndCreatesNothing()
    {
        // Arrange: the filled-in form, then an administrator disables the repository
        var (projectId, repositoryId) = await ArrangeAsync();
        var cut = RenderPage(projectId);
        await FillAsync(cut, repositoryId, "main", "v1");
        await Repositories.SetEnabledAsync(repositoryId, false, _admin, TestContext.Current.CancellationToken);

        // Act: submit
        await cut.Find("[data-testid='new-review-form']").SubmitAsync();

        // Assert: an error is shown and nothing was created
        Assert.NotEmpty(cut.Find("[data-testid='review-error']").TextContent);
        Assert.Empty(await Reviews.ListAsync(projectId, TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Test that skipped links and submodules are shown to the creator before the review is opened.
    /// </summary>
    [Fact]
    public async Task NewReviewPage_CreateForm_SkippedEntries_AreShownBeforeOpeningTheReview()
    {
        // Arrange: a change set that reports a link and a submodule as skipped
        var (projectId, repositoryId) = await ArrangeAsync();
        Git.Script(
            BaseSha,
            HeadSha,
            [("src/mod.txt", ChangeKind.Modified, "world\n", "hello\n")],
            new SkippedEntry("src/link", "symbolic link"),
            new SkippedEntry("src/sub", "submodule"));
        var cut = RenderPage(projectId);
        await FillAsync(cut, repositoryId, "main", "v1");

        // Act: submit
        await cut.Find("[data-testid='new-review-form']").SubmitAsync();

        // Assert: the review exists, the skipped entries are listed and the page did not navigate away
        Assert.NotNull(await Reviews.GetAsync(projectId, "REV-1", TestContext.Current.CancellationToken));
        Assert.Contains("src/link", cut.Find("[data-testid='skipped-entry-0']").TextContent, StringComparison.Ordinal);
        Assert.Contains("symbolic link", cut.Find("[data-testid='skipped-entry-0']").TextContent, StringComparison.Ordinal);
        Assert.Contains("src/sub", cut.Find("[data-testid='skipped-entry-1']").TextContent, StringComparison.Ordinal);
        Assert.Equal(
            $"/projects/{projectId}/reviews/REV-1",
            cut.Find("[data-testid='open-created-review-link']").GetAttribute("href"));
        var navigation = Services.GetRequiredService<NavigationManager>();
        Assert.DoesNotContain("/reviews/REV-1", navigation.Uri, StringComparison.Ordinal);
    }
}
