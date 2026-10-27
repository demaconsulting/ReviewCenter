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

namespace DemaConsulting.ReviewCenter.Api;

/// <summary>
///     A project as the API shows it.
/// </summary>
/// <param name="Id">The project number.</param>
/// <param name="Name">The project name.</param>
/// <param name="Description">The project description; empty when there is none.</param>
/// <param name="Created">When the project was created.</param>
internal sealed record ProjectDto(int Id, string Name, string Description, DateTimeOffset Created);

/// <summary>
///     A review in a list, without its details.
/// </summary>
/// <param name="Id">The review identifier, unique within its project.</param>
/// <param name="Title">The review title.</param>
/// <param name="State">The life-cycle state of the review, for example <c>Draft</c>.</param>
/// <param name="Created">When the review was created.</param>
/// <param name="ContentFingerprint">The fingerprint of the reviewed content.</param>
/// <param name="CreatedBy">The user name of the creator, or <c>null</c> when it was not recorded.</param>
/// <param name="CreatedViaApi"><c>true</c> when the review was created with an API token.</param>
/// <param name="IsFromRepository"><c>true</c> when the review was created from a git repository, <c>false</c> for an upload.</param>
internal sealed record ReviewSummaryDto(
    string Id,
    string Title,
    string State,
    DateTimeOffset Created,
    string ContentFingerprint,
    string? CreatedBy,
    bool CreatedViaApi,
    bool IsFromRepository);

/// <summary>
///     A reviewer slot of a review.
/// </summary>
/// <param name="Id">The identity of the slot.</param>
/// <param name="Number">The position of the slot among the slots of the review, starting at 1.</param>
/// <param name="Primary">The user name of the primary reviewer.</param>
/// <param name="Alternate">The user name of the alternate reviewer, or <c>null</c> when the slot has none.</param>
/// <param name="State">Whether the slot holds a signature of the current record: <c>NotSigned</c>, <c>Signed</c> or <c>OutOfDate</c>.</param>
/// <param name="SignedBy">The display name of the signer of the current signature, or <c>null</c> when the slot is not signed.</param>
/// <param name="SignedAt">The time of the current signature, or <c>null</c> when the slot is not signed.</param>
internal sealed record SlotDto(
    int Id,
    int Number,
    string Primary,
    string? Alternate,
    string State,
    string? SignedBy,
    DateTimeOffset? SignedAt);

/// <summary>
///     The counts that summarize the progress of a review.
/// </summary>
/// <param name="Files">The number of files in the review.</param>
/// <param name="OpenThreads">The number of comment threads that are open.</param>
/// <param name="ResolvedThreads">The number of comment threads that are resolved.</param>
/// <param name="ChecklistItems">The number of checklist items that apply to the review.</param>
/// <param name="ChecklistAnswered">The number of applicable checklist items that have an answer.</param>
/// <param name="ChecklistFailed">The number of applicable checklist items answered as failed.</param>
internal sealed record ReviewCountsDto(
    int Files,
    int OpenThreads,
    int ResolvedThreads,
    int ChecklistItems,
    int ChecklistAnswered,
    int ChecklistFailed);

/// <summary>
///     A review with its details, approval status and counts.
/// </summary>
/// <param name="Id">The review identifier, unique within its project.</param>
/// <param name="Title">The review title.</param>
/// <param name="State">The life-cycle state of the review, for example <c>Draft</c>.</param>
/// <param name="Created">When the review was created.</param>
/// <param name="ContentFingerprint">The fingerprint of the reviewed content.</param>
/// <param name="CreatedBy">The user name of the creator, or <c>null</c> when it was not recorded.</param>
/// <param name="CreatedViaApi"><c>true</c> when the review was created with an API token.</param>
/// <param name="IsFromRepository"><c>true</c> when the review was created from a git repository, <c>false</c> for an upload.</param>
/// <param name="RepositoryName">The name of the registered repository, or <c>null</c> for an upload.</param>
/// <param name="BaselineSha">The baseline commit, or <c>null</c> when there is none.</param>
/// <param name="HeadSha">The head commit, or <c>null</c> for an upload.</param>
/// <param name="Selection">The file selection patterns that were used, or <c>null</c> when none.</param>
/// <param name="ReviewSetId">The review-set the review was planned from, or <c>null</c>.</param>
/// <param name="Approved"><c>true</c> when every reviewer slot has signed the current record.</param>
/// <param name="ApprovalDigest">The digest of the review record as it is now.</param>
/// <param name="Slots">The reviewer slots in creation order.</param>
/// <param name="Counts">The progress counts.</param>
/// <param name="Blockers">The plain-language reasons the review cannot be signed yet; empty when it can be.</param>
/// <param name="NextStep">One short sentence that words what to do next.</param>
internal sealed record ReviewDetailDto(
    string Id,
    string Title,
    string State,
    DateTimeOffset Created,
    string ContentFingerprint,
    string? CreatedBy,
    bool CreatedViaApi,
    bool IsFromRepository,
    string? RepositoryName,
    string? BaselineSha,
    string? HeadSha,
    string? Selection,
    string? ReviewSetId,
    bool Approved,
    string ApprovalDigest,
    IReadOnlyList<SlotDto> Slots,
    ReviewCountsDto Counts,
    IReadOnlyList<string> Blockers,
    string NextStep);

/// <summary>
///     One file of an upload.
/// </summary>
/// <param name="Path">The path of the file within the review, with forward slashes.</param>
/// <param name="ContentBase64">The content of the file, Base64 encoded.</param>
internal sealed record UploadFileDto(string Path, string ContentBase64);

/// <summary>
///     A git repository source of a review.
/// </summary>
/// <param name="RepositoryId">The identity of an enabled registered repository.</param>
/// <param name="HeadRevision">The head revision: a branch, tag or commit.</param>
/// <param name="BaselineRevision">The baseline revision, or <c>null</c> to review every file at the head as added.</param>
/// <param name="Selection">The file selection patterns, one per line, or <c>null</c> to review every changed file.</param>
internal sealed record RepositorySourceDto(
    int RepositoryId,
    string HeadRevision,
    string? BaselineRevision,
    string? Selection);

/// <summary>
///     The request to create a review: either uploaded files or a repository source, never both.
/// </summary>
/// <param name="Id">The review identifier: letters, digits, '.', '_' and '-', starting with a letter or digit.</param>
/// <param name="Title">The review title.</param>
/// <param name="Files">The files to review; set this or <paramref name="Repository"/>.</param>
/// <param name="Repository">The repository source to review; set this or <paramref name="Files"/>.</param>
internal sealed record CreateReviewRequest(
    string? Id,
    string? Title,
    IReadOnlyList<UploadFileDto>? Files,
    RepositorySourceDto? Repository);

/// <summary>
///     A repository entry that was left out of a review.
/// </summary>
/// <param name="Path">The path of the entry.</param>
/// <param name="Reason">Why the entry was skipped.</param>
internal sealed record SkippedFileDto(string Path, string Reason);

/// <summary>
///     The answer to creating a review.
/// </summary>
/// <param name="Review">The review that was created.</param>
/// <param name="Skipped">The repository entries that were left out; empty for an upload.</param>
internal sealed record CreatedReviewDto(ReviewDetailDto Review, IReadOnlyList<SkippedFileDto> Skipped);
