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
using Microsoft.AspNetCore.Components.Web;
using TestContext = Xunit.TestContext;

namespace DemaConsulting.ReviewCenter.Tests.Web;

/// <summary>
///     Unit tests for the diff view of <see cref="ReviewPage"/> (the <c>DiffView</c> component and the
///     Diff / Head / Baseline selector): the default view, the summary, collapsed unchanged lines, the large,
///     binary and line-ending states, comments per side, threads under their lines and the thread index.
/// </summary>
public class ReviewPageDiffTests : PageTestBase
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
    ///     The modified file used by most tests; its diff has rows 0-8 unchanged (lines 1-9), row 9 removed
    ///     (old line 10), row 10 added (new line 10), rows 11-20 unchanged (lines 11-20) and row 21 added (new line 21).
    /// </summary>
    private const string ModPath = "src/mod.txt";

    /// <summary>
    ///     Joins numbered lines into a text with a final newline.
    /// </summary>
    /// <param name="count">The number of lines.</param>
    /// <param name="replaceTenth">The text to use for line 10 instead of <c>l10</c>.</param>
    /// <returns>The text.</returns>
    private static string Lines(int count, string replaceTenth = "l10")
    {
        return string.Concat(Enumerable.Range(1, count).Select(n => (n == 10 ? replaceTenth : $"l{n}") + "\n"));
    }

    /// <summary>
    ///     Creates a repository review <c>REV-1</c> with <see cref="ModPath"/> modified plus the given extra files.
    /// </summary>
    /// <param name="extra">More changed files.</param>
    /// <returns>The project identity.</returns>
    private async Task<int> CreateReviewAsync(params (string Path, ChangeKind Kind, string? Baseline, string? Head)[] extra)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var admin = new Actor("adm", "Adm");
        await Users.CreateAsync("adm", "Adm", UserRole.Admin, "correct horse battery", cancellationToken);
        var repository = await Repositories.CreateAsync("main-repo", "https://example.com/org/repo.git", null, null, admin, cancellationToken);
        Git.Revisions["main"] = HeadSha;
        Git.Revisions["v1"] = BaseSha;
        var changes = new List<(string Path, ChangeKind Kind, string? Baseline, string? Head)>
        {
            (ModPath, ChangeKind.Modified, Lines(20), Lines(20, "n10") + "l21\n")
        };
        changes.AddRange(extra);
        Git.Script(BaseSha, HeadSha, changes);
        var project = await Projects.CreateAsync("Apollo", null, cancellationToken);
        await Reviews.CreateFromRepositoryAsync(
            project.Id, "REV-1", "From git", repository.Id, "main", "v1", "src/**", TestActors.Ann, cancellationToken);
        return project.Id;
    }

    /// <summary>
    ///     Renders the page for the review <c>REV-1</c>.
    /// </summary>
    /// <param name="projectId">The project identity.</param>
    /// <param name="reviewId">The review identity.</param>
    /// <returns>The rendered component.</returns>
    private IRenderedComponent<ReviewPage> RenderPage(int projectId, string reviewId = "REV-1")
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        return Render<ReviewPage>(parameters => parameters
            .Add(page => page.ProjectId, projectId)
            .Add(page => page.ReviewId, reviewId));
    }

    /// <summary>
    ///     Chooses a file of the review by its path.
    /// </summary>
    /// <param name="cut">The rendered page.</param>
    /// <param name="path">The file path.</param>
    /// <returns>A task that completes when the file is shown.</returns>
    private static async Task SelectFileAsync(IRenderedComponent<ReviewPage> cut, string path)
    {
        var index = cut.FindAll("[data-testid^='file-path-']").ToList().FindIndex(e => e.TextContent == path);
        Assert.True(index >= 0, $"{path} is not listed");
        await cut.Find($"[data-testid='file-select-{index}']").ClickAsync();
    }

    /// <summary>
    ///     Renders the page with <see cref="ModPath"/> selected.
    /// </summary>
    /// <returns>The project identity and the page.</returns>
    private async Task<(int ProjectId, IRenderedComponent<ReviewPage> Page)> OpenModAsync()
    {
        var projectId = await CreateReviewAsync();
        var cut = RenderPage(projectId);
        await SelectFileAsync(cut, ModPath);
        return (projectId, cut);
    }

    /// <summary>
    ///     Starts a thread on <see cref="ModPath"/>.
    /// </summary>
    /// <param name="projectId">The project identity.</param>
    /// <param name="start">The first line.</param>
    /// <param name="end">The last line.</param>
    /// <param name="side">The version the lines refer to.</param>
    /// <param name="text">The first comment.</param>
    /// <param name="path">The file path.</param>
    /// <returns>The thread.</returns>
    private async Task<ReviewThread> StartAsync(int projectId, int start, int end, ThreadSide side, string text, string path = ModPath)
    {
        return await Threads.StartThreadAsync(
            projectId, "REV-1", path, start, end, TestActors.Bob, text, side, TestContext.Current.CancellationToken);
    }

    /// <summary>
    ///     Test that a modified file opens as a diff with the "x added, y removed" summary, the hint, and the
    ///     rows numbered on both sides.
    /// </summary>
    [Fact]
    public async Task ReviewPage_FileSelect_ModifiedFile_ShowsDiffWithSummaryHintAndNumbers()
    {
        // Arrange and Act: open the modified file
        var (_, cut) = await OpenModAsync();

        // Assert: the diff is the chosen view, with summary and hint
        Assert.Equal("true", cut.Find("[data-testid='view-diff-button']").GetAttribute("aria-pressed"));
        Assert.Equal("2 added, 1 removed", cut.Find("[data-testid='diff-summary']").TextContent);
        Assert.Equal("Click + to comment on a line.", cut.Find("[data-testid='diff-hint']").TextContent);

        // Assert: the removed row has only an old number (left), the added row only a new one (right), a context row both
        Assert.Equal("removed", cut.Find("[data-testid='diff-row-9']").GetAttribute("data-kind"));
        Assert.Equal("l10", cut.Find("[data-testid='diff-text-9']").TextContent);
        Assert.Equal("10", cut.Find("[data-testid='diff-old-no-9']").TextContent);
        Assert.Empty(cut.FindAll("[data-testid='diff-new-no-9']"));
        Assert.Equal("added", cut.Find("[data-testid='diff-row-10']").GetAttribute("data-kind"));
        Assert.Equal("n10", cut.Find("[data-testid='diff-text-10']").TextContent);
        Assert.Equal("10", cut.Find("[data-testid='diff-new-no-10']").TextContent);
        Assert.Empty(cut.FindAll("[data-testid='diff-old-no-10']"));
        Assert.Equal("context", cut.Find("[data-testid='diff-row-11']").GetAttribute("data-kind"));
        Assert.Equal("11", cut.Find("[data-testid='diff-old-no-11']").TextContent);
        Assert.Equal("11", cut.Find("[data-testid='diff-new-no-11']").TextContent);
    }

    /// <summary>
    ///     Test that the diff is laid out side by side: the removed line and the added line that replaces it share a
    ///     row (baseline on the left, head on the right), an unchanged line shows on both sides, and the line that
    ///     is only added has a muted empty placeholder on the left.
    /// </summary>
    [Fact]
    public async Task ReviewPage_DiffView_Rows_AreSideBySideWithPlaceholders()
    {
        // Arrange and Act: open the modified file
        var (_, cut) = await OpenModAsync();

        // Assert: the replaced line is one row, left removed and right added
        var replaced = cut.Find("[data-testid='diff-pair-9']");
        Assert.Equal(
            ["diff-row-9", "diff-row-10"],
            replaced.QuerySelectorAll(".diff-row").Select(e => e.GetAttribute("data-testid")).ToArray());
        Assert.Contains("left", cut.Find("[data-testid='diff-row-9']").ClassList);
        Assert.Contains("right", cut.Find("[data-testid='diff-row-10']").ClassList);

        // Assert: an unchanged line is on both sides, the baseline copy having its own ids
        Assert.Equal("l11", cut.Find("[data-testid='diff-text-old-11']").TextContent);
        Assert.Equal("l11", cut.Find("[data-testid='diff-text-11']").TextContent);
        Assert.Contains("left", cut.Find("[data-testid='diff-row-old-11']").ClassList);
        Assert.Contains("right", cut.Find("[data-testid='diff-row-11']").ClassList);

        // Assert: the pure addition (head line 21) has an empty left placeholder
        var placeholder = cut.Find("[data-testid='diff-empty-left-21']");
        Assert.Contains("empty", placeholder.ClassList);
        Assert.Equal("true", placeholder.GetAttribute("aria-hidden"));
        Assert.Equal("added", cut.Find("[data-testid='diff-row-21']").GetAttribute("data-kind"));
    }

    /// <summary>
    ///     Test that the left control comments on the baseline numbering, also for an unchanged line, and the right
    ///     control on the head numbering.
    /// </summary>
    [Fact]
    public async Task ReviewPage_DiffView_LeftControlIsBaseline_RightControlIsHead()
    {
        // Arrange: open the modified file
        var (projectId, cut) = await OpenModAsync();

        // Act: comment through the left cell of an unchanged line
        await cut.Find("[data-testid='diff-comment-old-11']").ClickAsync();

        // Assert: the form is aimed at baseline line 11
        Assert.Equal("Comment on line 11 (before).", cut.Find("[data-testid='thread-target']").TextContent);
        Assert.Contains("selected", cut.Find("[data-testid='diff-row-old-11']").ClassList);
        Assert.DoesNotContain("selected", cut.Find("[data-testid='diff-row-11']").ClassList);

        // Act: submit it
        await cut.Find("[data-testid='thread-text-input']").InputAsync("Was fine before");
        await cut.Find("[data-testid='new-thread-form']").SubmitAsync();

        // Assert: a baseline thread, shown under the row and marking the left cell only
        var thread = Assert.Single(await Threads.ListAsync(projectId, "REV-1", TestContext.Current.CancellationToken));
        Assert.Equal((11, 11, ThreadSide.Baseline), (thread.StartLine, thread.EndLine, thread.Side));
        Assert.Contains("anchored", cut.Find("[data-testid='diff-row-old-11']").ClassList);
        Assert.DoesNotContain("anchored", cut.Find("[data-testid='diff-row-11']").ClassList);
        Assert.NotEmpty(cut.FindAll($"[data-testid='diff-pair-11'] ~ [data-testid='diff-threads-11'] [data-testid='thread-{thread.Id}']"));

        // Act: comment through the right cell of the same line
        await cut.Find("[data-testid='diff-comment-11']").ClickAsync();

        // Assert: the form is aimed at head line 11
        Assert.Equal("Comment on line 11 (after).", cut.Find("[data-testid='thread-target']").TextContent);
        Assert.Contains("selected", cut.Find("[data-testid='diff-row-11']").ClassList);
        Assert.DoesNotContain("selected", cut.Find("[data-testid='diff-row-old-11']").ClassList);
    }

    /// <summary>
    ///     Test that opening a diff whose first change is preceded by collapsed lines scrolls that change into view.
    /// </summary>
    [Fact]
    public async Task ReviewPage_FileSelect_ModifiedFile_ScrollsToFirstChange()
    {
        // Arrange and Act: open the modified file (its first change is row 9)
        var (_, cut) = await OpenModAsync();

        // Assert: the page asked the browser to bring that row to the middle of the window
        await cut.WaitForAssertionAsync(() =>
            Assert.Equal(["diff-row-9", "center"], JSInterop.Invocations["reviewCenter.scrollToTestId"][^1].Arguments));
    }

    /// <summary>
    ///     Test that unchanged lines away from a change are collapsed into a "Show N unchanged lines" button
    ///     and that the button expands them.
    /// </summary>
    [Fact]
    public async Task ReviewPage_DiffView_UnchangedRuns_AreCollapsedAndExpandOnRequest()
    {
        // Arrange and Act: open the modified file
        var (_, cut) = await OpenModAsync();

        // Assert: the file opens on the first change; the six unchanged lines before it and the four after are collapsed
        Assert.Equal("Show 6 unchanged lines", cut.Find("[data-testid='diff-gap-0']").TextContent);
        Assert.Equal("Show 4 unchanged lines", cut.Find("[data-testid='diff-gap-14']").TextContent);
        Assert.Empty(cut.FindAll("[data-testid='diff-row-0']"));
        Assert.NotEmpty(cut.FindAll("[data-testid='diff-row-6']"));
        Assert.Empty(cut.FindAll("[data-testid='diff-row-15']"));

        // Act: expand the first run
        await cut.Find("[data-testid='diff-gap-0']").ClickAsync();

        // Assert: the lines are shown and the button is gone; the other run is still collapsed
        Assert.Equal("l1", cut.Find("[data-testid='diff-text-0']").TextContent);
        Assert.Empty(cut.FindAll("[data-testid='diff-gap-0']"));
        Assert.NotEmpty(cut.FindAll("[data-testid='diff-gap-14']"));
    }

    /// <summary>
    ///     Test that a file whose text is identical but for the line endings says so instead of showing a diff.
    /// </summary>
    [Fact]
    public async Task ReviewPage_DiffView_OnlyLineEndingsDiffer_SaysSo()
    {
        // Arrange: a file that differs only in its line endings
        var projectId = await CreateReviewAsync(("src/eol.txt", ChangeKind.Modified, "a\nb\n", "a\r\nb\r\n"));
        var cut = RenderPage(projectId);

        // Act: open it
        await SelectFileAsync(cut, "src/eol.txt");

        // Assert: the summary names the reason, and there is nothing to comment on
        Assert.Equal(
            "No line changes; only line endings or the final newline differ.",
            cut.Find("[data-testid='diff-summary']").TextContent);
        Assert.Empty(cut.FindAll("[data-testid^='diff-comment-']"));
    }

    /// <summary>
    ///     Test that a file too large to compare shows a plain message and offers the Head and Baseline versions.
    /// </summary>
    [Fact]
    public async Task ReviewPage_FileSelect_TooLargeFile_OffersHeadAndBaseline()
    {
        // Arrange: a file with more lines than a diff accepts
        var big = string.Concat(Enumerable.Repeat("x\n", ReviewLimits.MaxDiffLines + 1));
        var projectId = await CreateReviewAsync(("src/big.txt", ChangeKind.Modified, big, "y\nz\n"));
        var cut = RenderPage(projectId);

        // Act: open it
        await SelectFileAsync(cut, "src/big.txt");

        // Assert: the message is plain, there are no diff rows, and both versions are offered
        Assert.Contains("Too large to compare line by line", cut.Find("[data-testid='diff-too-large']").TextContent, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("[data-testid='diff-view']"));

        // Act: show the head version
        await cut.Find("[data-testid='diff-too-large-head']").ClickAsync();

        // Assert: the head lines are shown
        Assert.Equal("y", cut.Find("[data-testid='line-text-1']").TextContent);

        // Act: back to the diff, then to the baseline
        await cut.Find("[data-testid='view-diff-button']").ClickAsync();
        await cut.Find("[data-testid='diff-too-large-baseline']").ClickAsync();

        // Assert: the baseline lines are shown
        Assert.Equal("x", cut.Find("[data-testid='baseline-line-text-1']").TextContent);
    }

    /// <summary>
    ///     Test that a binary file shows the notice instead of a diff.
    /// </summary>
    [Fact]
    public async Task ReviewPage_FileSelect_BinaryFile_ShowsNotice()
    {
        // Arrange: a file with a NUL byte in its baseline
        var projectId = await CreateReviewAsync(("src/data.bin", ChangeKind.Modified, "a\0b", "text\n"));
        var cut = RenderPage(projectId);

        // Act: open it
        await SelectFileAsync(cut, "src/data.bin");

        // Assert: only the notice is shown
        Assert.Contains("binary file", cut.Find("[data-testid='file-binary-notice']").TextContent, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("[data-testid='diff-view']"));
    }

    /// <summary>
    ///     Test that an uploaded review, and a repository review created without a baseline revision, keep the
    ///     head-only view without a selector or a diff.
    /// </summary>
    [Fact]
    public async Task ReviewPage_FileSelect_UploadAndNoBaselineReview_KeepHeadOnlyView()
    {
        // Arrange: an upload review and a repository review without a baseline commit, whose files are all added
        var cancellationToken = TestContext.Current.CancellationToken;
        var projectId = await CreateReviewAsync();
        await Reviews.CreateDraftAsync(projectId, "REV-2", "Upload", [new("a.txt", TestContent.A)], cancellationToken);
        var repository = (await Repositories.ListEnabledAsync(cancellationToken)).Single();
        Git.Script(null, HeadSha, [("src/new.txt", ChangeKind.Added, null, "new\n")]);
        await Reviews.CreateFromRepositoryAsync(
            projectId, "REV-3", "No baseline", repository.Id, "main", null, "src/**", TestActors.Ann, cancellationToken);

        // Act: open the uploaded file
        var upload = RenderPage(projectId, "REV-2");
        await upload.Find("[data-testid='file-select-0']").ClickAsync();

        // Assert: head only
        Assert.Empty(upload.FindAll("[data-testid='file-version-toggle']"));
        Assert.Empty(upload.FindAll("[data-testid='diff-view']"));
        Assert.NotEmpty(upload.FindAll("[data-testid='file-content']"));

        // Act: open the added file of the review that has no baseline commit
        var cut = RenderPage(projectId, "REV-3");
        await SelectFileAsync(cut, "src/new.txt");

        // Assert: head only
        Assert.Empty(cut.FindAll("[data-testid='file-version-toggle']"));
        Assert.Empty(cut.FindAll("[data-testid='diff-view']"));
        Assert.NotEmpty(cut.FindAll("[data-testid='file-content']"));
        Assert.Equal("new", cut.Find("[data-testid='line-text-1']").TextContent);
    }

    /// <summary>
    ///     Test that an added file of a review that has a baseline commit opens as a diff of added lines with a
    ///     Diff and a Head choice but no Baseline choice.
    /// </summary>
    [Fact]
    public async Task ReviewPage_FileSelect_AddedFileOfReviewWithBaseline_OpensAsAllAddedDiffWithDiffAndHeadChoice()
    {
        // Arrange: a review with an added file of two lines
        var projectId = await CreateReviewAsync(("src/new.txt", ChangeKind.Added, null, "new\nsecond\n"));
        var cut = RenderPage(projectId);

        // Act: open the added file
        await SelectFileAsync(cut, "src/new.txt");

        // Assert: the diff is the default view, with Diff and Head but no Baseline
        Assert.Equal("Added", cut.Find("[data-testid='selected-file-kind']").TextContent);
        Assert.Equal("true", cut.Find("[data-testid='view-diff-button']").GetAttribute("aria-pressed"));
        Assert.NotEmpty(cut.FindAll("[data-testid='view-head-button']"));
        Assert.Empty(cut.FindAll("[data-testid='view-baseline-button']"));

        // Assert: every line is added, with an empty left placeholder and no old numbers
        Assert.Equal("2 added, 0 removed", cut.Find("[data-testid='diff-summary']").TextContent);
        Assert.Equal("Click + to comment on a line.", cut.Find("[data-testid='diff-hint']").TextContent);
        Assert.Equal(["added", "added"], cut.FindAll("[data-testid^='diff-row-']").Select(e => e.GetAttribute("data-kind")).ToArray());
        Assert.Equal("new", cut.Find("[data-testid='diff-text-0']").TextContent);
        Assert.Equal("second", cut.Find("[data-testid='diff-text-1']").TextContent);
        Assert.NotEmpty(cut.FindAll("[data-testid='diff-empty-left-0']"));
        Assert.NotEmpty(cut.FindAll("[data-testid='diff-empty-left-1']"));
        Assert.Empty(cut.FindAll("[data-testid^='diff-old-no-']"));
        Assert.Empty(cut.FindAll("[data-testid^='diff-comment-old-']"));
        Assert.Empty(cut.FindAll("[data-testid='file-content']"));
    }

    /// <summary>
    ///     Test that an added file can be shown as its head lines with the comment controls and that the diff
    ///     can be chosen again, without ever offering the Baseline.
    /// </summary>
    [Fact]
    public async Task ReviewPage_FileSelect_AddedFile_HeadViewAndBack_ShowsNumberedLinesAndCommentControls()
    {
        // Arrange: the added file shown as a diff
        var projectId = await CreateReviewAsync(("src/new.txt", ChangeKind.Added, null, "new\nsecond\n"));
        var cut = RenderPage(projectId);
        await SelectFileAsync(cut, "src/new.txt");

        // Act: switch to the head
        await cut.Find("[data-testid='view-head-button']").ClickAsync();

        // Assert: the numbered head lines with comment controls and the form, no diff
        Assert.Equal("true", cut.Find("[data-testid='view-head-button']").GetAttribute("aria-pressed"));
        Assert.Equal("new", cut.Find("[data-testid='line-text-1']").TextContent);
        Assert.NotEmpty(cut.FindAll("[data-testid='line-comment-1']"));
        Assert.NotEmpty(cut.FindAll("[data-testid='new-thread-form']"));
        Assert.Empty(cut.FindAll("[data-testid='diff-view']"));
        Assert.Empty(cut.FindAll("[data-testid='view-baseline-button']"));

        // Act: back to the diff
        await cut.Find("[data-testid='view-diff-button']").ClickAsync();

        // Assert: the diff is back and still no Baseline is offered
        Assert.NotEmpty(cut.FindAll("[data-testid='diff-view']"));
        Assert.Empty(cut.FindAll("[data-testid='file-content']"));
        Assert.Empty(cut.FindAll("[data-testid='view-baseline-button']"));
    }

    /// <summary>
    ///     Test that a comment on a line of an added file's diff starts a head thread shown under that row.
    /// </summary>
    [Fact]
    public async Task ReviewPage_DiffView_AddedFile_CommentOnLine_StartsHeadThreadUnderTheRow()
    {
        // Arrange: the added file shown as a diff
        var projectId = await CreateReviewAsync(("src/new.txt", ChangeKind.Added, null, "new\nsecond\n"));
        var cut = RenderPage(projectId);
        await SelectFileAsync(cut, "src/new.txt");

        // Act: choose the plus of the second line and submit a comment
        await cut.Find("[data-testid='diff-comment-1']").ClickAsync();
        Assert.Equal("Comment on line 2 (after).", cut.Find("[data-testid='thread-target']").TextContent);
        await cut.Find("[data-testid='thread-text-input']").InputAsync("Why this?");
        await cut.Find("[data-testid='new-thread-form']").SubmitAsync();

        // Assert: a head thread on line 2, shown under the row, worded as "After"
        var thread = Assert.Single(await Threads.ListAsync(projectId, "REV-1", TestContext.Current.CancellationToken));
        Assert.Equal((2, 2, ThreadSide.Head), (thread.StartLine, thread.EndLine, thread.Side));
        Assert.NotEmpty(cut.FindAll($"[data-testid='diff-threads-1'] [data-testid='thread-{thread.Id}']"));
        Assert.Equal("After: line 2", cut.Find($"[data-testid='thread-anchor-{thread.Id}']").TextContent);
        Assert.Equal(
            ReviewLinks.ForThread(projectId, "REV-1", thread.Id),
            cut.Find($"[data-testid='thread-link-{thread.Id}']").GetAttribute("href"));
        Assert.Empty(cut.FindAll("[data-testid^='diff-comment-old-']"));
    }

    /// <summary>
    ///     Test that an added file too large to compare offers only the Head version.
    /// </summary>
    [Fact]
    public async Task ReviewPage_FileSelect_AddedFileTooLarge_OffersHeadOnly()
    {
        // Arrange: an added file with more lines than a diff accepts
        var big = string.Concat(Enumerable.Repeat("x\n", ReviewLimits.MaxDiffLines + 1));
        var projectId = await CreateReviewAsync(("src/big.txt", ChangeKind.Added, null, big));
        var cut = RenderPage(projectId);

        // Act: open it
        await SelectFileAsync(cut, "src/big.txt");

        // Assert: the message is shown and only the head is offered
        Assert.NotEmpty(cut.FindAll("[data-testid='diff-too-large']"));
        Assert.Empty(cut.FindAll("[data-testid='diff-view']"));
        Assert.NotEmpty(cut.FindAll("[data-testid='diff-too-large-head']"));
        Assert.Empty(cut.FindAll("[data-testid='diff-too-large-baseline']"));

        // Act: show the head version
        await cut.Find("[data-testid='diff-too-large-head']").ClickAsync();

        // Assert: the head lines are shown
        Assert.NotEmpty(cut.FindAll("[data-testid='file-content']"));
    }

    /// <summary>
    ///     Test that an added binary file shows the notice and offers no Baseline.
    /// </summary>
    [Fact]
    public async Task ReviewPage_FileSelect_AddedBinaryFile_ShowsNoticeAndOffersHeadOnly()
    {
        // Arrange: an added file with a NUL byte
        var projectId = await CreateReviewAsync(("src/data.bin", ChangeKind.Added, null, "a\0b"));
        var cut = RenderPage(projectId);

        // Act: open it
        await SelectFileAsync(cut, "src/data.bin");

        // Assert: the notice is shown, no diff, and no Baseline choice
        Assert.NotEmpty(cut.FindAll("[data-testid='file-binary-notice']"));
        Assert.Empty(cut.FindAll("[data-testid='diff-view']"));
        Assert.Empty(cut.FindAll("[data-testid='view-baseline-button']"));
    }

    /// <summary>
    ///     Test that an empty added file shows a diff without rows, and still accepts a whole-file comment.
    /// </summary>
    [Fact]
    public async Task ReviewPage_FileSelect_EmptyAddedFile_ShowsDiffWithoutRows()
    {
        // Arrange: an added file without content
        var projectId = await CreateReviewAsync(("src/empty.txt", ChangeKind.Added, null, string.Empty));
        var cut = RenderPage(projectId);

        // Act: open it
        await SelectFileAsync(cut, "src/empty.txt");

        // Assert: a diff without rows, and the form for the whole file
        Assert.Equal("No changes.", cut.Find("[data-testid='diff-summary']").TextContent);
        Assert.Empty(cut.FindAll("[data-testid^='diff-row-']"));
        Assert.NotEmpty(cut.FindAll("[data-testid='new-thread-form']"));
    }

    /// <summary>
    ///     Test that choosing a head thread of an added file in the index opens the diff at that row.
    /// </summary>
    [Fact]
    public async Task ReviewPage_ThreadIndex_AddedFileHeadThread_JumpsToTheDiffRow()
    {
        // Arrange: a head thread on line 2 of the added file
        var projectId = await CreateReviewAsync(("src/new.txt", ChangeKind.Added, null, "new\nsecond\n"));
        var thread = await StartAsync(projectId, 2, 2, ThreadSide.Head, "Look", "src/new.txt");
        var cut = RenderPage(projectId);

        // Assert: the index words it like any head thread of a file with a baseline
        Assert.Equal("After: line 2", cut.Find($"[data-testid='index-anchor-{thread.Id}']").TextContent);

        // Act: choose the thread
        await cut.Find($"[data-testid='index-row-{thread.Id}']").ClickAsync();

        // Assert: the diff is shown and scrolled to the row
        Assert.Equal("true", cut.Find("[data-testid='view-diff-button']").GetAttribute("aria-pressed"));
        await cut.WaitForAssertionAsync(() =>
            Assert.Equal(["diff-row-1", "center"], JSInterop.Invocations["reviewCenter.scrollToTestId"][^1].Arguments));
        Assert.Contains("focused", cut.Find("[data-testid='diff-row-1']").ClassList);
    }

    /// <summary>
    ///     Test that the plus control of a removed line comments on the baseline version with the baseline number.
    /// </summary>
    [Fact]
    public async Task ReviewPage_DiffView_CommentOnRemovedLine_StartsBaselineThread()
    {
        // Arrange: the diff of the modified file
        var (projectId, cut) = await OpenModAsync();

        // Act: choose the plus of the removed line
        await cut.Find("[data-testid='diff-comment-9']").ClickAsync();

        // Assert: the form opens under that line and names the version in plain words
        Assert.NotEmpty(cut.FindAll("[data-testid='diff-composer'] [data-testid='new-thread-form']"));
        Assert.Equal("Comment on line 10 (before).", cut.Find("[data-testid='thread-target']").TextContent);
        Assert.Contains("selected", cut.Find("[data-testid='diff-row-9']").ClassList);

        // Act: write and submit
        await cut.Find("[data-testid='thread-text-input']").InputAsync("Why drop this?");
        await cut.Find("[data-testid='new-thread-form']").SubmitAsync();

        // Assert: the thread is on the baseline side, shown under the line, and the form is closed
        var thread = Assert.Single(await Threads.ListAsync(projectId, "REV-1", TestContext.Current.CancellationToken));
        Assert.Equal(ThreadSide.Baseline, thread.Side);
        Assert.Equal(10, thread.StartLine);
        Assert.Equal(10, thread.EndLine);
        Assert.NotEmpty(cut.FindAll($"[data-testid='diff-threads-9'] [data-testid='thread-{thread.Id}']"));
        Assert.Equal("Before: line 10", cut.Find($"[data-testid='thread-anchor-{thread.Id}']").TextContent);
        Assert.Empty(cut.FindAll("[data-testid='diff-composer']"));
        Assert.Contains("anchored", cut.Find("[data-testid='diff-row-9']").ClassList);
    }

    /// <summary>
    ///     Test that the plus control of an added line and of an unchanged line comments on the head version
    ///     with the head number.
    /// </summary>
    [Fact]
    public async Task ReviewPage_DiffView_CommentOnAddedAndContextLines_StartsHeadThreads()
    {
        // Arrange: the diff of the modified file
        var (projectId, cut) = await OpenModAsync();

        // Act: comment on the added line
        await cut.Find("[data-testid='diff-comment-10']").ClickAsync();
        Assert.Equal("Comment on line 10 (after).", cut.Find("[data-testid='thread-target']").TextContent);
        await cut.Find("[data-testid='thread-text-input']").InputAsync("Nice");
        await cut.Find("[data-testid='new-thread-form']").SubmitAsync();

        // Act: comment on an unchanged line (row 7 is line 8)
        await cut.Find("[data-testid='diff-comment-7']").ClickAsync();
        Assert.Equal("Comment on line 8 (after).", cut.Find("[data-testid='thread-target']").TextContent);
        await cut.Find("[data-testid='thread-text-input']").InputAsync("Context");
        await cut.Find("[data-testid='new-thread-form']").SubmitAsync();

        // Assert: both are head threads with head numbers
        var threads = await Threads.ListAsync(projectId, "REV-1", TestContext.Current.CancellationToken);
        Assert.Equal(2, threads.Count);
        Assert.All(threads, t => Assert.Equal(ThreadSide.Head, t.Side));
        Assert.Equal([10, 8], threads.Select(t => t.StartLine!.Value).ToArray());
        Assert.Equal("After: line 10", cut.Find($"[data-testid='thread-anchor-{threads[0].Id}']").TextContent);
    }

    /// <summary>
    ///     Test that Shift extends the selection within one side only, and that a line of the other side
    ///     starts a new selection.
    /// </summary>
    [Fact]
    public async Task ReviewPage_DiffView_ShiftClick_ExtendsOnlyWithinTheSameSide()
    {
        // Arrange: the diff of the modified file, with line 11 selected
        var (projectId, cut) = await OpenModAsync();
        await cut.Find("[data-testid='diff-comment-11']").ClickAsync();

        // Act: shift-click a later line of the same side
        await cut.Find("[data-testid='diff-comment-13']").ClickAsync(new MouseEventArgs { ShiftKey = true });

        // Assert: a range of head lines, with the form under the last line
        Assert.Equal("Comment on lines 11-13 (after).", cut.Find("[data-testid='thread-target']").TextContent);
        Assert.NotEmpty(cut.FindAll("[data-testid='diff-row-12'].selected"));
        Assert.NotEmpty(cut.FindAll("[data-testid='diff-pair-13'] ~ .diff-composer"));

        // Act: shift-click the removed line, which belongs to the other version
        await cut.Find("[data-testid='diff-comment-9']").ClickAsync(new MouseEventArgs { ShiftKey = true });

        // Assert: the selection restarted on the baseline instead of mixing the two numberings
        Assert.Equal("Comment on line 10 (before).", cut.Find("[data-testid='thread-target']").TextContent);

        // Act: select a range on the head and submit it
        await cut.Find("[data-testid='diff-comment-11']").ClickAsync();
        await cut.Find("[data-testid='diff-comment-12']").ClickAsync(new MouseEventArgs { ShiftKey = true });
        await cut.Find("[data-testid='thread-text-input']").InputAsync("Range");
        await cut.Find("[data-testid='new-thread-form']").SubmitAsync();

        // Assert: one thread over both lines
        var thread = Assert.Single(await Threads.ListAsync(projectId, "REV-1", TestContext.Current.CancellationToken));
        Assert.Equal((11, 12, ThreadSide.Head), (thread.StartLine, thread.EndLine, thread.Side));
    }

    /// <summary>
    ///     Test that the optional "Through line" field extends the comment to a later line.
    /// </summary>
    [Fact]
    public async Task ReviewPage_DiffView_ThroughLineField_ExtendsTheRange()
    {
        // Arrange: line 11 selected
        var (projectId, cut) = await OpenModAsync();
        await cut.Find("[data-testid='diff-comment-11']").ClickAsync();

        // Act: type the last line and submit
        await cut.Find("[data-testid='thread-end-line-input']").InputAsync("13");
        Assert.Equal("Comment on lines 11-13 (after).", cut.Find("[data-testid='thread-target']").TextContent);
        await cut.Find("[data-testid='thread-text-input']").InputAsync("Three lines");
        await cut.Find("[data-testid='new-thread-form']").SubmitAsync();

        // Assert: one thread over the three lines
        var thread = Assert.Single(await Threads.ListAsync(projectId, "REV-1", TestContext.Current.CancellationToken));
        Assert.Equal((11, 13), (thread.StartLine, thread.EndLine));
    }

    /// <summary>
    ///     Test that the form under a line can be cancelled and can be turned into a whole-file comment
    ///     that keeps the text typed so far.
    /// </summary>
    [Fact]
    public async Task ReviewPage_DiffView_CancelAndWholeFileOption_BehaveAsLabelled()
    {
        // Arrange: a line selected and some text typed
        var (projectId, cut) = await OpenModAsync();
        await cut.Find("[data-testid='diff-comment-11']").ClickAsync();
        await cut.Find("[data-testid='thread-text-input']").InputAsync("Typed");

        // Act: cancel
        await cut.Find("[data-testid='thread-cancel-button']").ClickAsync();

        // Assert: the inline form is gone and the selection with it
        Assert.Empty(cut.FindAll("[data-testid='diff-composer']"));
        Assert.Empty(cut.FindAll("[data-testid='diff-row-11'].selected"));

        // Act: select again, type, and switch to the whole file
        await cut.Find("[data-testid='diff-comment-11']").ClickAsync();
        await cut.Find("[data-testid='thread-text-input']").InputAsync("Whole");
        await cut.Find("[data-testid='thread-whole-file-button']").ClickAsync();

        // Assert: the form moved below the diff, kept the text, and no line is selected
        Assert.Empty(cut.FindAll("[data-testid='diff-composer']"));
        Assert.Equal("Whole", cut.Find("[data-testid='thread-text-input']").GetAttribute("value"));
        Assert.Equal("Comment on the whole file.", cut.Find("[data-testid='thread-target']").TextContent);

        // Act: submit
        await cut.Find("[data-testid='new-thread-form']").SubmitAsync();

        // Assert: the thread is anchored to the file, and listed below the diff
        var thread = Assert.Single(await Threads.ListAsync(projectId, "REV-1", TestContext.Current.CancellationToken));
        Assert.Null(thread.StartLine);
        Assert.NotEmpty(cut.FindAll($"[data-testid='threads-list'] [data-testid='thread-{thread.Id}']"));
        Assert.Equal("Whole file", cut.Find($"[data-testid='thread-anchor-{thread.Id}']").TextContent);
    }

    /// <summary>
    ///     Test that existing threads are drawn under the last line of their anchor on the right side, with the
    ///     lines they cover highlighted, even where the lines would otherwise be collapsed.
    /// </summary>
    [Fact]
    public async Task ReviewPage_DiffView_ExistingThreads_AreShownUnderTheirLines()
    {
        // Arrange: a head range inside the diff, a baseline line, and a head line inside a collapsed run
        var projectId = await CreateReviewAsync();
        var range = await StartAsync(projectId, 11, 12, ThreadSide.Head, "Range");
        var removed = await StartAsync(projectId, 10, 10, ThreadSide.Baseline, "Removed");
        var hidden = await StartAsync(projectId, 2, 2, ThreadSide.Head, "Far from changes");

        // Act: open the file
        var cut = RenderPage(projectId);
        await SelectFileAsync(cut, ModPath);

        // Assert: each thread is under the last row of its anchor, rows it covers are marked
        Assert.NotEmpty(cut.FindAll($"[data-testid='diff-threads-12'] [data-testid='thread-{range.Id}']"));
        Assert.NotEmpty(cut.FindAll($"[data-testid='diff-threads-9'] [data-testid='thread-{removed.Id}']"));
        Assert.Contains("anchored", cut.Find("[data-testid='diff-row-11']").ClassList);
        Assert.Contains("anchored", cut.Find("[data-testid='diff-row-12']").ClassList);

        // Assert: a thread inside a collapsed run keeps its line visible
        Assert.NotEmpty(cut.FindAll($"[data-testid='diff-threads-1'] [data-testid='thread-{hidden.Id}']"));
        Assert.Equal("l2", cut.Find("[data-testid='diff-text-1']").TextContent);

        // Assert: none is repeated in a list below the diff
        Assert.Empty(cut.FindAll("[data-testid='threads-list']"));
    }

    /// <summary>
    ///     Test that a thread covering several lines keeps its first line visible, even when that line would
    ///     otherwise be collapsed, because the thread index and links scroll to the first line.
    /// </summary>
    [Fact]
    public async Task ReviewPage_DiffView_MultiLineThread_KeepsItsFirstLineVisible()
    {
        // Arrange: a head range whose first line (2) is far from the changes and whose last line (12) is not
        var projectId = await CreateReviewAsync();
        var range = await StartAsync(projectId, 2, 12, ThreadSide.Head, "Long range");

        // Act: open the file
        var cut = RenderPage(projectId);
        await SelectFileAsync(cut, ModPath);

        // Assert: the first line is rendered (so a scroll to it can succeed), and the thread is under the last line
        Assert.Equal("l2", cut.Find("[data-testid='diff-text-1']").TextContent);
        Assert.NotEmpty(cut.FindAll($"[data-testid='diff-threads-12'] [data-testid='thread-{range.Id}']"));
    }

    /// <summary>
    ///     Test that a resolved thread under a line is collapsed, can be expanded, and that replying, resolving
    ///     and reopening work there as they do in the list.
    /// </summary>
    [Fact]
    public async Task ReviewPage_DiffView_InlineThread_ReplyResolveAndReopen()
    {
        // Arrange: a resolved and an open thread on the diff
        var projectId = await CreateReviewAsync();
        var open = await StartAsync(projectId, 11, 11, ThreadSide.Head, "Open one");
        var done = await StartAsync(projectId, 10, 10, ThreadSide.Baseline, "Done one");
        await Threads.ResolveAsync(projectId, "REV-1", done.Id, TestContext.Current.CancellationToken);
        var cut = RenderPage(projectId);
        await SelectFileAsync(cut, ModPath);

        // Assert: the resolved thread is one line until expanded
        Assert.NotEmpty(cut.FindAll($"[data-testid='thread-expand-{done.Id}']"));
        Assert.Empty(cut.FindAll($"[data-testid='reply-text-{done.Id}']"));

        // Act: expand it and reopen it
        await cut.Find($"[data-testid='thread-expand-{done.Id}']").ClickAsync();
        Assert.Equal("Done one", cut.Find($"[data-testid='comment-text-{done.Id}-1']").TextContent);
        await cut.Find($"[data-testid='thread-toggle-{done.Id}']").ClickAsync();

        // Assert: it is open again
        Assert.Equal("Open", cut.Find($"[data-testid='thread-state-{done.Id}']").TextContent);

        // Act: reply to the other thread and resolve it
        await cut.Find($"[data-testid='reply-text-{open.Id}']").InputAsync("A reply");
        await cut.Find($"[data-testid='reply-button-{open.Id}']").ClickAsync();
        Assert.Equal("A reply", cut.Find($"[data-testid='comment-text-{open.Id}-2']").TextContent);
        await cut.Find($"[data-testid='thread-toggle-{open.Id}']").ClickAsync();

        // Assert: it is resolved and collapsed
        Assert.Equal("Resolved", cut.Find($"[data-testid='thread-state-{open.Id}']").TextContent);
        Assert.Empty(cut.FindAll($"[data-testid='reply-text-{open.Id}']"));
    }

    /// <summary>
    ///     Test that the thread index tells the version and lines of a thread and jumps to the diff location,
    ///     also from the Head or Baseline view.
    /// </summary>
    [Fact]
    public async Task ReviewPage_ThreadIndex_ShowsSideAndLinesAndJumpsToTheDiff()
    {
        // Arrange: a baseline and a head thread
        var projectId = await CreateReviewAsync();
        var before = await StartAsync(projectId, 10, 10, ThreadSide.Baseline, "Before");
        var after = await StartAsync(projectId, 11, 13, ThreadSide.Head, "After");
        var whole = await Threads.StartThreadAsync(
            projectId, "REV-1", ModPath, null, null, TestActors.Bob, "Whole", TestContext.Current.CancellationToken);
        var cut = RenderPage(projectId);

        // Assert: the index words the version
        Assert.Equal("Before: line 10", cut.Find($"[data-testid='index-anchor-{before.Id}']").TextContent);
        Assert.Equal("After: lines 11-13", cut.Find($"[data-testid='index-anchor-{after.Id}']").TextContent);
        Assert.Equal("Whole file", cut.Find($"[data-testid='index-anchor-{whole.Id}']").TextContent);

        // Act: choose the baseline thread
        await cut.Find($"[data-testid='index-row-{before.Id}']").ClickAsync();

        // Assert: the diff is shown and scrolled to the removed row
        Assert.Equal("true", cut.Find("[data-testid='view-diff-button']").GetAttribute("aria-pressed"));
        await cut.WaitForAssertionAsync(() =>
            Assert.Equal(["diff-row-9", "center"], JSInterop.Invocations["reviewCenter.scrollToTestId"][^1].Arguments));

        // Act: switch to the head view and choose the head thread
        await cut.Find("[data-testid='view-head-button']").ClickAsync();
        await cut.Find($"[data-testid='index-row-{after.Id}']").ClickAsync();

        // Assert: back in the diff, at the first line of the range
        Assert.Equal("true", cut.Find("[data-testid='view-diff-button']").GetAttribute("aria-pressed"));
        await cut.WaitForAssertionAsync(() =>
            Assert.Equal(["diff-row-11", "center"], JSInterop.Invocations["reviewCenter.scrollToTestId"][^1].Arguments));
        Assert.Contains("focused", cut.Find("[data-testid='diff-row-12']").ClassList);

        // Act: choose the whole-file thread
        await cut.Find($"[data-testid='index-row-{whole.Id}']").ClickAsync();

        // Assert: it is brought into view
        await cut.WaitForAssertionAsync(() =>
            Assert.Equal([$"thread-{whole.Id}", "center"], JSInterop.Invocations["reviewCenter.scrollToTestId"][^1].Arguments));
    }

    /// <summary>
    ///     Test that on a file too large to compare the index opens the version a thread belongs to.
    /// </summary>
    [Fact]
    public async Task ReviewPage_ThreadIndex_TooLargeFile_OpensTheVersionOfTheThread()
    {
        // Arrange: a file too large to compare, with a baseline and a head thread
        var big = string.Concat(Enumerable.Repeat("x\n", ReviewLimits.MaxDiffLines + 1));
        var projectId = await CreateReviewAsync(("src/big.txt", ChangeKind.Modified, big, "y\nz\n"));
        var before = await StartAsync(projectId, 9000, 9000, ThreadSide.Baseline, "Before", "src/big.txt");
        var after = await StartAsync(projectId, 2, 2, ThreadSide.Head, "After", "src/big.txt");
        var cut = RenderPage(projectId);

        // Act: choose the baseline thread
        await cut.Find($"[data-testid='index-row-{before.Id}']").ClickAsync();

        // Assert: the baseline text is shown at that line
        Assert.NotEmpty(cut.FindAll("[data-testid='baseline-content']"));
        await cut.WaitForAssertionAsync(() =>
            Assert.Equal(["baseline-line-9000", "center"], JSInterop.Invocations["reviewCenter.scrollToTestId"][^1].Arguments));

        // Act: choose the head thread
        await cut.Find($"[data-testid='index-row-{after.Id}']").ClickAsync();

        // Assert: the head text is shown at that line
        Assert.NotEmpty(cut.FindAll("[data-testid='file-content']"));
        await cut.WaitForAssertionAsync(() =>
            Assert.Equal(["code-line-2", "center"], JSInterop.Invocations["reviewCenter.scrollToTestId"][^1].Arguments));
    }

    /// <summary>
    ///     Test that choosing a baseline thread in the index highlights the baseline lines it covers, and only those,
    ///     on a file whose baseline is shown alone; a head thread of the same numbers highlights nothing there.
    /// </summary>
    [Fact]
    public async Task ReviewPage_ThreadIndex_BaselineThreadOnTooLargeFile_HighlightsItsBaselineLines()
    {
        // Arrange: a file too large to compare, a baseline thread over lines 9000-9002 and a head thread on line 2
        var big = string.Concat(Enumerable.Repeat("x\n", ReviewLimits.MaxDiffLines + 1));
        var projectId = await CreateReviewAsync(("src/big.txt", ChangeKind.Modified, big, "y\nz\n"));
        var before = await StartAsync(projectId, 9000, 9002, ThreadSide.Baseline, "Before", "src/big.txt");
        var after = await StartAsync(projectId, 2, 2, ThreadSide.Head, "After", "src/big.txt");
        var cut = RenderPage(projectId);

        // Act: choose the baseline thread
        await cut.Find($"[data-testid='index-row-{before.Id}']").ClickAsync();

        // Assert: the lines of the thread, and no others, are highlighted in the baseline text
        Assert.Equal(
            ["baseline-line-9000", "baseline-line-9001", "baseline-line-9002"],
            cut.FindAll("[data-testid='baseline-content'] .code-line.focused").Select(e => e.GetAttribute("data-testid")).ToArray());

        // Act: choose the head thread
        await cut.Find($"[data-testid='index-row-{after.Id}']").ClickAsync();

        // Assert: the head line is highlighted in the head text
        Assert.Equal(
            ["code-line-2"],
            cut.FindAll("[data-testid='file-content'] .code-line.focused").Select(e => e.GetAttribute("data-testid")).ToArray());
    }

    /// <summary>
    ///     Test that the highlight of a chosen thread follows its side: with a baseline thread chosen, the head
    ///     lines of the same numbers are not highlighted, and with a head thread chosen, the baseline lines are not.
    /// </summary>
    [Fact]
    public async Task ReviewPage_ThreadIndex_ThreadHighlight_StaysOnTheSideOfTheThread()
    {
        // Arrange: a file too large to compare, with a baseline and a head thread on line 2
        var big = string.Concat(Enumerable.Repeat("x\n", ReviewLimits.MaxDiffLines + 1));
        var projectId = await CreateReviewAsync(("src/big.txt", ChangeKind.Modified, big, "y\nz\n"));
        var before = await StartAsync(projectId, 2, 2, ThreadSide.Baseline, "Before", "src/big.txt");
        var after = await StartAsync(projectId, 2, 2, ThreadSide.Head, "After", "src/big.txt");
        var cut = RenderPage(projectId);

        // Act: choose the baseline thread, then look at the head version
        await cut.Find($"[data-testid='index-row-{before.Id}']").ClickAsync();
        Assert.NotEmpty(cut.FindAll("[data-testid='baseline-line-2'].focused"));
        await cut.Find("[data-testid='view-head-button']").ClickAsync();

        // Assert: the head line 2 is not highlighted for a baseline thread
        Assert.Empty(cut.FindAll("[data-testid='code-line-2'].focused"));

        // Act: choose the head thread, then look at the baseline version
        await cut.Find($"[data-testid='index-row-{after.Id}']").ClickAsync();
        Assert.NotEmpty(cut.FindAll("[data-testid='code-line-2'].focused"));
        await cut.Find("[data-testid='view-baseline-button']").ClickAsync();

        // Assert: the baseline line 2 is not highlighted for a head thread
        Assert.Empty(cut.FindAll("[data-testid='baseline-content'] .code-line.focused"));
    }

    /// <summary>
    ///     Test that the file list keeps counting the open threads of each file, whichever side they are on.
    /// </summary>
    [Fact]
    public async Task ReviewPage_FileList_ThreadCounts_IncludeBothSides()
    {
        // Arrange: one thread on each side of the modified file, one resolved
        var projectId = await CreateReviewAsync();
        await StartAsync(projectId, 10, 10, ThreadSide.Baseline, "Before");
        var after = await StartAsync(projectId, 11, 11, ThreadSide.Head, "After");
        await StartAsync(projectId, 12, 12, ThreadSide.Head, "Another");
        await Threads.ResolveAsync(projectId, "REV-1", after.Id, TestContext.Current.CancellationToken);

        // Act: render the page
        var cut = RenderPage(projectId);

        // Assert: the two open threads are counted
        Assert.Equal("2 open threads", cut.Find("[data-testid='file-open-threads-0']").TextContent);
    }

    /// <summary>
    ///     Test that switching between Diff and Head keeps the text typed so far but drops a line selection
    ///     whose numbering no longer applies.
    /// </summary>
    [Fact]
    public async Task ReviewPage_ViewSwitch_KeepsTextAndClearsTheSelection()
    {
        // Arrange: a removed line selected and text typed
        var (_, cut) = await OpenModAsync();
        await cut.Find("[data-testid='diff-comment-9']").ClickAsync();
        await cut.Find("[data-testid='thread-text-input']").InputAsync("Keep me");

        // Act: switch to the head view
        await cut.Find("[data-testid='view-head-button']").ClickAsync();

        // Assert: the text is kept, the lines are blank, and the form comments on the whole file
        Assert.Equal("Keep me", cut.Find("[data-testid='thread-text-input']").GetAttribute("value"));
        Assert.Equal(string.Empty, cut.Find("[data-testid='thread-start-line-input']").GetAttribute("value") ?? string.Empty);
        Assert.Equal("Comment on the whole file.", cut.Find("[data-testid='thread-target']").TextContent);
    }
}
