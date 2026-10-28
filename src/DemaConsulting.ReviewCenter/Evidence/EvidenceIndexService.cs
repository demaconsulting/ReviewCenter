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

using System.Globalization;
using DemaConsulting.ReviewCenter.Core;

namespace DemaConsulting.ReviewCenter.Evidence;

/// <summary>
///     Builds the evidence index of a project: the completed reviews, in the form ReviewMark reads.
/// </summary>
/// <remarks>
///     A review is listed when it was planned from a review-set (it has a review-set id, which is the ReviewMark
///     review id) and it is complete: at least one reviewer slot, and every slot signed with the current record
///     digest, which is the completion rule of <see cref="AttestationService"/>. A review without a review-set id is
///     skipped, because a made-up id could wrongly satisfy a review-set. Only passing reviews exist: a review with a
///     failed checklist answer cannot be signed. ReviewMark keeps one entry per (id, fingerprint), so the index has
///     one entry per pair: the one completed last, and on a tie the lowest review id. The entries are ordered by id,
///     then fingerprint (ordinal), so the output is reproducible. The service is stateless and thread-safe.
/// </remarks>
internal sealed class EvidenceIndexService
{
    /// <summary>
    ///     Reads the reviews of a project.
    /// </summary>
    private readonly ReviewService _reviews;

    /// <summary>
    ///     Decides whether a review is complete.
    /// </summary>
    private readonly SigningService _signing;

    /// <summary>
    ///     Decides who may see which project.
    /// </summary>
    private readonly IProjectAccess _access;

    /// <summary>
    ///     Initializes a new instance of the <see cref="EvidenceIndexService"/> class.
    /// </summary>
    /// <param name="reviews">The review service. Must not be null.</param>
    /// <param name="signing">The signing service. Must not be null.</param>
    /// <param name="access">The access rules. Must not be null.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    public EvidenceIndexService(ReviewService reviews, SigningService signing, IProjectAccess access)
    {
        ArgumentNullException.ThrowIfNull(reviews);
        ArgumentNullException.ThrowIfNull(signing);
        ArgumentNullException.ThrowIfNull(access);
        _reviews = reviews;
        _signing = signing;
        _access = access;
    }

    /// <summary>
    ///     Builds the evidence index of a project.
    /// </summary>
    /// <param name="actor">The user (or the owner of the token) who asks; must be able to see the project. Must not be null.</param>
    /// <param name="projectId">The project number.</param>
    /// <param name="cancellationToken">A token to cancel the work.</param>
    /// <returns>The entries, one per (id, fingerprint), ordered by id and then fingerprint; empty when none.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="actor"/> is null.</exception>
    /// <exception cref="DomainRuleException">Thrown with <see cref="DomainRule.ProjectNotFound"/> when the actor may not see the project.</exception>
    public async Task<IReadOnlyList<EvidenceIndexEntry>> BuildAsync(
        Actor actor,
        int projectId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        await _access.RequireViewAsync(actor, projectId, cancellationToken);
        // Only reviews planned from a review set can be listed; their approvals are read in one batch so the users
        // are loaded once, not once per review
        var planned = (await _reviews.ListAsync(projectId, cancellationToken))
            .Where(review => !string.IsNullOrEmpty(review.ReviewSetId))
            .ToList();
        var approvals = await _signing.GetApprovalsAsync(projectId, planned.Select(review => review.Id), cancellationToken);
        var best = new Dictionary<(string Id, string Fingerprint), (DateTimeOffset At, string ReviewId)>();
        foreach (var review in planned)
        {
            if (!approvals.TryGetValue(review.Id, out var approval) || !approval.IsApproved)
            {
                continue;
            }

            // The day of the last signature of the current record
            var completed = approval.Slots.Max(s => s.SignedAt!.Value);
            var key = (review.ReviewSetId!, review.ContentFingerprint);
            if (!best.TryGetValue(key, out var kept)
                || completed > kept.At
                || (completed == kept.At && string.CompareOrdinal(review.Id, kept.ReviewId) < 0))
            {
                best[key] = (completed, review.Id);
            }
        }

        return best
            .OrderBy(pair => pair.Key.Id, StringComparer.Ordinal)
            .ThenBy(pair => pair.Key.Fingerprint, StringComparer.Ordinal)
            .Select(pair => new EvidenceIndexEntry(
                pair.Key.Id,
                pair.Key.Fingerprint,
                pair.Value.At.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                pair.Value.ReviewId))
            .ToList();
    }
}
