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
///     is private to ReviewCenter (no other tool is expected to reproduce it) and is versioned; this is v2, and
///     nothing of an earlier version was released. Any later change to it must bump the version. The text is built so that it never depends
///     on storage row identities or on the order rows happen to be read in, and so that two different records
///     can never produce the same text:
///     <list type="number">
///         <item>a version header line (<c>ReviewCenter record v2</c>);</item>
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
///         <item>the consolidated standing of the ballots (see <see cref="BallotConsolidation"/>; only submitted
///               ballots count, so a draft never changes the digest): a <c>checks</c> line, then one <c>check</c> line
///               per check of the sections that apply, sorted by id: id and <c>agreed</c> with the outcome, or
///               <c>disputed</c>, or <c>pending</c>;</item>
///         <item>a <c>sections</c> line, then one <c>applicability</c> line per section that is not applicable or
///               disputed, sorted by section id; and an <c>outcome</c> line (<c>none</c>, <c>approved</c> or
///               <c>rejected</c>). Justifications, answerers, reasons, times and ballots of a slot that has not
///               submitted are deliberately not covered, so a signature binds to what the reviewers agree on, not to how
///               they worded it;</item>
///         <item>only when the review has a review conclusion text (see <see cref="ReviewRecord.ReviewConclusionText"/>), a
///               <c>conclusion-text</c> line with the text, so a signature binds to the words the reviewers wrote as the
///               conclusion of the record. The line is absent when there is no text, so the digest of a review without
///               one is unchanged. It is unrelated to the review conclusion (approved or rejected) of the ballot;</item>
///         <item>the report: a <c>report</c> marker line, then one block for every thread that has at least one comment
///               included in the report (see <see cref="ReviewComment.IncludeInReport"/>). A block is a
///               <c>thread file</c> line (the anchor: path, side, first and last line, so the anchor slot can later
///               name a check instead of a file), then, in posting order, only the included comments with author
///               display name, author user name, text and time: <c>claim</c> for the starting comment (a Finding),
///               <c>comment</c> for an included reply and <c>resolution</c> for an included resolution note. Chat is
///               not covered, and neither are the thread state (whether a thread is open is the separate rule of
///               <see cref="ReviewLock"/>), the creation time of a thread and any identity; a thread without an
///               included comment is absent. The blocks are sorted, so the order of threads does not matter, while the
///               order of comments does;</item>
///         <item>only for a review that an administrator reopened, a final <c>reopens {count}</c> line. A review that
///               was never reopened has no such line, so its digest does not contain it;
///               after a reopen the digest differs from every earlier one,
///               whatever the content, so a signature given before a reopen can never be current again.</item>
///     </list>
///     A follow-up review (one created from an earlier review, see <see cref="ReviewService"/>) adds two kinds of
///     conditional text, in the same way as <c>reopens</c>: a <c>follows {id}</c> line straight after the source block,
///     and a <c>carried {id}</c> suffix at the end of a thread line whose thread was carried over from that review.
///     They are written only when present, so the digest of a review that is not a
///     follow-up does not contain them; a signature of a follow-up
///     therefore also binds to which review it follows and to which threads were carried rather than started in it.
///     Strings are written as <c>{length}:{text}</c> (length in UTF-16 code units) and absent values as
///     <c>-</c>, so no text can imitate a delimiter; numbers use the invariant culture and times are written as
///     UTC ticks. All sorting is ordinal. The class is stateless and thread-safe.
///     <para>
///         Two preconditions bound the claims above. Order independence assumes that the keys of the sorted
///         lists (check id, section id, change path, and the section and check ids inside the checklist definition)
///         are unique; the digest does not enforce this. The store's keys guarantee it for answers, section states and
///         changes, and a checklist definition is expected to hold unique ids (<see cref="ChecklistDefinition.FromJson"/>
///         does not reject duplicates); with duplicate keys the stable sort would keep the
///         input order. And the text is hashed as UTF-8, where a lone UTF-16 surrogate (malformed text that valid
///         user input does not contain) is replaced by U+FFFD, so two strings that differ only in such characters
///         give the same digest; this is accepted rather than changing the digest of any record.
///     </para>
/// </remarks>
public static class RecordDigest
{
    /// <summary>
    ///     The first line of the canonical text; names the format version.
    /// </summary>
    private const string Header = "ReviewCenter record v2";

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

        // A follow-up names the review it follows; the line is absent otherwise, so no other digest changes
        if (record.FollowsReviewId is not null)
        {
            lines.Add("follows " + Text(record.FollowsReviewId));
        }

        // The questions come before the answers so a signature binds to what was asked, not only to the replies
        AddChecklist(lines, record.Checklist);

        // The agreed standing of the submitted ballots replaces the individual answers: what a signature binds to is what
        // the reviewers handed in and agree on, so a draft or a slot that has not submitted never changes the digest
        AddConsolidation(lines, record);
        // The review conclusion text is part of what the reviewers sign; the line is absent when there is none, which
        // keeps the digest of every review without one as it was. It comes before the report, which ends the text
        if (record.ReviewConclusionText is not null)
        {
            lines.Add("conclusion-text " + Text(record.ReviewConclusionText));
        }

        // The report holds only the threads with an included comment; each becomes one block, and sorting the blocks
        // removes any dependence on row order or identity. Chat and the thread state are deliberately not covered
        lines.Add("report");
        var blocks = record.Threads
            .Where(thread => thread.Comments.Any(comment => comment.IncludeInReport))
            .Select(ThreadBlock)
            .ToList();
        blocks.Sort(StringComparer.Ordinal);
        lines.AddRange(blocks);

        // A reopen changes the digest on purpose, so the signatures of the approval that was reopened stay history;
        // the line is absent for a review never reopened, which keeps its digest as it was
        if (record.Reopens > 0)
        {
            lines.Add("reopens " + record.Reopens.ToString(CultureInfo.InvariantCulture));
        }

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
    ///     Writes the consolidated standing of the ballots: the agreed outcome of every check, the sections that do not
    ///     apply or are disputed, and the outcome of the review.
    /// </summary>
    /// <remarks>
    ///     Only submitted ballots take part (see <see cref="BallotConsolidation"/>). Each listed check gets one line with
    ///     its standing: <c>agreed</c> with the outcome, <c>disputed</c> or <c>pending</c>; a section appears only when
    ///     the submitted ballots all mark it not applicable or disagree on it. The checks of such a section are not
    ///     listed. The lines are sorted by identifier, so the order of the checklist and of the ballots does not matter.
    ///     A record without a checklist has no checks and no sections and the outcome <c>none</c>. Justifications,
    ///     answerers and times are not covered: wording and authorship never decide what the reviewers agree on.
    /// </remarks>
    /// <param name="lines">The lines written so far.</param>
    /// <param name="record">The record.</param>
    private static void AddConsolidation(List<string> lines, ReviewRecord record)
    {
        var result = record.Checklist is null
            ? null
            : BallotConsolidation.Consolidate(record.Checklist.Parse(), record.Ballots);

        lines.Add("checks");
        foreach (var check in (result?.Checks ?? []).OrderBy(c => c.ItemId, StringComparer.Ordinal))
        {
            var standing = check.Consensus switch
            {
                CheckConsensus.Agreed => "agreed " + Text(check.Outcome.ToString()),
                CheckConsensus.Disputed => "disputed",
                _ => "pending"
            };
            lines.Add($"check {Text(check.ItemId)} {standing}");
        }

        lines.Add("sections");
        foreach (var section in (result?.Sections ?? [])
                     .Where(s => s.Consensus != SectionConsensus.Applicable)
                     .OrderBy(s => s.SectionId, StringComparer.Ordinal))
        {
            var standing = section.Consensus == SectionConsensus.NotApplicable ? "not-applicable" : "disputed";
            lines.Add($"applicability {Text(section.SectionId)} {standing}");
        }

        var outcome = result?.Outcome switch
        {
            ReviewOutcome.Approved => "approved",
            ReviewOutcome.Rejected => "rejected",
            _ => "none"
        };
        lines.Add("outcome " + outcome);
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
    ///     Renders one thread of the report to a block of lines.
    /// </summary>
    /// <param name="thread">The thread; it has at least one comment included in the report.</param>
    /// <returns>
    ///     The block; the first line is the anchor (<c>thread file</c>, with the carried marker when the thread was
    ///     carried over), each following line is one included comment in posting order.
    /// </returns>
    private static string ThreadBlock(ReviewThread thread)
    {
        var lines = new List<string>
        {
            $"thread file {Text(thread.FilePath)} {Text(thread.Side.ToString())} {Number(thread.StartLine)} {Number(thread.EndLine)}" +
            Carried(thread.CarriedFromReviewId)
        };

        // Comment order is meaningful, so it is kept exactly as stored (the store orders by identity). The starting
        // comment is the claim of a Finding; a resolution note is named as such; chat is left out
        for (var index = 0; index < thread.Comments.Count; index++)
        {
            var comment = thread.Comments[index];
            if (!comment.IncludeInReport)
            {
                continue;
            }

            var label = LabelOf(index, comment);
            lines.Add(
                $"{label} {Text(comment.Author)} {Text(comment.AuthorUsername)} {Text(comment.Text)} " +
                comment.Created.UtcTicks.ToString(CultureInfo.InvariantCulture));
        }

        return string.Join('\n', lines);
    }

    /// <summary>
    ///     Names the line of an included comment.
    /// </summary>
    /// <param name="index">The position of the comment in the thread, 0 for the starting comment.</param>
    /// <param name="comment">The comment.</param>
    /// <returns><c>resolution</c> for a resolution note, <c>claim</c> for the starting comment, otherwise <c>comment</c>.</returns>
    private static string LabelOf(int index, ReviewComment comment)
    {
        if (comment.Kind == ReviewCommentKind.Resolution)
        {
            return "resolution";
        }

        return index == 0 ? "claim" : "comment";
    }

    /// <summary>
    ///     Writes the optional suffix of a carried row.
    /// </summary>
    /// <param name="carriedFrom">The review the row was carried from, or <c>null</c> for a row of this review.</param>
    /// <returns>The empty string for no marker, otherwise a space, <c>carried</c>, a space and the length-prefixed id.</returns>
    private static string Carried(string? carriedFrom)
    {
        return carriedFrom is null ? string.Empty : " carried " + Text(carriedFrom);
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
