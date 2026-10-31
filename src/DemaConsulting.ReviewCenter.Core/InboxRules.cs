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
///     The pure rules of the inbox: which slot needs a signature, which threads wait for an answer, which checks
///     failed and which comments are new to a user.
/// </summary>
/// <remarks>
///     The rules work on values only, so they are tested without any store. The slot state mirrors the one
///     <see cref="SigningService"/> reports (a signature of the current record digest means signed, signatures of
///     other digests only mean out of date), so the inbox and the review page never disagree; a parity test guards
///     that. The class has no state and is safe to call from several threads.
/// </remarks>
internal static class InboxRules
{
    /// <summary>
    ///     Compares two user names the way the rest of the system does, without regard to case.
    /// </summary>
    /// <param name="left">The first user name; may be null.</param>
    /// <param name="right">The second user name; may be null.</param>
    /// <returns><c>true</c> when both are the same user name.</returns>
    public static bool SameUser(string? left, string? right)
    {
        return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     Computes the signature state of a slot.
    /// </summary>
    /// <param name="slot">The slot.</param>
    /// <param name="signatures">The signatures of the review.</param>
    /// <param name="digest">The digest of the current record.</param>
    /// <returns>
    ///     <see cref="SlotSignatureState.Signed"/> when a signature of the slot has the current digest,
    ///     <see cref="SlotSignatureState.OutOfDate"/> when the slot has signatures but none of the current digest,
    ///     otherwise <see cref="SlotSignatureState.NotSigned"/>.
    /// </returns>
    public static SlotSignatureState SlotState(ReviewerSlot slot, IReadOnlyList<ReviewSignature> signatures, string digest)
    {
        var own = signatures.Where(signature => signature.SlotId == slot.Id).ToList();
        if (own.Any(signature => signature.RecordDigest == digest))
        {
            return SlotSignatureState.Signed;
        }

        return own.Count > 0 ? SlotSignatureState.OutOfDate : SlotSignatureState.NotSigned;
    }

    /// <summary>
    ///     Reports whether a user is the primary or the alternate reviewer of a slot.
    /// </summary>
    /// <param name="slot">The slot.</param>
    /// <param name="userId">The identity of the user.</param>
    /// <returns><c>true</c> when the user is assigned to the slot.</returns>
    public static bool IsAssigned(ReviewerSlot slot, int userId)
    {
        return slot.PrimaryUserId == userId || slot.AlternateUserId == userId;
    }

    /// <summary>
    ///     Reports whether a comment is new to a user.
    /// </summary>
    /// <param name="comment">The comment.</param>
    /// <param name="username">The user name of the user; the user's own comments are never unread.</param>
    /// <param name="lastSeenCommentId">The highest comment identity the user has seen; zero when none.</param>
    /// <returns><c>true</c> when the comment is by someone else and has a higher identity than the last seen one.</returns>
    public static bool IsUnread(ReviewComment comment, string username, int lastSeenCommentId)
    {
        return comment.Id > lastSeenCommentId && !SameUser(comment.AuthorUsername, username);
    }

    /// <summary>
    ///     Counts the comments of all threads that are new to a user.
    /// </summary>
    /// <param name="threads">The threads with their comments.</param>
    /// <param name="username">The user name of the user.</param>
    /// <param name="lastSeenCommentId">The highest comment identity the user has seen; zero when none.</param>
    /// <returns>The number of unread comments.</returns>
    public static int CountUnread(IEnumerable<ReviewThread> threads, string username, int lastSeenCommentId)
    {
        return threads.SelectMany(thread => thread.Comments).Count(comment => IsUnread(comment, username, lastSeenCommentId));
    }

    /// <summary>
    ///     Finds the highest comment identity of all threads.
    /// </summary>
    /// <param name="threads">The threads with their comments.</param>
    /// <returns>The highest identity, or zero when there is no comment.</returns>
    public static int MaxCommentId(IEnumerable<ReviewThread> threads)
    {
        return threads.SelectMany(thread => thread.Comments).Select(comment => comment.Id).DefaultIfEmpty(0).Max();
    }

    /// <summary>
    ///     Finds the open threads in which the latest comment is by someone else and that the user took part in
    ///     or whose review the user authored.
    /// </summary>
    /// <remarks>
    ///     A comment by the system (a carried follow-up thread, for example) counts as by someone else.
    /// </remarks>
    /// <param name="threads">The threads with their comments.</param>
    /// <param name="username">The user name of the user.</param>
    /// <param name="isAuthor">Whether the user authored the review.</param>
    /// <returns>The waiting threads; empty when none waits.</returns>
    public static IReadOnlyList<ReviewThread> WaitingThreads(IEnumerable<ReviewThread> threads, string username, bool isAuthor)
    {
        var waiting = new List<ReviewThread>();
        foreach (var thread in threads.Where(thread => thread.State == ReviewThreadState.Open))
        {
            var latest = thread.Comments.MaxBy(comment => comment.Id);
            if (latest is null || SameUser(latest.AuthorUsername, username))
            {
                continue;
            }

            if (isAuthor || thread.Comments.Any(comment => SameUser(comment.AuthorUsername, username)))
            {
                waiting.Add(thread);
            }
        }

        return waiting;
    }

    /// <summary>
    ///     Finds the checks answered Fail in the sections that apply.
    /// </summary>
    /// <param name="record">The record of the review.</param>
    /// <returns>The failed answers; empty when the review has no checklist or no failed check.</returns>
    public static IReadOnlyList<ChecklistAnswer> FailedAnswers(ReviewRecord record)
    {
        if (record.Checklist is null)
        {
            return [];
        }

        var state = new ChecklistState(record.Checklist.Parse(), record.SectionStates, record.Answers);
        return state.Definition.Sections
            .Where(section => state.IsApplicable(section.Id))
            .SelectMany(section => section.Items)
            .Select(item => state.AnswerFor(item.Id))
            .OfType<ChecklistAnswer>()
            .Where(answer => answer.Outcome == ChecklistOutcome.Fail)
            .ToList();
    }

    /// <summary>
    ///     Finds the signature that completed the current approval.
    /// </summary>
    /// <param name="signatures">The signatures of the review.</param>
    /// <param name="digest">The digest of the current record.</param>
    /// <returns>The latest signature of the current digest (the higher identity on a tie), or <c>null</c> when
    /// there is none.</returns>
    public static ReviewSignature? CompletingSignature(IReadOnlyList<ReviewSignature> signatures, string digest)
    {
        ReviewSignature? latest = null;
        foreach (var signature in signatures.Where(signature => signature.RecordDigest == digest))
        {
            if (latest is null
                || signature.SignedAt > latest.SignedAt
                || (signature.SignedAt == latest.SignedAt && signature.Id > latest.Id))
            {
                latest = signature;
            }
        }

        return latest;
    }
}
