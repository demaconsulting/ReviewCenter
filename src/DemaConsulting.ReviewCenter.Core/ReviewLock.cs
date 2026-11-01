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
///     The one definition of when a review is concluded, and therefore locked, and the guard the services call
///     before they change reviewed content.
/// </summary>
/// <remarks>
///     A review is concluded when it has at least one reviewer slot, every slot holds a signature of the current
///     record digest, no thread is open, and, for a review with a checklist, every slot has handed in its ballot and the
///     submitted ballots agree on every check with the outcome approved (a rejected outcome does not conclude the review
///     yet). The open-thread rule is deliberately separate from the digest: whether a
///     thread is open or resolved is not part of the digest (so resolving chat never makes a signature out of date),
///     but an open thread, chat or included, blocks signing and conclusion until somebody resolves it. A concluded
///     review is locked: nothing a signature covers (the report, checklist answers and applicability), nothing the
///     discussion consists of (threads and comments) and nothing that decides who signs (the reviewer slots) may
///     change until an administrator reopens it. Locked is not stored; it is computed from the same snapshot as the
///     approval status, so the two can never disagree. The storage layer applies <see cref="IsConcluded"/> again
///     inside its write transactions, which closes the gap between this check and a concurrent final signature. The
///     class is stateless and thread-safe.
/// </remarks>
public static class ReviewLock
{
    /// <summary>
    ///     The message of the refusal; it names the way forward so a user is never left guessing.
    /// </summary>
    public const string LockedMessage =
        "This review is concluded and locked. A project administrator must reopen it before it can change.";

    /// <summary>
    ///     Tells whether the review is concluded: every reviewer slot holds a signature of the current record and
    ///     no thread is open.
    /// </summary>
    /// <param name="record">The record as it is now. Must not be null.</param>
    /// <param name="slots">The reviewer slots of the review. Must not be null.</param>
    /// <param name="signatures">The signatures of the review, current and out of date. Must not be null.</param>
    /// <returns>
    ///     <c>true</c> when there is at least one slot, each slot has a signature of the current digest, and every
    ///     thread of the record is resolved.
    /// </returns>
    /// <remarks>
    ///     Pure and stateless, so the Storage subsystem can call it from inside a transaction with data it loaded
    ///     itself. The thread state is read from the record, not from the digest, which does not cover it.
    /// </remarks>
    public static bool IsConcluded(
        ReviewRecord record,
        IReadOnlyList<ReviewerSlot> slots,
        IReadOnlyList<ReviewSignature> signatures)
    {
        if (slots.Count == 0)
        {
            return false;
        }

        // An open thread keeps the review open, however many signatures are current
        if (record.Threads.Any(t => t.State != ReviewThreadState.Resolved))
        {
            return false;
        }

        // The digest is computed once; a slot is signed when any of its signatures carries it
        var digest = RecordDigest.Compute(record);
        if (!slots.All(slot => signatures.Any(s => s.SlotId == slot.Id && s.RecordDigest == digest)))
        {
            return false;
        }

        // A review without a checklist has no ballots and keeps the rule above (it cannot be signed anyway)
        return record.Checklist is null || BallotsAgreeOnApproval(record, slots);
    }

    /// <summary>
    ///     Tells whether every slot has handed in its ballot and the ballots agree on every check and lead to approval.
    /// </summary>
    /// <param name="record">The record, with a checklist.</param>
    /// <param name="slots">The reviewer slots of the review.</param>
    /// <returns>
    ///     <c>true</c> when each slot has a submitted ballot and the consolidation has no pending or disputed check and
    ///     the outcome is approved. A rejected review is not concluded: the rejected conclusion is not available yet.
    /// </returns>
    private static bool BallotsAgreeOnApproval(ReviewRecord record, IReadOnlyList<ReviewerSlot> slots)
    {
        var submitted = record.Ballots.Where(b => b.Status == BallotStatus.Submitted).Select(b => b.SlotId).ToHashSet();
        if (!slots.All(slot => submitted.Contains(slot.Id)))
        {
            return false;
        }

        var result = BallotConsolidation.Consolidate(record.Checklist!.Parse(), record.Ballots);
        return result.Pending == 0 && result.Disputed == 0 && result.Outcome == ReviewOutcome.Approved;
    }

    /// <summary>
    ///     Refuses the change when the review is concluded.
    /// </summary>
    /// <param name="store">The store to read the snapshot from. Must not be null.</param>
    /// <param name="projectId">The project identity.</param>
    /// <param name="reviewId">The review identifier. Must not be null.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    ///     A task with the snapshot that was read to decide, so the caller can reuse it; <c>null</c> when the review
    ///     does not exist.
    /// </returns>
    /// <remarks>
    ///     A review that does not exist is not locked: the caller's own not-found rule then applies, unchanged.
    /// </remarks>
    /// <exception cref="DomainRuleException">Thrown with <see cref="DomainRule.ReviewLocked"/> when the review is concluded.</exception>
    internal static async Task<ReviewSnapshot?> EnsureUnlockedAsync(
        IReviewStore store,
        int projectId,
        string reviewId,
        CancellationToken cancellationToken)
    {
        var snapshot = await store.GetSnapshotAsync(projectId, reviewId, cancellationToken);
        if (snapshot is not null && IsConcluded(snapshot.Record, snapshot.Slots, snapshot.Signatures))
        {
            throw new DomainRuleException(DomainRule.ReviewLocked, LockedMessage);
        }

        return snapshot;
    }
}
