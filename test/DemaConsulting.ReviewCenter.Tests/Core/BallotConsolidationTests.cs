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
///     Unit tests for <see cref="BallotConsolidation"/> and <see cref="Ballot"/>: the consolidation of submitted
///     ballots into the agreed standing of every check and section, the outcome it leads to, the counts, the
///     completeness of a ballot, and the refusal of null arguments. The tests are pure and use no store.
/// </summary>
public class BallotConsolidationTests
{
    /// <summary>
    ///     The checklist used by most tests: section <c>s1</c> with checks <c>i1</c> and <c>i2</c>, and section
    ///     <c>s2</c> with check <c>i3</c>.
    /// </summary>
    private static readonly ChecklistDefinition _definition = new(
        "1",
        [
            new ChecklistSection("s1", "Basics", "Skip when nothing is built.", [new ChecklistItem("i1", "Clear?"), new ChecklistItem("i2", "Tested?")]),
            new ChecklistSection("s2", "Docs", "Skip when nothing is documented.", [new ChecklistItem("i3", "Updated?")])
        ]);

    /// <summary>
    ///     Creates an answer.
    /// </summary>
    /// <param name="itemId">The check that is answered.</param>
    /// <param name="outcome">The outcome.</param>
    /// <param name="justification">The justification, if any.</param>
    /// <returns>The answer.</returns>
    private static ChecklistAnswer Answer(string itemId, ChecklistOutcome outcome, string? justification = null)
    {
        return new ChecklistAnswer { ItemId = itemId, Outcome = outcome, Justification = justification };
    }

    /// <summary>
    ///     Creates the state of a section that a ballot marks as not applicable.
    /// </summary>
    /// <param name="sectionId">The section.</param>
    /// <returns>The state.</returns>
    private static ChecklistSectionState NotApplicable(string sectionId)
    {
        return new ChecklistSectionState { SectionId = sectionId, Applicable = false, Reason = "Nothing to review." };
    }

    /// <summary>
    ///     Creates a ballot.
    /// </summary>
    /// <param name="slotId">The slot of the ballot.</param>
    /// <param name="status">The status of the ballot.</param>
    /// <param name="answers">The answers of the ballot.</param>
    /// <param name="sectionStates">The section states of the ballot.</param>
    /// <returns>The ballot.</returns>
    private static Ballot Make(int slotId, BallotStatus status, ChecklistAnswer[] answers, params ChecklistSectionState[] sectionStates)
    {
        var submitted = status == BallotStatus.Submitted;
        return new Ballot(
            slotId,
            status,
            submitted ? new DateTimeOffset(2026, 1, 5, 10, 0, 0, TimeSpan.Zero) : null,
            submitted ? "user" + slotId : null,
            answers,
            sectionStates);
    }

    /// <summary>
    ///     Creates a ballot that answers the three checks of the standard checklist.
    /// </summary>
    /// <param name="slotId">The slot of the ballot.</param>
    /// <param name="status">The status of the ballot.</param>
    /// <param name="outcome1">The outcome of <c>i1</c>.</param>
    /// <param name="outcome2">The outcome of <c>i2</c>.</param>
    /// <param name="outcome3">The outcome of <c>i3</c>.</param>
    /// <returns>The ballot.</returns>
    private static Ballot Full(
        int slotId,
        BallotStatus status,
        ChecklistOutcome outcome1 = ChecklistOutcome.Pass,
        ChecklistOutcome outcome2 = ChecklistOutcome.Pass,
        ChecklistOutcome outcome3 = ChecklistOutcome.Pass)
    {
        return Make(
            slotId,
            status,
            [Answer("i1", outcome1, Note(outcome1)), Answer("i2", outcome2, Note(outcome2)), Answer("i3", outcome3, Note(outcome3))]);
    }

    /// <summary>
    ///     Gives the justification that the rules require for an outcome.
    /// </summary>
    /// <param name="outcome">The outcome.</param>
    /// <returns>A justification for a fail or not-applicable outcome; otherwise <c>null</c>.</returns>
    private static string? Note(ChecklistOutcome outcome)
    {
        return outcome == ChecklistOutcome.Pass ? null : "Because.";
    }

    /// <summary>
    ///     Finds the consolidation of one check.
    /// </summary>
    /// <param name="result">The result.</param>
    /// <param name="itemId">The check.</param>
    /// <returns>The check consolidation.</returns>
    private static CheckConsolidation CheckOf(BallotConsolidationResult result, string itemId)
    {
        return result.Checks.Single(check => check.ItemId == itemId);
    }

    /// <summary>
    ///     Test that with no submitted ballot every check is pending and the outcome is null, whether the only ballot is
    ///     a draft or there is no ballot.
    /// </summary>
    [Fact]
    public void BallotConsolidation_Consolidate_NoSubmittedBallots_AllPending_OutcomeNull()
    {
        // Arrange: one draft ballot that answers everything, and no ballot at all
        var withDraft = new[] { Full(1, BallotStatus.Draft) };

        // Act: consolidate both
        var draftOnly = BallotConsolidation.Consolidate(_definition, withDraft);
        var none = BallotConsolidation.Consolidate(_definition, []);

        // Assert: nothing is agreed, three checks wait, and there is no outcome
        Assert.Multiple(
            () => Assert.All(draftOnly.Checks, check => Assert.Equal(CheckConsensus.Pending, check.Consensus)),
            () => Assert.Equal(3, draftOnly.Pending),
            () => Assert.Null(draftOnly.Outcome),
            () => Assert.All(draftOnly.Sections, section => Assert.Equal(SectionConsensus.Applicable, section.Consensus)),
            () => Assert.Equal(3, none.Checks.Count),
            () => Assert.All(none.Checks, check => Assert.Equal(CheckConsensus.Pending, check.Consensus)),
            () => Assert.Null(none.Outcome));
    }

    /// <summary>
    ///     Test that a draft ballot does not count even when a submitted one exists: the submitted one alone decides,
    ///     whatever the draft answers.
    /// </summary>
    [Fact]
    public void BallotConsolidation_Consolidate_OneSubmittedOneDraft_OnlySubmittedCounts()
    {
        // Arrange: slot 1 submitted everything as pass; slot 2 is a draft that fails everything
        var ballots = new[]
        {
            Full(1, BallotStatus.Submitted),
            Full(2, BallotStatus.Draft, ChecklistOutcome.Fail, ChecklistOutcome.Fail, ChecklistOutcome.Fail)
        };

        // Act
        var result = BallotConsolidation.Consolidate(_definition, ballots);

        // Assert: the agreed value is the submitted one, already final, and the draft is not shown in the outcomes
        Assert.Multiple(
            () => Assert.All(result.Checks, check => Assert.Equal(CheckConsensus.Agreed, check.Consensus)),
            () => Assert.All(result.Checks, check => Assert.Equal(ChecklistOutcome.Pass, check.Outcome)),
            () => Assert.All(result.Checks, check => Assert.Equal(new[] { 1 }, check.Outcomes.Keys)),
            () => Assert.Equal(ReviewOutcome.Approved, result.Outcome));
    }

    /// <summary>
    ///     Test that submitted ballots with the same outcome agree, and that the outcome of each slot is listed.
    /// </summary>
    [Fact]
    public void BallotConsolidation_Consolidate_SubmittedEqual_Agreed()
    {
        // Arrange: two submitted ballots that both pass everything
        var ballots = new[] { Full(1, BallotStatus.Submitted), Full(2, BallotStatus.Submitted) };

        // Act
        var result = BallotConsolidation.Consolidate(_definition, ballots);

        // Assert: i1 is agreed as a pass and lists both slots
        var check = CheckOf(result, "i1");
        Assert.Multiple(
            () => Assert.Equal(CheckConsensus.Agreed, check.Consensus),
            () => Assert.Equal(ChecklistOutcome.Pass, check.Outcome),
            () => Assert.Equal("s1", check.SectionId),
            () => Assert.Equal(ChecklistOutcome.Pass, check.Outcomes[1]),
            () => Assert.Equal(ChecklistOutcome.Pass, check.Outcomes[2]));
    }

    /// <summary>
    ///     Test that different outcomes dispute a check, and that the check has no agreed outcome but still lists
    ///     what each slot answered.
    /// </summary>
    [Fact]
    public void BallotConsolidation_Consolidate_DifferentOutcomes_Disputed()
    {
        // Arrange: slot 1 passes i2, slot 2 fails it
        var ballots = new[]
        {
            Full(1, BallotStatus.Submitted),
            Full(2, BallotStatus.Submitted, outcome2: ChecklistOutcome.Fail)
        };

        // Act
        var result = BallotConsolidation.Consolidate(_definition, ballots);

        // Assert: i2 is disputed with both answers listed and no agreed outcome; i1 is still agreed
        var check = CheckOf(result, "i2");
        Assert.Multiple(
            () => Assert.Equal(CheckConsensus.Disputed, check.Consensus),
            () => Assert.Null(check.Outcome),
            () => Assert.Equal(ChecklistOutcome.Pass, check.Outcomes[1]),
            () => Assert.Equal(ChecklistOutcome.Fail, check.Outcomes[2]),
            () => Assert.Equal(CheckConsensus.Agreed, CheckOf(result, "i1").Consensus));
    }

    /// <summary>
    ///     Test that different justifications for the same outcome do not create a dispute.
    /// </summary>
    [Fact]
    public void BallotConsolidation_Consolidate_NotesDiffer_StillAgreed()
    {
        // Arrange: both fail i2 but with different words, and one passes with a note while the other has none
        var ballots = new[]
        {
            Make(1, BallotStatus.Submitted, [Answer("i1", ChecklistOutcome.Pass, "Looks fine."), Answer("i2", ChecklistOutcome.Fail, "No tests."), Answer("i3", ChecklistOutcome.Pass)]),
            Make(2, BallotStatus.Submitted, [Answer("i1", ChecklistOutcome.Pass), Answer("i2", ChecklistOutcome.Fail, "Coverage is missing."), Answer("i3", ChecklistOutcome.Pass)])
        };

        // Act
        var result = BallotConsolidation.Consolidate(_definition, ballots);

        // Assert: both checks are agreed
        Assert.Multiple(
            () => Assert.Equal(CheckConsensus.Agreed, CheckOf(result, "i1").Consensus),
            () => Assert.Equal(CheckConsensus.Agreed, CheckOf(result, "i2").Consensus),
            () => Assert.Equal(ChecklistOutcome.Fail, CheckOf(result, "i2").Outcome));
    }

    /// <summary>
    ///     Test that the answers and section states of a draft are ignored completely.
    /// </summary>
    [Fact]
    public void BallotConsolidation_Consolidate_DraftAnswers_Ignored()
    {
        // Arrange: slot 1 submitted i1 as pass; slot 2 is a draft that marks i1 as a fail and a section as not applicable
        var ballots = new[]
        {
            Make(1, BallotStatus.Submitted, [Answer("i1", ChecklistOutcome.Pass), Answer("i2", ChecklistOutcome.Pass), Answer("i3", ChecklistOutcome.Pass)]),
            Make(2, BallotStatus.Draft, [Answer("i1", ChecklistOutcome.Fail, "No.")], NotApplicable("s2"))
        };

        // Act
        var result = BallotConsolidation.Consolidate(_definition, ballots);

        // Assert: the submitted answer decides, and the section is not disputed by the draft
        Assert.Multiple(
            () => Assert.Equal(ChecklistOutcome.Pass, CheckOf(result, "i1").Outcome),
            () => Assert.Equal(SectionConsensus.Applicable, result.Sections.Single(section => section.SectionId == "s2").Consensus),
            () => Assert.Equal(0, result.Disputed));
    }

    /// <summary>
    ///     Test that a section that one submitted ballot marks as not applicable and another treats as applicable is
    ///     disputed, with its checks left out and the outcome null.
    /// </summary>
    [Fact]
    public void BallotConsolidation_Consolidate_SectionNaVsApplicable_SectionDisputed()
    {
        // Arrange: slot 1 skips section s2; slot 2 answers its check
        var ballots = new[]
        {
            Make(1, BallotStatus.Submitted, [Answer("i1", ChecklistOutcome.Pass), Answer("i2", ChecklistOutcome.Pass)], NotApplicable("s2")),
            Full(2, BallotStatus.Submitted)
        };

        // Act
        var result = BallotConsolidation.Consolidate(_definition, ballots);

        // Assert: the section is disputed once, its check is not listed, and there is no outcome
        Assert.Multiple(
            () => Assert.Equal(SectionConsensus.Disputed, result.Sections.Single(section => section.SectionId == "s2").Consensus),
            () => Assert.DoesNotContain(result.Checks, check => check.ItemId == "i3"),
            () => Assert.Equal(2, result.Checks.Count),
            () => Assert.Equal(1, result.Disputed),
            () => Assert.Null(result.Outcome));
    }

    /// <summary>
    ///     Test that a section that every submitted ballot marks as not applicable is agreed as not applicable and its
    ///     checks are left out.
    /// </summary>
    [Fact]
    public void BallotConsolidation_Consolidate_AllSectionsNa_ChecksOmitted()
    {
        // Arrange: both ballots mark both sections as not applicable and answer nothing
        var ballots = new[]
        {
            Make(1, BallotStatus.Submitted, [], NotApplicable("s1"), NotApplicable("s2")),
            Make(2, BallotStatus.Submitted, [], NotApplicable("s1"), NotApplicable("s2"))
        };

        // Act
        var result = BallotConsolidation.Consolidate(_definition, ballots);

        // Assert: no check is listed, both sections do not apply, and nothing blocks the outcome
        Assert.Multiple(
            () => Assert.Empty(result.Checks),
            () => Assert.All(result.Sections, section => Assert.Equal(SectionConsensus.NotApplicable, section.Consensus)),
            () => Assert.Equal(0, result.Pending + result.Disputed + result.Agreed),
            () => Assert.Equal(ReviewOutcome.Approved, result.Outcome));
    }

    /// <summary>
    ///     Test that a pass against a not-applicable answer to the same check is a dispute.
    /// </summary>
    [Fact]
    public void BallotConsolidation_Consolidate_ItemNaVsPass_Disputed()
    {
        // Arrange: slot 1 passes i1, slot 2 says it does not apply
        var ballots = new[]
        {
            Full(1, BallotStatus.Submitted),
            Full(2, BallotStatus.Submitted, outcome1: ChecklistOutcome.NotApplicable)
        };

        // Act
        var result = BallotConsolidation.Consolidate(_definition, ballots);

        // Assert
        var check = CheckOf(result, "i1");
        Assert.Multiple(
            () => Assert.Equal(CheckConsensus.Disputed, check.Consensus),
            () => Assert.Equal(ChecklistOutcome.NotApplicable, check.Outcomes[2]),
            () => Assert.Null(result.Outcome));
    }

    /// <summary>
    ///     Test that a check that every submitted ballot marks not applicable is agreed as not applicable, is not a
    ///     failure, and does not block the outcome.
    /// </summary>
    [Fact]
    public void BallotConsolidation_Consolidate_AllItemNa_AgreedNotFailed()
    {
        // Arrange: both ballots mark every check as not applicable
        var na = ChecklistOutcome.NotApplicable;
        var ballots = new[]
        {
            Full(1, BallotStatus.Submitted, na, na, na),
            Full(2, BallotStatus.Submitted, na, na, na)
        };

        // Act
        var result = BallotConsolidation.Consolidate(_definition, ballots);

        // Assert
        Assert.Multiple(
            () => Assert.All(result.Checks, check => Assert.Equal(CheckConsensus.Agreed, check.Consensus)),
            () => Assert.All(result.Checks, check => Assert.Equal(ChecklistOutcome.NotApplicable, check.Outcome)),
            () => Assert.Equal(0, result.Failed),
            () => Assert.Equal(ReviewOutcome.Approved, result.Outcome));
    }

    /// <summary>
    ///     Test that an agreed fail makes the outcome rejected.
    /// </summary>
    [Fact]
    public void BallotConsolidation_Consolidate_AgreedFail_Rejected()
    {
        // Arrange: both ballots fail i2 and pass the rest
        var ballots = new[]
        {
            Full(1, BallotStatus.Submitted, outcome2: ChecklistOutcome.Fail),
            Full(2, BallotStatus.Submitted, outcome2: ChecklistOutcome.Fail)
        };

        // Act
        var result = BallotConsolidation.Consolidate(_definition, ballots);

        // Assert
        Assert.Multiple(
            () => Assert.Equal(ReviewOutcome.Rejected, result.Outcome),
            () => Assert.Equal(1, result.Failed),
            () => Assert.Equal(3, result.Agreed));
    }

    /// <summary>
    ///     Test that agreed passes make the outcome approved, also while another slot has not submitted.
    /// </summary>
    [Fact]
    public void BallotConsolidation_Consolidate_AllPass_Approved()
    {
        // Arrange: one ballot is submitted and another slot has only a draft that answers nothing
        var ballots = new[]
        {
            Full(1, BallotStatus.Submitted),
            Make(2, BallotStatus.Draft, [])
        };

        // Act
        var result = BallotConsolidation.Consolidate(_definition, ballots);

        // Assert: the slot that has not submitted cannot hold the outcome back
        Assert.Equal(ReviewOutcome.Approved, result.Outcome);
    }

    /// <summary>
    ///     Test that a dispute leaves the outcome null even when another check agreed as a fail.
    /// </summary>
    [Fact]
    public void BallotConsolidation_Consolidate_DisputedCheck_OutcomeNull()
    {
        // Arrange: i1 is disputed, i2 and i3 are agreed (i2 as a fail)
        var ballots = new[]
        {
            Full(1, BallotStatus.Submitted, outcome2: ChecklistOutcome.Fail),
            Full(2, BallotStatus.Submitted, ChecklistOutcome.Fail, ChecklistOutcome.Fail)
        };

        // Act
        var result = BallotConsolidation.Consolidate(_definition, ballots);

        // Assert
        Assert.Multiple(
            () => Assert.Equal(CheckConsensus.Disputed, CheckOf(result, "i1").Consensus),
            () => Assert.Null(result.Outcome));
    }

    /// <summary>
    ///     Test that a check that a submitted ballot does not answer is pending and leaves the outcome null.
    /// </summary>
    [Fact]
    public void BallotConsolidation_Consolidate_PendingCheck_OutcomeNull()
    {
        // Arrange: one submitted ballot leaves i3 unanswered and the other answers it
        var ballots = new[]
        {
            Make(1, BallotStatus.Submitted, [Answer("i1", ChecklistOutcome.Pass), Answer("i2", ChecklistOutcome.Pass)]),
            Full(2, BallotStatus.Submitted)
        };

        // Act
        var result = BallotConsolidation.Consolidate(_definition, ballots);

        // Assert: the check waits, lists only the slot that answered, and the outcome is null
        var check = CheckOf(result, "i3");
        Assert.Multiple(
            () => Assert.Equal(CheckConsensus.Pending, check.Consensus),
            () => Assert.Null(check.Outcome),
            () => Assert.Equal(new[] { 2 }, check.Outcomes.Keys),
            () => Assert.Null(result.Outcome));
    }

    /// <summary>
    ///     Test that a checklist without checks gives approved for one submitted ballot.
    /// </summary>
    [Fact]
    public void BallotConsolidation_Consolidate_NoChecklistItems_OneSubmittedBallot_Approved()
    {
        // Arrange: an empty checklist and one submitted ballot
        var empty = new ChecklistDefinition("1", []);

        // Act
        var result = BallotConsolidation.Consolidate(empty, [Make(1, BallotStatus.Submitted, [])]);

        // Assert
        Assert.Multiple(
            () => Assert.Empty(result.Checks),
            () => Assert.Equal(ReviewOutcome.Approved, result.Outcome));
    }

    /// <summary>
    ///     Test that a checklist without checks has no outcome while no ballot is submitted.
    /// </summary>
    [Fact]
    public void BallotConsolidation_Consolidate_NoChecklistItems_NoSubmittedBallot_OutcomeNull()
    {
        // Arrange: an empty checklist and only a draft
        var empty = new ChecklistDefinition("1", []);

        // Act
        var result = BallotConsolidation.Consolidate(empty, [Make(1, BallotStatus.Draft, [])]);

        // Assert
        Assert.Null(result.Outcome);
    }

    /// <summary>
    ///     Test that the counts are consistent with the listed checks and the disputed sections, and that a failed
    ///     check is counted once.
    /// </summary>
    [Fact]
    public void BallotConsolidation_Consolidate_Counts_AreConsistent()
    {
        // Arrange: five checks in two sections. i1 agrees as a pass, i2 is disputed, i3 agrees as a fail, i4 is
        // unanswered by both, and section s2 is disputed
        var definition = new ChecklistDefinition(
            "1",
            [
                new ChecklistSection("s1", "Basics", "-", [new ChecklistItem("i1", "A?"), new ChecklistItem("i2", "B?"), new ChecklistItem("i3", "C?"), new ChecklistItem("i4", "D?")]),
                new ChecklistSection("s2", "Docs", "-", [new ChecklistItem("i5", "E?")])
            ]);
        var ballots = new[]
        {
            Make(1, BallotStatus.Submitted, [Answer("i1", ChecklistOutcome.Pass), Answer("i2", ChecklistOutcome.Pass), Answer("i3", ChecklistOutcome.Fail, "No.")], NotApplicable("s2")),
            Make(2, BallotStatus.Submitted, [Answer("i1", ChecklistOutcome.Pass), Answer("i2", ChecklistOutcome.Fail, "No."), Answer("i3", ChecklistOutcome.Fail, "No."), Answer("i5", ChecklistOutcome.Pass)])
        };

        // Act
        var result = BallotConsolidation.Consolidate(definition, ballots);

        // Assert: four listed checks plus one disputed section make five
        Assert.Multiple(
            () => Assert.Equal(4, result.Checks.Count),
            () => Assert.Equal(2, result.Agreed),
            () => Assert.Equal(2, result.Disputed),
            () => Assert.Equal(1, result.Pending),
            () => Assert.Equal(1, result.Failed),
            () => Assert.Equal(result.Checks.Count + 1, result.Agreed + result.Disputed + result.Pending),
            () => Assert.Null(result.Outcome));
    }

    /// <summary>
    ///     Test that the numbers of submitted ballots and of all ballots are reported.
    /// </summary>
    [Fact]
    public void BallotConsolidation_Consolidate_BallotCounts()
    {
        // Arrange: two submitted ballots and a draft
        var ballots = new[]
        {
            Full(1, BallotStatus.Submitted),
            Full(2, BallotStatus.Draft),
            Full(3, BallotStatus.Submitted)
        };

        // Act
        var result = BallotConsolidation.Consolidate(_definition, ballots);

        // Assert
        Assert.Multiple(
            () => Assert.Equal(2, result.BallotsSubmitted),
            () => Assert.Equal(3, result.BallotsTotal));
    }

    /// <summary>
    ///     Test that the order of the ballots never changes the result and that the result follows the checklist order.
    /// </summary>
    [Fact]
    public void BallotConsolidation_Consolidate_BallotOrder_DoesNotMatter()
    {
        // Arrange: three ballots with an agreement, a dispute and a draft
        var ballots = new[]
        {
            Full(1, BallotStatus.Submitted),
            Full(2, BallotStatus.Submitted, outcome3: ChecklistOutcome.Fail),
            Full(3, BallotStatus.Draft, ChecklistOutcome.Fail)
        };

        // Act: consolidate in both orders
        var forward = BallotConsolidation.Consolidate(_definition, ballots);
        var backward = BallotConsolidation.Consolidate(_definition, ballots.OrderByDescending(ballot => ballot.SlotId).ToArray());

        // Assert: the same standing in the same checklist order, whatever the order of the ballots
        Assert.Multiple(
            () => Assert.Equal(new[] { "i1", "i2", "i3" }, forward.Checks.Select(check => check.ItemId)),
            () => Assert.Equal(Describe(forward), Describe(backward)),
            () => Assert.Equal(forward.Outcome, backward.Outcome));
    }

    /// <summary>
    ///     Describes the checks of a result in one line for comparison.
    /// </summary>
    /// <param name="result">The result.</param>
    /// <returns>The text.</returns>
    private static string Describe(BallotConsolidationResult result)
    {
        return string.Join(
            ";",
            result.Checks.Select(check => $"{check.ItemId}:{check.Consensus}:{check.Outcome}:{string.Join(",", check.Outcomes.OrderBy(pair => pair.Key).Select(pair => $"{pair.Key}={pair.Value}"))}"));
    }

    /// <summary>
    ///     Test that null arguments are refused, and a null ballot in the list.
    /// </summary>
    [Fact]
    public void BallotConsolidation_NullArguments_Throws()
    {
        // Arrange / Act / Assert: each null argument, and a list that holds a null ballot
        Assert.Multiple(
            () => Assert.Throws<ArgumentNullException>(() => BallotConsolidation.Consolidate(null!, [])),
            () => Assert.Throws<ArgumentNullException>(() => BallotConsolidation.Consolidate(_definition, null!)),
            () => Assert.Throws<ArgumentException>(() => BallotConsolidation.Consolidate(_definition, new Ballot[] { null! })),
            () => Assert.Throws<ArgumentException>(() => BallotConsolidation.Consolidate(_definition, new[] { Full(1, BallotStatus.Submitted), Full(1, BallotStatus.Submitted) })),
            () => Assert.Throws<ArgumentNullException>(() => BallotConsolidation.IsComplete(null!, Full(1, BallotStatus.Draft))),
            () => Assert.Throws<ArgumentNullException>(() => BallotConsolidation.IsComplete(_definition, null!)));
    }

    /// <summary>
    ///     Test that a ballot shows as submitted or as a draft, and is never shown as not started.
    /// </summary>
    [Fact]
    public void BallotConsolidation_BallotState_DraftAndSubmitted_ReflectsStatus()
    {
        // Arrange / Act: ballots of both stored statuses
        var draft = Full(1, BallotStatus.Draft);
        var submitted = Full(2, BallotStatus.Submitted);

        // Assert
        Assert.Multiple(
            () => Assert.Equal(BallotState.Draft, draft.State),
            () => Assert.Equal(BallotState.Submitted, submitted.State));
    }

    /// <summary>
    ///     Test that a ballot that answers every check is complete, whatever the outcomes and the status.
    /// </summary>
    [Fact]
    public void BallotConsolidation_IsComplete_AllAnswered_True()
    {
        // Arrange: a draft with a pass, a fail and a not-applicable answer
        var ballot = Full(1, BallotStatus.Draft, ChecklistOutcome.Pass, ChecklistOutcome.Fail, ChecklistOutcome.NotApplicable);

        // Act / Assert
        Assert.True(BallotConsolidation.IsComplete(_definition, ballot));
    }

    /// <summary>
    ///     Test that a ballot that leaves a check of an applicable section unanswered is not complete.
    /// </summary>
    [Fact]
    public void BallotConsolidation_IsComplete_MissingAnswer_False()
    {
        // Arrange: i3 has no answer
        var ballot = Make(1, BallotStatus.Submitted, [Answer("i1", ChecklistOutcome.Pass), Answer("i2", ChecklistOutcome.Pass)]);

        // Act / Assert
        Assert.False(BallotConsolidation.IsComplete(_definition, ballot));
    }

    /// <summary>
    ///     Test that a section that the ballot marks as not applicable asks for no answers.
    /// </summary>
    [Fact]
    public void BallotConsolidation_IsComplete_NaSectionSkipsItems_True()
    {
        // Arrange: section s2 is not applicable in the ballot and so i3 needs no answer
        var ballot = Make(1, BallotStatus.Draft, [Answer("i1", ChecklistOutcome.Pass), Answer("i2", ChecklistOutcome.Pass)], NotApplicable("s2"));

        // Act / Assert
        Assert.True(BallotConsolidation.IsComplete(_definition, ballot));
    }
}
