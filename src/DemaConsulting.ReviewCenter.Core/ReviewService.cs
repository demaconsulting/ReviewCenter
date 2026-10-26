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
    ///     The store that persists reviews.
    /// </summary>
    private readonly IReviewStore _store;

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
    /// <param name="timeProvider">The clock used for creation times; injected so tests control time. Must not be null.</param>
    /// <param name="repositories">The repository registry; needed only by <see cref="CreateFromRepositoryAsync"/>.</param>
    /// <param name="git">The git access; needed only by <see cref="CreateFromRepositoryAsync"/>.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="store"/> or <paramref name="timeProvider"/> is null.</exception>
    public ReviewService(
        IReviewStore store,
        TimeProvider timeProvider,
        RepositoryService? repositories = null,
        IGitRepositoryProvider? git = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _store = store;
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
    /// <returns>The created review, including its fingerprint and file metadata.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="files"/> or <paramref name="actor"/> is null.</exception>
    /// <exception cref="DomainRuleException">
    ///     Thrown when any rule is violated (see <see cref="DomainRule"/>); nothing is stored then.
    /// </exception>
    public async Task<Review> CreateDraftAsync(
        int projectId,
        string? id,
        string? title,
        IReadOnlyList<UploadedFile> files,
        Actor actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(actor);

        // Validate everything that needs no I/O first so a bad request fails fast and cheaply
        ValidateId(id);
        var trimmedTitle = ValidateTitle(title);
        ValidateFiles(files);

        // The project must exist, and the id must be free within it. The store backs the id check up
        // with a uniqueness constraint, which covers two requests racing past this check
        await EnsureProjectAndFreeIdAsync(projectId, id!, cancellationToken);

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
        var review = new Review
        {
            ProjectId = projectId,
            Id = id!,
            Title = trimmedTitle,
            State = ReviewState.Draft,
            Created = _timeProvider.GetUtcNow(),
            ContentFingerprint = Fingerprint.Compute(reviewFiles.Select(file => file.Sha256)),
            Files = reviewFiles,

            // The checklist is frozen now: later changes of the default never alter this review's checks
            Checklist = NewChecklist(projectId, id!)
        };

        await _store.AddReviewAsync(
            review,
            AuditDraft.By(
                actor,
                "review.created",
                $"review '{trimmedTitle}', fingerprint {review.ContentFingerprint}, {reviewFiles.Count} file{(reviewFiles.Count == 1 ? string.Empty : "s")}",
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
    /// <param name="repositoryId">The identity of an enabled registered repository.</param>
    /// <param name="headRevision">The head revision: a branch, tag or commit name (see <see cref="RevisionValidator"/>).</param>
    /// <param name="baselineRevision">
    ///     The baseline revision, or <c>null</c> or white space to review every file at the head as added.
    /// </param>
    /// <param name="selection">
    ///     The file selection: patterns, one per line (see <see cref="SelectionMatcher"/>). Only changed files that
    ///     match are reviewed.
    /// </param>
    /// <param name="actor">The signed-in user creating the review; recorded in the audit trail. Must not be null.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The created review and the entries that were skipped (symbolic links, submodules, unusable paths).</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="actor"/> is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the service was created without repository access.</exception>
    /// <exception cref="DomainRuleException">
    ///     Thrown when a rule is violated (see <see cref="DomainRule"/>), among them
    ///     <see cref="DomainRule.NothingToReview"/> when no file matching the selection changed; nothing is stored then.
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
        int repositoryId,
        string? headRevision,
        string? baselineRevision,
        string? selection,
        Actor actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (_repositories is null || _git is null)
        {
            throw new InvalidOperationException("This service was created without access to repositories.");
        }

        // Validate everything that needs no I/O first so a bad request fails fast and cheaply
        ValidateId(id);
        var trimmedTitle = ValidateTitle(title);
        var head = RevisionValidator.Validate(headRevision?.Trim());
        var baseline = string.IsNullOrWhiteSpace(baselineRevision) ? null : RevisionValidator.Validate(baselineRevision.Trim());
        var (patterns, selectionText) = ParseSelection(selection);

        await EnsureProjectAndFreeIdAsync(projectId, id!, cancellationToken);
        var access = await _repositories.GetAccessAsync(repositoryId, cancellationToken);

        // The session resolves both revisions; everything after that is shared with the planner
        await using var session = await _git.OpenAsync(access, cancellationToken);
        var headSha = await session.ResolveRevisionAsync(head, cancellationToken);
        var baselineSha = baseline is null ? null : await session.ResolveRevisionAsync(baseline, cancellationToken);
        return await CreateFromSessionAsync(
            session,
            access,
            projectId,
            id!,
            trimmedTitle,
            headSha,
            baselineSha,
            patterns,
            selectionText,
            null,
            head,
            baseline,
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
    /// <param name="id">The validated review identifier.</param>
    /// <param name="trimmedTitle">The validated, trimmed title.</param>
    /// <param name="headSha">The full head commit identifier.</param>
    /// <param name="baselineSha">The full baseline commit identifier, or <c>null</c> to review every file at the head.</param>
    /// <param name="patterns">The parsed selection.</param>
    /// <param name="selectionText">The normalized selection text stored with the review.</param>
    /// <param name="reviewSetId">The review-set the review is planned from, or <c>null</c>.</param>
    /// <param name="headLabel">How the head is named in the "nothing to review" message.</param>
    /// <param name="baselineLabel">How the baseline is named in that message, or <c>null</c> for none.</param>
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
        string id,
        string trimmedTitle,
        string headSha,
        string? baselineSha,
        IReadOnlyList<SelectionPattern> patterns,
        string selectionText,
        string? reviewSetId,
        string headLabel,
        string? baselineLabel,
        Actor actor,
        CancellationToken cancellationToken)
    {
        // The set id is stored, bound by signatures and written to the audit trail, so it is bounded here
        if (reviewSetId is not null && (reviewSetId.Length == 0 || reviewSetId.Length > ReviewLimits.MaxReviewSetIdLength))
        {
            throw new DomainRuleException(
                DomainRule.ReviewSetConfigInvalid,
                $"A review-set id must be 1 to {ReviewLimits.MaxReviewSetIdLength} characters.");
        }

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
        var review = new Review
        {
            ProjectId = projectId,
            Id = id,
            Title = trimmedTitle,
            State = ReviewState.Draft,
            Created = _timeProvider.GetUtcNow(),
            ContentFingerprint = Fingerprint.Compute(files.Where(f => f.ChangeKind != ChangeKind.Deleted).Select(f => f.Sha256)),
            RepositoryId = access.RepositoryId,
            RepositoryName = access.Name,
            BaselineSha = baselineSha,
            HeadSha = headSha,
            Selection = selectionText,
            ReviewSetId = reviewSetId,
            Files = files,
            Checklist = NewChecklist(projectId, id)
        };

        // The title is shortened so the SHAs and the fingerprint always fit in the audit detail
        var added = files.Count(f => f.ChangeKind == ChangeKind.Added);
        var modified = files.Count(f => f.ChangeKind == ChangeKind.Modified);
        var deleted = files.Count(f => f.ChangeKind == ChangeKind.Deleted);
        var setText = reviewSetId is null ? string.Empty : $", set '{AuditText.Clean(reviewSetId, 60)}'";
        await _store.AddReviewAsync(
            review,
            AuditDraft.By(
                actor,
                "review.created",
                $"review '{AuditText.Clean(trimmedTitle, 60)}' from repository '{access.Name}', baseline {baselineSha ?? "none"}, head {headSha}, " +
                $"{files.Count} file{(files.Count == 1 ? string.Empty : "s")}: {added} added, {modified} modified, {deleted} deleted, " +
                $"{skipped.Count} skipped, fingerprint {review.ContentFingerprint}{setText}",
                projectId,
                id),
            cancellationToken);
        return new RepositoryReviewResult(review, skipped);
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
    ///     Checks that the project exists and the review id is still free in it.
    /// </summary>
    /// <param name="projectId">The project identity.</param>
    /// <param name="id">The review identifier.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when both checks pass.</returns>
    /// <exception cref="DomainRuleException">Thrown with <see cref="DomainRule.ProjectNotFound"/> or <see cref="DomainRule.ReviewIdDuplicate"/>.</exception>
    private async Task EnsureProjectAndFreeIdAsync(int projectId, string id, CancellationToken cancellationToken)
    {
        if (await _store.GetProjectAsync(projectId, cancellationToken) is null)
        {
            throw new DomainRuleException(DomainRule.ProjectNotFound, $"Project {projectId} does not exist.");
        }

        if (await _store.ReviewExistsAsync(projectId, id, cancellationToken))
        {
            throw new DomainRuleException(
                DomainRule.ReviewIdDuplicate,
                $"A review with the id '{id}' already exists in this project.");
        }
    }

    /// <summary>
    ///     Builds the frozen copy of the current default checklist for a new review.
    /// </summary>
    /// <param name="projectId">The project identity.</param>
    /// <param name="reviewId">The review identifier.</param>
    /// <returns>The checklist; later changes of the default never alter it.</returns>
    private static ReviewChecklist NewChecklist(int projectId, string reviewId)
    {
        return new ReviewChecklist
        {
            ProjectId = projectId,
            ReviewId = reviewId,
            Version = DefaultChecklist.Version,
            Definition = DefaultChecklist.Current.ToJson()
        };
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
    /// <param name="selection">The selection text; <c>null</c> is treated as empty (which is invalid).</param>
    /// <returns>The parsed patterns and the normalized text a review stores.</returns>
    /// <exception cref="DomainRuleException">Thrown with <see cref="DomainRule.SelectionInvalid"/> when the selection is not valid or the normalized text is too long.</exception>
    internal static (IReadOnlyList<SelectionPattern> Patterns, string Text) ParseSelection(string? selection)
    {
        var patterns = SelectionMatcher.Parse(selection ?? string.Empty);
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
