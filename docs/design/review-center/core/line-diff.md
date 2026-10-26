### LineDiff

![Core Structure](CoreView.svg)

#### Purpose

`LineDiff` compares the lines of two versions of a file and gives the rows a reader needs to see what changed:
which lines are unchanged, which were removed from the baseline, and which were added at the head. It also
divides the rows into regions to show and runs of unchanged rows to collapse. Its single responsibility is the
comparison; it does not read files, decide which side of a review is compared, or render anything. It is a
stateless static class and therefore thread-safe.

#### Data Model

The unit works on the lists of lines that `FileText` gives (`SplitLines` and `TryReadLines`), so the line model is
the one the thread rules use: a line ends at a line feed, a carriage return before the line feed is not part of
the line, a lone carriage return is part of the line, and a final line ending adds no empty line. Lines are
compared as exact strings, case and white space included.

- `DiffLineKind` — `Context` (the line is in both versions), `Removed` (only in the baseline), `Added` (only at
  the head).
- `DiffLine(Kind, OldNumber, NewNumber, Text)` — one row. `OldNumber` is the 1-based baseline line number and is
  `null` for an added row; `NewNumber` is the 1-based head line number and is `null` for a removed row.
- `LineDiffResult(TooLarge, Lines, Added, Removed)` — the rows in display order and the numbers of added and
  removed rows. When `TooLarge` is `true`, `Lines` is empty.
- `DiffSegment(IsGap, Start, Count)` — a range of rows by index; a gap is a run of context rows to collapse.

The bounds are `ReviewLimits.MaxDiffLines` (10,000 lines on either side) and `ReviewLimits.MaxDiffEdits` (2,000
added plus removed lines).

#### Key Methods

**Compute**: Compares two lists of lines.

- *Parameters*: `IReadOnlyList<string> oldLines` — the baseline lines; `IReadOnlyList<string> newLines` — the head
  lines.
- *Returns*: `LineDiffResult` — the rows, or `TooLarge`.
- *Preconditions*: Neither list is null.
- *Postconditions*: The context and removed rows, in order, are exactly `oldLines`, and the context and added rows,
  in order, are exactly `newLines`. The number of removed plus added rows is the smallest possible. Within a block
  of changes, the removed rows come before the added rows. Row numbers on each side run from 1 without gaps.

Algorithm:

1. If either list has more than `MaxDiffLines` lines, return `TooLarge`.
2. Remove the common prefix and the common suffix of the two lists; they are context rows.
3. If one remaining part is empty, the other part is all added or all removed. This case does not use the edit
   bound, because it needs no search.
4. Otherwise map every distinct line text to an integer, and run the shortest edit script algorithm of Myers
   (*An O(ND) Difference Algorithm and Its Variations*, 1986) on the integers. The search keeps one snapshot of
   the furthest-reaching points per edit distance, up to `MaxDiffEdits`; if no script of that length exists,
   return `TooLarge`. The edit script is recovered by walking the snapshots backwards.
5. Put the removed rows of each block of changes before its added rows, and number the rows.

The memory used is bounded by the line bound and the edit bound (at most about `MaxDiffEdits` snapshots of at most
`2 * MaxDiffEdits + 1` integers), and the time by `O((N + M) * D)` with `D` at most `MaxDiffEdits`.

**Segment**: Divides rows into shown regions and gaps.

- *Parameters*: `IReadOnlyList<DiffLine> lines` — the rows; `int contextLines` — the unchanged rows to keep around
  a change (default 3).
- *Returns*: `IReadOnlyList<DiffSegment>` — consecutive segments that cover every row exactly once.
- *Postconditions*: Every changed row and the `contextLines` rows before and after it are in shown segments.
  Shown segments that touch are merged. A run of unchanged rows between two shown segments becomes a gap only
  when it has at least two rows; a run of one row stays shown, because collapsing it would take more room than
  showing it. The same holds at the start and the end of the file. A file without changes is one gap, or its single
  row when it has only one, or no segment when it has no rows.

#### Error Handling

A null argument raises `ArgumentNullException`; a negative number of context rows raises
`ArgumentOutOfRangeException`. Input that is too large is not an error: the result says `TooLarge` so the caller
can explain it. The unit throws no other exception for any text.

#### Dependencies

- **ReviewLimits** — the bounds `MaxDiffLines` and `MaxDiffEdits`.
- **.NET runtime library** — collections and string comparison (ordinal).

#### Callers

- **ReviewService** — `GetDiffAsync` splits the frozen contents into lines with `FileText.TryReadLines` and
  compares them with `Compute`.
- **ReviewPage** — groups the rows with `Segment` for display (planned for the diff view of a later phase).
- **Tests** — `LineDiffTests` check edge cases, the bounds, and a randomized reconstruction property.
