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
using Microsoft.AspNetCore.Components.Web;

namespace DemaConsulting.ReviewCenter.Web;

/// <summary>
///     A request to comment on a line of a diff, raised by the <c>+</c> control of a row.
/// </summary>
/// <remarks>The record is immutable and thread-safe.</remarks>
/// <param name="Side">The version the line number refers to: <see cref="ThreadSide.Baseline"/> for a removed line, otherwise <see cref="ThreadSide.Head"/>.</param>
/// <param name="Line">The 1-based line number on that side.</param>
/// <param name="Extend"><c>true</c> when the reader held Shift to extend the current selection to this line.</param>
public sealed record DiffCommentRequest(ThreadSide Side, int Line, bool Extend);

/// <summary>
///     Code-behind for the diff component (<c>DiffView.razor</c>): the changes of one file side by side (baseline
///     on the left, head on the right), where the reader can comment on a line and read the threads under their
///     rows.
/// </summary>
/// <remarks>
///     The component shows a <see cref="LineDiffResult"/> that the page computed once per file selection; it
///     holds no domain state of its own. The only state it keeps is which collapsed runs of unchanged lines the
///     reader has expanded. Unchanged runs are collapsed (see <see cref="LineDiff.Segment"/>) except where a
///     thread is anchored or the reader expanded them, so every thread is always visible. A comment control in a
///     left cell (a removed line, or the baseline copy of an unchanged line) addresses the baseline numbering, a
///     right cell the head numbering; this is the numbering the thread service validates. The pairing of removed
///     and added lines into rows is <see cref="LineDiff.Pair"/>. The threads and the new-thread form are rendered by the page
///     (passed as fragments) so the existing reply, resolve and reopen behavior is reused unchanged. All text of
///     the file is rendered as plain text.
/// </remarks>
public partial class DiffView : ComponentBase
{
    /// <summary>
    ///     The rows of the diff the cached lookups below were built for.
    /// </summary>
    private LineDiffResult? _builtFor;

    /// <summary>
    ///     The row index of each baseline line number (index 0 unused); <c>-1</c> for a number with no row.
    /// </summary>
    private int[] _oldRows = [];

    /// <summary>
    ///     The row index of each head line number (index 0 unused); <c>-1</c> for a number with no row.
    /// </summary>
    private int[] _newRows = [];

    /// <summary>
    ///     The rows the reader has expanded, which stay visible.
    /// </summary>
    private readonly HashSet<int> _revealed = [];

    /// <summary>
    ///     The rows that carry a thread (the row of the last line of its anchor), the threads of each row in order.
    /// </summary>
    private Dictionary<int, List<ReviewThread>> _threadsByRow = [];

    /// <summary>
    ///     The rows that must stay visible because a thread ends there or the reader is commenting there.
    /// </summary>
    private SortedSet<int> _pins = [];

    /// <summary>
    ///     The row indexes the current segments were built with, to rebuild them only when something changed.
    /// </summary>
    private int[] _pinnedKey = [];

    /// <summary>
    ///     The visible runs and collapsed gaps, rebuilt when the pinned rows or the expanded rows change.
    /// </summary>
    private IReadOnlyList<DiffSegment> _segments = [];

    /// <summary>
    ///     Gets or sets the diff to show. Must not be null.
    /// </summary>
    [Parameter]
    [EditorRequired]
    public LineDiffResult Result { get; set; } = null!;

    /// <summary>
    ///     Gets or sets a value indicating whether the two versions have identical lines and differ only in their
    ///     line endings or final newline.
    /// </summary>
    [Parameter]
    public bool OnlyLineEndingsDiffer { get; set; }

    /// <summary>
    ///     Gets or sets the threads of the file that are anchored to lines (of either side); whole-file threads are
    ///     not shown here.
    /// </summary>
    [Parameter]
    public IReadOnlyList<ReviewThread> Threads { get; set; } = [];

    /// <summary>
    ///     Gets or sets the identity of the thread to highlight, or <c>null</c>.
    /// </summary>
    [Parameter]
    public int? FocusedThreadId { get; set; }

    /// <summary>
    ///     Gets or sets the version the new comment is being written for, or <c>null</c> when none is selected.
    /// </summary>
    [Parameter]
    public ThreadSide? SelectedSide { get; set; }

    /// <summary>
    ///     Gets or sets the first selected line (on <see cref="SelectedSide"/>), or <c>null</c> when none is selected.
    /// </summary>
    [Parameter]
    public int? SelectedStart { get; set; }

    /// <summary>
    ///     Gets or sets the last selected line (on <see cref="SelectedSide"/>), or <c>null</c> when none is selected.
    /// </summary>
    [Parameter]
    public int? SelectedEnd { get; set; }

    /// <summary>
    ///     Gets or sets how a thread is drawn under its line; supplied by the page so the thread controls are shared.
    /// </summary>
    [Parameter]
    public RenderFragment<ReviewThread>? ThreadTemplate { get; set; }

    /// <summary>
    ///     Gets or sets the new-thread form, drawn under the last selected line; supplied by the page.
    /// </summary>
    [Parameter]
    public RenderFragment? Composer { get; set; }

    /// <summary>
    ///     Gets or sets the callback raised when the reader chooses the comment control of a line.
    /// </summary>
    [Parameter]
    public EventCallback<DiffCommentRequest> OnComment { get; set; }

    /// <summary>
    ///     Finds the row that carries a line number of one side.
    /// </summary>
    /// <param name="result">The diff. Must not be null.</param>
    /// <param name="side">The side the number refers to.</param>
    /// <param name="line">The 1-based line number on that side.</param>
    /// <returns>The zero-based row index, or <c>-1</c> when no row has that number.</returns>
    public static int RowOf(LineDiffResult result, ThreadSide side, int line)
    {
        ArgumentNullException.ThrowIfNull(result);
        for (var i = 0; i < result.Lines.Count; i++)
        {
            var number = side == ThreadSide.Baseline ? result.Lines[i].OldNumber : result.Lines[i].NewNumber;
            if (number == line)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    ///     Finds the first row that is an addition or a removal.
    /// </summary>
    /// <param name="result">The diff. Must not be null.</param>
    /// <returns>The zero-based row index, or <c>-1</c> when nothing changed.</returns>
    public static int FirstChangeRow(LineDiffResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        for (var i = 0; i < result.Lines.Count; i++)
        {
            if (result.Lines[i].Kind != DiffLineKind.Context)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    ///     Rebuilds the lookups and the collapsed runs when the diff, the threads or the expanded rows changed.
    /// </summary>
    protected override void OnParametersSet()
    {
        ArgumentNullException.ThrowIfNull(Result);

        // A new diff (another file) starts collapsed again and gets fresh number lookups
        if (!ReferenceEquals(_builtFor, Result))
        {
            _builtFor = Result;
            _revealed.Clear();
            _segments = [];
            BuildRowLookups();
        }

        // Every thread keeps its line visible, so no thread is ever hidden inside a collapsed run
        _threadsByRow = [];
        _pins = [];

        // The row being commented on stays visible with its context, so the form is never inside a collapsed run
        if (SelectedSide is { } selectedSide)
        {
            foreach (var number in new[] { SelectedStart, SelectedEnd })
            {
                if (number is { } selected && RowIndex(selectedSide, selected) is var selectedRow and >= 0)
                {
                    _pins.Add(selectedRow);
                }
            }
        }

        foreach (var thread in Threads.Where(t => t.StartLine is not null))
        {
            var side = thread.Side;
            var row = RowIndex(side, thread.EndLine!.Value);
            if (row < 0)
            {
                continue;
            }

            if (!_threadsByRow.TryGetValue(row, out var here))
            {
                here = [];
                _threadsByRow[row] = here;
            }

            here.Add(thread);
            _pins.Add(row);
        }

        RebuildSegments();
    }

    /// <summary>
    ///     Rebuilds the visible runs and collapsed gaps from the pinned and the expanded rows.
    /// </summary>
    /// <remarks>Walking every row is skipped while nothing that affects the runs changed.</remarks>
    private void RebuildSegments()
    {
        var pinned = new SortedSet<int>(_pins);
        pinned.UnionWith(_revealed);
        var key = pinned.ToArray();
        if (!key.AsSpan().SequenceEqual(_pinnedKey) || _segments.Count == 0)
        {
            _pinnedKey = key;
            _segments = LineDiff.Segment(Result.Lines, LineDiff.DefaultContextLines, pinned);
        }
    }

    /// <summary>
    ///     Builds the lookups from a line number to its row, for both sides.
    /// </summary>
    private void BuildRowLookups()
    {
        _oldRows = new int[Result.Removed + CountContext() + 1];
        _newRows = new int[Result.Added + CountContext() + 1];
        Array.Fill(_oldRows, -1);
        Array.Fill(_newRows, -1);
        for (var i = 0; i < Result.Lines.Count; i++)
        {
            var line = Result.Lines[i];
            if (line.OldNumber is { } old)
            {
                _oldRows[old] = i;
            }

            if (line.NewNumber is { } current)
            {
                _newRows[current] = i;
            }
        }
    }

    /// <summary>
    ///     Counts the unchanged rows of the diff.
    /// </summary>
    /// <returns>The number of rows that are neither added nor removed.</returns>
    private int CountContext()
    {
        return Result.Lines.Count - Result.Added - Result.Removed;
    }

    /// <summary>
    ///     Looks up the row of a line number of one side.
    /// </summary>
    /// <param name="side">The side.</param>
    /// <param name="line">The 1-based line number.</param>
    /// <returns>The row index, or <c>-1</c> when the side has no such line.</returns>
    private int RowIndex(ThreadSide side, int line)
    {
        var rows = side == ThreadSide.Baseline ? _oldRows : _newRows;
        return line >= 1 && line < rows.Length ? rows[line] : -1;
    }

    /// <summary>
    ///     Expands a collapsed run so its rows stay visible.
    /// </summary>
    /// <param name="segment">The collapsed run.</param>
    private void Reveal(DiffSegment segment)
    {
        for (var i = segment.Start; i < segment.Start + segment.Count; i++)
        {
            _revealed.Add(i);
        }

        RebuildSegments();
    }

    /// <summary>
    ///     Gets the row indexes a side-by-side row stands for.
    /// </summary>
    /// <param name="pair">The side-by-side row.</param>
    /// <returns>The index of its left line and of its right line, once when both are the same line; never empty.</returns>
    private static int[] RowsOf(DiffRow pair)
    {
        return pair.LeftIndex is { } left && pair.RightIndex is { } right && left != right
            ? [left, right]
            : [pair.LeftIndex ?? pair.RightIndex!.Value];
    }

    /// <summary>
    ///     Gets the key that names a cell in its test ids.
    /// </summary>
    /// <param name="line">The line shown in the cell.</param>
    /// <param name="row">The row index of the line.</param>
    /// <param name="isLeft"><c>true</c> for the left (baseline) cell.</param>
    /// <returns>
    ///     The row index; the left cell of an unchanged line, which repeats the right cell, gets an "old-" prefix
    ///     so both ids stay unique.
    /// </returns>
    private static string CellKey(DiffLine line, int row, bool isLeft)
    {
        var index = row.ToString(CultureInfo.InvariantCulture);
        return isLeft && line.Kind == DiffLineKind.Context ? $"old-{index}" : index;
    }

    /// <summary>
    ///     Raises the comment callback for a cell.
    /// </summary>
    /// <param name="row">The row index of the line.</param>
    /// <param name="side">The side of the cell: the baseline for the left cell, the head for the right cell.</param>
    /// <param name="args">The click, to read whether Shift was held.</param>
    /// <returns>A task that completes when the callback has run.</returns>
    private Task CommentAsync(int row, ThreadSide side, MouseEventArgs args)
    {
        // The cell decides the numbering: the left cell is the baseline, the right cell the head
        var line = Result.Lines[row];
        var number = side == ThreadSide.Baseline ? line.OldNumber : line.NewNumber;
        return OnComment.InvokeAsync(new DiffCommentRequest(side, number!.Value, args.ShiftKey));
    }

    /// <summary>
    ///     Tells whether a cell is inside the selection of the new comment.
    /// </summary>
    /// <param name="line">The line shown in the cell.</param>
    /// <param name="side">The side of the cell.</param>
    /// <returns><c>true</c> when the selection is on this side and the line's number lies in the selected range.</returns>
    private bool IsSelected(DiffLine line, ThreadSide side)
    {
        return SelectedSide == side && InRange(line, side, SelectedStart, SelectedEnd);
    }

    /// <summary>
    ///     Tells whether a cell is inside the anchor of any thread.
    /// </summary>
    /// <param name="line">The line shown in the cell.</param>
    /// <param name="side">The side of the cell.</param>
    /// <returns><c>true</c> when a thread on this side covers the line.</returns>
    private bool IsAnchored(DiffLine line, ThreadSide side)
    {
        return Threads.Any(t => t.Side == side && InRange(line, side, t.StartLine, t.EndLine));
    }

    /// <summary>
    ///     Tells whether a cell is inside the anchor of the focused thread.
    /// </summary>
    /// <param name="line">The line shown in the cell.</param>
    /// <param name="side">The side of the cell.</param>
    /// <returns><c>true</c> when the focused thread is on this side and covers the line.</returns>
    private bool IsFocused(DiffLine line, ThreadSide side)
    {
        return FocusedThreadId is { } id
               && Threads.FirstOrDefault(t => t.Id == id) is { } thread
               && thread.Side == side
               && InRange(line, side, thread.StartLine, thread.EndLine);
    }

    /// <summary>
    ///     Tells whether the number of a row on one side lies in a range.
    /// </summary>
    /// <param name="line">The row.</param>
    /// <param name="side">The side whose number is compared.</param>
    /// <param name="start">The first line of the range, or <c>null</c> for no range.</param>
    /// <param name="end">The last line of the range.</param>
    /// <returns><c>true</c> when the row has a number on that side and it is within the range.</returns>
    private static bool InRange(DiffLine line, ThreadSide? side, int? start, int? end)
    {
        if (side is null || start is null || end is null)
        {
            return false;
        }

        var number = side == ThreadSide.Baseline ? line.OldNumber : line.NewNumber;
        return number >= start && number <= end;
    }

    /// <summary>
    ///     Gets the index of the row under which the new-thread form is drawn.
    /// </summary>
    /// <returns>The row of the last selected line (or of the first when the last has no row); <c>-1</c> for no selection.</returns>
    private int ComposerRow()
    {
        if (SelectedSide is not { } side || SelectedStart is not { } start)
        {
            return -1;
        }

        var row = RowIndex(side, SelectedEnd ?? start);
        return row >= 0 ? row : RowIndex(side, start);
    }

    /// <summary>
    ///     Words the one-line summary above the rows.
    /// </summary>
    /// <returns>For example "3 added, 1 removed", or a sentence saying that nothing changed.</returns>
    private string SummaryText()
    {
        if (Result.Added == 0 && Result.Removed == 0)
        {
            return OnlyLineEndingsDiffer
                ? "No line changes; only line endings or the final newline differ."
                : "No changes.";
        }

        return string.Create(CultureInfo.InvariantCulture, $"{Result.Added} added, {Result.Removed} removed");
    }

    /// <summary>
    ///     Gets the CSS class that colors a row by its kind.
    /// </summary>
    /// <param name="line">The row.</param>
    /// <returns><c>added</c>, <c>removed</c> or <c>context</c>.</returns>
    private static string KindClass(DiffLine line)
    {
        return line.Kind switch
        {
            DiffLineKind.Added => "added",
            DiffLineKind.Removed => "removed",
            _ => "context"
        };
    }

    /// <summary>
    ///     Gets the mark shown in front of the text of a row, for readers who cannot tell the colors apart.
    /// </summary>
    /// <param name="line">The row.</param>
    /// <returns>"+" for an added line, "-" for a removed line and a space otherwise.</returns>
    private static string Sign(DiffLine line)
    {
        return line.Kind switch
        {
            DiffLineKind.Added => "+",
            DiffLineKind.Removed => "-",
            _ => " "
        };
    }

    /// <summary>
    ///     Words the accessible name of the comment control of a cell.
    /// </summary>
    /// <param name="line">The line shown in the cell.</param>
    /// <param name="side">The side of the cell.</param>
    /// <returns>
    ///     For example "Comment on removed line 12", "Comment on added line 14", "Comment on line 7" (head) or
    ///     "Comment on baseline line 7" for the left copy of an unchanged line.
    /// </returns>
    private static string CommentLabel(DiffLine line, ThreadSide side)
    {
        return line.Kind switch
        {
            DiffLineKind.Removed => string.Create(CultureInfo.InvariantCulture, $"Comment on removed line {line.OldNumber}"),
            DiffLineKind.Added => string.Create(CultureInfo.InvariantCulture, $"Comment on added line {line.NewNumber}"),
            _ when side == ThreadSide.Baseline => string.Create(CultureInfo.InvariantCulture, $"Comment on baseline line {line.OldNumber}"),
            _ => string.Create(CultureInfo.InvariantCulture, $"Comment on line {line.NewNumber}")
        };
    }
}
