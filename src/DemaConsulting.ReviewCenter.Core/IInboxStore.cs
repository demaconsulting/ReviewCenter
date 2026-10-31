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
///     A review a user might have to act on, as the store finds it without loading its files.
/// </summary>
/// <remarks>
///     The record is a light projection: only what the inbox needs to decide whether the review is worth a closer
///     look. It is an immutable value holder and is safe to share between threads.
/// </remarks>
/// <param name="ProjectId">The identity of the owning project.</param>
/// <param name="ReviewId">The review identifier.</param>
/// <param name="Title">The review title.</param>
/// <param name="CreatedBy">The user name of the author of the review, or <c>null</c> when it has none.</param>
/// <param name="DueDate">The due date of the review, or <c>null</c> when it has none.</param>
public sealed record InboxCandidate(int ProjectId, string ReviewId, string Title, string? CreatedBy, DateOnly? DueDate);

/// <summary>
///     One review a user has seen up to a given comment, as handed to the store when several are marked at once.
/// </summary>
/// <param name="ProjectId">The identity of the owning project.</param>
/// <param name="ReviewId">The review identifier.</param>
/// <param name="LastSeenCommentId">The highest comment identity the user has seen; zero when the review has no comment.</param>
public sealed record InboxSeenMark(int ProjectId, string ReviewId, int LastSeenCommentId);

/// <summary>
///     Persists and reads the read state of the inbox and finds the reviews a user is involved in.
/// </summary>
/// <remarks>
///     The store keeps one <see cref="ReviewUserState"/> per user and review and nothing else: there is no
///     notification record, because the inbox is computed from the review data. Writes are not audited, because
///     read state is a personal convenience and not part of the review record. Implementations must be safe for
///     concurrent use; marking is a monotonic upsert, so parallel calls cannot move the state backwards.
/// </remarks>
public interface IInboxStore
{
    /// <summary>
    ///     Lists the reviews in which a user is the primary or alternate reviewer of a slot or is the author.
    /// </summary>
    /// <param name="userId">The identity of the user.</param>
    /// <param name="username">The user name of the user, compared with the author of a review without regard to case.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The reviews ordered by project and review identifier; empty when there are none.</returns>
    Task<IReadOnlyList<InboxCandidate>> ListInvolvedReviewsAsync(
        int userId,
        string username,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Lists all read-state rows of a user.
    /// </summary>
    /// <param name="userId">The identity of the user.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The rows of the user only; empty when the user has seen no review.</returns>
    Task<IReadOnlyList<ReviewUserState>> ListStatesAsync(int userId, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Reads the read-state row of a user for one review.
    /// </summary>
    /// <param name="userId">The identity of the user.</param>
    /// <param name="projectId">The identity of the owning project.</param>
    /// <param name="reviewId">The review identifier.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The row, or <c>null</c> when the user has not seen the review.</returns>
    Task<ReviewUserState?> GetStateAsync(
        int userId,
        int projectId,
        string reviewId,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Records that a user has seen a review, without ever moving the state backwards.
    /// </summary>
    /// <remarks>
    ///     The row is created when it does not exist. An existing row keeps the higher of the stored and the given
    ///     comment identity and the later of the stored and the given time, so a slow call that arrives late cannot
    ///     make seen comments unread again.
    /// </remarks>
    /// <param name="userId">The identity of the user.</param>
    /// <param name="projectId">The identity of the owning project.</param>
    /// <param name="reviewId">The review identifier.</param>
    /// <param name="lastSeenCommentId">The highest comment identity the user has seen.</param>
    /// <param name="seenAt">The time (UTC) to remember.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the state is stored.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="reviewId"/> is null or empty.</exception>
    Task MarkSeenAsync(
        int userId,
        int projectId,
        string reviewId,
        int lastSeenCommentId,
        DateTimeOffset seenAt,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Records that a user has seen several reviews, each as <see cref="MarkSeenAsync"/> does.
    /// </summary>
    /// <param name="userId">The identity of the user.</param>
    /// <param name="marks">The reviews to mark; must not be null.</param>
    /// <param name="seenAt">The time (UTC) to remember for every review.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when all states are stored.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="marks"/> is null.</exception>
    Task MarkSeenManyAsync(
        int userId,
        IReadOnlyList<InboxSeenMark> marks,
        DateTimeOffset seenAt,
        CancellationToken cancellationToken = default);
}
