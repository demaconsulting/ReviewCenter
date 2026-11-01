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
///     review id) and it is complete: at least one reviewer slot, every slot signed with the current record
///     digest and no thread open, which is the completion rule of <see cref="AttestationService"/>. A review without a review-set id is
///     skipped, because a made-up id could wrongly satisfy a review-set. Only approved reviews are listed: a review
///     that was rejected (every slot signed the outcome rejected) is concluded and locked but never listed, because
///     it is no evidence that the file set passed. ReviewMark keeps one entry per (id, fingerprint), so the index has
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
    ///     Reads the evidence settings, for a request without a signed-in user.
    /// </summary>
    private readonly EvidenceSettingsService _settings;

    /// <summary>
    ///     Initializes a new instance of the <see cref="EvidenceIndexService"/> class.
    /// </summary>
    /// <param name="reviews">The review service. Must not be null.</param>
    /// <param name="signing">The signing service. Must not be null.</param>
    /// <param name="access">The access rules. Must not be null.</param>
    /// <param name="settings">The evidence settings service. Must not be null.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    public EvidenceIndexService(
        ReviewService reviews,
        SigningService signing,
        IProjectAccess access,
        EvidenceSettingsService settings)
    {
        ArgumentNullException.ThrowIfNull(reviews);
        ArgumentNullException.ThrowIfNull(signing);
        ArgumentNullException.ThrowIfNull(access);
        ArgumentNullException.ThrowIfNull(settings);
        _reviews = reviews;
        _signing = signing;
        _access = access;
        _settings = settings;
    }

    /// <summary>
    ///     Builds the evidence index of a project.
    /// </summary>
    /// <param name="actor">
    ///     The user (or the owner of the token) who asks, who must be able to see the project; <c>null</c> for a
    ///     request without a signed-in user, which is served only when the project allows an anonymous evidence index.
    /// </param>
    /// <param name="projectId">The project number.</param>
    /// <param name="cancellationToken">A token to cancel the work.</param>
    /// <returns>The entries, one per (id, fingerprint), ordered by id and then fingerprint; empty when none.</returns>
    /// <exception cref="DomainRuleException">
    ///     Thrown with <see cref="DomainRule.ProjectNotFound"/> when the actor may not see the project, or when there
    ///     is no actor and the project does not exist or does not allow an anonymous evidence index.
    /// </exception>
    public async Task<IReadOnlyList<EvidenceIndexEntry>> BuildAsync(
        Actor? actor,
        int projectId,
        CancellationToken cancellationToken = default)
    {
        if (actor is null)
        {
            // Without a signed-in user the project's own switch is the only thing that can allow the read, and a
            // project that does not allow it answers like one that does not exist
            var effective = await _settings.GetEffectiveAsync(projectId, cancellationToken);
            if (effective is not { AnonymousIndex: true })
            {
                throw new DomainRuleException(DomainRule.ProjectNotFound, $"Project {projectId} does not exist.");
            }
        }
        else
        {
            await _access.RequireViewAsync(actor, projectId, cancellationToken);
        }

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
