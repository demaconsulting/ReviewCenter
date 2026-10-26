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
///     Unit tests for how <see cref="ReviewPage"/> shows a review created from a git repository: change kinds,
///     the Head and Baseline versions, deleted files and the source details.
/// </summary>
public class ReviewPageRepositoryTests : PageTestBase
{
    /// <summary>
    ///     A commit identifier used as the baseline.
    /// </summary>
    private const string BaseSha = "1111111111111111111111111111111111111111";

    /// <summary>
    ///     A commit identifier used as the head.
    /// </summary>
    private const string HeadSha = "2222222222222222222222222222222222222222";

    /// <summary>
    ///     Creates a repository review <c>REV-1</c> with <c>docs/gone.md</c> deleted, <c>src/mod.txt</c> modified
    ///     and <c>src/new.txt</c> added (listed in that order).
    /// </summary>
    /// <returns>The project identity.</returns>
    private async Task<int> CreateRepositoryReviewAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var admin = new Actor("adm", "Adm");
        await Users.CreateAsync("adm", "Adm", UserRole.Admin, "correct horse battery", cancellationToken);
        var repository = await Repositories.CreateAsync("main-repo", "https://example.com/org/repo.git", null, null, admin, cancellationToken);
        Git.Revisions["main"] = HeadSha;
        Git.Revisions["v1"] = BaseSha;
        Git.Script(
            BaseSha,
            HeadSha,
            [
                ("docs/gone.md", ChangeKind.Deleted, "old doc\nsecond\n", null),
                ("src/mod.txt", ChangeKind.Modified, "before 1\nbefore 2\n", "head 1\nhead 2\nhead 3\n"),
                ("src/new.txt", ChangeKind.Added, null, "new\n")
            ]);
        var project = await Projects.CreateAsync("Apollo", null, cancellationToken);
        await Reviews.CreateFromRepositoryAsync(
            project.Id, "REV-1", "From git", repository.Id, "main", "v1", "src/**\ndocs/**", TestActors.Ann, cancellationToken);
        return project.Id;
    }

    /// <summary>
    ///     Renders the page for the review.
    /// </summary>
    /// <param name="projectId">The project identity.</param>
    /// <returns>The rendered component.</returns>
    private IRenderedComponent<ReviewPage> RenderPage(int projectId)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        return Render<ReviewPage>(parameters => parameters
            .Add(page => page.ProjectId, projectId)
            .Add(page => page.ReviewId, "REV-1"));
    }

    /// <summary>
    ///     Test that each file of a repository review shows its change kind, and an uploaded review shows none.
    /// </summary>
    [Fact]
    public async Task ReviewPage_Render_RepositoryReview_ShowsChangeKindPerFile()
    {
        // Arrange: a repository review and an uploaded one
        var projectId = await CreateRepositoryReviewAsync();
        await Reviews.CreateDraftAsync(projectId, "REV-2", "Upload", [new("a.txt", TestContent.A)], TestContext.Current.CancellationToken);

        // Act: render the repository review and then the upload
        var cut = RenderPage(projectId);
        var upload = Render<ReviewPage>(parameters => parameters
            .Add(page => page.ProjectId, projectId)
            .Add(page => page.ReviewId, "REV-2"));

        // Assert: kinds in the file list, none for the upload
        Assert.Equal(
            ["Deleted", "Modified", "Added"],
            Enumerable.Range(0, 3).Select(index => cut.Find($"[data-testid='file-kind-{index}']").TextContent));
        Assert.Empty(upload.FindAll("[data-testid='file-kind-0']"));
        Assert.Empty(upload.FindAll("[data-testid='file-version-toggle']"));
    }

    /// <summary>
    ///     Test that the Details section shows the repository name, both commits and the selection.
    /// </summary>
    [Fact]
    public async Task ReviewPage_Render_RepositoryReview_DetailsShowRepositoryShasAndSelection()
    {
        // Arrange: a repository review
        var projectId = await CreateRepositoryReviewAsync();

        // Act: render the page
        var cut = RenderPage(projectId);

        // Assert: the source details
        Assert.Equal("main-repo", cut.Find("[data-testid='review-repository']").TextContent);
        Assert.Equal(BaseSha, cut.Find("[data-testid='review-baseline-sha']").TextContent);
        Assert.Equal(HeadSha, cut.Find("[data-testid='review-head-sha']").TextContent);
        Assert.Equal("src/**\ndocs/**", cut.Find("[data-testid='review-selection']").TextContent);

        // Assert: a review that was not planned from a review-set has no Review set row
        Assert.Empty(cut.FindAll("[data-testid='review-set-id']"));
    }

    /// <summary>
    ///     Test that the Details section of a review created by planning shows the review-set it belongs to.
    /// </summary>
    [Fact]
    public async Task ReviewPage_Render_PlannedReview_DetailsShowReviewSet()
    {
        // Arrange: a review created from the review-set "core" by the planner
        var projectId = await CreateRepositoryReviewAsync();
        Git.Script(null, HeadSha, [("src/a.cs", ChangeKind.Added, null, "a\n")]);
        var repositoryId = (await Repositories.ListEnabledAsync(TestContext.Current.CancellationToken)).Single().Id;
        var results = await Planner.CreateAsync(
            projectId,
            repositoryId,
            HeadSha,
            [new PlanCreateRequest("core", "Core", "src/**", null)],
            TestActors.Ann,
            TestContext.Current.CancellationToken);
        var reviewId = results.Single().ReviewId!;

        // Act: render the planned review
        var cut = Render<ReviewPage>(parameters => parameters
            .Add(page => page.ProjectId, projectId)
            .Add(page => page.ReviewId, reviewId));

        // Assert: the set id is shown in the Details, after the repository
        Assert.Equal("core", cut.Find("[data-testid='review-set-id']").TextContent);
        Assert.Equal("main-repo", cut.Find("[data-testid='review-repository']").TextContent);
    }

    /// <summary>
    ///     Test that a modified file opens as a diff and offers a toggle to the Head version with the comment
    ///     controls and to the read-only Baseline version, and that no toggle is offered for an added file.
    /// </summary>
    [Fact]
    public async Task ReviewPage_FileSelect_ModifiedFile_TogglesBetweenDiffHeadAndReadOnlyBaseline()
    {
        // Arrange: the repository review, with the modified file selected
        var projectId = await CreateRepositoryReviewAsync();
        var cut = RenderPage(projectId);
        await cut.Find("[data-testid='file-select-1']").ClickAsync();

        // Assert: the diff is the default view
        Assert.Equal("Modified", cut.Find("[data-testid='selected-file-kind']").TextContent);
        Assert.Equal("true", cut.Find("[data-testid='view-diff-button']").GetAttribute("aria-pressed"));
        Assert.NotEmpty(cut.FindAll("[data-testid='diff-view']"));
        Assert.Empty(cut.FindAll("[data-testid='file-content']"));

        // Act: switch to the head
        await cut.Find("[data-testid='view-head-button']").ClickAsync();

        // Assert: the head version is shown with comment buttons
        Assert.Equal("true", cut.Find("[data-testid='view-head-button']").GetAttribute("aria-pressed"));
        Assert.Equal("head 3", cut.Find("[data-testid='line-text-3']").TextContent);
        Assert.NotEmpty(cut.FindAll("[data-testid='line-comment-1']"));
        Assert.Empty(cut.FindAll("[data-testid='baseline-content']"));
        Assert.Empty(cut.FindAll("[data-testid='diff-view']"));

        // Act: switch to the baseline
        await cut.Find("[data-testid='view-baseline-button']").ClickAsync();

        // Assert: the baseline lines are shown numbered and read-only, and commenting is declined
        Assert.Equal("true", cut.Find("[data-testid='view-baseline-button']").GetAttribute("aria-pressed"));
        Assert.Equal("before 1", cut.Find("[data-testid='baseline-line-text-1']").TextContent);
        Assert.Equal("2", cut.Find("[data-testid='baseline-line-no-2']").TextContent);
        Assert.Equal(2, cut.FindAll("[data-testid^='baseline-line-text-']").Count);
        Assert.Empty(cut.FindAll("[data-testid='file-content']"));
        Assert.Empty(cut.FindAll("[data-testid^='line-comment-']"));
        Assert.Empty(cut.FindAll("[data-testid='new-thread-form']"));
        Assert.NotEmpty(cut.FindAll("[data-testid='baseline-comment-note']"));

        // Act: switch back to the head
        await cut.Find("[data-testid='view-head-button']").ClickAsync();

        // Assert: the head version and the comment form are back
        Assert.Equal("head 1", cut.Find("[data-testid='line-text-1']").TextContent);
        Assert.NotEmpty(cut.FindAll("[data-testid='new-thread-form']"));
        Assert.Empty(cut.FindAll("[data-testid='baseline-content']"));

        // Act: select the added file
        await cut.Find("[data-testid='file-select-2']").ClickAsync();

        // Assert: it has only the head version
        Assert.Empty(cut.FindAll("[data-testid='file-version-toggle']"));
        Assert.Equal("new", cut.Find("[data-testid='line-text-1']").TextContent);
    }

    /// <summary>
    ///     Test that selecting another file after viewing a baseline starts on the head version again.
    /// </summary>
    [Fact]
    public async Task ReviewPage_FileSelect_AfterBaselineView_ShowsHeadOfTheNextFile()
    {
        // Arrange: the modified file shown at its baseline
        var projectId = await CreateRepositoryReviewAsync();
        var cut = RenderPage(projectId);
        await cut.Find("[data-testid='file-select-1']").ClickAsync();
        await cut.Find("[data-testid='view-baseline-button']").ClickAsync();

        // Act: select the added file
        await cut.Find("[data-testid='file-select-2']").ClickAsync();

        // Assert: the head version of the added file is shown, not a baseline view
        Assert.Empty(cut.FindAll("[data-testid='baseline-content']"));
        Assert.NotEmpty(cut.FindAll("[data-testid='file-content']"));
    }

    /// <summary>
    ///     Test that a deleted file opens as a diff of removed lines that can be commented on in the baseline
    ///     numbering, offers the baseline text, and still accepts a whole-file comment.
    /// </summary>
    [Fact]
    public async Task ReviewPage_FileSelect_DeletedFile_ShowsRemovedLinesAndAcceptsWholeFileComment()
    {
        // Arrange: the repository review
        var projectId = await CreateRepositoryReviewAsync();
        var cut = RenderPage(projectId);

        // Act: select the deleted file
        await cut.Find("[data-testid='file-select-0']").ClickAsync();

        // Assert: the diff shows every line removed, with a Diff and a Baseline view but no Head view
        Assert.Equal("Deleted", cut.Find("[data-testid='selected-file-kind']").TextContent);
        Assert.Equal("0 added, 2 removed", cut.Find("[data-testid='diff-summary']").TextContent);
        Assert.Equal("old doc", cut.Find("[data-testid='diff-text-0']").TextContent);
        Assert.Equal("removed", cut.Find("[data-testid='diff-row-0']").GetAttribute("data-kind"));
        Assert.Empty(cut.FindAll("[data-testid='view-head-button']"));
        Assert.NotEmpty(cut.FindAll("[data-testid='view-baseline-button']"));

        // Act: comment on the whole file, as the form below the diff offers
        await cut.Find("[data-testid='thread-text-input']").InputAsync("Why was this removed?");
        await cut.Find("[data-testid='new-thread-form']").SubmitAsync();

        // Assert: the whole-file thread exists on the deleted file
        var thread = Assert.Single(await Threads.ListAsync(projectId, "REV-1", TestContext.Current.CancellationToken));
        Assert.Equal("docs/gone.md", thread.FilePath);
        Assert.Null(thread.StartLine);
        Assert.Empty(cut.FindAll("[data-testid='thread-error']"));
    }

    /// <summary>
    ///     Test that the baseline view of a deleted file shows the deleted content with a note and offers a
    ///     whole-file comment only.
    /// </summary>
    [Fact]
    public async Task ReviewPage_FileSelect_DeletedFileBaselineView_ShowsNoteWithoutLineControls()
    {
        // Arrange: the deleted file selected
        var projectId = await CreateRepositoryReviewAsync();
        var cut = RenderPage(projectId);
        await cut.Find("[data-testid='file-select-0']").ClickAsync();

        // Act: switch to the baseline
        await cut.Find("[data-testid='view-baseline-button']").ClickAsync();

        // Assert: the content is read-only with a note, and only a whole-file comment is offered
        Assert.Equal("old doc", cut.Find("[data-testid='baseline-line-text-1']").TextContent);
        Assert.Contains("Deleted file", cut.Find("[data-testid='baseline-note']").TextContent, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("[data-testid^='line-comment-']"));
        Assert.Empty(cut.FindAll("[data-testid='thread-start-line-input']"));
        Assert.NotEmpty(cut.FindAll("[data-testid='deleted-comment-note']"));
    }

    /// <summary>
    ///     Test that a deleted file is listed with the size of its baseline content, not of its empty head.
    /// </summary>
    [Fact]
    public async Task ReviewPage_Render_DeletedFile_ListsBaselineSize()
    {
        // Arrange and Act: the repository review, rendered
        var projectId = await CreateRepositoryReviewAsync();
        var cut = RenderPage(projectId);

        // Assert: "old doc\nsecond\n" is 15 bytes
        Assert.Equal("15", cut.Find("[data-testid='file-size-0']").TextContent);
    }
}
