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
using System.Security.Cryptography;
using System.Text;

namespace DemaConsulting.ReviewCenter.Core;

/// <summary>
///     Computes the digest of a review record: the value a signature binds itself to.
/// </summary>
/// <remarks>
///     The digest is SHA-256 over a canonical text of the record, written as lowercase hexadecimal. The text
///     is built so that it never depends on storage row identities or on the order rows happen to be read
///     in, and so that two different records can never produce the same text:
///     <list type="number">
///         <item>a version header line (<c>ReviewCenter record v1</c>; any change to this format must bump it);</item>
///         <item>the content fingerprint;</item>
///         <item>every stored checklist answer, sorted by check id: id, outcome, justification
///               (who answered and when are deliberately not covered, so answering the same value again does not
///               invalidate a signature);</item>
///         <item>the sections that do not apply, sorted by section id: id and reason;</item>
///         <item>every thread rendered to one block (anchor, state, creation time, and its comments in posting
///               order with author display name, author user name, text and time); the blocks are sorted, so
///               the order of threads does not matter, while the order of comments does.</item>
///     </list>
///     Strings are written as <c>{length}:{text}</c> (length in UTF-16 code units) and absent values as
///     <c>-</c>, so no text can imitate a delimiter; numbers use the invariant culture and times are written as
///     UTC ticks. All sorting is ordinal. The class is stateless and thread-safe.
/// </remarks>
public static class RecordDigest
{
    /// <summary>
    ///     The first line of the canonical text; names the format version.
    /// </summary>
    private const string Header = "ReviewCenter record v1";

    /// <summary>
    ///     Builds the canonical text of a record.
    /// </summary>
    /// <param name="record">The record. Must not be null.</param>
    /// <returns>The canonical text; lines are separated by <c>\n</c>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="record"/> is null.</exception>
    public static string Canonicalize(ReviewRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        var lines = new List<string>
        {
            Header,
            "fingerprint " + Text(record.ContentFingerprint)
        };

        // Answers, in check id order; the identity of the person and the time are left out on purpose
        lines.Add("answers");
        foreach (var answer in record.Answers.OrderBy(a => a.ItemId, StringComparer.Ordinal))
        {
            lines.Add($"answer {Text(answer.ItemId)} {Text(answer.Outcome.ToString())} {Text(answer.Justification)}");
        }

        // Only the sections that do not apply matter; "applies" is the default and carries no reason
        lines.Add("sections");
        foreach (var section in record.SectionStates.Where(s => !s.Applicable).OrderBy(s => s.SectionId, StringComparer.Ordinal))
        {
            lines.Add($"not-applicable {Text(section.SectionId)} {Text(section.Reason)}");
        }

        // Each thread becomes one block; sorting the blocks removes any dependence on row order or identity
        lines.Add("threads");
        var blocks = record.Threads.Select(ThreadBlock).ToList();
        blocks.Sort(StringComparer.Ordinal);
        lines.AddRange(blocks);

        return string.Join('\n', lines);
    }

    /// <summary>
    ///     Computes the digest of a record.
    /// </summary>
    /// <param name="record">The record. Must not be null.</param>
    /// <returns>The SHA-256 of the UTF-8 bytes of <see cref="Canonicalize"/>, as 64 lowercase hexadecimal characters.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="record"/> is null.</exception>
    public static string Compute(ReviewRecord record)
    {
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Canonicalize(record))));
    }

    /// <summary>
    ///     Renders one thread with its comments to a block of lines.
    /// </summary>
    /// <param name="thread">The thread.</param>
    /// <returns>The block; the first line is the thread, each following line is one comment in posting order.</returns>
    private static string ThreadBlock(ReviewThread thread)
    {
        var lines = new List<string>
        {
            $"thread {Text(thread.FilePath)} {Number(thread.StartLine)} {Number(thread.EndLine)} " +
            $"{Text(thread.State.ToString())} {thread.Created.UtcTicks.ToString(CultureInfo.InvariantCulture)}"
        };

        // Comment order is meaningful, so it is kept exactly as stored (the store orders by identity)
        lines.AddRange(thread.Comments.Select(comment =>
            $"comment {Text(comment.Author)} {Text(comment.AuthorUsername)} {Text(comment.Text)} " +
            comment.Created.UtcTicks.ToString(CultureInfo.InvariantCulture)));
        return string.Join('\n', lines);
    }

    /// <summary>
    ///     Writes a string length-prefixed, or <c>-</c> for null.
    /// </summary>
    /// <param name="value">The value, possibly null.</param>
    /// <returns>For example <c>3:abc</c>.</returns>
    private static string Text(string? value)
    {
        return value is null ? "-" : $"{value.Length.ToString(CultureInfo.InvariantCulture)}:{value}";
    }

    /// <summary>
    ///     Writes an optional number, or <c>-</c> for null.
    /// </summary>
    /// <param name="value">The value, possibly null.</param>
    /// <returns>The number in the invariant culture.</returns>
    private static string Number(int? value)
    {
        return value is null ? "-" : value.Value.ToString(CultureInfo.InvariantCulture);
    }
}
