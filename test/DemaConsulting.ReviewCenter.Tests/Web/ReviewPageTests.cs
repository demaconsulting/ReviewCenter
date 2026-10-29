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

using System.Text;
using Bunit;
using DemaConsulting.ReviewCenter.Core;
using DemaConsulting.ReviewCenter.Evidence;
using DemaConsulting.ReviewCenter.Tests.Core;
using DemaConsulting.ReviewCenter.Tests.Evidence;
using DemaConsulting.ReviewCenter.Web;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

using TestContext = Xunit.TestContext;

namespace DemaConsulting.ReviewCenter.Tests.Web;

/// <summary>
///     Unit tests for the <see cref="ReviewPage"/> component, rendered in memory with bUnit.
/// </summary>
public class ReviewPageTests : PageTestBase
{
    /// <summary>
    ///     Creates a project with one review holding the given files; every user that exists is a member.
    /// </summary>
    /// <param name="files">The files of the review, in the order they should be listed.</param>
    /// <returns>The project id.</returns>
    private async Task<int> CreateReviewAsync(params UploadedFile[] files)
    {
        var project = await Projects.CreateAsync("Apollo", null, TestContext.Current.CancellationToken);
        await AddMembersAsync(project.Id);
        await Reviews.CreateDraftAsync(project.Id, "REV-1", "First review", files, TestContext.Current.CancellationToken);
        return project.Id;
    }

    /// <summary>
    ///     Makes every user of the store a member of the project, so that the users can occupy slots and sign.
    /// </summary>
    /// <param name="projectId">The project identity.</param>
    /// <returns>A task that completes when the memberships exist.</returns>
    private async Task AddMembersAsync(int projectId)
    {
        foreach (var user in await UserStore.ListAsync(TestContext.Current.CancellationToken))
        {
            await Store.AddMembershipAsync(user.Id, projectId, TestContext.Current.CancellationToken);
        }
    }

    /// <summary>
    ///     Renders the page for a review.
    /// </summary>
    /// <param name="projectId">The project identity.</param>
    /// <param name="reviewId">The review id.</param>
    /// <returns>The rendered component.</returns>
    private IRenderedComponent<ReviewPage> RenderPage(int projectId, string reviewId = "REV-1")
    {
        // File and thread selection ask the browser to scroll; the tests only check what was asked
        JSInterop.Mode = JSRuntimeMode.Loose;
        return Render<ReviewPage>(parameters => parameters
            .Add(page => page.ProjectId, projectId)
            .Add(page => page.ReviewId, reviewId));
    }

    /// <summary>
    ///     Test that an unknown review shows a not-found message and no details.
    /// </summary>
    [Fact]
    public async Task ReviewPage_Render_UnknownReview_ShowsNotFound()
    {
        // Arrange: a project without that review
        var project = await Projects.CreateAsync("Apollo", null, TestContext.Current.CancellationToken);

        // Act: render the page for a review that does not exist
        var cut = RenderPage(project.Id, "NOPE");

        // Assert: not-found names the review and no details are shown
        Assert.Contains("NOPE", cut.Find("[data-testid='review-not-found']").TextContent, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("[data-testid='review-fingerprint']"));
    }

    /// <summary>
    ///     Test that the title row links to the history of the review.
    /// </summary>
    [Fact]
    public async Task ReviewPage_Render_ExistingReview_ShowsHistoryLink()
    {
        // Arrange
        var projectId = await CreateReviewAsync(new UploadedFile("a.txt", TestContent.A));

        // Act
        var cut = RenderPage(projectId);

        // Assert
        Assert.Equal($"/projects/{projectId}/reviews/REV-1/history", cut.Find("[data-testid='history-link']").GetAttribute("href"));
    }

    /// <summary>
    ///     Test that the summary strip shows the checklist progress, the reviewer state and the thread counts.
    /// </summary>
    [Fact]
    public async Task ReviewPage_Render_ExistingReview_ShowsSummaryStripWithChecklistReviewersAndThreads()
    {
        // Arrange: a review without answers, slots or threads
        var projectId = await CreateReviewAsync(new UploadedFile("a.txt", TestContent.A));

        // Act: render the page
        var cut = RenderPage(projectId);

        // Assert: the strip names the checklist link, the empty reviewer state and zero thread counts
        var strip = cut.Find("[data-testid='review-summary']");
        Assert.NotNull(strip.QuerySelector("[data-testid='checklist-progress-summary']"));
        Assert.Equal($"/projects/{projectId}/reviews/REV-1/checklist", strip.QuerySelector("[data-testid='checklist-link']")!.GetAttribute("href"));
        Assert.Contains("No reviewer slots yet", strip.QuerySelector("[data-testid='summary-reviewers']")!.TextContent, StringComparison.Ordinal);
        Assert.Contains("0 open", strip.QuerySelector("[data-testid='summary-threads']")!.TextContent, StringComparison.Ordinal);
        Assert.Contains("0 resolved", strip.QuerySelector("[data-testid='summary-threads']")!.TextContent, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that the review's id, title, state and fingerprint are shown.
    /// </summary>
    [Fact]
    public async Task ReviewPage_Render_ExistingReview_ShowsIdTitleStateAndFingerprint()
    {
        // Arrange: a review of three files with a known fingerprint
        var projectId = await CreateReviewAsync(
            new UploadedFile("a.txt", TestContent.A),
            new UploadedFile("b.txt", TestContent.B),
            new UploadedFile("c.bin", TestContent.C));

        // Act: render the page
        var cut = RenderPage(projectId);

        // Assert: every header value is shown
        Assert.Equal("First review", cut.Find("[data-testid='review-title']").TextContent);
        Assert.Equal("REV-1", cut.Find("[data-testid='review-id']").TextContent);
        Assert.Equal("Draft", cut.Find("[data-testid='review-state']").TextContent);
        Assert.Equal(TestContent.FingerprintABC, cut.Find("[data-testid='review-fingerprint']").TextContent);
    }

    /// <summary>
    ///     Test that every file is listed with its path, size and SHA-256 hash.
    /// </summary>
    [Fact]
    public async Task ReviewPage_Render_ExistingReview_ListsFilesWithPathSizeAndHash()
    {
        // Arrange: a review of two files
        var projectId = await CreateReviewAsync(
            new UploadedFile("a.txt", TestContent.A),
            new UploadedFile("c.bin", TestContent.C));

        // Act: render the page
        var cut = RenderPage(projectId);

        // Assert: path, size and hash of each file
        Assert.Equal("a.txt", cut.Find("[data-testid='file-path-0']").TextContent);
        Assert.Equal(TestContent.A.Length.ToString(System.Globalization.CultureInfo.InvariantCulture), cut.Find("[data-testid='file-size-0']").TextContent);
        Assert.Equal(TestContent.HashA, cut.Find("[data-testid='file-hash-0']").TextContent);
        Assert.Equal("c.bin", cut.Find("[data-testid='file-path-1']").TextContent);
        Assert.Equal(TestContent.HashC, cut.Find("[data-testid='file-hash-1']").TextContent);
    }

    /// <summary>
    ///     Test that the breadcrumb names the owning project and the selected file's row is highlighted.
    /// </summary>
    [Fact]
    public async Task ReviewPage_Render_FileSelected_ShowsProjectNameAndHighlightsRow()
    {
        // Arrange: a review of two files in the project named "Apollo"
        var projectId = await CreateReviewAsync(
            new UploadedFile("a.txt", TestContent.A),
            new UploadedFile("b.txt", TestContent.B));
        var cut = RenderPage(projectId);

        // Act: select the second file
        await cut.Find("[data-testid='file-select-1']").ClickAsync();

        // Assert: the breadcrumb shows the project name and only the selected row is highlighted
        Assert.Equal("Apollo", cut.Find("[data-testid='project-link']").TextContent);
        Assert.DoesNotContain("selected", cut.Find("[data-testid='file-row-0']").ClassList);
        Assert.Contains("selected", cut.Find("[data-testid='file-row-1']").ClassList);
    }

    /// <summary>
    ///     Test that no file content is shown until a file is selected.
    /// </summary>
    [Fact]
    public async Task ReviewPage_Render_NoFileSelected_ShowsNoContent()
    {
        // Arrange: a review with one file
        var projectId = await CreateReviewAsync(new UploadedFile("a.txt", TestContent.A));

        // Act: render the page
        var cut = RenderPage(projectId);

        // Assert: neither content nor binary notice is shown
        Assert.Empty(cut.FindAll("[data-testid='file-content']"));
        Assert.Empty(cut.FindAll("[data-testid='file-binary-notice']"));
    }

    /// <summary>
    ///     Test that selecting a text file shows each line, numbered, with its exact text as plain text.
    /// </summary>
    [Fact]
    public async Task ReviewPage_FileSelect_TextFile_ShowsNumberedLinesWithExactText()
    {
        // Arrange: a review with a CRLF text file that has markup-like characters and an indented line
        var text = "line one\r\n  <b>indented & raw</b>\r\nline three\r\n";
        var projectId = await CreateReviewAsync(new UploadedFile("a.txt", Encoding.UTF8.GetBytes(text)));
        var cut = RenderPage(projectId);

        // Act: select the file
        await cut.Find("[data-testid='file-select-0']").ClickAsync();

        // Assert: three numbered lines without terminators, the markup shown as text, and the path
        Assert.Equal(3, cut.FindAll("[data-testid^='code-line-']").Count);
        Assert.Equal("line one", cut.Find("[data-testid='line-text-1']").TextContent);
        Assert.Equal("  <b>indented & raw</b>", cut.Find("[data-testid='line-text-2']").TextContent);
        Assert.Empty(cut.Find("[data-testid='line-text-2']").QuerySelectorAll("b"));
        Assert.Equal("3", cut.Find("[data-testid='line-no-3']").TextContent);
        Assert.Equal("a.txt", cut.Find("[data-testid='selected-file-path']").TextContent);
        Assert.Empty(cut.FindAll("[data-testid='file-binary-notice']"));
    }

    /// <summary>
    ///     Test that a carriage return is dropped only as part of a CRLF terminator, not at the very end of the file.
    /// </summary>
    [Fact]
    public async Task ReviewPage_FileSelect_TrailingLoneCarriageReturn_IsKept()
    {
        // Arrange: a file whose last line ends in a lone carriage return rather than a terminator
        var projectId = await CreateReviewAsync(new UploadedFile("a.txt", Encoding.UTF8.GetBytes("one\r\ntwo\r")));
        var cut = RenderPage(projectId);

        // Act: select the file
        await cut.Find("[data-testid='file-select-0']").ClickAsync();

        // Assert: the CRLF line is clean and the unterminated last line keeps its carriage return
        // (the HTML parser of the test renderer reports a kept carriage return as a line feed)
        Assert.Equal("one", cut.Find("[data-testid='line-text-1']").TextContent);
        Assert.Equal("two\n", cut.Find("[data-testid='line-text-2']").TextContent);
    }

    /// <summary>
    ///     Test that selecting a binary file shows the binary notice instead of content.
    /// </summary>
    [Fact]
    public async Task ReviewPage_FileSelect_BinaryFile_ShowsBinaryNoticeAndNoContent()
    {
        // Arrange: a review with a binary file
        var projectId = await CreateReviewAsync(new UploadedFile("c.bin", TestContent.C));
        var cut = RenderPage(projectId);

        // Act: select the file
        await cut.Find("[data-testid='file-select-0']").ClickAsync();

        // Assert: the notice is shown, the content is not, and the path is shown
        Assert.Contains("binary", cut.Find("[data-testid='file-binary-notice']").TextContent, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(cut.FindAll("[data-testid='file-content']"));
        Assert.Equal("c.bin", cut.Find("[data-testid='selected-file-path']").TextContent);
    }

    /// <summary>
    ///     Test that text which is not valid UTF-8 (without a NUL byte) is treated as binary.
    /// </summary>
    [Fact]
    public async Task ReviewPage_FileSelect_InvalidUtf8_ShowsBinaryNotice()
    {
        // Arrange: a file with a lone continuation byte
        var projectId = await CreateReviewAsync(new UploadedFile("latin1.txt", [0x63, 0x61, 0x66, 0xe9, 0x0a]));
        var cut = RenderPage(projectId);

        // Act: select the file
        await cut.Find("[data-testid='file-select-0']").ClickAsync();

        // Assert: shown as binary
        Assert.NotEmpty(cut.FindAll("[data-testid='file-binary-notice']"));
    }

    /// <summary>
    ///     Test that an empty file shows an empty-file notice without any lines rather than the binary notice.
    /// </summary>
    [Fact]
    public async Task ReviewPage_FileSelect_EmptyFile_ShowsNoLines()
    {
        // Arrange: a review with an empty file
        var projectId = await CreateReviewAsync(new UploadedFile("empty.txt", []));
        var cut = RenderPage(projectId);

        // Act: select the file
        await cut.Find("[data-testid='file-select-0']").ClickAsync();

        // Assert: the empty-file notice, no lines, no binary notice
        Assert.NotEmpty(cut.FindAll("[data-testid='file-empty-notice']"));
        Assert.Empty(cut.FindAll("[data-testid='file-content']"));
        Assert.Empty(cut.FindAll("[data-testid='code-line-1']"));
        Assert.Empty(cut.FindAll("[data-testid='file-binary-notice']"));
    }

    /// <summary>
    ///     Test that selecting another file replaces the shown content.
    /// </summary>
    [Fact]
    public async Task ReviewPage_FileSelect_SecondFile_ReplacesContent()
    {
        // Arrange: a review with two text files, the first one shown
        var projectId = await CreateReviewAsync(new UploadedFile("a.txt", TestContent.A), new UploadedFile("b.txt", TestContent.B));
        var cut = RenderPage(projectId);
        await cut.Find("[data-testid='file-select-0']").ClickAsync();

        // Act: select the second file
        await cut.Find("[data-testid='file-select-1']").ClickAsync();

        // Assert: the second file is shown
        Assert.Equal("world", cut.Find("[data-testid='line-text-1']").TextContent);
        Assert.Equal("b.txt", cut.Find("[data-testid='selected-file-path']").TextContent);
    }

    /// <summary>
    ///     Test that navigating to another review replaces the shown review and clears the selected file and
    ///     everything typed for threads.
    /// </summary>
    [Fact]
    public async Task ReviewPage_ParametersChange_OtherReview_ShowsNewReviewAndClearsSelection()
    {
        // Arrange: a page showing REV-1 with its file selected, a thread, typed text and an error, and a second review
        var projectId = await CreateReviewAsync(new UploadedFile("a.txt", TestContent.A));
        await Reviews.CreateDraftAsync(projectId, "REV-2", "Second review", [new("b.txt", TestContent.B)], TestContext.Current.CancellationToken);
        await Threads.StartThreadAsync(projectId, "REV-1", "a.txt", null, null, TestActors.Ann, "Hello", TestContext.Current.CancellationToken);
        var cut = RenderPage(projectId);
        await cut.Find("[data-testid='file-select-0']").ClickAsync();
        await cut.Find("[data-testid='thread-text-input']").InputAsync("draft text");
        await cut.Find("[data-testid='thread-start-line-input']").InputAsync("abc");
        await cut.Find("[data-testid='new-thread-form']").SubmitAsync();
        Assert.NotEmpty(cut.FindAll("[data-testid='thread-error']"));

        // Act: the router reuses the component for the other review
        await cut.InvokeAsync(() => cut.Instance.SetParametersAsync(ParameterView.FromDictionary(
            new Dictionary<string, object?> { [nameof(ReviewPage.ProjectId)] = projectId, [nameof(ReviewPage.ReviewId)] = "REV-2" })));

        // Assert: the second review is shown and nothing from the first one remains
        await cut.WaitForAssertionAsync(() => Assert.Equal("Second review", cut.Find("[data-testid='review-title']").TextContent));
        Assert.Equal("b.txt", cut.Find("[data-testid='file-path-0']").TextContent);
        Assert.Empty(cut.FindAll("[data-testid='file-content']"));
        Assert.Empty(cut.FindAll("[data-testid='thread-error']"));
        Assert.Empty(cut.FindAll("[data-testid='threads-list']"));
        Assert.Equal("0", cut.Find("[data-testid='threads-open-count']").TextContent);

        // Assert: selecting the file of the new review shows an empty form
        await cut.Find("[data-testid='file-select-0']").ClickAsync();
        Assert.Equal(string.Empty, cut.Find("[data-testid='thread-text-input']").GetAttribute("value") ?? string.Empty);
        Assert.Empty(cut.FindAll("[data-testid='thread-error']"));
    }

    /// <summary>
    ///     Test that every file button carries an accessible name that identifies its file.
    /// </summary>
    [Fact]
    public async Task ReviewPage_Render_ExistingReview_FileButtonsNameTheirFile()
    {
        // Arrange: a review of two files
        var projectId = await CreateReviewAsync(new UploadedFile("a.txt", TestContent.A), new UploadedFile("b.txt", TestContent.B));

        // Act: render the page
        var cut = RenderPage(projectId);

        // Assert: each button's accessible name includes its file path
        Assert.Equal("View a.txt", cut.Find("[data-testid='file-select-0']").GetAttribute("aria-label"));
        Assert.Equal("View b.txt", cut.Find("[data-testid='file-select-1']").GetAttribute("aria-label"));
    }

    /// <summary>
    ///     Test that the interactive marker is set once the component has rendered on a live circuit.
    /// </summary>
    [Fact]
    public void ReviewPage_AfterRender_Interactive_SetsMarker()
    {
        // Arrange: no setup required

        // Act: render the page
        var cut = RenderPage(1);

        // Assert: the root element reports interactivity
        Assert.Equal("true", cut.Find("[data-testid='review-page']").GetAttribute("data-interactive"));
    }

    /// <summary>
    ///     Test that a review without threads shows zero counts in the summary and for every file.
    /// </summary>
    [Fact]
    public async Task ReviewPage_Render_NoThreads_ShowsZeroSummaryAndZeroFileCounts()
    {
        // Arrange: a review of two files without threads
        var projectId = await CreateReviewAsync(new UploadedFile("a.txt", TestContent.A), new UploadedFile("b.txt", TestContent.B));

        // Act: render the page
        var cut = RenderPage(projectId);

        // Assert: zero everywhere
        Assert.Equal("0", cut.Find("[data-testid='threads-open-count']").TextContent);
        Assert.Equal("0", cut.Find("[data-testid='threads-resolved-count']").TextContent);
        Assert.Equal("0 open threads", cut.Find("[data-testid='file-open-threads-0']").TextContent);
        Assert.Equal("0 open threads", cut.Find("[data-testid='file-open-threads-1']").TextContent);
    }

    /// <summary>
    ///     Test that existing threads are shown with counts, markers, anchors, state and comments.
    /// </summary>
    [Fact]
    public async Task ReviewPage_FileSelect_ThreadsExist_ShowsSummaryCountsMarkersAndComments()
    {
        // Arrange: a four-line file with a whole-file thread, a range thread with a reply, and a resolved thread
        var projectId = await CreateReviewAsync(
            new UploadedFile("a.txt", "1\n2\n3\n4\n"u8.ToArray()),
            new UploadedFile("b.txt", TestContent.B));
        var whole = await Threads.StartThreadAsync(projectId, "REV-1", "a.txt", null, null, TestActors.Ann, "Whole", TestContext.Current.CancellationToken);
        var range = await Threads.StartThreadAsync(projectId, "REV-1", "a.txt", 2, 3, TestActors.Bob, "Range", TestContext.Current.CancellationToken);
        await Threads.ReplyAsync(projectId, "REV-1", range.Id, TestActors.Cy, "Reply", TestContext.Current.CancellationToken);
        var resolved = await Threads.StartThreadAsync(projectId, "REV-1", "a.txt", 4, 4, TestActors.Dee, "Done", TestContext.Current.CancellationToken);
        await Threads.ResolveAsync(projectId, "REV-1", resolved.Id, TestContext.Current.CancellationToken);
        await Threads.StartThreadAsync(projectId, "REV-1", "b.txt", null, null, TestActors.Eve, "Other file", TestContext.Current.CancellationToken);
        var cut = RenderPage(projectId);

        // Act: select the first file
        await cut.Find("[data-testid='file-select-0']").ClickAsync();

        // Assert: the summary (all files) and the per-file counts
        Assert.Equal("3", cut.Find("[data-testid='threads-open-count']").TextContent);
        Assert.Equal("1", cut.Find("[data-testid='threads-resolved-count']").TextContent);
        Assert.Equal("2 open threads", cut.Find("[data-testid='file-open-threads-0']").TextContent);
        Assert.Equal("1 open thread", cut.Find("[data-testid='file-open-threads-1']").TextContent);

        // Assert: anchored lines are highlighted and the start line of each ranged thread has a marker
        Assert.DoesNotContain("anchored", cut.Find("[data-testid='code-line-1']").ClassList);
        Assert.Contains("anchored", cut.Find("[data-testid='code-line-2']").ClassList);
        Assert.Contains("anchored", cut.Find("[data-testid='code-line-3']").ClassList);
        Assert.Contains("anchored", cut.Find("[data-testid='code-line-4']").ClassList);
        Assert.Empty(cut.FindAll("[data-testid='line-marker-1']"));
        Assert.Equal("1", cut.Find("[data-testid='line-marker-2']").TextContent);
        Assert.Empty(cut.FindAll("[data-testid='line-marker-3']"));
        Assert.Equal("1", cut.Find("[data-testid='line-marker-4']").TextContent);

        // Assert: only the threads of this file are listed, in order, with anchor, state and comments
        Assert.Equal(3, cut.FindAll("[data-testid='threads-list'] > li").Count);
        Assert.Equal("Whole file", cut.Find($"[data-testid='thread-anchor-{whole.Id}']").TextContent);
        Assert.Equal("Lines 2-3", cut.Find($"[data-testid='thread-anchor-{range.Id}']").TextContent);
        Assert.Equal("Line 4", cut.Find($"[data-testid='thread-anchor-{resolved.Id}']").TextContent);
        Assert.Equal("Open", cut.Find($"[data-testid='thread-state-{range.Id}']").TextContent);
        Assert.Equal("Resolved", cut.Find($"[data-testid='thread-state-{resolved.Id}']").TextContent);
        Assert.Equal("Bob", cut.Find($"[data-testid='comment-author-{range.Id}-1']").TextContent);
        Assert.Equal("Range", cut.Find($"[data-testid='comment-text-{range.Id}-1']").TextContent);
        Assert.Equal("Cy", cut.Find($"[data-testid='comment-author-{range.Id}-2']").TextContent);
        Assert.Equal("Reply", cut.Find($"[data-testid='comment-text-{range.Id}-2']").TextContent);
        Assert.Equal("2025-03-04 05:06 UTC", cut.Find($"[data-testid='comment-time-{range.Id}-1']").TextContent);
        Assert.DoesNotContain("Other file", cut.Markup, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a file without threads says so.
    /// </summary>
    [Fact]
    public async Task ReviewPage_FileSelect_NoThreadsOnFile_ShowsEmptyMessage()
    {
        // Arrange: a review with one file and no threads
        var projectId = await CreateReviewAsync(new UploadedFile("a.txt", TestContent.A));
        var cut = RenderPage(projectId);

        // Act: select the file
        await cut.Find("[data-testid='file-select-0']").ClickAsync();

        // Assert: the empty message is shown and no list
        Assert.NotEmpty(cut.FindAll("[data-testid='threads-empty']"));
        Assert.Empty(cut.FindAll("[data-testid='threads-list']"));
    }

    /// <summary>
    ///     Test that the Comment button of a line fills the start and end line fields.
    /// </summary>
    [Fact]
    public async Task ReviewPage_LineCommentButton_Click_FillsStartAndEndLine()
    {
        // Arrange: a two-line file, selected
        var projectId = await CreateReviewAsync(new UploadedFile("a.txt", "x\ny\n"u8.ToArray()));
        var cut = RenderPage(projectId);
        await cut.Find("[data-testid='file-select-0']").ClickAsync();

        // Act: click the Comment button of line 2
        await cut.Find("[data-testid='line-comment-2']").ClickAsync();

        // Assert: both fields hold 2 and the target says so
        Assert.Equal("2", cut.Find("[data-testid='thread-start-line-input']").GetAttribute("value"));
        Assert.Equal("2", cut.Find("[data-testid='thread-end-line-input']").GetAttribute("value"));
        Assert.Contains("line 2", cut.Find("[data-testid='thread-target']").TextContent, StringComparison.Ordinal);

        // Assert: the browser was asked to bring the form into view, since it sits below the code
        await cut.WaitForAssertionAsync(() =>
            Assert.Equal(["new-thread-form", "center"], JSInterop.Invocations["reviewCenter.scrollToTestId"][^1].Arguments));
    }

    /// <summary>
    ///     Test that a whole-file thread is created, shown, counted, and that the text field is cleared.
    /// </summary>
    [Fact]
    public async Task ReviewPage_StartThread_WholeFile_CreatesThreadAndClearsText()
    {
        // Arrange: a selected file
        var projectId = await CreateReviewAsync(new UploadedFile("a.txt", TestContent.A));
        var cut = RenderPage(projectId);
        await cut.Find("[data-testid='file-select-0']").ClickAsync();

        // Act: fill in the text and start the thread (the page has no field for a name)
        await cut.Find("[data-testid='thread-text-input']").InputAsync("Please check");
        await cut.Find("[data-testid='new-thread-form']").SubmitAsync();

        // Assert: stored and shown as an open whole-file thread under the signed-in user; counts updated; text cleared
        var thread = Assert.Single(await Threads.ListAsync(projectId, "REV-1", TestContext.Current.CancellationToken));
        Assert.Null(thread.StartLine);
        Assert.Equal(("Ann", "ann"), (thread.Comments[0].Author, thread.Comments[0].AuthorUsername));
        Assert.Equal("Whole file", cut.Find($"[data-testid='thread-anchor-{thread.Id}']").TextContent);
        Assert.Equal("Ann", cut.Find($"[data-testid='comment-author-{thread.Id}-1']").TextContent);
        Assert.Equal("1", cut.Find("[data-testid='threads-open-count']").TextContent);
        Assert.Equal("1 open thread", cut.Find("[data-testid='file-open-threads-0']").TextContent);
        Assert.Empty(cut.FindAll("[data-testid='thread-error']"));
        Assert.Equal(string.Empty, cut.Find("[data-testid='thread-text-input']").GetAttribute("value") ?? string.Empty);
        Assert.Empty(cut.FindAll("[data-testid='comment-author-input']"));
    }

    /// <summary>
    ///     Test that a start and end line, and a start line alone, anchor a range and a single line.
    /// </summary>
    [Fact]
    public async Task ReviewPage_StartThread_LineRangeAndSingleLine_AnchorLines()
    {
        // Arrange: a selected three-line file
        var projectId = await CreateReviewAsync(new UploadedFile("a.txt", "a\nb\nc\n"u8.ToArray()));
        var cut = RenderPage(projectId);
        await cut.Find("[data-testid='file-select-0']").ClickAsync();

        // Act: start a thread on lines 1 to 2, then one on line 3 alone
        await cut.Find("[data-testid='thread-start-line-input']").InputAsync("1");
        await cut.Find("[data-testid='thread-end-line-input']").InputAsync("2");
        await cut.Find("[data-testid='thread-text-input']").InputAsync("Range");
        await cut.Find("[data-testid='new-thread-form']").SubmitAsync();
        await cut.Find("[data-testid='thread-start-line-input']").InputAsync("3");
        await cut.Find("[data-testid='thread-text-input']").InputAsync("Single");
        await cut.Find("[data-testid='new-thread-form']").SubmitAsync();

        // Assert: both anchors are stored as typed
        var threads = await Threads.ListAsync(projectId, "REV-1", TestContext.Current.CancellationToken);
        Assert.Equal([(1, 2), (3, 3)], threads.Select(t => (t.StartLine!.Value, t.EndLine!.Value)));
        Assert.Contains("anchored", cut.Find("[data-testid='code-line-2']").ClassList);
        Assert.Contains("anchored", cut.Find("[data-testid='code-line-3']").ClassList);
    }

    /// <summary>
    ///     Test that invalid input shows the message next to the form, keeps the entered values and stores nothing.
    /// </summary>
    /// <param name="start">The start line to enter.</param>
    /// <param name="text">The text to enter.</param>
    /// <param name="expected">A fragment of the expected message.</param>
    [Theory]
    [InlineData("", "", "comment text is required")]
    [InlineData("abc", "Text", "whole numbers")]
    [InlineData("1.5", "Text", "whole numbers")]
    [InlineData("9", "Text", "only 1 line")]
    public async Task ReviewPage_StartThread_InvalidInput_ShowsErrorAndKeepsValues(string start, string text, string expected)
    {
        // Arrange: a selected one-line file
        var projectId = await CreateReviewAsync(new UploadedFile("a.txt", TestContent.A));
        var cut = RenderPage(projectId);
        await cut.Find("[data-testid='file-select-0']").ClickAsync();
        await cut.Find("[data-testid='thread-start-line-input']").InputAsync(start);
        await cut.Find("[data-testid='thread-text-input']").InputAsync(text);

        // Act: submit the form
        await cut.Find("[data-testid='new-thread-form']").SubmitAsync();

        // Assert: the message is shown, the values are kept, and nothing is stored
        Assert.Contains(expected, cut.Find("[data-testid='thread-error']").TextContent, StringComparison.Ordinal);
        Assert.Equal(start, cut.Find("[data-testid='thread-start-line-input']").GetAttribute("value") ?? string.Empty);
        Assert.Equal(text, cut.Find("[data-testid='thread-text-input']").GetAttribute("value") ?? cut.Find("[data-testid='thread-text-input']").TextContent);
        Assert.Empty(await Threads.ListAsync(projectId, "REV-1", TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Test that a binary file accepts a whole-file thread and refuses a line range with a message.
    /// </summary>
    [Fact]
    public async Task ReviewPage_StartThread_BinaryFile_WholeFileAllowedAndRangeRefused()
    {
        // Arrange: a selected binary file
        var projectId = await CreateReviewAsync(new UploadedFile("c.bin", TestContent.C));
        var cut = RenderPage(projectId);
        await cut.Find("[data-testid='file-select-0']").ClickAsync();
        await cut.Find("[data-testid='thread-text-input']").InputAsync("Text");

        // Act: submit with a line, then without
        await cut.Find("[data-testid='thread-start-line-input']").InputAsync("1");
        await cut.Find("[data-testid='new-thread-form']").SubmitAsync();
        var refused = cut.Find("[data-testid='thread-error']").TextContent;
        await cut.Find("[data-testid='thread-start-line-input']").InputAsync(string.Empty);
        await cut.Find("[data-testid='new-thread-form']").SubmitAsync();

        // Assert: the range was refused as binary, the whole-file thread was created
        Assert.Contains("binary", refused, StringComparison.Ordinal);
        Assert.Null(Assert.Single(await Threads.ListAsync(projectId, "REV-1", TestContext.Current.CancellationToken)).StartLine);
        Assert.Empty(cut.FindAll("[data-testid='thread-error']"));
    }

    /// <summary>
    ///     Test that a reply is appended, and that a blank reply shows its message in the thread.
    /// </summary>
    [Fact]
    public async Task ReviewPage_Reply_ValidAndBlank_AppendsCommentOrShowsErrorInThread()
    {
        // Arrange: a selected file with a thread by Ann, with Bob signed in
        SignInAs("bob", "Bob");
        var projectId = await CreateReviewAsync(new UploadedFile("a.txt", TestContent.A));
        var thread = await Threads.StartThreadAsync(projectId, "REV-1", "a.txt", null, null, TestActors.Ann, "First", TestContext.Current.CancellationToken);
        var cut = RenderPage(projectId);
        await cut.Find("[data-testid='file-select-0']").ClickAsync();

        // Act: post a blank reply, then a real one
        await cut.Find($"[data-testid='reply-button-{thread.Id}']").ClickAsync();
        var blankError = cut.Find($"[data-testid='reply-error-{thread.Id}']").TextContent;
        await cut.Find($"[data-testid='reply-text-{thread.Id}']").InputAsync("Second");
        await cut.Find($"[data-testid='reply-button-{thread.Id}']").ClickAsync();

        // Assert: the blank reply was refused in the thread, the real one is shown and the error is gone
        Assert.Contains("comment text is required", blankError, StringComparison.Ordinal);
        Assert.Equal("Bob", cut.Find($"[data-testid='comment-author-{thread.Id}-2']").TextContent);
        Assert.Equal("Second", cut.Find($"[data-testid='comment-text-{thread.Id}-2']").TextContent);
        Assert.Empty(cut.FindAll($"[data-testid='reply-error-{thread.Id}']"));
        var stored = Assert.Single(await Threads.ListAsync(projectId, "REV-1", TestContext.Current.CancellationToken));
        Assert.Equal(2, stored.Comments.Count);
        Assert.Equal("bob", stored.Comments[1].AuthorUsername);
    }

    /// <summary>
    ///     Test that a page asked to post as someone else still records the signed-in user: nothing in the form
    ///     can name the author.
    /// </summary>
    [Fact]
    public async Task ReviewPage_Reply_NoAuthorField_AuthorCannotBeChosen()
    {
        // Arrange: a selected file with a thread, with Bob signed in
        SignInAs("bob", "Bob");
        var projectId = await CreateReviewAsync(new UploadedFile("a.txt", TestContent.A));
        var thread = await Threads.StartThreadAsync(projectId, "REV-1", "a.txt", null, null, TestActors.Ann, "First", TestContext.Current.CancellationToken);
        var cut = RenderPage(projectId);
        await cut.Find("[data-testid='file-select-0']").ClickAsync();
        await cut.Find($"[data-testid='reply-text-{thread.Id}']").InputAsync("Second");

        // Act: post the reply
        await cut.Find($"[data-testid='reply-button-{thread.Id}']").ClickAsync();

        // Assert: there is no field for a name, and the reply carries the signed-in user
        Assert.Empty(cut.FindAll("[data-testid='comment-author-input']"));
        Assert.DoesNotContain("Your name", cut.Markup, StringComparison.Ordinal);
        Assert.Equal("Bob", cut.Find($"[data-testid='comment-author-{thread.Id}-2']").TextContent);
    }

    /// <summary>
    ///     Test that Resolve and Reopen toggle the thread state and update the summary.
    /// </summary>
    [Fact]
    public async Task ReviewPage_ResolveAndReopen_Toggle_ChangesStateAndSummary()
    {
        // Arrange: a selected file with an open thread
        var projectId = await CreateReviewAsync(new UploadedFile("a.txt", TestContent.A));
        var thread = await Threads.StartThreadAsync(projectId, "REV-1", "a.txt", null, null, TestActors.Ann, "First", TestContext.Current.CancellationToken);
        var cut = RenderPage(projectId);
        await cut.Find("[data-testid='file-select-0']").ClickAsync();

        // Act: resolve
        await cut.Find($"[data-testid='thread-toggle-{thread.Id}']").ClickAsync();

        // Assert: resolved and collapsed, so it offers to expand; the counts moved
        Assert.Equal("Resolved", cut.Find($"[data-testid='thread-state-{thread.Id}']").TextContent);
        Assert.Empty(cut.FindAll($"[data-testid='thread-toggle-{thread.Id}']"));
        Assert.Equal("0", cut.Find("[data-testid='threads-open-count']").TextContent);
        Assert.Equal("1", cut.Find("[data-testid='threads-resolved-count']").TextContent);
        Assert.Equal("0 open threads", cut.Find("[data-testid='file-open-threads-0']").TextContent);

        // Act: expand and reopen
        await cut.Find($"[data-testid='thread-expand-{thread.Id}']").ClickAsync();
        Assert.Equal("Reopen", cut.Find($"[data-testid='thread-toggle-{thread.Id}']").TextContent);
        await cut.Find($"[data-testid='thread-toggle-{thread.Id}']").ClickAsync();

        // Assert: open again and the counts moved back
        Assert.Equal("Open", cut.Find($"[data-testid='thread-state-{thread.Id}']").TextContent);
        Assert.Equal("Resolve", cut.Find($"[data-testid='thread-toggle-{thread.Id}']").TextContent);
        Assert.Equal("1", cut.Find("[data-testid='threads-open-count']").TextContent);
        Assert.Equal("0", cut.Find("[data-testid='threads-resolved-count']").TextContent);
    }

    /// <summary>
    ///     Test that resolving a thread after the session ended, but before the page learned of it, changes nothing:
    ///     the page leaves for the not-found page (the access check finds no user).
    /// </summary>
    [Fact]
    public async Task ReviewPage_ResolveAndReopen_SessionEndedBeforePageKnows_ShowsNotFoundAndChangesNothing()
    {
        // Arrange: an open thread and a resolved thread on the selected file
        var projectId = await CreateReviewAsync(new UploadedFile("a.txt", TestContent.A));
        var open = await Threads.StartThreadAsync(projectId, "REV-1", "a.txt", null, null, TestActors.Ann, "Open one", TestContext.Current.CancellationToken);
        var resolved = await Threads.StartThreadAsync(projectId, "REV-1", "a.txt", null, null, TestActors.Ann, "Resolved one", TestContext.Current.CancellationToken);
        await Threads.ResolveAsync(projectId, "REV-1", resolved.Id, TestContext.Current.CancellationToken);
        var cut = RenderPage(projectId);
        await cut.Find("[data-testid='file-select-0']").ClickAsync();
        await cut.Find($"[data-testid='thread-expand-{resolved.Id}']").ClickAsync();
        SignOutQuietly();

        // Act: resolve the open thread
        await cut.Find($"[data-testid='thread-toggle-{open.Id}']").ClickAsync();

        // Assert: the page is the not-found page, and both threads keep their state
        Assert.NotEmpty(cut.FindAll("[data-testid='review-not-found']"));
        Assert.Empty(cut.FindAll($"[data-testid='thread-toggle-{resolved.Id}']"));
        var stored = (await Threads.ListAsync(projectId, "REV-1", TestContext.Current.CancellationToken)).ToDictionary(thread => thread.Id);
        Assert.Equal(ReviewThreadState.Open, stored[open.Id].State);
        Assert.Equal(ReviewThreadState.Resolved, stored[resolved.Id].State);
    }

    /// <summary>
    ///     Test that author and comment text containing markup are shown as plain text.
    /// </summary>
    [Fact]
    public async Task ReviewPage_Render_ThreadText_IsShownAsPlainText()
    {
        // Arrange: a thread whose author and text contain markup
        var projectId = await CreateReviewAsync(new UploadedFile("a.txt", TestContent.A));
        var thread = await Threads.StartThreadAsync(
            projectId, "REV-1", "a.txt", null, null, new Actor("ann", "<i>Ann</i>"), "<script>alert(1)</script> & <b>bold</b>", TestContext.Current.CancellationToken);
        var cut = RenderPage(projectId);

        // Act: select the file
        await cut.Find("[data-testid='file-select-0']").ClickAsync();

        // Assert: the characters are text, and no element was created from them
        var text = cut.Find($"[data-testid='comment-text-{thread.Id}-1']");
        Assert.Equal("<script>alert(1)</script> & <b>bold</b>", text.TextContent);
        Assert.Empty(text.QuerySelectorAll("*"));
        Assert.Equal("<i>Ann</i>", cut.Find($"[data-testid='comment-author-{thread.Id}-1']").TextContent);
        Assert.Empty(cut.FindAll("script"));
    }

    /// <summary>
    ///     Test that the files, thread index and code come before the review details, which are collapsed and
    ///     hold the id, state, fingerprint and per-file hashes.
    /// </summary>
    [Fact]
    public async Task ReviewPage_Render_ExistingReview_FilesFirstAndDetailsCollapsedHoldMetadata()
    {
        // Arrange: a review of two files
        var projectId = await CreateReviewAsync(
            new UploadedFile("a.txt", TestContent.A),
            new UploadedFile("b.txt", TestContent.B),
            new UploadedFile("c.bin", TestContent.C));

        // Act: render the page
        var cut = RenderPage(projectId);

        // Assert: files and the thread index come before the details in the page
        var markup = cut.Markup;
        var nextStep = markup.IndexOf("data-testid=\"next-step\"", StringComparison.Ordinal);
        var files = markup.IndexOf("data-testid=\"files-list\"", StringComparison.Ordinal);
        var index = markup.IndexOf("data-testid=\"thread-index\"", StringComparison.Ordinal);
        var details = markup.IndexOf("data-testid=\"review-details\"", StringComparison.Ordinal);
        Assert.InRange(nextStep, 0, files - 1);
        Assert.InRange(files, nextStep, index - 1);
        Assert.InRange(index, files, details - 1);

        // Assert: the details are collapsed and hold the metadata; the file list holds no hash
        var detailsElement = cut.Find("[data-testid='review-details']");
        Assert.False(detailsElement.HasAttribute("open"));
        Assert.Equal("REV-1", detailsElement.QuerySelector("[data-testid='review-id']")?.TextContent);
        Assert.Equal("Draft", detailsElement.QuerySelector("[data-testid='review-state']")?.TextContent);
        Assert.Equal(TestContent.FingerprintABC, detailsElement.QuerySelector("[data-testid='review-fingerprint']")?.TextContent);
        Assert.Equal(TestContent.HashA, detailsElement.QuerySelector("[data-testid='file-hash-0']")?.TextContent);
        Assert.Equal("c.bin", detailsElement.QuerySelector("[data-testid='file-hash-path-2']")?.TextContent);
        Assert.Empty(cut.Find("[data-testid='files-list']").QuerySelectorAll("[data-testid^='file-hash']"));
    }

    /// <summary>
    ///     Test that the next-step line follows the state of the threads, next to the unanswered checks.
    /// </summary>
    [Fact]
    public async Task ReviewPage_Render_NextStep_FollowsThreadState()
    {
        // Arrange: a review without threads, with its 58 checks all unanswered
        var projectId = await CreateReviewAsync(new UploadedFile("a.txt", TestContent.A));
        var cut = RenderPage(projectId);
        Assert.Equal("58 checklist checks unanswered.", cut.Find("[data-testid='next-step']").TextContent);

        // Act: start a thread through the page
        await cut.Find("[data-testid='file-select-0']").ClickAsync();
        await cut.Find("[data-testid='thread-text-input']").InputAsync("Please check");
        await cut.Find("[data-testid='new-thread-form']").SubmitAsync();

        // Assert: the checks and one open thread to resolve are named in one sentence
        Assert.Equal(
            "58 checklist checks unanswered, 1 open thread to resolve.",
            cut.Find("[data-testid='next-step']").TextContent);

        // Act: resolve it
        var thread = Assert.Single(await Threads.ListAsync(projectId, "REV-1", TestContext.Current.CancellationToken));
        await cut.Find($"[data-testid='thread-toggle-{thread.Id}']").ClickAsync();

        // Assert: only the checks are left
        Assert.Equal("58 checklist checks unanswered.", cut.Find("[data-testid='next-step']").TextContent);
    }

    /// <summary>
    ///     Test that the next-step line counts the unanswered checks, and says so when the checklist is complete.
    /// </summary>
    [Fact]
    public async Task ReviewPage_Render_NextStep_IncludesChecklistProgress()
    {
        // Arrange: a review with one answered check and a not-applicable section of 5 checks
        var projectId = await CreateReviewAsync(new UploadedFile("a.txt", TestContent.A));
        await Checklists.SetAnswerAsync(
            projectId, "REV-1", "REQ-01", ChecklistOutcome.Pass, null, TestActors.Ann, TestContext.Current.CancellationToken);
        await Checklists.SetSectionApplicabilityAsync(
            projectId, "REV-1", "DES", false, "No design", TestActors.Ann, TestContext.Current.CancellationToken);

        // Act, Assert: 58 - 1 answered - 5 not applicable = 52 unanswered
        Assert.Equal("52 checklist checks unanswered.", RenderPage(projectId).Find("[data-testid='next-step']").TextContent);

        // Arrange: everything that applies is answered, and the one thread is resolved
        foreach (var item in DefaultChecklist.Current.Sections.Where(s => s.Id != "DES").SelectMany(s => s.Items))
        {
            await Checklists.SetAnswerAsync(
                projectId, "REV-1", item.Id, ChecklistOutcome.Pass, null, TestActors.Ann, TestContext.Current.CancellationToken);
        }

        var thread = await Threads.StartThreadAsync(
            projectId, "REV-1", "a.txt", null, null, TestActors.Ann, "Done", TestContext.Current.CancellationToken);
        await Threads.ResolveAsync(projectId, "REV-1", thread.Id, TestContext.Current.CancellationToken);

        // Act, Assert: one sentence says that only the reviewer slot is missing
        Assert.Equal(
            "Add a reviewer slot.",
            RenderPage(projectId).Find("[data-testid='next-step']").TextContent);
    }

    /// <summary>
    ///     Test that the review page shows the checklist progress and a link to the checklist.
    /// </summary>
    [Fact]
    public async Task ReviewPage_Render_NewReview_ShowsChecklistProgressAndLink()
    {
        // Arrange: a review with two answered checks
        var projectId = await CreateReviewAsync(new UploadedFile("a.txt", TestContent.A));
        foreach (var id in new[] { "REQ-01", "REQ-02" })
        {
            await Checklists.SetAnswerAsync(
                projectId, "REV-1", id, ChecklistOutcome.Pass, null, TestActors.Ann, TestContext.Current.CancellationToken);
        }

        // Act: render the page
        var cut = RenderPage(projectId);

        // Assert: the summary and the link to the checklist page
        Assert.Equal("2 of 58 checks answered", cut.Find("[data-testid='checklist-progress-summary']").TextContent);
        Assert.Equal(
            $"/projects/{projectId}/reviews/REV-1/checklist",
            cut.Find("[data-testid='checklist-link']").GetAttribute("href"));
    }

    /// <summary>
    ///     Test that a review without a checklist shows no checklist card.
    /// </summary>
    [Fact]
    public async Task ReviewPage_Render_ReviewWithoutChecklist_HidesChecklistCard()
    {
        // Arrange: a review stored without a snapshot, straight into the store
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

        // Assert: the review shows, without checklist card or checklist words in the next step
        Assert.Equal("Old", cut.Find("[data-testid='review-title']").TextContent);
        Assert.Empty(cut.FindAll("[data-testid='checklist-card']"));
        Assert.DoesNotContain("checklist", cut.Find("[data-testid='next-step']").TextContent, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     Test that the thread index shows the open threads by default, filters to All and Resolved, and
    ///     shows file, anchor, state, comment count, first author and last activity on each row.
    /// </summary>
    [Fact]
    public async Task ReviewPage_ThreadIndex_Filters_ShowRowsWithFileAnchorStateCountAuthorAndActivity()
    {
        // Arrange: a page rendered before any thread exists
        var projectId = await CreateReviewAsync(new UploadedFile("a.txt", "a\nb\nc\n"u8.ToArray()), new UploadedFile("b.txt", TestContent.B));
        var cut = RenderPage(projectId);
        Assert.Equal("No open threads.", cut.Find("[data-testid='thread-index-empty']").TextContent);

        // Arrange: an open thread with a reply on a.txt and a resolved thread on b.txt, loaded by selecting a file
        var open = await Threads.StartThreadAsync(projectId, "REV-1", "a.txt", 2, 3, TestActors.Ann, "Look", TestContext.Current.CancellationToken);
        await Threads.ReplyAsync(projectId, "REV-1", open.Id, TestActors.Bob, "Done", TestContext.Current.CancellationToken);
        var resolved = await Threads.StartThreadAsync(projectId, "REV-1", "b.txt", null, null, TestActors.Cy, "Old", TestContext.Current.CancellationToken);
        await Threads.ResolveAsync(projectId, "REV-1", resolved.Id, TestContext.Current.CancellationToken);
        await cut.Find("[data-testid='file-select-0']").ClickAsync();

        // Assert: only the open thread is listed, with all its facts
        Assert.Equal("true", cut.Find("[data-testid='index-filter-open']").GetAttribute("aria-pressed"));
        Assert.Single(cut.FindAll("[data-testid='thread-index-list'] > li"));
        Assert.Equal("a.txt", cut.Find($"[data-testid='index-file-{open.Id}']").TextContent);
        Assert.Equal("Lines 2-3", cut.Find($"[data-testid='index-anchor-{open.Id}']").TextContent);
        Assert.Equal("Open", cut.Find($"[data-testid='index-state-{open.Id}']").TextContent);
        Assert.Equal("2 comments", cut.Find($"[data-testid='index-count-{open.Id}']").TextContent);
        Assert.Equal("Ann", cut.Find($"[data-testid='index-author-{open.Id}']").TextContent);
        Assert.Equal("2025-03-04 05:06 UTC", cut.Find($"[data-testid='index-activity-{open.Id}']").TextContent);

        // Assert: the row button's accessible name carries the same facts, not just the file
        Assert.Equal(
            "Show Lines 2-3 of a.txt, Open, 2 comments, first by Ann, last activity 2025-03-04 05:06 UTC",
            cut.Find($"[data-testid='index-row-{open.Id}']").GetAttribute("aria-label"));
        // Act, Assert: All shows both threads, with the counts in the filter buttons
        await cut.Find("[data-testid='index-filter-all']").ClickAsync();
        Assert.Equal(2, cut.FindAll("[data-testid='thread-index-list'] > li").Count);
        Assert.Equal("2", cut.Find("[data-testid='threads-all-count']").TextContent);
        Assert.Equal("true", cut.Find("[data-testid='index-filter-all']").GetAttribute("aria-pressed"));

        // Act, Assert: Resolved shows only the resolved thread
        await cut.Find("[data-testid='index-filter-resolved']").ClickAsync();
        Assert.Empty(cut.FindAll($"[data-testid='index-row-{open.Id}']"));
        Assert.Equal("Whole file", cut.Find($"[data-testid='index-anchor-{resolved.Id}']").TextContent);
        Assert.Equal("Resolved", cut.Find($"[data-testid='index-state-{resolved.Id}']").TextContent);
        Assert.Equal("1 comment", cut.Find($"[data-testid='index-count-{resolved.Id}']").TextContent);
    }

    /// <summary>
    ///     Test that choosing an index row selects its file, highlights its lines and thread, expands a
    ///     resolved thread and asks the browser to scroll to the first line.
    /// </summary>
    [Fact]
    public async Task ReviewPage_ThreadIndex_RowClick_SelectsFileHighlightsAnchorAndScrolls()
    {
        // Arrange: a resolved thread on lines 2-3 of b.txt, and a page showing a.txt
        var projectId = await CreateReviewAsync(
            new UploadedFile("a.txt", TestContent.A),
            new UploadedFile("b.txt", "x\ny\nz\n"u8.ToArray()));
        var thread = await Threads.StartThreadAsync(projectId, "REV-1", "b.txt", 2, 3, TestActors.Ann, "Check this", TestContext.Current.CancellationToken);
        await Threads.ResolveAsync(projectId, "REV-1", thread.Id, TestContext.Current.CancellationToken);
        var cut = RenderPage(projectId);
        await cut.Find("[data-testid='file-select-0']").ClickAsync();
        await cut.Find("[data-testid='index-filter-all']").ClickAsync();

        // Assert: choosing a file asked the browser to bring its code to the top of the window
        await cut.WaitForAssertionAsync(() =>
            Assert.Equal(["selected-file-path", "start"], JSInterop.Invocations["reviewCenter.scrollToTestId"][^1].Arguments));

        // Act: choose the thread in the index
        await cut.Find($"[data-testid='index-row-{thread.Id}']").ClickAsync();

        // Assert: its file is shown with the covered lines and the thread highlighted, and the thread is expanded
        Assert.Equal("b.txt", cut.Find("[data-testid='selected-file-path']").TextContent);
        Assert.DoesNotContain("focused", cut.Find("[data-testid='code-line-1']").ClassList);
        Assert.Contains("focused", cut.Find("[data-testid='code-line-2']").ClassList);
        Assert.Contains("focused", cut.Find("[data-testid='code-line-3']").ClassList);
        Assert.Contains("focused", cut.Find($"[data-testid='thread-{thread.Id}']").ClassList);
        Assert.Equal("Check this", cut.Find($"[data-testid='comment-text-{thread.Id}-1']").TextContent);

        // Assert: the browser was last asked to scroll the first line to the middle of the window
        await cut.WaitForAssertionAsync(() =>
        {
            var scroll = JSInterop.Invocations["reviewCenter.scrollToTestId"][^1];
            Assert.Equal(["code-line-2", "center"], scroll.Arguments);
        });
    }

    /// <summary>
    ///     Test that a resolved thread is a one-line header that expands and collapses on request, an open
    ///     thread stays expanded, and resolving a thread collapses it.
    /// </summary>
    [Fact]
    public async Task ReviewPage_ResolvedThread_CollapsedHeaderExpandsOnRequest()
    {
        // Arrange: a resolved thread with a reply and an open thread on the selected file
        var projectId = await CreateReviewAsync(new UploadedFile("a.txt", "a\nb\n"u8.ToArray()));
        var done = await Threads.StartThreadAsync(projectId, "REV-1", "a.txt", 1, 1, TestActors.Ann, "Fixed?", TestContext.Current.CancellationToken);
        await Threads.ReplyAsync(projectId, "REV-1", done.Id, TestActors.Bob, "Yes", TestContext.Current.CancellationToken);
        await Threads.ResolveAsync(projectId, "REV-1", done.Id, TestContext.Current.CancellationToken);
        var open = await Threads.StartThreadAsync(projectId, "REV-1", "a.txt", 2, 2, TestActors.Cy, "Why?", TestContext.Current.CancellationToken);
        var cut = RenderPage(projectId);

        // Act: select the file
        await cut.Find("[data-testid='file-select-0']").ClickAsync();

        // Assert: the resolved thread shows only its header; the open thread is expanded
        Assert.Equal("Line 1", cut.Find($"[data-testid='thread-anchor-{done.Id}']").TextContent);
        Assert.Equal("Resolved", cut.Find($"[data-testid='thread-state-{done.Id}']").TextContent);
        Assert.Equal("2 comments", cut.Find($"[data-testid='thread-count-{done.Id}']").TextContent);
        Assert.Empty(cut.FindAll($"[data-testid='comment-text-{done.Id}-1']"));
        Assert.Empty(cut.FindAll($"[data-testid='reply-text-{done.Id}']"));
        Assert.Equal("Why?", cut.Find($"[data-testid='comment-text-{open.Id}-1']").TextContent);
        Assert.Empty(cut.FindAll($"[data-testid='thread-expand-{open.Id}']"));

        // Act: expand it
        await cut.Find($"[data-testid='thread-expand-{done.Id}']").ClickAsync();

        // Assert: comments and Reopen are shown
        Assert.Equal("Fixed?", cut.Find($"[data-testid='comment-text-{done.Id}-1']").TextContent);
        Assert.Equal("Yes", cut.Find($"[data-testid='comment-text-{done.Id}-2']").TextContent);
        Assert.Equal("Reopen", cut.Find($"[data-testid='thread-toggle-{done.Id}']").TextContent);

        // Act: collapse it again
        await cut.Find($"[data-testid='thread-expand-{done.Id}']").ClickAsync();

        // Assert: only the header remains
        Assert.Empty(cut.FindAll($"[data-testid='comment-text-{done.Id}-1']"));

        // Act: resolve the open thread
        await cut.Find($"[data-testid='thread-toggle-{open.Id}']").ClickAsync();

        // Assert: it collapsed
        Assert.Empty(cut.FindAll($"[data-testid='comment-text-{open.Id}-1']"));
        Assert.NotEmpty(cut.FindAll($"[data-testid='thread-expand-{open.Id}']"));
    }

    /// <summary>
    ///     Test that every interactive element of the review page, with a file selected and threads present,
    ///     has a kebab-case <c>data-testid</c>.
    /// </summary>
    [Fact]
    public async Task ReviewPage_Render_ThreadControls_EveryInteractiveElementHasTestId()
    {
        // Arrange: a selected file with an open and a resolved thread, and an error shown
        var projectId = await CreateReviewAsync(new UploadedFile("a.txt", "a\nb\n"u8.ToArray()));
        await Threads.StartThreadAsync(projectId, "REV-1", "a.txt", 1, 2, TestActors.Ann, "First", TestContext.Current.CancellationToken);
        var done = await Threads.StartThreadAsync(projectId, "REV-1", "a.txt", null, null, TestActors.Bob, "Done", TestContext.Current.CancellationToken);
        await Threads.ResolveAsync(projectId, "REV-1", done.Id, TestContext.Current.CancellationToken);
        var cut = RenderPage(projectId);
        await cut.Find("[data-testid='index-filter-all']").ClickAsync();
        await cut.Find("[data-testid='file-select-0']").ClickAsync();
        await cut.Find("[data-testid='new-thread-form']").SubmitAsync();

        // Act: collect the interactive elements
        var elements = cut.FindAll("a, button, input, select, textarea");

        // Assert: there are many, and each has a kebab-case test id
        Assert.True(elements.Count > 10);
        foreach (var element in elements)
        {
            var testId = element.GetAttribute("data-testid");
            Assert.True(
                testId is not null && System.Text.RegularExpressions.Regex.IsMatch(testId, "^[a-z0-9]+(?:[-.][a-z0-9]+)*$"),
                $"A <{element.LocalName}> has no kebab-case data-testid (found '{testId}').");
        }
    }

    /// <summary>
    ///     The password every user of the signing tests has.
    /// </summary>
    private const string SigningPassword = "correct horse battery";

    /// <summary>
    ///     Creates the users ann, bob and cyd, each with the signing password.
    /// </summary>
    /// <returns>The identities of the users by user name.</returns>
    private async Task<Dictionary<string, int>> CreateUsersAsync()
    {
        var ids = new Dictionary<string, int>();
        foreach (var (username, name) in new[] { ("ann", "Ann"), ("bob", "Bob"), ("cyd", "Cyd") })
        {
            ids[username] = (await Users.CreateAsync(username, name, UserRole.Reviewer, SigningPassword, TestContext.Current.CancellationToken)).Id;
        }

        return ids;
    }

    /// <summary>
    ///     Answers every check of <c>REV-1</c> as Pass.
    /// </summary>
    /// <param name="projectId">The project identity.</param>
    /// <returns>A task that completes when every check is answered.</returns>
    private async Task AnswerAllAsync(int projectId)
    {
        foreach (var item in DefaultChecklist.Current.Sections.SelectMany(section => section.Items))
        {
            await Checklists.SetAnswerAsync(
                projectId, "REV-1", item.Id, ChecklistOutcome.Pass, null, TestActors.Ann, TestContext.Current.CancellationToken);
        }
    }

    /// <summary>
    ///     Creates a review that can be signed, with one slot of ann (primary) and bob (alternate).
    /// </summary>
    /// <returns>The project identity, the slot identity and the user identities.</returns>
    private async Task<(int ProjectId, int SlotId, Dictionary<string, int> Ids)> CreateSignableAsync()
    {
        var ids = await CreateUsersAsync();
        var projectId = await CreateReviewAsync(new UploadedFile("a.txt", TestContent.A));
        await AnswerAllAsync(projectId);
        var slot = await Signing.AddSlotAsync(
            TestActors.Ann, projectId, "REV-1", ids["ann"], ids["bob"], TestContext.Current.CancellationToken);
        return (projectId, slot.Id, ids);
    }

    /// <summary>
    ///     Test that a review without slots shows an empty Reviewers card with the add form, and is In review.
    /// </summary>
    [Fact]
    public async Task ReviewPage_Render_NoSlots_ShowsEmptyReviewersCardAndInReviewBadge()
    {
        // Arrange: a review without slots
        var projectId = await CreateReviewAsync(new UploadedFile("a.txt", TestContent.A));

        // Act: render the page
        var cut = RenderPage(projectId);

        // Assert: the card says so, offers the add form, and the review is In review
        Assert.NotNull(cut.Find("[data-testid='reviewers-card']"));
        Assert.NotNull(cut.Find("[data-testid='slots-empty']"));
        Assert.NotNull(cut.Find("[data-testid='slot-add-form']"));
        Assert.Equal("In review", cut.Find("[data-testid='review-approval']").TextContent);
    }

    /// <summary>
    ///     Test that a slot is added through the page and shows primary, alternate and "Not signed".
    /// </summary>
    [Fact]
    public async Task ReviewPage_AddSlot_PrimaryAndAlternate_ShowsSlotNotSigned()
    {
        // Arrange: users and a review
        var ids = await CreateUsersAsync();
        var projectId = await CreateReviewAsync(new UploadedFile("a.txt", TestContent.A));
        var cut = RenderPage(projectId);

        // Act: choose primary and alternate and add the slot
        await cut.Find("[data-testid='new-slot-primary-select']").ChangeAsync(ids["bob"].ToString());
        await cut.Find("[data-testid='new-slot-alternate-select']").ChangeAsync(ids["cyd"].ToString());
        await cut.Find("[data-testid='slot-add-form']").SubmitAsync();

        // Assert: the slot is shown and stored
        var slot = Assert.Single((await Signing.GetApprovalAsync(projectId, "REV-1", TestContext.Current.CancellationToken))!.Slots);
        Assert.Equal("Slot 1", cut.Find($"[data-testid='slot-label-{slot.SlotId}']").TextContent);
        Assert.Equal("Bob", cut.Find($"[data-testid='slot-primary-{slot.SlotId}']").TextContent);
        Assert.Equal("Cyd", cut.Find($"[data-testid='slot-alternate-{slot.SlotId}']").TextContent);
        Assert.Equal("Not signed", cut.Find($"[data-testid='slot-status-{slot.SlotId}']").TextContent);
        Assert.Empty(cut.FindAll("[data-testid='slots-empty']"));
    }

    /// <summary>
    ///     Test that adding a slot without a primary, or with the same user twice, shows the reason.
    /// </summary>
    [Fact]
    public async Task ReviewPage_AddSlot_InvalidChoices_ShowsErrorAndAddsNothing()
    {
        // Arrange: users and a review
        var ids = await CreateUsersAsync();
        var projectId = await CreateReviewAsync(new UploadedFile("a.txt", TestContent.A));
        var cut = RenderPage(projectId);

        // Act, Assert: no primary
        await cut.Find("[data-testid='slot-add-form']").SubmitAsync();
        Assert.NotEmpty(cut.Find("[data-testid='new-slot-error']").TextContent);

        // Act, Assert: the same user as primary and alternate
        await cut.Find("[data-testid='new-slot-primary-select']").ChangeAsync(ids["bob"].ToString());
        await cut.Find("[data-testid='new-slot-alternate-select']").ChangeAsync(ids["bob"].ToString());
        await cut.Find("[data-testid='slot-add-form']").SubmitAsync();
        Assert.NotEmpty(cut.Find("[data-testid='new-slot-error']").TextContent);
        Assert.Empty((await Signing.GetApprovalAsync(projectId, "REV-1", TestContext.Current.CancellationToken))!.Slots);
    }

    /// <summary>
    ///     Test that a slot whose primary and alternate were removed from the project shows the indicator for each,
    ///     and that a slot whose people are still members shows none.
    /// </summary>
    [Fact]
    public async Task ReviewPage_Render_AssigneesNoLongerMembers_ShowsIndicatorOnlyForThem()
    {
        // Arrange: a slot of ann (primary) and bob (alternate), and the page rendered while both are members
        var (projectId, slotId, _) = await CreateSignableAsync();
        var members = RenderPage(projectId);
        await RevokeMembershipAsync(projectId, "bob");

        // Act: render the page again, after bob left the project
        var afterBob = RenderPage(projectId);
        await RevokeMembershipAsync(projectId, "ann");
        await Users.CreateAsync("root", "Root", UserRole.Admin, SigningPassword, TestContext.Current.CancellationToken);
        SignInAs("root", "Root", UserRole.Admin);
        var afterAnn = RenderPage(projectId);

        // Assert: the indicator appears for exactly the people who left
        Assert.Empty(members.FindAll("[data-testid^='slot-'][data-testid*='-not-member-']"));
        Assert.Empty(afterBob.FindAll($"[data-testid='slot-primary-not-member-{slotId}']"));
        Assert.NotNull(afterBob.Find($"[data-testid='slot-alternate-not-member-{slotId}']"));
        Assert.NotNull(afterAnn.Find($"[data-testid='slot-primary-not-member-{slotId}']"));
        Assert.NotNull(afterAnn.Find($"[data-testid='slot-alternate-not-member-{slotId}']"));
        Assert.NotNull(afterAnn.Find($"[data-testid='slot-not-member-hint-{slotId}']"));
    }

    /// <summary>
    ///     Test that an unsigned slot can be changed and removed through the page.
    /// </summary>
    [Fact]
    public async Task ReviewPage_EditAndRemoveSlot_Unsigned_ChangesAndRemoves()
    {
        // Arrange: a slot of ann and bob
        var (projectId, slotId, ids) = await CreateSignableAsync();
        var cut = RenderPage(projectId);

        // Act: make cyd the primary and drop the alternate
        await cut.Find($"[data-testid='slot-primary-select-{slotId}']").ChangeAsync(ids["cyd"].ToString());
        await cut.Find($"[data-testid='slot-alternate-select-{slotId}']").ChangeAsync(string.Empty);
        await cut.Find($"[data-testid='slot-save-{slotId}']").ClickAsync();

        // Assert: the slot shows the change
        Assert.Equal("Cyd", cut.Find($"[data-testid='slot-primary-{slotId}']").TextContent);
        Assert.Equal("None", cut.Find($"[data-testid='slot-alternate-{slotId}']").TextContent);

        // Act: remove it
        await cut.Find($"[data-testid='slot-remove-{slotId}']").ClickAsync();

        // Assert: it is gone
        Assert.Empty(cut.FindAll($"[data-testid='slot-{slotId}']"));
        Assert.NotNull(cut.Find("[data-testid='slots-empty']"));
    }

    /// <summary>
    ///     Test that a user occupying a slot of a review that cannot be signed sees the blockers and no sign
    ///     form, and that a user who is not in the slot sees neither.
    /// </summary>
    [Fact]
    public async Task ReviewPage_Render_BlockedReview_ShowsBlockersInsteadOfSignForm()
    {
        // Arrange: a slot of ann and bob on a review with unanswered checks and an open thread
        var ids = await CreateUsersAsync();
        var projectId = await CreateReviewAsync(new UploadedFile("a.txt", TestContent.A));
        await Threads.StartThreadAsync(projectId, "REV-1", "a.txt", null, null, TestActors.Bob, "Open", TestContext.Current.CancellationToken);
        var slot = await Signing.AddSlotAsync(
            TestActors.Ann, projectId, "REV-1", ids["ann"], ids["bob"], TestContext.Current.CancellationToken);

        // Act: render as ann (the primary)
        var cut = RenderPage(projectId);

        // Assert: the blockers are listed and there is no sign form
        var blockers = cut.Find($"[data-testid='sign-blockers-{slot.Id}']").TextContent;
        Assert.Contains("58 checklist checks are unanswered", blockers, StringComparison.Ordinal);
        Assert.Contains("1 thread is open", blockers, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll($"[data-testid='sign-form-{slot.Id}']"));

        // Act: render as cyd, who is not in the slot
        SignInAs("cyd", "Cyd");
        var other = RenderPage(projectId);

        // Assert: neither blockers nor sign form
        Assert.Empty(other.FindAll($"[data-testid='sign-blockers-{slot.Id}']"));
        Assert.Empty(other.FindAll($"[data-testid='sign-form-{slot.Id}']"));
    }

    /// <summary>
    ///     Test that a user who is not in the slot of a signable review sees no sign form.
    /// </summary>
    [Fact]
    public async Task ReviewPage_Render_SignableReview_NonOccupantSeesNoSignForm()
    {
        // Arrange: a signable review of ann and bob, viewed by cyd
        var (projectId, slotId, _) = await CreateSignableAsync();
        SignInAs("cyd", "Cyd");

        // Act: render the page
        var cut = RenderPage(projectId);

        // Assert: no sign form
        Assert.Empty(cut.FindAll($"[data-testid='sign-form-{slotId}']"));
        Assert.Empty(cut.FindAll($"[data-testid='sign-button-{slotId}']"));
    }

    /// <summary>
    ///     Test that a wrong password is refused with the generic message, signs nothing, and neither the typed
    ///     password nor any value is left in the page.
    /// </summary>
    [Fact]
    public async Task ReviewPage_Sign_WrongPassword_ShowsGenericErrorAndKeepsNoPassword()
    {
        // Arrange: a signable review viewed by ann, who types a wrong password
        var (projectId, slotId, _) = await CreateSignableAsync();
        var cut = RenderPage(projectId);
        Assert.Equal(SigningService.MeaningText, cut.Find($"[data-testid='sign-meaning-{slotId}']").TextContent);
        Assert.Equal("password", cut.Find($"[data-testid='sign-password-{slotId}']").GetAttribute("type"));
        await cut.Find($"[data-testid='sign-password-{slotId}']").InputAsync("wrong secret value");

        // Act: sign
        await cut.Find($"[data-testid='sign-form-{slotId}']").SubmitAsync();

        // Assert: the generic message, no signature, and the typed value is nowhere in the markup
        Assert.Equal("The password is incorrect.", cut.Find($"[data-testid='sign-error-{slotId}']").TextContent);
        Assert.Equal("Not signed", cut.Find($"[data-testid='slot-status-{slotId}']").TextContent);
        Assert.Empty(await Store.ListSignaturesAsync(projectId, "REV-1", TestContext.Current.CancellationToken));
        Assert.DoesNotContain("wrong secret value", cut.Markup, StringComparison.Ordinal);
        Assert.Empty(cut.Find($"[data-testid='sign-password-{slotId}']").GetAttribute("value") ?? string.Empty);
    }

    /// <summary>
    ///     Test that signing with the right password records the signature, shows who signed and when, approves
    ///     the review, and locks the slot against changes.
    /// </summary>
    [Fact]
    public async Task ReviewPage_Sign_CorrectPassword_ApprovesReviewAndLocksSlot()
    {
        // Arrange: a signable review viewed by ann
        var (projectId, slotId, ids) = await CreateSignableAsync();
        var cut = RenderPage(projectId);
        await cut.Find($"[data-testid='sign-password-{slotId}']").InputAsync(SigningPassword);

        // Act: sign
        await cut.Find($"[data-testid='sign-form-{slotId}']").SubmitAsync();

        // Assert: signed, approved and the typed password is gone
        Assert.Equal("Signed by Ann on 2025-03-04 05:06 UTC", cut.Find($"[data-testid='slot-status-{slotId}']").TextContent);
        Assert.Equal("Approved", cut.Find("[data-testid='review-approval']").TextContent);
        Assert.Equal("Approved. Every reviewer slot has a current signature.", cut.Find("[data-testid='next-step']").TextContent);
        Assert.DoesNotContain(SigningPassword, cut.Markup, StringComparison.Ordinal);

        // Assert: the slot cannot be changed or signed again from the page
        Assert.Empty(cut.FindAll($"[data-testid='slot-save-{slotId}']"));
        Assert.Empty(cut.FindAll($"[data-testid='slot-remove-{slotId}']"));
        Assert.Empty(cut.FindAll($"[data-testid='sign-form-{slotId}']"));
        var signature = Assert.Single(await Store.ListSignaturesAsync(projectId, "REV-1", TestContext.Current.CancellationToken));
        Assert.Equal((ids["ann"], SlotRole.Primary), (signature.UserId, signature.Role));
    }

    /// <summary>
    ///     Test that a signer without a second factor sees no code field in the sign form.
    /// </summary>
    [Fact]
    public async Task ReviewPage_Render_SignerNotEnrolled_ShowsNoCodeField()
    {
        // Arrange: a signable review viewed by ann, who has no second factor
        var (projectId, slotId, _) = await CreateSignableAsync();

        // Act: render the page
        var cut = RenderPage(projectId);

        // Assert: the password is asked for, the code is not
        Assert.NotNull(cut.Find($"[data-testid='sign-password-{slotId}']"));
        Assert.Empty(cut.FindAll($"[data-testid='sign-totp-{slotId}']"));
    }

    /// <summary>
    ///     Test that an enrolled signer sees the code field, and that the password and a fresh code sign.
    /// </summary>
    [Fact]
    public async Task ReviewPage_Sign_EnrolledSignerWithPasswordAndFreshCode_SignsAndClearsFields()
    {
        // Arrange: a signable review viewed by ann, who has a second factor; her enrollment code is from an earlier step
        var (projectId, slotId, _) = await CreateSignableAsync();
        var (secret, _) = await EnrollTotpAsync("ann", SigningPassword);
        Time.Advance(TimeSpan.FromSeconds(30));
        var cut = RenderPage(projectId);
        var code = TotpOracle.CodeAt(secret, Time.GetUtcNow());
        Assert.Equal("one-time-code", cut.Find($"[data-testid='sign-totp-{slotId}']").GetAttribute("autocomplete"));
        await cut.Find($"[data-testid='sign-password-{slotId}']").InputAsync(SigningPassword);
        await cut.Find($"[data-testid='sign-totp-{slotId}']").InputAsync(code);

        // Act: sign
        await cut.Find($"[data-testid='sign-form-{slotId}']").SubmitAsync();

        // Assert: signed, and neither the password nor the code is left in the page
        Assert.StartsWith("Signed by Ann", cut.Find($"[data-testid='slot-status-{slotId}']").TextContent, StringComparison.Ordinal);
        Assert.Single(await Store.ListSignaturesAsync(projectId, "REV-1", TestContext.Current.CancellationToken));
        Assert.DoesNotContain(SigningPassword, cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain($"value=\"{code}\"", cut.Markup, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a missing or wrong code is refused with the message that does not say which of the two was
    ///     wrong, signs nothing, and leaves both fields empty.
    /// </summary>
    /// <param name="code">The code typed.</param>
    [Theory]
    [InlineData("")]
    [InlineData("000000")]
    public async Task ReviewPage_Sign_EnrolledSignerWithWrongCode_ShowsGenericErrorAndSignsNothing(string code)
    {
        // Arrange: a signable review viewed by ann, who has a second factor and types the right password
        var (projectId, slotId, _) = await CreateSignableAsync();
        await EnrollTotpAsync("ann", SigningPassword);
        Time.Advance(TimeSpan.FromSeconds(30));
        var cut = RenderPage(projectId);
        await cut.Find($"[data-testid='sign-password-{slotId}']").InputAsync(SigningPassword);
        await cut.Find($"[data-testid='sign-totp-{slotId}']").InputAsync(code);

        // Act: sign
        await cut.Find($"[data-testid='sign-form-{slotId}']").SubmitAsync();

        // Assert: the generic message, no signature, and both fields are empty
        Assert.Equal("The password or the code is incorrect.", cut.Find($"[data-testid='sign-error-{slotId}']").TextContent);
        Assert.Empty(await Store.ListSignaturesAsync(projectId, "REV-1", TestContext.Current.CancellationToken));
        Assert.Empty(cut.Find($"[data-testid='sign-password-{slotId}']").GetAttribute("value") ?? string.Empty);
        Assert.Empty(cut.Find($"[data-testid='sign-totp-{slotId}']").GetAttribute("value") ?? string.Empty);
    }

    /// <summary>
    ///     Test that a code that was already used in its 30-second step is refused, and that the next code works.
    /// </summary>
    [Fact]
    public async Task ReviewPage_Sign_CodeAlreadyUsedInStep_IsRefusedUntilTheNextCode()
    {
        // Arrange: a signable review viewed by ann; the code of the step in which she enrolled was already used
        var (projectId, slotId, _) = await CreateSignableAsync();
        var (secret, _) = await EnrollTotpAsync("ann", SigningPassword);
        var cut = RenderPage(projectId);
        await cut.Find($"[data-testid='sign-password-{slotId}']").InputAsync(SigningPassword);
        await cut.Find($"[data-testid='sign-totp-{slotId}']").InputAsync(TotpOracle.CodeAt(secret, Time.GetUtcNow()));

        // Act: sign with the used code
        await cut.Find($"[data-testid='sign-form-{slotId}']").SubmitAsync();

        // Assert: refused
        Assert.NotNull(cut.Find($"[data-testid='sign-error-{slotId}']"));
        Assert.Empty(await Store.ListSignaturesAsync(projectId, "REV-1", TestContext.Current.CancellationToken));

        // Act: wait for the next step and sign with its code
        Time.Advance(TimeSpan.FromSeconds(30));
        await cut.Find($"[data-testid='sign-password-{slotId}']").InputAsync(SigningPassword);
        await cut.Find($"[data-testid='sign-totp-{slotId}']").InputAsync(TotpOracle.CodeAt(secret, Time.GetUtcNow()));
        await cut.Find($"[data-testid='sign-form-{slotId}']").SubmitAsync();

        // Assert: signed
        Assert.StartsWith("Signed by Ann", cut.Find($"[data-testid='slot-status-{slotId}']").TextContent, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that signing with the right password and a recovery code in place of the code is refused.
    /// </summary>
    [Fact]
    public async Task ReviewPage_Sign_RecoveryCodeInsteadOfCode_IsRefused()
    {
        // Arrange: a signable review viewed by ann, who types a recovery code
        var (projectId, slotId, _) = await CreateSignableAsync();
        var (_, recovery) = await EnrollTotpAsync("ann", SigningPassword);
        Time.Advance(TimeSpan.FromSeconds(30));
        var cut = RenderPage(projectId);
        await cut.Find($"[data-testid='sign-password-{slotId}']").InputAsync(SigningPassword);
        await cut.Find($"[data-testid='sign-totp-{slotId}']").InputAsync(recovery[0]);

        // Act: sign
        await cut.Find($"[data-testid='sign-form-{slotId}']").SubmitAsync();

        // Assert: refused, nothing signed
        Assert.NotNull(cut.Find($"[data-testid='sign-error-{slotId}']"));
        Assert.Empty(await Store.ListSignaturesAsync(projectId, "REV-1", TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Test that a change to the record after signing shows the signature as out of date, takes the approval
    ///     away and offers to sign again.
    /// </summary>
    [Fact]
    public async Task ReviewPage_Render_RecordChangedAfterSigning_ShowsOutOfDateAndInReview()
    {
        // Arrange: a signed slot
        var (projectId, slotId, _) = await CreateSignableAsync();
        var cut = RenderPage(projectId);
        await cut.Find($"[data-testid='sign-password-{slotId}']").InputAsync(SigningPassword);
        await cut.Find($"[data-testid='sign-form-{slotId}']").SubmitAsync();

        // Act: a comment changes the record, and the page is opened again
        await Threads.StartThreadAsync(projectId, "REV-1", "a.txt", null, null, TestActors.Bob, "Late", TestContext.Current.CancellationToken);
        await Threads.ResolveAsync(
            projectId, "REV-1", (await Threads.ListAsync(projectId, "REV-1", TestContext.Current.CancellationToken)).Single().Id, TestContext.Current.CancellationToken);
        var reopened = RenderPage(projectId);

        // Assert: out of date and In review, the slot is still locked, and signing again is offered
        Assert.Equal("Signature out of date", reopened.Find($"[data-testid='slot-status-{slotId}']").TextContent);
        Assert.Equal("In review", reopened.Find("[data-testid='review-approval']").TextContent);
        Assert.Empty(reopened.FindAll($"[data-testid='slot-save-{slotId}']"));
        Assert.NotNull(reopened.Find($"[data-testid='sign-form-{slotId}']"));
    }

    /// <summary>
    ///     Test that signing a record that changed since the page was drawn is refused and signs nothing.
    /// </summary>
    [Fact]
    public async Task ReviewPage_Sign_RecordChangedSincePageDrawn_IsRefused()
    {
        // Arrange: a signable review drawn for ann, then changed by somebody else
        var (projectId, slotId, _) = await CreateSignableAsync();
        var cut = RenderPage(projectId);
        await Threads.StartThreadAsync(projectId, "REV-1", "a.txt", null, null, TestActors.Bob, "Late", TestContext.Current.CancellationToken);
        await Threads.ResolveAsync(
            projectId, "REV-1", (await Threads.ListAsync(projectId, "REV-1", TestContext.Current.CancellationToken)).Single().Id, TestContext.Current.CancellationToken);
        await cut.Find($"[data-testid='sign-password-{slotId}']").InputAsync(SigningPassword);

        // Act: sign what was on screen
        await cut.Find($"[data-testid='sign-form-{slotId}']").SubmitAsync();

        // Assert: refused with the reason, nothing stored
        Assert.Contains("changed", cut.Find($"[data-testid='sign-error-{slotId}']").TextContent, StringComparison.Ordinal);
        Assert.Empty(await Store.ListSignaturesAsync(projectId, "REV-1", TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Test that the full record digest is in the collapsed details, shortened on screen with the whole value
    ///     in the title.
    /// </summary>
    [Fact]
    public async Task ReviewPage_Render_Details_ShowShortDigestWithFullValueInTitle()
    {
        // Arrange: a review
        var projectId = await CreateReviewAsync(new UploadedFile("a.txt", TestContent.A));
        var approval = await Signing.GetApprovalAsync(projectId, "REV-1", TestContext.Current.CancellationToken);

        // Act: render the page
        var cut = RenderPage(projectId);

        // Assert: inside the details, 16 characters shown and all 64 in the title
        var digest = cut.Find("[data-testid='review-details'] [data-testid='record-digest']");
        Assert.Equal(approval!.Digest[..16], digest.TextContent);
        Assert.Equal(approval.Digest, digest.GetAttribute("title"));
        Assert.Equal(64, approval.Digest.Length);
    }

    /// <summary>
    ///     Test that a user-controlled display name is shown as plain text in the Reviewers card.
    /// </summary>
    [Fact]
    public async Task ReviewPage_Render_SlotDisplayName_IsShownAsPlainText()
    {
        // Arrange: the signed-in user, and a user whose display name looks like markup, in a slot
        await CreateUsersAsync();
        var user = await Users.CreateAsync("eve", "<b>Eve</b>", UserRole.Reviewer, SigningPassword, TestContext.Current.CancellationToken);
        var projectId = await CreateReviewAsync(new UploadedFile("a.txt", TestContent.A));
        var slot = await Signing.AddSlotAsync(TestActors.Ann, projectId, "REV-1", user.Id, null, TestContext.Current.CancellationToken);

        // Act: render the page
        var cut = RenderPage(projectId);

        // Assert: the text is the literal name and no element was created from it
        var primary = cut.Find($"[data-testid='slot-primary-{slot.Id}']");
        Assert.Equal("<b>Eve</b>", primary.TextContent);
        Assert.Empty(primary.QuerySelectorAll("b"));
    }

    /// <summary>
    ///     Test that every interactive element of the Reviewers card, in all its variants, has a kebab-case
    ///     <c>data-testid</c>.
    /// </summary>
    [Fact]
    public async Task ReviewPage_Render_ReviewersCard_EveryInteractiveElementHasTestId()
    {
        // Arrange: one slot ready to sign with an error shown, and one signed by bob
        var (projectId, slotId, ids) = await CreateSignableAsync();
        await Signing.AddSlotAsync(TestActors.Ann, projectId, "REV-1", ids["cyd"], null, TestContext.Current.CancellationToken);
        var cut = RenderPage(projectId);
        await cut.Find($"[data-testid='sign-form-{slotId}']").SubmitAsync();
        await cut.Find("[data-testid='slot-add-form']").SubmitAsync();

        // Act: collect the interactive elements inside the card
        var elements = cut.Find("[data-testid='reviewers-card']").QuerySelectorAll("a, button, input, select, textarea");

        // Assert: there are many, and each has a kebab-case test id
        Assert.True(elements.Length > 8);
        foreach (var element in elements)
        {
            var testId = element.GetAttribute("data-testid");
            Assert.True(
                testId is not null && System.Text.RegularExpressions.Regex.IsMatch(testId, "^[a-z0-9]+(?:[-.][a-z0-9]+)*$"),
                $"A <{element.LocalName}> has no kebab-case data-testid (found '{testId}').");
        }
    }

    /// <summary>
    ///     Test that when the user of an open review page is disabled, the page stops showing its content and
    ///     actions, and nothing the stale page could still send is stored.
    /// </summary>
    [Fact]
    public async Task ReviewPage_Revalidation_UserDisabled_PageStopsShowingAndAcceptingActions()
    {
        // Arrange: a signable review open for ann, whose session is re-checked every 100 ms
        var (projectId, slotId, _) = await CreateSignableAsync();
        UseRevalidation(TimeSpan.FromMilliseconds(100));
        SignInAs("ann", "Ann", UserRole.Reviewer);
        var cut = RenderPage(projectId);
        await cut.Find("[data-testid='file-select-0']").ClickAsync();
        var threadForm = cut.Find("[data-testid='new-thread-form']");
        var signForm = cut.Find($"[data-testid='sign-form-{slotId}']");
        var threadText = cut.Find("[data-testid='thread-text-input']");
        await threadText.InputAsync("Typed before the session ended");
        Assert.NotEmpty(cut.FindAll("[data-testid='review-page']"));

        // Act: an administrator disables ann, and the next check ends the session
        await Users.SetEnabledAsync("ann", false, TestContext.Current.CancellationToken);
        await cut.WaitForAssertionAsync(
            () => Assert.NotEmpty(cut.FindAll("[data-testid='session-ended']")),
            TimeSpan.FromSeconds(30));

        // Assert: the page, its forms and the typed text are gone
        Assert.Empty(cut.FindAll("[data-testid='review-page']"));
        Assert.Empty(cut.FindAll("[data-testid='new-thread-form']"));
        Assert.Empty(cut.FindAll($"[data-testid='sign-form-{slotId}']"));
        Assert.Empty(cut.FindAll($"[data-testid='sign-button-{slotId}']"));
        Assert.DoesNotContain("Typed before the session ended", cut.Markup, StringComparison.Ordinal);

        // Act: the stale forms are submitted anyway
        _ = await Record.ExceptionAsync(() => threadForm.SubmitAsync());
        _ = await Record.ExceptionAsync(() => signForm.SubmitAsync());

        // Assert: nothing was stored
        Assert.Empty(await Threads.ListAsync(projectId, "REV-1", TestContext.Current.CancellationToken));
        Assert.Empty(await Store.ListSignaturesAsync(projectId, "REV-1", TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Test that an action sent after the session ended, but before the page learned of it, stores nothing and
    ///     leaves the not-found page (the access check finds no user), instead of failing the page.
    /// </summary>
    [Fact]
    public async Task ReviewPage_StartThread_SessionEndedBeforePageKnows_ShowsNotFoundAndStoresNothing()
    {
        // Arrange: a selected file with text typed, and then the session ends without the page being told
        var projectId = await CreateReviewAsync(new UploadedFile("a.txt", TestContent.A));
        var cut = RenderPage(projectId);
        await cut.Find("[data-testid='file-select-0']").ClickAsync();
        await cut.Find("[data-testid='thread-text-input']").InputAsync("Please check");
        SignOutQuietly();

        // Act: the thread is started
        await cut.Find("[data-testid='new-thread-form']").SubmitAsync();

        // Assert: the page shows the not-found state, and no thread exists
        Assert.NotEmpty(cut.FindAll("[data-testid='review-not-found']"));
        Assert.Empty(cut.FindAll("[data-testid='new-thread-form']"));
        Assert.Empty(await Threads.ListAsync(projectId, "REV-1", TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Signs the only slot of <c>REV-1</c> as ann with the current record, which completes the review.
    /// </summary>
    /// <param name="projectId">The project identity.</param>
    /// <param name="slotId">The slot identity.</param>
    /// <returns>A task that completes when the slot is signed.</returns>
    private async Task SignSlotAsync(int projectId, int slotId)
    {
        var approval = await Signing.GetApprovalAsync(projectId, "REV-1", TestContext.Current.CancellationToken);
        await Signing.SignAsync(
            TestActors.Ann, projectId, "REV-1", slotId, approval!.Digest, SigningPassword, TestContext.Current.CancellationToken);
    }

    /// <summary>
    ///     Test that a review that is not complete does not offer the attestation.
    /// </summary>
    [Fact]
    public async Task ReviewPage_Render_ReviewNotApproved_OffersNoAttestation()
    {
        // Arrange: a signable review that nobody has signed
        var (projectId, _, _) = await CreateSignableAsync();

        // Act: render the page
        var cut = RenderPage(projectId);

        // Assert: nothing about an attestation
        Assert.Empty(cut.FindAll("[data-testid='attestation']"));
        Assert.Empty(cut.FindAll("[data-testid='attestation-download-button']"));
    }

    /// <summary>
    ///     Test that a complete review offers the attestation as a button-styled download of the endpoint, with the
    ///     note that the key is self-signed and a link to the help.
    /// </summary>
    [Fact]
    public async Task ReviewPage_Render_ReviewApprovedWithSelfSignedKey_OffersAttestationDownloadWithKeyNote()
    {
        // Arrange: a complete review and the default key, which is self-signed
        var (projectId, slotId, _) = await CreateSignableAsync();
        await SignSlotAsync(projectId, slotId);

        // Act: render the page
        var cut = RenderPage(projectId);

        // Assert: a button-styled download of the endpoint, the note and the help link
        var button = cut.Find("[data-testid='attestation-download-button']");
        Assert.Equal("Download attestation (PDF)", button.TextContent);
        Assert.Equal(ReviewLinks.ForAttestation(projectId, "REV-1"), button.GetAttribute("href"));
        Assert.Equal($"attestation-{projectId}-REV-1.pdf", button.GetAttribute("download"));
        Assert.Contains("action", button.ClassList);
        var note = cut.Find("[data-testid='attestation-note']").TextContent;
        Assert.Contains("self-signed key generated by this server - not an organization key", note, StringComparison.Ordinal);
        Assert.Equal("/help/signed-attestation", cut.Find("[data-testid='attestation-help-link']").GetAttribute("href"));
        Assert.Empty(cut.FindAll("[data-testid='attestation-key-problem']"));
    }

    /// <summary>
    ///     Test that the note for a self-signed certificate from the certificate store says that the administrator
    ///     supplied it, and does not claim that the server generated it.
    /// </summary>
    [Fact]
    public async Task ReviewPage_Render_ReviewApprovedWithSelfSignedCertificateFromStore_NoteSaysAdministratorSuppliedIt()
    {
        // Arrange: a complete review and a self-signed certificate that the certificate store provider supplies
        using var key = TestKeys.SelfSignedFromStore();
        using var keys = new FakeAttestationKeyProvider(key);
        Services.AddSingleton<IAttestationKeyProvider>(keys);
        var (projectId, slotId, _) = await CreateSignableAsync();
        await SignSlotAsync(projectId, slotId);

        // Act: render the page
        var cut = RenderPage(projectId);

        // Assert: the note says the certificate is self-signed and supplied by the administrator, not generated here
        var note = cut.Find("[data-testid='attestation-note']").TextContent;
        Assert.Contains("self-signed certificate supplied by the administrator from the certificate store", note, StringComparison.Ordinal);
        Assert.Contains("not an organization key", note, StringComparison.Ordinal);
        Assert.Contains("not issued by a certificate authority", note, StringComparison.Ordinal);
        Assert.DoesNotContain("generated by this server", note, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that the note names the certificate when the key is an organization key.
    /// </summary>
    [Fact]
    public async Task ReviewPage_Render_ReviewApprovedWithOrganizationKey_NoteNamesTheCertificate()
    {
        // Arrange: a complete review and an organization key
        using var key = TestKeys.Organization();
        var thumbprint = key.Info.Thumbprint;
        using var keys = new FakeAttestationKeyProvider(key);
        Services.AddSingleton<IAttestationKeyProvider>(keys);
        var (projectId, slotId, _) = await CreateSignableAsync();
        await SignSlotAsync(projectId, slotId);

        // Act: render the page
        var cut = RenderPage(projectId);

        // Assert: the note names the certificate and does not call the key self-signed
        var note = cut.Find("[data-testid='attestation-note']").TextContent;
        Assert.Contains("CN=Test organization signer", note, StringComparison.Ordinal);
        Assert.Contains(thumbprint[..8], note, StringComparison.Ordinal);
        Assert.DoesNotContain("self-signed", note, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a key that cannot be used shows the problem instead of the download.
    /// </summary>
    [Fact]
    public async Task ReviewPage_Render_ReviewApprovedWithKeyProblem_ShowsProblemInsteadOfDownload()
    {
        // Arrange: a complete review and a key provider that reports a problem
        using var keys = new FakeAttestationKeyProvider("The key store is locked.");
        Services.AddSingleton<IAttestationKeyProvider>(keys);
        var (projectId, slotId, _) = await CreateSignableAsync();
        await SignSlotAsync(projectId, slotId);

        // Act: render the page
        var cut = RenderPage(projectId);

        // Assert: an alert with the problem, and no download
        var problem = cut.Find("[data-testid='attestation-key-problem']");
        Assert.Equal("alert", problem.GetAttribute("role"));
        Assert.Contains("The key store is locked.", problem.TextContent, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("[data-testid='attestation-download-button']"));
    }

    /// <summary>
    ///     Test that the download appears on the page as soon as the last slot is signed through the page.
    /// </summary>
    [Fact]
    public async Task ReviewPage_Sign_CorrectPassword_OffersAttestationDownload()
    {
        // Arrange: a signable review viewed by ann, without a download yet
        var (projectId, slotId, _) = await CreateSignableAsync();
        var cut = RenderPage(projectId);
        Assert.Empty(cut.FindAll("[data-testid='attestation-download-button']"));
        await cut.Find($"[data-testid='sign-password-{slotId}']").InputAsync(SigningPassword);

        // Act: sign
        await cut.Find($"[data-testid='sign-form-{slotId}']").SubmitAsync();

        // Assert: the download is offered
        Assert.NotNull(cut.Find("[data-testid='attestation-download-button']"));
    }

    /// <summary>
    ///     The user name of the member whose access the revoke tests withdraw.
    /// </summary>
    private const string RevokedUser = "mem";

    /// <summary>
    ///     Creates a review whose every part (project name, files, comment, reviewer, checklist) carries a text that
    ///     must never reach a user without access: <c>mem</c> is a member, <c>out</c> is not.
    /// </summary>
    /// <returns>The project identity and the identity of the thread on <c>b.txt</c>.</returns>
    private async Task<(int ProjectId, int ThreadId)> CreateConfidentialReviewAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var member = await Users.CreateAsync(RevokedUser, "Zelda Quill", UserRole.Reviewer, SigningPassword, cancellationToken);
        await Users.CreateAsync("ann", "Ann", UserRole.Reviewer, SigningPassword, cancellationToken);
        var projectId = await CreateReviewAsync(
            new UploadedFile("a.txt", "alpha-confidential\n"u8.ToArray()),
            new UploadedFile("b.txt", "bravo-confidential\n"u8.ToArray()));
        await Users.CreateAsync("out", "Out", UserRole.Reviewer, SigningPassword, cancellationToken);
        var thread = await Threads.StartThreadAsync(
            projectId, "REV-1", "b.txt", 1, 1, TestActors.Ann, "confidential-thread-comment", cancellationToken);
        await AddReviewerSlotAsync(projectId, member.Id);
        var item = DefaultChecklist.Current.Sections.SelectMany(section => section.Items).First();
        await Checklists.SetAnswerAsync(projectId, "REV-1", item.Id, ChecklistOutcome.Pass, null, TestActors.Ann, cancellationToken);
        return (projectId, thread.Id);
    }

    /// <summary>
    ///     Gives the review a reviewer slot, so that the name of a user is part of what the page shows.
    /// </summary>
    /// <param name="projectId">The project identity.</param>
    /// <param name="userId">The identity of the user who occupies the slot.</param>
    /// <returns>A task that completes when the slot exists.</returns>
    private async Task AddReviewerSlotAsync(int projectId, int userId)
    {
        await Signing.AddSlotAsync(TestActors.Ann, projectId, "REV-1", userId, null, TestContext.Current.CancellationToken);
    }

    /// <summary>
    ///     Asserts that the page shows only the not-found message and nothing of the project, the review, its files,
    ///     comments, reviewers or checklist.
    /// </summary>
    /// <param name="cut">The rendered page.</param>
    private static void AssertNothingOfTheReviewIsShown(IRenderedComponent<ReviewPage> cut)
    {
        Assert.NotEmpty(cut.FindAll("[data-testid='review-not-found']"));
        Assert.Empty(cut.FindAll("[data-testid='review-fingerprint']"));
        Assert.Empty(cut.FindAll("[data-testid='selected-file-path']"));
        foreach (var secret in new[]
                 {
                     "Apollo", "First review", "a.txt", "b.txt", "alpha-confidential", "bravo-confidential",
                     "confidential-thread-comment", "Zelda Quill", "checks answered"
                 })
        {
            Assert.DoesNotContain(secret, cut.Markup, StringComparison.Ordinal);
        }
    }

    /// <summary>
    ///     Test that a user who is no member of the project gets the same page as for a review that does not exist and
    ///     sees nothing of the review.
    /// </summary>
    [Fact]
    public async Task ReviewPage_Render_NonMember_LooksLikeAMissingReview()
    {
        // Arrange: a confidential review, opened by a user who is no member
        var (projectId, _) = await CreateConfidentialReviewAsync();
        SignInAs("out", "Out", UserRole.Reviewer);

        // Act
        var cut = RenderPage(projectId);
        var missing = RenderPage(999);

        // Assert: the message is the one for a missing project and nothing of the review is shown
        Assert.Equal(
            missing.Find("[data-testid='review-not-found']").TextContent.Replace("project 999", "project #"),
            cut.Find("[data-testid='review-not-found']").TextContent.Replace($"project {projectId}", "project #"));
        AssertNothingOfTheReviewIsShown(cut);
    }

    /// <summary>
    ///     Test that a member who loses the membership while the review is open sees nothing of the review when
    ///     choosing a file: the file is not read and what was loaded is dropped.
    /// </summary>
    [Fact]
    public async Task ReviewPage_SelectFile_MembershipRevoked_ShowsNotFoundAndNoData()
    {
        // Arrange: the member has the review open and sees it
        var (projectId, _) = await CreateConfidentialReviewAsync();
        SignInAs(RevokedUser, "Zelda Quill", UserRole.Reviewer);
        var cut = RenderPage(projectId);
        Assert.Contains("b.txt", cut.Markup, StringComparison.Ordinal);
        await RevokeMembershipAsync(projectId, RevokedUser);

        // Act: choose the second file
        await cut.Find("[data-testid='file-select-1']").ClickAsync();

        // Assert: the page is the not-found page and holds none of the data
        AssertNothingOfTheReviewIsShown(cut);
    }

    /// <summary>
    ///     Test that a member who loses the membership while the review is open sees nothing of the review when
    ///     choosing a thread of the index.
    /// </summary>
    [Fact]
    public async Task ReviewPage_SelectThread_MembershipRevoked_ShowsNotFoundAndNoData()
    {
        // Arrange: the member has the review open and sees the thread in the index
        var (projectId, threadId) = await CreateConfidentialReviewAsync();
        SignInAs(RevokedUser, "Zelda Quill", UserRole.Reviewer);
        var cut = RenderPage(projectId);
        Assert.NotEmpty(cut.FindAll($"[data-testid='index-row-{threadId}']"));
        await RevokeMembershipAsync(projectId, RevokedUser);

        // Act: choose the thread
        await cut.Find($"[data-testid='index-row-{threadId}']").ClickAsync();

        // Assert
        AssertNothingOfTheReviewIsShown(cut);
    }

    /// <summary>
    ///     Test that a view-only action (filtering the thread index) of an open page also re-checks the access, so the
    ///     data that was loaded disappears at the first interaction after the removal.
    /// </summary>
    [Fact]
    public async Task ReviewPage_FilterIndex_MembershipRevoked_ShowsNotFoundAndNoData()
    {
        // Arrange: the member has the review open
        var (projectId, _) = await CreateConfidentialReviewAsync();
        SignInAs(RevokedUser, "Zelda Quill", UserRole.Reviewer);
        var cut = RenderPage(projectId);
        await RevokeMembershipAsync(projectId, RevokedUser);

        // Act: show all threads
        await cut.Find("[data-testid='index-filter-all']").ClickAsync();

        // Assert
        AssertNothingOfTheReviewIsShown(cut);
    }

    /// <summary>
    ///     Test that a reply posted after the membership was removed is refused, stores nothing and leaves only the
    ///     not-found page.
    /// </summary>
    [Fact]
    public async Task ReviewPage_Reply_MembershipRevoked_ShowsNotFoundAndStoresNothing()
    {
        // Arrange: the member has the file selected and has typed a reply
        var (projectId, threadId) = await CreateConfidentialReviewAsync();
        SignInAs(RevokedUser, "Zelda Quill", UserRole.Reviewer);
        var cut = RenderPage(projectId);
        await cut.Find("[data-testid='file-select-1']").ClickAsync();
        await cut.Find($"[data-testid='reply-text-{threadId}']").InputAsync("late reply");
        await RevokeMembershipAsync(projectId, RevokedUser);

        // Act: post the reply
        await cut.Find($"[data-testid='reply-button-{threadId}']").ClickAsync();

        // Assert: nothing is shown, and the thread still has its one comment
        AssertNothingOfTheReviewIsShown(cut);
        Assert.DoesNotContain("late reply", cut.Markup, StringComparison.Ordinal);
        var stored = Assert.Single(await Threads.ListAsync(projectId, "REV-1", TestContext.Current.CancellationToken));
        Assert.Single(stored.Comments);
    }
}
