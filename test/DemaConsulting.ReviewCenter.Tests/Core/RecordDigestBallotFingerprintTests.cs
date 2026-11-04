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

using System.Security.Cryptography;
using System.Text;
using DemaConsulting.ReviewCenter.Core;

namespace DemaConsulting.ReviewCenter.Tests.Core;

/// <summary>
///     Unit tests of the ballot fingerprint of <see cref="RecordDigest"/>: the canonical text of the submitted
///     ballots and its SHA-256, the identity of the ballots appendix of an attestation.
/// </summary>
public class RecordDigestBallotFingerprintTests
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
    /// <param name="justification">The justification; none for a Pass when omitted.</param>
    /// <param name="displayName">The display name of the answerer.</param>
    /// <returns>The answer.</returns>
    private static ChecklistAnswer Answer(
        int slotId,
        string item,
        ChecklistOutcome outcome = ChecklistOutcome.Pass,
        string? justification = null,
        string displayName = "Bob")
    {
        return new ChecklistAnswer
        {
            ProjectId = 1,
            ReviewId = "r",
            SlotId = slotId,
            ItemId = item,
            Outcome = outcome,
            Justification = justification ?? (outcome == ChecklistOutcome.Pass ? null : "Because"),
            AnsweredBy = displayName,
            AnsweredByUsername = "bob",
            AnsweredAt = _then
        };
    }

    /// <summary>
    ///     Builds a section state that marks a section not applicable.
    /// </summary>
    /// <param name="slotId">The slot.</param>
    /// <param name="section">The section id.</param>
    /// <returns>The state.</returns>
    private static ChecklistSectionState NotApplicable(int slotId, string section)
    {
        return new ChecklistSectionState
        {
            ProjectId = 1,
            ReviewId = "r",
            SlotId = slotId,
            SectionId = section,
            Applicable = false,
            Reason = "Not relevant",
            SetBy = "Bob",
            SetByUsername = "bob",
            SetAt = _then
        };
    }

    /// <summary>
    ///     Builds a submitted ballot that answers every check Pass except the first, which has the given outcome.
    /// </summary>
    /// <param name="slotId">The slot.</param>
    /// <param name="first">The outcome of the first check.</param>
    /// <param name="displayName">The display name of the answerer.</param>
    /// <returns>The ballot.</returns>
    private static Ballot Submitted(int slotId, ChecklistOutcome first = ChecklistOutcome.Pass, string displayName = "Bob")
    {
        return new Ballot(
            slotId,
            BallotStatus.Submitted,
            _then,
            "bob",
            [
                Answer(slotId, "a-01", first, displayName: displayName),
                Answer(slotId, "a-02"),
                Answer(slotId, "b-01")
            ],
            []);
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
    ///     Test that a record without a submitted ballot gives only the header, and that a draft ballot does not count.
    /// </summary>
    [Fact]
    public void RecordDigest_CanonicalizeBallots_NoSubmittedBallot_GivesOnlyTheHeader()
    {
        // Arrange: a record without ballots, and one with a draft ballot
        var none = Record();
        var draft = Record(new Ballot(1, BallotStatus.Draft, null, null, [Answer(1, "a-01")], []));

        // Act
        var emptyText = RecordDigest.CanonicalizeBallots(none);
        var draftText = RecordDigest.CanonicalizeBallots(draft);

        // Assert
        Assert.Equal("ReviewCenter ballots v1", emptyText);
        Assert.Equal(RecordDigest.BallotsHeader, draftText);
        Assert.Equal(RecordDigest.ComputeBallotFingerprint(none), RecordDigest.ComputeBallotFingerprint(draft));
    }

    /// <summary>
    ///     Test that the fingerprint is the lowercase hexadecimal SHA-256 of the canonical text.
    /// </summary>
    [Fact]
    public void RecordDigest_ComputeBallotFingerprint_Record_IsSha256OfCanonicalTextInLowercaseHex()
    {
        // Arrange
        var record = Record(Submitted(1), Submitted(2, ChecklistOutcome.Fail));

        // Act
        var fingerprint = RecordDigest.ComputeBallotFingerprint(record);

        // Assert
        var expected = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(RecordDigest.CanonicalizeBallots(record))))
            .ToLowerInvariant();
        Assert.Equal(expected, fingerprint);
        Assert.Equal(64, fingerprint.Length);
        Assert.Equal(fingerprint, RecordDigest.ComputeBallotFingerprint(record));
    }

    /// <summary>
    ///     Test that the ballots are written in the order of their slot, numbered from 1, whatever the order they are
    ///     held in and whatever the storage identities of the slots are.
    /// </summary>
    [Fact]
    public void RecordDigest_CanonicalizeBallots_SlotIdentities_NumbersBallotsByOrdinalInSlotOrder()
    {
        // Arrange: ballots held in reverse order with large identities; the lower slot answers Fail
        var held = Record(Submitted(20), Submitted(10, ChecklistOutcome.Fail));
        var renumbered = Record(Submitted(2), Submitted(1, ChecklistOutcome.Fail));

        // Act
        var lines = RecordDigest.CanonicalizeBallots(held).Split('\n');

        // Assert: ballot 1 is the Fail ballot of the lower slot, and the text does not depend on the identities
        Assert.Equal(RecordDigest.BallotsHeader, lines[0]);
        Assert.StartsWith("ballot 1 submitted", lines[1], StringComparison.Ordinal);
        Assert.Contains("Fail", lines[2], StringComparison.Ordinal);
        Assert.Contains(lines, l => l.StartsWith("ballot 2 submitted", StringComparison.Ordinal));
        Assert.Equal(RecordDigest.CanonicalizeBallots(held), RecordDigest.CanonicalizeBallots(renumbered));
        Assert.Equal(RecordDigest.ComputeBallotFingerprint(held), RecordDigest.ComputeBallotFingerprint(renumbered));
    }

    /// <summary>
    ///     Test that a change of one answer of one slot, which the record digest does not see when the slots agree,
    ///     changes the fingerprint, and that a draft ballot or a display name does not.
    /// </summary>
    [Fact]
    public void RecordDigest_ComputeBallotFingerprint_ChangedAnswer_ChangesFingerprintButDisplayNameDoesNot()
    {
        // Arrange
        var baseline = Record(Submitted(1), Submitted(2));
        var otherOutcome = Record(Submitted(1), Submitted(2, ChecklistOutcome.NotApplicable));
        var renamed = Record(Submitted(1), Submitted(2, displayName: "Robert"));
        var withDraft = Record(Submitted(1), Submitted(2), new Ballot(3, BallotStatus.Draft, null, null, [Answer(3, "a-01", ChecklistOutcome.Fail)], []));

        // Act
        var baselineFingerprint = RecordDigest.ComputeBallotFingerprint(baseline);

        // Assert
        Assert.NotEqual(baselineFingerprint, RecordDigest.ComputeBallotFingerprint(otherOutcome));
        Assert.Equal(baselineFingerprint, RecordDigest.ComputeBallotFingerprint(renamed));
        Assert.Equal(baselineFingerprint, RecordDigest.ComputeBallotFingerprint(withDraft));
    }

    /// <summary>
    ///     Test that when a check was answered more than once in a ballot only the last row counts, that the answer
    ///     lines are sorted by check id, and that a section marked not applicable has a line of its own (the store keeps
    ///     one row per section, so there is no repeated section state to reduce).
    /// </summary>
    [Fact]
    public void RecordDigest_CanonicalizeBallots_RepeatedAnswer_LastRowCountsAndLinesAreSorted()
    {
        // Arrange: b-01 first, then a-01 twice (Fail, then Pass), and section b marked not applicable
        var ballot = new Ballot(
            1,
            BallotStatus.Submitted,
            _then,
            "bob",
            [Answer(1, "b-01"), Answer(1, "a-01", ChecklistOutcome.Fail), Answer(1, "a-01")],
            [NotApplicable(1, "b")]);

        // Act
        var lines = RecordDigest.CanonicalizeBallots(Record(ballot)).Split('\n');

        // Assert: header, ballot, two answer lines in id order with the last outcome, then the section line
        Assert.Equal(5, lines.Length);
        Assert.StartsWith("answer ", lines[2], StringComparison.Ordinal);
        Assert.Contains("a-01", lines[2], StringComparison.Ordinal);
        Assert.Contains("Pass", lines[2], StringComparison.Ordinal);
        Assert.DoesNotContain("Fail", lines[2], StringComparison.Ordinal);
        Assert.Contains("b-01", lines[3], StringComparison.Ordinal);
        Assert.StartsWith("section ", lines[4], StringComparison.Ordinal);
        Assert.Contains("not-applicable", lines[4], StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a change of only the justification of an answer changes the fingerprint, although the record digest
    ///     does not cover the justification.
    /// </summary>
    [Fact]
    public void RecordDigest_ComputeBallotFingerprint_ChangedJustification_ChangesFingerprint()
    {
        // Arrange: two records that differ only in the justification of one answer
        var first = Record(new Ballot(1, BallotStatus.Submitted, _then, "bob", [Answer(1, "a-01", ChecklistOutcome.Fail, "One reason")], []));
        var second = Record(new Ballot(1, BallotStatus.Submitted, _then, "bob", [Answer(1, "a-01", ChecklistOutcome.Fail, "Another reason")], []));

        // Act
        var firstFingerprint = RecordDigest.ComputeBallotFingerprint(first);
        var secondFingerprint = RecordDigest.ComputeBallotFingerprint(second);

        // Assert
        Assert.NotEqual(firstFingerprint, secondFingerprint);
    }

    /// <summary>
    ///     Test that the section states of a ballot are written in the order of the section identifier, whatever the
    ///     order they are held in.
    /// </summary>
    [Fact]
    public void RecordDigest_CanonicalizeBallots_SectionStates_AreSortedBySectionId()
    {
        // Arrange: a ballot that holds the states of section b and then section a, both not applicable
        var ballot = new Ballot(1, BallotStatus.Submitted, _then, "bob", [], [NotApplicable(1, "b"), NotApplicable(1, "a")]);

        // Act
        var lines = RecordDigest.CanonicalizeBallots(Record(ballot)).Split('\n');

        // Assert: header, ballot, then the section lines in the order a, b
        Assert.Equal(4, lines.Length);
        Assert.StartsWith("section 1:a ", lines[2], StringComparison.Ordinal);
        Assert.StartsWith("section 1:b ", lines[3], StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a null record is refused.
    /// </summary>
    [Fact]
    public void RecordDigest_CanonicalizeBallots_NullRecord_Throws()
    {
        // Arrange, Act, Assert
        Assert.Throws<ArgumentNullException>(() => RecordDigest.CanonicalizeBallots(null!));
        Assert.Throws<ArgumentNullException>(() => RecordDigest.ComputeBallotFingerprint(null!));
    }
}
