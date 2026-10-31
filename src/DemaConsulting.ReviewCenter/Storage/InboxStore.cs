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
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DemaConsulting.ReviewCenter.Storage;

/// <summary>
///     The <see cref="IInboxStore"/> implementation backed by <see cref="ReviewCenterDbContext"/>.
/// </summary>
/// <remarks>
///     Each operation uses its own short-lived context from the factory, so the store is safe for concurrent use.
///     The involved reviews are found by one projection query that never loads a file. Marking a review as seen is a
///     compare-and-set: the row is read, the new values are computed in memory (the SQLite provider cannot compare
///     <see cref="DateTimeOffset"/> values in a query), and one conditional <c>UPDATE</c> that matches the values that
///     were read stores them, so two writers that race cannot move the state backwards; the loser reads again and
///     retries. A missing row is inserted, and a duplicate key from a racing insert makes the loser retry as an update.
///     Nothing here is audited: read state is a personal convenience and not part of the review record.
/// </remarks>
/// <param name="factory">The factory that creates a context per operation.</param>
internal sealed class InboxStore(IDbContextFactory<ReviewCenterDbContext> factory) : IInboxStore
{
    /// <summary>
    ///     The SQLite extended result code of a primary key violation.
    /// </summary>
    private const int SqlitePrimaryKeyViolation = 1555;

    /// <summary>
    ///     The SQLite extended result code of a unique constraint violation.
    /// </summary>
    private const int SqliteUniqueViolation = 2067;

    /// <summary>
    ///     How often a mark is retried after losing a race; every lost round means another writer succeeded, so the
    ///     number of writers that run at the same time bounds the rounds that are needed.
    /// </summary>
    private const int MaxAttempts = 50;

    /// <inheritdoc />
    public async Task<IReadOnlyList<InboxCandidate>> ListInvolvedReviewsAsync(
        int userId,
        string username,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(username);
        await using var context = await factory.CreateDbContextAsync(cancellationToken);

        // The creator column has the NOCASE collation, so the equality ignores case and the index serves it
        var rows = await context.Reviews.AsNoTracking()
            .Where(review => review.CreatedBy == username
                             || context.ReviewerSlots.Any(slot =>
                                 slot.ProjectId == review.ProjectId
                                 && slot.ReviewId == review.Id
                                 && (slot.PrimaryUserId == userId || slot.AlternateUserId == userId)))
            .Select(review => new InboxCandidate(review.ProjectId, review.Id, review.Title, review.CreatedBy, review.DueDate))
            .ToListAsync(cancellationToken);
        return rows
            .OrderBy(row => row.ProjectId)
            .ThenBy(row => row.ReviewId, StringComparer.Ordinal)
            .ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ReviewUserState>> ListStatesAsync(int userId, CancellationToken cancellationToken = default)
    {
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        return await context.ReviewUserStates.AsNoTracking()
            .Where(state => state.UserId == userId)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ReviewUserState?> GetStateAsync(
        int userId,
        int projectId,
        string reviewId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(reviewId);
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        return await context.ReviewUserStates.AsNoTracking()
            .FirstOrDefaultAsync(
                state => state.UserId == userId && state.ProjectId == projectId && state.ReviewId == reviewId,
                cancellationToken);
    }

    /// <inheritdoc />
    public async Task MarkSeenAsync(
        int userId,
        int projectId,
        string reviewId,
        int lastSeenCommentId,
        DateTimeOffset seenAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(reviewId);
        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            await using var context = await factory.CreateDbContextAsync(cancellationToken);
            var current = await context.ReviewUserStates.AsNoTracking()
                .FirstOrDefaultAsync(
                    state => state.UserId == userId && state.ProjectId == projectId && state.ReviewId == reviewId,
                    cancellationToken);

            // No row yet: insert; a racing insert of the same key makes this call read the row and update it
            if (current is null)
            {
                context.ReviewUserStates.Add(new ReviewUserState(userId, projectId, reviewId, lastSeenCommentId, seenAt));
                try
                {
                    await context.SaveChangesAsync(cancellationToken);
                    return;
                }
                catch (DbUpdateException exception) when (IsDuplicateKey(exception))
                {
                    continue;
                }
            }

            // Never backwards: keep the higher comment identity and the later time
            var newCommentId = Math.Max(current.LastSeenCommentId, lastSeenCommentId);
            var newSeenAt = seenAt > current.LastSeenAt ? seenAt : current.LastSeenAt;
            if (newCommentId == current.LastSeenCommentId && newSeenAt == current.LastSeenAt)
            {
                return;
            }

            // Compare-and-set on the values that were read: a writer that changed the row in between wins, and
            // this call reads again
            var changed = await context.ReviewUserStates
                .Where(state => state.UserId == userId
                                && state.ProjectId == projectId
                                && state.ReviewId == reviewId
                                && state.LastSeenCommentId == current.LastSeenCommentId
                                && state.LastSeenAt == current.LastSeenAt)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(state => state.LastSeenCommentId, newCommentId)
                        .SetProperty(state => state.LastSeenAt, newSeenAt),
                    cancellationToken);
            if (changed > 0)
            {
                return;
            }
        }

        throw new InvalidOperationException("The read state could not be stored because it kept changing.");
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

    /// <summary>
    ///     Reports whether a failed save was a duplicate key.
    /// </summary>
    /// <param name="exception">The exception of the failed save.</param>
    /// <returns><c>true</c> when SQLite reported a primary key or unique constraint violation.</returns>
    private static bool IsDuplicateKey(DbUpdateException exception)
    {
        return exception.InnerException is SqliteException
        {
            SqliteExtendedErrorCode: SqlitePrimaryKeyViolation or SqliteUniqueViolation
        };
    }
}
