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
///     A current signature that a change to the review would make out of date.
/// </summary>
/// <remarks>
///     The record is an immutable value holder and is safe to share between threads.
/// </remarks>
/// <param name="SlotId">The identity of the reviewer slot the signature belongs to.</param>
/// <param name="SlotNumber">The number of the slot as the approval status shows it (1 for the first slot).</param>
/// <param name="SignerDisplayName">The display name of the signer when signing.</param>
/// <param name="SignerUserId">The identity of the signer.</param>
public sealed record AffectedSignature(int SlotId, int SlotNumber, string SignerDisplayName, int SignerUserId);

/// <summary>
///     Works out which current signatures a change to a review would make out of date, and refuses the change until
///     the person making it has acknowledged them.
/// </summary>
/// <remarks>
///     A signature is current while its digest equals the digest of the record, so any change to what the record
///     covers makes every current signature out of date. The check compares the digest of the record as it is with
///     the digest of the record as the change would leave it, both in memory: no extra read and no speculative write.
///     A change that leaves the digest alone (answering the same value again as the same person, for example) warns
///     nobody. The check uses the snapshot read just before the write, so a signature stored in between is not seen;
///     it still becomes out of date, the warning is best effort and the approval is always computed from the record.
///     The class is stateless and thread-safe.
/// </remarks>
internal static class SignatureImpact
{
    /// <summary>
    ///     Lists the signatures of the current record, one per slot.
    /// </summary>
    /// <param name="snapshot">The review snapshot. Must not be null.</param>
    /// <returns>The signatures that carry the digest of the record now, in slot order; empty when there are none.</returns>
    public static IReadOnlyList<AffectedSignature> CurrentSignatures(ReviewSnapshot snapshot)
    {
        var digest = RecordDigest.Compute(snapshot.Record);
        // The slot number is the position by identity, the same numbering the approval status uses
        return snapshot.Slots
            .OrderBy(slot => slot.Id)
            .Select((slot, index) => (slot, number: index + 1))
            .Select(entry => (
                entry.slot,
                entry.number,
                signature: snapshot.Signatures
                    .Where(s => s.SlotId == entry.slot.Id && s.RecordDigest == digest)
                    .MaxBy(s => s.Id)))
            .Where(entry => entry.signature is not null)
            .Select(entry => new AffectedSignature(
                entry.slot.Id, entry.number, entry.signature!.DisplayName, entry.signature.UserId))
            .ToList();
    }

    /// <summary>
    ///     Refuses a change that would make a signature out of date unless every such signature is acknowledged.
    /// </summary>
    /// <param name="snapshot">The review snapshot read before the change, or <c>null</c> when the review does not exist.</param>
    /// <param name="changed">
    ///     The record as the change would leave it. The digest of it is compared with the digest of the current record;
    ///     a <c>null</c> record (kept for a change that always alters the digest) no longer occurs for threads, because
    ///     chat leaves the digest alone.
    /// </param>
    /// <param name="acknowledged">The slot identities the person has acknowledged, or <c>null</c> for none.</param>
    /// <param name="excludeSlotId">
    ///     The slot whose own signature needs no acknowledgement, or <c>null</c> for none. A reviewer who changes the
    ///     ballot of the own slot knows that the own signature, which the change withdraws, is out of date; the
    ///     signatures of the other slots are still listed.
    /// </param>
    /// <exception cref="DomainRuleException">
    ///     Thrown with <see cref="DomainRule.SignaturesWouldBecomeOutOfDate"/> when the change alters the digest and a
    ///     current signature is not acknowledged; the exception lists the signatures not acknowledged.
    /// </exception>
    public static void RequireAcknowledged(
        ReviewSnapshot? snapshot,
        ReviewRecord? changed,
        IReadOnlyCollection<int>? acknowledged,
        int? excludeSlotId = null)
    {
        if (snapshot is null)
        {
            return;
        }

        // Without a current signature there is nothing to lose, and no second digest is needed
        var current = CurrentSignatures(snapshot).Where(s => s.SlotId != excludeSlotId).ToList();
        if (current.Count == 0)
        {
            return;
        }

        // A change that leaves the digest alone is a no-op for the signatures
        if (changed is not null && RecordDigest.Compute(changed) == RecordDigest.Compute(snapshot.Record))
        {
            return;
        }

        var remaining = current.Where(s => acknowledged is null || !acknowledged.Contains(s.SlotId)).ToList();
        if (remaining.Count == 0)
        {
            return;
        }

        var names = string.Join(", ", remaining.Select(s => $"slot {s.SlotNumber} ({s.SignerDisplayName})"));
        throw new DomainRuleException(
            DomainRule.SignaturesWouldBecomeOutOfDate,
            $"Signed reviewers will need to look again and sign again, because the report changes: {names}. Acknowledge this to continue.",
            remaining);
    }
}
