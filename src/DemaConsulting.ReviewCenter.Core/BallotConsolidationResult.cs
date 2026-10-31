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
///     How the submitted ballots stand on one check.
/// </summary>
public enum CheckConsensus
{
    /// <summary>Some submitted ballot has no answer to the check, or no ballot is submitted yet.</summary>
    Pending,

    /// <summary>Every submitted ballot answers the check with the same outcome.</summary>
    Agreed,

    /// <summary>The submitted ballots answer the check with different outcomes.</summary>
    Disputed
}

/// <summary>
///     How the submitted ballots stand on whether one section applies.
/// </summary>
public enum SectionConsensus
{
    /// <summary>Every submitted ballot treats the section as applicable, or no ballot is submitted yet.</summary>
    Applicable,

    /// <summary>Every submitted ballot marks the section as not applicable.</summary>
    NotApplicable,

    /// <summary>Some submitted ballots mark the section as not applicable and others treat it as applicable.</summary>
    Disputed
}

/// <summary>
///     The consolidated standing of one check of the checklist.
/// </summary>
/// <remarks>
///     Only the outcomes of the submitted ballots take part; notes and justifications never do. The record is
///     immutable.
/// </remarks>
/// <param name="ItemId">The identifier of the check within the checklist.</param>
/// <param name="SectionId">The identifier of the section the check belongs to.</param>
/// <param name="Consensus">The standing of the check.</param>
/// <param name="Outcome">The agreed outcome; not <c>null</c> exactly when <paramref name="Consensus"/> is <see cref="CheckConsensus.Agreed"/>.</param>
/// <param name="Outcomes">
///     The outcome that each submitted ballot gave, by the identity of its slot and enumerated in ascending slot
///     order whatever the order of the ballots, for showing differing answers side by side; a ballot that did not
///     answer the check has no entry.
/// </param>
public sealed record CheckConsolidation(
    string ItemId,
    string SectionId,
    CheckConsensus Consensus,
    ChecklistOutcome? Outcome,
    IReadOnlyDictionary<int, ChecklistOutcome> Outcomes);

/// <summary>
///     The consolidated standing of one section of the checklist.
/// </summary>
/// <param name="SectionId">The identifier of the section.</param>
/// <param name="Consensus">Whether the section applies, does not apply, or is disputed.</param>
public sealed record SectionConsolidation(string SectionId, SectionConsensus Consensus);

/// <summary>
///     The result of <see cref="BallotConsolidation.Consolidate"/>: the standing of every check and section, the
///     outcome it leads to, and counts for a summary.
/// </summary>
/// <remarks>
///     The record is immutable. <paramref name="Agreed"/> + <paramref name="Disputed"/> + <paramref name="Pending"/>
///     equals the number of <paramref name="Checks"/> plus the number of disputed sections, because the checks of a
///     disputed section are not listed and the dispute of the section counts once.
/// </remarks>
/// <param name="Checks">
///     The checks of the sections that apply, in checklist order; the checks of a section that does not apply, or
///     that is disputed, are not listed.
/// </param>
/// <param name="Sections">Every section, in checklist order.</param>
/// <param name="Outcome">
///     The outcome the submitted ballots lead to: <c>null</c> while no ballot is submitted or any check is pending
///     or disputed or any section is disputed, otherwise <see cref="ReviewOutcome.Rejected"/> when an agreed check
///     failed and <see cref="ReviewOutcome.Approved"/> if not.
/// </param>
/// <param name="Agreed">The number of agreed checks.</param>
/// <param name="Disputed">The number of disputed checks plus the number of disputed sections.</param>
/// <param name="Pending">The number of pending checks.</param>
/// <param name="Failed">The number of agreed checks whose agreed outcome is a fail.</param>
/// <param name="BallotsSubmitted">The number of submitted ballots.</param>
/// <param name="BallotsTotal">The number of ballots given, drafts included.</param>
public sealed record BallotConsolidationResult(
    IReadOnlyList<CheckConsolidation> Checks,
    IReadOnlyList<SectionConsolidation> Sections,
    ReviewOutcome? Outcome,
    int Agreed,
    int Disputed,
    int Pending,
    int Failed,
    int BallotsSubmitted,
    int BallotsTotal);
