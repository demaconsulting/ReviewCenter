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
///     Unit tests for the threads about a check on the <see cref="ReviewPage"/> component: the card of check
///     discussions, the index row, the reply and resolve controls and the address of such a thread.
/// </summary>
public class ReviewPageCheckThreadTests : PageTestBase
{
    /// <summary>
    ///     Gets the identifier of the first check of the default checklist.
    /// </summary>
    private static string FirstCheck => DefaultChecklist.Current.Sections[0].Items[0].Id;

    /// <summary>
    ///     Creates a project with one upload review <c>REV-1</c> of the file <c>a.txt</c> and one open thread about the
    ///     first check.
    /// </summary>
    /// <returns>The project identity and the thread.</returns>
    private async Task<(int ProjectId, ReviewThread Thread)> CreateReviewWithCheckThreadAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var project = await Projects.CreateAsync("Apollo", null, cancellationToken);
        foreach (var user in await UserStore.ListAsync(cancellationToken))
        {
            await Store.AddMembershipAsync(user.Id, project.Id, cancellationToken);
        }

        await Reviews.CreateDraftAsync(project.Id, "REV-1", "First review", [new UploadedFile("a.txt", TestContent.A)], cancellationToken);
        var thread = await Threads.StartThreadAsync(
            project.Id, "REV-1", ThreadAnchor.ForCheck(FirstCheck), TestActors.Ann, "Is this check clear?", cancellationToken);
        return (project.Id, thread);
    }

    /// <summary>
    ///     Renders the page for the review <c>REV-1</c>.
    /// </summary>
    /// <param name="projectId">The project identity.</param>
    /// <param name="threadId">The thread of the address, or <c>null</c> for the plain review address.</param>
    /// <returns>The rendered component.</returns>
    private IRenderedComponent<ReviewPage> RenderPage(int projectId, int? threadId = null)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        return Render<ReviewPage>(parameters => parameters
            .Add(page => page.ProjectId, projectId)
            .Add(page => page.ReviewId, "REV-1")
            .Add(page => page.ThreadId, threadId));
    }

    /// <summary>
    ///     Test that a thread about a check is listed in the thread index with the check label in place of a file path
    ///     and that the card of check discussions shows the thread, while a review without such a thread has no card.
    /// </summary>
    [Fact]
    public async Task ReviewPage_CheckThread_ListedInIndexWithCheckLabel()
    {
        // Arrange: a review with one thread about the first check
        var (projectId, thread) = await CreateReviewWithCheckThreadAsync();

        // Act
        var cut = RenderPage(projectId);

        // Assert: the index row names the check, not an empty file
        Assert.Equal("Check discussion", cut.Find($"[data-testid='index-file-{thread.Id}']").TextContent);
        Assert.Equal($"Check {FirstCheck}", cut.Find($"[data-testid='index-anchor-{thread.Id}']").TextContent);

        // Assert: the card holds the thread with its comment
        var card = cut.Find("[data-testid='check-discussions']");
        Assert.NotNull(card.QuerySelector($"[data-testid='thread-{thread.Id}']"));
        Assert.Equal($"Check {FirstCheck}", cut.Find($"[data-testid='thread-anchor-{thread.Id}']").TextContent);
        Assert.Equal("Is this check clear?", cut.Find($"[data-testid='comment-text-{thread.Id}-1']").TextContent);
        Assert.Equal("1", cut.Find("[data-testid='threads-open-count']").TextContent);

        // Assert: a review without a thread about a check shows no card
        var other = await Projects.CreateAsync("Gemini", null, TestContext.Current.CancellationToken);
        await Reviews.CreateDraftAsync(
            other.Id, "REV-1", "Other review", [new UploadedFile("a.txt", TestContent.A)], TestContext.Current.CancellationToken);
        Assert.Empty(RenderPage(other.Id).FindAll("[data-testid='check-discussions']"));
    }

    /// <summary>
    ///     Test that a thread about a check is kept out of the list of the threads of the selected file: the list holds
    ///     only the thread of the file, and the card of check discussions holds only the thread about the check.
    /// </summary>
    [Fact]
    public async Task ReviewPage_CheckThread_SelectedFile_KeepsCheckThreadOutOfFileList()
    {
        // Arrange: a review with a thread about a check and a whole-file thread on a.txt
        var (projectId, checkThread) = await CreateReviewWithCheckThreadAsync();
        var fileThread = await Threads.StartThreadAsync(
            projectId, "REV-1", new ThreadAnchor("a.txt"), TestActors.Ann, "A note on the file", TestContext.Current.CancellationToken);
        var cut = RenderPage(projectId);

        // Act: select the file
        await cut.Find("[data-testid='file-select-0']").ClickAsync();

        // Assert: the list of the file holds exactly the thread of the file
        var listed = cut.Find("[data-testid='threads-list']").QuerySelectorAll("[data-testid^='thread-anchor-']");
        Assert.Equal([$"thread-anchor-{fileThread.Id}"], listed.Select(e => e.GetAttribute("data-testid")));
        Assert.Null(cut.Find("[data-testid='threads-list']").QuerySelector($"[data-testid='thread-{checkThread.Id}']"));

        // Assert: the card of check discussions holds exactly the thread about the check
        var card = cut.Find("[data-testid='check-discussions']");
        Assert.Equal([$"thread-anchor-{checkThread.Id}"], card.QuerySelectorAll("[data-testid^='thread-anchor-']")
            .Select(e => e.GetAttribute("data-testid")));
        Assert.Null(card.QuerySelector($"[data-testid='thread-{fileThread.Id}']"));
    }

    /// <summary>
    ///     Test that the text of a comment of a thread about a check is shown as plain text: markup in it is shown as
    ///     typed and creates no element.
    /// </summary>
    [Fact]
    public async Task ReviewPage_CheckThread_CommentText_IsShownAsPlainText()
    {
        // Arrange: a review with a thread about a check whose text holds markup
        var cancellationToken = TestContext.Current.CancellationToken;
        var (projectId, _) = await CreateReviewWithCheckThreadAsync();
        var markup = await Threads.StartThreadAsync(
            projectId, "REV-1", ThreadAnchor.ForCheck(FirstCheck), TestActors.Ann, "<b>x</b>", cancellationToken);

        // Act
        var cut = RenderPage(projectId);

        // Assert: the text equals the literal typed and the card holds no element made from it
        var text = cut.Find($"[data-testid='comment-text-{markup.Id}-1']");
        Assert.Equal("<b>x</b>", text.TextContent);
        Assert.Empty(text.QuerySelectorAll("b"));
        Assert.Empty(cut.Find("[data-testid='check-discussions']").QuerySelectorAll("b"));
    }

    /// <summary>
    ///     Test that the card of check discussions offers no form to start a thread, while the page offers the form
    ///     for the selected file outside the card.
    /// </summary>
    [Fact]
    public async Task ReviewPage_CheckThread_Card_OffersNoFormToStartThread()
    {
        // Arrange: a review with a thread about a check, and a file selected so that the page offers a form of its own
        var (projectId, _) = await CreateReviewWithCheckThreadAsync();
        var cut = RenderPage(projectId);
        await cut.Find("[data-testid='file-select-0']").ClickAsync();
        Assert.Single(cut.FindAll("[data-testid='new-thread-form']"));

        // Act
        var card = cut.Find("[data-testid='check-discussions']");

        // Assert: nothing in the card starts a thread
        Assert.Empty(card.QuerySelectorAll("form"));
        Assert.Empty(card.QuerySelectorAll("[data-testid='new-thread-form']"));
        Assert.Empty(card.QuerySelectorAll("[data-testid='thread-text-input']"));
        Assert.Empty(card.QuerySelectorAll("[data-testid='start-thread-button']"));
        Assert.Empty(card.QuerySelectorAll("[data-testid='thread-kind-choice']"));
    }

    /// <summary>
    ///     Test that a reply and Resolve work in the card, with no file chosen: the reply is stored, and resolving
    ///     moves the thread to the resolved count and collapses it.
    /// </summary>
    [Fact]
    public async Task ReviewPage_CheckThread_ReplyAndResolve_Work()
    {
        // Arrange: bob signed in, a review with a thread about a check
        SignInAs("bob", "Bob");
        var (projectId, thread) = await CreateReviewWithCheckThreadAsync();
        var cut = RenderPage(projectId);

        // Act: reply
        await cut.Find($"[data-testid='reply-text-{thread.Id}']").InputAsync("It is clear enough");
        await cut.Find($"[data-testid='reply-button-{thread.Id}']").ClickAsync();

        // Assert: the reply is shown and stored under bob
        Assert.Equal("Bob", cut.Find($"[data-testid='comment-author-{thread.Id}-2']").TextContent);
        Assert.Equal("It is clear enough", cut.Find($"[data-testid='comment-text-{thread.Id}-2']").TextContent);
        var stored = Assert.Single(await Threads.ListAsync(projectId, "REV-1", TestContext.Current.CancellationToken));
        Assert.Equal(2, stored.Comments.Count);

        // Act: resolve
        await cut.Find($"[data-testid='thread-toggle-{thread.Id}']").ClickAsync();
        await cut.Find($"[data-testid='resolve-note-confirm-{thread.Id}']").ClickAsync();

        // Assert: resolved, collapsed and counted
        Assert.Equal("Resolved", cut.Find($"[data-testid='thread-state-{thread.Id}']").TextContent);
        Assert.Equal("0", cut.Find("[data-testid='threads-open-count']").TextContent);
        Assert.Equal("1", cut.Find("[data-testid='threads-resolved-count']").TextContent);
        stored = Assert.Single(await Threads.ListAsync(projectId, "REV-1", TestContext.Current.CancellationToken));
        Assert.Equal(ReviewThreadState.Resolved, stored.State);
        Assert.Empty(cut.FindAll("[data-testid='selected-file-path']"));
    }

    /// <summary>
    ///     Test that the address of a thread about a check highlights the thread in its card and shows no message, even
    ///     though the thread has no file.
    /// </summary>
    [Fact]
    public async Task ReviewPage_CheckThreadDeepLink_ShowsCheckCard()
    {
        // Arrange: a review with a thread about a check
        var (projectId, thread) = await CreateReviewWithCheckThreadAsync();

        // Act: open the address of the thread
        var cut = RenderPage(projectId, thread.Id);

        // Assert: the thread of the card is focused and nothing is reported
        await cut.WaitForAssertionAsync(() =>
            Assert.Contains("focused", cut.Find($"[data-testid='thread-{thread.Id}']").ClassList));
        Assert.Empty(cut.FindAll("[data-testid='deep-link-message']"));
        Assert.NotNull(cut.Find("[data-testid='check-discussions']").QuerySelector($"[data-testid='thread-{thread.Id}']"));
    }

    /// <summary>
    ///     Test that the address of a thread about a check selects no file and shows no content of a file.
    /// </summary>
    [Fact]
    public async Task ReviewPage_CheckThreadDeepLink_SelectsNoFile()
    {
        // Arrange: a review with a thread about a check
        var (projectId, thread) = await CreateReviewWithCheckThreadAsync();

        // Act: open the address of the thread
        var cut = RenderPage(projectId, thread.Id);

        // Assert: the thread is shown, but no file is chosen and no message is reported
        await cut.WaitForAssertionAsync(() =>
            Assert.Contains("focused", cut.Find($"[data-testid='thread-{thread.Id}']").ClassList));
        Assert.Empty(cut.FindAll("[data-testid='selected-file-path']"));
        Assert.Empty(cut.FindAll("[data-testid^='line-text-']"));
        Assert.Empty(cut.FindAll("[data-testid='deep-link-message']"));
    }

    /// <summary>
    ///     Test that choosing a thread about a check in the index shows the thread without choosing a file, and keeps a
    ///     file that was chosen before.
    /// </summary>
    [Fact]
    public async Task ReviewPage_CheckThread_DoesNotSelectFile()
    {
        // Arrange: a review with a thread about a check, and a page that shows no file yet
        var (projectId, thread) = await CreateReviewWithCheckThreadAsync();
        var cut = RenderPage(projectId);
        Assert.Empty(cut.FindAll("[data-testid='selected-file-path']"));

        // Act: choose the thread in the index
        await cut.Find($"[data-testid='index-row-{thread.Id}']").ClickAsync();

        // Assert: still no file, no message, and the thread is focused
        Assert.Empty(cut.FindAll("[data-testid='selected-file-path']"));
        Assert.Empty(cut.FindAll("[data-testid='deep-link-message']"));
        Assert.Contains("focused", cut.Find($"[data-testid='thread-{thread.Id}']").ClassList);

        // Act: choose the file, then the thread again
        await cut.Find("[data-testid='file-select-0']").ClickAsync();
        await cut.Find($"[data-testid='index-row-{thread.Id}']").ClickAsync();

        // Assert: the file stays chosen
        Assert.Equal("a.txt", cut.Find("[data-testid='selected-file-path']").TextContent);
    }
}
