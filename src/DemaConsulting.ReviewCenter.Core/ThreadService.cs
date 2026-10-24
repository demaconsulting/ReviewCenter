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
///     or to a range of lines and always starts with one comment; anyone can reply. Comments are
///     immutable, so the service offers no way to edit or delete one; the only change to an existing
///     thread is its <see cref="ReviewThreadState"/>. There is no sign-in yet, so authors are unverified text.
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
    ///     The clock that supplies creation times.
    /// </summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>
    ///     Initializes a new instance of the <see cref="ThreadService"/> class.
    /// </summary>
    /// <param name="store">The store that persists threads. Must not be null.</param>
    /// <param name="timeProvider">The clock used for creation times; injected so tests control time. Must not be null.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    public ThreadService(IReviewStore store, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _store = store;
        _timeProvider = timeProvider;
    }

    /// <summary>
    ///     Starts a thread on a file of a review, with its first comment.
    /// </summary>
    /// <param name="projectId">The identity of the owning project.</param>
    /// <param name="reviewId">The review identifier.</param>
    /// <param name="filePath">The path of the file within the review (ordinal comparison).</param>
    /// <param name="startLine">
    ///     The first line of the anchor (1-based), or <c>null</c> to anchor the thread to the whole file.
    /// </param>
    /// <param name="endLine">
    ///     The last line of the anchor (inclusive); <c>null</c> exactly when <paramref name="startLine"/> is
    ///     <c>null</c>. A range requires a text file with at least that many lines (see <see cref="FileText"/>).
    /// </param>
    /// <param name="author">
    ///     The author name; leading and trailing white space is removed. Must not be empty and must not
    ///     exceed <see cref="ReviewLimits.MaxAuthorLength"/> characters.
    /// </param>
    /// <param name="text">
    ///     The comment text; leading and trailing white space is removed. Must not be empty and must not
    ///     exceed <see cref="ReviewLimits.MaxCommentLength"/> characters.
    /// </param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The stored thread in the <see cref="ReviewThreadState.Open"/> state, with its first comment.</returns>
    /// <exception cref="DomainRuleException">
    ///     Thrown when a rule is violated (see <see cref="DomainRule"/>); nothing is stored then.
    /// </exception>
    public async Task<ReviewThread> StartThreadAsync(
        int projectId,
        string reviewId,
        string filePath,
        int? startLine,
        int? endLine,
        string? author,
        string? text,
        CancellationToken cancellationToken = default)
    {
        // Validate everything that needs no I/O first so a bad request fails fast and cheaply
        var trimmedAuthor = ValidateAuthor(author);
        var trimmedText = ValidateText(text);
        ValidateAnchorShape(startLine, endLine);

        // The file must exist in the review; the metadata read does not load any file bytes
        var review = await _store.GetReviewAsync(projectId, reviewId, cancellationToken);
        if (review is null || !review.Files.Any(file => string.Equals(file.Path, filePath, StringComparison.Ordinal)))
        {
            throw new DomainRuleException(
                DomainRule.ThreadAnchorInvalid,
                $"The file '{filePath}' does not exist in this review.");
        }

        // Only a line range needs the bytes, to count lines the way the page shows them
        if (startLine is not null)
        {
            await ValidateRangeWithinFileAsync(projectId, reviewId, filePath, endLine!.Value, cancellationToken);
        }

        var now = _timeProvider.GetUtcNow();
        var thread = new ReviewThread
        {
            ProjectId = projectId,
            ReviewId = reviewId,
            FilePath = filePath,
            StartLine = startLine,
            EndLine = endLine,
            State = ReviewThreadState.Open,
            Created = now,
            Comments = [new ReviewComment { Author = trimmedAuthor, Text = trimmedText, Created = now }]
        };

        return await _store.AddThreadAsync(thread, cancellationToken);
    }

    /// <summary>
    ///     Adds a reply to an existing thread of a review.
    /// </summary>
    /// <param name="projectId">The identity of the owning project.</param>
    /// <param name="reviewId">The review identifier.</param>
    /// <param name="threadId">The identity of the thread; it must belong to the given review.</param>
    /// <param name="author">The author name; same rules as for a new thread.</param>
    /// <param name="text">The reply text; same rules as for a new thread.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    ///     The stored comment. Earlier comments are untouched and the thread state does not change, so a
    ///     resolved thread can still receive replies.
    /// </returns>
    /// <exception cref="DomainRuleException">
    ///     Thrown when a rule is violated (see <see cref="DomainRule"/>); nothing is stored then.
    /// </exception>
    public async Task<ReviewComment> ReplyAsync(
        int projectId,
        string reviewId,
        int threadId,
        string? author,
        string? text,
        CancellationToken cancellationToken = default)
    {
        var comment = new ReviewComment
        {
            Author = ValidateAuthor(author),
            Text = ValidateText(text),
            Created = _timeProvider.GetUtcNow()
        };

        return await _store.AddCommentAsync(projectId, reviewId, threadId, comment, cancellationToken)
               ?? throw ThreadNotFound(threadId);
    }

    /// <summary>
    ///     Marks a thread resolved.
    /// </summary>
    /// <param name="projectId">The identity of the owning project.</param>
    /// <param name="reviewId">The review identifier.</param>
    /// <param name="threadId">The identity of the thread; it must belong to the given review.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the state is stored; resolving a resolved thread does nothing.</returns>
    /// <exception cref="DomainRuleException">Thrown with <see cref="DomainRule.ThreadNotFound"/> when the thread does not exist in the review.</exception>
    public Task ResolveAsync(int projectId, string reviewId, int threadId, CancellationToken cancellationToken = default)
    {
        return SetStateAsync(projectId, reviewId, threadId, ReviewThreadState.Resolved, cancellationToken);
    }

    /// <summary>
    ///     Marks a thread open again.
    /// </summary>
    /// <param name="projectId">The identity of the owning project.</param>
    /// <param name="reviewId">The review identifier.</param>
    /// <param name="threadId">The identity of the thread; it must belong to the given review.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the state is stored; reopening an open thread does nothing.</returns>
    /// <exception cref="DomainRuleException">Thrown with <see cref="DomainRule.ThreadNotFound"/> when the thread does not exist in the review.</exception>
    public Task ReopenAsync(int projectId, string reviewId, int threadId, CancellationToken cancellationToken = default)
    {
        return SetStateAsync(projectId, reviewId, threadId, ReviewThreadState.Open, cancellationToken);
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
    /// <returns>
    ///     One short sentence: the number of unanswered checks and open threads, that the checklist is complete,
    ///     that all threads are resolved, or (without a checklist and without threads) to start a discussion.
    /// </returns>
    public static string NextStep(IEnumerable<ReviewThread> threads, ChecklistProgress? checklist = null)
    {
        ArgumentNullException.ThrowIfNull(threads);

        var all = threads.ToList();
        var open = all.Count(thread => thread.State == ReviewThreadState.Open);

        // Without a checklist the sentence depends on the threads alone
        if (checklist is null)
        {
            if (all.Count == 0)
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

        // With a checklist the unanswered checks come first, then the open threads
        var unanswered = checklist.Value.Unanswered;
        var checks = unanswered == 1 ? "1 checklist check unanswered" : $"{unanswered} checklist checks unanswered";
        var threadsToResolve = open == 1 ? "1 open thread to resolve" : $"{open} open threads to resolve";
        if (unanswered > 0)
        {
            return open > 0 ? $"{checks}, {threadsToResolve}." : $"{checks}.";
        }

        if (open > 0)
        {
            return $"Checklist complete; {threadsToResolve}.";
        }

        return all.Count > 0 ? "Checklist complete and all threads resolved." : "Checklist complete.";
    }

    /// <summary>
    ///     Stores a new state for a thread.
    /// </summary>
    /// <param name="projectId">The identity of the owning project.</param>
    /// <param name="reviewId">The review identifier.</param>
    /// <param name="threadId">The identity of the thread.</param>
    /// <param name="state">The state to store.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the state is stored.</returns>
    /// <exception cref="DomainRuleException">Thrown with <see cref="DomainRule.ThreadNotFound"/> when the thread does not exist.</exception>
    private async Task SetStateAsync(
        int projectId,
        string reviewId,
        int threadId,
        ReviewThreadState state,
        CancellationToken cancellationToken)
    {
        if (!await _store.SetThreadStateAsync(projectId, reviewId, threadId, state, cancellationToken))
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
    ///     Checks and normalizes a comment author.
    /// </summary>
    /// <param name="author">The candidate author.</param>
    /// <returns>The author without leading and trailing white space.</returns>
    /// <exception cref="DomainRuleException">Thrown with <see cref="DomainRule.CommentAuthorInvalid"/> when the author is empty or too long.</exception>
    private static string ValidateAuthor(string? author)
    {
        var trimmed = author?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            throw new DomainRuleException(DomainRule.CommentAuthorInvalid, "The author name is required.");
        }

        if (trimmed.Length > ReviewLimits.MaxAuthorLength)
        {
            throw new DomainRuleException(
                DomainRule.CommentAuthorInvalid,
                $"The author name must not be longer than {ReviewLimits.MaxAuthorLength} characters.");
        }

        return trimmed;
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
    ///     Checks that a file is text and has at least the given number of lines.
    /// </summary>
    /// <param name="projectId">The identity of the owning project.</param>
    /// <param name="reviewId">The review identifier.</param>
    /// <param name="filePath">The path of the file; it exists in the review.</param>
    /// <param name="endLine">The last line the anchor needs.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the range is accepted.</returns>
    /// <exception cref="DomainRuleException">Thrown with <see cref="DomainRule.ThreadAnchorInvalid"/> when the file is binary, empty or too short.</exception>
    private async Task ValidateRangeWithinFileAsync(
        int projectId,
        string reviewId,
        string filePath,
        int endLine,
        CancellationToken cancellationToken)
    {
        var content = await _store.GetFileContentAsync(projectId, reviewId, filePath, cancellationToken) ?? [];
        var text = FileText.TryDecode(content);
        if (text is null)
        {
            throw new DomainRuleException(
                DomainRule.ThreadAnchorInvalid,
                "Line comments are not possible on a binary file; comment on the whole file instead.");
        }

        var lineCount = FileText.SplitLines(text).Count;
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
