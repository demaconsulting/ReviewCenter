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
///     Consolidates the ballots of the reviewer slots of a review into one agreed standing of every check and
///     decides whether a ballot is complete.
/// </summary>
/// <remarks>
///     <para>
///         The consolidation exists so that the record of a review, and therefore what a signature binds to, depends
///         only on what the reviewers have handed in and agree on: only submitted ballots count, so a slot that has
///         not submitted can never change the result, and a check on which all submitted ballots agree stands as
///         agreed (provisionally, until a slot that submits later disagrees). Only the outcome of a check is compared;
///         justifications and notes never create a dispute, because wording must not decide whether reviewers agree.
///     </para>
///     <para>
///         The class is static, pure (no file system, database or clock), and thread-safe. Results follow the order
///         of the checklist and never depend on the order of the ballots.
///     </para>
/// </remarks>
public static class BallotConsolidation
{
    /// <summary>
    ///     Consolidates the ballots of a review against its checklist.
    /// </summary>
    /// <remarks>
    ///     The rules are, in order. Draft ballots are ignored. A section is not applicable in a ballot when that
    ///     ballot has a state for it that says so; it is not applicable when every submitted ballot says so, disputed
    ///     when only some do, and applicable when none does or when no ballot is submitted. The checks of a section
    ///     that does not apply, or that is disputed, are left out of <see cref="BallotConsolidationResult.Checks"/>;
    ///     the dispute of a section counts once. A check of an applicable section is pending when no ballot is
    ///     submitted or some submitted ballot does not answer it, agreed when all submitted ballots give the same
    ///     outcome (an item-level not-applicable answer is an outcome like the others, and is neither a fail nor a
    ///     block), and disputed when the outcomes differ. The outcome is <c>null</c> while no ballot is submitted or
    ///     anything is pending or disputed, and otherwise rejected when an agreed check failed and approved if not.
    /// </remarks>
    /// <param name="definition">The checklist frozen with the review. Must not be null.</param>
    /// <param name="ballots">The ballots of the review, drafts included, in any order. Must not be null or hold null.</param>
    /// <returns>The consolidated standing, in the order of the checklist.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="definition"/> or <paramref name="ballots"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="ballots"/> holds a null element or two ballots of one slot.</exception>
    public static BallotConsolidationResult Consolidate(ChecklistDefinition definition, IReadOnlyCollection<Ballot> ballots)
    {
        // Refuse missing input first: a null checklist or ballot has no meaningful standing
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(ballots);
        if (ballots.Any(ballot => ballot is null))
        {
            throw new ArgumentException("The ballots must not hold a null element.", nameof(ballots));
        }

        // One ballot per slot: a repeated slot would let one outcome silently overwrite another and hide a dispute
        if (ballots.Select(ballot => ballot.SlotId).Distinct().Count() != ballots.Count)
        {
            throw new ArgumentException("The ballots must not hold two ballots of the same slot.", nameof(ballots));
        }

        // Only submitted ballots count; each is indexed once so that every check is a dictionary lookup
        var submitted = ballots
            .Where(ballot => ballot.Status == BallotStatus.Submitted)
            .Select(Index)
            .ToList();

        // Walk the checklist in its own order so that the result never depends on the order of the ballots
        var checks = new List<CheckConsolidation>();
        var sections = new List<SectionConsolidation>();
        foreach (var section in definition.Sections)
        {
            var consensus = ConsolidateSection(section.Id, submitted);
            sections.Add(new SectionConsolidation(section.Id, consensus));

            // The checks of a section that does not apply, or that is disputed, are not listed
            if (consensus != SectionConsensus.Applicable)
            {
                continue;
            }

            foreach (var item in section.Items)
            {
                checks.Add(ConsolidateCheck(item.Id, section.Id, submitted));
            }
        }

        // Count the standing; a disputed section is one dispute, as its checks are not listed
        var agreed = checks.Count(check => check.Consensus == CheckConsensus.Agreed);
        var pending = checks.Count(check => check.Consensus == CheckConsensus.Pending);
        var disputed = checks.Count(check => check.Consensus == CheckConsensus.Disputed)
                       + sections.Count(section => section.Consensus == SectionConsensus.Disputed);
        var failed = checks.Count(check => check.Outcome == ChecklistOutcome.Fail);

        return new BallotConsolidationResult(
            checks,
            sections,
            DeriveOutcome(submitted.Count, pending, disputed, failed),
            agreed,
            disputed,
            pending,
            failed,
            submitted.Count,
            ballots.Count);
    }

    /// <summary>
    ///     Tells whether a ballot answers every check that it requires.
    /// </summary>
    /// <remarks>
    ///     A ballot is complete when, for every section that the ballot itself treats as applicable, every check has
    ///     an answer of any outcome. It decides whether a reviewer may hand the ballot in. The status of the ballot is
    ///     not looked at, so a draft can be asked whether it is ready.
    /// </remarks>
    /// <param name="definition">The checklist frozen with the review. Must not be null.</param>
    /// <param name="ballot">The ballot to test. Must not be null.</param>
    /// <returns><c>true</c> when no required check lacks an answer; otherwise <c>false</c>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="definition"/> or <paramref name="ballot"/> is null.</exception>
    public static bool IsComplete(ChecklistDefinition definition, Ballot ballot)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(ballot);

        var indexed = Index(ballot);
        foreach (var section in definition.Sections)
        {
            // A section that this ballot marks as not applicable asks for no answers
            if (indexed.NotApplicableSections.Contains(section.Id))
            {
                continue;
            }

            if (section.Items.Any(item => !indexed.Answers.ContainsKey(item.Id)))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    ///     Decides the outcome that the standing leads to.
    /// </summary>
    /// <param name="submittedCount">The number of submitted ballots.</param>
    /// <param name="pending">The number of pending checks.</param>
    /// <param name="disputed">The number of disputed checks and sections.</param>
    /// <param name="failed">The number of agreed checks that failed.</param>
    /// <returns>
    ///     <c>null</c> when there is no submitted ballot or anything is pending or disputed; otherwise rejected when a
    ///     check failed and approved if not (also when the checklist has no checks).
    /// </returns>
    private static ReviewOutcome? DeriveOutcome(int submittedCount, int pending, int disputed, int failed)
    {
        if (submittedCount == 0 || pending > 0 || disputed > 0)
        {
            return null;
        }

        return failed > 0 ? ReviewOutcome.Rejected : ReviewOutcome.Approved;
    }

    /// <summary>
    ///     Finds how the submitted ballots stand on whether one section applies.
    /// </summary>
    /// <param name="sectionId">The identifier of the section.</param>
    /// <param name="submitted">The indexed submitted ballots.</param>
    /// <returns>The standing; with no submitted ballot nothing is disputed, so the section applies.</returns>
    private static SectionConsensus ConsolidateSection(string sectionId, IReadOnlyList<IndexedBallot> submitted)
    {
        var notApplicable = submitted.Count(ballot => ballot.NotApplicableSections.Contains(sectionId));
        if (notApplicable == 0)
        {
            return SectionConsensus.Applicable;
        }

        return notApplicable == submitted.Count ? SectionConsensus.NotApplicable : SectionConsensus.Disputed;
    }

    /// <summary>
    ///     Finds how the submitted ballots stand on one check of a section that applies.
    /// </summary>
    /// <param name="itemId">The identifier of the check.</param>
    /// <param name="sectionId">The identifier of its section.</param>
    /// <param name="submitted">The indexed submitted ballots.</param>
    /// <returns>The standing of the check, with the outcome of each ballot that answered it.</returns>
    private static CheckConsolidation ConsolidateCheck(string itemId, string sectionId, IReadOnlyList<IndexedBallot> submitted)
    {
        // Collect what each submitted ballot answered, keyed in slot order so that the map never depends on the order
        // of the ballots; a missing answer makes the check pending
        var outcomes = new SortedDictionary<int, ChecklistOutcome>();
        var unanswered = submitted.Count == 0;
        foreach (var ballot in submitted)
        {
            if (ballot.Answers.TryGetValue(itemId, out var outcome))
            {
                outcomes[ballot.SlotId] = outcome;
            }
            else
            {
                unanswered = true;
            }
        }

        if (unanswered)
        {
            return new CheckConsolidation(itemId, sectionId, CheckConsensus.Pending, null, outcomes);
        }

        // Every submitted ballot answered: equal outcomes agree, any difference is a dispute
        var distinct = outcomes.Values.Distinct().ToList();
        return distinct.Count == 1
            ? new CheckConsolidation(itemId, sectionId, CheckConsensus.Agreed, distinct[0], outcomes)
            : new CheckConsolidation(itemId, sectionId, CheckConsensus.Disputed, null, outcomes);
    }

    /// <summary>
    ///     Indexes the answers and the not-applicable sections of a ballot for lookup.
    /// </summary>
    /// <remarks>
    ///     A ballot holds at most one answer per check and one state per section; should a list repeat one, the last
    ///     entry wins, so the result is still defined.
    /// </remarks>
    /// <param name="ballot">The ballot to index.</param>
    /// <returns>The indexed ballot.</returns>
    private static IndexedBallot Index(Ballot ballot)
    {
        var answers = new Dictionary<string, ChecklistOutcome>(StringComparer.Ordinal);
        foreach (var answer in ballot.Answers)
        {
            answers[answer.ItemId] = answer.Outcome;
        }

        var applicability = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (var state in ballot.SectionStates)
        {
            applicability[state.SectionId] = state.Applicable;
        }

        var notApplicable = applicability
            .Where(entry => !entry.Value)
            .Select(entry => entry.Key)
            .ToHashSet(StringComparer.Ordinal);

        return new IndexedBallot(ballot.SlotId, answers, notApplicable);
    }

    /// <summary>
    ///     One ballot prepared for lookups by check and by section.
    /// </summary>
    /// <param name="SlotId">The identity of the slot of the ballot.</param>
    /// <param name="Answers">The outcome of each answered check, by the identifier of the check.</param>
    /// <param name="NotApplicableSections">The identifiers of the sections that the ballot marks as not applicable.</param>
    private sealed record IndexedBallot(
        int SlotId,
        IReadOnlyDictionary<string, ChecklistOutcome> Answers,
        IReadOnlySet<string> NotApplicableSections);
}
