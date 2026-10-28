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
///     Remembers, for a short time, that an external-provider user has just confirmed their identity for one
///     specific signature, so the signature can be recorded once.
/// </summary>
/// <remarks>
///     A user of a local account confirms a signature with the password, which is checked in the same request.
///     A user of an external account confirms it by signing in again at the identity provider, which ends in a
///     different request; this store carries that confirmation from the sign-in callback to the signing request.
///     A proof is bound to the user, the project, the review and the slot, expires after
///     <see cref="Lifetime"/>, and is spent by the first <see cref="TryConsume"/>, so it can not be reused, moved
///     to another review or slot, or used by another user. It lives in memory only (a restart simply asks the
///     user to confirm again), and holds at most <see cref="MaxEntries"/> proofs: expired ones are dropped first,
///     then the oldest. The class is safe for concurrent use.
/// </remarks>
public sealed class ReauthProofStore
{
    /// <summary>
    ///     How long a proof stays valid after it is issued.
    /// </summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    /// <summary>
    ///     The largest number of proofs kept at the same time.
    /// </summary>
    public const int MaxEntries = 10_000;

    /// <summary>
    ///     The clock that decides when a proof expires.
    /// </summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>
    ///     The proofs by what they are bound to, each with the moment it was issued.
    /// </summary>
    private readonly Dictionary<(int UserId, int ProjectId, string ReviewId, int SlotId), DateTimeOffset> _proofs = [];

    /// <summary>
    ///     Guards <see cref="_proofs"/>.
    /// </summary>
    private readonly object _lock = new();

    /// <summary>
    ///     Initializes a new instance of the <see cref="ReauthProofStore"/> class.
    /// </summary>
    /// <param name="timeProvider">The clock; injected so tests control time. Must not be null.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="timeProvider"/> is null.</exception>
    public ReauthProofStore(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        _timeProvider = timeProvider;
    }

    /// <summary>
    ///     Records that a user has just confirmed their identity for one signature.
    /// </summary>
    /// <param name="userId">The identity of the user who confirmed.</param>
    /// <param name="projectId">The project of the review.</param>
    /// <param name="reviewId">The review identifier. Must not be null.</param>
    /// <param name="slotId">The slot the user is about to sign.</param>
    /// <remarks>
    ///     An older proof for the same user, review and slot is replaced, so the lifetime restarts. When the store is
    ///     full, expired proofs are dropped first and then the oldest one.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="reviewId"/> is null.</exception>
    public void Issue(int userId, int projectId, string reviewId, int slotId)
    {
        ArgumentNullException.ThrowIfNull(reviewId);
        var now = _timeProvider.GetUtcNow();
        lock (_lock)
        {
            var key = (userId, projectId, reviewId, slotId);
            if (!_proofs.ContainsKey(key) && _proofs.Count >= MaxEntries)
            {
                MakeRoom(now);
            }

            _proofs[key] = now;
        }
    }

    /// <summary>
    ///     Tells whether a live proof exists, without spending it.
    /// </summary>
    /// <param name="userId">The identity of the user.</param>
    /// <param name="projectId">The project of the review.</param>
    /// <param name="reviewId">The review identifier. Must not be null.</param>
    /// <param name="slotId">The slot.</param>
    /// <returns><c>true</c> when the user confirmed their identity for exactly this signature and it has not expired.</returns>
    /// <remarks>Used by a page to show that the identity is confirmed; it never decides whether a signature is allowed.</remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="reviewId"/> is null.</exception>
    public bool Has(int userId, int projectId, string reviewId, int slotId)
    {
        ArgumentNullException.ThrowIfNull(reviewId);
        var now = _timeProvider.GetUtcNow();
        lock (_lock)
        {
            return _proofs.TryGetValue((userId, projectId, reviewId, slotId), out var issued) && IsLive(issued, now);
        }
    }

    /// <summary>
    ///     Spends the proof of a signature.
    /// </summary>
    /// <param name="userId">The identity of the user who signs.</param>
    /// <param name="projectId">The project of the review.</param>
    /// <param name="reviewId">The review identifier. Must not be null.</param>
    /// <param name="slotId">The slot being signed.</param>
    /// <returns>
    ///     <c>true</c> exactly once per issued proof, when it is bound to exactly these values and has not expired;
    ///     <c>false</c> otherwise (an expired proof is removed too).
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="reviewId"/> is null.</exception>
    public bool TryConsume(int userId, int projectId, string reviewId, int slotId)
    {
        ArgumentNullException.ThrowIfNull(reviewId);
        var now = _timeProvider.GetUtcNow();
        lock (_lock)
        {
            if (!_proofs.Remove((userId, projectId, reviewId, slotId), out var issued))
            {
                return false;
            }

            return IsLive(issued, now);
        }
    }

    /// <summary>
    ///     Tells whether a proof issued at a moment is still valid.
    /// </summary>
    /// <param name="issued">The moment the proof was issued.</param>
    /// <param name="now">The current moment.</param>
    /// <returns><c>true</c> while the proof is younger than <see cref="Lifetime"/>.</returns>
    private static bool IsLive(DateTimeOffset issued, DateTimeOffset now)
    {
        return now - issued < Lifetime;
    }

    /// <summary>
    ///     Frees one place: drops the expired proofs, and the oldest one when none was expired.
    /// </summary>
    /// <param name="now">The current moment.</param>
    /// <remarks>The caller holds the lock.</remarks>
    private void MakeRoom(DateTimeOffset now)
    {
        var expired = _proofs.Where(pair => !IsLive(pair.Value, now)).Select(pair => pair.Key).ToList();
        foreach (var key in expired)
        {
            _proofs.Remove(key);
        }

        if (_proofs.Count >= MaxEntries)
        {
            _proofs.Remove(_proofs.MinBy(pair => pair.Value).Key);
        }
    }
}
