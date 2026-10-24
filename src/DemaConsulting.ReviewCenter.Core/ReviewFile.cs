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
///     One file of a review: its path, its hash, and (when loaded) its exact bytes.
/// </summary>
/// <remarks>
///     The identity is (<see cref="ProjectId"/>, <see cref="ReviewId"/>, <see cref="Path"/>). The
///     size and hash are stored with the file so that listing a review never has to load the file
///     bytes (a review can hold 20 MiB). The type is not thread-safe.
/// </remarks>
public sealed class ReviewFile
{
    /// <summary>
    ///     Gets the identity of the project that owns the review.
    /// </summary>
    public int ProjectId { get; init; }

    /// <summary>
    ///     Gets the identifier of the owning review.
    /// </summary>
    public string ReviewId { get; init; } = string.Empty;

    /// <summary>
    ///     Gets the path of the file within the review, exactly as supplied.
    /// </summary>
    public string Path { get; init; } = string.Empty;

    /// <summary>
    ///     Gets the exact bytes of the file, kept verbatim.
    /// </summary>
    /// <remarks>
    ///     Empty (not null) on instances read back for display, which carry metadata only; use
    ///     <see cref="IReviewStore.GetFileContentAsync"/> to load the bytes of one file.
    /// </remarks>
    public byte[] Content { get; init; } = [];

    /// <summary>
    ///     Gets the size of the file in bytes.
    /// </summary>
    public long Size { get; init; }

    /// <summary>
    ///     Gets the SHA-256 hash of the bytes as 64 lowercase hexadecimal characters.
    /// </summary>
    public string Sha256 { get; init; } = string.Empty;
}
