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
    ///     Gets the content fingerprint of the file set, as computed by <see cref="Fingerprint"/>.
    /// </summary>
    public string ContentFingerprint { get; init; } = string.Empty;

    /// <summary>
    ///     Gets the files of the review.
    /// </summary>
    public List<ReviewFile> Files { get; init; } = [];
}
