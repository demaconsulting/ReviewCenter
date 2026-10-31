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
///     Creates and reads reviews, enforcing the review rules and computing the content fingerprint.
/// </summary>
/// <remarks>
///     The service is the single place where review rules live. A review is created in the
///     <see cref="ReviewState.Draft"/> state from a set of uploaded files; the bytes are stored
///     verbatim and the fingerprint is computed with <see cref="Fingerprint"/>. It holds no state
///     of its own and is safe for concurrent use as long as the injected <see cref="IReviewStore"/> is.
/// </remarks>
public sealed class ReviewService
{
    /// <summary>
    ///     The action recorded when the due date of a review is set, changed or cleared after creation.
    /// </summary>
    public const string DueDateChangedAction = "review.due-date-changed";

    /// <summary>
    ///     How many years ahead a due date may lie: a sanity bound that catches typing errors such as the wrong year.
    /// </summary>
    public const int MaxDueDateYearsAhead = 2;

    /// <summary>
    ///     The store that persists reviews.
    /// </summary>
    private readonly IReviewStore _store;

    /// <summary>
    ///     Decides who may see which project.
    /// </summary>
    private readonly IProjectAccess _access;

    /// <summary>
    ///     The clock that supplies creation times.
    /// </summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>
    ///     The repository registry, or <c>null</c> when reviews cannot be created from repositories.
    /// </summary>
    private readonly RepositoryService? _repositories;

    /// <summary>
    ///     Opens sessions on git repositories, or <c>null</c> when reviews cannot be created from repositories.
    /// </summary>
    private readonly IGitRepositoryProvider? _git;

    /// <summary>
    ///     Initializes a new instance of the <see cref="ReviewService"/> class.
    /// </summary>
    /// <param name="store">The store that persists reviews. Must not be null.</param>
    /// <param name="access">The access rules; creating a review needs a member of the project. Must not be null.</param>
    /// <param name="timeProvider">The clock used for creation times; injected so tests control time. Must not be null.</param>
    /// <param name="repositories">The repository registry; needed only by <see cref="CreateFromRepositoryAsync"/>.</param>
    /// <param name="git">The git access; needed only by <see cref="CreateFromRepositoryAsync"/>.</param>
    /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
    public ReviewService(
        IReviewStore store,
        IProjectAccess access,
        TimeProvider timeProvider,
        RepositoryService? repositories = null,
        IGitRepositoryProvider? git = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(access);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _store = store;
        _access = access;
        _timeProvider = timeProvider;
        _repositories = repositories;
        _git = git;
    }

    /// <summary>
    ///     Creates a review in the draft state from a set of files.
    /// </summary>
    /// <param name="projectId">The identity of the owning project.</param>
    /// <param name="id">
    ///     The review identifier: 1 to <see cref="ReviewLimits.MaxReviewIdLength"/> characters from
    ///     letters, digits, dots, underscores and hyphens, starting with a letter or digit. Must be
    ///     unique within the project (other projects may use the same id).
    /// </param>
    /// <param name="title">
    ///     The title; leading and trailing white space is removed. Must not be empty and must not
    ///     exceed <see cref="ReviewLimits.MaxReviewTitleLength"/> characters.
    /// </param>
    /// <param name="files">
    ///     The files of the review. Must not be null. Must contain at least one file, at most
    ///     <see cref="ReviewLimits.MaxFileCount"/>; paths must be non-empty, at most
    ///     <see cref="ReviewLimits.MaxPathLength"/> characters, and unique (ordinal comparison); each file at most
    ///     <see cref="ReviewLimits.MaxFileSizeBytes"/> and all together at most
    ///     <see cref="ReviewLimits.MaxTotalSizeBytes"/> bytes.
    /// </param>
    /// <param name="actor">The signed-in user creating the review; recorded in the audit trail. Must not be null.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <param name="dueDate">
    ///     The optional due date: a calendar day in UTC that is not before today and not more than
    ///     <see cref="MaxDueDateYearsAhead"/> years ahead; <c>null</c> for none.
    /// </param>
    /// <param name="followsReviewId">
    ///     The identifier of an earlier review of the same project that the new review follows up, or <c>null</c> (or
    ///     blank) for an ordinary review; see <see cref="FollowUpCarry"/> for what is carried over.
    /// </param>
    /// <param name="acknowledgeDroppedThreads">
    ///     <c>true</c> when the creator has checked the open threads of the earlier review whose files are not in the new
    ///     review (they are not carried); without it such threads make the creation fail with
    ///     <see cref="DomainRule.FollowUpInvalid"/>.
    /// </param>
    /// <returns>The created review, including its fingerprint and file metadata.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="files"/> or <paramref name="actor"/> is null.</exception>
    /// <exception cref="DomainRuleException">
    ///     Thrown when any rule is violated (see <see cref="DomainRule"/>), among them
    ///     <see cref="DomainRule.DueDateInvalid"/> and <see cref="DomainRule.FollowUpInvalid"/>; nothing is stored then.
    /// </exception>
    public async Task<Review> CreateDraftAsync(
        int projectId,
        string? id,
        string? title,
        IReadOnlyList<UploadedFile> files,
        Actor actor,
        CancellationToken cancellationToken = default,
        DateOnly? dueDate = null,
        string? followsReviewId = null,
        bool acknowledgeDroppedThreads = false)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(actor);

        // Whoever may not see the project learns nothing about it, not even from a validation message; a member
        // who may only read is refused as well
        await _access.RequireContributeAsync(actor, projectId, cancellationToken);

        // Validate everything that needs no I/O first so a bad request fails fast and cheaply
        ValidateId(id);
        var trimmedTitle = ValidateTitle(title);
        ValidateFiles(files);
        ValidateNewDueDate(dueDate);

        // The id must be free within the project. The store backs the id check up
        // with a uniqueness constraint, which covers two requests racing past this check
        await EnsureFreeIdAsync(projectId, id!, cancellationToken);

        // The checklist the review is frozen with: the project's own definition, else the built-in default
        var checklistDefinition = await ResolveChecklistDefinitionAsync(projectId, cancellationToken);

        // Keep the bytes verbatim and record the size and hash of each file alongside them
        var reviewFiles = files
            .Select(file => new ReviewFile
            {
                ProjectId = projectId,
                ReviewId = id!,
                Path = file.Path,
                Content = file.Content,
                Size = file.Content.Length,
                Sha256 = Fingerprint.ComputeFileHash(file.Content)
            })
            .ToList();

        // The fingerprint covers the file hashes only; see Fingerprint for the exact algorithm
        var created = _timeProvider.GetUtcNow();
        var follow = NormalizeFollows(followsReviewId);
        var carry = await PrepareFollowUpAsync(
            projectId, id!, follow, acknowledgeDroppedThreads, null, null, reviewFiles, checklistDefinition, created, cancellationToken);
        var review = new Review
        {
            ProjectId = projectId,
            Id = id!,
            Title = trimmedTitle,
            State = ReviewState.Draft,
            Created = created,
            CreatedBy = actor.Username,
            CreatedViaApi = actor.ViaApiToken,
            DueDate = dueDate,
            ContentFingerprint = Fingerprint.Compute(reviewFiles.Select(file => file.Sha256)),
            Files = reviewFiles,

            // The checklist is frozen now: later changes of the project's definition or of the default never alter
            // this review's checks
            Checklist = NewChecklist(projectId, id!, checklistDefinition),
            FollowsReviewId = follow,
            CarriedAnswerCount = carry?.Answers.Count ?? 0,
            CarriedThreadCount = carry?.Threads.Count ?? 0,
            CarryOver = carry
        };

        // An API-created review says so in the trail (at the end: the upload detail is short enough to always fit); a
        // follow-up uses a shorter title so the follow-up text always fits as well
        var titleText = follow is null ? trimmedTitle : AuditText.Clean(trimmedTitle, 100);
        await _store.AddReviewAsync(
            review,
            AuditDraft.By(
                actor,
                "review.created",
                $"review '{titleText}', fingerprint {review.ContentFingerprint}, {reviewFiles.Count} file{(reviewFiles.Count == 1 ? string.Empty : "s")}{FollowText(follow, carry)}{DueText(dueDate)}{CreatedViaText(actor)}",
                projectId,
                id),
            cancellationToken);
        return review;
    }

    /// <summary>
    ///     Creates a review in the draft state from the files that changed between two revisions of a registered
    ///     repository.
    /// </summary>
    /// <param name="projectId">The identity of the owning project.</param>
    /// <param name="id">The review identifier; same rules as for <see cref="CreateDraftAsync"/>.</param>
    /// <param name="title">The title; same rules as for <see cref="CreateDraftAsync"/>.</param>
    /// <param name="source">
    ///     The repository, the head and baseline revisions and the file selection to review (see
    ///     <see cref="RepositorySource"/> for what each value may hold). Must not be null.
    /// </param>
    /// <param name="actor">The signed-in user creating the review; recorded in the audit trail. Must not be null.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <param name="dueDate">The optional due date; same rules as for <see cref="CreateDraftAsync"/>.</param>
    /// <param name="followsReviewId">The earlier review to follow up; same rules as for <see cref="CreateDraftAsync"/>.</param>
    /// <param name="acknowledgeDroppedThreads">Whether dropped open threads are acknowledged; see <see cref="CreateDraftAsync"/>.</param>
    /// <returns>The created review and the entries that were skipped (symbolic links, submodules, unusable paths).</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="source"/> or <paramref name="actor"/> is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the service was created without repository access.</exception>
    /// <exception cref="DomainRuleException">
    ///     Thrown when a rule is violated (see <see cref="DomainRule"/>), among them
    ///     <see cref="DomainRule.NothingToReview"/> when no file matching the selection changed,
    ///     <see cref="DomainRule.DueDateInvalid"/> and <see cref="DomainRule.FollowUpInvalid"/>; nothing is stored then.
    /// </exception>
    /// <remarks>
    ///     The server fetches the repository, resolves both revisions to commits, lists the changes, keeps those that
    ///     match the selection and reads both sides of each file. The review then holds frozen copies of all of it
    ///     (paths, change kinds, baseline and head bytes, both commits and the selection); nothing reads the repository
    ///     again. The fingerprint covers the files that exist at the head (added and modified), so it equals the
    ///     ReviewMark fingerprint of those files.
    /// </remarks>
    public async Task<RepositoryReviewResult> CreateFromRepositoryAsync(
        int projectId,
        string? id,
        string? title,
        RepositorySource source,
        Actor actor,
        CancellationToken cancellationToken = default,
        DateOnly? dueDate = null,
        string? followsReviewId = null,
        bool acknowledgeDroppedThreads = false)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(actor);
        if (_repositories is null || _git is null)
        {
            throw new InvalidOperationException("This service was created without access to repositories.");
        }

        await _access.RequireContributeAsync(actor, projectId, cancellationToken);

        // Validate everything that needs no I/O first so a bad request fails fast and cheaply
        ValidateId(id);
        var trimmedTitle = ValidateTitle(title);
        var head = RevisionValidator.Validate(source.HeadRevision?.Trim());
        var baseline = string.IsNullOrWhiteSpace(source.BaselineRevision) ? null : RevisionValidator.Validate(source.BaselineRevision.Trim());
        var (patterns, selectionText) = ParseSelection(source.Selection);
        ValidateNewDueDate(dueDate);

        await EnsureFreeIdAsync(projectId, id!, cancellationToken);
        var access = await _repositories.GetAccessAsync(projectId, source.RepositoryId, cancellationToken);

        // The session resolves both revisions; everything after that is shared with the planner
        await using var session = await _git.OpenAsync(access, cancellationToken);
        var headSha = await session.ResolveRevisionAsync(head, cancellationToken);
        var baselineSha = baseline is null ? null : await session.ResolveRevisionAsync(baseline, cancellationToken);
        return await CreateFromSessionAsync(
            session,
            access,
            projectId,
            new SessionReviewRequest(
                id!, trimmedTitle, headSha, baselineSha, patterns, selectionText, null, head, baseline, dueDate,
                NormalizeFollows(followsReviewId), acknowledgeDroppedThreads),
            actor,
            cancellationToken);
    }

    /// <summary>
    ///     Creates a review in the draft state from the changes between two resolved commits, using a session that
    ///     is already open.
    /// </summary>
    /// <remarks>
    ///     The planner opens one session for a whole batch of reviews and calls this method once per review, so the
    ///     repository is fetched once. The caller has already validated the id, title and selection (see
    ///     <see cref="ValidateId"/>, <see cref="ValidateTitle"/> and <see cref="ParseSelection"/>) and has checked
    ///     the project and the id; this method checks the review-set id, lists and reads the changes and stores the
    ///     review with its audit entry in one transaction, so the review exists completely or not at all.
    /// </remarks>
    /// <param name="session">The open repository session. Must not be null.</param>
    /// <param name="access">The repository the session reads. Must not be null.</param>
    /// <param name="projectId">The identity of the owning project.</param>
    /// <param name="request">What to create: the validated identifier, title, commits and selection of the review.</param>
    /// <param name="actor">The signed-in user creating the review. Must not be null.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The created review and the entries that were skipped.</returns>
    /// <exception cref="DomainRuleException">
    ///     Thrown when a rule is violated (see <see cref="DomainRule"/>); nothing is stored then.
    /// </exception>
    internal async Task<RepositoryReviewResult> CreateFromSessionAsync(
        IGitRepository session,
        RepositoryAccess access,
        int projectId,
        SessionReviewRequest request,
        Actor actor,
        CancellationToken cancellationToken)
    {
        var (id, trimmedTitle, headSha, baselineSha, patterns, selectionText, reviewSetId, headLabel, baselineLabel, dueDate, follow, acknowledgeDroppedThreads) = request;

        // The due date is checked here too, so every creation path (the planner calls this method directly) is covered
        // and a bad date fails before the repository is read
        ValidateNewDueDate(dueDate);

        // The set id is stored, bound by signatures and written to the audit trail, so it is bounded here
        if (reviewSetId is not null && (reviewSetId.Length == 0 || reviewSetId.Length > ReviewLimits.MaxReviewSetIdLength))
        {
            throw new DomainRuleException(
                DomainRule.ReviewSetConfigInvalid,
                $"A review-set id must be 1 to {ReviewLimits.MaxReviewSetIdLength} characters.");
        }

        // The checklist the review is frozen with (the project's own definition, else the default); resolved first so
        // a definition that cannot be used fails the creation before the repository is read
        var checklistDefinition = await ResolveChecklistDefinitionAsync(projectId, cancellationToken);

        // Deleted files are matched by their path like any other
        var changeSet = await session.ListChangesAsync(baselineSha, headSha, cancellationToken);
        var selected = changeSet.Changes
            .Where(change => SelectionMatcher.IsMatch(change.Path, patterns))
            .OrderBy(change => change.Path, StringComparer.Ordinal)
            .ToList();
        var skipped = changeSet.Skipped
            .Where(entry => SelectionMatcher.IsMatch(entry.Path, patterns))
            .OrderBy(entry => entry.Path, StringComparer.Ordinal)
            .ToList();
        if (selected.Count == 0)
        {
            throw new DomainRuleException(
                DomainRule.NothingToReview,
                "Nothing to review: no files matching the selection changed between " +
                $"{baselineLabel ?? "the beginning of the history"} and {headLabel}");
        }

        if (selected.Count > ReviewLimits.MaxFileCount)
        {
            throw new DomainRuleException(
                DomainRule.TooManyFiles,
                $"A review can have at most {ReviewLimits.MaxFileCount} files; the selection matches {selected.Count}.");
        }

        // The limits are checked from the sizes first, so a huge file is never read
        await CheckSizesAsync(session, selected, cancellationToken);
        var files = new List<ReviewFile>();
        foreach (var change in selected)
        {
            files.Add(await ReadFileAsync(session, projectId, id, change, cancellationToken));
        }

        // Only files that exist at the head take part in the fingerprint, as in the ReviewMark checkout
        var created = _timeProvider.GetUtcNow();
        var carry = await PrepareFollowUpAsync(
            projectId, id, follow, acknowledgeDroppedThreads, access.RepositoryId, reviewSetId, files, checklistDefinition, created, cancellationToken);
        var review = new Review
        {
            ProjectId = projectId,
            Id = id,
            Title = trimmedTitle,
            State = ReviewState.Draft,
            Created = created,
            CreatedBy = actor.Username,
            CreatedViaApi = actor.ViaApiToken,
            DueDate = dueDate,
            ContentFingerprint = Fingerprint.Compute(files.Where(f => f.ChangeKind != ChangeKind.Deleted).Select(f => f.Sha256)),
            RepositoryId = access.RepositoryId,
            RepositoryName = access.Name,
            BaselineSha = baselineSha,
            HeadSha = headSha,
            Selection = selectionText,
            ReviewSetId = reviewSetId,
            Files = files,
            Checklist = NewChecklist(projectId, id, checklistDefinition),
            FollowsReviewId = follow,
            CarriedAnswerCount = carry?.Answers.Count ?? 0,
            CarriedThreadCount = carry?.Threads.Count ?? 0,
            CarryOver = carry
        };

        // The title is shortened so the SHAs and the fingerprint always fit in the audit detail; an API-created review
        // names its token right after the title (a shorter title) so the statement can never be cut off at the end;
        // a follow-up adds its own text after the fingerprint (with the whole identifier of the earlier review, so two
        // reviews that share a prefix stay apart) and so shortens the title a little more
        var added = files.Count(f => f.ChangeKind == ChangeKind.Added);
        var modified = files.Count(f => f.ChangeKind == ChangeKind.Modified);
        var deleted = files.Count(f => f.ChangeKind == ChangeKind.Deleted);
        var setText = reviewSetId is null ? string.Empty : $", set '{AuditText.Clean(reviewSetId, 60)}'";
        var viaText = CreatedViaText(actor);
        var titleText = AuditText.Clean(trimmedTitle, (viaText.Length == 0 ? 60 : 40) - (follow is null ? 0 : 30));
        await _store.AddReviewAsync(
            review,
            AuditDraft.By(
                actor,
                "review.created",
                $"review '{titleText}'{viaText} from repository '{access.Name}', baseline {baselineSha ?? "none"}, head {headSha}, " +
                $"{files.Count} file{(files.Count == 1 ? string.Empty : "s")}: {added} added, {modified} modified, {deleted} deleted, " +
                $"{skipped.Count} skipped, fingerprint {review.ContentFingerprint}{FollowText(follow, carry)}{setText}{DueText(dueDate)}",
                projectId,
                id),
            cancellationToken);
        return new RepositoryReviewResult(review, skipped);
    }

    /// <summary>
    ///     Reads the review a new review follows up, checks that it fits and works out what is carried over.
    /// </summary>
    /// <remarks>
    ///     Pure reading: nothing is stored; the result travels with the new review (<see cref="Review.CarryOver"/>) so the
    ///     store writes it in the same transaction. A review that does not exist in the project (also one that exists only in
    ///     another project) gets one message that reveals nothing about other projects. The checks run in this order:
    ///     the review exists, it fits (<see cref="FollowUpCarry.CheckFit"/>), and open threads on files that are not in the
    ///     new review are acknowledged.
    /// </remarks>
    /// <param name="projectId">The project of the new review.</param>
    /// <param name="reviewId">The identifier of the new review.</param>
    /// <param name="follow">The normalized identifier of the earlier review, or <c>null</c> for an ordinary review.</param>
    /// <param name="acknowledge">Whether the creator acknowledged threads on files that are not in the new review.</param>
    /// <param name="repositoryId">The repository of the new review, or <c>null</c> for an upload.</param>
    /// <param name="reviewSetId">The review-set of the new review, or <c>null</c>.</param>
    /// <param name="newFiles">The files of the new review.</param>
    /// <param name="definition">The checklist of the new review.</param>
    /// <param name="now">The creation time of the new review.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>What to carry, or <c>null</c> when the review is not a follow-up.</returns>
    /// <exception cref="DomainRuleException">Thrown with <see cref="DomainRule.FollowUpInvalid"/> when a check fails.</exception>
    private async Task<ReviewCarryOver?> PrepareFollowUpAsync(
        int projectId,
        string reviewId,
        string? follow,
        bool acknowledge,
        int? repositoryId,
        string? reviewSetId,
        IReadOnlyList<ReviewFile> newFiles,
        ChecklistDefinition definition,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (follow is null)
        {
            return null;
        }

        // The store is asked within this project only, so another project's review is "not found" like a missing one
        var missing = new DomainRuleException(
            DomainRule.FollowUpInvalid,
            $"The review '{follow}' does not exist in this project and cannot be followed up.");
        var source = await _store.GetReviewAsync(projectId, follow, cancellationToken) ?? throw missing;
        var misfit = FollowUpCarry.CheckFit(source, repositoryId, reviewSetId, newFiles.Select(f => f.Path));
        if (misfit is not null)
        {
            throw new DomainRuleException(DomainRule.FollowUpInvalid, misfit);
        }

        var record = await _store.GetRecordAsync(projectId, follow, cancellationToken) ?? throw missing;
        var carry = FollowUpCarry.Compute(follow, record, source.Files, projectId, reviewId, newFiles, definition, now);
        if (carry.DroppedThreadFiles.Count > 0 && !acknowledge)
        {
            throw new DomainRuleException(DomainRule.FollowUpInvalid, FollowUpCarry.DroppedMessage(carry.DroppedThreadFiles));
        }

        return carry;
    }

    /// <summary>
    ///     Normalizes the identifier of the review to follow.
    /// </summary>
    /// <param name="followsReviewId">The identifier as given, possibly null or blank.</param>
    /// <returns>The trimmed identifier, or <c>null</c> when none was given.</returns>
    private static string? NormalizeFollows(string? followsReviewId)
    {
        return string.IsNullOrWhiteSpace(followsReviewId) ? null : followsReviewId.Trim();
    }

    /// <summary>
    ///     Builds the part of the <c>review.created</c> detail that tells about a follow-up.
    /// </summary>
    /// <param name="follow">The identifier of the earlier review, or <c>null</c>.</param>
    /// <param name="carry">What was carried, or <c>null</c>.</param>
    /// <returns>
    ///     The empty string for an ordinary review; otherwise <c>, follows 'X'; carried N answers, M threads</c> with, when
    ///     there are any, the number of resolved threads that were not carried and the number of files whose open threads
    ///     were dropped on the creator's acknowledgement.
    /// </returns>
    private static string FollowText(string? follow, ReviewCarryOver? carry)
    {
        if (follow is null || carry is null)
        {
            return string.Empty;
        }

        var text = $", follows '{AuditText.Clean(follow, ReviewLimits.MaxReviewIdLength)}'; carried {Plural(carry.Answers.Count, "answer")}, {Plural(carry.Threads.Count, "thread")}";
        if (carry.ResolvedThreadsNotCarried > 0)
        {
            text += $", {Plural(carry.ResolvedThreadsNotCarried, "resolved thread")} not carried";
        }

        if (carry.DroppedThreadFiles.Count > 0)
        {
            text += $", open threads on {Plural(carry.DroppedThreadFiles.Count, "file")} dropped";
        }

        return text;
    }

    /// <summary>
    ///     Writes a count with its noun in the singular or plural.
    /// </summary>
    /// <param name="count">The count.</param>
    /// <param name="noun">The noun in the singular.</param>
    /// <returns>For example <c>1 answer</c> or <c>3 answers</c>.</returns>
    private static string Plural(int count, string noun)
    {
        return $"{count.ToString(CultureInfo.InvariantCulture)} {noun}{(count == 1 ? string.Empty : "s")}";
    }

    /// <summary>
    ///     Builds the audit text that names the API token a review was created with.
    /// </summary>
    /// <param name="actor">The actor creating the review.</param>
    /// <returns>
    ///     <c>, created via API token 'ID'</c> for an actor that authenticated with a token; otherwise an empty
    ///     string, so a review created in the web pages reads exactly as before.
    /// </returns>
    private static string CreatedViaText(Actor actor)
    {
        return actor.ViaApiToken ? $", created via API token '{AuditText.Clean(actor.ApiTokenId, 20)}'" : string.Empty;
    }

    /// <summary>
    ///     Gets today's date in UTC, the day against which due dates are compared.
    /// </summary>
    /// <remarks>
    ///     A due date is a calendar day without a time zone; the product reads it as a UTC day everywhere (creation
    ///     rule, overdue badge, documentation), so that the answer never depends on the viewer's location.
    /// </remarks>
    public DateOnly TodayUtc => DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime);

    /// <summary>
    ///     Tells whether a review is overdue: it has a due date before today (UTC) and is not approved.
    /// </summary>
    /// <param name="review">The review. Must not be null.</param>
    /// <param name="isApproved">
    ///     Whether the review is approved now (see <see cref="ReviewLock.IsApproved"/>). An approved review is never
    ///     overdue; its date is kept as history.
    /// </param>
    /// <returns><c>true</c> when the review has a due date that lies before today (UTC) and is not approved.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="review"/> is null.</exception>
    public bool IsOverdue(Review review, bool isApproved)
    {
        ArgumentNullException.ThrowIfNull(review);
        return !isApproved && review.DueDate is { } due && due < TodayUtc;
    }

    /// <summary>
    ///     Reads the text of a due date as entered on a form or sent to the API.
    /// </summary>
    /// <param name="text">The text: an ISO calendar date (<c>yyyy-MM-dd</c>), or <c>null</c> or white space for no date.</param>
    /// <returns>The date, or <c>null</c> when none was given.</returns>
    /// <exception cref="DomainRuleException">Thrown with <see cref="DomainRule.DueDateInvalid"/> when the text is not an ISO date.</exception>
    public static DateOnly? ParseDueDate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        // Exactly the form of the HTML date input and of the API, whatever the culture of the server
        if (!DateOnly.TryParseExact(text.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            throw new DomainRuleException(
                DomainRule.DueDateInvalid,
                "The due date must be a calendar date in the form yyyy-MM-dd (a UTC day).");
        }

        return parsed;
    }

    /// <summary>
    ///     Shows what a follow-up review of uploaded files would take over from an earlier review, without creating
    ///     anything.
    /// </summary>
    /// <remarks>
    ///     A read-only companion of <see cref="CreateDraftAsync"/> for the form of a new review: it applies the same rules
    ///     (<see cref="FollowUpCarry.CheckFit"/> and <see cref="FollowUpCarry.Compute"/>, with the checklist the new review
    ///     would be frozen with), so the counts shown are the counts that creation stores. Nothing is stored and nothing is
    ///     audited. Reading needs only the right to view the project, like the other reads of a review; creating still
    ///     needs the right to contribute. Open threads on files that are not among <paramref name="files"/> are listed in
    ///     <see cref="FollowUpPreview.DroppedThreadFiles"/>; creation asks the creator to acknowledge them.
    /// </remarks>
    /// <param name="projectId">The project of the earlier and of the new review.</param>
    /// <param name="followsReviewId">The identifier of the earlier review.</param>
    /// <param name="files">The files of the new review; only their paths and content hashes are used.</param>
    /// <param name="actor">The signed-in user. Must not be null.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>What would be carried.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="files"/> or <paramref name="actor"/> is null.</exception>
    /// <exception cref="DomainRuleException">
    ///     Thrown with <see cref="DomainRule.ProjectNotFound"/> when the actor may not see the project, and with
    ///     <see cref="DomainRule.FollowUpInvalid"/> when no review is named, the review does not exist in the project or
    ///     does not fit the new files, and with the file rules of <see cref="CreateDraftAsync"/> (for example
    ///     <see cref="DomainRule.DuplicatePath"/>) when the files would be refused at creation.
    /// </exception>
    public async Task<FollowUpPreview> PreviewFollowUpAsync(
        int projectId,
        string? followsReviewId,
        IReadOnlyList<UploadedFile> files,
        Actor actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(actor);

        // Whoever may not see the project learns nothing about it, and nothing about its reviews
        await _access.RequireViewAsync(actor, projectId, cancellationToken);

        // The files obey the rules of creation (count, paths, sizes, unique paths): the carry-over compares paths, so
        // a set that creation would refuse is refused with the same message here
        ValidateFiles(files);
        var follow = NormalizeFollows(followsReviewId)
                     ?? throw new DomainRuleException(DomainRule.FollowUpInvalid, "Choose the review to follow up.");

        // The same preparation as creation, but only the paths and hashes are needed and nothing is kept
        var newFiles = files
            .Select(file => new ReviewFile
            {
                ProjectId = projectId,
                ReviewId = string.Empty,
                Path = file.Path,
                Size = file.Content.Length,
                Sha256 = Fingerprint.ComputeFileHash(file.Content)
            })
            .ToList();
        var definition = await ResolveChecklistDefinitionAsync(projectId, cancellationToken);
        var carry = await PrepareFollowUpAsync(
            projectId, string.Empty, follow, acknowledge: true, null, null, newFiles, definition, _timeProvider.GetUtcNow(), cancellationToken);
        return new FollowUpPreview(
            carry!.Answers.Count,
            carry.SectionStates.Count,
            carry.Threads.Count,
            carry.ResolvedThreadsNotCarried,
            carry.DroppedThreadFiles);
    }

    /// <summary>
    ///     Tells whether an actor may change the due date of a review.
    /// </summary>
    /// <remarks>
    ///     Project administrators (and system administrators) own the oversight of a project, and the person who
    ///     created the review owns the planning of it; nobody else may change it, and an API token never may (the date
    ///     can only be set when a review is created through the API). The pages use this to show the edit control;
    ///     <see cref="SetDueDateAsync"/> checks the same rule again, so the pages are never the only guard.
    /// </remarks>
    /// <param name="actor">The signed-in user; may be null, which is not allowed.</param>
    /// <param name="review">The review. Must not be null.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns><c>true</c> when the actor is a person who administers the project or created the review.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="review"/> is null.</exception>
    public async Task<bool> CanSetDueDateAsync(Actor? actor, Review review, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(review);
        if (actor is null || actor.ViaApiToken)
        {
            return false;
        }

        // The creator is recorded by user name, which is unique without regard to case; the right needs a project in
        // which the actor can still work (a creator who has since become a read-only member cannot change the date)
        var isCreator = review.CreatedBy is not null
                        && string.Equals(review.CreatedBy, actor.Username, StringComparison.OrdinalIgnoreCase)
                        && await _access.CanContributeAsync(actor, review.ProjectId, cancellationToken);
        return isCreator || await _access.CanAdministerAsync(actor, review.ProjectId, cancellationToken);
    }

    /// <summary>
    ///     Sets, changes or clears the due date of a review.
    /// </summary>
    /// <param name="projectId">The project identity.</param>
    /// <param name="reviewId">The review identifier.</param>
    /// <param name="dueDate">The new due date (a UTC calendar day), or <c>null</c> to clear it. A date in the past is allowed.</param>
    /// <param name="actor">
    ///     The signed-in user; must be a project administrator (or system administrator) or the user who created the
    ///     review. An API token is refused. Must not be null.
    /// </param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns><c>true</c> when the date was changed; <c>false</c> when it already had that value and nothing was recorded.</returns>
    /// <remarks>
    ///     The change and its audit entry <c>review.due-date-changed</c> (detail <c>"&lt;old|none&gt; -&gt; &lt;new|none&gt;"</c>)
    ///     are stored in one transaction, as a compare-and-set on the value this method read, so the entry always names
    ///     the value that was really replaced. The date is not part of the record digest, so no signature is affected.
    ///     A past date is allowed (a lead may record that something was due last week); one that is more than
    ///     <see cref="MaxDueDateYearsAhead"/> years ahead is not. An approved review is locked and refuses the change;
    ///     after a reopen the date can be changed again.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="actor"/> is null.</exception>
    /// <exception cref="DomainRuleException">
    ///     Thrown with <see cref="DomainRule.ProjectNotFound"/> when the actor may not see the project,
    ///     <see cref="DomainRule.ReviewNotFound"/> when the review does not exist,
    ///     <see cref="DomainRule.ActorNotAllowed"/> for an API token or a member who is neither administrator nor creator,
    ///     <see cref="DomainRule.DueDateInvalid"/> for a date too far ahead, <see cref="DomainRule.ReviewLocked"/> when the
    ///     review is approved, and <see cref="DomainRule.RecordChanged"/> when someone else changed the date at the same
    ///     moment; nothing is stored then.
    /// </exception>
    public async Task<bool> SetDueDateAsync(
        int projectId,
        string reviewId,
        DateOnly? dueDate,
        Actor actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);

        // Whoever may not see the project learns nothing about it, and nothing about its reviews
        await _access.RequireViewAsync(actor, projectId, cancellationToken);
        var review = await _store.GetReviewAsync(projectId, reviewId, cancellationToken)
                     ?? throw new DomainRuleException(DomainRule.ReviewNotFound, "The review does not exist.");

        // Who may change it: a person who administers the project or created the review, never an API token
        if (actor.ViaApiToken)
        {
            throw new DomainRuleException(
                DomainRule.ActorNotAllowed,
                "The due date cannot be changed with an API token; sign in to the web pages.");
        }

        if (!await CanSetDueDateAsync(actor, review, cancellationToken))
        {
            throw new DomainRuleException(
                DomainRule.ActorNotAllowed,
                "Only a project administrator or the creator of the review can change its due date.");
        }

        // A past date is fine here; a date far in the future is almost certainly a typing error
        ValidateDueDateRange(dueDate, allowPast: true);

        // Approved means locked: the date has no purpose then, and "locked means nothing changes" is easy to explain
        await ReviewLock.EnsureUnlockedAsync(_store, projectId, reviewId, cancellationToken);
        if (review.DueDate == dueDate)
        {
            return false;
        }

        var stored = await _store.UpdateDueDateAsync(
            projectId,
            reviewId,
            review.DueDate,
            dueDate,
            AuditDraft.By(actor, DueDateChangedAction, $"{DueDateText(review.DueDate)} -> {DueDateText(dueDate)}", projectId, reviewId),
            cancellationToken);
        if (!stored)
        {
            throw new DomainRuleException(
                DomainRule.RecordChanged,
                "The due date was changed by someone else. The current date is shown; check it and save again.");
        }

        return true;
    }

    /// <summary>
    ///     Checks the due date of a review that is being created.
    /// </summary>
    /// <param name="dueDate">The due date, or <c>null</c> for none.</param>
    /// <exception cref="DomainRuleException">
    ///     Thrown with <see cref="DomainRule.DueDateInvalid"/> when the date is before today (UTC) or more than
    ///     <see cref="MaxDueDateYearsAhead"/> years ahead.
    /// </exception>
    internal void ValidateNewDueDate(DateOnly? dueDate)
    {
        ValidateDueDateRange(dueDate, allowPast: false);
    }

    /// <summary>
    ///     Checks a due date against the allowed range.
    /// </summary>
    /// <param name="dueDate">The due date, or <c>null</c> for none (always valid).</param>
    /// <param name="allowPast">Whether a date before today is allowed (when changing a date, but not at creation).</param>
    /// <exception cref="DomainRuleException">Thrown with <see cref="DomainRule.DueDateInvalid"/> when the date is out of range.</exception>
    private void ValidateDueDateRange(DateOnly? dueDate, bool allowPast)
    {
        if (dueDate is not { } due)
        {
            return;
        }

        // Both bounds are calendar days in UTC: today itself is allowed, and so is exactly the same day two years ahead
        var today = TodayUtc;
        if (!allowPast && due < today)
        {
            throw new DomainRuleException(
                DomainRule.DueDateInvalid,
                "The due date must not be before today (UTC).");
        }

        if (due > today.AddYears(MaxDueDateYearsAhead))
        {
            throw new DomainRuleException(
                DomainRule.DueDateInvalid,
                $"The due date must not be more than {MaxDueDateYearsAhead} years ahead.");
        }
    }

    /// <summary>
    ///     Writes a due date for the audit trail.
    /// </summary>
    /// <param name="dueDate">The due date, or <c>null</c>.</param>
    /// <returns>The date as <c>yyyy-MM-dd</c>, or <c>none</c>.</returns>
    private static string DueDateText(DateOnly? dueDate)
    {
        return dueDate is { } due ? due.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "none";
    }

    /// <summary>
    ///     Builds the part of the <c>review.created</c> detail that names the due date.
    /// </summary>
    /// <param name="dueDate">The due date, or <c>null</c>.</param>
    /// <returns><c>, due yyyy-MM-dd</c> when a date is set; otherwise an empty string, so a review without a date reads as before.</returns>
    private static string DueText(DateOnly? dueDate)
    {
        return dueDate is null ? string.Empty : $", due {DueDateText(dueDate)}";
    }

    /// <summary>
    ///     Reads one review with its file metadata.
    /// </summary>
    /// <param name="projectId">The project identity.</param>
    /// <param name="reviewId">The review identifier.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The review with its files ordered by path (no file bytes), or <c>null</c> when it does not exist.</returns>
    public Task<Review?> GetAsync(int projectId, string reviewId, CancellationToken cancellationToken = default)
    {
        return _store.GetReviewAsync(projectId, reviewId, cancellationToken);
    }

    /// <summary>
    ///     Lists the reviews of a project.
    /// </summary>
    /// <param name="projectId">The project identity.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The reviews ordered by id, without files; empty when there are none.</returns>
    public Task<IReadOnlyList<Review>> ListAsync(int projectId, CancellationToken cancellationToken = default)
    {
        return _store.ListReviewsAsync(projectId, cancellationToken);
    }

    /// <summary>
    ///     Reads the exact bytes of one file of a review.
    /// </summary>
    /// <param name="projectId">The project identity.</param>
    /// <param name="reviewId">The review identifier.</param>
    /// <param name="path">The file path within the review.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The bytes exactly as uploaded, or <c>null</c> when the file does not exist.</returns>
    public Task<byte[]?> GetFileContentAsync(
        int projectId,
        string reviewId,
        string path,
        CancellationToken cancellationToken = default)
    {
        return _store.GetFileContentAsync(projectId, reviewId, path, cancellationToken);
    }

    /// <summary>
    ///     Reads the exact baseline bytes of one file of a repository review.
    /// </summary>
    /// <param name="projectId">The project identity.</param>
    /// <param name="reviewId">The review identifier.</param>
    /// <param name="path">The file path within the review.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The frozen baseline bytes, or <c>null</c> when the file does not exist or has no baseline.</returns>
    public Task<byte[]?> GetBaselineContentAsync(
        int projectId,
        string reviewId,
        string path,
        CancellationToken cancellationToken = default)
    {
        return _store.GetBaselineContentAsync(projectId, reviewId, path, cancellationToken);
    }

    /// <summary>
    ///     Compares the frozen baseline and head of one file of a repository review line by line.
    /// </summary>
    /// <remarks>
    ///     Only the content stored with the review is read, never the git repository, so the diff is the same
    ///     whenever it is viewed. An added file is compared with no lines (all added) and a deleted file
    ///     produces no head lines (all removed). Line numbers come from <see cref="FileText.TryReadLines"/>,
    ///     the same definition thread validation uses.
    /// </remarks>
    /// <param name="projectId">The project identity.</param>
    /// <param name="reviewId">The review identifier.</param>
    /// <param name="path">The file path within the review (ordinal comparison).</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    ///     <c>null</c> when the review or the file does not exist; otherwise the diff with status
    ///     <see cref="FileDiffStatus.NoBaseline"/> (upload review or no baseline commit),
    ///     <see cref="FileDiffStatus.Binary"/>, <see cref="FileDiffStatus.TooLarge"/> or
    ///     <see cref="FileDiffStatus.Diff"/>.
    /// </returns>
    public async Task<FileDiff?> GetDiffAsync(
        int projectId,
        string reviewId,
        string path,
        CancellationToken cancellationToken = default)
    {
        // The file must exist in the review; the metadata read does not load any file bytes
        var review = await _store.GetReviewAsync(projectId, reviewId, cancellationToken);
        var file = review?.Files.FirstOrDefault(f => string.Equals(f.Path, path, StringComparison.Ordinal));
        if (review is null || file is null)
        {
            return null;
        }

        // Without a frozen baseline there is nothing to compare against
        if (!review.IsFromRepository || review.BaselineSha is null)
        {
            return new FileDiff(FileDiffStatus.NoBaseline);
        }

        // A file that is not there on one side (added or deleted) has no bytes, hence no lines, on that side
        var headBytes = file.ChangeKind == ChangeKind.Deleted
            ? []
            : await _store.GetFileContentAsync(projectId, reviewId, path, cancellationToken) ?? [];
        var baselineBytes = file.ChangeKind == ChangeKind.Added
            ? []
            : await _store.GetBaselineContentAsync(projectId, reviewId, path, cancellationToken) ?? [];
        var headLines = FileText.TryReadLines(headBytes);
        var baselineLines = FileText.TryReadLines(baselineBytes);
        if (headLines is null || baselineLines is null)
        {
            return new FileDiff(FileDiffStatus.Binary);
        }

        var result = LineDiff.Compute(baselineLines, headLines);
        if (result.TooLarge)
        {
            return new FileDiff(FileDiffStatus.TooLarge);
        }

        // Identical lines with different bytes means only line endings (or the final newline) changed
        var onlyLineEndings = result.Added == 0 && result.Removed == 0 && !baselineBytes.AsSpan().SequenceEqual(headBytes);
        return new FileDiff(FileDiffStatus.Diff, result, onlyLineEndings);
    }

    /// <summary>
    ///     Checks that the review id is still free in the project (the caller has checked the project).
    /// </summary>
    /// <param name="projectId">The project identity.</param>
    /// <param name="id">The review identifier.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when both checks pass.</returns>
    /// <exception cref="DomainRuleException">Thrown with <see cref="DomainRule.ReviewIdDuplicate"/>.</exception>
    private async Task EnsureFreeIdAsync(int projectId, string id, CancellationToken cancellationToken)
    {
        if (await _store.ReviewExistsAsync(projectId, id, cancellationToken))
        {
            throw new DomainRuleException(
                DomainRule.ReviewIdDuplicate,
                $"A review with the id '{id}' already exists in this project.");
        }
    }

    /// <summary>
    ///     Builds the frozen copy of a checklist definition for a new review.
    /// </summary>
    /// <param name="projectId">The project identity.</param>
    /// <param name="reviewId">The review identifier.</param>
    /// <param name="definition">The definition to freeze: the project's own, or the built-in default.</param>
    /// <returns>The checklist; later changes of the project's definition or of the default never alter it.</returns>
    private static ReviewChecklist NewChecklist(int projectId, string reviewId, ChecklistDefinition definition)
    {
        return new ReviewChecklist
        {
            ProjectId = projectId,
            ReviewId = reviewId,
            Version = definition.Version,
            Definition = definition.ToJson()
        };
    }

    /// <summary>
    ///     Finds the checklist definition a new review of a project is frozen with.
    /// </summary>
    /// <remarks>
    ///     A project without a definition of its own gets the built-in <see cref="DefaultChecklist"/>. A stored
    ///     definition is validated again here with <see cref="ChecklistValidator"/>, so even a value that reached the
    ///     database by other means is never frozen into a review; a definition that does not pass fails the creation
    ///     (it never falls back silently to the default, which would give reviewers other questions than the
    ///     administrators configured).
    /// </remarks>
    /// <param name="projectId">The project identity.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The definition to freeze.</returns>
    /// <exception cref="DomainRuleException">
    ///     Thrown with <see cref="DomainRule.ProjectNotFound"/> when the project does not exist, and with
    ///     <see cref="DomainRule.ChecklistDefinitionInvalid"/> when the stored definition is not valid.
    /// </exception>
    private async Task<ChecklistDefinition> ResolveChecklistDefinitionAsync(int projectId, CancellationToken cancellationToken)
    {
        var project = await _store.GetProjectAsync(projectId, cancellationToken)
                      ?? throw new DomainRuleException(DomainRule.ProjectNotFound, $"Project {projectId} does not exist.");
        if (project.ChecklistDefinition is null)
        {
            return DefaultChecklist.Current;
        }

        try
        {
            return ChecklistValidator.ParseOrThrow(project.ChecklistDefinition);
        }
        catch (DomainRuleException ex) when (ex.Rule == DomainRule.ChecklistDefinitionInvalid)
        {
            throw new DomainRuleException(
                DomainRule.ChecklistDefinitionInvalid,
                $"The review was not created because the checklist of project {projectId} cannot be used. {ex.Message}");
        }
    }

    /// <summary>
    ///     Checks the size limits of the selected changes from the blob sizes, without reading any content.
    /// </summary>
    /// <param name="session">The open repository session.</param>
    /// <param name="changes">The selected changes.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when all limits hold.</returns>
    /// <exception cref="DomainRuleException">Thrown with <see cref="DomainRule.FileTooLarge"/> or <see cref="DomainRule.TotalSizeExceeded"/>.</exception>
    private static async Task CheckSizesAsync(
        IGitRepository session,
        IReadOnlyList<GitChange> changes,
        CancellationToken cancellationToken)
    {
        long total = 0;
        foreach (var change in changes)
        {
            foreach (var (blob, side) in new[] { (change.BaselineBlobSha, "baseline"), (change.HeadBlobSha, "head") })
            {
                if (blob is null)
                {
                    continue;
                }

                var size = await session.GetBlobSizeAsync(blob, cancellationToken);
                if (size > ReviewLimits.MaxFileSizeBytes)
                {
                    throw new DomainRuleException(
                        DomainRule.FileTooLarge,
                        $"The {side} of the file '{change.Path}' is larger than the limit of {ReviewLimits.MaxFileSizeBytes / 1024 / 1024} MiB.");
                }

                total += size;
                if (total > ReviewLimits.MaxTotalSizeBytes)
                {
                    throw new DomainRuleException(
                        DomainRule.TotalSizeExceeded,
                        $"The files together (both sides of each change) are larger than the limit of {ReviewLimits.MaxTotalSizeBytes / 1024 / 1024} MiB.");
                }
            }
        }
    }

    /// <summary>
    ///     Reads both sides of one changed file and builds its review file.
    /// </summary>
    /// <param name="session">The open repository session.</param>
    /// <param name="projectId">The project identity.</param>
    /// <param name="reviewId">The review identifier.</param>
    /// <param name="change">The change.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    ///     The file; a deleted file has empty head content, size and hash, and carries the baseline columns instead.
    /// </returns>
    private static async Task<ReviewFile> ReadFileAsync(
        IGitRepository session,
        int projectId,
        string reviewId,
        GitChange change,
        CancellationToken cancellationToken)
    {
        byte[]? baseline = change.BaselineBlobSha is null
            ? null
            : await session.ReadBlobAsync(change.BaselineBlobSha, ReviewLimits.MaxFileSizeBytes, cancellationToken);
        byte[] head = change.HeadBlobSha is null
            ? []
            : await session.ReadBlobAsync(change.HeadBlobSha, ReviewLimits.MaxFileSizeBytes, cancellationToken);
        return new ReviewFile
        {
            ProjectId = projectId,
            ReviewId = reviewId,
            Path = change.Path,
            Content = head,
            Size = head.Length,
            Sha256 = change.HeadBlobSha is null ? string.Empty : Fingerprint.ComputeFileHash(head),
            ChangeKind = change.Kind,
            BaselineContent = baseline,
            BaselineSize = baseline?.Length,
            BaselineSha256 = baseline is null ? null : Fingerprint.ComputeFileHash(baseline)
        };
    }

    /// <summary>
    ///     Parses a file selection and normalizes it for storage.
    /// </summary>
    /// <param name="selection">
    ///     The selection text; <c>null</c> (no selection given) means every changed file
    ///     (<see cref="SelectionMatcher.AllFiles"/>), whereas empty or blank text is invalid.
    /// </param>
    /// <returns>The parsed patterns and the normalized text a review stores.</returns>
    /// <exception cref="DomainRuleException">Thrown with <see cref="DomainRule.SelectionInvalid"/> when the selection is not valid or the normalized text is too long.</exception>
    internal static (IReadOnlyList<SelectionPattern> Patterns, string Text) ParseSelection(string? selection)
    {
        // Only a missing selection means "every changed file"; typed-in empty text stays an error
        var patterns = SelectionMatcher.Parse(selection ?? SelectionMatcher.AllFiles);
        var text = SelectionMatcher.Normalize(patterns);
        if (text.Length > ReviewLimits.MaxSelectionLength)
        {
            throw new DomainRuleException(
                DomainRule.SelectionInvalid,
                $"The selection must not be longer than {ReviewLimits.MaxSelectionLength} characters.");
        }

        return (patterns, text);
    }

    /// <summary>
    ///     Checks that a review id is acceptable.
    /// </summary>
    /// <param name="id">The candidate id.</param>
    /// <exception cref="DomainRuleException">Thrown with <see cref="DomainRule.ReviewIdInvalid"/> when the id is not acceptable.</exception>
    internal static void ValidateId(string? id)
    {
        // The id appears in page addresses, so only characters that are safe there are accepted
        if (string.IsNullOrEmpty(id) || id.Length > ReviewLimits.MaxReviewIdLength || !IsAllowedId(id))
        {
            throw new DomainRuleException(
                DomainRule.ReviewIdInvalid,
                $"The review id must be 1 to {ReviewLimits.MaxReviewIdLength} characters: letters, digits, '.', '_' " +
                "and '-', starting with a letter or digit.");
        }
    }

    /// <summary>
    ///     Reports whether every character of a review id is allowed and the first is a letter or digit.
    /// </summary>
    /// <param name="id">The candidate id; not empty.</param>
    /// <returns><c>true</c> when the id has the allowed form.</returns>
    private static bool IsAllowedId(string id)
    {
        // ASCII only: char.IsLetterOrDigit would also accept letters that are awkward in addresses
        static bool IsAsciiLetterOrDigit(char c) => c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9');

        if (!IsAsciiLetterOrDigit(id[0]))
        {
            return false;
        }

        return id.All(c => IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-');
    }

    /// <summary>
    ///     Checks and normalizes a review title.
    /// </summary>
    /// <param name="title">The candidate title.</param>
    /// <returns>The title without leading and trailing white space.</returns>
    /// <exception cref="DomainRuleException">Thrown with <see cref="DomainRule.ReviewTitleInvalid"/> when the title is empty or too long.</exception>
    internal static string ValidateTitle(string? title)
    {
        var trimmed = title?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            throw new DomainRuleException(DomainRule.ReviewTitleInvalid, "The review title is required.");
        }

        if (trimmed.Length > ReviewLimits.MaxReviewTitleLength)
        {
            throw new DomainRuleException(
                DomainRule.ReviewTitleInvalid,
                $"The review title must not be longer than {ReviewLimits.MaxReviewTitleLength} characters.");
        }

        return trimmed;
    }

    /// <summary>
    ///     Checks the file set against the count, path, size and uniqueness rules.
    /// </summary>
    /// <param name="files">The files offered for the review.</param>
    /// <exception cref="DomainRuleException">Thrown when a file rule is violated.</exception>
    private static void ValidateFiles(IReadOnlyList<UploadedFile> files)
    {
        if (files.Count == 0)
        {
            throw new DomainRuleException(DomainRule.NoFiles, "A review needs at least one file.");
        }

        if (files.Count > ReviewLimits.MaxFileCount)
        {
            throw new DomainRuleException(
                DomainRule.TooManyFiles,
                $"A review can have at most {ReviewLimits.MaxFileCount} files.");
        }

        // Paths are compared ordinally: file systems and ReviewMark treat paths as exact strings
        var paths = new HashSet<string>(StringComparer.Ordinal);
        long total = 0;
        foreach (var file in files)
        {
            if (string.IsNullOrWhiteSpace(file.Path) || file.Path.Length > ReviewLimits.MaxPathLength)
            {
                throw new DomainRuleException(
                    DomainRule.PathInvalid,
                    $"A file path must be 1 to {ReviewLimits.MaxPathLength} characters.");
            }

            if (!paths.Add(file.Path))
            {
                throw new DomainRuleException(DomainRule.DuplicatePath, $"The file path '{file.Path}' appears more than once.");
            }

            if (file.Content.Length > ReviewLimits.MaxFileSizeBytes)
            {
                throw new DomainRuleException(
                    DomainRule.FileTooLarge,
                    $"The file '{file.Path}' is larger than the limit of {ReviewLimits.MaxFileSizeBytes / 1024 / 1024} MiB.");
            }

            total += file.Content.Length;
        }

        if (total > ReviewLimits.MaxTotalSizeBytes)
        {
            throw new DomainRuleException(
                DomainRule.TotalSizeExceeded,
                $"The files together are larger than the limit of {ReviewLimits.MaxTotalSizeBytes / 1024 / 1024} MiB.");
        }
    }
}

/// <summary>
///     What <see cref="ReviewService.CreateFromSessionAsync"/> creates: the validated identity, commits and selection
///     of one review, grouped so that the method keeps a short parameter list.
/// </summary>
/// <param name="Id">The validated review identifier.</param>
/// <param name="TrimmedTitle">The validated, trimmed title.</param>
/// <param name="HeadSha">The full head commit identifier.</param>
/// <param name="BaselineSha">The full baseline commit identifier, or <c>null</c> to review every file at the head.</param>
/// <param name="Patterns">The parsed selection.</param>
/// <param name="SelectionText">The normalized selection text stored with the review.</param>
/// <param name="ReviewSetId">The review-set the review is planned from, or <c>null</c>.</param>
/// <param name="HeadLabel">How the head is named in the "nothing to review" message.</param>
/// <param name="BaselineLabel">How the baseline is named in that message, or <c>null</c> for none.</param>
/// <param name="DueDate">The optional due date of the review (a UTC calendar day), or <c>null</c>.</param>
/// <param name="FollowsReviewId">The normalized identifier of the earlier review this one follows up, or <c>null</c>.</param>
/// <param name="AcknowledgeDroppedThreads">Whether the creator acknowledged open threads on files that are not in the new review.</param>
internal sealed record SessionReviewRequest(
    string Id,
    string TrimmedTitle,
    string HeadSha,
    string? BaselineSha,
    IReadOnlyList<SelectionPattern> Patterns,
    string SelectionText,
    string? ReviewSetId,
    string HeadLabel,
    string? BaselineLabel,
    DateOnly? DueDate = null,
    string? FollowsReviewId = null,
    bool AcknowledgeDroppedThreads = false);
