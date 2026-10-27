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

namespace DemaConsulting.ReviewCenter.Web;

/// <summary>
///     Builds the stable addresses of a review, of one of its files (optionally at a line), of one of its threads and of
///     its signed PDF attestation.
/// </summary>
/// <remarks>
///     Every variable part (the review id and each segment of a file path) is escaped once with
///     <see cref="Uri.EscapeDataString(string)"/>, so spaces, <c>#</c>, <c>?</c>, <c>%</c>, <c>+</c>, <c>&amp;</c> and
///     non-ASCII characters survive; the slashes between the segments of a path stay as they are. The router
///     decodes the address exactly once when it fills the page parameters, so the page uses the decoded value as it
///     is and never decodes it again. The addresses are relative to the site root and safe as a sign-in return
///     address (they start with one <c>/</c>).
/// </remarks>
public static class ReviewLinks
{
    /// <summary>
    ///     The query value that selects the baseline numbering of a line link.
    /// </summary>
    public const string BaselineSide = "baseline";

    /// <summary>
    ///     The query value that selects the head numbering of a line link.
    /// </summary>
    public const string HeadSide = "head";

    /// <summary>
    ///     Builds the address of a review.
    /// </summary>
    /// <param name="projectId">The identity of the owning project.</param>
    /// <param name="reviewId">The review id. Must not be null.</param>
    /// <returns>For example <c>/projects/3/reviews/REV-1</c>.</returns>
    public static string ForReview(int projectId, string reviewId)
    {
        ArgumentNullException.ThrowIfNull(reviewId);
        return string.Create(
            CultureInfo.InvariantCulture, $"/projects/{projectId}/reviews/{Uri.EscapeDataString(reviewId)}");
    }

    /// <summary>
    ///     Builds the address that opens a file of a review, optionally at one line.
    /// </summary>
    /// <param name="projectId">The identity of the owning project.</param>
    /// <param name="reviewId">The review id. Must not be null.</param>
    /// <param name="path">The path of the file as stored in the review. Must not be null.</param>
    /// <param name="line">The 1-based line to scroll to and highlight, or <c>null</c> for the file only.</param>
    /// <param name="side">Which numbering the line uses; ignored without a line.</param>
    /// <returns>
    ///     For example <c>/projects/3/reviews/REV-1/files/src/a%20b.cs?line=7&amp;side=head</c>.
    /// </returns>
    public static string ForFile(int projectId, string reviewId, string path, int? line = null, ThreadSide side = ThreadSide.Head)
    {
        ArgumentNullException.ThrowIfNull(path);
        var escaped = string.Join('/', path.Split('/').Select(Uri.EscapeDataString));
        var address = $"{ForReview(projectId, reviewId)}/files/{escaped}";
        if (line is not { } number)
        {
            return address;
        }

        var sideText = side == ThreadSide.Baseline ? BaselineSide : HeadSide;
        return string.Create(CultureInfo.InvariantCulture, $"{address}?line={number}&side={sideText}");
    }

    /// <summary>
    ///     Builds the address of the signed PDF attestation of a review.
    /// </summary>
    /// <param name="projectId">The identity of the owning project.</param>
    /// <param name="reviewId">The review id. Must not be null.</param>
    /// <returns>For example <c>/projects/3/reviews/REV-1/attestation.pdf</c>.</returns>
    public static string ForAttestation(int projectId, string reviewId)
    {
        return $"{ForReview(projectId, reviewId)}/attestation.pdf";
    }

    /// <summary>
    ///     Builds the address that opens a thread at its anchor.
    /// </summary>
    /// <param name="projectId">The identity of the owning project.</param>
    /// <param name="reviewId">The review id. Must not be null.</param>
    /// <param name="threadId">The identity of the thread.</param>
    /// <returns>For example <c>/projects/3/reviews/REV-1/threads/12</c>.</returns>
    public static string ForThread(int projectId, string reviewId, int threadId)
    {
        return string.Create(CultureInfo.InvariantCulture, $"{ForReview(projectId, reviewId)}/threads/{threadId}");
    }
}
