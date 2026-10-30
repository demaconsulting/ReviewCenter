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
///     The one definition of when a review is approved, and therefore locked, and the guard the services call
///     before they change reviewed content.
/// </summary>
/// <remarks>
///     A review is approved when it has at least one reviewer slot and every slot holds a signature of the current
///     record digest. An approved review is locked: nothing a signature covers (threads, comments, checklist answers
///     and applicability) and nothing that decides who signs (the reviewer slots) may change until an administrator
///     reopens it. Locked is not stored; it is computed from the same snapshot as the approval status, so the two can
///     never disagree. The storage layer applies <see cref="IsApproved"/> again inside its write transactions, which
///     closes the gap between this check and a concurrent final signature. The class is stateless and thread-safe.
/// </remarks>
public static class ReviewLock
{
    /// <summary>
    ///     The message of the refusal; it names the way forward so a user is never left guessing.
    /// </summary>
    public const string LockedMessage =
        "This review is approved and locked. A project administrator must reopen it before it can change.";

    /// <summary>
    ///     Tells whether every reviewer slot holds a signature of the current record.
    /// </summary>
    /// <param name="record">The record as it is now. Must not be null.</param>
    /// <param name="slots">The reviewer slots of the review. Must not be null.</param>
    /// <param name="signatures">The signatures of the review, current and out of date. Must not be null.</param>
    /// <returns><c>true</c> when there is at least one slot and each slot has a signature of the current digest.</returns>
    /// <remarks>
    ///     Pure and stateless, so the Storage subsystem can call it from inside a transaction with data it loaded
    ///     itself.
    /// </remarks>
    public static bool IsApproved(
        ReviewRecord record,
        IReadOnlyList<ReviewerSlot> slots,
        IReadOnlyList<ReviewSignature> signatures)
    {
        if (slots.Count == 0)
        {
            return false;
        }

        // The digest is computed once; a slot is signed when any of its signatures carries it
        var digest = RecordDigest.Compute(record);
        return slots.All(slot => signatures.Any(s => s.SlotId == slot.Id && s.RecordDigest == digest));
    }

    /// <summary>
    ///     Refuses the change when the review is approved.
    /// </summary>
    /// <param name="store">The store to read the snapshot from. Must not be null.</param>
    /// <param name="projectId">The project identity.</param>
    /// <param name="reviewId">The review identifier. Must not be null.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the review is known not to be locked.</returns>
    /// <remarks>
    ///     A review that does not exist is not locked: the caller's own not-found rule then applies, unchanged.
    /// </remarks>
    /// <exception cref="DomainRuleException">Thrown with <see cref="DomainRule.ReviewLocked"/> when the review is approved.</exception>
    internal static async Task EnsureUnlockedAsync(
        IReviewStore store,
        int projectId,
        string reviewId,
        CancellationToken cancellationToken)
    {
        var snapshot = await store.GetSnapshotAsync(projectId, reviewId, cancellationToken);
        if (snapshot is not null && IsApproved(snapshot.Record, snapshot.Slots, snapshot.Signatures))
        {
            throw new DomainRuleException(DomainRule.ReviewLocked, LockedMessage);
        }
    }
}
