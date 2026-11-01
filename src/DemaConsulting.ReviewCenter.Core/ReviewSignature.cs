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
///     One signature on a review: who approved which version of the record, in which slot and seat, and when.
/// </summary>
/// <remarks>
///     A signature is append-only: <see cref="IReviewStore"/> has no way to change or delete one. It names
///     the signer by identity and by display name at the time of signing, so the record stays readable even
///     if the user is renamed later. It is <em>current</em> only while <see cref="RecordDigest"/> equals the
///     digest of the review's record now; after any change to the record it is out of date but is kept.
///     The class is a plain data holder with no behavior; instances are not thread-safe.
/// </remarks>
public sealed class ReviewSignature
{
    /// <summary>
    ///     Gets the identity of the signature, assigned by the store.
    /// </summary>
    public int Id { get; init; }

    /// <summary>
    ///     Gets the identity of the owning project.
    /// </summary>
    public int ProjectId { get; init; }

    /// <summary>
    ///     Gets the identifier of the signed review.
    /// </summary>
    public string ReviewId { get; init; } = string.Empty;

    /// <summary>
    ///     Gets the identity of the reviewer slot the signature belongs to.
    /// </summary>
    public int SlotId { get; init; }

    /// <summary>
    ///     Gets the identity of the signer.
    /// </summary>
    public int UserId { get; init; }

    /// <summary>
    ///     Gets the display name of the signer when signing.
    /// </summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>
    ///     Gets the seat the signer held in the slot when signing.
    /// </summary>
    public SlotRole Role { get; init; }

    /// <summary>
    ///     Gets the time of signing (UTC).
    /// </summary>
    public DateTimeOffset SignedAt { get; init; }

    /// <summary>
    ///     Gets the digest of the review record that was signed (see <see cref="Core.RecordDigest"/>).
    /// </summary>
    public string RecordDigest { get; init; } = string.Empty;

    /// <summary>
    ///     Gets the outcome the signer attested: approved or rejected.
    /// </summary>
    /// <remarks>
    ///     It is the agreed outcome of the consolidated ballots of the record the signature points to (the digest
    ///     covers it, so a signature of the current digest always attests the outcome the record has now). Approved is
    ///     the default, which is also the value of a review without a checklist.
    /// </remarks>
    public ReviewOutcome Outcome { get; init; } = ReviewOutcome.Approved;

    /// <summary>
    ///     Gets the statement the signer made (see <see cref="SigningService.MeaningFor"/>).
    /// </summary>
    public string Meaning { get; init; } = string.Empty;
}
