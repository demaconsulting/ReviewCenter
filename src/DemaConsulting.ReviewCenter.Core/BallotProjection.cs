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
///     The consolidated standing of a record together with the checklist state that reads from it.
/// </summary>
/// <param name="State">
///     The checklist state of the agreed standing: an agreed check is answered with the row of the lowest slot that
///     answered it, an agreed not-applicable section has the row of the lowest slot that marked it; a pending or
///     disputed check has no answer.
/// </param>
/// <param name="Result">The consolidation the state was built from.</param>
public sealed record ConsolidatedView(ChecklistState State, BallotConsolidationResult Result);

/// <summary>
///     Projects the ballots of a record into the views the rest of the application reads: the consolidated standing of
///     the review, and the checklist state of one ballot.
/// </summary>
/// <remarks>
///     <para>
///         Everything that bears on progress, blockers, inbox rows, the attestation and the carry-over of a follow-up
///         review reads the checklist through this class, so each of them sees the same agreed standing that the record
///         digest covers (see <see cref="BallotConsolidation"/>).
///     </para>
///     <para>
///         A review with exactly one slot has no second reviewer to agree with, and its reviewer has no separate
///         hand-in step. For the counts of progress and blockers only (never for the digest), the sole ballot is then
///         read as submitted even while it is a draft, so a partly answered checklist shows its true progress. The
///         class is static, pure and thread-safe.
///     </para>
/// </remarks>
public static class BallotProjection
{
    /// <summary>
    ///     Consolidates the ballots of a record.
    /// </summary>
    /// <param name="record">The record. Must not be null.</param>
    /// <param name="slotCount">
    ///     The number of slots of the review when the sole ballot of a single-slot review is to count as submitted for
    ///     progress and blockers; <c>null</c> (the default) reads the submitted ballots only, as the digest does.
    /// </param>
    /// <param name="authorBlocked">The value of <see cref="ChecklistState.AuthorBlocked"/> for the state.</param>
    /// <param name="noSlot">The value of <see cref="ChecklistState.NoSlot"/> for the state.</param>
    /// <returns>The consolidated view, or <c>null</c> when the review has no checklist.</returns>
    public static ConsolidatedView? Consolidate(
        ReviewRecord record,
        int? slotCount = null,
        bool authorBlocked = false,
        bool noSlot = false)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (record.Checklist is null)
        {
            return null;
        }

        var definition = record.Checklist.Parse();
        IReadOnlyList<Ballot> ballots = record.Ballots;
        if (slotCount == 1 && ballots.Count == 1 && ballots[0].Status != BallotStatus.Submitted)
        {
            ballots = [ballots[0] with { Status = BallotStatus.Submitted }];
        }

        var result = BallotConsolidation.Consolidate(definition, ballots);
        var bySlot = ballots.Where(b => b.Status == BallotStatus.Submitted).ToDictionary(b => b.SlotId);

        // An agreed check is answered with the row of the lowest slot that answered it (the outcomes are in slot order)
        var answers = new List<ChecklistAnswer>();
        foreach (var check in result.Checks.Where(c => c.Consensus == CheckConsensus.Agreed))
        {
            var slotId = check.Outcomes.Keys.First();
            answers.Add(bySlot[slotId].Answers.Last(a => a.ItemId == check.ItemId));
        }

        // An agreed not-applicable section has the row of the lowest submitted slot that marked it
        var sections = new List<ChecklistSectionState>();
        foreach (var section in result.Sections.Where(s => s.Consensus == SectionConsensus.NotApplicable))
        {
            var state = bySlot.OrderBy(entry => entry.Key)
                .SelectMany(entry => entry.Value.SectionStates)
                .First(s => s.SectionId == section.SectionId && !s.Applicable);
            sections.Add(state);
        }

        var checklist = new ChecklistState(definition, sections, answers)
        {
            Consolidation = result,
            AuthorBlocked = authorBlocked,
            NoSlot = noSlot
        };
        return new ConsolidatedView(checklist, result);
    }

    /// <summary>
    ///     Builds the checklist state of one ballot, drafts included.
    /// </summary>
    /// <param name="definition">The frozen checklist. Must not be null.</param>
    /// <param name="slotId">The identity of the slot the ballot belongs to.</param>
    /// <param name="ballot">The ballot, or <c>null</c> when the slot has none yet (an empty state).</param>
    /// <param name="authorBlocked">The value of <see cref="ChecklistState.AuthorBlocked"/> for the state.</param>
    /// <returns>The state of the ballot, marked as the viewer's own ballot.</returns>
    public static ChecklistState ForBallot(ChecklistDefinition definition, int slotId, Ballot? ballot, bool authorBlocked = false)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return new ChecklistState(definition, ballot?.SectionStates ?? [], ballot?.Answers ?? [])
        {
            SlotId = slotId,
            AuthorBlocked = authorBlocked,
            BallotState = ballot?.State ?? BallotState.NotStarted,
            IsOwnBallot = true
        };
    }
}
