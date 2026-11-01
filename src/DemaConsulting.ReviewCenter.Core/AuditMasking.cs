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
///     Hides, when an audit entry is shown, what the entry would give away about the answers of the other reviewers.
/// </summary>
/// <remarks>
///     <para>
///         The audit detail of an answer names the check and the outcome (<c>item abc: Fail</c>), and the detail of a
///         discussion about a check names the check. While a viewer may not see the other reviewers' answers (see
///         <see cref="BallotVisibility"/>), those details are replaced by a neutral sentence. The masking happens when
///         an entry is read for display, so the stored chain, its hashes and the export are untouched, and a viewer
///         always sees what they did themselves.
///     </para>
///     <para>
///         Who acted and when stays visible, because that is progress and not a verdict. The class is pure and
///         thread-safe.
///     </para>
/// </remarks>
public static class AuditMasking
{
    /// <summary>
    ///     The detail that replaces a hidden answer or applicability change.
    /// </summary>
    public const string HiddenAnswerDetail = "Checklist answer recorded (details are hidden until you hand in your own ballot)";

    /// <summary>
    ///     The detail that replaces a hidden discussion about a check.
    /// </summary>
    public const string HiddenDiscussionDetail = "Discussion started (details are hidden until you hand in your own ballot)";

    /// <summary>
    ///     The detail that replaces the detail of a later entry of a discussion (a reply, a resolution, a change of
    ///     kind) whose thread is not known to be about a file.
    /// </summary>
    public const string HiddenDiscussionActivityDetail = "Discussion activity recorded (details are hidden until you hand in your own ballot)";

    /// <summary>
    ///     Prepares audit entries for showing to a viewer.
    /// </summary>
    /// <param name="entries">The entries. Must not be null.</param>
    /// <param name="canSeeOthers">
    ///     <c>true</c> when the viewer may see the answers of the other reviewers (see
    ///     <see cref="BallotVisibility"/>); the entries are then returned as they are.
    /// </param>
    /// <param name="viewerUserId">The identity of the viewer, whose own entries are never hidden; or <c>null</c>.</param>
    /// <returns>
    ///     The entries in the same order; an answer, an applicability change by somebody else and the start of a
    ///     discussion about a check carry a neutral detail when <paramref name="canSeeOthers"/> is <c>false</c>, and so
    ///     does every later entry of a discussion (a reply, a resolution, a reopening, a change of kind or of the
    ///     report flag) unless the entries show that its thread is about a file. The check is fail-closed: a thread
    ///     whose start is not among the given entries is treated as being about a check.
    /// </returns>
    public static IReadOnlyList<AuditEntry> ForViewer(
        IReadOnlyList<AuditEntry> entries,
        bool canSeeOthers,
        int? viewerUserId)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (canSeeOthers)
        {
            return entries;
        }

        // A later entry of a thread (a reply, a resolution, a new kind) only names the thread, so the threads that are
        // known not to be about a check are collected first from the entries that start a thread
        var fileThreads = entries
            .Where(entry => entry.Action == "thread.started" && !IsCheckStart(entry))
            .Select(entry => ThreadKey(entry))
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);
        return entries.Select(entry => Mask(entry, viewerUserId, fileThreads)).ToList();
    }

    /// <summary>
    ///     Masks one entry when it would give an answer away.
    /// </summary>
    /// <param name="entry">The entry.</param>
    /// <param name="viewerUserId">The identity of the viewer, or <c>null</c>.</param>
    /// <param name="fileThreads">The keys of the threads that are known not to be about a check.</param>
    /// <returns>The entry itself, or a copy with a neutral detail.</returns>
    private static AuditEntry Mask(AuditEntry entry, int? viewerUserId, HashSet<string> fileThreads)
    {
        // What a person did themselves is no secret from them
        if (viewerUserId is not null && entry.ActorUserId == viewerUserId)
        {
            return entry;
        }

        var detail = entry.Action switch
        {
            "checklist.answered" or "checklist.applicability-changed" => HiddenAnswerDetail,
            "thread.started" when IsCheckStart(entry) => HiddenDiscussionDetail,
            "thread.started" => null,

            // Any other entry of a thread is hidden unless the thread is known to be about a file; a thread whose
            // start is not among the entries (another page) is hidden too, because it may be about a check
            _ when entry.Action.StartsWith("thread.", StringComparison.Ordinal)
                   && !(ThreadKey(entry) is { } key && fileThreads.Contains(key)) => HiddenDiscussionActivityDetail,
            _ => null
        };
        return detail is null ? entry : Copy(entry, detail);
    }

    /// <summary>
    ///     Tells whether an entry records the start of a discussion about a check.
    /// </summary>
    /// <param name="entry">The <c>thread.started</c> entry.</param>
    /// <returns><c>true</c> when the discussion is about a check.</returns>
    private static bool IsCheckStart(AuditEntry entry)
    {
        return entry.Detail.Contains(" on check ", StringComparison.Ordinal);
    }

    /// <summary>
    ///     Reads which thread an entry is about; every thread entry begins its detail with <c>thread {id}</c>.
    /// </summary>
    /// <param name="entry">The entry.</param>
    /// <returns>The project, review and thread identity as one text; or <c>null</c> when no thread is named.</returns>
    private static string? ThreadKey(AuditEntry entry)
    {
        const string prefix = "thread ";
        if (!entry.Detail.StartsWith(prefix, StringComparison.Ordinal))
        {
            return null;
        }

        var end = prefix.Length;
        while (end < entry.Detail.Length && char.IsAsciiDigit(entry.Detail[end]))
        {
            end++;
        }

        return int.TryParse(entry.Detail.AsSpan(prefix.Length, end - prefix.Length), out var id)
            ? $"{entry.ProjectId}/{entry.ReviewId}/{id}"
            : null;
    }

    /// <summary>
    ///     Copies an entry with another detail.
    /// </summary>
    /// <param name="entry">The entry.</param>
    /// <param name="detail">The detail to show.</param>
    /// <returns>The copy; its hashes are those of the stored entry, so the copy is for display only.</returns>
    private static AuditEntry Copy(AuditEntry entry, string detail)
    {
        return new AuditEntry
        {
            Sequence = entry.Sequence,
            TimestampUtc = entry.TimestampUtc,
            ActorUserId = entry.ActorUserId,
            ActorName = entry.ActorName,
            Action = entry.Action,
            ProjectId = entry.ProjectId,
            ReviewId = entry.ReviewId,
            Detail = detail,
            PreviousHash = entry.PreviousHash,
            Hash = entry.Hash
        };
    }
}
