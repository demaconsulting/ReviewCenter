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
///     Unit tests of <see cref="FollowUpCarry"/> for a source review with several ballots: only what the submitted
///     ballots agree on is carried, with the row of the lowest slot that gave it.
/// </summary>
public class FollowUpCarryConsolidatedTests
{
    /// <summary>
    ///     The instant at which the earlier review's rows were written.
    /// </summary>
    private static readonly DateTimeOffset _then = new(2025, 3, 4, 5, 6, 7, TimeSpan.Zero);

    /// <summary>
    ///     The instant at which the new review is created.
    /// </summary>
    private static readonly DateTimeOffset _now = new(2025, 4, 5, 6, 7, 8, TimeSpan.Zero);

    /// <summary>
    ///     The checklist of both reviews: one section with two checks.
    /// </summary>
    private static readonly ChecklistDefinition _definition = new(
        "v",
        [new ChecklistSection("a", "Alpha", string.Empty, [new ChecklistItem("a-01", "First"), new ChecklistItem("a-02", "Second")])]);

    /// <summary>
    ///     Builds an answer of a slot of the earlier review.
    /// </summary>
    /// <param name="slotId">The slot.</param>
    /// <param name="item">The check id.</param>
    /// <param name="outcome">The outcome.</param>
    /// <param name="who">The user name of the answerer.</param>
    /// <returns>The answer.</returns>
    private static ChecklistAnswer Answer(int slotId, string item, ChecklistOutcome outcome, string who)
    {
        return new ChecklistAnswer
        {
            ProjectId = 1,
            ReviewId = "old",
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
    ///     Builds a ballot of the earlier review.
    /// </summary>
    /// <param name="slotId">The slot.</param>
    /// <param name="status">The status.</param>
    /// <param name="answers">The answers.</param>
    /// <returns>The ballot.</returns>
    private static Ballot BallotOf(int slotId, BallotStatus status, params ChecklistAnswer[] answers)
    {
        return new Ballot(slotId, status, status == BallotStatus.Submitted ? _then : null, status == BallotStatus.Submitted ? "x" : null, answers, []);
    }

    /// <summary>
    ///     Computes what is carried from the given ballots into a review of the same file.
    /// </summary>
    /// <param name="ballots">The ballots of the earlier review.</param>
    /// <returns>The carry-over.</returns>
    private static ReviewCarryOver Compute(params Ballot[] ballots)
    {
        var checklist = new ReviewChecklist { ProjectId = 1, ReviewId = "old", Version = "v", Definition = _definition.ToJson() };
        var record = new ReviewRecord("fp", checklist, ballots, [], ReviewSource.Upload);
        ReviewFile[] files = [new ReviewFile { ProjectId = 1, ReviewId = "x", Path = "a.txt", Sha256 = "h1", BaselineSha256 = "b1" }];
        return FollowUpCarry.Compute("old", record, files, 1, "new", files, _definition, _now);
    }

    /// <summary>
    ///     Test that a check both submitted ballots answered Pass is carried once, with the row of the lowest slot.
    /// </summary>
    [Fact]
    public void FollowUpCarry_Compute_ConsolidatedSource_CarriesAgreedPassOnly()
    {
        // Arrange: two submitted ballots that agree on the first check; the second check is Pass in one only
        var first = BallotOf(1, BallotStatus.Submitted, Answer(1, "a-01", ChecklistOutcome.Pass, "bob"), Answer(1, "a-02", ChecklistOutcome.Pass, "bob"));
        var second = BallotOf(2, BallotStatus.Submitted, Answer(2, "a-01", ChecklistOutcome.Pass, "cyd"));

        // Act
        var carry = Compute(first, second);

        // Assert: only the agreed check is carried, with the row of slot 1
        var carried = Assert.Single(carry.Answers);
        Assert.Equal("a-01", carried.ItemId);
        Assert.Equal("bob", carried.AnsweredByUsername);
    }

    /// <summary>
    ///     Test that a check the submitted ballots dispute is not carried.
    /// </summary>
    [Fact]
    public void FollowUpCarry_Compute_DisputedNotCarried()
    {
        // Arrange: Pass against not applicable, and Pass against Fail
        var first = BallotOf(
            1, BallotStatus.Submitted, Answer(1, "a-01", ChecklistOutcome.Pass, "bob"), Answer(1, "a-02", ChecklistOutcome.Pass, "bob"));
        var second = BallotOf(
            2, BallotStatus.Submitted, Answer(2, "a-01", ChecklistOutcome.NotApplicable, "cyd"), Answer(2, "a-02", ChecklistOutcome.Fail, "cyd"));

        // Act
        var carry = Compute(first, second);

        // Assert
        Assert.Empty(carry.Answers);
    }

    /// <summary>
    ///     Test that the answers of a draft ballot are not carried, and do not stand in the way of the agreed checks.
    /// </summary>
    [Fact]
    public void FollowUpCarry_Compute_DraftBallot_IsIgnored()
    {
        // Arrange: a submitted ballot, and a draft that answers the same check differently and another check as Pass
        var submitted = BallotOf(1, BallotStatus.Submitted, Answer(1, "a-01", ChecklistOutcome.Pass, "bob"));
        var draft = BallotOf(
            2, BallotStatus.Draft, Answer(2, "a-01", ChecklistOutcome.Fail, "cyd"), Answer(2, "a-02", ChecklistOutcome.Pass, "cyd"));

        // Act
        var carry = Compute(submitted, draft);

        // Assert: only the submitted ballot counts, so its Pass is agreed
        Assert.Equal(["a-01"], carry.Answers.Select(a => a.ItemId));
    }

    /// <summary>
    ///     Test that a source whose only ballot is a draft carries nothing.
    /// </summary>
    [Fact]
    public void FollowUpCarry_Compute_OnlyDraftBallots_CarriesNothing()
    {
        // Arrange
        var draft = BallotOf(1, BallotStatus.Draft, Answer(1, "a-01", ChecklistOutcome.Pass, "bob"));

        // Act
        var carry = Compute(draft);

        // Assert
        Assert.Empty(carry.Answers);
        Assert.Empty(carry.SectionStates);
    }
}
