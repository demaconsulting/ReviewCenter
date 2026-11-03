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
///     Explains, from the audit trail, what changed in a review after a reviewer signed it.
/// </summary>
/// <remarks>
///     <para>
///         The signature binds to the record digest (see <see cref="RecordDigest"/>), which covers the consolidated
///         ballots, the review conclusion text and the part of the discussions that is included in the report. The audit
///         trail records every such change, so the entries after the signature that have one of the actions below explain
///         why the signature no longer matches. A draft answer is not listed: it does not change the digest, and the
///         store records <c>ballot.submitted</c> and <c>ballot.reopened</c> when a ballot starts or stops counting.
///     </para>
///     <para>
///         A reply or a comment alters the digest only when it is included in the report, and the audit text says so,
///         so it is listed only then. A resolution note counts only for a Finding; the audit text does not name the kind,
///         so the kind of every thread is replayed from the entries that start a thread or change its kind. A thread
///         carried over from a followed review has no start entry, so its kind at carry time is seeded from the thread
///         itself (read-side only; the audit trail and the digest are unchanged). The reasons
///         are fixed sentences that never contain text of the entry, and an entry that the audit masking hides from the
///         viewer gets one neutral sentence, so the list reveals nothing the viewer may not see. The class is pure and
///         thread-safe.
///     </para>
/// </remarks>
internal static class ReviewChangeRules
{
    /// <summary>
    ///     The reason shown for a change the viewer may not see the details of yet.
    /// </summary>
    public const string HiddenReason = "Something in the report changed (details are hidden until you hand in your own ballot)";

    /// <summary>
    ///     The text a started thread or a reply carries when it goes in the report.
    /// </summary>
    private const string InReportMarker = "; in report";

    /// <summary>
    ///     The text a resolution carries when it has a note.
    /// </summary>
    private const string NoteMarker = "; note sha256";

    /// <summary>
    ///     Derives the changes after a moment from the entries of a review.
    /// </summary>
    /// <param name="entries">All entries of the review, oldest first.</param>
    /// <param name="shown">
    ///     The same entries after the audit masking for the viewer, in the same order; an entry that was masked is a
    ///     different object than the entry it replaces.
    /// </param>
    /// <param name="after">The moment of the signature; only later entries are listed.</param>
    /// <param name="threads">
    ///     The threads of the review as they are now; the ones carried over from a followed review have no start entry
    ///     in the trail, so their kind at carry time is seeded from here. <c>null</c> seeds nothing.
    /// </param>
    /// <returns>The changes after <paramref name="after"/> that alter the report, oldest first.</returns>
    public static IReadOnlyList<ReviewChange> Derive(
        IReadOnlyList<AuditEntry> entries,
        IReadOnlyList<AuditEntry> shown,
        DateTimeOffset after,
        IReadOnlyList<ReviewThread>? threads = null)
    {
        var findings = SeedCarriedThreads(entries, threads);
        var changes = new List<ReviewChange>();
        for (var i = 0; i < entries.Count; i++)
        {
            // Every entry is replayed, also the ones before the signature, because they tell the kind of a thread
            var entry = entries[i];
            var reason = ReasonOf(entry, findings);
            if (reason is null || entry.TimestampUtc <= after)
            {
                continue;
            }

            var hidden = !ReferenceEquals(shown[i], entry);
            changes.Add(new ReviewChange(entry.TimestampUtc, entry.ActorName, hidden ? HiddenReason : reason));
        }

        return changes;
    }

    /// <summary>
    ///     Seeds the kind of the threads that were carried over from a followed review.
    /// </summary>
    /// <param name="entries">All entries of the review, oldest first.</param>
    /// <param name="threads">The threads of the review as they are now; <c>null</c> when none are known.</param>
    /// <returns>Whether each carried thread was a Finding at carry time, by thread identity.</returns>
    /// <remarks>
    ///     A carried thread has no <c>thread.started</c> entry in the follow-up's trail. Its kind at carry time is the
    ///     kind named before the arrow of its first <c>thread.kind-changed</c> entry, or its current kind when the
    ///     kind never changed. The audit trail itself is not altered.
    /// </remarks>
    private static Dictionary<int, bool> SeedCarriedThreads(
        IReadOnlyList<AuditEntry> entries,
        IReadOnlyList<ReviewThread>? threads)
    {
        var findings = new Dictionary<int, bool>();
        if (threads is null)
        {
            return findings;
        }

        foreach (var thread in threads)
        {
            if (thread.CarriedFromReviewId is null)
            {
                continue;
            }

            var prefix = $"thread {thread.Id}: ";
            var first = entries.FirstOrDefault(e =>
                e.Action == "thread.kind-changed" && e.Detail.StartsWith(prefix, StringComparison.Ordinal));
            findings[thread.Id] = first is null
                ? thread.Kind == ThreadKind.Finding
                : first.Detail.StartsWith(prefix + ThreadKindRules.KindText(ThreadKind.Finding) + " ->", StringComparison.Ordinal);
        }

        return findings;
    }

    /// <summary>
    ///     Words one entry as a change of the report, and follows the kind of the threads.
    /// </summary>
    /// <param name="entry">The entry.</param>
    /// <param name="findings">Whether each thread known so far is a Finding, by thread identity.</param>
    /// <returns>The reason; <c>null</c> when the entry does not alter the report.</returns>
    private static string? ReasonOf(AuditEntry entry, Dictionary<int, bool> findings)
    {
        var detail = entry.Detail;
        switch (entry.Action)
        {
            case "ballot.submitted":
                return "Handed in a ballot";
            case "ballot.reopened":
                return "Took a ballot back to change it";
            case "ballot.reassigned":
                return "Gave a ballot to another reviewer";
            case ChecklistService.ReviewConclusionTextChangedAction:
                return "Changed the review conclusion";
            case "thread.started":
                var started = detail.Contains(InReportMarker, StringComparison.Ordinal);
                if (ThreadIdOf(detail) is { } startedId)
                {
                    findings[startedId] = started;
                }

                return started ? "Started a finding that goes in the report" : null;
            case "thread.replied":
                return detail.Contains(InReportMarker, StringComparison.Ordinal) ? "Added a reply that goes in the report" : null;
            case "thread.resolved":
                return detail.Contains(NoteMarker, StringComparison.Ordinal)
                       && ThreadIdOf(detail) is { } resolvedId
                       && findings.GetValueOrDefault(resolvedId)
                    ? "Resolved a finding with a note that goes in the report"
                    : null;
            case "thread.kind-changed":
                return KindChange(detail, findings);
            case "thread.comment-report-changed":
                if (detail.EndsWith("taken out of report", StringComparison.Ordinal))
                {
                    return "Took a comment out of the report";
                }

                return detail.EndsWith("included in report", StringComparison.Ordinal)
                    ? "Put a comment in the report"
                    : null;
            default:
                return null;
        }
    }

    /// <summary>
    ///     Words a change of the kind of a thread and follows it.
    /// </summary>
    /// <param name="detail">The detail of the entry, <c>thread {id}: {old} -&gt; {new}</c>.</param>
    /// <param name="findings">Whether each thread known so far is a Finding, by thread identity.</param>
    /// <returns>The reason; <c>null</c> when the detail names no known kind.</returns>
    private static string? KindChange(string detail, Dictionary<int, bool> findings)
    {
        var finding = detail.EndsWith("-> " + ThreadKindRules.KindText(ThreadKind.Finding), StringComparison.Ordinal);
        var question = detail.EndsWith("-> " + ThreadKindRules.KindText(ThreadKind.Question), StringComparison.Ordinal);
        if (!finding && !question)
        {
            return null;
        }

        if (ThreadIdOf(detail) is { } id)
        {
            findings[id] = finding;
        }

        return finding ? "Turned a question into a finding" : "Turned a finding into a question";
    }

    /// <summary>
    ///     Reads the thread an entry is about; every thread entry begins its detail with <c>thread {id}</c>.
    /// </summary>
    /// <param name="detail">The detail of the entry.</param>
    /// <returns>The identity of the thread; <c>null</c> when none is named.</returns>
    private static int? ThreadIdOf(string detail)
    {
        const string prefix = "thread ";
        if (!detail.StartsWith(prefix, StringComparison.Ordinal))
        {
            return null;
        }

        var end = prefix.Length;
        while (end < detail.Length && char.IsAsciiDigit(detail[end]))
        {
            end++;
        }

        return int.TryParse(detail.AsSpan(prefix.Length, end - prefix.Length), out var id) ? id : null;
    }
}
