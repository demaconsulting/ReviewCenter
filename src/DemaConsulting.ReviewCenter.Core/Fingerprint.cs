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

using System.Security.Cryptography;
using System.Text;

namespace DemaConsulting.ReviewCenter.Core;

/// <summary>
///     Computes the content fingerprint of a set of files, using exactly the algorithm of the
///     ReviewMark tool.
/// </summary>
/// <remarks>
///     ReviewCenter fingerprints must equal the ReviewMark fingerprints of the same files so that a
///     review recorded here can be matched with a review plan elsewhere. The algorithm is:
///     (1) hash the raw bytes of each file with SHA-256 and render the hash as lowercase hex;
///     (2) sort the hex strings with an ordinal comparison; (3) concatenate them with no separator;
///     (4) hash the UTF-8 bytes of the concatenation with SHA-256 and render it as lowercase hex.
///     File paths take no part, so renaming or moving files does not change the fingerprint, and
///     files with identical content are not collapsed. Line endings are not normalized: the bytes
///     are hashed as they are. The class is stateless and thread-safe.
/// </remarks>
public static class Fingerprint
{
    /// <summary>
    ///     Computes the SHA-256 hash of the raw bytes of one file.
    /// </summary>
    /// <param name="content">The exact file bytes; may be empty.</param>
    /// <returns>The hash as 64 lowercase hexadecimal characters.</returns>
    /// <remarks>
    ///     The per-file hash is shown to reviewers next to each file, and it is the input of
    ///     <see cref="Compute(IEnumerable{string})"/>. Pure function with no side effects.
    /// </remarks>
    public static string ComputeFileHash(ReadOnlySpan<byte> content)
    {
        // Hash the bytes verbatim; any text decoding or line-ending change would break ReviewMark equality
        return Convert.ToHexStringLower(SHA256.HashData(content));
    }

    /// <summary>
    ///     Computes the fingerprint of a file set from the per-file hashes.
    /// </summary>
    /// <param name="fileHashes">
    ///     The lowercase hexadecimal SHA-256 hash of each file, in any order. Must not be null; the
    ///     values are used as given and are not validated.
    /// </param>
    /// <returns>The fingerprint as 64 lowercase hexadecimal characters.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="fileHashes"/> is null.</exception>
    /// <remarks>
    ///     Ordering the hashes makes the result independent of the order in which files were
    ///     supplied. Pure function with no side effects.
    /// </remarks>
    public static string Compute(IEnumerable<string> fileHashes)
    {
        ArgumentNullException.ThrowIfNull(fileHashes);

        // Order by ordinal comparison, exactly as ReviewMark does; culture-aware ordering would differ
        var ordered = fileHashes.ToList();
        ordered.Sort(StringComparer.Ordinal);

        // Concatenate without a separator and hash the UTF-8 bytes of the result
        var combined = string.Concat(ordered);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(combined)));
    }

    /// <summary>
    ///     Computes the fingerprint of a file set directly from the file contents.
    /// </summary>
    /// <param name="contents">The exact bytes of each file, in any order. Must not be null or contain nulls.</param>
    /// <returns>The fingerprint as 64 lowercase hexadecimal characters.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="contents"/> is null.</exception>
    /// <remarks>
    ///     Convenience composition of <see cref="ComputeFileHash"/> and
    ///     <see cref="Compute(IEnumerable{string})"/> for callers that do not need the per-file hashes.
    /// </remarks>
    public static string Compute(IEnumerable<byte[]> contents)
    {
        ArgumentNullException.ThrowIfNull(contents);
        return Compute(contents.Select(content => ComputeFileHash(content)));
    }
}
