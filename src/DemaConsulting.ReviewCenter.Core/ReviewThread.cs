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
///     A discussion about one file of a review, anchored to the whole file or to a range of lines.
/// </summary>
/// <remarks>
///     A thread always has at least one comment: the first comment is created together with the
///     thread. The comments are immutable in what they say; what changes after creation is the
///     <see cref="State"/> and which comments are part of the signed report (<see cref="ReviewComment.IncludeInReport"/>),
///     which decides the <see cref="Kind"/>. Instances are created by <see cref="ThreadService"/>, which enforces
///     the rules, and are populated by the storage layer. The type is not thread-safe.
/// </remarks>
public sealed class ReviewThread
{
    /// <summary>
    ///     Gets the identity assigned by the database; it grows with creation order.
    /// </summary>
    public int Id { get; init; }

    /// <summary>
    ///     Gets the identity of the project that owns the review.
    /// </summary>
    public int ProjectId { get; init; }

    /// <summary>
    ///     Gets the identifier of the review the thread belongs to.
    /// </summary>
    public string ReviewId { get; init; } = string.Empty;

    /// <summary>
    ///     Gets the path of the file the thread is anchored to (see <see cref="ReviewFile.Path"/>).
    /// </summary>
    public string FilePath { get; init; } = string.Empty;

    /// <summary>
    ///     Gets the version of the file the line numbers refer to; <see cref="ThreadSide.Head"/> for a whole-file thread.
    /// </summary>
    public ThreadSide Side { get; init; } = ThreadSide.Head;

    /// <summary>
    ///     Gets the first line of the anchor (1-based), or <c>null</c> when the thread is anchored to the whole file.
    /// </summary>
    public int? StartLine { get; init; }

    /// <summary>
    ///     Gets the last line of the anchor (1-based, inclusive, not before <see cref="StartLine"/>), or
    ///     <c>null</c> exactly when <see cref="StartLine"/> is <c>null</c>.
    /// </summary>
    public int? EndLine { get; init; }

    /// <summary>
    ///     Gets whether the thread is open or resolved; <see cref="ReviewThreadState.Open"/> when created.
    /// </summary>
    public ReviewThreadState State { get; init; } = ReviewThreadState.Open;

    /// <summary>
    ///     Gets the moment the thread was started (UTC).
    /// </summary>
    public DateTimeOffset Created { get; init; }

    /// <summary>
    ///     Gets the identifier of the earlier review this thread was carried over from when the review was created as a
    ///     follow-up, or <c>null</c> for a thread started in this review. Part of the record digest.
    /// </summary>
    public string? CarriedFromReviewId { get; init; }

    /// <summary>
    ///     Gets the identity the thread had in <see cref="CarriedFromReviewId"/>, or <c>null</c> for a thread started in
    ///     this review. It is not part of the record digest (identities never are).
    /// </summary>
    public int? CarriedFromThreadId { get; init; }

    /// <summary>
    ///     Gets the comments of the thread in posting order; never empty for a stored thread.
    /// </summary>
    public List<ReviewComment> Comments { get; init; } = [];

    /// <summary>
    ///     Gets whether the thread is chat or a finding of the signed report, derived from its starting comment.
    /// </summary>
    /// <remarks>
    ///     <see cref="ThreadKind.Finding"/> exactly when the first comment (the comments are kept in posting order) is
    ///     included in the report; <see cref="ThreadKind.Question"/> otherwise, also for a thread without comments.
    ///     Derived, not stored.
    /// </remarks>
    public ThreadKind Kind =>
        Comments.Count > 0 && Comments[0].IncludeInReport ? ThreadKind.Finding : ThreadKind.Question;

    /// <summary>
    ///     Gets the time of the latest comment, or <see cref="Created"/> when the thread has no comments.
    /// </summary>
    /// <remarks>Derived from <see cref="Comments"/>; it is not stored.</remarks>
    public DateTimeOffset LastActivity =>
        Comments.Count == 0 ? Created : Comments.Max(comment => comment.Created);
}
