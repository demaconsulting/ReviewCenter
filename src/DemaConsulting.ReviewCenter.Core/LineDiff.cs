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

namespace DemaConsulting.ReviewCenter.Core;

/// <summary>
///     How a line appears in a line diff.
/// </summary>
public enum DiffLineKind
{
    /// <summary>
    ///     The line is the same in the baseline and in the head.
    /// </summary>
    Context,

    /// <summary>
    ///     The line exists only in the head.
    /// </summary>
    Added,

    /// <summary>
    ///     The line exists only in the baseline.
    /// </summary>
    Removed
}

/// <summary>
///     One row of a line diff.
/// </summary>
/// <remarks>
///     The numbers are 1-based positions in the line lists given to <see cref="LineDiff.Compute"/>, which come
///     from <see cref="FileText.TryReadLines"/>; they are therefore the same numbers a thread anchor uses. The
///     record is immutable and thread-safe.
/// </remarks>
/// <param name="Kind">Whether the line is unchanged, added or removed.</param>
/// <param name="OldNumber">The baseline line number; <c>null</c> for an added line.</param>
/// <param name="NewNumber">The head line number; <c>null</c> for a removed line.</param>
/// <param name="Text">The line text without its terminator.</param>
public sealed record DiffLine(DiffLineKind Kind, int? OldNumber, int? NewNumber, string Text);

/// <summary>
///     The outcome of comparing two line lists.
/// </summary>
/// <remarks>The record is immutable and thread-safe.</remarks>
/// <param name="TooLarge">
///     <c>true</c> when the inputs exceed <see cref="ReviewLimits.MaxDiffLines"/> or the difference exceeds
///     <see cref="ReviewLimits.MaxDiffEdits"/>; <paramref name="Lines"/> is empty then.
/// </param>
/// <param name="Lines">Every line of both versions in display order: unchanged, removed and added lines.</param>
/// <param name="Added">The number of added lines.</param>
/// <param name="Removed">The number of removed lines.</param>
public sealed record LineDiffResult(bool TooLarge, IReadOnlyList<DiffLine> Lines, int Added, int Removed);

/// <summary>
///     A run of diff rows that is either shown or collapsed.
/// </summary>
/// <remarks>The record is immutable and thread-safe.</remarks>
/// <param name="IsGap"><c>true</c> for a run of unchanged lines that is collapsed; <c>false</c> for rows that are shown.</param>
/// <param name="Start">The index of the first row of the run in <see cref="LineDiffResult.Lines"/>.</param>
/// <param name="Count">The number of rows in the run (at least 1).</param>
public sealed record DiffSegment(bool IsGap, int Start, int Count);

/// <summary>
///     Compares two versions of a file line by line, so a reviewer sees what changed.
/// </summary>
/// <remarks>
///     The comparison runs on line lists rather than bytes, so the line numbers are by construction the ones
///     produced by <see cref="FileText"/>, which is what thread validation uses. The algorithm is the
///     <c>O(ND)</c> shortest-edit-script method of Myers, written here (about a hundred lines) instead of taken
///     from a library, so that it adds no third-party item to review. The common first and last lines are
///     removed first, which makes the usual case (a few small edits in a large file) very cheap. Two limits
///     keep the work of one request bounded: <see cref="ReviewLimits.MaxDiffLines"/> per side and
///     <see cref="ReviewLimits.MaxDiffEdits"/> changed lines; beyond either, the result is flagged
///     <see cref="LineDiffResult.TooLarge"/> and carries no rows. A file that exists on one side only is
///     reported directly as all added or all removed, without the edit limit. Within each block of changes the
///     removed lines come before the added lines. The class is stateless and thread-safe.
/// </remarks>
public static class LineDiff
{
    /// <summary>
    ///     The number of unchanged lines shown around each change by default.
    /// </summary>
    public const int DefaultContextLines = 3;

    /// <summary>
    ///     Compares two line lists.
    /// </summary>
    /// <param name="oldLines">The baseline lines. Must not be null.</param>
    /// <param name="newLines">The head lines. Must not be null.</param>
    /// <returns>
    ///     The rows covering every line of both lists, with the counts of added and removed lines; or a result
    ///     flagged <see cref="LineDiffResult.TooLarge"/> when a limit is exceeded. Lines are compared ordinally.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    public static LineDiffResult Compute(IReadOnlyList<string> oldLines, IReadOnlyList<string> newLines)
    {
        ArgumentNullException.ThrowIfNull(oldLines);
        ArgumentNullException.ThrowIfNull(newLines);

        // Refuse oversized inputs before doing any work
        if (oldLines.Count > ReviewLimits.MaxDiffLines || newLines.Count > ReviewLimits.MaxDiffLines)
        {
            return TooLargeResult();
        }

        // Lines shared at the start and end need no search; they are context whatever the middle looks like
        var prefix = 0;
        var shortest = Math.Min(oldLines.Count, newLines.Count);
        while (prefix < shortest && string.Equals(oldLines[prefix], newLines[prefix], StringComparison.Ordinal))
        {
            prefix++;
        }

        var suffix = 0;
        while (suffix < shortest - prefix
               && string.Equals(oldLines[^(suffix + 1)], newLines[^(suffix + 1)], StringComparison.Ordinal))
        {
            suffix++;
        }

        var oldMiddle = oldLines.Count - prefix - suffix;
        var newMiddle = newLines.Count - prefix - suffix;

        // Find the edit script of the middle part; one side empty needs no search
        List<DiffLineKind>? script;
        if (oldMiddle == 0)
        {
            script = Enumerable.Repeat(DiffLineKind.Added, newMiddle).ToList();
        }
        else if (newMiddle == 0)
        {
            script = Enumerable.Repeat(DiffLineKind.Removed, oldMiddle).ToList();
        }
        else
        {
            script = Myers(oldLines, newLines, prefix, oldMiddle, newMiddle);
            if (script is null)
            {
                return TooLargeResult();
            }
        }

        return Assemble(oldLines, newLines, prefix, suffix, script);
    }

    /// <summary>
    ///     Splits a diff into the rows that are shown and the runs of unchanged rows that are collapsed.
    /// </summary>
    /// <remarks>
    ///     Rows within <paramref name="contextLines"/> of a change are shown. A run of hidden rows of length 1 is
    ///     shown too, because a "show 1 unchanged line" button would take more room than the line itself; longer
    ///     runs become a gap. A diff without any change is one gap (or the lone row when it has a single line).
    ///     The caller can name rows that must stay visible although they are unchanged (a row that carries a
    ///     comment thread, or a row the reader has revealed); such a row is treated like a change, so it keeps
    ///     its context rows too.
    /// </remarks>
    /// <param name="lines">The rows of <see cref="LineDiffResult.Lines"/>. Must not be null.</param>
    /// <param name="contextLines">The number of unchanged rows to keep around each change; not negative.</param>
    /// <param name="pinnedRows">
    ///     Zero-based row indexes to keep visible like changes; <c>null</c> for none. Indexes outside the rows
    ///     are ignored.
    /// </param>
    /// <returns>Consecutive segments that together cover every row exactly once, in order; empty for no rows.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="lines"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="contextLines"/> is negative.</exception>
    public static IReadOnlyList<DiffSegment> Segment(
        IReadOnlyList<DiffLine> lines,
        int contextLines = DefaultContextLines,
        IReadOnlySet<int>? pinnedRows = null)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentOutOfRangeException.ThrowIfNegative(contextLines);

        // Mark every row within the context distance of a change (or of a pinned row) as visible
        var visible = new bool[lines.Count];
        for (var i = 0; i < lines.Count; i++)
        {
            if (lines[i].Kind == DiffLineKind.Context && pinnedRows?.Contains(i) != true)
            {
                continue;
            }

            var from = Math.Max(0, i - contextLines);
            var to = Math.Min(lines.Count - 1, i + contextLines);
            for (var j = from; j <= to; j++)
            {
                visible[j] = true;
            }
        }

        // Walk the rows and cut them into runs of equal visibility; a hidden run of one row is kept visible
        var segments = new List<DiffSegment>();
        var start = 0;
        while (start < lines.Count)
        {
            var end = start;
            while (end < lines.Count && visible[end] == visible[start])
            {
                end++;
            }

            var count = end - start;
            var isGap = !visible[start] && count >= 2;
            if (segments.Count > 0 && segments[^1].IsGap == isGap)
            {
                // A kept single hidden row joins the visible run next to it
                var last = segments[^1];
                segments[^1] = last with { Count = last.Count + count };
            }
            else
            {
                segments.Add(new DiffSegment(isGap, start, count));
            }

            start = end;
        }

        return segments;
    }

    /// <summary>
    ///     Builds the result that stands for an input that is too large to compare.
    /// </summary>
    /// <returns>A flagged result without rows.</returns>
    private static LineDiffResult TooLargeResult()
    {
        return new LineDiffResult(true, [], 0, 0);
    }

    /// <summary>
    ///     Finds the shortest edit script between the middle parts of two line lists with Myers' algorithm.
    /// </summary>
    /// <remarks>
    ///     The lines are first mapped to integers so the inner loop compares numbers, not strings. The search
    ///     keeps, for every edit distance, a copy of the furthest-reaching points of the diagonals reached so far
    ///     (about <c>D*D</c> integers in total) and walks them backwards to recover the script.
    /// </remarks>
    /// <param name="oldLines">All baseline lines.</param>
    /// <param name="newLines">All head lines.</param>
    /// <param name="offset">The number of common leading lines, which are not part of the middle.</param>
    /// <param name="oldCount">The number of baseline lines in the middle; at least 1.</param>
    /// <param name="newCount">The number of head lines in the middle; at least 1.</param>
    /// <returns>
    ///     The operations in order: <see cref="DiffLineKind.Context"/>, <see cref="DiffLineKind.Removed"/> or
    ///     <see cref="DiffLineKind.Added"/>; or <c>null</c> when more than <see cref="ReviewLimits.MaxDiffEdits"/>
    ///     edits would be needed.
    /// </returns>
    private static List<DiffLineKind>? Myers(
        IReadOnlyList<string> oldLines,
        IReadOnlyList<string> newLines,
        int offset,
        int oldCount,
        int newCount)
    {
        // Map every distinct line to a number so that equal lines have equal numbers
        var ids = new Dictionary<string, int>(StringComparer.Ordinal);
        var a = new int[oldCount];
        var b = new int[newCount];
        for (var i = 0; i < oldCount; i++)
        {
            a[i] = Identify(ids, oldLines[offset + i]);
        }

        for (var i = 0; i < newCount; i++)
        {
            b[i] = Identify(ids, newLines[offset + i]);
        }

        // The furthest x reached on each diagonal k = x - y; index k + center. Points may run one past the
        // end of a list, which is harmless because they can never come back to the exact end point
        var maxEdits = Math.Min(oldCount + newCount, ReviewLimits.MaxDiffEdits);
        var center = maxEdits + 1;
        var v = new int[(2 * maxEdits) + 3];
        var trace = new List<int[]>(maxEdits + 1);
        var found = -1;

        for (var d = 0; d <= maxEdits && found < 0; d++)
        {
            for (var k = -d; k <= d; k += 2)
            {
                // Step down (add a head line) from the neighbor that got further, otherwise step right (remove)
                var x = k == -d || (k != d && v[center + k - 1] < v[center + k + 1])
                    ? v[center + k + 1]
                    : v[center + k - 1] + 1;
                var y = x - k;

                // Follow the diagonal for as long as the lines are equal
                while (x < oldCount && y < newCount && a[x] == b[y])
                {
                    x++;
                    y++;
                }

                v[center + k] = x;
                if (x == oldCount && y == newCount)
                {
                    found = d;
                    break;
                }
            }

            // Keep the diagonals of this distance for the backward walk
            var snapshot = new int[(2 * d) + 1];
            Array.Copy(v, center - d, snapshot, 0, snapshot.Length);
            trace.Add(snapshot);
        }

        if (found < 0)
        {
            return null;
        }

        return Backtrack(trace, found, oldCount, newCount);
    }

    /// <summary>
    ///     Recovers the edit script from the recorded diagonals.
    /// </summary>
    /// <param name="trace">For each distance d, the furthest x of the diagonals -d..d after that distance.</param>
    /// <param name="distance">The edit distance of the solution.</param>
    /// <param name="oldCount">The number of baseline lines of the middle.</param>
    /// <param name="newCount">The number of head lines of the middle.</param>
    /// <returns>The operations from the start to the end of the middle.</returns>
    private static List<DiffLineKind> Backtrack(List<int[]> trace, int distance, int oldCount, int newCount)
    {
        var reversed = new List<DiffLineKind>(oldCount + newCount);
        var x = oldCount;
        var y = newCount;

        for (var d = distance; d > 0; d--)
        {
            // Which neighbor did this point come from? The same rule as the forward search, on the previous distance
            var previous = trace[d - 1];
            var k = x - y;
            var down = k == -d || (k != d && previous[k - 1 + d - 1] < previous[k + 1 + d - 1]);
            var previousK = down ? k + 1 : k - 1;
            var previousX = previous[previousK + d - 1];
            var previousY = previousX - previousK;

            // The diagonal run that ended at this point, then the single step that started it
            var stepX = down ? previousX : previousX + 1;
            var stepY = down ? previousY + 1 : previousY;
            while (x > stepX && y > stepY)
            {
                reversed.Add(DiffLineKind.Context);
                x--;
                y--;
            }

            reversed.Add(down ? DiffLineKind.Added : DiffLineKind.Removed);
            x = previousX;
            y = previousY;
        }

        // Distance zero is a pure diagonal from the start
        while (x > 0 && y > 0)
        {
            reversed.Add(DiffLineKind.Context);
            x--;
            y--;
        }

        reversed.Reverse();
        return reversed;
    }

    /// <summary>
    ///     Gives a line text a stable number, so equal texts share one.
    /// </summary>
    /// <param name="ids">The numbers assigned so far.</param>
    /// <param name="text">The line text.</param>
    /// <returns>The number of the text.</returns>
    private static int Identify(Dictionary<string, int> ids, string text)
    {
        if (!ids.TryGetValue(text, out var id))
        {
            id = ids.Count;
            ids[text] = id;
        }

        return id;
    }

    /// <summary>
    ///     Combines the shared lines and the edit script into the rows of the result.
    /// </summary>
    /// <param name="oldLines">All baseline lines.</param>
    /// <param name="newLines">All head lines.</param>
    /// <param name="prefix">The number of common leading lines.</param>
    /// <param name="suffix">The number of common trailing lines.</param>
    /// <param name="script">The operations for the middle part.</param>
    /// <returns>The result, with removed lines placed before added lines inside each block of changes.</returns>
    private static LineDiffResult Assemble(
        IReadOnlyList<string> oldLines,
        IReadOnlyList<string> newLines,
        int prefix,
        int suffix,
        List<DiffLineKind> script)
    {
        var rows = new List<DiffLine>(oldLines.Count + newLines.Count);
        var oldIndex = 0;
        var newIndex = 0;
        var added = 0;
        var removed = 0;

        // The leading shared lines
        for (; oldIndex < prefix; oldIndex++, newIndex++)
        {
            rows.Add(new DiffLine(DiffLineKind.Context, oldIndex + 1, newIndex + 1, oldLines[oldIndex]));
        }

        // The middle: a block of changes is every run of non-context operations; show its removals first
        var position = 0;
        while (position < script.Count)
        {
            if (script[position] == DiffLineKind.Context)
            {
                rows.Add(new DiffLine(DiffLineKind.Context, oldIndex + 1, newIndex + 1, oldLines[oldIndex]));
                oldIndex++;
                newIndex++;
                position++;
                continue;
            }

            var end = position;
            while (end < script.Count && script[end] != DiffLineKind.Context)
            {
                end++;
            }

            var removals = script.Skip(position).Take(end - position).Count(kind => kind == DiffLineKind.Removed);
            var additions = end - position - removals;
            for (var i = 0; i < removals; i++, oldIndex++)
            {
                rows.Add(new DiffLine(DiffLineKind.Removed, oldIndex + 1, null, oldLines[oldIndex]));
            }

            for (var i = 0; i < additions; i++, newIndex++)
            {
                rows.Add(new DiffLine(DiffLineKind.Added, null, newIndex + 1, newLines[newIndex]));
            }

            removed += removals;
            added += additions;
            position = end;
        }

        // The trailing shared lines
        for (var i = 0; i < suffix; i++, oldIndex++, newIndex++)
        {
            rows.Add(new DiffLine(DiffLineKind.Context, oldIndex + 1, newIndex + 1, oldLines[oldIndex]));
        }

        return new LineDiffResult(false, rows, added, removed);
    }
}
