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
///     A review: a titled, fingerprinted set of files within a project.
/// </summary>
/// <remarks>
///     The identity is the pair (<see cref="ProjectId"/>, <see cref="Id"/>), so the same review id
///     can be used in different projects. Instances are created by <see cref="ReviewService"/>,
///     which enforces the rules, and are populated by the storage layer. Reviews read back from the
///     store for display carry file metadata only (see <see cref="ReviewFile.Content"/>). The type
///     is not thread-safe.
/// </remarks>
public sealed class Review
{
    /// <summary>
    ///     Gets the identity of the owning project.
    /// </summary>
    public int ProjectId { get; init; }

    /// <summary>
    ///     Gets the review identifier, unique within the project; it appears in page addresses, so
    ///     it consists of letters, digits, dots, underscores and hyphens and starts with a letter or digit.
    /// </summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>
    ///     Gets the human-readable title; never empty, at most <see cref="ReviewLimits.MaxReviewTitleLength"/> characters.
    /// </summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>
    ///     Gets the life-cycle state of the review.
    /// </summary>
    public ReviewState State { get; init; } = ReviewState.Draft;

    /// <summary>
    ///     Gets the moment the review was created.
    /// </summary>
    public DateTimeOffset Created { get; init; }

    /// <summary>
    ///     Gets the user name of the user who created the review, or <c>null</c> for a review created before this was recorded.
    /// </summary>
    /// <remarks>
    ///     Provenance metadata only; it is not part of the record digest, because the tamper-evident audit trail
    ///     carries the same fact.
    /// </remarks>
    public string? CreatedBy { get; init; }

    /// <summary>
    ///     Gets a value indicating whether the review was created through the API with a personal access token.
    /// </summary>
    public bool CreatedViaApi { get; init; }

    /// <summary>
    ///     Gets the optional due date of the review: a calendar day that is interpreted in UTC, or <c>null</c> when
    ///     none is set.
    /// </summary>
    /// <remarks>
    ///     A planning attribute, not reviewed content: it is not part of the record digest, so changing it can never
    ///     invalidate a signature. A review is overdue when the day is before today (UTC) and the review is not approved.
    /// </remarks>
    public DateOnly? DueDate { get; init; }

    /// <summary>
    ///     Gets the identifier of the earlier review of the same project that this review follows up, or <c>null</c>
    ///     when it is not a follow-up; at most <see cref="ReviewLimits.MaxReviewIdLength"/> characters.
    /// </summary>
    /// <remarks>
    ///     Always the immediate source (a chain of follow-ups points one step back each time). It is a plain value, not a
    ///     foreign key, and the source is in the same project by rule. It is part of the record digest (see
    ///     <see cref="RecordDigest"/>), so a signature binds to what the review was a follow-up of.
    /// </remarks>
    public string? FollowsReviewId { get; init; }

    /// <summary>
    ///     Gets how many checklist answers were carried over from <see cref="FollowsReviewId"/> when the review was
    ///     created; zero for a review that is not a follow-up. A display and audit value; the carried answers themselves
    ///     are marked on the answers (<see cref="ChecklistAnswer.CarriedFromReviewId"/>).
    /// </summary>
    public int CarriedAnswerCount { get; init; }

    /// <summary>
    ///     Gets how many open threads were carried over from <see cref="FollowsReviewId"/> when the review was created;
    ///     zero for a review that is not a follow-up. A display and audit value; see <see cref="ReviewThread.CarriedFromReviewId"/>.
    /// </summary>
    public int CarriedThreadCount { get; init; }

    /// <summary>
    ///     Gets the rows carried over from the followed review, set only when the review is created so that the store
    ///     writes them in the same transaction; reviews read back from the store do not carry it.
    /// </summary>
    public ReviewCarryOver? CarryOver { get; init; }

    /// <summary>
    ///     Gets the content fingerprint of the file set, as computed by <see cref="Fingerprint"/>.
    /// </summary>
    public string ContentFingerprint { get; init; } = string.Empty;

    /// <summary>
    ///     Gets the identity of the registered repository the review was created from, or <c>null</c> for an upload.
    /// </summary>
    public int? RepositoryId { get; init; }

    /// <summary>
    ///     Gets the name the repository had when the review was created (a display snapshot), or <c>null</c> for an upload.
    /// </summary>
    public string? RepositoryName { get; init; }

    /// <summary>
    ///     Gets the full commit identifier of the baseline revision, or <c>null</c> for an upload or a review of all head files.
    /// </summary>
    public string? BaselineSha { get; init; }

    /// <summary>
    ///     Gets the full commit identifier of the head revision, or <c>null</c> for an upload.
    /// </summary>
    public string? HeadSha { get; init; }

    /// <summary>
    ///     Gets the file selection text (normalized patterns, one per line) used to pick the changed files, or <c>null</c> for an upload.
    /// </summary>
    public string? Selection { get; init; }

    /// <summary>
    ///     Gets the identity of the review-set of the repository's <c>.reviewmark.yaml</c> this review was planned
    ///     from, or <c>null</c> for an upload or a review that was not planned from a review-set; at most
    ///     <see cref="ReviewLimits.MaxReviewSetIdLength"/> characters.
    /// </summary>
    public string? ReviewSetId { get; init; }

    /// <summary>
    ///     Gets a value indicating whether the review was created from a repository.
    /// </summary>
    public bool IsFromRepository => RepositoryId is not null;

    /// <summary>
    ///     Gets the files of the review.
    /// </summary>
    public List<ReviewFile> Files { get; init; } = [];

    /// <summary>
    ///     Gets the frozen checklist, set only when the review is created; reviews read back from the store do not
    ///     carry it (read it with <see cref="ChecklistService"/>).
    /// </summary>
    public ReviewChecklist? Checklist { get; init; }
}
