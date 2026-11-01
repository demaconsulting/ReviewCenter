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
///     The size and length limits the domain enforces on projects, reviews, comments and users.
/// </summary>
/// <remarks>
///     The limits are named constants in one place so the services, the database mapping and the
///     web pages (which must enforce the upload limits before the bytes reach the service) all use
///     the same numbers. They protect the single server against unbounded uploads; they are not
///     per-user quotas.
/// </remarks>
public static class ReviewLimits
{
    /// <summary>
    ///     The largest accepted size of one file, in bytes (2 MiB).
    /// </summary>
    public const long MaxFileSizeBytes = 2L * 1024 * 1024;

    /// <summary>
    ///     The largest accepted number of files in one review.
    /// </summary>
    public const int MaxFileCount = 200;

    /// <summary>
    ///     The largest accepted combined size of all files of one review, in bytes (20 MiB).
    /// </summary>
    public const long MaxTotalSizeBytes = 20L * 1024 * 1024;

    /// <summary>
    ///     The largest number of lines per side that the line diff compares; larger files are reported as too large.
    /// </summary>
    public const int MaxDiffLines = 10_000;

    /// <summary>
    ///     The largest number of changed lines (added plus removed) that the line diff computes; more are
    ///     reported as too large. It bounds the work of the diff to a few tens of millions of steps.
    /// </summary>
    public const int MaxDiffEdits = 2_000;

    /// <summary>
    ///     The longest accepted file path, in characters.
    /// </summary>
    public const int MaxPathLength = 400;

    /// <summary>
    ///     The longest accepted file selection text of a repository review, in characters.
    /// </summary>
    public const int MaxSelectionLength = 2000;

    /// <summary>
    ///     The longest accepted project name, in characters.
    /// </summary>
    public const int MaxProjectNameLength = 100;

    /// <summary>
    ///     The longest accepted project description, in characters.
    /// </summary>
    public const int MaxProjectDescriptionLength = 1000;

    /// <summary>
    ///     The longest accepted review identifier, in characters.
    /// </summary>
    public const int MaxReviewIdLength = 100;

    /// <summary>
    ///     The longest accepted review title, in characters.
    /// </summary>
    public const int MaxReviewTitleLength = 200;

    /// <summary>
    ///     The longest accepted review-set identifier of a repository's <c>.reviewmark.yaml</c>, in characters.
    /// </summary>
    public const int MaxReviewSetIdLength = 100;

    /// <summary>
    ///     The longest accepted review-set title, in characters.
    /// </summary>
    public const int MaxReviewSetTitleLength = 200;

    /// <summary>
    ///     The largest accepted <c>.reviewmark.yaml</c> file, in bytes (256 KiB).
    /// </summary>
    public const long MaxReviewSetFileBytes = 256L * 1024;

    /// <summary>
    ///     The largest accepted number of review-sets in one <c>.reviewmark.yaml</c>.
    /// </summary>
    public const int MaxReviewSets = 500;

    /// <summary>
    ///     The largest accepted checklist definition (the JSON text of a project's checklist), in bytes (256 KiB).
    /// </summary>
    public const long MaxChecklistBytes = 256L * 1024;

    /// <summary>
    ///     The largest accepted number of sections in one checklist definition.
    /// </summary>
    public const int MaxChecklistSections = 50;

    /// <summary>
    ///     The largest accepted number of checks (items) in one checklist definition, over all its sections.
    /// </summary>
    public const int MaxChecklistItems = 500;

    /// <summary>
    ///     The longest accepted checklist version label, in characters; it is the length of the stored column.
    /// </summary>
    public const int MaxChecklistVersionLength = 64;

    /// <summary>
    ///     The longest accepted section or check identifier of a checklist definition, in characters; it is the
    ///     length of the stored answer and section-state columns.
    /// </summary>
    public const int MaxChecklistIdLength = 32;

    /// <summary>
    ///     The longest accepted section title of a checklist definition, in characters.
    /// </summary>
    public const int MaxChecklistTitleLength = 200;

    /// <summary>
    ///     The longest accepted wording of one check of a checklist definition, in characters.
    /// </summary>
    public const int MaxChecklistItemTextLength = 1000;

    /// <summary>
    ///     The longest accepted section applicability hint of a checklist definition, in characters.
    /// </summary>
    public const int MaxChecklistHintLength = 500;

    /// <summary>
    ///     The longest stored comment author name (the display name at the time), in characters.
    /// </summary>
    public const int MaxAuthorLength = 100;

    /// <summary>
    ///     The longest accepted comment text, in characters.
    /// </summary>
    public const int MaxCommentLength = 4000;

    /// <summary>
    ///     The longest accepted review conclusion text (the free text the reviewers write as the conclusion of a
    ///     review's record, not the approved or rejected conclusion), in characters; it is the length of the stored column.
    /// </summary>
    public const int MaxConclusionTextLength = 4000;

    /// <summary>
    ///     The longest accepted reason for reopening a concluded (approved or rejected) review, in characters; it is half of
    ///     <see cref="AuditText.MaxDetailLength"/> so the reason and the previous digest fit in one audit entry.
    /// </summary>
    public const int MaxReopenReasonLength = 250;

    /// <summary>
    ///     The longest accepted checklist justification or not-applicable reason, in characters.
    /// </summary>
    public const int MaxJustificationLength = 2000;

    /// <summary>
    ///     The shortest accepted user name, in characters.
    /// </summary>
    public const int MinUsernameLength = 3;

    /// <summary>
    ///     The longest accepted user name, in characters.
    /// </summary>
    public const int MaxUsernameLength = 50;

    /// <summary>
    ///     The longest accepted user display name, in characters.
    /// </summary>
    public const int MaxDisplayNameLength = 100;

    /// <summary>
    ///     The longest accepted issuer of an external identity provider, in characters.
    /// </summary>
    public const int MaxExternalIssuerLength = 512;

    /// <summary>
    ///     The longest accepted subject (the identifier of a person at an external identity provider), in characters;
    ///     the OpenID Connect specification allows at most 255.
    /// </summary>
    public const int MaxExternalSubjectLength = 256;

    /// <summary>
    ///     The shortest accepted password, in characters.
    /// </summary>
    public const int MinPasswordLength = 12;

    /// <summary>
    ///     The longest accepted password, in characters; the limit keeps hashing time bounded.
    /// </summary>
    public const int MaxPasswordLength = 128;

    /// <summary>
    ///     The largest number of active (not revoked, not expired) personal access tokens of one user.
    /// </summary>
    public const int MaxActiveTokensPerUser = 20;

    /// <summary>
    ///     The longest accepted personal access token name, in characters.
    /// </summary>
    public const int MaxTokenNameLength = 100;

    /// <summary>
    ///     The lifetimes, in days, a personal access token can be created with; a token never lasts forever.
    /// </summary>
    public static readonly IReadOnlyList<int> TokenExpiryChoicesInDays = [30, 90, 365];

    /// <summary>
    ///     The lifetime, in days, that is preselected when a token is created.
    /// </summary>
    public const int DefaultTokenExpiryDays = 90;

    /// <summary>
    ///     How often the "last used" moment of a token is written at most, so busy automation does not write on every request.
    /// </summary>
    public static readonly TimeSpan TokenLastUsedGranularity = TimeSpan.FromMinutes(5);
}
