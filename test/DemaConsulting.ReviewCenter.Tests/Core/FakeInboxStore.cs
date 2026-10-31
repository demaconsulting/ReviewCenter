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

using DemaConsulting.ReviewCenter.Core;

namespace DemaConsulting.ReviewCenter.Tests.Core;

/// <summary>
///     In-memory <see cref="IInboxStore"/> used to test the inbox service and the pages without a database.
/// </summary>
/// <remarks>
///     The involved reviews are derived from a <see cref="FakeReviewStore"/> through its public reads (projects,
///     reviews and slots), so the two fakes never disagree. The read state is held in a list and updated the way the
///     real store does: it never moves backwards.
/// </remarks>
/// <param name="reviews">The review store the involved reviews are derived from.</param>
internal sealed class FakeInboxStore(IReviewStore reviews) : IInboxStore
{
    /// <summary>
    ///     The stored read-state rows.
    /// </summary>
    private readonly List<ReviewUserState> _states = [];

    /// <summary>
    ///     Gets a snapshot of the stored read-state rows.
    /// </summary>
    public IReadOnlyList<ReviewUserState> States => _states.ToArray();

    /// <inheritdoc />
    public async Task<IReadOnlyList<InboxCandidate>> ListInvolvedReviewsAsync(
        int userId,
        string username,
        CancellationToken cancellationToken = default)
    {
        var result = new List<InboxCandidate>();
        foreach (var project in (await reviews.ListProjectsAsync(cancellationToken)).OrderBy(p => p.Id))
        {
            foreach (var review in (await reviews.ListReviewsAsync(project.Id, cancellationToken)).OrderBy(r => r.Id, StringComparer.Ordinal))
            {
                var slots = await reviews.ListSlotsAsync(project.Id, review.Id, cancellationToken);
                var assigned = slots.Any(s => s.PrimaryUserId == userId || s.AlternateUserId == userId);
                var authored = string.Equals(review.CreatedBy, username, StringComparison.OrdinalIgnoreCase);
                if (assigned || authored)
                {
                    result.Add(new InboxCandidate(project.Id, review.Id, review.Title, review.CreatedBy, review.DueDate));
                }
            }
        }

        return result;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ReviewUserState>> ListStatesAsync(int userId, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ReviewUserState> list = _states.Where(s => s.UserId == userId).ToList();
        return Task.FromResult(list);
    }

    /// <inheritdoc />
    public Task<ReviewUserState?> GetStateAsync(
        int userId,
        int projectId,
        string reviewId,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(
            _states.FirstOrDefault(s => s.UserId == userId && s.ProjectId == projectId && s.ReviewId == reviewId));
    }

    /// <inheritdoc />
    public Task MarkSeenAsync(
        int userId,
        int projectId,
        string reviewId,
        int lastSeenCommentId,
        DateTimeOffset seenAt,
        CancellationToken cancellationToken = default)
    {
        var index = _states.FindIndex(s => s.UserId == userId && s.ProjectId == projectId && s.ReviewId == reviewId);
        if (index < 0)
        {
            _states.Add(new ReviewUserState(userId, projectId, reviewId, lastSeenCommentId, seenAt));
        }
        else
        {
            var old = _states[index];
            _states[index] = old with
            {
                LastSeenCommentId = Math.Max(old.LastSeenCommentId, lastSeenCommentId),
                LastSeenAt = old.LastSeenAt > seenAt ? old.LastSeenAt : seenAt
            };
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task MarkSeenManyAsync(
        int userId,
        IReadOnlyList<InboxSeenMark> marks,
        DateTimeOffset seenAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(marks);
        foreach (var mark in marks)
        {
            await MarkSeenAsync(userId, mark.ProjectId, mark.ReviewId, mark.LastSeenCommentId, seenAt, cancellationToken);
        }
    }
}
