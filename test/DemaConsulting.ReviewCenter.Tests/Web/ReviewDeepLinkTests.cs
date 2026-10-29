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

using System.Net;
using System.Text;
using Bunit;
using Bunit.TestDoubles;
using DemaConsulting.ReviewCenter.Core;
using DemaConsulting.ReviewCenter.Tests.Core;
using DemaConsulting.ReviewCenter.Web;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using TestContext = Xunit.TestContext;

namespace DemaConsulting.ReviewCenter.Tests.Web;

/// <summary>
///     Tests of the stable links of the review page: the file, line and thread addresses opened through the real
///     router, the highlight, the friendly messages for addresses that name nothing, the replacement of the address
///     as files and threads are chosen, the thread links, and the return to a deep link after signing in.
/// </summary>
public class ReviewDeepLinkTests : PageTestBase
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
    ///     The modified file: rows 0-8 unchanged (lines 1-9), row 9 removed (baseline line 10), row 10 added (head
    ///     line 10), rows 11-20 unchanged (lines 11-20), row 21 added (head line 21). Lines 10-13 and 18-20 show
    ///     around the changes; the other unchanged lines start collapsed.
    /// </summary>
    private const string ModPath = "src/mod.txt";

    /// <summary>
    ///     The added file (three lines, no baseline).
    /// </summary>
    private const string NewPath = "src/new.txt";

    /// <summary>
    ///     The deleted file (two baseline lines, no head lines).
    /// </summary>
    private const string GonePath = "src/gone.txt";

    /// <summary>
    ///     Joins numbered lines into a text with a final newline.
    /// </summary>
    /// <param name="count">The number of lines.</param>
    /// <param name="tenth">The text to use for line 10 instead of <c>l10</c>.</param>
    /// <returns>The text.</returns>
    private static string Lines(int count, string tenth = "l10")
    {
        return string.Concat(Enumerable.Range(1, count).Select(n => (n == 10 ? tenth : $"l{n}") + "\n"));
    }

    /// <summary>
    ///     Creates a repository review <c>REV-1</c> with a modified, an added and a deleted file, plus the given extra files.
    /// </summary>
    /// <param name="extra">More changed files.</param>
    /// <returns>The project identity.</returns>
    private async Task<int> CreateReviewAsync(params (string Path, ChangeKind Kind, string? Baseline, string? Head)[] extra)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var admin = new Actor("adm", "Adm");
        await Users.CreateAsync("adm", "Adm", UserRole.Admin, "correct horse battery", cancellationToken);
        var project = await Projects.CreateAsync("Apollo", null, cancellationToken);
        var repository = await Repositories.CreateAsync("main-repo", "https://example.com/org/repo.git", null, null, admin, cancellationToken);
        Git.Revisions["main"] = HeadSha;
        Git.Revisions["v1"] = BaseSha;
        List<(string Path, ChangeKind Kind, string? Baseline, string? Head)> changes =
        [
            (ModPath, ChangeKind.Modified, Lines(20), Lines(20, "n10") + "l21\n"),
            (NewPath, ChangeKind.Added, null, "one\ntwo\nthree\n"),
            (GonePath, ChangeKind.Deleted, "x1\nx2\n", null),
            .. extra
        ];
        Git.Script(BaseSha, HeadSha, changes);
        await Reviews.CreateFromRepositoryAsync(
            project.Id, "REV-1", "From git", new RepositorySource(repository.Id, "main", "v1", "src/**"), TestActors.Ann, cancellationToken);
        return project.Id;
    }

    /// <summary>
    ///     Creates an upload review <c>REV-1</c> whose files each have the lines <c>one</c>, <c>two</c> and <c>three</c>.
    /// </summary>
    /// <param name="paths">The file paths.</param>
    /// <returns>The project identity.</returns>
    private async Task<int> CreateUploadReviewAsync(params string[] paths)
    {
        var project = await Projects.CreateAsync("Apollo", null, TestContext.Current.CancellationToken);
        var files = paths.Select(path => new UploadedFile(path, Encoding.UTF8.GetBytes("one\ntwo\nthree\n"))).ToArray();
        await Reviews.CreateDraftAsync(project.Id, "REV-1", "Uploaded", files, TestContext.Current.CancellationToken);
        return project.Id;
    }

    /// <summary>
    ///     Starts a thread on <c>REV-1</c>.
    /// </summary>
    /// <param name="projectId">The project identity.</param>
    /// <param name="path">The file path.</param>
    /// <param name="start">The first line, or <c>null</c> for the whole file.</param>
    /// <param name="side">The version the line refers to.</param>
    /// <returns>The thread.</returns>
    private async Task<ReviewThread> StartAsync(int projectId, string path, int? start, ThreadSide side)
    {
        return await Threads.StartThreadAsync(
            projectId, "REV-1", new ThreadAnchor(path, start, start, side), TestActors.Bob, "Please check", TestContext.Current.CancellationToken);
    }

    /// <summary>
    ///     The router rendered by the last <see cref="Open"/>; every router follows the one navigation manager of the
    ///     test, so a test that opens a second address must first dispose of the first router.
    /// </summary>
    private IRenderedComponent<Routes>? _opened;

    /// <summary>
    ///     Opens an address through the real router, as a browser that is signed in as <c>ann</c> would.
    /// </summary>
    /// <param name="address">The address, relative to the site root and escaped as in a browser.</param>
    /// <returns>The rendered router, after the review has loaded.</returns>
    private IRenderedComponent<Routes> Open(string address)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        _opened?.Dispose();
        Services.GetRequiredService<NavigationManager>().NavigateTo(address);
        var cut = Render<Routes>();
        cut.WaitForElement("[data-testid='review-title']");
        _opened = cut;
        return cut;
    }

    /// <summary>
    ///     Gets the arguments of the last scroll request.
    /// </summary>
    /// <returns>The test id and the block.</returns>
    private object?[] LastScroll()
    {
        return [.. JSInterop.Invocations["reviewCenter.scrollToTestId"][^1].Arguments];
    }

    /// <summary>
    ///     Gets the address a redirect points to without the site origin, exactly as the server wrote it.
    /// </summary>
    /// <param name="response">The redirect response.</param>
    /// <returns>The path and query.</returns>
    private static string LocalPart(HttpResponseMessage response)
    {
        var written = response.Headers.Location!.OriginalString;
        // Only an http(s) address is absolute here: on Linux, Uri.TryCreate treats a rooted path such as "/login" as
        // an absolute file address, which would cut the path off
        return Uri.TryCreate(written, UriKind.Absolute, out var absolute) && absolute.Scheme is "http" or "https"
            ? written[absolute.GetLeftPart(UriPartial.Authority).Length..]
            : written;
    }

    /// <summary>
    ///     Gets the addresses the page asked the browser to put in the address bar.
    /// </summary>
    /// <returns>The addresses, in order.</returns>
    private object?[] ReplaceCalls()
    {
        return [.. JSInterop.Invocations.Where(i => i.Identifier == "reviewCenter.replaceUrl").Select(i => i.Arguments[0])];
    }

    /// <summary>
    ///     Tests that the file address opens that file as a diff, and that the review address opens none.
    /// </summary>
    [Fact]
    public async Task ReviewPage_FileAddress_ModifiedFile_OpensFileInDiff()
    {
        // Arrange: a review with a modified file
        var projectId = await CreateReviewAsync();

        // Act: open the file address
        var file = Open(ReviewLinks.ForFile(projectId, "REV-1", ModPath));

        // Assert: the file address shows the diff of that file, no message, and no highlighted line
        Assert.Equal(ModPath, file.Find("[data-testid='selected-file-path']").TextContent);
        Assert.Equal("true", file.Find("[data-testid='view-diff-button']").GetAttribute("aria-pressed"));
        Assert.Equal("2 added, 1 removed", file.Find("[data-testid='diff-summary']").TextContent);
        Assert.Empty(file.FindAll("[data-testid='deep-link-message']"));
        Assert.Empty(file.FindAll(".diff-row.focused"));

        // Act: open the plain review address
        var review = Open(ReviewLinks.ForReview(projectId, "REV-1"));

        // Assert: the review address still works and selects nothing
        Assert.Empty(review.FindAll("[data-testid='selected-file-path']"));
        Assert.Empty(review.FindAll("[data-testid='deep-link-message']"));
        Assert.Equal(3, review.FindAll("[data-testid^='file-row-']").Count);
    }

    /// <summary>
    ///     Tests that a head line is highlighted in the diff and scrolled into view.
    /// </summary>
    [Fact]
    public async Task ReviewPage_LineAddress_HeadLine_HighlightsAndScrollsToTheRow()
    {
        // Arrange: a review with a modified file
        var projectId = await CreateReviewAsync();

        // Act: open head line 10 (the added line that replaced baseline line 10)
        var cut = Open(ReviewLinks.ForFile(projectId, "REV-1", ModPath, 10));

        // Assert: only the right cell of that row is highlighted, and the browser is asked to scroll to it
        Assert.Equal(["diff-row-10"], cut.FindAll(".diff-row.focused").Select(e => e.GetAttribute("data-testid")).ToArray());
        Assert.Equal(["diff-row-10", "center"], LastScroll());
    }

    /// <summary>
    ///     Tests that a baseline line is highlighted on the left side.
    /// </summary>
    [Fact]
    public async Task ReviewPage_LineAddress_BaselineLine_HighlightsTheLeftCell()
    {
        // Arrange: a review with a modified file
        var projectId = await CreateReviewAsync();

        // Act: open baseline line 10 (the removed line)
        var removed = Open(ReviewLinks.ForFile(projectId, "REV-1", ModPath, 10, ThreadSide.Baseline));
        var removedRows = removed.FindAll(".diff-row.focused").Select(e => e.GetAttribute("data-testid")).ToArray();

        // Act: open baseline line 11 (an unchanged line)
        var context = Open(ReviewLinks.ForFile(projectId, "REV-1", ModPath, 11, ThreadSide.Baseline));
        var contextRows = context.FindAll(".diff-row.focused").Select(e => e.GetAttribute("data-testid")).ToArray();

        // Assert: the removed cell is highlighted; of the unchanged line the baseline copy is
        Assert.Equal(["diff-row-9"], removedRows);
        Assert.Equal(["diff-row-old-11"], contextRows);
    }

    /// <summary>
    ///     Tests that a line inside a run of collapsed unchanged lines is shown, so it can be highlighted.
    /// </summary>
    [Fact]
    public async Task ReviewPage_LineAddress_LineInCollapsedRun_IsShownAndHighlighted()
    {
        // Arrange: a review with a modified file; line 15 is far from both changes
        var projectId = await CreateReviewAsync();
        var plain = Open(ReviewLinks.ForFile(projectId, "REV-1", ModPath));
        Assert.Empty(plain.FindAll("[data-testid='diff-row-15']"));

        // Act: open line 15
        var cut = Open(ReviewLinks.ForFile(projectId, "REV-1", ModPath, 15));

        // Assert: the row is on the page and highlighted
        Assert.Contains("focused", cut.Find("[data-testid='diff-row-15']").ClassList);
    }

    /// <summary>
    ///     Tests that a line of an added file of a review with a baseline is highlighted in its diff.
    /// </summary>
    [Fact]
    public async Task ReviewPage_LineAddress_AddedFile_HighlightsTheDiffRow()
    {
        // Arrange: a review with an added file
        var projectId = await CreateReviewAsync();

        // Act: open line 2 of the added file
        var cut = Open(ReviewLinks.ForFile(projectId, "REV-1", NewPath, 2));

        // Assert: only the row of that line is highlighted and scrolled to
        Assert.Equal(["diff-row-1"], cut.FindAll(".diff-row.focused").Select(e => e.GetAttribute("data-testid")).ToArray());
        Assert.Equal(["diff-row-1", "center"], LastScroll());
    }

    /// <summary>
    ///     Tests that a line of an uploaded file is highlighted in the numbered text.
    /// </summary>
    [Fact]
    public async Task ReviewPage_LineAddress_UploadFile_HighlightsTheCodeLine()
    {
        // Arrange: an upload review
        var projectId = await CreateUploadReviewAsync("a.txt");

        // Act: open line 2 of the file
        var cut = Open(ReviewLinks.ForFile(projectId, "REV-1", "a.txt", 2));

        // Assert: the line of the numbered text is highlighted and scrolled to, the others are not
        Assert.Contains("focused", cut.Find("[data-testid='code-line-2']").ClassList);
        Assert.DoesNotContain("focused", cut.Find("[data-testid='code-line-1']").ClassList);
        Assert.Equal(["code-line-2", "center"], LastScroll());
    }

    /// <summary>
    ///     Tests that the baseline text of a deleted file can be addressed by line, and that it has no head lines.
    /// </summary>
    [Fact]
    public async Task ReviewPage_LineAddress_DeletedFile_BaselineLineYesHeadLineNo()
    {
        // Arrange: a review with a deleted file
        var projectId = await CreateReviewAsync();

        // Act: open baseline line 2
        var baseline = Open(ReviewLinks.ForFile(projectId, "REV-1", GonePath, 2, ThreadSide.Baseline));
        var focusedCount = baseline.FindAll(".diff-row.focused").Count;
        var baselineMessages = baseline.FindAll("[data-testid='deep-link-message']").Count;

        // Act: open head line 2
        var head = Open(ReviewLinks.ForFile(projectId, "REV-1", GonePath, 2));

        // Assert: the baseline line is highlighted; the head has no such line
        Assert.Equal(1, focusedCount);
        Assert.Equal(0, baselineMessages);
        Assert.Contains("Line 2 doesn't exist in this file.", head.Find("[data-testid='deep-link-message']").TextContent, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Tests that the thread address opens the file of the thread at its anchor and highlights the thread.
    /// </summary>
    [Fact]
    public async Task ReviewPage_ThreadAddress_HeadThread_OpensFileAndHighlightsThread()
    {
        // Arrange: a thread on head line 15 of the modified file, and one on another file
        var projectId = await CreateReviewAsync();
        var thread = await StartAsync(projectId, ModPath, 15, ThreadSide.Head);
        await StartAsync(projectId, NewPath, 1, ThreadSide.Head);

        // Act: open the thread address
        var cut = Open(ReviewLinks.ForThread(projectId, "REV-1", thread.Id));

        // Assert: the file is open in the diff, the line and the thread are highlighted, and the page scrolls to the row
        Assert.Equal(ModPath, cut.Find("[data-testid='selected-file-path']").TextContent);
        Assert.Equal("true", cut.Find("[data-testid='view-diff-button']").GetAttribute("aria-pressed"));
        Assert.Contains("focused", cut.Find($"[data-testid='thread-{thread.Id}']").ClassList);
        Assert.Contains("focused", cut.Find("[data-testid='diff-row-15']").ClassList);
        Assert.Equal(["diff-row-15", "center"], LastScroll());
        Assert.Empty(cut.FindAll("[data-testid='deep-link-message']"));
    }

    /// <summary>
    ///     Tests that a head thread of an added file opens the diff and highlights the row of the thread.
    /// </summary>
    [Fact]
    public async Task ReviewPage_ThreadAddress_AddedFileHeadThread_OpensDiffAndHighlightsRow()
    {
        // Arrange: a head thread on line 1 of the added file
        var projectId = await CreateReviewAsync();
        var thread = await StartAsync(projectId, NewPath, 1, ThreadSide.Head);

        // Act: open the thread address
        var cut = Open(ReviewLinks.ForThread(projectId, "REV-1", thread.Id));

        // Assert: the diff is shown with the row highlighted and the thread under it
        Assert.Equal(NewPath, cut.Find("[data-testid='selected-file-path']").TextContent);
        Assert.Equal("true", cut.Find("[data-testid='view-diff-button']").GetAttribute("aria-pressed"));
        Assert.Contains("focused", cut.Find("[data-testid='diff-row-0']").ClassList);
        Assert.Equal(["diff-row-0", "center"], LastScroll());
        Assert.NotEmpty(cut.FindAll($"[data-testid='diff-threads-0'] [data-testid='thread-{thread.Id}']"));
    }

    /// <summary>
    ///     Tests that a resolved thread on the baseline side is opened expanded, with its baseline line highlighted.
    /// </summary>
    [Fact]
    public async Task ReviewPage_ThreadAddress_ResolvedBaselineThread_IsExpandedAndHighlightedOnTheLeft()
    {
        // Arrange: a resolved thread on baseline line 10
        var projectId = await CreateReviewAsync();
        var thread = await StartAsync(projectId, ModPath, 10, ThreadSide.Baseline);
        await Threads.ResolveAsync(projectId, "REV-1", thread.Id, TestActors.Bob, TestContext.Current.CancellationToken);

        // Act: open the thread address
        var cut = Open(ReviewLinks.ForThread(projectId, "REV-1", thread.Id));

        // Assert: the comments are visible and the removed line is highlighted
        Assert.NotEmpty(cut.FindAll($"[data-testid='comment-{thread.Id}-1']"));
        Assert.Equal(["diff-row-9"], cut.FindAll(".diff-row.focused").Select(e => e.GetAttribute("data-testid")).ToArray());
    }

    /// <summary>
    ///     Tests that a thread address of a baseline thread on a file too large to compare shows the baseline text with
    ///     the lines of the thread highlighted (and only those), whatever the numbering of the head.
    /// </summary>
    [Fact]
    public async Task ReviewPage_ThreadAddress_BaselineThreadOnTooLargeFile_HighlightsItsBaselineLines()
    {
        // Arrange: a file too large to compare, and a baseline thread over three lines of it
        var big = string.Concat(Enumerable.Repeat("x\n", ReviewLimits.MaxDiffLines + 1));
        var projectId = await CreateReviewAsync(("src/big.txt", ChangeKind.Modified, big, "y\nz\n"));
        var thread = await Threads.StartThreadAsync(
            projectId, "REV-1", new ThreadAnchor("src/big.txt", 9000, 9002, ThreadSide.Baseline), TestActors.Bob, "Please check", TestContext.Current.CancellationToken);

        // Act: open the thread address
        var cut = Open(ReviewLinks.ForThread(projectId, "REV-1", thread.Id));

        // Assert: the baseline text is shown and exactly the lines of the thread are highlighted
        Assert.NotEmpty(cut.FindAll("[data-testid='baseline-content']"));
        Assert.Equal(
            ["baseline-line-9000", "baseline-line-9001", "baseline-line-9002"],
            cut.FindAll("[data-testid='baseline-content'] .code-line.focused").Select(e => e.GetAttribute("data-testid")).ToArray());
    }

    /// <summary>
    ///     Tests that the highlight of a thread address follows the side of the thread: switching to the other version
    ///     of the file does not highlight lines of the same numbers there.
    /// </summary>
    [Fact]
    public async Task ReviewPage_ThreadAddress_BaselineThread_DoesNotHighlightTheSameLinesOfTheHead()
    {
        // Arrange: a file too large to compare, with a baseline thread on line 2 (the head also has a line 2)
        var big = string.Concat(Enumerable.Repeat("x\n", ReviewLimits.MaxDiffLines + 1));
        var projectId = await CreateReviewAsync(("src/big.txt", ChangeKind.Modified, big, "y\nz\n"));
        var thread = await StartAsync(projectId, "src/big.txt", 2, ThreadSide.Baseline);
        var cut = Open(ReviewLinks.ForThread(projectId, "REV-1", thread.Id));
        Assert.NotEmpty(cut.FindAll("[data-testid='baseline-line-2'].focused"));

        // Act: show the head version of the file
        await cut.Find("[data-testid='view-head-button']").ClickAsync();

        // Assert: the head lines are shown and none is highlighted
        Assert.NotEmpty(cut.FindAll("[data-testid='file-content']"));
        Assert.Empty(cut.FindAll("[data-testid='file-content'] .code-line.focused"));
    }

    /// <summary>
    ///     Tests that a thread on the whole file is highlighted and scrolled to.
    /// </summary>
    [Fact]
    public async Task ReviewPage_ThreadAddress_WholeFileThread_ScrollsToTheThread()
    {
        // Arrange: a whole-file thread on the added file
        var projectId = await CreateReviewAsync();
        var thread = await StartAsync(projectId, NewPath, null, ThreadSide.Head);

        // Act: open the thread address
        var cut = Open(ReviewLinks.ForThread(projectId, "REV-1", thread.Id));

        // Assert: the file is open and the thread is highlighted and scrolled to
        Assert.Equal(NewPath, cut.Find("[data-testid='selected-file-path']").TextContent);
        Assert.Contains("focused", cut.Find($"[data-testid='thread-{thread.Id}']").ClassList);
        Assert.Equal([$"thread-{thread.Id}", "center"], LastScroll());
    }

    /// <summary>
    ///     Tests that an address that names nothing gives a friendly message with a way back, and keeps the file list.
    /// </summary>
    /// <param name="what">Which kind of address to build.</param>
    /// <param name="message">The start of the expected message.</param>
    [Theory]
    [InlineData("unknown-file", "This review has no file named 'src/nope.txt'.")]
    [InlineData("unknown-thread", "Thread #99999 doesn't exist in this review.")]
    [InlineData("other-review-thread", "doesn't exist in this review.")]
    [InlineData("baseline-of-added-file", "This file has no baseline version, so there is no baseline line 1.")]
    [InlineData("line-too-large", "Line 999 doesn't exist in this file.")]
    [InlineData("line-zero", "'0' is not a line number")]
    [InlineData("line-negative", "'-1' is not a line number")]
    [InlineData("line-text", "'abc' is not a line number")]
    public async Task ReviewPage_Address_NamesNothing_ShowsMessageWithWayBack(string what, string message)
    {
        // Arrange: a review, and a thread of another review of the same project
        var projectId = await CreateReviewAsync();
        await Reviews.CreateDraftAsync(
            projectId, "REV-2", "Other", [new UploadedFile("x.txt", Encoding.UTF8.GetBytes("a\n"))], TestContext.Current.CancellationToken);
        var foreign = await Threads.StartThreadAsync(
            projectId, "REV-2", new ThreadAnchor("x.txt", 1, 1, ThreadSide.Head), TestActors.Bob, "Elsewhere", TestContext.Current.CancellationToken);
        var address = what switch
        {
            "unknown-file" => ReviewLinks.ForFile(projectId, "REV-1", "src/nope.txt"),
            "unknown-thread" => ReviewLinks.ForThread(projectId, "REV-1", 99999),
            "other-review-thread" => ReviewLinks.ForThread(projectId, "REV-1", foreign.Id),
            "baseline-of-added-file" => ReviewLinks.ForFile(projectId, "REV-1", NewPath, 1, ThreadSide.Baseline),
            "line-too-large" => ReviewLinks.ForFile(projectId, "REV-1", ModPath, 999),
            "line-zero" => $"{ReviewLinks.ForFile(projectId, "REV-1", ModPath)}?line=0",
            "line-negative" => $"{ReviewLinks.ForFile(projectId, "REV-1", ModPath)}?line=-1",
            _ => $"{ReviewLinks.ForFile(projectId, "REV-1", ModPath)}?line=abc"
        };

        // Act: open the address
        var cut = Open(address);

        // Assert: a message in the page, not an error page, with a link back to the review and the file list intact
        var shown = cut.Find("[data-testid='deep-link-message']");
        Assert.Contains(message, shown.TextContent, StringComparison.Ordinal);
        Assert.Equal(ReviewLinks.ForReview(projectId, "REV-1"), cut.Find("[data-testid='deep-link-back']").GetAttribute("href"));
        Assert.Equal(3, cut.FindAll("[data-testid^='file-row-']").Count);
        Assert.Empty(cut.FindAll(".diff-row.focused"));
        Assert.Empty(cut.FindAll(".code-line.focused"));
        Assert.DoesNotContain("Page not found", cut.Markup, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Tests that a line address with an unknown side value is read as the head, and that the message goes
    ///     away when the reader chooses a file.
    /// </summary>
    [Fact]
    public async Task ReviewPage_Address_UnknownSideAndLaterChoice_UsesHeadAndClearsMessage()
    {
        // Arrange: a review, and an address with a nonsense side
        var projectId = await CreateReviewAsync();
        var cut = Open($"{ReviewLinks.ForFile(projectId, "REV-1", ModPath)}?line=10&side=SIDEWAYS");
        Assert.Equal(["diff-row-10"], cut.FindAll(".diff-row.focused").Select(e => e.GetAttribute("data-testid")).ToArray());

        // Act: open a missing file, then choose a file from the list
        var missing = Open(ReviewLinks.ForFile(projectId, "REV-1", "src/nope.txt"));
        Assert.NotEmpty(missing.FindAll("[data-testid='deep-link-message']"));
        await missing.Find("[data-testid='file-select-1']").ClickAsync();

        // Assert: the message is gone
        Assert.Empty(missing.FindAll("[data-testid='deep-link-message']"));
    }

    /// <summary>
    ///     Tests that choosing a file or a thread replaces the address through the browser helper: the address
    ///     matches what is shown, and no navigation (and so no history entry and no reset of the page) happens.
    /// </summary>
    [Fact]
    public async Task ReviewPage_Choose_FileAndThread_ReplacesAddressWithoutNavigating()
    {
        // Arrange: a review with a thread, opened at its plain address
        var projectId = await CreateReviewAsync();
        var thread = await StartAsync(projectId, ModPath, 15, ThreadSide.Head);
        var cut = Open(ReviewLinks.ForReview(projectId, "REV-1"));
        var navigation = Services.GetRequiredService<NavigationManager>();
        var before = navigation.Uri;
        var history = Services.GetRequiredService<BunitNavigationManager>().History.Count;
        Assert.Empty(ReplaceCalls());

        // Act: choose the added file, then choose the thread in the index
        await cut.Find("[data-testid='file-select-2']").ClickAsync();
        var afterFile = ReplaceCalls();
        await cut.Find("[data-testid='index-row-" + thread.Id + "']").ClickAsync();
        var afterThread = ReplaceCalls();

        // Assert: each choice asked the browser to replace the address by the matching link
        Assert.Equal([ReviewLinks.ForFile(projectId, "REV-1", NewPath)], afterFile);
        Assert.Equal(
            [ReviewLinks.ForFile(projectId, "REV-1", NewPath), ReviewLinks.ForThread(projectId, "REV-1", thread.Id)],
            afterThread);

        // Assert: the router was not involved: same location, no history entry
        Assert.Equal(before, navigation.Uri);
        Assert.Equal(history, Services.GetRequiredService<BunitNavigationManager>().History.Count);
    }

    /// <summary>
    ///     Tests that opening a deep link does not rewrite the address (it already is the link), and that text typed
    ///     on the page survives choosing a thread of the same file.
    /// </summary>
    [Fact]
    public async Task ReviewPage_OpenDeepLink_DoesNotReplaceAddress()
    {
        // Arrange and Act: open a line link
        var projectId = await CreateReviewAsync();
        Open(ReviewLinks.ForFile(projectId, "REV-1", ModPath, 10));

        // Assert: the browser was never asked to change the address
        Assert.Empty(ReplaceCalls());
    }

    /// <summary>
    ///     Tests that each thread shows a button-styled link to its address and a "Copy link" button.
    /// </summary>
    [Fact]
    public async Task ReviewPage_Thread_ShowsLinkButtonAndCopyLinkControl()
    {
        // Arrange: threads in the diff and on a whole file
        var projectId = await CreateReviewAsync();
        var inDiff = await StartAsync(projectId, ModPath, 15, ThreadSide.Head);
        var whole = await StartAsync(projectId, NewPath, null, ThreadSide.Head);

        // Act: open each file
        var diff = Open(ReviewLinks.ForFile(projectId, "REV-1", ModPath));
        var diffLink = diff.Find($"[data-testid='thread-link-{inDiff.Id}']");
        var diffTag = diffLink.TagName.ToLowerInvariant();
        var diffText = diffLink.TextContent;
        var diffHref = diffLink.GetAttribute("href");
        var diffClasses = diffLink.ClassList.ToArray();
        var diffCopy = diff.Find($"[data-testid='thread-copy-link-{inDiff.Id}']");
        var list = Open(ReviewLinks.ForFile(projectId, "REV-1", NewPath));
        var listCopy = list.Find($"[data-testid='thread-copy-link-{whole.Id}']");

        // Assert: the link is an anchor to the thread address with the button style
        Assert.Equal("a", diffTag);
        Assert.Equal("Link", diffText);
        Assert.Contains("thread-link", diffClasses);
        Assert.Contains("action", diffClasses);
        Assert.Equal(ReviewLinks.ForThread(projectId, "REV-1", inDiff.Id), diffHref);
        Assert.Equal(ReviewLinks.ForThread(projectId, "REV-1", whole.Id), list.Find($"[data-testid='thread-link-{whole.Id}']").GetAttribute("href"));

        // Assert: a "Copy link" button sits beside it, in the diff and in the whole-file list
        Assert.Equal("button", diffCopy.TagName.ToLowerInvariant());
        Assert.Equal("button", diffCopy.GetAttribute("type"));
        Assert.Equal("Copy link", diffCopy.TextContent);
        Assert.Equal("Copy link", listCopy.TextContent);
    }

    /// <summary>
    ///     Tests that pressing "Copy link" hands the browser the absolute address of the thread, shows "Copied" in the
    ///     status next to the button (which keeps reading "Copy link"), and clears it after a short delay.
    /// </summary>
    [Fact]
    public async Task ReviewPage_CopyLink_Click_CopiesAbsoluteThreadAddressAndShowsCopied()
    {
        // Arrange: a thread, and a browser that copies successfully
        var projectId = await CreateReviewAsync();
        var thread = await StartAsync(projectId, ModPath, 15, ThreadSide.Head);
        JSInterop.Setup<bool>("reviewCenter.copyText", _ => true).SetResult(true);
        var cut = Open(ReviewLinks.ForFile(projectId, "REV-1", ModPath));
        var button = $"[data-testid='thread-copy-link-{thread.Id}']";
        var status = $"[data-testid='thread-copy-status-{thread.Id}']";

        // Act: press the button
        await cut.Find(button).ClickAsync();

        // Assert: the browser was asked to copy the absolute address, once
        var call = Assert.Single(JSInterop.Invocations, i => i.Identifier == "reviewCenter.copyText");
        Assert.Equal("http://localhost" + ReviewLinks.ForThread(projectId, "REV-1", thread.Id), call.Arguments[0]);

        // Assert: only the status says Copied; the button keeps its label
        Assert.Equal("Copied", cut.Find(status).TextContent);
        Assert.Equal("Copy link", cut.Find(button).TextContent);

        // Act: let the feedback time run out
        Time.Advance(TimeSpan.FromSeconds(2));

        // Assert: the status is cleared and the button label never changed
        await cut.WaitForAssertionAsync(() => Assert.Equal(string.Empty, cut.Find(status).TextContent));
        Assert.Equal("Copy link", cut.Find(button).TextContent);
    }

    /// <summary>
    ///     Tests that a second click on the same thread keeps the feedback for the full time after that click, rather
    ///     than the time left of the first click.
    /// </summary>
    [Fact]
    public async Task ReviewPage_CopyLink_SecondClick_KeepsFeedbackUntilTwoSecondsAfterTheLatestClick()
    {
        // Arrange: a thread, and a browser that copies successfully
        var projectId = await CreateReviewAsync();
        var thread = await StartAsync(projectId, ModPath, 15, ThreadSide.Head);
        JSInterop.Setup<bool>("reviewCenter.copyText", _ => true).SetResult(true);
        var cut = Open(ReviewLinks.ForFile(projectId, "REV-1", ModPath));
        var button = $"[data-testid='thread-copy-link-{thread.Id}']";
        var status = $"[data-testid='thread-copy-status-{thread.Id}']";

        // Act: click, wait 1.5 s, click again, then pass the moment (2 s after the first click) at which the
        // first click's timer would have cleared the feedback
        await cut.Find(button).ClickAsync();
        Time.Advance(TimeSpan.FromSeconds(1.5));
        await cut.Find(button).ClickAsync();
        Time.Advance(TimeSpan.FromSeconds(1.5));

        // Assert: a stale timer, if any, has had time to run, and the feedback of the second click is still there
        await Task.Delay(TimeSpan.FromMilliseconds(200), TestContext.Current.CancellationToken);
        Assert.Equal("Copied", cut.Find(status).TextContent);

        // Act: pass 2 s after the second click
        Time.Advance(TimeSpan.FromSeconds(0.6));

        // Assert: the feedback is cleared
        await cut.WaitForAssertionAsync(() => Assert.Equal(string.Empty, cut.Find(status).TextContent));
    }

    /// <summary>
    ///     Tests that a browser that cannot copy is reported as "Copy failed" and the thread keeps working.
    /// </summary>
    [Fact]
    public async Task ReviewPage_CopyLink_ClipboardFails_ShowsCopyFailed()
    {
        // Arrange: a thread, and a browser that refuses to copy
        var projectId = await CreateReviewAsync();
        var thread = await StartAsync(projectId, ModPath, 15, ThreadSide.Head);
        JSInterop.Setup<bool>("reviewCenter.copyText", _ => true).SetResult(false);
        var cut = Open(ReviewLinks.ForFile(projectId, "REV-1", ModPath));

        // Act: press the button
        await cut.Find($"[data-testid='thread-copy-link-{thread.Id}']").ClickAsync();

        // Assert: the failure is shown, the button still reads Copy link, and the thread link is still there
        Assert.Equal("Copy failed", cut.Find($"[data-testid='thread-copy-status-{thread.Id}']").TextContent);
        Assert.Equal("Copy link", cut.Find($"[data-testid='thread-copy-link-{thread.Id}']").TextContent);
        Assert.Equal(
            ReviewLinks.ForThread(projectId, "REV-1", thread.Id),
            cut.Find($"[data-testid='thread-link-{thread.Id}']").GetAttribute("href"));
    }

    /// <summary>
    ///     Tests that the per-file thread counts stay in the file list when a link opens the page.
    /// </summary>
    [Fact]
    public async Task ReviewPage_ThreadAddress_KeepsThreadCountsInTheFileList()
    {
        // Arrange: two open threads on the modified file
        var projectId = await CreateReviewAsync();
        var thread = await StartAsync(projectId, ModPath, 15, ThreadSide.Head);
        await StartAsync(projectId, ModPath, 3, ThreadSide.Baseline);

        // Act: open one thread
        var cut = Open(ReviewLinks.ForThread(projectId, "REV-1", thread.Id));

        // Assert: the counts are still there
        Assert.Equal("2 open threads", cut.Find("[data-testid='file-open-threads-1']").TextContent);
        Assert.Equal("0 open threads", cut.Find("[data-testid='file-open-threads-2']").TextContent);
    }

    /// <summary>
    ///     Tests that a link built by <see cref="ReviewLinks"/> opens the very file it was built for, whatever the
    ///     characters of its path: the router decodes the address once, and the page does not decode it again.
    /// </summary>
    /// <param name="path">The file path.</param>
    [Theory]
    [InlineData("a.txt")]
    [InlineData("deep/er/nested/dir/x.txt")]
    [InlineData("my docs/read me.txt")]
    [InlineData("c#/d#.txt")]
    [InlineData("what?/q?m.txt")]
    [InlineData("100%/done%.txt")]
    [InlineData("a%2Fb.txt")]
    [InlineData("a%20b.txt")]
    [InlineData("a+b&c=d.txt")]
    [InlineData("naïve/日本語.txt")]
    public async Task ReviewPage_RoundTrip_LinkFromBuilder_OpensTheSameFileAndLine(string path)
    {
        // Arrange: a review that holds a decoy file next to the one under test
        var projectId = await CreateUploadReviewAsync("decoy.txt", path);

        // Act: open the file link
        var file = Open(ReviewLinks.ForFile(projectId, "REV-1", path));
        var filePath = file.Find("[data-testid='selected-file-path']").TextContent;

        // Act: open the line link
        var line = Open(ReviewLinks.ForFile(projectId, "REV-1", path, 2));

        // Assert: the same file is shown, the same line highlighted, and no message
        Assert.Equal(path, filePath);
        Assert.Equal(path, line.Find("[data-testid='selected-file-path']").TextContent);
        Assert.Contains("focused", line.Find("[data-testid='code-line-2']").ClassList);
        Assert.Empty(line.FindAll("[data-testid='deep-link-message']"));
    }

    /// <summary>
    ///     Tests that the router sends a signed-out visitor to the login page with a return address that is the
    ///     deep link exactly as it was requested, escapes and query included.
    /// </summary>
    [Fact]
    public async Task Routes_DeepLink_SignedOut_LoginLinkKeepsEscapesAndQuery()
    {
        // Arrange: nobody is signed in, and the router is at an escaped line link
        SignOut();
        var projectId = await CreateUploadReviewAsync("my docs/c#.txt");
        var address = ReviewLinks.ForFile(projectId, "REV-1", "my docs/c#.txt", 3, ThreadSide.Baseline);
        Services.GetRequiredService<NavigationManager>().NavigateTo(address);

        // Act: render the router
        var cut = Render<Routes>();

        // Assert: the return address is the whole link, escaped once more as a query value
        Assert.Empty(cut.FindAll("[data-testid='review-page']"));
        Assert.Equal(
            "/login?ReturnUrl=" + Uri.EscapeDataString(address),
            cut.Find("[data-testid='access-denied-login-link']").GetAttribute("href"));
    }

    /// <summary>
    ///     Tests the whole way for a signed-out visitor over HTTP: the deep link redirects to the login page with the
    ///     link as the return address, the login page keeps it, a login goes back to it, and the page shows the file.
    /// </summary>
    /// <param name="path">The file path.</param>
    [Theory(Timeout = 120000)]
    [InlineData("plain.txt")]
    [InlineData("deep/er/nested.txt")]
    [InlineData("my docs/a b.txt")]
    [InlineData("c#/d#.txt")]
    [InlineData("what?/q?m.txt")]
    [InlineData("100%/done%.txt")]
    [InlineData("a%2Fb.txt")]
    [InlineData("naïve/日本語.txt")]
    public async Task SignIn_DeepLink_SignedOut_ReturnsToTheSameLinkAfterLogin(string path)
    {
        // Arrange: an application with a review that has the file, a user, and a visitor who is not signed in
        await using var app = await TestWebApp.StartAsync(new CapturingLoggerProvider(), "serve", "--silent");
        var project = await TestWebApp.CreateProjectAsync(app, "Apollo");
        await app.Services.GetRequiredService<ReviewService>().CreateDraftAsync(
            project.Id,
            "REV-1",
            "Uploaded",
            [new UploadedFile(path, Encoding.UTF8.GetBytes("one\ntwo\nthree\n"))],
            TestContext.Current.CancellationToken);
        using var browser = new TestBrowser(app);
        var deepLink = ReviewLinks.ForFile(project.Id, "REV-1", path, 2);

        // Act: request the link, follow the redirect to the login page, log in with what the page offers
        var redirect = await browser.Client.GetAsync(deepLink, TestContext.Current.CancellationToken);
        var loginAddress = LocalPart(redirect);
        var loginPage = await browser.GetPageAsync(loginAddress);
        var offered = loginPage.QuerySelector("input[name='returnUrl']")?.GetAttribute("value");
        var login = await browser.LoginAsync("ann", TestBrowser.Password, offered);
        var landing = LocalPart(login);
        var page = await browser.GetPageAsync(landing);

        // Assert: the return address is the link as requested, local, and the visitor lands on the file and line
        Assert.Equal(HttpStatusCode.Redirect, redirect.StatusCode);
        Assert.StartsWith("/login?ReturnUrl=", loginAddress, StringComparison.Ordinal);
        Assert.Equal(deepLink, offered);
        Assert.Equal(deepLink, landing);
        Assert.Equal(path, page.QuerySelector("[data-testid='selected-file-path']")?.TextContent);
        Assert.Contains("focused", page.QuerySelector("[data-testid='code-line-2']")?.ClassName ?? string.Empty, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Tests that a deep link whose characters the server normalizes (<c>+</c> and <c>&amp;</c>) still comes back
    ///     to the same file after login, and that the return address stays a local path.
    /// </summary>
    [Fact(Timeout = 120000)]
    public async Task SignIn_DeepLink_PlusAndAmpersand_ReturnsToTheSameFileAfterLogin()
    {
        // Arrange: a review with a file whose name has + and &
        const string path = "a+b&c=d.txt";
        await using var app = await TestWebApp.StartAsync(new CapturingLoggerProvider(), "serve", "--silent");
        var project = await TestWebApp.CreateProjectAsync(app, "Apollo");
        await app.Services.GetRequiredService<ReviewService>().CreateDraftAsync(
            project.Id, "REV-1", "Uploaded", [new UploadedFile(path, "one\ntwo\n"u8.ToArray())], TestContext.Current.CancellationToken);
        using var browser = new TestBrowser(app);

        // Act: request the link signed out, log in with the offered return address, open where it leads
        var redirect = await browser.Client.GetAsync(ReviewLinks.ForFile(project.Id, "REV-1", path, 1), TestContext.Current.CancellationToken);
        var loginPage = await browser.GetPageAsync(LocalPart(redirect));
        var offered = loginPage.QuerySelector("input[name='returnUrl']")?.GetAttribute("value");
        var login = await browser.LoginAsync("ann", TestBrowser.Password, offered);
        var page = await browser.GetPageAsync(LocalPart(login));

        // Assert: local return address, and the same file is shown
        Assert.StartsWith("/projects/", offered, StringComparison.Ordinal);
        Assert.Equal(path, page.QuerySelector("[data-testid='selected-file-path']")?.TextContent);
    }

    /// <summary>
    ///     Tests that a hostile return address does not survive the login, even when it looks like a deep link.
    /// </summary>
    /// <param name="returnUrl">The return address posted with the login.</param>
    [Theory(Timeout = 120000)]
    [InlineData("https://evil.example/projects/1/reviews/REV-1/files/a.txt")]
    [InlineData("//evil.example/projects/1/reviews/REV-1/threads/1")]
    [InlineData("/\\evil.example/projects/1/reviews/REV-1")]
    public async Task SignIn_DeepLink_HostileReturnUrl_GoesToTheHomePage(string returnUrl)
    {
        // Arrange: an application with a user
        await using var app = await TestWebApp.StartAsync(new CapturingLoggerProvider(), "serve", "--silent");
        await app.Services.GetRequiredService<UserService>().CreateAsync(
            "ann", "Ann", UserRole.Reviewer, TestBrowser.Password, TestContext.Current.CancellationToken);
        using var browser = new TestBrowser(app);

        // Act: log in with the hostile address
        var login = await browser.LoginAsync("ann", TestBrowser.Password, returnUrl);

        // Assert: the visitor lands on the home page of this site
        Assert.Equal("/", LocalPart(login));
    }

    /// <summary>
    ///     Tests that a signed-in visitor who opens a thread link over HTTP gets the page already showing the file
    ///     and the highlighted thread (the first response, before any live connection).
    /// </summary>
    [Fact(Timeout = 120000)]
    public async Task ReviewPage_ThreadAddress_OverHttp_RendersFileAndHighlightedThread()
    {
        // Arrange: an application with a review, a thread, and a signed-in browser
        await using var app = await TestWebApp.StartAsync(new CapturingLoggerProvider(), "serve", "--silent");
        var project = await TestWebApp.CreateProjectAsync(app, "Apollo");
        await app.Services.GetRequiredService<ReviewService>().CreateDraftAsync(
            project.Id,
            "REV-1",
            "Uploaded",
            [new UploadedFile("x.txt", "one\ntwo\n"u8.ToArray()), new UploadedFile("y z.txt", "uno\ndos\n"u8.ToArray())],
            TestContext.Current.CancellationToken);
        var thread = await app.Services.GetRequiredService<ThreadService>().StartThreadAsync(
            project.Id, "REV-1", new ThreadAnchor("y z.txt", 2, 2, ThreadSide.Head), new Actor("ann", "Ann"), "Look", TestContext.Current.CancellationToken);
        using var browser = await TestBrowser.SignedInAsync(app, "ann");

        // Act: open the thread address
        var page = await browser.GetPageAsync(ReviewLinks.ForThread(project.Id, "REV-1", thread.Id));

        // Assert: the file of the thread is open, its line and the thread are highlighted
        Assert.Equal("y z.txt", page.QuerySelector("[data-testid='selected-file-path']")?.TextContent);
        Assert.Contains("focused", page.QuerySelector("[data-testid='code-line-2']")?.ClassName ?? string.Empty, StringComparison.Ordinal);
        Assert.Contains("focused", page.QuerySelector($"[data-testid='thread-{thread.Id}']")?.ClassName ?? string.Empty, StringComparison.Ordinal);
    }
}
