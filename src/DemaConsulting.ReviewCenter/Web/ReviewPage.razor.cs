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

using System.Globalization;
using DemaConsulting.ReviewCenter.Core;
using Microsoft.AspNetCore.Components;

namespace DemaConsulting.ReviewCenter.Web;

/// <summary>
///     Code-behind for the review page (<c>ReviewPage.razor</c>): the view of one review, of the content of its
///     files, and of the comment threads on them.
/// </summary>
/// <remarks>
///     The page reads reviews through <see cref="ReviewService"/> and reads and changes threads through
///     <see cref="ThreadService"/> only; the rules live there and a violation is shown next to the form that
///     caused it. File bytes are loaded one file at a time when the operator selects it, never with the file
///     list. A file is shown as numbered lines only when it is valid UTF-8 without NUL bytes (see
///     <see cref="FileText"/>); anything else is reported as a binary file rather than rendered as garbage.
///     All text from the files and the comments is rendered as plain text, never as markup. There is no
///     sign-in yet, so the author name of a comment is a free-text label that the operator types once and that
///     is not verified. Threads are read when the page loads and after each change made here; comments posted
///     in other browsers appear on the next load or change. Each browser connection (circuit) gets its own
///     page instance, so the state here is per-user and needs no synchronization.
/// </remarks>
public partial class ReviewPage : ComponentBase
{
    /// <summary>
    ///     Backing field for <see cref="InteractiveMarker"/>; flips to <c>true</c> after the first render
    ///     on a live circuit.
    /// </summary>
    private bool _interactive;

    /// <summary>
    ///     Gets or sets the identity of the owning project, from the page address.
    /// </summary>
    [Parameter]
    public int ProjectId { get; set; }

    /// <summary>
    ///     Gets or sets the review identifier, from the page address.
    /// </summary>
    [Parameter]
    public string ReviewId { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the service that reads reviews, supplied by dependency injection.
    /// </summary>
    [Inject]
    private ReviewService ReviewService { get; set; } = null!;

    /// <summary>
    ///     Gets or sets the service that reads and changes comment threads, supplied by dependency injection.
    /// </summary>
    [Inject]
    private ThreadService ThreadService { get; set; } = null!;

    /// <summary>
    ///     Gets or sets the service that reads projects, supplied by dependency injection.
    /// </summary>
    [Inject]
    private ProjectService ProjectService { get; set; } = null!;

    /// <summary>
    ///     Gets the name of the owning project shown in the breadcrumb, or <c>null</c> when it is unknown.
    /// </summary>
    private string? ProjectName { get; set; }

    /// <summary>
    ///     Gets a value indicating whether the review lookup has finished.
    /// </summary>
    private bool Loaded { get; set; }

    /// <summary>
    ///     Gets the review shown, or <c>null</c> when it does not exist.
    /// </summary>
    private Review? Review { get; set; }

    /// <summary>
    ///     Gets the path of the file whose content is shown, or <c>null</c> when no file is selected.
    /// </summary>
    private string? SelectedPath { get; set; }

    /// <summary>
    ///     Gets the lines of the selected file, or <c>null</c> when no file is selected or it is binary.
    /// </summary>
    private IReadOnlyList<string>? SelectedLines { get; set; }

    /// <summary>
    ///     Gets all threads of the review (every file), in creation order.
    /// </summary>
    private IReadOnlyList<ReviewThread> Threads { get; set; } = [];

    /// <summary>
    ///     Gets or sets the author name typed by the operator; used for new threads and replies.
    /// </summary>
    private string AuthorName { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the text of the first line field of the new-thread form (blank for the whole file).
    /// </summary>
    private string NewStartLine { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the text of the last line field of the new-thread form (blank means the start line only).
    /// </summary>
    private string NewEndLine { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the comment text of the new-thread form.
    /// </summary>
    private string NewText { get; set; } = string.Empty;

    /// <summary>
    ///     Gets the message of the last failed attempt to start a thread, or <c>null</c>.
    /// </summary>
    private string? NewThreadError { get; set; }

    /// <summary>
    ///     Gets the reply text typed for each thread, by thread identity.
    /// </summary>
    private Dictionary<int, string> ReplyTexts { get; } = [];

    /// <summary>
    ///     Gets the message of the last failed reply or state change for each thread, by thread identity.
    /// </summary>
    private Dictionary<int, string> ReplyErrors { get; } = [];

    /// <summary>
    ///     Gets the value rendered into the <c>data-interactive</c> attribute.
    /// </summary>
    /// <remarks>
    ///     During static prerendering no circuit exists and events cannot be handled; drivers wait for
    ///     <c>true</c> before interacting.
    /// </remarks>
    private string InteractiveMarker => _interactive ? "true" : "false";

    /// <summary>
    ///     Loads the review and its threads whenever the address parameters change, and clears the file
    ///     selection and everything typed.
    /// </summary>
    /// <returns>A task that completes when the review is loaded.</returns>
    protected override async Task OnParametersSetAsync()
    {
        // Clear the previous review before the first await so the loading state is shown, and a stale
        // review is never displayed or acted on under the new address
        Loaded = false;
        Review = null;
        ProjectName = null;
        SelectedPath = null;
        SelectedLines = null;
        Threads = [];
        ClearNewThreadForm();
        ReplyTexts.Clear();
        ReplyErrors.Clear();

        Review = await ReviewService.GetAsync(ProjectId, ReviewId);
        ProjectName = (await ProjectService.GetAsync(ProjectId))?.Name;
        Threads = await ThreadService.ListAsync(ProjectId, ReviewId);
        Loaded = true;
    }

    /// <summary>
    ///     Marks the page interactive after it has rendered on a live circuit.
    /// </summary>
    /// <param name="firstRender"><c>true</c> for the first render of this component instance.</param>
    /// <remarks>
    ///     <c>OnAfterRender</c> is never invoked during static prerendering, so reaching it proves that
    ///     the circuit is connected. A second render makes the new marker value reach the browser.
    /// </remarks>
    protected override void OnAfterRender(bool firstRender)
    {
        // Only the first render transitions the marker; later renders must not loop
        if (firstRender)
        {
            _interactive = true;
            StateHasChanged();
        }
    }

    /// <summary>
    ///     Loads and shows the content of one file of the review.
    /// </summary>
    /// <param name="index">The zero-based position of the file in the displayed list.</param>
    /// <returns>A task that completes when the content is loaded.</returns>
    /// <remarks>
    ///     An index outside the list is ignored. A file that cannot be found in the store (removed since the
    ///     page was loaded) is shown as empty text rather than failing the circuit. Selecting a file clears the
    ///     new-thread form but keeps the author name.
    /// </remarks>
    private async Task SelectFileAsync(int index)
    {
        if (Review is null || index < 0 || index >= Review.Files.Count)
        {
            return;
        }

        // Load the bytes of just this file
        var path = Review.Files[index].Path;
        var content = await ReviewService.GetFileContentAsync(ProjectId, ReviewId, path) ?? [];

        // Show text only when it is clean UTF-8; otherwise tell the operator it is binary
        var text = FileText.TryDecode(content);
        SelectedPath = path;
        SelectedLines = text is null ? null : FileText.SplitLines(text);
        ClearNewThreadForm();
    }

    /// <summary>
    ///     Aims the new-thread form at one line.
    /// </summary>
    /// <param name="line">The 1-based line number.</param>
    private void CommentOnLine(int line)
    {
        NewStartLine = line.ToString(CultureInfo.InvariantCulture);
        NewEndLine = NewStartLine;
    }

    /// <summary>
    ///     Starts a thread on the selected file from the new-thread form.
    /// </summary>
    /// <returns>A task that completes when the thread is stored or the problem is shown.</returns>
    /// <remarks>
    ///     Both line fields blank anchors the whole file; only a start line anchors that single line. On a
    ///     rule violation the message is shown next to the form and everything typed stays, so it can be fixed.
    ///     Success clears the text and the line fields.
    /// </remarks>
    private async Task StartThreadAsync()
    {
        if (SelectedPath is null)
        {
            return;
        }

        if (!TryParseLine(NewStartLine, out var start) || !TryParseLine(NewEndLine, out var end))
        {
            NewThreadError = "Line numbers must be whole numbers.";
            return;
        }

        try
        {
            await ThreadService.StartThreadAsync(
                ProjectId, ReviewId, SelectedPath, start, end ?? start, AuthorName, NewText);
        }
        catch (DomainRuleException ex)
        {
            NewThreadError = ex.Message;
            return;
        }

        NewThreadError = null;
        NewText = string.Empty;
        NewStartLine = string.Empty;
        NewEndLine = string.Empty;
        Threads = await ThreadService.ListAsync(ProjectId, ReviewId);
    }

    /// <summary>
    ///     Adds the typed reply to a thread.
    /// </summary>
    /// <param name="threadId">The identity of the thread.</param>
    /// <returns>A task that completes when the reply is stored or the problem is shown.</returns>
    private async Task ReplyAsync(int threadId)
    {
        try
        {
            await ThreadService.ReplyAsync(
                ProjectId, ReviewId, threadId, AuthorName, ReplyTexts.GetValueOrDefault(threadId));
        }
        catch (DomainRuleException ex)
        {
            // The name field is far from the thread, so say where to fix a missing name
            ReplyErrors[threadId] = ex.Rule == DomainRule.CommentAuthorInvalid
                ? $"{ex.Message} Enter it in the 'Your name' field."
                : ex.Message;
            return;
        }

        ReplyErrors.Remove(threadId);
        ReplyTexts.Remove(threadId);
        Threads = await ThreadService.ListAsync(ProjectId, ReviewId);
    }

    /// <summary>
    ///     Resolves an open thread or reopens a resolved one.
    /// </summary>
    /// <param name="thread">The thread as shown.</param>
    /// <returns>A task that completes when the state is stored or the problem is shown.</returns>
    private async Task ToggleThreadAsync(ReviewThread thread)
    {
        try
        {
            if (thread.State == ReviewThreadState.Open)
            {
                await ThreadService.ResolveAsync(ProjectId, ReviewId, thread.Id);
            }
            else
            {
                await ThreadService.ReopenAsync(ProjectId, ReviewId, thread.Id);
            }
        }
        catch (DomainRuleException ex)
        {
            ReplyErrors[thread.Id] = ex.Message;
            return;
        }

        ReplyErrors.Remove(thread.Id);
        Threads = await ThreadService.ListAsync(ProjectId, ReviewId);
    }

    /// <summary>
    ///     Counts the open threads anchored to a file and words the count for display.
    /// </summary>
    /// <param name="path">The file path.</param>
    /// <returns>For example "0 open threads" or "1 open thread".</returns>
    private string OpenThreadLabel(string path)
    {
        var count = Threads.Count(t => t.State == ReviewThreadState.Open && t.FilePath == path);
        return count == 1 ? "1 open thread" : $"{count} open threads";
    }

    /// <summary>
    ///     Describes where the new thread would be anchored, from the current line fields.
    /// </summary>
    /// <returns>A short sentence such as "Comment on lines 3-5"; the whole file when the fields are blank or unusable.</returns>
    private string NewThreadTarget()
    {
        if (!TryParseLine(NewStartLine, out var start) || !TryParseLine(NewEndLine, out var end) || start is null)
        {
            return "Comment on the whole file";
        }

        return end is null || end == start ? $"Comment on line {start}" : $"Comment on lines {start}-{end}";
    }

    /// <summary>
    ///     Describes the anchor of a thread for display.
    /// </summary>
    /// <param name="thread">The thread.</param>
    /// <returns>"Whole file", "Line 4" or "Lines 4-9".</returns>
    private static string AnchorLabel(ReviewThread thread)
    {
        if (thread.StartLine is null)
        {
            return "Whole file";
        }

        return thread.EndLine == thread.StartLine ? $"Line {thread.StartLine}" : $"Lines {thread.StartLine}-{thread.EndLine}";
    }

    /// <summary>
    ///     Formats a comment time for display.
    /// </summary>
    /// <param name="time">The time to format.</param>
    /// <returns>The UTC time as "yyyy-MM-dd HH:mm UTC".</returns>
    private static string FormatTime(DateTimeOffset time)
    {
        return time.UtcDateTime.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture);
    }

    /// <summary>
    ///     Parses an optional line number field.
    /// </summary>
    /// <param name="text">The field text.</param>
    /// <param name="line">The number, or <c>null</c> when the field is blank.</param>
    /// <returns><c>false</c> when the field is not blank and not a whole number.</returns>
    private static bool TryParseLine(string text, out int? line)
    {
        line = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
        {
            return false;
        }

        line = number;
        return true;
    }

    /// <summary>
    ///     Empties the new-thread form fields and its message; the author name is kept.
    /// </summary>
    private void ClearNewThreadForm()
    {
        NewStartLine = string.Empty;
        NewEndLine = string.Empty;
        NewText = string.Empty;
        NewThreadError = null;
    }
}
