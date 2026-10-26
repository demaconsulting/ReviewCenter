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
    ///     The longest stored comment author name (the display name at the time), in characters.
    /// </summary>
    public const int MaxAuthorLength = 100;

    /// <summary>
    ///     The longest accepted comment text, in characters.
    /// </summary>
    public const int MaxCommentLength = 4000;

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
    ///     The shortest accepted password, in characters.
    /// </summary>
    public const int MinPasswordLength = 12;

    /// <summary>
    ///     The longest accepted password, in characters; the limit keeps hashing time bounded.
    /// </summary>
    public const int MaxPasswordLength = 128;
}
