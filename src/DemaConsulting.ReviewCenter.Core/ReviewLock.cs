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
///     record digest that attests the outcome the record has now, no thread is open, and, for a review with a
///     checklist, every slot has handed in its ballot and the submitted ballots agree on every check. The agreed
///     outcome is approved, or rejected when an agreed check failed; a rejected review is concluded only when it also
///     has a review conclusion text that is not blank (the reasons), so a rejection is never signed without them.
///     The open-thread rule is deliberately separate from the digest: whether a
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
    ///     <c>true</c> when the review is concluded as approved or as rejected (see <see cref="ConcludedOutcome"/>).
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
        return ConcludedOutcome(record, slots, signatures) != ReviewConclusion.None;
    }

    /// <summary>
    ///     Decides how the review is concluded, if it is.
    /// </summary>
    /// <param name="record">The record as it is now. Must not be null.</param>
    /// <param name="slots">The reviewer slots of the review. Must not be null.</param>
    /// <param name="signatures">The signatures of the review, current and out of date. Must not be null.</param>
    /// <returns>
    ///     <see cref="ReviewConclusion.Approved"/> or <see cref="ReviewConclusion.Rejected"/> when there is at least
    ///     one slot, every thread of the record is resolved, each slot has a signature of the current digest that
    ///     attests the outcome the record has now and, for a review with a checklist, each slot has a submitted ballot,
    ///     no check is pending or disputed and the outcome is agreed; a rejected outcome also needs a review conclusion
    ///     text that is not blank. Otherwise <see cref="ReviewConclusion.None"/>.
    /// </returns>
    /// <remarks>
    ///     This is the one definition of a concluded review; <see cref="IsConcluded"/> is derived from it. A review
    ///     without a checklist has no ballots and concludes as approved (it cannot be signed anyway). A signature
    ///     whose stored outcome differs from the outcome the record has now never concludes the review (fail safe).
    /// </remarks>
    public static ReviewConclusion ConcludedOutcome(
        ReviewRecord record,
        IReadOnlyList<ReviewerSlot> slots,
        IReadOnlyList<ReviewSignature> signatures)
    {
        if (slots.Count == 0)
        {
            return ReviewConclusion.None;
        }

        // An open thread keeps the review open, however many signatures are current
        if (record.Threads.Any(t => t.State != ReviewThreadState.Resolved))
        {
            return ReviewConclusion.None;
        }

        var outcome = AgreedOutcome(record, slots);
        if (outcome is null)
        {
            return ReviewConclusion.None;
        }

        // A rejection is signed together with the reasons for it
        if (outcome == ReviewOutcome.Rejected && string.IsNullOrWhiteSpace(record.ReviewConclusionText))
        {
            return ReviewConclusion.None;
        }

        // The digest is computed once; a slot is signed when a signature of it carries the digest and the outcome
        var digest = RecordDigest.Compute(record);
        if (!slots.All(slot => signatures.Any(
                s => s.SlotId == slot.Id && s.RecordDigest == digest && s.Outcome == outcome)))
        {
            return ReviewConclusion.None;
        }

        return outcome == ReviewOutcome.Rejected ? ReviewConclusion.Rejected : ReviewConclusion.Approved;
    }

    /// <summary>
    ///     Gets the outcome every slot's signature has to attest: the agreed outcome of the submitted ballots.
    /// </summary>
    /// <param name="record">The record.</param>
    /// <param name="slots">The reviewer slots of the review.</param>
    /// <returns>
    ///     <see cref="ReviewOutcome.Approved"/> for a review without a checklist; otherwise the outcome of the
    ///     consolidation when each slot has a submitted ballot and no check is pending or disputed; else <c>null</c>.
    /// </returns>
    private static ReviewOutcome? AgreedOutcome(ReviewRecord record, IReadOnlyList<ReviewerSlot> slots)
    {
        if (record.Checklist is null)
        {
            return ReviewOutcome.Approved;
        }

        var submitted = record.Ballots.Where(b => b.Status == BallotStatus.Submitted).Select(b => b.SlotId).ToHashSet();
        if (!slots.All(slot => submitted.Contains(slot.Id)))
        {
            return null;
        }

        var result = BallotConsolidation.Consolidate(record.Checklist.Parse(), record.Ballots);
        return result.Pending == 0 && result.Disputed == 0 ? result.Outcome : null;
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
