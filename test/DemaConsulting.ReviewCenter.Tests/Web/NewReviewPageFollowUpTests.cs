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
using Microsoft.AspNetCore.Components.Forms;
using TestContext = Xunit.TestContext;

namespace DemaConsulting.ReviewCenter.Tests.Web;

/// <summary>
///     Unit tests for the "follow up on an earlier review" part of the new-review form of <see cref="NewReviewPage"/>:
///     the selector, the preview of what is carried over, the acknowledgement of dropped threads, and the pass-through
///     to the service at both create paths.
/// </summary>
public class NewReviewPageFollowUpTests : PageTestBase
{
    /// <summary>
    ///     A commit identifier used as the head of the repository reviews.
    /// </summary>
    private const string HeadSha = "2222222222222222222222222222222222222222";

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
    ///     Creates a project and an earlier upload review <c>REV-1</c> of the files <c>a.txt</c> and <c>b.txt</c>, with
    ///     a Pass answer, an open thread on each file, and a resolved thread on <c>a.txt</c>.
    /// </summary>
    /// <returns>The project identity.</returns>
    private async Task<int> ArrangeSourceAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var project = await Projects.CreateAsync("Apollo", null, cancellationToken);
        await Reviews.CreateDraftAsync(
            project.Id,
            "REV-1",
            "First review",
            [new("a.txt", TestContent.A), new("b.txt", TestContent.B)],
            TestActors.Ann,
            cancellationToken);
        await Checklists.SetAnswerAsync(
            project.Id,
            "REV-1",
            DefaultChecklist.Current.Sections[0].Items[0].Id,
            ChecklistOutcome.Pass,
            null,
            TestActors.Bob,
            cancellationToken);
        await Threads.StartThreadAsync(project.Id, "REV-1", new ThreadAnchor("a.txt", 1, 1), TestActors.Bob, "Look at a", cancellationToken);
        await Threads.StartThreadAsync(project.Id, "REV-1", new ThreadAnchor("b.txt", 1, 1), TestActors.Bob, "Look at b", cancellationToken);
        var done = await Threads.StartThreadAsync(project.Id, "REV-1", new ThreadAnchor("a.txt", 1, 1), TestActors.Bob, "Done", cancellationToken);
        await Threads.ResolveAsync(project.Id, "REV-1", done.Id, TestActors.Bob, cancellationToken);
        return project.Id;
    }

    /// <summary>
    ///     Selects files in the file input the way a browser would and waits until the page lists them.
    /// </summary>
    /// <param name="cut">The rendered page.</param>
    /// <param name="names">The names of the files; each has the content <see cref="TestContent.A"/>.</param>
    /// <returns>A task that completes when the files are listed.</returns>
    private static async Task SelectFilesAsync(IRenderedComponent<NewReviewPage> cut, params string[] names)
    {
        cut.FindComponent<InputFile>().UploadFiles(
            names.Select(name => InputFileContent.CreateFromBinary(TestContent.A, name)).ToArray());
        await cut.WaitForAssertionAsync(() => Assert.NotEmpty(cut.FindAll("[data-testid='selected-file-0']")));
    }

    /// <summary>
    ///     Gets the text of an element with its white space collapsed, so that a test is not tied to the markup layout.
    /// </summary>
    /// <param name="cut">The rendered page.</param>
    /// <param name="testId">The test id of the element.</param>
    /// <returns>The collapsed text.</returns>
    private static string TextOf(IRenderedComponent<NewReviewPage> cut, string testId)
    {
        return Regex.Replace(cut.Find($"[data-testid='{testId}']").TextContent, @"\s+", " ").Trim();
    }

    /// <summary>
    ///     Creates an administrator, an enabled repository and an earlier repository review <c>R-1</c> of the files
    ///     <c>src/a.txt</c> and <c>src/b.txt</c> with an open thread on each.
    /// </summary>
    /// <param name="projectId">The project identity.</param>
    /// <returns>The repository identity.</returns>
    private async Task<int> ArrangeRepositorySourceAsync(int projectId)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var admin = new Actor("adm", "Adm");
        await Users.CreateAsync("adm", "Adm", UserRole.Admin, "correct horse battery", cancellationToken);
        var repository = await Repositories.CreateAsync(
            "main-repo", "https://example.com/org/repo.git", null, null, admin, cancellationToken);
        Git.Revisions["main"] = HeadSha;
        Git.Script(
            null,
            HeadSha,
            [("src/a.txt", ChangeKind.Added, null, "a\n"), ("src/b.txt", ChangeKind.Added, null, "b\n")]);
        await Reviews.CreateFromRepositoryAsync(
            projectId,
            "R-1",
            "From git",
            new RepositorySource(repository.Id, "main", null, "**/*"),
            TestActors.Ann,
            cancellationToken);
        await Threads.StartThreadAsync(projectId, "R-1", new ThreadAnchor("src/a.txt", 1, 1), TestActors.Bob, "Look at a", cancellationToken);
        await Threads.StartThreadAsync(projectId, "R-1", new ThreadAnchor("src/b.txt", 1, 1), TestActors.Bob, "Look at b", cancellationToken);
        return repository.Id;
    }

    /// <summary>
    ///     Test that a project without a review has no follow-up selector, and that a project whose reviews do not
    ///     suit the chosen kind of source shows the selector with an empty-state hint instead of options.
    /// </summary>
    [Fact]
    public async Task NewReviewPage_FollowUp_NoEligibleReview_HidesSelectorOrShowsEmptyHint()
    {
        // Arrange: a project with an upload review, and a repository to choose
        var projectId = await ArrangeSourceAsync();
        await ArrangeRepositorySourceAsync(projectId);

        // Act, Assert: a project without reviews has no selector at all
        var empty = await Projects.CreateAsync("Empty", null, TestContext.Current.CancellationToken);
        Assert.Empty(RenderPage(empty.Id).FindAll("[data-testid='follow-up']"));
        var cut = RenderPage(projectId);

        // Act: choose the repository source without choosing a repository
        await cut.Find("[data-testid='source-repository-radio']").ChangeAsync(true);

        // Assert: the selector offers nothing and says how to get options
        Assert.Equal(["No, this is a new review"], cut.FindAll("[data-testid='follow-source-select'] option").Select(o => o.TextContent));
        Assert.Contains("Choose a repository", TextOf(cut, "follow-source-none"), StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that the selector lists the earlier uploaded reviews for an upload and the reviews of the chosen
    ///     repository for a repository source.
    /// </summary>
    [Fact]
    public async Task NewReviewPage_FollowUp_Selector_ListsEligibleReviewsOfTheChosenSource()
    {
        // Arrange: an upload review and a repository review in one project
        var projectId = await ArrangeSourceAsync();
        var repositoryId = await ArrangeRepositorySourceAsync(projectId);
        var cut = RenderPage(projectId);

        // Act, Assert: an upload can follow the upload review only
        Assert.Equal(
            ["No, this is a new review", "REV-1 - First review"],
            cut.FindAll("[data-testid='follow-source-select'] option").Select(o => o.TextContent));

        // Act: choose the repository source and the repository
        await cut.Find("[data-testid='source-repository-radio']").ChangeAsync(true);
        await cut.Find("[data-testid='repository-select']").ChangeAsync(repositoryId.ToString(System.Globalization.CultureInfo.InvariantCulture));

        // Assert: only the review of that repository is offered
        Assert.Equal(
            ["No, this is a new review", "R-1 - From git"],
            cut.FindAll("[data-testid='follow-source-select'] option").Select(o => o.TextContent));
    }

    /// <summary>
    ///     Test that choosing an earlier review and the files shows what the follow-up carries, suggests an editable
    ///     title, and that creating passes the choice to the service for an upload.
    /// </summary>
    [Fact]
    public async Task NewReviewPage_FollowUp_Upload_ShowsPreviewSuggestsTitleAndCreatesFollowUp()
    {
        // Arrange: the earlier review and a form with the file a.txt
        var projectId = await ArrangeSourceAsync();
        var cut = RenderPage(projectId);
        await cut.Find("[data-testid='review-id-input']").InputAsync("REV-2");
        await SelectFilesAsync(cut, "a.txt", "b.txt");

        // Act: choose the earlier review
        await cut.Find("[data-testid='follow-source-select']").ChangeAsync("REV-1");

        // Assert: the preview counts the Pass answer, the two open threads and the resolved one that is not carried;
        // every file is in the new review, so nothing is dropped and no acknowledgement is asked for
        var preview = TextOf(cut, "follow-preview");
        Assert.Contains("1 Pass or N/A answer,", preview, StringComparison.Ordinal);
        Assert.Contains("0 N/A sections", preview, StringComparison.Ordinal);
        Assert.Contains("2 open threads", preview, StringComparison.Ordinal);
        Assert.Contains("1 resolved thread is not carried", preview, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("[data-testid='follow-acknowledge-checkbox']"));
        Assert.Equal("Follow-up to REV-1", cut.Find("[data-testid='review-title-input']").GetAttribute("value"));

        // Act: create the review
        await cut.Find("[data-testid='new-review-form']").SubmitAsync();

        // Assert: the review follows REV-1 and counts what it took over
        var review = await Reviews.GetAsync(projectId, "REV-2", TestContext.Current.CancellationToken);
        Assert.NotNull(review);
        Assert.Equal("REV-1", review.FollowsReviewId);
        Assert.Equal("Follow-up to REV-1", review.Title);
        Assert.Equal(1, review.CarriedAnswerCount);
        Assert.Equal(2, review.CarriedThreadCount);
    }

    /// <summary>
    ///     Test that a title the creator typed is kept when an earlier review is chosen.
    /// </summary>
    [Fact]
    public async Task NewReviewPage_FollowUp_TypedTitle_IsNotReplacedBySuggestion()
    {
        // Arrange: a form with a typed title
        var projectId = await ArrangeSourceAsync();
        var cut = RenderPage(projectId);
        await cut.Find("[data-testid='review-title-input']").InputAsync("My own title");
        await SelectFilesAsync(cut, "a.txt");

        // Act: choose the earlier review
        await cut.Find("[data-testid='follow-source-select']").ChangeAsync("REV-1");

        // Assert: the title is unchanged
        Assert.Equal("My own title", cut.Find("[data-testid='review-title-input']").GetAttribute("value"));
    }

    /// <summary>
    ///     Test that open threads on files the new review does not have are listed, that creating without the
    ///     acknowledgement is refused with the service message and creates nothing, and that the acknowledgement lets
    ///     the creation through.
    /// </summary>
    [Fact]
    public async Task NewReviewPage_FollowUp_DroppedThreads_NeedTheAcknowledgement()
    {
        // Arrange: the earlier review has an open thread on b.txt, and the new review has only a.txt
        var projectId = await ArrangeSourceAsync();
        var cut = RenderPage(projectId);
        await cut.Find("[data-testid='review-id-input']").InputAsync("REV-2");
        await SelectFilesAsync(cut, "a.txt");
        await cut.Find("[data-testid='follow-source-select']").ChangeAsync("REV-1");

        // Assert: the dropped file is named and the checkbox is offered
        Assert.Contains("b.txt", TextOf(cut, "follow-dropped-files"), StringComparison.Ordinal);
        Assert.Contains("1 open thread.", TextOf(cut, "follow-preview"), StringComparison.Ordinal);
        Assert.False(cut.Find("[data-testid='follow-acknowledge-checkbox']").HasAttribute("checked"));

        // Act: create without the acknowledgement
        await cut.Find("[data-testid='new-review-form']").SubmitAsync();

        // Assert: the service refuses and nothing is created
        Assert.Contains("Confirm that you have checked them", TextOf(cut, "review-error"), StringComparison.Ordinal);
        Assert.Null(await Reviews.GetAsync(projectId, "REV-2", TestContext.Current.CancellationToken));

        // Act: acknowledge and create
        await cut.Find("[data-testid='follow-acknowledge-checkbox']").ChangeAsync(true);
        await cut.Find("[data-testid='new-review-form']").SubmitAsync();

        // Assert: the follow-up exists and carried the thread of a.txt only
        var review = await Reviews.GetAsync(projectId, "REV-2", TestContext.Current.CancellationToken);
        Assert.Equal("REV-1", review?.FollowsReviewId);
        Assert.Equal(1, review?.CarriedThreadCount);
    }

    /// <summary>
    ///     Test that a refusal of the service (no file in common) shows its message with the choice and refuses the
    ///     creation with the same message.
    /// </summary>
    [Fact]
    public async Task NewReviewPage_FollowUp_UnfitFiles_ShowsServiceRefusal()
    {
        // Arrange: the earlier review has a.txt and b.txt, and the new files share none of them
        var projectId = await ArrangeSourceAsync();
        var cut = RenderPage(projectId);
        await cut.Find("[data-testid='review-id-input']").InputAsync("REV-2");
        await SelectFilesAsync(cut, "z.txt");

        // Act: choose the earlier review
        await cut.Find("[data-testid='follow-source-select']").ChangeAsync("REV-1");

        // Assert: the preview shows the message instead of counts
        Assert.Contains("no file in common", TextOf(cut, "follow-preview-error"), StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("[data-testid='follow-preview']"));

        // Act: create anyway
        await cut.Find("[data-testid='new-review-form']").SubmitAsync();

        // Assert: the same refusal is shown and nothing is created
        Assert.Contains("no file in common", TextOf(cut, "review-error"), StringComparison.Ordinal);
        Assert.Null(await Reviews.GetAsync(projectId, "REV-2", TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Test that choosing no earlier review again drops the preview and the suggested title, and that the review is
    ///     then created as a plain one.
    /// </summary>
    [Fact]
    public async Task NewReviewPage_FollowUp_ChoiceCleared_CreatesPlainReview()
    {
        // Arrange: a follow-up chosen and then cleared
        var projectId = await ArrangeSourceAsync();
        var cut = RenderPage(projectId);
        await cut.Find("[data-testid='review-id-input']").InputAsync("REV-2");
        await cut.Find("[data-testid='review-title-input']").InputAsync("Plain");
        await SelectFilesAsync(cut, "a.txt", "b.txt");
        await cut.Find("[data-testid='follow-source-select']").ChangeAsync("REV-1");
        await cut.Find("[data-testid='follow-source-select']").ChangeAsync(string.Empty);

        // Assert: no preview is left
        Assert.Empty(cut.FindAll("[data-testid='follow-preview']"));

        // Act: create
        await cut.Find("[data-testid='new-review-form']").SubmitAsync();

        // Assert: nothing is followed or carried
        var review = await Reviews.GetAsync(projectId, "REV-2", TestContext.Current.CancellationToken);
        Assert.NotNull(review);
        Assert.Null(review.FollowsReviewId);
        Assert.Equal(0, review.CarriedThreadCount);
    }

    /// <summary>
    ///     Test that for a repository source the page passes the choice and the acknowledgement to the service: the
    ///     creation is refused without the acknowledgement when threads would be dropped, and goes through with it.
    /// </summary>
    [Fact]
    public async Task NewReviewPage_FollowUp_Repository_PassesChoiceAndAcknowledgement()
    {
        // Arrange: the earlier repository review has open threads on src/a.txt and src/b.txt; the new selection has a
        var projectId = await ArrangeSourceAsync();
        var repositoryId = await ArrangeRepositorySourceAsync(projectId);
        var cut = RenderPage(projectId);
        await cut.Find("[data-testid='review-id-input']").InputAsync("R-2");
        await cut.Find("[data-testid='source-repository-radio']").ChangeAsync(true);
        await cut.Find("[data-testid='repository-select']").ChangeAsync(repositoryId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        await cut.Find("[data-testid='head-revision-input']").InputAsync("main");
        await cut.Find("[data-testid='selection-input']").InputAsync("src/a.txt");
        await cut.Find("[data-testid='follow-source-select']").ChangeAsync("R-1");

        // Assert: nothing is counted before the files are read, and a hint says so
        Assert.NotEmpty(cut.FindAll("[data-testid='follow-preview-hint']"));
        Assert.Empty(cut.FindAll("[data-testid='follow-preview']"));

        // Act: create without the acknowledgement
        await cut.Find("[data-testid='new-review-form']").SubmitAsync();

        // Assert: the service refuses with its message and nothing is created
        Assert.Contains("src/b.txt", TextOf(cut, "review-error"), StringComparison.Ordinal);
        Assert.Null(await Reviews.GetAsync(projectId, "R-2", TestContext.Current.CancellationToken));

        // Act: acknowledge and create
        await cut.Find("[data-testid='follow-acknowledge-checkbox']").ChangeAsync(true);
        await cut.Find("[data-testid='new-review-form']").SubmitAsync();

        // Assert: the follow-up carried the thread of src/a.txt
        var review = await Reviews.GetAsync(projectId, "R-2", TestContext.Current.CancellationToken);
        Assert.Equal("R-1", review?.FollowsReviewId);
        Assert.Equal(1, review?.CarriedThreadCount);
    }
}
