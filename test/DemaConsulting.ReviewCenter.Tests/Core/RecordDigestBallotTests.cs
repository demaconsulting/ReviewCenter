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
///     Unit tests of the ballot part of <see cref="RecordDigest"/>: the digest covers the agreed standing of the
///     submitted ballots and nothing of the individual ballots.
/// </summary>
public class RecordDigestBallotTests
{
    /// <summary>
    ///     The instant of every row.
    /// </summary>
    private static readonly DateTimeOffset _then = new(2025, 3, 4, 5, 6, 7, TimeSpan.Zero);

    /// <summary>
    ///     The checklist of the record: section a with two checks and section b with one check.
    /// </summary>
    private static readonly ChecklistDefinition _definition = new(
        "v",
        [
            new ChecklistSection("a", "Alpha", string.Empty, [new ChecklistItem("a-01", "First"), new ChecklistItem("a-02", "Second")]),
            new ChecklistSection("b", "Beta", string.Empty, [new ChecklistItem("b-01", "Third")])
        ]);

    /// <summary>
    ///     Builds an answer.
    /// </summary>
    /// <param name="slotId">The slot.</param>
    /// <param name="item">The check id.</param>
    /// <param name="outcome">The outcome.</param>
    /// <param name="who">The user name of the answerer.</param>
    /// <param name="justification">The justification; a fixed text for anything but Pass when omitted.</param>
    /// <returns>The answer.</returns>
    private static ChecklistAnswer Answer(
        int slotId, string item, ChecklistOutcome outcome, string who = "bob", string? justification = null)
    {
        return new ChecklistAnswer
        {
            ProjectId = 1,
            ReviewId = "r",
            SlotId = slotId,
            ItemId = item,
            Outcome = outcome,
            Justification = justification ?? (outcome == ChecklistOutcome.Pass ? null : "Because"),
            AnsweredBy = who,
            AnsweredByUsername = who,
            AnsweredAt = _then
        };
    }

    /// <summary>
    ///     Builds a section state.
    /// </summary>
    /// <param name="slotId">The slot.</param>
    /// <param name="section">The section id.</param>
    /// <param name="reason">The reason for not applicable.</param>
    /// <returns>The state of a section that does not apply.</returns>
    private static ChecklistSectionState NotApplicable(int slotId, string section, string reason = "Not relevant")
    {
        return new ChecklistSectionState
        {
            ProjectId = 1,
            ReviewId = "r",
            SlotId = slotId,
            SectionId = section,
            Applicable = false,
            Reason = reason,
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
    ///     Builds a submitted ballot that answers every check Pass except the first, which has the given outcome.
    /// </summary>
    /// <param name="slotId">The slot.</param>
    /// <param name="first">The outcome of the first check.</param>
    /// <param name="who">The user name of the answerer.</param>
    /// <returns>The ballot.</returns>
    private static Ballot Full(int slotId, ChecklistOutcome first = ChecklistOutcome.Pass, string who = "bob")
    {
        return BallotOf(
            slotId,
            BallotStatus.Submitted,
            [Answer(slotId, "a-01", first, who), Answer(slotId, "a-02", ChecklistOutcome.Pass, who), Answer(slotId, "b-01", ChecklistOutcome.Pass, who)]);
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
    ///     Test that a check the submitted ballots agree on is one line with the outcome.
    /// </summary>
    [Fact]
    public void RecordDigest_Canonicalize_AgreedCheck_Line()
    {
        // Act
        var text = RecordDigest.Canonicalize(Record(Full(1), Full(2)));

        // Assert
        Assert.Contains("check 4:a-01 agreed 4:Pass", text, StringComparison.Ordinal);
        Assert.Contains("outcome approved", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a check the submitted ballots disagree on is one line that says so, and the outcome is none.
    /// </summary>
    [Fact]
    public void RecordDigest_Canonicalize_Disputed_Line()
    {
        // Act
        var text = RecordDigest.Canonicalize(Record(Full(1), Full(2, ChecklistOutcome.Fail)));

        // Assert
        Assert.Contains("check 4:a-01 disputed", text, StringComparison.Ordinal);
        Assert.Contains("check 4:a-02 agreed 4:Pass", text, StringComparison.Ordinal);
        Assert.Contains("outcome none", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a check nobody has answered in a submitted ballot is pending.
    /// </summary>
    [Fact]
    public void RecordDigest_Canonicalize_Pending_Line()
    {
        // Arrange: a submitted ballot with one answer only
        var ballot = BallotOf(1, BallotStatus.Submitted, [Answer(1, "a-01", ChecklistOutcome.Pass)]);

        // Act
        var text = RecordDigest.Canonicalize(Record(ballot));

        // Assert
        Assert.Contains("check 4:a-02 pending", text, StringComparison.Ordinal);
        Assert.Contains("outcome none", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a section all submitted ballots mark as not applicable has a line, and its checks have none.
    /// </summary>
    [Fact]
    public void RecordDigest_Canonicalize_AgreedNaSection_Line()
    {
        // Arrange: both slots mark section b as not applicable and answer section a
        var first = BallotOf(1, BallotStatus.Submitted, Full(1).Answers.Where(a => a.ItemId != "b-01").ToArray(), [NotApplicable(1, "b")]);
        var second = BallotOf(2, BallotStatus.Submitted, Full(2).Answers.Where(a => a.ItemId != "b-01").ToArray(), [NotApplicable(2, "b", "Other words")]);

        // Act
        var text = RecordDigest.Canonicalize(Record(first, second));

        // Assert
        Assert.Contains("applicability 1:b not-applicable", text, StringComparison.Ordinal);
        Assert.DoesNotContain("check 4:b-01", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a section only some submitted ballots mark as not applicable is disputed.
    /// </summary>
    [Fact]
    public void RecordDigest_Canonicalize_DisputedSection_Line()
    {
        // Arrange
        var first = BallotOf(1, BallotStatus.Submitted, Full(1).Answers.Where(a => a.ItemId != "b-01").ToArray(), [NotApplicable(1, "b")]);

        // Act
        var text = RecordDigest.Canonicalize(Record(first, Full(2)));

        // Assert
        Assert.Contains("applicability 1:b disputed", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test the three outcome lines: none without a decision, approved, and rejected.
    /// </summary>
    [Fact]
    public void RecordDigest_Canonicalize_OutcomeLine_NoneApprovedRejected()
    {
        // Act
        var none = RecordDigest.Canonicalize(Record());
        var approved = RecordDigest.Canonicalize(Record(Full(1)));
        var rejected = RecordDigest.Canonicalize(Record(Full(1, ChecklistOutcome.Fail)));

        // Assert
        Assert.Contains("outcome none", none, StringComparison.Ordinal);
        Assert.Contains("outcome approved", approved, StringComparison.Ordinal);
        Assert.Contains("outcome rejected", rejected, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a draft ballot, however different, does not change the digest.
    /// </summary>
    [Fact]
    public void RecordDigest_Compute_DraftBallot_DoesNotChangeDigest()
    {
        // Arrange
        var without = Record(Full(1));
        var draft = BallotOf(2, BallotStatus.Draft, [Answer(2, "a-01", ChecklistOutcome.Fail)], [NotApplicable(2, "b")]);

        // Act and assert
        Assert.Equal(RecordDigest.Compute(without), RecordDigest.Compute(Record(Full(1), draft)));
    }

    /// <summary>
    ///     Test that the answerer, the justification and the reason are not covered.
    /// </summary>
    [Fact]
    public void RecordDigest_Compute_JustificationAndAnswerer_NotCovered()
    {
        // Arrange: the same standing (a Fail with another justification, from another person)
        var one = Record(Full(1, ChecklistOutcome.Fail, "bob"));
        var other = Record(BallotOf(
            1,
            BallotStatus.Submitted,
            [Answer(1, "a-01", ChecklistOutcome.Fail, "cyd", "Quite different words"), Answer(1, "a-02", ChecklistOutcome.Pass, "cyd"), Answer(1, "b-01", ChecklistOutcome.Pass, "cyd")]));

        // Act and assert
        Assert.Equal(RecordDigest.Compute(one), RecordDigest.Compute(other));
    }

    /// <summary>
    ///     Test that the order and the identities of the slots are not covered.
    /// </summary>
    [Fact]
    public void RecordDigest_Compute_SlotOrderAndIds_NotCovered()
    {
        // Act and assert
        Assert.Equal(
            RecordDigest.Compute(Record(Full(1), Full(2))),
            RecordDigest.Compute(Record(Full(9), Full(4))));
    }

    /// <summary>
    ///     Test that a second ballot that agrees leaves the digest as it is.
    /// </summary>
    [Fact]
    public void RecordDigest_Compute_AgreeingSecondSubmit_SameDigest()
    {
        // Act and assert
        Assert.Equal(RecordDigest.Compute(Record(Full(1))), RecordDigest.Compute(Record(Full(1), Full(2))));
    }

    /// <summary>
    ///     Test that the first ballot that disagrees changes the digest.
    /// </summary>
    [Fact]
    public void RecordDigest_Compute_FirstDisagreement_ChangesDigest()
    {
        // Act and assert
        Assert.NotEqual(
            RecordDigest.Compute(Record(Full(1), Full(2))),
            RecordDigest.Compute(Record(Full(1), Full(2, ChecklistOutcome.NotApplicable))));
    }

    /// <summary>
    ///     Test that the check lines are sorted by id whatever the order of the answers.
    /// </summary>
    [Fact]
    public void RecordDigest_Canonicalize_ChecksSortedOrdinal()
    {
        // Arrange: answers in reverse order
        var ballot = BallotOf(
            1,
            BallotStatus.Submitted,
            [Answer(1, "b-01", ChecklistOutcome.Pass), Answer(1, "a-02", ChecklistOutcome.Pass), Answer(1, "a-01", ChecklistOutcome.Pass)]);

        // Act
        var text = RecordDigest.Canonicalize(Record(ballot));

        // Assert
        Assert.True(text.IndexOf("check 4:a-01", StringComparison.Ordinal) < text.IndexOf("check 4:a-02", StringComparison.Ordinal));
        Assert.True(text.IndexOf("check 4:a-02", StringComparison.Ordinal) < text.IndexOf("check 4:b-01", StringComparison.Ordinal));
    }

    /// <summary>
    ///     Test that a carried marker on an answer is not covered, so a carried and a fresh answer give one digest.
    /// </summary>
    [Fact]
    public void RecordDigest_Compute_CarriedMarker_NotCovered()
    {
        // Arrange: the same answers, each marked as carried from an earlier review
        var fresh = Full(1);
        var carried = fresh with
        {
            Answers = fresh.Answers.Select(a => new ChecklistAnswer
            {
                ProjectId = a.ProjectId,
                ReviewId = a.ReviewId,
                SlotId = a.SlotId,
                ItemId = a.ItemId,
                Outcome = a.Outcome,
                Justification = a.Justification,
                AnsweredBy = a.AnsweredBy,
                AnsweredByUsername = a.AnsweredByUsername,
                AnsweredAt = a.AnsweredAt,
                CarriedFromReviewId = "old"
            }).ToArray()
        };

        // Act and assert
        Assert.Equal(RecordDigest.Compute(Record(fresh)), RecordDigest.Compute(Record(carried)));
    }
}
