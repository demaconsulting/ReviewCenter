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

using Microsoft.AspNetCore.Components;

namespace DemaConsulting.ReviewCenter.Web;

/// <summary>
///     Code-behind for the composer (<c>ThreadComposer.razor</c>): the form that starts a thread as a Question or a
///     Finding, and the box that replies to a thread with an optional "Add to report" tick.
/// </summary>
/// <remarks>
///     The component keeps no copy of what is typed: the parent owns the text and the choice (so a rejected or
///     waiting change keeps what was typed and a stored one clears it) and is told of every change. It makes no
///     decision; a submit is handed to the parent, which stores the thread or reply and shows the result. Each browser
///     connection gets its own instance, so the state needs no synchronization.
/// </remarks>
public partial class ThreadComposer : ComponentBase
{
    /// <summary>
    ///     Gets or sets a value indicating whether the composer replies to a thread (text and "Add to report" tick)
    ///     instead of starting one (kind choice and text).
    /// </summary>
    [Parameter]
    public bool IsReply { get; set; }

    /// <summary>
    ///     Gets or sets the text typed so far.
    /// </summary>
    [Parameter]
    public string Text { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the callback that receives the text after every keystroke.
    /// </summary>
    [Parameter]
    public EventCallback<string> TextChanged { get; set; }

    /// <summary>
    ///     Gets or sets a value indicating whether the thread or reply goes in the report: a Finding, or a reply that
    ///     is ticked "Add to report".
    /// </summary>
    [Parameter]
    public bool IncludeInReport { get; set; }

    /// <summary>
    ///     Gets or sets the callback that receives the choice when it changes.
    /// </summary>
    [Parameter]
    public EventCallback<bool> IncludeInReportChanged { get; set; }

    /// <summary>
    ///     Gets or sets the message of the last refused submit, or <c>null</c> for none.
    /// </summary>
    [Parameter]
    public string? Error { get; set; }

    /// <summary>
    ///     Gets or sets the callback that runs when the person submits (the button or Ctrl+Enter).
    /// </summary>
    [Parameter]
    public EventCallback OnSubmit { get; set; }

    /// <summary>
    ///     Gets or sets the text of the label of the text field; also the accessible name of the reply field.
    /// </summary>
    [Parameter]
    public string TextLabel { get; set; } = "Comment";

    /// <summary>
    ///     Gets or sets the text of the submit button of a new thread; the accessible name of the Reply button.
    /// </summary>
    [Parameter]
    public string SubmitLabel { get; set; } = "Start thread";

    /// <summary>
    ///     Gets or sets the test identifier of the form (or of the reply box).
    /// </summary>
    [Parameter]
    public string FormTestId { get; set; } = "new-thread-form";

    /// <summary>
    ///     Gets or sets the test identifier of the text field.
    /// </summary>
    [Parameter]
    public string TextTestId { get; set; } = "thread-text-input";

    /// <summary>
    ///     Gets or sets the test identifier of the submit button; Ctrl+Enter in the text presses it.
    /// </summary>
    [Parameter]
    public string SubmitTestId { get; set; } = "start-thread-button";

    /// <summary>
    ///     Gets or sets the test identifier of the error message.
    /// </summary>
    [Parameter]
    public string ErrorTestId { get; set; } = "thread-error";

    /// <summary>
    ///     Gets or sets the test identifier of the keyboard hint.
    /// </summary>
    [Parameter]
    public string HintTestId { get; set; } = "thread-submit-hint";

    /// <summary>
    ///     Gets or sets the test identifier of the kind choice (its radio buttons append <c>-question</c> and
    ///     <c>-finding</c>).
    /// </summary>
    [Parameter]
    public string KindTestId { get; set; } = "thread-kind-choice";

    /// <summary>
    ///     Gets or sets the test identifier of the "Add to report" tick of a reply.
    /// </summary>
    [Parameter]
    public string ReportTestId { get; set; } = "reply-report";

    /// <summary>
    ///     Gets or sets what the page shows before the kind choice of a new thread (the line fields).
    /// </summary>
    [Parameter]
    public RenderFragment? Fields { get; set; }

    /// <summary>
    ///     Gets or sets what the page shows between the text and the buttons (the signature warning).
    /// </summary>
    [Parameter]
    public RenderFragment? Notices { get; set; }

    /// <summary>
    ///     Gets or sets the buttons the page adds next to the submit button of a new thread (Cancel and the like).
    /// </summary>
    [Parameter]
    public RenderFragment? Actions { get; set; }

    /// <summary>
    ///     Hands the typed text to the parent.
    /// </summary>
    /// <param name="args">The input event.</param>
    /// <returns>A task that completes when the parent has been told.</returns>
    private Task OnTextInput(ChangeEventArgs args)
    {
        return TextChanged.InvokeAsync(args.Value?.ToString() ?? string.Empty);
    }

    /// <summary>
    ///     Hands the state of the "Add to report" tick to the parent.
    /// </summary>
    /// <param name="args">The change event.</param>
    /// <returns>A task that completes when the parent has been told.</returns>
    private Task OnReportChanged(ChangeEventArgs args)
    {
        return IncludeInReportChanged.InvokeAsync(args.Value is true);
    }

    /// <summary>
    ///     Hands the chosen kind to the parent.
    /// </summary>
    /// <param name="finding"><c>true</c> for Finding, <c>false</c> for Question.</param>
    /// <returns>A task that completes when the parent has been told.</returns>
    private Task ChooseAsync(bool finding)
    {
        return IncludeInReportChanged.InvokeAsync(finding);
    }

    /// <summary>
    ///     Tells the parent that the person submitted.
    /// </summary>
    /// <returns>A task that completes when the parent has handled it.</returns>
    private Task SubmitAsync()
    {
        return OnSubmit.InvokeAsync();
    }
}
