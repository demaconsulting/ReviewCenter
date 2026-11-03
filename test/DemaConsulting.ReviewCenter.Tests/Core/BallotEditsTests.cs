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
///     Unit tests for <see cref="BallotEdits"/>: the record or ballot that a ballot edit or a slot change would lead
///     to, computed without changing the input, and the refusal of null arguments. The tests are pure and use no store.
/// </summary>
public class BallotEditsTests
{
    /// <summary>
    ///     Creates an answer.
    /// </summary>
    /// <param name="itemId">The check that is answered.</param>
    /// <param name="outcome">The outcome.</param>
    /// <param name="username">The user name of the person who answered.</param>
    /// <param name="carriedFrom">The review the answer was carried from; <c>null</c> for an answer given in this review.</param>
    /// <returns>The answer.</returns>
    private static ChecklistAnswer Answer(string itemId, ChecklistOutcome outcome, string username = "ann", string? carriedFrom = null)
    {
        return new ChecklistAnswer
        {
            ItemId = itemId,
            Outcome = outcome,
            AnsweredByUsername = username,
            CarriedFromReviewId = carriedFrom
        };
    }

    /// <summary>
    ///     Creates the state of a section.
    /// </summary>
    /// <param name="sectionId">The section.</param>
    /// <param name="applicable">Whether the section applies.</param>
    /// <param name="username">The user name of the person who set the state.</param>
    /// <param name="carriedFrom">The review the state was carried from; <c>null</c> for a state set in this review.</param>
    /// <returns>The state.</returns>
    private static ChecklistSectionState State(string sectionId, bool applicable, string username = "ann", string? carriedFrom = null)
    {
        return new ChecklistSectionState
        {
            SectionId = sectionId,
            Applicable = applicable,
            SetByUsername = username,
            CarriedFromReviewId = carriedFrom
        };
    }

    /// <summary>
    ///     Creates a submitted ballot.
    /// </summary>
    /// <param name="slotId">The slot of the ballot.</param>
    /// <param name="answers">The answers of the ballot.</param>
    /// <param name="sectionStates">The section states of the ballot.</param>
    /// <returns>The ballot.</returns>
    private static Ballot Submitted(int slotId, ChecklistAnswer[] answers, ChecklistSectionState[]? sectionStates = null)
    {
        return new Ballot(
            slotId,
            BallotStatus.Submitted,
            new DateTimeOffset(2026, 1, 5, 10, 0, 0, TimeSpan.Zero),
            "ann",
            answers,
            sectionStates ?? []);
    }

    /// <summary>
    ///     Creates a record with the given ballots.
    /// </summary>
    /// <param name="ballots">The ballots of the record.</param>
    /// <returns>The record.</returns>
    private static ReviewRecord RecordOf(params Ballot[] ballots)
    {
        return new ReviewRecord("fingerprint", null, ballots, [], ReviewSource.Upload);
    }

    /// <summary>
    ///     Test that a ballot for a slot that already has one replaces it and the ballots stay in slot order.
    /// </summary>
    [Fact]
    public void BallotEdits_WithBallot_ExistingSlot_ReplacesTheBallotInSlotOrder()
    {
        // Arrange: a record with the ballots of slots 1 and 2, and a new ballot for slot 1
        var first = Submitted(1, [Answer("i1", ChecklistOutcome.Pass)]);
        var second = Submitted(2, [Answer("i1", ChecklistOutcome.Pass)]);
        var record = RecordOf(first, second);
        var replacement = Submitted(1, [Answer("i1", ChecklistOutcome.Fail)]);

        // Act: put the new ballot in the record
        var edited = BallotEdits.WithBallot(record, replacement);

        // Assert: slot 1 holds the new ballot, slot 2 is untouched, and the input record is unchanged
        Assert.Multiple(
            () => Assert.Equal([1, 2], edited.Ballots.Select(b => b.SlotId)),
            () => Assert.Same(replacement, edited.Ballots[0]),
            () => Assert.Same(second, edited.Ballots[1]),
            () => Assert.Equal(2, record.Ballots.Count),
            () => Assert.Same(first, record.Ballots[0]));
    }

    /// <summary>
    ///     Test that a ballot for a slot without one is added in slot order.
    /// </summary>
    [Fact]
    public void BallotEdits_WithBallot_NewSlot_AddsTheBallotInSlotOrder()
    {
        // Arrange: a record with the ballots of slots 1 and 3, and a new ballot for slot 2
        var record = RecordOf(Submitted(1, []), Submitted(3, []));
        var added = Submitted(2, []);

        // Act: put the new ballot in the record
        var edited = BallotEdits.WithBallot(record, added);

        // Assert: the ballots are in slot order, and the input record is unchanged
        Assert.Multiple(
            () => Assert.Equal([1, 2, 3], edited.Ballots.Select(b => b.SlotId)),
            () => Assert.Same(added, edited.Ballots[1]),
            () => Assert.Equal([1, 3], record.Ballots.Select(b => b.SlotId)));
    }

    /// <summary>
    ///     Test that removing the ballot of a slot leaves the other ballots.
    /// </summary>
    [Fact]
    public void BallotEdits_WithoutBallot_RemovesOnlyThatSlot()
    {
        // Arrange: a record with the ballots of slots 1 and 2
        var record = RecordOf(Submitted(1, []), Submitted(2, []));

        // Act: remove the ballot of slot 1 and of a slot that has none
        var edited = BallotEdits.WithoutBallot(record, 1);
        var unchanged = BallotEdits.WithoutBallot(record, 9);

        // Assert: only slot 1 is gone, nothing is removed for an unknown slot, and the input record is unchanged
        Assert.Multiple(
            () => Assert.Equal([2], edited.Ballots.Select(b => b.SlotId)),
            () => Assert.Equal([1, 2], unchanged.Ballots.Select(b => b.SlotId)),
            () => Assert.Equal([1, 2], record.Ballots.Select(b => b.SlotId)));
    }

    /// <summary>
    ///     Test that a stored answer replaces the earlier one for the same check, keeps the others, and makes the ballot
    ///     a draft.
    /// </summary>
    [Fact]
    public void BallotEdits_WithAnswer_ReplacesTheAnswerAndRevertsToDraft()
    {
        // Arrange: a submitted ballot that answers i2 and i1, and a new answer for i1
        var current = Submitted(4, [Answer("i2", ChecklistOutcome.Pass), Answer("i1", ChecklistOutcome.Pass)]);
        var answer = Answer("i1", ChecklistOutcome.Fail);

        // Act: store the answer
        var edited = BallotEdits.WithAnswer(current, 4, answer);

        // Assert: the ballot is a draft of the slot, i1 is the new answer, the answers are in check order, and the input
        // ballot is unchanged
        Assert.Multiple(
            () => Assert.Equal(BallotStatus.Draft, edited.Status),
            () => Assert.Null(edited.SubmittedAt),
            () => Assert.Null(edited.SubmittedByUsername),
            () => Assert.Equal(4, edited.SlotId),
            () => Assert.Equal(["i1", "i2"], edited.Answers.Select(a => a.ItemId)),
            () => Assert.Same(answer, edited.Answers[0]),
            () => Assert.Equal(BallotStatus.Submitted, current.Status),
            () => Assert.Equal(ChecklistOutcome.Pass, current.Answers.Single(a => a.ItemId == "i1").Outcome),
            () => Assert.Equal(["i2", "i1"], current.Answers.Select(a => a.ItemId)));
    }

    /// <summary>
    ///     Test that an answer for a slot without a ballot creates a draft that holds just that answer.
    /// </summary>
    [Fact]
    public void BallotEdits_WithAnswer_NoBallotYet_CreatesADraftWithTheAnswer()
    {
        // Arrange: no ballot yet, and an answer
        var answer = Answer("i1", ChecklistOutcome.Pass);

        // Act: store the answer
        var edited = BallotEdits.WithAnswer(null, 7, answer);

        // Assert: a draft of slot 7 with the one answer and no section state
        Assert.Multiple(
            () => Assert.Equal(7, edited.SlotId),
            () => Assert.Equal(BallotStatus.Draft, edited.Status),
            () => Assert.Same(answer, Assert.Single(edited.Answers)),
            () => Assert.Empty(edited.SectionStates));
    }

    /// <summary>
    ///     Test that a stored section state replaces the earlier one for the same section, keeps the answers, and makes
    ///     the ballot a draft.
    /// </summary>
    [Fact]
    public void BallotEdits_WithSectionState_ReplacesTheStateAndRevertsToDraft()
    {
        // Arrange: a submitted ballot with an answer and a state for s1, and a new state for s1
        var current = Submitted(2, [Answer("i1", ChecklistOutcome.Pass)], [State("s1", applicable: true)]);
        var state = State("s1", applicable: false);

        // Act: store the section state
        var edited = BallotEdits.WithSectionState(current, 2, state);
        var created = BallotEdits.WithSectionState(null, 3, state);

        // Assert: the ballot is a draft holding the new state and the old answers; with no ballot a draft is created; the
        // input ballot is unchanged
        Assert.Multiple(
            () => Assert.Equal(BallotStatus.Draft, edited.Status),
            () => Assert.Same(state, Assert.Single(edited.SectionStates)),
            () => Assert.Same(current.Answers, edited.Answers),
            () => Assert.Equal(BallotStatus.Draft, created.Status),
            () => Assert.Equal(3, created.SlotId),
            () => Assert.Empty(created.Answers),
            () => Assert.Same(state, Assert.Single(created.SectionStates)),
            () => Assert.Equal(BallotStatus.Submitted, current.Status),
            () => Assert.True(Assert.Single(current.SectionStates).Applicable));
    }

    /// <summary>
    ///     Test that reassigning a slot reverts the ballot to a draft, drops the rows the departing reviewer gave
    ///     (comparing user names ignoring case) and keeps the rows carried from an earlier review.
    /// </summary>
    [Fact]
    public void BallotEdits_Reassign_DepartingReviewer_DropsOwnRowsAndKeepsCarriedRows()
    {
        // Arrange: a submitted ballot with rows of the departing reviewer, of another reviewer, and carried rows that name
        // the departing reviewer
        var ballot = Submitted(
            1,
            [
                Answer("i1", ChecklistOutcome.Pass, "ANN"),
                Answer("i2", ChecklistOutcome.Pass, "bob"),
                Answer("i3", ChecklistOutcome.Pass, "ann", "rev-0")
            ],
            [
                State("s1", applicable: false, "Ann"),
                State("s2", applicable: false, "bob"),
                State("s3", applicable: false, "ann", "rev-0")
            ]);

        // Act: the reviewer "ann" leaves the slot
        var edited = BallotEdits.Reassign(ballot, ["ann"]);

        // Assert: a draft without the rows of ann, with the rows of bob and the carried rows, and the input ballot is
        // unchanged
        Assert.Multiple(
            () => Assert.Equal(BallotStatus.Draft, edited.Status),
            () => Assert.Null(edited.SubmittedAt),
            () => Assert.Null(edited.SubmittedByUsername),
            () => Assert.Equal(["i2", "i3"], edited.Answers.Select(a => a.ItemId)),
            () => Assert.Equal(["s2", "s3"], edited.SectionStates.Select(s => s.SectionId)),
            () => Assert.Equal(BallotStatus.Submitted, ballot.Status),
            () => Assert.Equal(3, ballot.Answers.Count),
            () => Assert.Equal(3, ballot.SectionStates.Count));
    }

    /// <summary>
    ///     Test that every function refuses missing arguments.
    /// </summary>
    [Fact]
    public void BallotEdits_NullArguments_Throw()
    {
        // Arrange: valid values to pair with a null
        var record = RecordOf();
        var ballot = Submitted(1, []);

        // Act and assert: each missing argument is refused
        Assert.Multiple(
            () => Assert.Throws<ArgumentNullException>(() => BallotEdits.WithBallot(null!, ballot)),
            () => Assert.Throws<ArgumentNullException>(() => BallotEdits.WithBallot(record, null!)),
            () => Assert.Throws<ArgumentNullException>(() => BallotEdits.WithoutBallot(null!, 1)),
            () => Assert.Throws<ArgumentNullException>(() => BallotEdits.WithAnswer(ballot, 1, null!)),
            () => Assert.Throws<ArgumentNullException>(() => BallotEdits.WithSectionState(ballot, 1, null!)),
            () => Assert.Throws<ArgumentNullException>(() => BallotEdits.Reassign(null!, ["ann"])),
            () => Assert.Throws<ArgumentNullException>(() => BallotEdits.Reassign(ballot, null!)));
    }
}
