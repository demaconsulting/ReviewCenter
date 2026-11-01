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

using DemaConsulting.ReviewCenter.Core;
using Microsoft.AspNetCore.Components;

namespace DemaConsulting.ReviewCenter.Web;

/// <summary>
///     Code-behind for the resolve dialog (<c>ResolveNoteDialog.razor</c>): the note asked for when a thread is
///     resolved, required for a Finding and optional for a Question.
/// </summary>
/// <remarks>
///     The component keeps the typed note only until the person resolves or cancels. A Finding with a blank note is
///     not handed to the parent: the dialog shows what is missing instead, so the person can fix it at once. The
///     service checks the rule again. Each browser connection gets its own instance, so the state needs no
///     synchronization.
/// </remarks>
public partial class ResolveNoteDialog : ComponentBase
{
    /// <summary>
    ///     The note typed so far.
    /// </summary>
    private string _note = string.Empty;

    /// <summary>
    ///     The message about a missing note, or <c>null</c> for none.
    /// </summary>
    private string? _error;

    /// <summary>
    ///     Gets or sets the kind of the thread being resolved; a Finding needs a note.
    /// </summary>
    [Parameter]
    public ThreadKind Kind { get; set; } = ThreadKind.Question;

    /// <summary>
    ///     Gets or sets the text that makes the test identifiers of this dialog unique (the thread identity).
    /// </summary>
    [Parameter]
    public string Suffix { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the callback that receives the note (trimmed, or <c>null</c> when none was written) when the
    ///     person resolves.
    /// </summary>
    [Parameter]
    public EventCallback<string?> OnConfirm { get; set; }

    /// <summary>
    ///     Gets or sets the callback that runs when the person cancels.
    /// </summary>
    [Parameter]
    public EventCallback OnCancel { get; set; }

    /// <summary>
    ///     Gets the accessible name of the dialog.
    /// </summary>
    private string Title => Kind == ThreadKind.Finding ? "Resolve this finding" : "Resolve this question";

    /// <summary>
    ///     Gets the words above the note field.
    /// </summary>
    private string Prompt => Kind == ThreadKind.Finding
        ? "How was this finding dealt with? The note is printed in the report."
        : "Note (optional)";

    /// <summary>
    ///     Gets the example shown in the empty note field.
    /// </summary>
    private string Placeholder => Kind == ThreadKind.Finding
        ? "For example: fixed in abc123, ticket #123, not an issue"
        : "For example: answered above";

    /// <summary>
    ///     Resolves with the note, unless a Finding has none.
    /// </summary>
    /// <returns>A task that completes when the parent has handled the note.</returns>
    private Task ConfirmAsync()
    {
        var note = _note.Trim();
        if (Kind == ThreadKind.Finding && note.Length == 0)
        {
            _error = "A finding needs a short note that says how it was dealt with, for example \"fixed in abc123\".";
            return Task.CompletedTask;
        }

        _error = null;
        return OnConfirm.InvokeAsync(note.Length == 0 ? null : note);
    }

    /// <summary>
    ///     Closes the dialog without resolving.
    /// </summary>
    /// <returns>A task that completes when the parent has handled it.</returns>
    private Task CancelAsync()
    {
        _error = null;
        return OnCancel.InvokeAsync();
    }
}
