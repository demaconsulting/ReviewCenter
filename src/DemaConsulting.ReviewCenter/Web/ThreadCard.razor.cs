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
///     Code-behind for the thread card (<c>ThreadCard.razor</c>): one thread with its kind, comments, reply box and the
///     actions the person may take.
/// </summary>
/// <remarks>
///     The card decides nothing about permissions: it shows the buttons the <see cref="Actions"/> allow, and the
///     parent stores every change (and shows its refusal through <see cref="Error"/>). The only state it keeps is
///     whether the resolve dialog is open. Each browser connection gets its own instance, so the state needs no
///     synchronization.
/// </remarks>
public partial class ThreadCard : ComponentBase
{
    /// <summary>
    ///     Whether the resolve dialog is open.
    /// </summary>
    private bool _resolving;

    /// <summary>
    ///     Gets or sets the thread to show.
    /// </summary>
    [Parameter]
    [EditorRequired]
    public ReviewThread Thread { get; set; } = null!;

    /// <summary>
    ///     Gets or sets what the person may do with the thread; <see cref="ThreadActions.None"/> for nothing.
    /// </summary>
    [Parameter]
    public ThreadActions Actions { get; set; } = ThreadActions.None;

    /// <summary>
    ///     Gets or sets the words that say where the thread is anchored.
    /// </summary>
    [Parameter]
    public string AnchorText { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the words that count the comments, for example <c>3 comments</c>.
    /// </summary>
    [Parameter]
    public string CountText { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets a value indicating whether the thread is the one a link points at.
    /// </summary>
    [Parameter]
    public bool Focused { get; set; }

    /// <summary>
    ///     Gets or sets a value indicating whether the thread has comments the person has not seen.
    /// </summary>
    [Parameter]
    public bool HasUnread { get; set; }

    /// <summary>
    ///     Gets or sets a value indicating whether the comments and the reply box are shown; a resolved thread may be
    ///     collapsed.
    /// </summary>
    [Parameter]
    public bool Expanded { get; set; } = true;

    /// <summary>
    ///     Gets or sets a value indicating whether the person may reply (the review is open and they can contribute).
    /// </summary>
    [Parameter]
    public bool CanReply { get; set; }

    /// <summary>
    ///     Gets or sets the live-region text next to the "Copy link" button.
    /// </summary>
    [Parameter]
    public string CopyStatus { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the message of the last refused change to the thread, or <c>null</c> for none.
    /// </summary>
    [Parameter]
    public string? Error { get; set; }

    /// <summary>
    ///     Gets or sets the reply text typed so far.
    /// </summary>
    [Parameter]
    public string ReplyText { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the callback that receives the reply text after every keystroke.
    /// </summary>
    [Parameter]
    public EventCallback<string> ReplyTextChanged { get; set; }

    /// <summary>
    ///     Gets or sets a value indicating whether the reply is ticked "Add to report".
    /// </summary>
    [Parameter]
    public bool ReplyIncludeInReport { get; set; }

    /// <summary>
    ///     Gets or sets the callback that receives the "Add to report" tick of the reply.
    /// </summary>
    [Parameter]
    public EventCallback<bool> ReplyIncludeInReportChanged { get; set; }

    /// <summary>
    ///     Gets or sets what the parent shows under the header (the signature warning of a change to the thread).
    /// </summary>
    [Parameter]
    public RenderFragment? HeadNotice { get; set; }

    /// <summary>
    ///     Gets or sets what the parent shows in the reply box (the signature warning of a reply).
    /// </summary>
    [Parameter]
    public RenderFragment? ReplyNotice { get; set; }

    /// <summary>
    ///     Gets or sets the callback that runs when the person asks to copy the link of the thread.
    /// </summary>
    [Parameter]
    public EventCallback OnCopyLink { get; set; }

    /// <summary>
    ///     Gets or sets the callback that runs when the person expands or collapses a resolved thread.
    /// </summary>
    [Parameter]
    public EventCallback OnToggleExpanded { get; set; }

    /// <summary>
    ///     Gets or sets the callback that runs when the person sends the reply.
    /// </summary>
    [Parameter]
    public EventCallback OnReply { get; set; }

    /// <summary>
    ///     Gets or sets the callback that runs when the person resolves the thread; it receives the note, or
    ///     <c>null</c> when none was written.
    /// </summary>
    [Parameter]
    public EventCallback<string?> OnResolve { get; set; }

    /// <summary>
    ///     Gets or sets the callback that runs when the person opens a resolved thread again.
    /// </summary>
    [Parameter]
    public EventCallback OnReopen { get; set; }

    /// <summary>
    ///     Gets or sets the callback that runs when the person makes the Question a Finding.
    /// </summary>
    [Parameter]
    public EventCallback OnPromote { get; set; }

    /// <summary>
    ///     Gets or sets the callback that runs when the person makes the Finding a Question.
    /// </summary>
    [Parameter]
    public EventCallback OnDemote { get; set; }

    /// <summary>
    ///     Gets or sets the callback that runs when the person adds a reply to the report or takes it out; it
    ///     receives the reply.
    /// </summary>
    [Parameter]
    public EventCallback<ReviewComment> OnToggleReport { get; set; }

    /// <summary>
    ///     Formats a comment time for display.
    /// </summary>
    /// <param name="time">The time to format.</param>
    /// <returns>The UTC time as "yyyy-MM-dd HH:mm UTC".</returns>
    public static string FormatTime(DateTimeOffset time)
    {
        return time.UtcDateTime.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture);
    }

    /// <summary>
    ///     Resolves an open thread after asking for a note, or opens a resolved one again.
    /// </summary>
    /// <returns>A task that completes when the parent has handled a reopen; an open thread only shows the dialog.</returns>
    private Task ToggleAsync()
    {
        if (Thread.State == ReviewThreadState.Resolved)
        {
            return OnReopen.InvokeAsync();
        }

        _resolving = !_resolving;
        return Task.CompletedTask;
    }

    /// <summary>
    ///     Closes the dialog once the thread is resolved, so a thread that is opened again does not show it.
    /// </summary>
    protected override void OnParametersSet()
    {
        if (Thread.State == ReviewThreadState.Resolved)
        {
            _resolving = false;
        }
    }

    /// <summary>
    ///     Hands the note to the parent; the dialog stays open, with the note in it, until the thread is resolved.
    /// </summary>
    /// <param name="note">The note, or <c>null</c> when none was written.</param>
    /// <returns>A task that completes when the parent has handled the resolve.</returns>
    /// <remarks>
    ///     The parent may ask for confirmation first (signatures would become out of date) and the person may cancel
    ///     that; keeping the dialog open means the typed note is not lost then. A resolved thread closes it.
    /// </remarks>
    private Task ConfirmResolveAsync(string? note)
    {
        return OnResolve.InvokeAsync(note);
    }

    /// <summary>
    ///     Closes the dialog without resolving.
    /// </summary>
    private void CancelResolve()
    {
        _resolving = false;
    }
}
