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
///     Unit tests of <see cref="BallotProjection"/>, which turns the ballots of a record into the consolidated
///     standing and into the checklist state of one ballot.
/// </summary>
public class BallotProjectionTests
{
    /// <summary>
    ///     The instant of every row.
    /// </summary>
    private static readonly DateTimeOffset _then = new(2025, 3, 4, 5, 6, 7, TimeSpan.Zero);

    /// <summary>
    ///     The checklist of the record: two sections with two checks and one check.
    /// </summary>
    private static readonly ChecklistDefinition _definition = new(
        "v",
        [
            new ChecklistSection("a", "Alpha", string.Empty, [new ChecklistItem("a-01", "First"), new ChecklistItem("a-02", "Second")]),
            new ChecklistSection("b", "Beta", string.Empty, [new ChecklistItem("b-01", "Third")])
        ]);

    /// <summary>
    ///     Builds an answer of a slot.
    /// </summary>
    /// <param name="slotId">The slot.</param>
    /// <param name="item">The check id.</param>
    /// <param name="outcome">The outcome.</param>
    /// <param name="who">The user name of the answerer.</param>
    /// <returns>The answer.</returns>
    private static ChecklistAnswer Answer(int slotId, string item, ChecklistOutcome outcome, string who = "bob")
    {
        return new ChecklistAnswer
        {
            ProjectId = 1,
            ReviewId = "r",
            SlotId = slotId,
            ItemId = item,
            Outcome = outcome,
            Justification = outcome == ChecklistOutcome.Pass ? null : "Because",
            AnsweredBy = who,
            AnsweredByUsername = who,
            AnsweredAt = _then
        };
    }

    /// <summary>
    ///     Builds the state of a section of a slot.
    /// </summary>
    /// <param name="slotId">The slot.</param>
    /// <param name="section">The section id.</param>
    /// <param name="applicable">Whether the section applies.</param>
    /// <returns>The section state.</returns>
    private static ChecklistSectionState Section(int slotId, string section, bool applicable)
    {
        return new ChecklistSectionState
        {
            ProjectId = 1,
            ReviewId = "r",
            SlotId = slotId,
            SectionId = section,
            Applicable = applicable,
            Reason = applicable ? null : "Not relevant",
            SetBy = "Bob",
            SetByUsername = "bob",
            SetAt = _then
        };
    }

    /// <summary>
    ///     Builds a ballot.
    /// </summary>
    /// <param name="slotId">The slot.</param>
    /// <param name="status">The status.</param>
    /// <param name="answers">The answers.</param>
    /// <param name="sections">The section states.</param>
    /// <returns>The ballot.</returns>
    private static Ballot BallotOf(
        int slotId, BallotStatus status, ChecklistAnswer[] answers, ChecklistSectionState[]? sections = null)
    {
        return new Ballot(slotId, status, status == BallotStatus.Submitted ? _then : null, status == BallotStatus.Submitted ? "x" : null, answers, sections ?? []);
    }

    /// <summary>
    ///     Builds a record with the checklist and the given ballots.
    /// </summary>
    /// <param name="ballots">The ballots.</param>
    /// <returns>The record.</returns>
    private static ReviewRecord Record(params Ballot[] ballots)
    {
        var checklist = new ReviewChecklist { ProjectId = 1, ReviewId = "r", Version = "v", Definition = _definition.ToJson() };
        return new ReviewRecord("fp", checklist, ballots, [], ReviewSource.Upload);
    }

    /// <summary>
    ///     Test that a review without a checklist has no consolidated view.
    /// </summary>
    [Fact]
    public void BallotProjection_Consolidate_NoChecklist_ReturnsNull()
    {
        // Arrange
        var record = new ReviewRecord("fp", null, [], [], ReviewSource.Upload);

        // Act and assert
        Assert.Null(BallotProjection.Consolidate(record));
    }

    /// <summary>
    ///     Test that a missing record is refused.
    /// </summary>
    [Fact]
    public void BallotProjection_NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => BallotProjection.Consolidate(null!));
        Assert.Throws<ArgumentNullException>(() => BallotProjection.ForBallot(null!, 1, null));
    }

    /// <summary>
    ///     Test that an agreed check is answered with the row of the lowest slot, and a disputed or pending check has no
    ///     answer.
    /// </summary>
    [Fact]
    public void BallotProjection_Consolidate_RepresentativeRow_IsTheLowestSlot()
    {
        // Arrange: the slots agree on a-01, disagree on a-02, and nobody answered b-01
        var first = BallotOf(1, BallotStatus.Submitted, [Answer(1, "a-01", ChecklistOutcome.Pass, "bob"), Answer(1, "a-02", ChecklistOutcome.Pass)]);
        var second = BallotOf(2, BallotStatus.Submitted, [Answer(2, "a-01", ChecklistOutcome.Pass, "cyd"), Answer(2, "a-02", ChecklistOutcome.Fail, "cyd")]);

        // Act
        var view = BallotProjection.Consolidate(Record(second, first))!;

        // Assert
        var agreed = view.State.AnswerFor("a-01")!;
        Assert.Equal("bob", agreed.AnsweredByUsername);
        Assert.Equal(1, agreed.SlotId);
        Assert.Null(view.State.AnswerFor("a-02"));
        Assert.Equal(CheckConsensus.Disputed, view.State.ConsolidationFor("a-02")!.Consensus);
        Assert.Null(view.State.AnswerFor("b-01"));
        Assert.Equal(CheckConsensus.Pending, view.State.ConsolidationFor("b-01")!.Consensus);
        Assert.Same(view.Result, view.State.Consolidation);
        Assert.False(view.State.IsOwnBallot);
    }

    /// <summary>
    ///     Test that a draft ballot takes no part, however far along it is.
    /// </summary>
    [Fact]
    public void BallotProjection_Consolidate_DraftBallots_AreIgnored()
    {
        // Arrange: one submitted ballot and one draft that answers more
        var submitted = BallotOf(1, BallotStatus.Submitted, [Answer(1, "a-01", ChecklistOutcome.Pass)]);
        var draft = BallotOf(2, BallotStatus.Draft, [Answer(2, "a-01", ChecklistOutcome.Fail), Answer(2, "a-02", ChecklistOutcome.Pass)]);

        // Act
        var view = BallotProjection.Consolidate(Record(submitted, draft), slotCount: 2)!;

        // Assert
        Assert.Equal(ChecklistOutcome.Pass, view.State.AnswerFor("a-01")!.Outcome);
        Assert.Null(view.State.AnswerFor("a-02"));
        Assert.Equal(1, view.Result.BallotsSubmitted);
    }

    /// <summary>
    ///     Test that the sole draft ballot of a single-slot review counts when the slot count is given, and does not
    ///     count for the digest view.
    /// </summary>
    [Fact]
    public void BallotProjection_Consolidate_SingleSlotDraft_CountsOnlyWithTheSlotCount()
    {
        // Arrange
        var record = Record(BallotOf(1, BallotStatus.Draft, [Answer(1, "a-01", ChecklistOutcome.Pass)]));

        // Act
        var forProgress = BallotProjection.Consolidate(record, slotCount: 1)!;
        var forDigest = BallotProjection.Consolidate(record)!;
        var twoSlots = BallotProjection.Consolidate(record, slotCount: 2)!;

        // Assert
        Assert.NotNull(forProgress.State.AnswerFor("a-01"));
        Assert.Null(forDigest.State.AnswerFor("a-01"));
        Assert.Null(twoSlots.State.AnswerFor("a-01"));
    }

    /// <summary>
    ///     Test that a section is not applicable only when the submitted ballots agree, and is disputed otherwise.
    /// </summary>
    [Fact]
    public void BallotProjection_Consolidate_Sections_AgreedNotApplicableAndDisputed()
    {
        // Arrange: both slots mark section a as not applicable; only the first marks section b
        var first = BallotOf(1, BallotStatus.Submitted, [], [Section(1, "a", false), Section(1, "b", false)]);
        var second = BallotOf(2, BallotStatus.Submitted, [], [Section(2, "a", false)]);

        // Act
        var state = BallotProjection.Consolidate(Record(first, second), authorBlocked: true, noSlot: true)!.State;

        // Assert
        Assert.False(state.IsApplicable("a"));
        Assert.Equal(1, state.SectionStateFor("a")!.SlotId);
        Assert.True(state.IsSectionDisputed("b"));
        Assert.True(state.AuthorBlocked);
        Assert.True(state.NoSlot);
    }

    /// <summary>
    ///     Test that the state of one ballot shows its own rows, drafts included, and is marked as the viewer's own.
    /// </summary>
    [Fact]
    public void BallotProjection_ForBallot_ShowsTheOwnDraft()
    {
        // Arrange
        var ballot = BallotOf(3, BallotStatus.Draft, [Answer(3, "a-01", ChecklistOutcome.Fail)], [Section(3, "b", false)]);

        // Act
        var state = BallotProjection.ForBallot(_definition, 3, ballot);

        // Assert
        Assert.True(state.IsOwnBallot);
        Assert.Equal(3, state.SlotId);
        Assert.Equal(BallotState.Draft, state.BallotState);
        Assert.Equal(ChecklistOutcome.Fail, state.AnswerFor("a-01")!.Outcome);
        Assert.False(state.IsApplicable("b"));
    }

    /// <summary>
    ///     Test that a slot without a ballot gets an empty state that has not been started.
    /// </summary>
    [Fact]
    public void BallotProjection_ForBallot_NoBallot_IsNotStarted()
    {
        // Act
        var state = BallotProjection.ForBallot(_definition, 5, null, authorBlocked: true);

        // Assert
        Assert.Equal(BallotState.NotStarted, state.BallotState);
        Assert.True(state.IsOwnBallot);
        Assert.True(state.AuthorBlocked);
        Assert.Null(state.AnswerFor("a-01"));
    }
}
