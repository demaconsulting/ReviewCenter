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
///     Pure functions that compute the record a ballot edit or a slot change would lead to, so the services can ask
///     <see cref="SignatureImpact"/> which signatures it would make out of date before anything is stored.
/// </summary>
/// <remarks>
///     The store applies the same rules to the stored rows; these functions only mirror them on a <see cref="ReviewRecord"/>
///     in memory. The class belongs to the <see cref="BallotConsolidation"/> unit, holds no state and does no I/O, and is
///     thread-safe.
/// </remarks>
public static class BallotEdits
{
    /// <summary>
    ///     Replaces the ballot of a slot in a record, or adds it, keeping the ballots in slot order.
    /// </summary>
    /// <param name="record">The record. Must not be null.</param>
    /// <param name="ballot">The ballot to put in the record. Must not be null.</param>
    /// <returns>The record with the ballot.</returns>
    public static ReviewRecord WithBallot(ReviewRecord record, Ballot ballot)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(ballot);
        var ballots = record.Ballots.Where(b => b.SlotId != ballot.SlotId).Append(ballot).OrderBy(b => b.SlotId).ToList();
        return record with { Ballots = ballots };
    }

    /// <summary>
    ///     Removes the ballot of a slot from a record.
    /// </summary>
    /// <param name="record">The record. Must not be null.</param>
    /// <param name="slotId">The identity of the slot.</param>
    /// <returns>The record without the ballot of the slot.</returns>
    public static ReviewRecord WithoutBallot(ReviewRecord record, int slotId)
    {
        ArgumentNullException.ThrowIfNull(record);
        return record with { Ballots = record.Ballots.Where(b => b.SlotId != slotId).ToList() };
    }

    /// <summary>
    ///     Gets the ballot a slot would have after an answer is stored: the answer replaces the earlier one and the ballot
    ///     is a draft.
    /// </summary>
    /// <param name="current">The ballot as stored, or <c>null</c> when the slot has none yet.</param>
    /// <param name="slotId">The identity of the slot.</param>
    /// <param name="answer">The answer row, with the slot identity set. Must not be null.</param>
    /// <returns>The edited ballot, as a draft.</returns>
    public static Ballot WithAnswer(Ballot? current, int slotId, ChecklistAnswer answer)
    {
        ArgumentNullException.ThrowIfNull(answer);
        var answers = (current?.Answers ?? []).Where(a => a.ItemId != answer.ItemId).Append(answer).OrderBy(a => a.ItemId, StringComparer.Ordinal).ToList();
        return new Ballot(slotId, BallotStatus.Draft, null, null, answers, current?.SectionStates ?? []);
    }

    /// <summary>
    ///     Gets the ballot a slot would have after a section state is stored: the state replaces the earlier one and the
    ///     ballot is a draft.
    /// </summary>
    /// <param name="current">The ballot as stored, or <c>null</c> when the slot has none yet.</param>
    /// <param name="slotId">The identity of the slot.</param>
    /// <param name="state">The section state row, with the slot identity set. Must not be null.</param>
    /// <returns>The edited ballot, as a draft.</returns>
    public static Ballot WithSectionState(Ballot? current, int slotId, ChecklistSectionState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var sections = (current?.SectionStates ?? []).Where(s => s.SectionId != state.SectionId).Append(state).OrderBy(s => s.SectionId, StringComparer.Ordinal).ToList();
        return new Ballot(slotId, BallotStatus.Draft, null, null, current?.Answers ?? [], sections);
    }

    /// <summary>
    ///     Gets the ballot a slot would have after a reviewer leaves it: the ballot is a draft again and the rows that the
    ///     departing reviewers gave themselves are gone.
    /// </summary>
    /// <param name="ballot">The ballot as stored. Must not be null.</param>
    /// <param name="departingUsernames">The user names of the reviewers who leave the slot. Must not be null.</param>
    /// <returns>
    ///     The reverted ballot. Rows carried from an earlier review stay, because their author is not the departing
    ///     reviewer; the comparison of user names ignores case.
    /// </returns>
    public static Ballot Reassign(Ballot ballot, IReadOnlyCollection<string> departingUsernames)
    {
        ArgumentNullException.ThrowIfNull(ballot);
        ArgumentNullException.ThrowIfNull(departingUsernames);
        bool Departed(string username) => departingUsernames.Contains(username, StringComparer.OrdinalIgnoreCase);
        return new Ballot(
            ballot.SlotId,
            BallotStatus.Draft,
            null,
            null,
            ballot.Answers.Where(a => a.CarriedFromReviewId is not null || !Departed(a.AnsweredByUsername)).ToList(),
            ballot.SectionStates.Where(s => s.CarriedFromReviewId is not null || !Departed(s.SetByUsername)).ToList());
    }
}
