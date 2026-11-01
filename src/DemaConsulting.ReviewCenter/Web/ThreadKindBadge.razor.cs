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
///     Code-behind for the thread kind badge (<c>ThreadKindBadge.razor</c>): the kind of a thread as an icon and a
///     word, or as the counted marker in the gutter of a line.
/// </summary>
/// <remarks>
///     The component only shows what its parent gives it and holds no state, so it needs no synchronization. The
///     icons are drawn inline (a speech bubble for a Question, a flag for a Finding) and hidden from assistive
///     technology; the visible word, or in the compact form the title and aria-label, names the kind.
/// </remarks>
public partial class ThreadKindBadge : ComponentBase
{
    /// <summary>
    ///     The outline of the speech bubble of a Question, in a 16 by 16 box.
    /// </summary>
    private const string QuestionPath =
        "M2 2.5A1.5 1.5 0 0 1 3.5 1h9A1.5 1.5 0 0 1 14 2.5v7a1.5 1.5 0 0 1-1.5 1.5H7.2l-3 3.2c-.3.3-.8.1-.8-.3V11H3.5A1.5 1.5 0 0 1 2 9.5z";

    /// <summary>
    ///     The outline of the flag of a Finding, in a 16 by 16 box.
    /// </summary>
    private const string FindingPath =
        "M3 1.5a.75.75 0 0 1 1.5 0V2h8.2c.6 0 .9.7.5 1.1L11.3 6l1.9 2.9c.3.4 0 1.1-.5 1.1H4.5v4.5a.75.75 0 0 1-1.5 0z";

    /// <summary>
    ///     Gets or sets the kind to show.
    /// </summary>
    [Parameter]
    public ThreadKind Kind { get; set; } = ThreadKind.Question;

    /// <summary>
    ///     Gets or sets a value indicating whether to show the compact gutter marker (icon and count) instead of the
    ///     badge (icon and word).
    /// </summary>
    [Parameter]
    public bool Compact { get; set; }

    /// <summary>
    ///     Gets or sets the number of threads a compact marker stands for.
    /// </summary>
    [Parameter]
    public int Count { get; set; } = 1;

    /// <summary>
    ///     Gets or sets a value indicating whether a Finding also shows the "In report" badge; ignored in the compact
    ///     form and for a Question.
    /// </summary>
    [Parameter]
    public bool ShowInReport { get; set; } = true;

    /// <summary>
    ///     Gets or sets the stable test identifier of the element; the parts inside it append a suffix.
    /// </summary>
    [Parameter]
    public string TestId { get; set; } = "thread-kind";

    /// <summary>
    ///     Gets the semantic class of the kind: <c>kind-question</c> or <c>kind-finding</c>.
    /// </summary>
    private string KindClass => Kind == ThreadKind.Finding ? "kind-finding" : "kind-question";

    /// <summary>
    ///     Gets the outline of the icon of the kind.
    /// </summary>
    private string IconPath => Kind == ThreadKind.Finding ? FindingPath : QuestionPath;

    /// <summary>
    ///     Gets the visible word of the kind.
    /// </summary>
    private string Label => Kind == ThreadKind.Finding ? "Finding" : "Question";

    /// <summary>
    ///     Gets a value indicating whether the "In report" badge is shown.
    /// </summary>
    private bool ShowsInReport => ShowInReport && Kind == ThreadKind.Finding;

    /// <summary>
    ///     Gets the words of a compact marker, for example <c>2 questions</c> or <c>1 finding</c>.
    /// </summary>
    private string MarkerLabel => Describe(Kind, Count);

    /// <summary>
    ///     Words a number of threads of one kind.
    /// </summary>
    /// <param name="kind">The kind.</param>
    /// <param name="count">The number of threads.</param>
    /// <returns>For example <c>1 question</c> or <c>3 findings</c>.</returns>
    public static string Describe(ThreadKind kind, int count)
    {
        var noun = kind == ThreadKind.Finding ? "finding" : "question";
        return count == 1 ? $"1 {noun}" : $"{count} {noun}s";
    }
}
