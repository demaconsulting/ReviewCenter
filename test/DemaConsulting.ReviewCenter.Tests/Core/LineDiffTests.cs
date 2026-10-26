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

namespace DemaConsulting.ReviewCenter.Tests.Core;

/// <summary>
///     Unit tests for <see cref="LineDiff"/>: the Myers line comparison, its bounds and the segmenting of rows.
/// </summary>
public class LineDiffTests
{
    /// <summary>
    ///     Splits text into lines the way the review does.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>The lines.</returns>
    private static IReadOnlyList<string> Lines(string text) => FileText.SplitLines(text);

    /// <summary>
    ///     Renders the rows compactly, for example <c>" a"</c>, <c>"-b"</c>, <c>"+c"</c>.
    /// </summary>
    /// <param name="result">The diff.</param>
    /// <returns>One text per row.</returns>
    private static string[] Render(LineDiffResult result)
    {
        return result.Lines.Select(l => Marker(l.Kind) + l.Text).ToArray();
    }

    /// <summary>
    ///     Gets the marker character of a row kind.
    /// </summary>
    /// <param name="kind">The kind.</param>
    /// <returns><c>"+"</c> for added, <c>"-"</c> for removed, a space for context.</returns>
    private static string Marker(DiffLineKind kind)
    {
        return kind switch
        {
            DiffLineKind.Added => "+",
            DiffLineKind.Removed => "-",
            _ => " "
        };
    }

    /// <summary>
    ///     Applies the diff backwards and forwards: the baseline is every context and removed row, the head is
    ///     every context and added row, and each side is numbered 1..n without gaps.
    /// </summary>
    /// <param name="result">The diff.</param>
    /// <param name="oldLines">The expected baseline lines.</param>
    /// <param name="newLines">The expected head lines.</param>
    private static void AssertReconstructs(LineDiffResult result, IReadOnlyList<string> oldLines, IReadOnlyList<string> newLines)
    {
        var rebuiltOld = result.Lines.Where(l => l.Kind != DiffLineKind.Added).ToList();
        var rebuiltNew = result.Lines.Where(l => l.Kind != DiffLineKind.Removed).ToList();
        Assert.Equal(oldLines, rebuiltOld.Select(l => l.Text));
        Assert.Equal(newLines, rebuiltNew.Select(l => l.Text));
        Assert.Equal(Enumerable.Range(1, oldLines.Count), rebuiltOld.Select(l => l.OldNumber!.Value));
        Assert.Equal(Enumerable.Range(1, newLines.Count), rebuiltNew.Select(l => l.NewNumber!.Value));
        Assert.All(result.Lines.Where(l => l.Kind == DiffLineKind.Added), l => Assert.Null(l.OldNumber));
        Assert.All(result.Lines.Where(l => l.Kind == DiffLineKind.Removed), l => Assert.Null(l.NewNumber));
        Assert.Equal(result.Added, result.Lines.Count(l => l.Kind == DiffLineKind.Added));
        Assert.Equal(result.Removed, result.Lines.Count(l => l.Kind == DiffLineKind.Removed));
    }

    /// <summary>
    ///     Computes the length of the longest common subsequence with the textbook dynamic program, as an
    ///     independent oracle for the minimal number of edits.
    /// </summary>
    /// <param name="a">The first list.</param>
    /// <param name="b">The second list.</param>
    /// <returns>The length of the longest common subsequence.</returns>
    private static int LongestCommonSubsequence(IReadOnlyList<string> a, IReadOnlyList<string> b)
    {
        var table = new int[a.Count + 1, b.Count + 1];
        for (var i = 1; i <= a.Count; i++)
        {
            for (var j = 1; j <= b.Count; j++)
            {
                table[i, j] = a[i - 1] == b[j - 1] ? table[i - 1, j - 1] + 1 : Math.Max(table[i - 1, j], table[i, j - 1]);
            }
        }

        return table[a.Count, b.Count];
    }

    /// <summary>
    ///     Test that two empty files have no rows and no changes.
    /// </summary>
    [Fact]
    public void LineDiff_Compute_BothEmpty_NoRows()
    {
        // Arrange, Act: compare nothing with nothing
        var result = LineDiff.Compute([], []);

        // Assert: nothing to show
        Assert.False(result.TooLarge);
        Assert.Empty(result.Lines);
        Assert.Equal(0, result.Added);
        Assert.Equal(0, result.Removed);
    }

    /// <summary>
    ///     Test that identical files are all context, numbered on both sides.
    /// </summary>
    [Fact]
    public void LineDiff_Compute_Identical_AllContext()
    {
        // Arrange: the same three lines
        var lines = Lines("a\nb\nc\n");

        // Act: compare
        var result = LineDiff.Compute(lines, lines);

        // Assert: three context rows with both numbers
        Assert.Equal([" a", " b", " c"], Render(result));
        Assert.Equal(0, result.Added + result.Removed);
        Assert.Equal([1, 2, 3], result.Lines.Select(l => l.OldNumber!.Value));
        Assert.Equal([1, 2, 3], result.Lines.Select(l => l.NewNumber!.Value));
    }

    /// <summary>
    ///     Test that a file compared with nothing is all added (a new file), without any edit limit.
    /// </summary>
    [Fact]
    public void LineDiff_Compute_OldEmpty_AllAdded()
    {
        // Arrange: a baseline without lines and a head with three
        var head = Lines("a\nb\nc\n");

        // Act: compare
        var result = LineDiff.Compute([], head);

        // Assert: every row is added with a head number only
        Assert.Equal(["+a", "+b", "+c"], Render(result));
        Assert.Equal(3, result.Added);
        Assert.Equal(0, result.Removed);
        AssertReconstructs(result, [], head);
    }

    /// <summary>
    ///     Test that a deleted file is all removed.
    /// </summary>
    [Fact]
    public void LineDiff_Compute_NewEmpty_AllRemoved()
    {
        // Arrange: a baseline with two lines and an empty head
        var baseline = Lines("a\nb\n");

        // Act: compare
        var result = LineDiff.Compute(baseline, []);

        // Assert: every row is removed with a baseline number only
        Assert.Equal(["-a", "-b"], Render(result));
        Assert.Equal(0, result.Added);
        Assert.Equal(2, result.Removed);
        AssertReconstructs(result, baseline, []);
    }

    /// <summary>
    ///     Test that a replaced line shows the removal before the addition, with the context around it.
    /// </summary>
    [Fact]
    public void LineDiff_Compute_ReplacedLine_RemovedBeforeAdded()
    {
        // Arrange: one line in the middle changes
        var baseline = Lines("a\nb\nc\n");
        var head = Lines("a\nB\nc\n");

        // Act: compare
        var result = LineDiff.Compute(baseline, head);

        // Assert: context, the old line, the new line, context
        Assert.Equal([" a", "-b", "+B", " c"], Render(result));
        Assert.Equal((1, 1), (result.Added, result.Removed));
        Assert.Equal(2, result.Lines[1].OldNumber);
        Assert.Equal(2, result.Lines[2].NewNumber);
    }

    /// <summary>
    ///     Test the classic example of the Myers paper: the script has the minimal five edits.
    /// </summary>
    [Fact]
    public void LineDiff_Compute_MyersPaperExample_MinimalEdits()
    {
        // Arrange: the sequence A B C A B B A to C B A B A C, one letter per line
        var baseline = "A B C A B B A".Split(' ').ToList();
        var head = "C B A B A C".Split(' ').ToList();

        // Act: compare
        var result = LineDiff.Compute(baseline, head);

        // Assert: five edits (three removed, two added) and the sides reconstruct
        Assert.Equal(5, result.Added + result.Removed);
        AssertReconstructs(result, baseline, head);
    }

    /// <summary>
    ///     Test that inserted and deleted lines at the start and end are found around shared lines.
    /// </summary>
    [Fact]
    public void LineDiff_Compute_InsertAtStartAndDeleteAtEnd_AreFound()
    {
        // Arrange: a line is added in front and the last one is removed
        var baseline = Lines("a\nb\nc\n");
        var head = Lines("x\na\nb\n");

        // Act: compare
        var result = LineDiff.Compute(baseline, head);

        // Assert: one added at the start, one removed at the end
        Assert.Equal(["+x", " a", " b", "-c"], Render(result));
    }

    /// <summary>
    ///     Test that CRLF and LF versions of the same text have no line changes, because the lines are the same.
    /// </summary>
    [Fact]
    public void LineDiff_Compute_CrlfVersusLf_NoLineChanges()
    {
        // Arrange: the same lines with different terminators
        var crlf = Lines("a\r\nb\r\n");
        var lf = Lines("a\nb\n");

        // Act: compare
        var result = LineDiff.Compute(crlf, lf);

        // Assert: all context
        Assert.Equal(0, result.Added + result.Removed);
        Assert.Equal(2, result.Lines.Count);
    }

    /// <summary>
    ///     Test that a missing final newline is not a line change.
    /// </summary>
    [Fact]
    public void LineDiff_Compute_NoTrailingNewline_NoLineChanges()
    {
        // Arrange: the same lines with and without a final terminator
        var without = Lines("a\nb");
        var with = Lines("a\nb\n");

        // Act: compare
        var result = LineDiff.Compute(without, with);

        // Assert: all context
        Assert.Equal(0, result.Added + result.Removed);
        Assert.Equal(2, result.Lines.Count);
    }

    /// <summary>
    ///     Test that a lone carriage return stays part of its line, so such lines compare as different.
    /// </summary>
    [Fact]
    public void LineDiff_Compute_LoneCarriageReturn_IsPartOfTheLine()
    {
        // Arrange: a line that ends in a lone CR at the end of the text, and the same line without it
        var withCr = Lines("a\rb");
        var without = Lines("a\nb");

        // Act: compare
        var result = LineDiff.Compute(withCr, without);

        // Assert: one line "a\rb" against two lines "a" and "b"
        Assert.Equal(["-a\rb", "+a", "+b"], Render(result));
    }

    /// <summary>
    ///     Test that line numbers are the ones <see cref="FileText.SplitLines"/> defines, for a mixed text.
    /// </summary>
    [Fact]
    public void LineDiff_Compute_Numbering_AgreesWithFileTextSplitLines()
    {
        // Arrange: texts with blank lines, mixed terminators and no final newline
        var oldText = "one\r\n\r\ntwo\nthree\n\nfour";
        var newText = "one\n\ntwo\r\nTHREE\n\nfour\nfive\n";
        var oldLines = FileText.SplitLines(oldText);
        var newLines = FileText.SplitLines(newText);

        // Act: compare
        var result = LineDiff.Compute(oldLines, newLines);

        // Assert: the numbers run 1..n on each side exactly as SplitLines counts
        AssertReconstructs(result, oldLines, newLines);
        Assert.Equal(oldLines.Count, result.Lines.Count(l => l.OldNumber is not null));
        Assert.Equal(newLines.Count, result.Lines.Count(l => l.NewNumber is not null));
    }

    /// <summary>
    ///     Test that exactly the largest allowed number of lines is compared and one more is too large.
    /// </summary>
    [Fact]
    public void LineDiff_Compute_LineLimit_IsInclusive()
    {
        // Arrange: files of exactly the limit and one line more
        var limit = Enumerable.Range(0, ReviewLimits.MaxDiffLines).Select(i => $"line {i}").ToList();
        var over = limit.Append("one more").ToList();

        // Act: compare the limit with itself, and the larger file against both a small file and the limit
        var same = LineDiff.Compute(limit, limit);
        var tooManyNew = LineDiff.Compute(limit, over);
        var tooManyOld = LineDiff.Compute(over, ["x"]);

        // Assert: the limit is fine, one more on either side is too large and carries no rows
        Assert.False(same.TooLarge);
        Assert.Equal(ReviewLimits.MaxDiffLines, same.Lines.Count);
        Assert.True(tooManyNew.TooLarge);
        Assert.True(tooManyOld.TooLarge);
        Assert.Empty(tooManyNew.Lines);
    }

    /// <summary>
    ///     Test that a new file of the largest size is shown as all added, without the edit limit.
    /// </summary>
    [Fact]
    public void LineDiff_Compute_OneSideEmptyAtLineLimit_AllAddedNotTooLarge()
    {
        // Arrange: no baseline and a head of the largest size
        var head = Enumerable.Range(0, ReviewLimits.MaxDiffLines).Select(i => $"line {i}").ToList();

        // Act: compare
        var result = LineDiff.Compute([], head);

        // Assert: all added
        Assert.False(result.TooLarge);
        Assert.Equal(ReviewLimits.MaxDiffLines, result.Added);
    }

    /// <summary>
    ///     Test that the edit limit is inclusive: exactly the largest number of changed lines is computed, one more
    ///     is too large.
    /// </summary>
    [Fact]
    public void LineDiff_Compute_EditLimit_IsInclusive()
    {
        // Arrange: completely different files with exactly 2000 edits, and with 2001
        var half = ReviewLimits.MaxDiffEdits / 2;
        var oldLines = Enumerable.Range(0, half).Select(i => $"old {i}").ToList();
        var newLines = Enumerable.Range(0, half).Select(i => $"new {i}").ToList();
        var oldPlusOne = oldLines.Append("old extra").ToList();

        // Act: compare
        var atLimit = LineDiff.Compute(oldLines, newLines);
        var overLimit = LineDiff.Compute(oldPlusOne, newLines);

        // Assert: the first is computed and reconstructs, the second is too large
        Assert.False(atLimit.TooLarge);
        Assert.Equal(ReviewLimits.MaxDiffEdits, atLimit.Added + atLimit.Removed);
        AssertReconstructs(atLimit, oldLines, newLines);
        Assert.True(overLimit.TooLarge);
        Assert.Empty(overLimit.Lines);
    }

    /// <summary>
    ///     Test that a large file with a few edits is cheap and exact, because the shared lines are not searched.
    /// </summary>
    [Fact]
    public void LineDiff_Compute_LargeFileFewEdits_FindsTheEdits()
    {
        // Arrange: ten thousand lines with three scattered changes
        var oldLines = Enumerable.Range(0, ReviewLimits.MaxDiffLines).Select(i => $"line {i}").ToList();
        var newLines = oldLines.ToList();
        newLines[100] = "changed";
        newLines.RemoveAt(5000);
        newLines.Insert(9000, "inserted");

        // Act: compare
        var result = LineDiff.Compute(oldLines, newLines);

        // Assert: two added, two removed, reconstructs
        Assert.False(result.TooLarge);
        Assert.Equal((2, 2), (result.Added, result.Removed));
        AssertReconstructs(result, oldLines, newLines);
    }

    /// <summary>
    ///     Property test with a fixed seed: for random small inputs the diff reconstructs both sides, numbers
    ///     them without gaps, and has the minimal number of edits (checked against the textbook LCS).
    /// </summary>
    [Fact]
    public void LineDiff_Compute_RandomInputs_ReconstructBothSidesWithMinimalEdits()
    {
        // Arrange: a fixed seed and a small alphabet, so there are many equal lines and many ways to match
        var random = new Random(20250304);
        for (var round = 0; round < 500; round++)
        {
            var oldLines = Enumerable.Range(0, random.Next(0, 41)).Select(_ => ((char)('a' + random.Next(0, 5))).ToString()).ToList();
            var newLines = Enumerable.Range(0, random.Next(0, 41)).Select(_ => ((char)('a' + random.Next(0, 5))).ToString()).ToList();

            // Act: compare
            var result = LineDiff.Compute(oldLines, newLines);

            // Assert: both sides come back, and no shorter edit script exists
            Assert.False(result.TooLarge);
            AssertReconstructs(result, oldLines, newLines);
            Assert.Equal(oldLines.Count + newLines.Count - (2 * LongestCommonSubsequence(oldLines, newLines)), result.Added + result.Removed);
        }
    }

    /// <summary>
    ///     Test that within a block of changes every removed row comes before every added row, whatever the
    ///     shortest script looks like.
    /// </summary>
    [Fact]
    public void LineDiff_Compute_RandomInputs_RemovedRowsPrecedeAddedRowsInEachBlock()
    {
        // Arrange: a fixed seed
        var random = new Random(77);
        for (var round = 0; round < 200; round++)
        {
            var oldLines = Enumerable.Range(0, random.Next(0, 30)).Select(_ => ((char)('a' + random.Next(0, 4))).ToString()).ToList();
            var newLines = Enumerable.Range(0, random.Next(0, 30)).Select(_ => ((char)('a' + random.Next(0, 4))).ToString()).ToList();

            // Act: compare
            var rows = LineDiff.Compute(oldLines, newLines).Lines;

            // Assert: an added row is never followed directly by a removed row
            for (var i = 1; i < rows.Count; i++)
            {
                Assert.False(rows[i - 1].Kind == DiffLineKind.Added && rows[i].Kind == DiffLineKind.Removed);
            }
        }
    }

    /// <summary>
    ///     Test that a null argument is rejected.
    /// </summary>
    [Fact]
    public void LineDiff_Compute_NullArgument_ThrowsArgumentNullException()
    {
        // Arrange, Act, Assert: each argument is checked
        Assert.Throws<ArgumentNullException>(() => LineDiff.Compute(null!, []));
        Assert.Throws<ArgumentNullException>(() => LineDiff.Compute([], null!));
        Assert.Throws<ArgumentNullException>(() => LineDiff.Segment(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => LineDiff.Segment([], -1));
    }

    /// <summary>
    ///     Builds the rows of a diff of <paramref name="count"/> lines where the lines at the given zero-based
    ///     positions are changed (the others are context).
    /// </summary>
    /// <param name="count">The number of rows.</param>
    /// <param name="changed">The positions that are added rows.</param>
    /// <returns>The rows.</returns>
    private static List<DiffLine> Rows(int count, params int[] changed)
    {
        return Enumerable.Range(0, count)
            .Select(i => changed.Contains(i)
                ? new DiffLine(DiffLineKind.Added, null, i + 1, $"r{i}")
                : new DiffLine(DiffLineKind.Context, i + 1, i + 1, $"r{i}"))
            .ToList();
    }

    /// <summary>
    ///     Test that no rows give no segments, and an unchanged diff is one gap (or the lone row).
    /// </summary>
    [Fact]
    public void LineDiff_Segment_NoChanges_OneGapOrNothing()
    {
        // Arrange, Act: no rows, one unchanged row and ten unchanged rows
        var none = LineDiff.Segment([]);
        var one = LineDiff.Segment(Rows(1));
        var ten = LineDiff.Segment(Rows(10));

        // Assert: nothing; the row itself (a hidden run of one stays visible); one gap of ten
        Assert.Empty(none);
        Assert.Equal([new DiffSegment(false, 0, 1)], one);
        Assert.Equal([new DiffSegment(true, 0, 10)], ten);
    }

    /// <summary>
    ///     Test that three rows of context are kept around a change and the rest collapses.
    /// </summary>
    [Fact]
    public void LineDiff_Segment_SingleChange_KeepsThreeContextRowsEachSide()
    {
        // Arrange: 20 rows, the change at position 10
        var rows = Rows(20, 10);

        // Act: segment
        var segments = LineDiff.Segment(rows);

        // Assert: gap of 7 rows, 7 visible rows (3 + change + 3), gap of 7 rows
        Assert.Equal([new DiffSegment(true, 0, 7), new DiffSegment(false, 7, 7), new DiffSegment(true, 14, 6)], segments);
    }

    /// <summary>
    ///     Test that a change near the start or the end has no gap on that side.
    /// </summary>
    [Fact]
    public void LineDiff_Segment_ChangeAtEdges_HasNoGapOnThatSide()
    {
        // Arrange: changes at the first and at the last row of 20
        var first = LineDiff.Segment(Rows(20, 0));
        var last = LineDiff.Segment(Rows(20, 19));

        // Assert: visible rows 0-3 then a gap; a gap then visible rows 16-19
        Assert.Equal([new DiffSegment(false, 0, 4), new DiffSegment(true, 4, 16)], first);
        Assert.Equal([new DiffSegment(true, 0, 16), new DiffSegment(false, 16, 4)], last);
    }

    /// <summary>
    ///     Test that a hidden run of one row is shown instead of collapsed, and a run of two collapses.
    /// </summary>
    [Fact]
    public void LineDiff_Segment_GapOfOneRow_StaysVisibleAndGapOfTwoCollapses()
    {
        // Arrange: changes at 0 and 8 leave exactly one hidden row (4) between the context ranges 0-3 and 5-11;
        // changes at 0 and 9 leave two hidden rows (4, 5)
        var oneHidden = LineDiff.Segment(Rows(12, 0, 8));
        var twoHidden = LineDiff.Segment(Rows(13, 0, 9));

        // Assert: one visible run; a gap of two between two visible runs
        Assert.Equal([new DiffSegment(false, 0, 12)], oneHidden);
        Assert.Equal([new DiffSegment(false, 0, 4), new DiffSegment(true, 4, 2), new DiffSegment(false, 6, 7)], twoHidden);
    }

    /// <summary>
    ///     Test that nearby changes merge into one visible run, and that the context size is honored.
    /// </summary>
    [Fact]
    public void LineDiff_Segment_NearbyChangesMergeAndContextIsConfigurable()
    {
        // Arrange: changes at 5 and 8 in 30 rows
        var rows = Rows(30, 5, 8);

        // Act: three rows of context, then none
        var three = LineDiff.Segment(rows);
        var zero = LineDiff.Segment(rows, 0);

        // Assert: one visible run 2-11 between two gaps; with no context the changes alone are visible, and the
        // two hidden rows between them collapse
        Assert.Equal([new DiffSegment(true, 0, 2), new DiffSegment(false, 2, 10), new DiffSegment(true, 12, 18)], three);
        Assert.Equal(
            [
                new DiffSegment(true, 0, 5),
                new DiffSegment(false, 5, 1),
                new DiffSegment(true, 6, 2),
                new DiffSegment(false, 8, 1),
                new DiffSegment(true, 9, 21)
            ],
            zero);
    }

    /// <summary>
    ///     Test that the segments always cover every row exactly once, in order.
    /// </summary>
    [Fact]
    public void LineDiff_Segment_RandomChanges_CoverEveryRowOnce()
    {
        // Arrange: a fixed seed and random change positions
        var random = new Random(5);
        for (var round = 0; round < 100; round++)
        {
            var count = random.Next(0, 60);
            var changed = Enumerable.Range(0, count).Where(_ => random.Next(0, 8) == 0).ToArray();

            // Act: segment
            var segments = LineDiff.Segment(Rows(count, changed));

            // Assert: the runs are consecutive, start at 0, end at count, and a gap is never one row
            var next = 0;
            foreach (var segment in segments)
            {
                Assert.Equal(next, segment.Start);
                Assert.True(segment.Count >= 1);
                Assert.False(segment.IsGap && segment.Count < 2);
                next += segment.Count;
            }

            Assert.Equal(count, next);
        }
    }
}
