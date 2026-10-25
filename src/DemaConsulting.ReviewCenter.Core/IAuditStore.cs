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
///     The persistence contract of the audit trail, implemented by the storage layer.
/// </summary>
/// <remarks>
///     The contract is append-only: there is deliberately no way to change or delete an entry. Entries that
///     belong to a change of data are saved together with that change through the draft parameters of
///     <see cref="IReviewStore"/> and <see cref="IUserStore"/>; <see cref="AppendAsync"/> is for events that
///     change no data (sign-in, sign-out, failed sign-in, lockout). Implementations must give every entry the
///     next sequence number and the hash of the previous entry, one at a time, and must be safe for concurrent use.
/// </remarks>
public interface IAuditStore
{
    /// <summary>
    ///     Appends one entry in its own transaction.
    /// </summary>
    /// <param name="draft">What to record. Must not be null.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The stored entry with its sequence, time and hashes.</returns>
    Task<AuditEntry> AppendAsync(AuditDraft draft, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Lists entries newest first, optionally of one review and optionally from one side of a position.
    /// </summary>
    /// <param name="projectId">Only entries of this project, or <c>null</c> for all. Used together with <paramref name="reviewId"/>.</param>
    /// <param name="reviewId">Only entries of this review (of <paramref name="projectId"/>), or <c>null</c> for all.</param>
    /// <param name="beforeSequence">Only entries with a lower sequence than this, or <c>null</c>.</param>
    /// <param name="afterSequence">
    ///     Only entries with a higher sequence than this, or <c>null</c>; the nearest ones are returned, still newest first.
    /// </param>
    /// <param name="take">The largest number of entries to return.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>At most <paramref name="take"/> entries, newest first.</returns>
    Task<IReadOnlyList<AuditEntry>> ListAsync(
        int? projectId,
        string? reviewId,
        long? beforeSequence,
        long? afterSequence,
        int take,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Reads entries in sequence order, for verification; never loads the whole trail at once.
    /// </summary>
    /// <param name="afterSequence">Only entries with a higher sequence than this; 0 starts at the beginning.</param>
    /// <param name="count">The largest number of entries to return.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>At most <paramref name="count"/> entries, oldest first; empty at the end of the trail.</returns>
    Task<IReadOnlyList<AuditEntry>> ReadAfterAsync(
        long afterSequence,
        int count,
        CancellationToken cancellationToken = default);
}
