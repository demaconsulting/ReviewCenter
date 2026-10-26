### LineDiff

#### Verification Approach

`LineDiff` is verified with unit tests defined in `LineDiffTests.cs`, and the line model it shares with the thread
rules, `FileText.TryReadLines`, with unit tests defined in `FileTextTests.cs`. The tests call the static methods
directly with small lists of lines and compare the rows with the expected rows. Two properties are proved with
seeded pseudo-random inputs instead of examples: the context and removed rows rebuild the baseline and the context
and added rows rebuild the head, and the number of changed rows equals the minimum that an independent
longest-common-subsequence calculation in the test gives. The bounds are proved at the limit itself and one more.
No mocking is applied.

#### Test Environment

N/A - standard test environment.

#### Acceptance Criteria

- All unit tests pass with zero failures.
- Empty lists give no rows, equal lists give only context rows, an empty baseline gives only added rows, and an empty
  head gives only removed rows.
- The script is minimal, including the example of the paper that describes the algorithm, a change at the start, and
  a change at the end.
- The removed rows of a block come before its added rows.
- The rows are numbered like the lines of `FileText.SplitLines`, and text that differs only in line endings, or in a
  missing final line ending, has no changed rows.
- Lists of exactly 10,000 lines and scripts of exactly 2,000 changed lines are accepted; one more is reported as too
  large with no rows; an empty side is not subject to the edit bound.
- The regions keep three context rows around every change, merge when they touch, keep a gap of one row shown,
  collapse a gap of two or more, and cover every row exactly once.
- Pairing puts an unchanged row on both sides, pairs a run of removed rows row by row with the following run of
  added rows with empty cells for the leftover rows, gives pure additions and pure removals empty cells on the other
  side, and covers every row exactly once.
- A binary text gives no lines, and a null argument is rejected with an `ArgumentNullException`.

#### Test Scenarios

**LineDiff_Compute_BothEmpty_NoRows**: Two empty lists give no rows and no changes. This scenario is tested by
`LineDiff_Compute_BothEmpty_NoRows`.

**LineDiff_Compute_Identical_AllContext**: Two equal lists give only context rows, numbered on both sides. This
scenario is tested by `LineDiff_Compute_Identical_AllContext`.

**LineDiff_Compute_OldEmpty_AllAdded**: An empty baseline gives every head line as an added row. This scenario is
tested by `LineDiff_Compute_OldEmpty_AllAdded`.

**LineDiff_Compute_NewEmpty_AllRemoved**: An empty head gives every baseline line as a removed row. This scenario is
tested by `LineDiff_Compute_NewEmpty_AllRemoved`.

**LineDiff_Compute_ReplacedLine_RemovedBeforeAdded**: A replaced line gives the removed row and then the added row,
with the counts of both. This scenario is tested by `LineDiff_Compute_ReplacedLine_RemovedBeforeAdded`.

**LineDiff_Compute_MyersPaperExample_MinimalEdits**: The two sequences of the paper that describes the algorithm
give a script of five changed lines, which is the minimum. This scenario is tested by
`LineDiff_Compute_MyersPaperExample_MinimalEdits`.

**LineDiff_Compute_InsertAtStartAndDeleteAtEnd_AreFound**: A line inserted before the first line and a line deleted
after the last line are both found, with the lines between them as context. This scenario is tested by
`LineDiff_Compute_InsertAtStartAndDeleteAtEnd_AreFound`.

**LineDiff_Compute_CrlfVersusLf_NoLineChanges**: The same text with Windows and with Unix line endings gives no
changed rows. This scenario is tested by `LineDiff_Compute_CrlfVersusLf_NoLineChanges`.

**LineDiff_Compute_NoTrailingNewline_NoLineChanges**: The same text with and without a final line ending gives no
changed rows. This scenario is tested by `LineDiff_Compute_NoTrailingNewline_NoLineChanges`.

**LineDiff_Compute_LoneCarriageReturn_IsPartOfTheLine**: A carriage return that is not followed by a line feed stays
in the line, so two texts that differ in it have a changed line. This scenario is tested by
`LineDiff_Compute_LoneCarriageReturn_IsPartOfTheLine`.

**LineDiff_Compute_Numbering_AgreesWithFileTextSplitLines**: A text with blank lines, mixed line endings, and no final
line ending is compared with a changed text; the baseline rows are as many as `FileText.SplitLines` gives lines for the
baseline text, and likewise for the head. This scenario is tested by
`LineDiff_Compute_Numbering_AgreesWithFileTextSplitLines`.

**LineDiff_Compute_LineLimit_IsInclusive**: Lists of exactly 10,000 lines are compared; a head or a baseline of 10,001
lines is reported as too large with no rows. This scenario is tested by `LineDiff_Compute_LineLimit_IsInclusive`.

**LineDiff_Compute_OneSideEmptyAtLineLimit_AllAddedNotTooLarge**: An empty baseline against a head of exactly 10,000
lines gives all lines added and is not reported as too large, although the number of changes is above the edit bound.
This scenario is tested by `LineDiff_Compute_OneSideEmptyAtLineLimit_AllAddedNotTooLarge`.

**LineDiff_Compute_EditLimit_IsInclusive**: A script of exactly 2,000 changed lines is accepted and one of 2,001 is
reported as too large. This scenario is tested by `LineDiff_Compute_EditLimit_IsInclusive`.

**LineDiff_Compute_LargeFileFewEdits_FindsTheEdits**: A list of 10,000 lines and a copy with one line changed, one
removed, and one inserted give exactly two added and two removed rows. This scenario is tested by
`LineDiff_Compute_LargeFileFewEdits_FindsTheEdits`.

**LineDiff_Compute_RandomInputs_ReconstructBothSidesWithMinimalEdits**: For many seeded random lists, the context and
removed rows are the baseline, the context and added rows are the head, and the number of changed rows is the
minimum. This scenario is tested by `LineDiff_Compute_RandomInputs_ReconstructBothSidesWithMinimalEdits`.

**LineDiff_Compute_RandomInputs_RemovedRowsPrecedeAddedRowsInEachBlock**: For many seeded random lists, no removed row
follows an added row within a block of changes. This scenario is tested by
`LineDiff_Compute_RandomInputs_RemovedRowsPrecedeAddedRowsInEachBlock`.

**LineDiff_Compute_NullArgument_ThrowsArgumentNullException**: A null baseline and a null head are each rejected with
an `ArgumentNullException`. This scenario is tested by `LineDiff_Compute_NullArgument_ThrowsArgumentNullException`.

**LineDiff_Segment_NoChanges_OneGapOrNothing**: No rows give no regions, a single unchanged row stays visible, and
ten unchanged rows give one gap. This scenario is tested by `LineDiff_Segment_NoChanges_OneGapOrNothing`.

**LineDiff_Segment_SingleChange_KeepsThreeContextRowsEachSide**: One change in a long file gives a gap, three context
rows, the change, three context rows, and a gap. This scenario is tested by
`LineDiff_Segment_SingleChange_KeepsThreeContextRowsEachSide`.

**LineDiff_Segment_ChangeAtEdges_HasNoGapOnThatSide**: A change at the first row has no gap before it and a change at
the last row has no gap after it. This scenario is tested by `LineDiff_Segment_ChangeAtEdges_HasNoGapOnThatSide`.

**LineDiff_Segment_GapOfOneRow_StaysVisibleAndGapOfTwoCollapses**: A single unchanged row between two regions is shown,
and two unchanged rows are collapsed into a gap. This scenario is tested by
`LineDiff_Segment_GapOfOneRow_StaysVisibleAndGapOfTwoCollapses`.

**LineDiff_Segment_NearbyChangesMergeAndContextIsConfigurable**: Changes that are three rows apart share one visible
region, and with no context the changes alone are visible and the rows between them collapse. This scenario is
tested by `LineDiff_Segment_NearbyChangesMergeAndContextIsConfigurable`.

**LineDiff_Segment_RandomChanges_CoverEveryRowOnce**: For many seeded random change positions, the regions follow one
another from the first row to the last, and no gap holds fewer than two rows. This scenario is tested by
`LineDiff_Segment_RandomChanges_CoverEveryRowOnce`.

**LineDiff_Segment_PinnedRows_StayVisibleWithContext**: A pinned row in the middle of an unchanged run stays shown
with its context rows and splits the run into two gaps, and pins outside the rows are ignored. This scenario is tested by
`LineDiff_Segment_PinnedRows_StayVisibleWithContext`.

**LineDiff_Pair_ThreeRemovedOneAdded_PairsFirstAndLeavesRightEmpty**: Three removed rows followed by one added row give
three side-by-side rows: the first removed row with the added row, the other two with an empty right cell. This
scenario is tested by `LineDiff_Pair_ThreeRemovedOneAdded_PairsFirstAndLeavesRightEmpty`.

**LineDiff_Pair_OneRemovedThreeAdded_PairsFirstAndLeavesLeftEmpty**: One removed row followed by three added rows give
three rows: the removed row with the first added row, the other two added rows with an empty left cell. This scenario
is tested by `LineDiff_Pair_OneRemovedThreeAdded_PairsFirstAndLeavesLeftEmpty`.

**LineDiff_Pair_EqualRunsPureAddAndPureRemove_AreHandled**: Two removed against two added rows pair completely, only
added rows have empty left cells, and only removed rows have empty right cells. This scenario is tested by
`LineDiff_Pair_EqualRunsPureAddAndPureRemove_AreHandled`.

**LineDiff_Pair_ContextRows_AppearOnBothSidesAndRangesAreRespected**: An unchanged row gives one row with the same index
on both sides, between paired blocks, and a sub-range is paired on its own. This scenario is tested by
`LineDiff_Pair_ContextRows_AppearOnBothSidesAndRangesAreRespected`.

**LineDiff_Pair_InvalidArguments_Throw**: A null list and a range outside the rows are rejected. This scenario is
tested by `LineDiff_Pair_InvalidArguments_Throw`.

**LineDiff_Pair_VisibleSegmentsOfRealDiff_KeepContextGapsAndPins**: The shown segments of a computed diff (with a
pinned unchanged row) are paired; the collapsed gaps stay out, the pinned row is shown, a replacement of one line by
two pairs the first and leaves an empty left cell for the second, and every shown row appears exactly once. This
scenario is tested by `LineDiff_Pair_VisibleSegmentsOfRealDiff_KeepContextGapsAndPins`.

**FileText_TryReadLines_Text_SplitsLikeSplitLines**: Texts with Unix and Windows line endings, no final line ending,
blank lines, a lone carriage return, and an empty text give the same lines as `FileText.SplitLines`. This scenario is
tested by `FileText_TryReadLines_Text_SplitsLikeSplitLines`.

**FileText_TryReadLines_Binary_ReturnsNull**: Content with a zero byte and content that is not valid UTF-8 give null.
This scenario is tested by `FileText_TryReadLines_Binary_ReturnsNull`.

**FileText_TryReadLines_Null_ThrowsArgumentNullException**: Null content is rejected with an
`ArgumentNullException`. This scenario is tested by `FileText_TryReadLines_Null_ThrowsArgumentNullException`.
