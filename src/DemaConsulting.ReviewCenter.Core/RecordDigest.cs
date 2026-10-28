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
///     The digest is SHA-256 over a canonical text of the record, written as lowercase hexadecimal. The format
///     is private to ReviewCenter (no other tool is expected to reproduce it) and is versioned from v1 at the
///     first release; any later change to it must bump the version. The text is built so that it never depends
///     on storage row identities or on the order rows happen to be read in, and so that two different records
///     can never produce the same text:
///     <list type="number">
///         <item>a version header line (<c>ReviewCenter record v1</c>);</item>
///         <item>the content fingerprint;</item>
///         <item>the source of the files: <c>source upload</c> for an upload, otherwise
///               <c>source git</c> with the repository identity, the baseline commit (or <c>-</c>), the head commit,
///               the selection text and the review-set identifier (or <c>-</c>), followed by a <c>changes</c> line
///               and one <c>change</c> line per changed
///               file, sorted by path: path, kind, baseline hash (or <c>-</c>) and head hash (or <c>-</c>). The
///               repository name is not covered (it is a display snapshot, and renaming a repository must not
///               invalidate a signature);</item>
///         <item>the frozen checklist the review was created with: a <c>checklist</c> line with its version
///               (<c>-</c> when the review has none), then each section sorted by id (<c>section</c> line: id and
///               title) followed by its checks sorted by id (<c>item</c> line: id and question wording), so a
///               signature binds to the exact questions that were answered;</item>
///         <item>every stored checklist answer, sorted by check id: id, outcome, justification, and who answered
///               (display name and user name). The moment of answering is deliberately not covered, so answering
///               the same value again as the same person does not invalidate a signature;</item>
///         <item>the sections that do not apply, sorted by section id: id and reason;</item>
///         <item>every thread rendered to one block (anchor with its side, state, creation time, and its comments in posting
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

        // The source comes straight after the fingerprint so a signature binds to where the files came from
        AddSource(lines, record.Source);

        // The questions come before the answers so a signature binds to what was asked, not only to the replies
        AddChecklist(lines, record.Checklist);

        // Answers, in check id order, with who answered; only the time is left out on purpose
        lines.Add("answers");
        foreach (var answer in record.Answers.OrderBy(a => a.ItemId, StringComparer.Ordinal))
        {
            lines.Add(
                $"answer {Text(answer.ItemId)} {Text(answer.Outcome.ToString())} {Text(answer.Justification)} " +
                $"{Text(answer.AnsweredBy)} {Text(answer.AnsweredByUsername)}");
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
    ///     Writes the checklist block of the canonical text: the wording of every question the reviewers answered.
    /// </summary>
    /// <remarks>
    ///     The block is read from the definition frozen with the review, so it cannot change after the review
    ///     is created; covering it makes the digest say which questions the answers belong to. Sections and
    ///     checks are sorted by identifier so the order of the definition does not matter. The applicability
    ///     hint of a section is display help and is not covered.
    /// </remarks>
    /// <param name="lines">The lines written so far.</param>
    /// <param name="checklist">The frozen checklist, or <c>null</c> when the review has none.</param>
    /// <exception cref="System.Text.Json.JsonException">
    ///     Thrown when the stored definition is not valid JSON or not a complete definition
    ///     (<see cref="ChecklistDefinition.FromJson"/> rejects both); the store only holds text written by
    ///     <see cref="ChecklistDefinition.ToJson"/>, so this signals corrupted data and must not be hidden.
    /// </exception>
    private static void AddChecklist(List<string> lines, ReviewChecklist? checklist)
    {
        if (checklist is null)
        {
            lines.Add("checklist -");
            return;
        }

        var definition = checklist.Parse();
        lines.Add($"checklist {Text(definition.Version)}");
        foreach (var section in definition.Sections.OrderBy(s => s.Id, StringComparer.Ordinal))
        {
            lines.Add($"section {Text(section.Id)} {Text(section.Title)}");
            foreach (var item in section.Items.OrderBy(i => i.Id, StringComparer.Ordinal))
            {
                lines.Add($"item {Text(item.Id)} {Text(item.Text)}");
            }
        }
    }

    /// <summary>
    ///     Writes the source block of the canonical text.
    /// </summary>
    /// <param name="lines">The lines written so far.</param>
    /// <param name="source">The source of the review's files.</param>
    private static void AddSource(List<string> lines, ReviewSource source)
    {
        if (source.IsUpload)
        {
            lines.Add("source upload");
            return;
        }

        lines.Add(
            $"source git {Number(source.RepositoryId)} {Text(source.BaselineSha)} {Text(source.HeadSha)} {Text(source.Selection)} {Text(source.ReviewSetId)}");

        // One line per changed file, sorted by path, so the order of storage does not matter
        lines.Add("changes");
        foreach (var change in source.Changes.OrderBy(c => c.Path, StringComparer.Ordinal))
        {
            lines.Add(
                $"change {Text(change.Path)} {Text(change.Kind.ToString())} {Text(change.BaselineSha256)} {Text(change.HeadSha256)}");
        }
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
            $"thread {Text(thread.FilePath)} {Text(thread.Side.ToString())} {Number(thread.StartLine)} {Number(thread.EndLine)} " +
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
