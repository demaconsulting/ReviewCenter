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
///     Starts, answers, resolves and reads the comment threads of a review, enforcing the comment rules.
/// </summary>
/// <remarks>
///     The service is the single place where comment rules live. A thread is anchored to the whole file
///     or to a range of lines of the head or (for a repository review) of the baseline, and always starts
///     with one comment; anyone can reply. Comments are
///     immutable, so the service offers no way to edit or delete one; the only change to an existing
///     thread is its <see cref="ReviewThreadState"/>. The author of a comment is the signed-in
///     <see cref="Actor"/>, never free text, so it cannot be spoofed by a form field.
///     Two pure static helpers shape threads for display (<see cref="BuildIndex"/>, <see cref="NextStep"/>).
///     The service holds no state of its own and is safe for concurrent use as long as the injected
///     <see cref="IReviewStore"/> is.
/// </remarks>
public sealed class ThreadService
{
    /// <summary>
    ///     The store that persists threads.
    /// </summary>
    private readonly IReviewStore _store;

    /// <summary>
    ///     Decides who may see which project; every change needs a user who can see it.
    /// </summary>
    private readonly IProjectAccess _access;

    /// <summary>
    ///     The clock that supplies creation times.
    /// </summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>
    ///     Initializes a new instance of the <see cref="ThreadService"/> class.
    /// </summary>
    /// <param name="store">The store that persists threads. Must not be null.</param>
    /// <param name="access">The access rules. Must not be null.</param>
    /// <param name="timeProvider">The clock used for creation times; injected so tests control time. Must not be null.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    public ThreadService(IReviewStore store, IProjectAccess access, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(access);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _store = store;
        _access = access;
        _timeProvider = timeProvider;
    }

    /// <summary>
    ///     Starts a thread on a file of a review, with its first comment.
    /// </summary>
    /// <remarks>
    ///     Where the thread goes (file, lines, side) is one <see cref="ThreadAnchor"/>, so that the request reads
    ///     "this anchor, this author, this text"; a whole-file thread on the head is just
    ///     <c>new ThreadAnchor(path)</c>.
    /// </remarks>
    /// <param name="projectId">The identity of the owning project.</param>
    /// <param name="reviewId">The review identifier.</param>
    /// <param name="anchor">
    ///     Where the thread is anchored; must not be null. A line range (both lines set) requires a text file with at
    ///     least that many lines (see <see cref="FileText"/>). The side of the anchor is ignored for a whole-file
    ///     anchor (which is always stored as <see cref="ThreadSide.Head"/>, and rejects
    ///     <see cref="ThreadSide.Baseline"/>). <see cref="ThreadSide.Baseline"/> needs a repository review with a
    ///     baseline commit and a file that is <see cref="ChangeKind.Modified"/> or <see cref="ChangeKind.Deleted"/>;
    ///     the range is then checked against the baseline lines. On the head side a <see cref="ChangeKind.Deleted"/>
    ///     file accepts no lines.
    /// </param>
    /// <param name="actor">The signed-in user who writes the comment. Must not be null.</param>
    /// <param name="text">
    ///     The comment text; leading and trailing white space is removed. Must not be empty and must not
    ///     exceed <see cref="ReviewLimits.MaxCommentLength"/> characters.
    /// </param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The stored thread in the <see cref="ReviewThreadState.Open"/> state, with its first comment.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="anchor"/> or <paramref name="actor"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the side of <paramref name="anchor"/> is not a defined value.</exception>
    /// <exception cref="DomainRuleException">
    ///     Thrown when a rule is violated (see <see cref="DomainRule"/>); nothing is stored then.
    /// </exception>
    public async Task<ReviewThread> StartThreadAsync(
        int projectId,
        string reviewId,
        ThreadAnchor anchor,
        Actor actor,
        string? text,
        CancellationToken cancellationToken = default)
    {
        // Validate everything that needs no I/O first so a bad request fails fast and cheaply
        ArgumentNullException.ThrowIfNull(anchor);
        ArgumentNullException.ThrowIfNull(actor);
        await _access.RequireViewAsync(actor, projectId, cancellationToken);
        var (filePath, startLine, endLine, side) = anchor;
        if (!Enum.IsDefined(side))
        {
            throw new ArgumentOutOfRangeException(nameof(anchor), side, "The side of the anchor is not a known value.");
        }

        var trimmedText = ValidateText(text);
        ValidateAnchorShape(startLine, endLine);

        // A whole-file anchor has no line numbers, so it has no side to choose: it is always stored as Head
        if (startLine is null && side == ThreadSide.Baseline)
        {
            throw new DomainRuleException(
                DomainRule.ThreadAnchorInvalid,
                "A whole-file comment has no baseline side; comment on a baseline line instead.");
        }

        // The file must exist in the review; the metadata read does not load any file bytes
        var review = await _store.GetReviewAsync(projectId, reviewId, cancellationToken);
        var file = review?.Files.FirstOrDefault(f => string.Equals(f.Path, filePath, StringComparison.Ordinal));
        if (review is null || file is null)
        {
            throw new DomainRuleException(
                DomainRule.ThreadAnchorInvalid,
                $"The file '{filePath}' does not exist in this review.");
        }

        // Which side may carry line anchors depends on the kind of change
        if (startLine is not null)
        {
            ValidateSideForFile(review, file, side);

            // Only a line range needs the bytes, to count lines the way the page shows them
            await ValidateRangeWithinFileAsync(projectId, reviewId, filePath, side, endLine!.Value, cancellationToken);
        }

        var now = _timeProvider.GetUtcNow();
        var thread = new ReviewThread
        {
            ProjectId = projectId,
            ReviewId = reviewId,
            FilePath = filePath,
            Side = startLine is null ? ThreadSide.Head : side,
            StartLine = startLine,
            EndLine = endLine,
            State = ReviewThreadState.Open,
            Created = now,
            Comments =
            [
                new ReviewComment
                {
                    Author = actor.DisplayName,
                    AuthorUsername = actor.Username,
                    Text = trimmedText,
                    Created = now
                }
            ]
        };

        return await _store.AddThreadAsync(
            thread,
            stored =>
            {
                // A single line reads "line 3", a range "lines 3-5", a whole file "(whole file)"; only the
                // baseline side is named, because the head is the default and keeps the text unchanged
                string anchor;
                if (stored.StartLine is not { } start)
                {
                    anchor = "(whole file)";
                }
                else if (stored.EndLine == start)
                {
                    anchor = $"line {start}";
                }
                else
                {
                    anchor = $"lines {start}-{stored.EndLine}";
                }

                if (stored.StartLine is not null && stored.Side == ThreadSide.Baseline)
                {
                    anchor += " (baseline)";
                }

                var first = stored.Comments[0];

                // The comment text itself is never audited; its short digest still identifies it
                return AuditDraft.By(
                    actor,
                    "thread.started",
                    $"thread {stored.Id} on {stored.FilePath} {anchor}; comment {first.Id}, text sha256 {AuditText.ShortHash(first.Text)}",
                    projectId,
                    reviewId);
            },
            cancellationToken);
    }

    /// <summary>
    ///     Adds a reply to an existing thread of a review.
    /// </summary>
    /// <param name="projectId">The identity of the owning project.</param>
    /// <param name="reviewId">The review identifier.</param>
    /// <param name="threadId">The identity of the thread; it must belong to the given review.</param>
    /// <param name="actor">The signed-in user who writes the reply. Must not be null.</param>
    /// <param name="text">The reply text; same rules as for a new thread.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    ///     The stored comment. Earlier comments are untouched and the thread state does not change, so a
    ///     resolved thread can still receive replies.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="actor"/> is null.</exception>
    /// <exception cref="DomainRuleException">
    ///     Thrown when a rule is violated (see <see cref="DomainRule"/>); nothing is stored then.
    /// </exception>
    public async Task<ReviewComment> ReplyAsync(
        int projectId,
        string reviewId,
        int threadId,
        Actor actor,
        string? text,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        await _access.RequireViewAsync(actor, projectId, cancellationToken);
        var comment = new ReviewComment
        {
            Author = actor.DisplayName,
            AuthorUsername = actor.Username,
            Text = ValidateText(text),
            Created = _timeProvider.GetUtcNow()
        };

        return await _store.AddCommentAsync(
                   projectId,
                   reviewId,
                   threadId,
                   comment,
                   stored => AuditDraft.By(
                       actor,
                       "thread.replied",
                       $"thread {threadId}, comment {stored.Id}, text sha256 {AuditText.ShortHash(stored.Text)}",
                       projectId,
                       reviewId),
                   cancellationToken)
               ?? throw ThreadNotFound(threadId);
    }

    /// <summary>
    ///     Marks a thread resolved.
    /// </summary>
    /// <param name="projectId">The identity of the owning project.</param>
    /// <param name="reviewId">The review identifier.</param>
    /// <param name="threadId">The identity of the thread; it must belong to the given review.</param>
    /// <param name="actor">The signed-in user who resolves the thread; recorded in the audit trail. Must not be null.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the state is stored; resolving a resolved thread does nothing.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="actor"/> is null.</exception>
    /// <exception cref="DomainRuleException">Thrown with <see cref="DomainRule.ThreadNotFound"/> when the thread does not exist in the review.</exception>
    public Task ResolveAsync(
        int projectId,
        string reviewId,
        int threadId,
        Actor actor,
        CancellationToken cancellationToken = default)
    {
        return SetStateAsync(projectId, reviewId, threadId, ReviewThreadState.Resolved, actor, cancellationToken);
    }

    /// <summary>
    ///     Marks a thread open again.
    /// </summary>
    /// <param name="projectId">The identity of the owning project.</param>
    /// <param name="reviewId">The review identifier.</param>
    /// <param name="threadId">The identity of the thread; it must belong to the given review.</param>
    /// <param name="actor">The signed-in user who reopens the thread; recorded in the audit trail. Must not be null.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the state is stored; reopening an open thread does nothing.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="actor"/> is null.</exception>
    /// <exception cref="DomainRuleException">Thrown with <see cref="DomainRule.ThreadNotFound"/> when the thread does not exist in the review.</exception>
    public Task ReopenAsync(
        int projectId,
        string reviewId,
        int threadId,
        Actor actor,
        CancellationToken cancellationToken = default)
    {
        return SetStateAsync(projectId, reviewId, threadId, ReviewThreadState.Open, actor, cancellationToken);
    }

    /// <summary>
    ///     Lists all threads of a review, for every file.
    /// </summary>
    /// <param name="projectId">The project identity.</param>
    /// <param name="reviewId">The review identifier.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    ///     The threads in creation order, each with its comments in posting order; empty when there are none
    ///     or the review does not exist.
    /// </returns>
    public Task<IReadOnlyList<ReviewThread>> ListAsync(
        int projectId,
        string reviewId,
        CancellationToken cancellationToken = default)
    {
        return _store.ListThreadsAsync(projectId, reviewId, cancellationToken);
    }

    /// <summary>
    ///     Selects the threads of a review for the thread index, newest activity first.
    /// </summary>
    /// <param name="threads">The threads of the review.</param>
    /// <param name="state">The state to keep, or <c>null</c> to keep every thread.</param>
    /// <returns>
    ///     The matching threads ordered by <see cref="ReviewThread.LastActivity"/> descending, then by
    ///     <see cref="ReviewThread.Id"/> descending so the order is stable when times are equal.
    /// </returns>
    public static IReadOnlyList<ReviewThread> BuildIndex(IEnumerable<ReviewThread> threads, ReviewThreadState? state)
    {
        ArgumentNullException.ThrowIfNull(threads);

        // Sorted in memory because the store cannot order by a time stamp
        return threads
            .Where(thread => state is null || thread.State == state)
            .OrderByDescending(thread => thread.LastActivity)
            .ThenByDescending(thread => thread.Id)
            .ToList();
    }

    /// <summary>
    ///     Words the next action for the reader of a review, from its threads and, when it has one, its checklist.
    /// </summary>
    /// <param name="threads">The threads of the review.</param>
    /// <param name="checklist">
    ///     The checklist progress of the review, or <c>null</c> when the review has no checklist (the wording
    ///     then depends on the threads alone).
    /// </param>
    /// <param name="failedChecks">
    ///     The number of checks answered Fail; each one blocks signing, so it is named in the sentence. Ignored
    ///     without a checklist. Zero (the default) leaves the wording as it was before signing existed.
    /// </param>
    /// <returns>
    ///     One short sentence: the number of unanswered checks, failed checks and open threads, that the checklist
    ///     is complete, that all threads are resolved, or (without a checklist and without threads) to start a discussion.
    /// </returns>
    public static string NextStep(IEnumerable<ReviewThread> threads, ChecklistProgress? checklist = null, int failedChecks = 0)
    {
        ArgumentNullException.ThrowIfNull(threads);

        var all = threads.ToList();
        var open = all.Count(thread => thread.State == ReviewThreadState.Open);

        // Without a checklist the sentence depends on the threads alone
        if (checklist is null)
        {
            return ThreadsOnlyStep(all.Count, open);
        }

        // With a checklist the unanswered checks come first, then failed checks, then the open threads
        var unanswered = checklist.Value.Unanswered;
        var parts = new List<string>();
        if (unanswered > 0)
        {
            parts.Add(Counted(unanswered, "checklist check unanswered", "checklist checks unanswered"));
        }

        if (failedChecks > 0)
        {
            parts.Add(Counted(failedChecks, "check answered Fail", "checks answered Fail"));
        }

        if (open > 0)
        {
            parts.Add(Counted(open, "open thread to resolve", "open threads to resolve"));
        }

        if (parts.Count > 0)
        {
            var sentence = string.Join(", ", parts);
            return unanswered > 0 ? $"{sentence}." : $"Checklist complete; {sentence}.";
        }

        return all.Count > 0 ? "Checklist complete and all threads resolved." : "Checklist complete.";
    }

    /// <summary>
    ///     Words the next action for a review that has no checklist.
    /// </summary>
    /// <param name="threadCount">The number of threads of the review.</param>
    /// <param name="open">The number of open threads.</param>
    /// <returns>One short sentence about the threads.</returns>
    private static string ThreadsOnlyStep(int threadCount, int open)
    {
        if (threadCount == 0)
        {
            return "No discussion yet - select a file and comment on a line.";
        }

        return open switch
        {
            0 => "All threads resolved.",
            1 => "1 open thread to resolve.",
            _ => $"{open} open threads to resolve."
        };
    }

    /// <summary>
    ///     Words a count with the singular or plural form of a phrase.
    /// </summary>
    /// <param name="count">The count.</param>
    /// <param name="singular">The phrase for exactly one.</param>
    /// <param name="plural">The phrase for any other count.</param>
    /// <returns>The count followed by the matching phrase.</returns>
    private static string Counted(int count, string singular, string plural)
    {
        return count == 1 ? $"1 {singular}" : $"{count} {plural}";
    }

    /// <summary>
    ///     Stores a new state for a thread.
    /// </summary>
    /// <param name="projectId">The identity of the owning project.</param>
    /// <param name="reviewId">The review identifier.</param>
    /// <param name="threadId">The identity of the thread.</param>
    /// <param name="state">The state to store.</param>
    /// <param name="actor">The signed-in user; recorded in the audit trail.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the state is stored.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="actor"/> is null.</exception>
    /// <exception cref="DomainRuleException">Thrown with <see cref="DomainRule.ThreadNotFound"/> when the thread does not exist.</exception>
    private async Task SetStateAsync(
        int projectId,
        string reviewId,
        int threadId,
        ReviewThreadState state,
        Actor actor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        await _access.RequireViewAsync(actor, projectId, cancellationToken);

        // The store saves the entry only when the thread exists, so a refused call leaves no trace
        var action = state == ReviewThreadState.Resolved ? "thread.resolved" : "thread.reopened";
        var audit = AuditDraft.By(actor, action, $"thread {threadId}", projectId, reviewId);
        if (!await _store.SetThreadStateAsync(projectId, reviewId, threadId, state, audit, cancellationToken))
        {
            throw ThreadNotFound(threadId);
        }
    }

    /// <summary>
    ///     Builds the exception for a thread that is not part of the review.
    /// </summary>
    /// <param name="threadId">The identity that was not found.</param>
    /// <returns>The exception to throw.</returns>
    private static DomainRuleException ThreadNotFound(int threadId)
    {
        return new DomainRuleException(DomainRule.ThreadNotFound, $"Thread {threadId} does not exist in this review.");
    }

    /// <summary>
    ///     Checks and normalizes a comment text.
    /// </summary>
    /// <param name="text">The candidate text.</param>
    /// <returns>The text without leading and trailing white space.</returns>
    /// <exception cref="DomainRuleException">Thrown with <see cref="DomainRule.CommentTextInvalid"/> when the text is empty or too long.</exception>
    private static string ValidateText(string? text)
    {
        var trimmed = text?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            throw new DomainRuleException(DomainRule.CommentTextInvalid, "The comment text is required.");
        }

        if (trimmed.Length > ReviewLimits.MaxCommentLength)
        {
            throw new DomainRuleException(
                DomainRule.CommentTextInvalid,
                $"The comment text must not be longer than {ReviewLimits.MaxCommentLength} characters.");
        }

        return trimmed;
    }

    /// <summary>
    ///     Checks the shape of an anchor that needs no file access.
    /// </summary>
    /// <param name="startLine">The first line, or <c>null</c> for the whole file.</param>
    /// <param name="endLine">The last line, or <c>null</c> for the whole file.</param>
    /// <exception cref="DomainRuleException">Thrown with <see cref="DomainRule.ThreadAnchorInvalid"/> when the lines are inconsistent.</exception>
    private static void ValidateAnchorShape(int? startLine, int? endLine)
    {
        if ((startLine is null) != (endLine is null))
        {
            throw new DomainRuleException(
                DomainRule.ThreadAnchorInvalid,
                "A line range needs both a start line and an end line.");
        }

        if (startLine is null)
        {
            return;
        }

        if (startLine < 1)
        {
            throw new DomainRuleException(DomainRule.ThreadAnchorInvalid, "Line numbers start at 1.");
        }

        if (endLine < startLine)
        {
            throw new DomainRuleException(
                DomainRule.ThreadAnchorInvalid,
                "The end line must not be before the start line.");
        }
    }

    /// <summary>
    ///     Checks that the chosen side can carry line anchors for this file of this review.
    /// </summary>
    /// <param name="review">The review the file belongs to.</param>
    /// <param name="file">The file the thread is anchored to.</param>
    /// <param name="side">The requested side.</param>
    /// <exception cref="DomainRuleException">Thrown with <see cref="DomainRule.ThreadAnchorInvalid"/> when the file has no lines on that side.</exception>
    private static void ValidateSideForFile(Review review, ReviewFile file, ThreadSide side)
    {
        if (side == ThreadSide.Head)
        {
            // A deleted file has no head content, so there are no head lines to anchor to
            if (file.ChangeKind == ChangeKind.Deleted)
            {
                throw new DomainRuleException(
                    DomainRule.ThreadAnchorInvalid,
                    "A deleted file has no head lines; comment on its baseline lines or on the whole file instead.");
            }

            return;
        }

        // Baseline lines exist only when the review froze a baseline and the file was there before the change
        if (review.BaselineSha is null || file.ChangeKind == ChangeKind.Added)
        {
            throw new DomainRuleException(
                DomainRule.ThreadAnchorInvalid,
                "This file has no baseline lines to comment on.");
        }
    }

    /// <summary>
    ///     Checks that a file is text and has at least the given number of lines on the chosen side.
    /// </summary>
    /// <param name="projectId">The identity of the owning project.</param>
    /// <param name="reviewId">The review identifier.</param>
    /// <param name="filePath">The path of the file; it exists in the review.</param>
    /// <param name="side">The side whose content is counted.</param>
    /// <param name="endLine">The last line the anchor needs.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the range is accepted.</returns>
    /// <exception cref="DomainRuleException">Thrown with <see cref="DomainRule.ThreadAnchorInvalid"/> when the file is binary, empty or too short.</exception>
    private async Task ValidateRangeWithinFileAsync(
        int projectId,
        string reviewId,
        string filePath,
        ThreadSide side,
        int endLine,
        CancellationToken cancellationToken)
    {
        var content = (side == ThreadSide.Baseline
            ? await _store.GetBaselineContentAsync(projectId, reviewId, filePath, cancellationToken)
            : await _store.GetFileContentAsync(projectId, reviewId, filePath, cancellationToken)) ?? [];
        var lines = FileText.TryReadLines(content);
        if (lines is null)
        {
            throw new DomainRuleException(
                DomainRule.ThreadAnchorInvalid,
                "Line comments are not possible on a binary file; comment on the whole file instead.");
        }

        var lineCount = lines.Count;
        if (endLine > lineCount)
        {
            throw new DomainRuleException(
                DomainRule.ThreadAnchorInvalid,
                lineCount == 0
                    ? "The file is empty; comment on the whole file instead."
                    : $"The file has only {lineCount} line(s); the end line is beyond it.");
        }
    }
}
