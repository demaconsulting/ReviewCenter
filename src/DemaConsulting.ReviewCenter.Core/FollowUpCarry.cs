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

namespace DemaConsulting.ReviewCenter.Core;

/// <summary>
///     The pure rules of a follow-up review: whether an earlier review fits a new one, and exactly what is carried over.
/// </summary>
/// <remarks>
///     The class belongs to the <see cref="ReviewService"/> unit and holds no state and does no I/O: <see cref="ReviewService"/>
///     reads the earlier review from the store, calls these methods and hands the result to the store together with the new
///     review, so the rules can be tested without a store. What is carried:
///     <list type="bullet">
///         <item>Pass and not-applicable answers that the submitted ballots of the earlier review agree on, whose check exists
///               in the new checklist with exactly the same text (ordinal
///               comparison of the text the answer was given against). Outcome, justification, answerer and time stay
///               those of the original answer (the row of the lowest slot that answered it); the answer is marked as carried
///               from the immediate source. Fail answers
///               are never carried: the check stays unanswered so a reviewer decides again. When the earlier review is itself
///               a follow-up, its review-level baseline rows (slot 0, what it carried) are carried onward under the same
///               rules for every check no submitted ballot has an answer for, so a follow-up chain loses nothing when the
///               middle review has no submitted ballot (for example no reviewer slot); wherever a submitted ballot has a row,
///               the ballots win.</item>
///         <item>Not-applicable sections (with their reason) that the submitted ballots agree on (or, for a section no
///               submitted ballot has a state for, that the baseline marks), whose section exists in
///               the new checklist with the same title.</item>
///         <item>Open threads whose file is in the new review, with all comments verbatim (each keeping whether it is part
///               of the report and whether it is a resolution note, so a carried Finding stays a Finding and chat stays
///               chat) and in the state Open. The anchor
///               is kept only when the file content the line numbers refer to is identical in both reviews; otherwise the
///               thread is moved to the whole file (the model has a whole-file anchor, so a first line is never guessed) and
///               a system comment (chat) at the end of the thread names the original anchor. Resolved threads are not carried.</item>
///     </list>
///     Reviewer slots, signatures and reopens are never carried, and carrying never makes the creator of the new review the
///     answerer of anything.
/// </remarks>
public static class FollowUpCarry
{
    /// <summary>
    ///     The author name of the system comment that records a moved anchor.
    /// </summary>
    public const string SystemAuthor = "ReviewCenter";

    /// <summary>
    ///     The user name of the system comment; it contains characters a user name can never have, so no user can be it.
    /// </summary>
    public const string SystemUsername = "(system)";

    /// <summary>
    ///     Checks whether an earlier review may be followed by a review with the given source and files.
    /// </summary>
    /// <param name="source">The earlier review, read with the metadata of its files. Must not be null.</param>
    /// <param name="repositoryId">The repository of the new review, or <c>null</c> for an upload.</param>
    /// <param name="reviewSetId">The review-set the new review is planned from, or <c>null</c>.</param>
    /// <param name="newPaths">The paths of the files of the new review. Must not be null.</param>
    /// <returns>
    ///     <c>null</c> when the earlier review fits; otherwise the message that explains why not. When both reviews come
    ///     from a review-set the sets must be the same; otherwise both must come from the same repository (or both from
    ///     uploads) and share at least one file path.
    /// </returns>
    public static string? CheckFit(Review source, int? repositoryId, string? reviewSetId, IEnumerable<string> newPaths)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(newPaths);

        if (source.ReviewSetId is not null && reviewSetId is not null)
        {
            return string.Equals(source.ReviewSetId, reviewSetId, StringComparison.Ordinal)
                ? null
                : $"The review '{source.Id}' was planned from another review-set and cannot be followed up by a review of the set '{reviewSetId}'.";
        }

        if (source.RepositoryId != repositoryId)
        {
            return source.RepositoryId is null || repositoryId is null
                ? $"The review '{source.Id}' and the new review do not have the same kind of source (uploaded files against a repository)."
                : $"The review '{source.Id}' was made from another repository.";
        }

        var common = new HashSet<string>(source.Files.Select(file => file.Path), StringComparer.Ordinal);
        return newPaths.Any(common.Contains)
            ? null
            : $"The review '{source.Id}' and the new review have no file in common.";
    }

    /// <summary>
    ///     Computes what the new review takes over from the earlier one.
    /// </summary>
    /// <param name="sourceId">The identifier of the earlier review (the immediate source).</param>
    /// <param name="source">The record of the earlier review: its checklist, ballots and threads. Must not be null.</param>
    /// <param name="sourceFiles">The files of the earlier review (metadata is enough). Must not be null.</param>
    /// <param name="projectId">The project of both reviews.</param>
    /// <param name="reviewId">The identifier of the new review.</param>
    /// <param name="newFiles">The files of the new review. Must not be null.</param>
    /// <param name="newDefinition">The checklist the new review is frozen with. Must not be null.</param>
    /// <param name="now">The creation time of the new review, used for the system comment of a moved anchor.</param>
    /// <param name="baselineAnswers">
    ///     The review-level (slot 0) answers of the earlier review, or <c>null</c> for none: what the earlier review itself
    ///     carried and what a follow-up of it may therefore carry onward even when it has no submitted ballot. Used only for
    ///     checks that no submitted ballot has an answer for.
    /// </param>
    /// <param name="baselineSections">
    ///     The review-level (slot 0) section states of the earlier review, or <c>null</c> for none; used only for sections
    ///     that no submitted ballot has a state for.
    /// </param>
    /// <returns>The rows to store with the new review and the counts for the audit text.</returns>
    public static ReviewCarryOver Compute(
        string sourceId,
        ReviewRecord source,
        IReadOnlyList<ReviewFile> sourceFiles,
        int projectId,
        string reviewId,
        IReadOnlyList<ReviewFile> newFiles,
        ChecklistDefinition newDefinition,
        DateTimeOffset now,
        IReadOnlyList<ChecklistAnswer>? baselineAnswers = null,
        IReadOnlyList<ChecklistSectionState>? baselineSections = null)
    {
        ArgumentNullException.ThrowIfNull(sourceId);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(sourceFiles);
        ArgumentNullException.ThrowIfNull(reviewId);
        ArgumentNullException.ThrowIfNull(newFiles);
        ArgumentNullException.ThrowIfNull(newDefinition);

        var (answers, sections) = CarryChecklist(
            sourceId, source, projectId, reviewId, newDefinition, baselineAnswers ?? [], baselineSections ?? []);
        var (threads, resolved, dropped, checkLeftOut) =
            CarryThreads(sourceId, source, sourceFiles, projectId, reviewId, newFiles, now);
        return new ReviewCarryOver(answers, sections, threads, resolved, dropped, checkLeftOut);
    }

    /// <summary>
    ///     Lists the files that a message about dropped threads names, in one text.
    /// </summary>
    /// <param name="files">The sorted paths of the files that have open threads and are not in the new review.</param>
    /// <returns>The message of the <see cref="DomainRule.FollowUpInvalid"/> that asks the creator to acknowledge them.</returns>
    public static string DroppedMessage(IReadOnlyList<string> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        const int listed = 10;
        var shown = string.Join(", ", files.Take(listed).Select(file => $"'{file}'"));
        if (files.Count > listed)
        {
            shown += $" and {files.Count - listed} more";
        }

        return $"The earlier review has open threads on {files.Count} file{(files.Count == 1 ? string.Empty : "s")} that " +
               $"{(files.Count == 1 ? "is" : "are")} not in the new review and would not be carried over: {shown}. " +
               "Confirm that you have checked them to create the review anyway.";
    }

    /// <summary>
    ///     Carries the Pass and not-applicable answers and the not-applicable sections.
    /// </summary>
    /// <param name="sourceId">The identifier of the earlier review.</param>
    /// <param name="source">The record of the earlier review.</param>
    /// <param name="projectId">The project of both reviews.</param>
    /// <param name="reviewId">The identifier of the new review.</param>
    /// <param name="newDefinition">The checklist of the new review.</param>
    /// <param name="baselineAnswers">The review-level answers of the earlier review (never null).</param>
    /// <param name="baselineSections">The review-level section states of the earlier review (never null).</param>
    /// <returns>The carried answers and the carried section states, each sorted by identifier.</returns>
    private static (List<ChecklistAnswer> Answers, List<ChecklistSectionState> Sections) CarryChecklist(
        string sourceId,
        ReviewRecord source,
        int projectId,
        string reviewId,
        ChecklistDefinition newDefinition,
        IReadOnlyList<ChecklistAnswer> baselineAnswers,
        IReadOnlyList<ChecklistSectionState> baselineSections)
    {
        var answers = new List<ChecklistAnswer>();
        var sections = new List<ChecklistSectionState>();

        // A review without a checklist has no questions whose wording could be compared, so nothing is carried
        if (source.Checklist is null)
        {
            return (answers, sections);
        }

        var oldDefinition = source.Checklist.Parse();
        var oldItems = ItemTexts(oldDefinition);
        var newItems = ItemTexts(newDefinition);

        // Only what the submitted ballots agree on is carried: the consolidated state holds the representative row of
        // every agreed check and not-applicable section, so a disputed or pending check stays open in the new review
        var view = BallotProjection.Consolidate(source);
        if (view is null)
        {
            return (answers, sections);
        }

        var consolidated = view.State;

        // The baseline rows (what the earlier review itself carried and no submitted ballot has taken over) fill in only
        // the checks and sections that no submitted ballot has a row for; wherever a submitted ballot has a row, the
        // ballots decide, even when they are pending or disputed. The checks of a section that the ballots mark not
        // applicable (or dispute) are not carried, as the section is carried as not applicable instead
        var submittedBallots = source.Ballots.Where(b => b.Status == BallotStatus.Submitted).ToList();
        var coveredItems = submittedBallots.SelectMany(b => b.Answers).Select(a => a.ItemId).ToHashSet(StringComparer.Ordinal);
        var coveredSections = submittedBallots.SelectMany(b => b.SectionStates).Select(s => s.SectionId).ToHashSet(StringComparer.Ordinal);
        var closedSections = view.Result.Sections
            .Where(s => s.Consensus != SectionConsensus.Applicable)
            .Select(s => s.SectionId)
            .Concat(baselineSections
                .Where(s => s.SlotId == ChecklistAnswer.BaselineSlotId && !s.Applicable && !coveredSections.Contains(s.SectionId))
                .Select(s => s.SectionId))
            .ToHashSet(StringComparer.Ordinal);
        var sectionOfItem = oldDefinition.Sections
            .SelectMany(section => section.Items.Select(item => (item.Id, SectionId: section.Id)))
            .ToDictionary(pair => pair.Id, pair => pair.SectionId, StringComparer.Ordinal);

        var agreedAnswers = oldDefinition.Sections
            .SelectMany(section => section.Items)
            .Select(item => consolidated.AnswerFor(item.Id))
            .OfType<ChecklistAnswer>()
            .Concat(baselineAnswers
                .Where(a => a.SlotId == ChecklistAnswer.BaselineSlotId
                    && !coveredItems.Contains(a.ItemId)
                    && !(sectionOfItem.TryGetValue(a.ItemId, out var sectionId) && closedSections.Contains(sectionId))))
            .OrderBy(a => a.ItemId, StringComparer.Ordinal);
        foreach (var answer in agreedAnswers)
        {
            // A Fail is not carried: the check stays open so a reviewer has to decide again. A check is carried only
            // when the question it was answered against is unchanged
            if (answer.Outcome == ChecklistOutcome.Fail
                || !oldItems.TryGetValue(answer.ItemId, out var oldText)
                || !newItems.TryGetValue(answer.ItemId, out var newText)
                || !string.Equals(oldText, newText, StringComparison.Ordinal))
            {
                continue;
            }

            answers.Add(new ChecklistAnswer
            {
                ProjectId = projectId,
                ReviewId = reviewId,
                ItemId = answer.ItemId,
                Outcome = answer.Outcome,
                Justification = answer.Justification,
                AnsweredBy = answer.AnsweredBy,
                AnsweredByUsername = answer.AnsweredByUsername,
                AnsweredAt = answer.AnsweredAt,
                CarriedFromReviewId = sourceId
            });
        }

        var newTitles = newDefinition.Sections.ToDictionary(s => s.Id, s => s.Title, StringComparer.Ordinal);
        var agreedStates = oldDefinition.Sections
            .Select(section => consolidated.SectionStateFor(section.Id))
            .OfType<ChecklistSectionState>()
            .Concat(baselineSections
                .Where(s => s.SlotId == ChecklistAnswer.BaselineSlotId && !coveredSections.Contains(s.SectionId)))
            .Where(s => !s.Applicable)
            .OrderBy(s => s.SectionId, StringComparer.Ordinal);
        foreach (var state in agreedStates)
        {
            if (!newTitles.TryGetValue(state.SectionId, out var title)
                || !string.Equals(title, oldDefinition.Sections.FirstOrDefault(s => s.Id == state.SectionId)?.Title, StringComparison.Ordinal))
            {
                continue;
            }

            sections.Add(new ChecklistSectionState
            {
                ProjectId = projectId,
                ReviewId = reviewId,
                SectionId = state.SectionId,
                Applicable = false,
                Reason = state.Reason,
                SetBy = state.SetBy,
                SetByUsername = state.SetByUsername,
                SetAt = state.SetAt,
                CarriedFromReviewId = sourceId
            });
        }

        return (answers, sections);
    }

    /// <summary>
    ///     Carries the open threads whose file is in the new review; a thread about a check is never carried.
    /// </summary>
    /// <remarks>
    ///     A check thread belongs to the discussion of the review it was started in (the checklist of the new review is
    ///     answered afresh), so it is left out before any other rule: it is neither counted as a resolved thread nor
    ///     reported as a dropped file, and it never needs the creator's acknowledgement. Its count is reported
    ///     separately.
    /// </remarks>
    /// <param name="sourceId">The identifier of the earlier review.</param>
    /// <param name="source">The record of the earlier review.</param>
    /// <param name="sourceFiles">The files of the earlier review.</param>
    /// <param name="projectId">The project of both reviews.</param>
    /// <param name="reviewId">The identifier of the new review.</param>
    /// <param name="newFiles">The files of the new review.</param>
    /// <param name="now">The creation time of the new review.</param>
    /// <returns>
    ///     The carried threads, the number of resolved threads left out, the sorted files of dropped open threads and
    ///     the number of check threads (open or resolved) left out.
    /// </returns>
    private static (List<ReviewThread> Threads, int Resolved, List<string> Dropped, int CheckLeftOut) CarryThreads(
        string sourceId,
        ReviewRecord source,
        IReadOnlyList<ReviewFile> sourceFiles,
        int projectId,
        string reviewId,
        IReadOnlyList<ReviewFile> newFiles,
        DateTimeOffset now)
    {
        var oldByPath = sourceFiles.ToDictionary(f => f.Path, StringComparer.Ordinal);
        var newByPath = newFiles.ToDictionary(f => f.Path, StringComparer.Ordinal);
        var threads = new List<ReviewThread>();
        var dropped = new SortedSet<string>(StringComparer.Ordinal);
        var resolved = 0;
        var checkLeftOut = 0;
        foreach (var thread in source.Threads.OrderBy(t => t.Id))
        {
            // A check thread has no file to follow: count it and move on
            if (thread.IsCheckThread)
            {
                checkLeftOut++;
                continue;
            }

            if (thread.State != ReviewThreadState.Open)
            {
                resolved++;
                continue;
            }

            if (!newByPath.TryGetValue(thread.FilePath, out var newFile))
            {
                dropped.Add(thread.FilePath);
                continue;
            }

            threads.Add(CarryThread(sourceId, thread, oldByPath.GetValueOrDefault(thread.FilePath), newFile, projectId, reviewId, now));
        }

        return (threads, resolved, dropped.ToList(), checkLeftOut);
    }

    /// <summary>
    ///     Copies one open thread into the new review, keeping or moving its anchor.
    /// </summary>
    /// <param name="sourceId">The identifier of the earlier review.</param>
    /// <param name="thread">The open thread of the earlier review.</param>
    /// <param name="oldFile">The file of the earlier review the thread belongs to, or <c>null</c> when it is unknown.</param>
    /// <param name="newFile">The same file in the new review.</param>
    /// <param name="projectId">The project of both reviews.</param>
    /// <param name="reviewId">The identifier of the new review.</param>
    /// <param name="now">The creation time of the new review.</param>
    /// <returns>The thread of the new review, without identity.</returns>
    private static ReviewThread CarryThread(
        string sourceId,
        ReviewThread thread,
        ReviewFile? oldFile,
        ReviewFile newFile,
        int projectId,
        string reviewId,
        DateTimeOffset now)
    {
        // A whole-file anchor never goes stale; a line anchor stays only when the content its numbers refer to is the same
        var keep = thread.StartLine is null || SameContent(thread.Side, oldFile, newFile);
        var comments = thread.Comments
            .OrderBy(c => c.Id)
            .Select(c => new ReviewComment
            {
                Author = c.Author,
                AuthorUsername = c.AuthorUsername,
                Text = c.Text,
                Created = c.Created,
                IncludeInReport = c.IncludeInReport,
                Kind = c.Kind
            })
            .ToList();
        if (!keep)
        {
            // The original anchor is told to the reviewers in a comment of the system, appended after the original
            // comments so their order is not disturbed; it is chat (not included in the report), so it never
            // changes what a signature of the new review covers
            comments.Add(new ReviewComment
            {
                Author = SystemAuthor,
                AuthorUsername = SystemUsername,
                Text = $"Carried over from review '{sourceId}'. The file is not the same as in that review, so the thread, which was " +
                       $"anchored to {AnchorText(thread)}, now refers to the whole file.",
                Created = now,
                IncludeInReport = false,
                Kind = ReviewCommentKind.Comment
            });
        }

        return new ReviewThread
        {
            ProjectId = projectId,
            ReviewId = reviewId,
            FilePath = thread.FilePath,
            Side = keep ? thread.Side : ThreadSide.Head,
            StartLine = keep ? thread.StartLine : null,
            EndLine = keep ? thread.EndLine : null,
            State = ReviewThreadState.Open,
            Created = thread.Created,
            CarriedFromReviewId = sourceId,
            CarriedFromThreadId = thread.Id,
            Comments = comments
        };
    }

    /// <summary>
    ///     Tells whether the content a line anchor refers to is identical in both reviews.
    /// </summary>
    /// <param name="side">The side the line numbers refer to.</param>
    /// <param name="oldFile">The file in the earlier review, or <c>null</c> when it is unknown.</param>
    /// <param name="newFile">The file in the new review.</param>
    /// <returns><c>true</c> when the hash of that side is the same and known in both.</returns>
    private static bool SameContent(ThreadSide side, ReviewFile? oldFile, ReviewFile newFile)
    {
        if (oldFile is null)
        {
            return false;
        }

        var (oldHash, newHash) = side == ThreadSide.Head
            ? (oldFile.Sha256, newFile.Sha256)
            : (oldFile.BaselineSha256, newFile.BaselineSha256);
        return !string.IsNullOrEmpty(oldHash) && string.Equals(oldHash, newHash, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Describes the anchor of a thread for the system comment.
    /// </summary>
    /// <param name="thread">The thread of the earlier review.</param>
    /// <returns>For example <c>head lines 3 to 5</c> or <c>baseline line 7</c>.</returns>
    private static string AnchorText(ReviewThread thread)
    {
        var side = thread.Side == ThreadSide.Head ? "head" : "baseline";
        var start = thread.StartLine!.Value.ToString(CultureInfo.InvariantCulture);
        return thread.EndLine is { } end && end != thread.StartLine
            ? $"{side} lines {start} to {end.ToString(CultureInfo.InvariantCulture)}"
            : $"{side} line {start}";
    }

    /// <summary>
    ///     Lists the question text of every check of a definition by check identifier.
    /// </summary>
    /// <param name="definition">The definition.</param>
    /// <returns>The texts; with a duplicate identifier the first check wins (a definition is expected to have none).</returns>
    private static Dictionary<string, string> ItemTexts(ChecklistDefinition definition)
    {
        var texts = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in definition.Sections.SelectMany(s => s.Items))
        {
            texts.TryAdd(item.Id, item.Text);
        }

        return texts;
    }
}
