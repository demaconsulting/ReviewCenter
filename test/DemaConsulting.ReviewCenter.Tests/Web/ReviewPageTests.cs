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
using DemaConsulting.ReviewCenter.Tests.Core;
using DemaConsulting.ReviewCenter.Web;
using Microsoft.AspNetCore.Components;

using TestContext = Xunit.TestContext;

namespace DemaConsulting.ReviewCenter.Tests.Web;

/// <summary>
///     Unit tests for the <see cref="ReviewPage"/> component, rendered in memory with bUnit.
/// </summary>
public class ReviewPageTests : PageTestBase
{
    /// <summary>
    ///     Creates a project with one review holding the given files.
    /// </summary>
    /// <param name="files">The files of the review, in the order they should be listed.</param>
    /// <returns>The project id.</returns>
    private async Task<int> CreateReviewAsync(params UploadedFile[] files)
    {
        var project = await Projects.CreateAsync("Apollo", null, TestContext.Current.CancellationToken);
        await Reviews.CreateDraftAsync(project.Id, "REV-1", "First review", files, TestContext.Current.CancellationToken);
        return project.Id;
    }

    /// <summary>
    ///     Renders the page for a review.
    /// </summary>
    /// <param name="projectId">The project identity.</param>
    /// <param name="reviewId">The review id.</param>
    /// <returns>The rendered component.</returns>
    private IRenderedComponent<ReviewPage> RenderPage(int projectId, string reviewId = "REV-1")
    {
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
        await Threads.StartThreadAsync(projectId, "REV-1", "a.txt", null, null, "Ann", "Hello", TestContext.Current.CancellationToken);
        var cut = RenderPage(projectId);
        await cut.Find("[data-testid='file-select-0']").ClickAsync();
        await cut.Find("[data-testid='thread-text-input']").InputAsync("draft text");
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
        var whole = await Threads.StartThreadAsync(projectId, "REV-1", "a.txt", null, null, "Ann", "Whole", TestContext.Current.CancellationToken);
        var range = await Threads.StartThreadAsync(projectId, "REV-1", "a.txt", 2, 3, "Bob", "Range", TestContext.Current.CancellationToken);
        await Threads.ReplyAsync(projectId, "REV-1", range.Id, "Cy", "Reply", TestContext.Current.CancellationToken);
        var resolved = await Threads.StartThreadAsync(projectId, "REV-1", "a.txt", 4, 4, "Dee", "Done", TestContext.Current.CancellationToken);
        await Threads.ResolveAsync(projectId, "REV-1", resolved.Id, TestContext.Current.CancellationToken);
        await Threads.StartThreadAsync(projectId, "REV-1", "b.txt", null, null, "Eve", "Other file", TestContext.Current.CancellationToken);
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

        // Act: fill in the author and text and start the thread
        await cut.Find("[data-testid='comment-author-input']").InputAsync("Ann");
        await cut.Find("[data-testid='thread-text-input']").InputAsync("Please check");
        await cut.Find("[data-testid='new-thread-form']").SubmitAsync();

        // Assert: stored and shown as an open whole-file thread; counts updated; text cleared; author kept
        var thread = Assert.Single(await Threads.ListAsync(projectId, "REV-1", TestContext.Current.CancellationToken));
        Assert.Null(thread.StartLine);
        Assert.Equal("Whole file", cut.Find($"[data-testid='thread-anchor-{thread.Id}']").TextContent);
        Assert.Equal("Ann", cut.Find($"[data-testid='comment-author-{thread.Id}-1']").TextContent);
        Assert.Equal("1", cut.Find("[data-testid='threads-open-count']").TextContent);
        Assert.Equal("1 open thread", cut.Find("[data-testid='file-open-threads-0']").TextContent);
        Assert.Empty(cut.FindAll("[data-testid='thread-error']"));
        Assert.Equal(string.Empty, cut.Find("[data-testid='thread-text-input']").GetAttribute("value") ?? string.Empty);
        Assert.Equal("Ann", cut.Find("[data-testid='comment-author-input']").GetAttribute("value"));
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
        await cut.Find("[data-testid='comment-author-input']").InputAsync("Ann");

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
    /// <param name="author">The author to enter.</param>
    /// <param name="start">The start line to enter.</param>
    /// <param name="text">The text to enter.</param>
    /// <param name="expected">A fragment of the expected message.</param>
    [Theory]
    [InlineData("", "", "Text", "author name is required")]
    [InlineData("Ann", "", "", "comment text is required")]
    [InlineData("Ann", "abc", "Text", "whole numbers")]
    [InlineData("Ann", "1.5", "Text", "whole numbers")]
    [InlineData("Ann", "9", "Text", "only 1 line")]
    public async Task ReviewPage_StartThread_InvalidInput_ShowsErrorAndKeepsValues(string author, string start, string text, string expected)
    {
        // Arrange: a selected one-line file
        var projectId = await CreateReviewAsync(new UploadedFile("a.txt", TestContent.A));
        var cut = RenderPage(projectId);
        await cut.Find("[data-testid='file-select-0']").ClickAsync();
        await cut.Find("[data-testid='comment-author-input']").InputAsync(author);
        await cut.Find("[data-testid='thread-start-line-input']").InputAsync(start);
        await cut.Find("[data-testid='thread-text-input']").InputAsync(text);

        // Act: submit the form
        await cut.Find("[data-testid='new-thread-form']").SubmitAsync();

        // Assert: the message is shown, the values are kept, and nothing is stored
        Assert.Contains(expected, cut.Find("[data-testid='thread-error']").TextContent, StringComparison.Ordinal);
        Assert.Equal(author, cut.Find("[data-testid='comment-author-input']").GetAttribute("value") ?? string.Empty);
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
        // Arrange: a selected binary file with the author entered
        var projectId = await CreateReviewAsync(new UploadedFile("c.bin", TestContent.C));
        var cut = RenderPage(projectId);
        await cut.Find("[data-testid='file-select-0']").ClickAsync();
        await cut.Find("[data-testid='comment-author-input']").InputAsync("Ann");
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
        // Arrange: a selected file with a thread, and the author entered
        var projectId = await CreateReviewAsync(new UploadedFile("a.txt", TestContent.A));
        var thread = await Threads.StartThreadAsync(projectId, "REV-1", "a.txt", null, null, "Ann", "First", TestContext.Current.CancellationToken);
        var cut = RenderPage(projectId);
        await cut.Find("[data-testid='file-select-0']").ClickAsync();
        await cut.Find("[data-testid='comment-author-input']").InputAsync("Bob");

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
        Assert.Equal(2, Assert.Single(await Threads.ListAsync(projectId, "REV-1", TestContext.Current.CancellationToken)).Comments.Count);
    }

    /// <summary>
    ///     Test that a reply without an author tells the operator where to enter the name.
    /// </summary>
    [Fact]
    public async Task ReviewPage_Reply_NoAuthor_ShowsErrorPointingToNameField()
    {
        // Arrange: a selected file with a thread and no author entered
        var projectId = await CreateReviewAsync(new UploadedFile("a.txt", TestContent.A));
        var thread = await Threads.StartThreadAsync(projectId, "REV-1", "a.txt", null, null, "Ann", "First", TestContext.Current.CancellationToken);
        var cut = RenderPage(projectId);
        await cut.Find("[data-testid='file-select-0']").ClickAsync();
        await cut.Find($"[data-testid='reply-text-{thread.Id}']").InputAsync("Second");

        // Act: post the reply
        await cut.Find($"[data-testid='reply-button-{thread.Id}']").ClickAsync();

        // Assert: the message names the field
        Assert.Contains("Your name", cut.Find($"[data-testid='reply-error-{thread.Id}']").TextContent, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that Resolve and Reopen toggle the thread state and update the summary.
    /// </summary>
    [Fact]
    public async Task ReviewPage_ResolveAndReopen_Toggle_ChangesStateAndSummary()
    {
        // Arrange: a selected file with an open thread
        var projectId = await CreateReviewAsync(new UploadedFile("a.txt", TestContent.A));
        var thread = await Threads.StartThreadAsync(projectId, "REV-1", "a.txt", null, null, "Ann", "First", TestContext.Current.CancellationToken);
        var cut = RenderPage(projectId);
        await cut.Find("[data-testid='file-select-0']").ClickAsync();

        // Act: resolve
        await cut.Find($"[data-testid='thread-toggle-{thread.Id}']").ClickAsync();

        // Assert: resolved, with the button offering to reopen, and the counts moved
        Assert.Equal("Resolved", cut.Find($"[data-testid='thread-state-{thread.Id}']").TextContent);
        Assert.Equal("Reopen", cut.Find($"[data-testid='thread-toggle-{thread.Id}']").TextContent);
        Assert.Equal("0", cut.Find("[data-testid='threads-open-count']").TextContent);
        Assert.Equal("1", cut.Find("[data-testid='threads-resolved-count']").TextContent);
        Assert.Equal("0 open threads", cut.Find("[data-testid='file-open-threads-0']").TextContent);

        // Act: reopen
        await cut.Find($"[data-testid='thread-toggle-{thread.Id}']").ClickAsync();

        // Assert: open again and the counts moved back
        Assert.Equal("Open", cut.Find($"[data-testid='thread-state-{thread.Id}']").TextContent);
        Assert.Equal("Resolve", cut.Find($"[data-testid='thread-toggle-{thread.Id}']").TextContent);
        Assert.Equal("1", cut.Find("[data-testid='threads-open-count']").TextContent);
        Assert.Equal("0", cut.Find("[data-testid='threads-resolved-count']").TextContent);
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
            projectId, "REV-1", "a.txt", null, null, "<i>Ann</i>", "<script>alert(1)</script> & <b>bold</b>", TestContext.Current.CancellationToken);
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
    ///     Test that every interactive element of the review page, with a file selected and threads present,
    ///     has a kebab-case <c>data-testid</c>.
    /// </summary>
    [Fact]
    public async Task ReviewPage_Render_ThreadControls_EveryInteractiveElementHasTestId()
    {
        // Arrange: a selected file with a thread and an error shown
        var projectId = await CreateReviewAsync(new UploadedFile("a.txt", "a\nb\n"u8.ToArray()));
        await Threads.StartThreadAsync(projectId, "REV-1", "a.txt", 1, 2, "Ann", "First", TestContext.Current.CancellationToken);
        var cut = RenderPage(projectId);
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
}
