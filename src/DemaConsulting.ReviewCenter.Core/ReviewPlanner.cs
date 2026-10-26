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

using System.Text;

namespace DemaConsulting.ReviewCenter.Core;

/// <summary>
///     Plans the reviews a head revision of a repository needs, one per review-set of its
///     <c>.reviewmark.yaml</c>, and creates the ones the user picks.
/// </summary>
/// <remarks>
///     <para>
///         <see cref="PlanAsync"/> only reads: it fetches the repository once, reads the configuration at the head,
///         chooses a baseline for every set (none, one given revision, or the head of the set's last signed review
///         when that is an ancestor of the new head and the set's selection is unchanged) and classifies each set
///         as will create, already filed, nothing to review or cannot plan. A fallback to a full review is always
///         the safe side, and the row says why in a plain sentence.
///     </para>
///     <para>
///         <see cref="CreateAsync"/> creates the picked reviews from the commits of the plan (never from branch
///         names, which may have moved), through <see cref="ReviewService"/> so every review has the same rules,
///         frozen content and audit entry as one created by hand. Every review is stored in its own transaction:
///         all-or-nothing per review, not per batch, so one failing row neither blocks nor undoes the others.
///         An unexpected fault (not a domain rule violation, for example a database error) also becomes a failed row
///         with a generic message, so the results of the reviews already created are never lost; cancellation
///         still propagates. A git access failure of one row is reported with a fixed sentence, never with the text
///         git printed.
///     </para>
///     <para>The planner holds no state and is safe for concurrent use.</para>
/// </remarks>
public sealed class ReviewPlanner
{
    /// <summary>
    ///     The path of the review configuration: only the root of the repository is searched.
    /// </summary>
    private const string ConfigPath = ".reviewmark.yaml";

    /// <summary>
    ///     The longest base of a generated review id, leaving room for a suffix such as <c>-123</c>.
    /// </summary>
    private const int MaxIdBaseLength = ReviewLimits.MaxReviewIdLength - 4;

    /// <summary>
    ///     How many id suffixes are tried when a generated id turns out to be taken.
    /// </summary>
    private const int MaxIdAttempts = 5;

    /// <summary>
    ///     The number of characters of a commit shown in titles and notes.
    /// </summary>
    private const int ShortShaLength = 7;

    /// <summary>
    ///     The fixed sentence that replaces the text of a git access failure, so that no raw git output (a location, for
    ///     example) reaches a page.
    /// </summary>
    public const string GitAccessFailedMessage = "The repository could not be read.";

    /// <summary>
    ///     The message of a row that failed for a reason that is not a domain rule: safe to show, with no internal detail.
    /// </summary>
    private const string UnexpectedFailureMessage =
        "This review could not be created because of an unexpected error; nothing was created for it. Try again, and tell an administrator if it keeps happening.";

    /// <summary>
    ///     The store that lists the reviews of a project.
    /// </summary>
    private readonly IReviewStore _store;

    /// <summary>
    ///     The service that creates the reviews.
    /// </summary>
    private readonly ReviewService _reviews;

    /// <summary>
    ///     The repository registry.
    /// </summary>
    private readonly RepositoryService _repositories;

    /// <summary>
    ///     Opens sessions on git repositories.
    /// </summary>
    private readonly IGitRepositoryProvider _git;

    /// <summary>
    ///     Tells which reviews are signed.
    /// </summary>
    private readonly SigningService _signing;

    /// <summary>
    ///     Reads the review-sets out of the configuration.
    /// </summary>
    private readonly IReviewSetParser _parser;

    /// <summary>
    ///     Initializes a new instance of the <see cref="ReviewPlanner"/> class.
    /// </summary>
    /// <param name="store">The store that lists reviews. Must not be null.</param>
    /// <param name="reviews">The service that creates reviews. Must not be null.</param>
    /// <param name="repositories">The repository registry. Must not be null.</param>
    /// <param name="git">The git access. Must not be null.</param>
    /// <param name="signing">The signing service that decides whether a review is signed. Must not be null.</param>
    /// <param name="parser">The parser of the review configuration. Must not be null.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    public ReviewPlanner(
        IReviewStore store,
        ReviewService reviews,
        RepositoryService repositories,
        IGitRepositoryProvider git,
        SigningService signing,
        IReviewSetParser parser)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(reviews);
        ArgumentNullException.ThrowIfNull(repositories);
        ArgumentNullException.ThrowIfNull(git);
        ArgumentNullException.ThrowIfNull(signing);
        ArgumentNullException.ThrowIfNull(parser);
        _store = store;
        _reviews = reviews;
        _repositories = repositories;
        _git = git;
        _signing = signing;
        _parser = parser;
    }

    /// <summary>
    ///     Computes the plan for a head revision of a repository.
    /// </summary>
    /// <param name="projectId">The identity of the project the reviews would belong to.</param>
    /// <param name="repositoryId">The identity of an enabled registered repository.</param>
    /// <param name="headRevision">The head revision: a branch, tag or commit name (see <see cref="RevisionValidator"/>).</param>
    /// <param name="mode">How baselines are chosen.</param>
    /// <param name="baselineRevision">The baseline revision; required for <see cref="BaselineMode.Explicit"/>, ignored otherwise.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The plan, with one row per review-set in the order of the configuration.</returns>
    /// <exception cref="DomainRuleException">
    ///     Thrown when the plan cannot be made at all: the project, repository or a revision is not valid or not
    ///     found, git fails, or the head has no usable <c>.reviewmark.yaml</c>
    ///     (<see cref="DomainRule.ReviewSetConfigMissing"/>, <see cref="DomainRule.ReviewSetConfigInvalid"/>). A
    ///     problem with one set never fails the plan; it becomes a <see cref="PlanRowStatus.CannotPlan"/> row.
    /// </exception>
    public async Task<ReviewPlan> PlanAsync(
        int projectId,
        int repositoryId,
        string? headRevision,
        BaselineMode mode,
        string? baselineRevision,
        CancellationToken cancellationToken = default)
    {
        // Validate everything that needs no I/O first so a bad request fails fast and cheaply
        var head = RevisionValidator.Validate(headRevision?.Trim());
        var explicitRevision = mode == BaselineMode.Explicit ? RevisionValidator.Validate(baselineRevision?.Trim()) : null;
        await EnsureProjectAsync(projectId, cancellationToken);
        var access = await _repositories.GetAccessAsync(repositoryId, cancellationToken);

        // One session, so the repository is fetched once for the whole plan
        await using var session = await _git.OpenAsync(access, cancellationToken);
        var headSha = await session.ResolveRevisionAsync(head, cancellationToken);
        var explicitSha = explicitRevision is null ? null : await session.ResolveRevisionAsync(explicitRevision, cancellationToken);

        // The head listing finds the configuration and is also the change list of every full review
        var headList = await session.ListChangesAsync(null, headSha, cancellationToken);
        var definitions = _parser.Parse(await ReadConfigurationAsync(session, headList, cancellationToken));
        var existing = (await _store.ListReviewsAsync(projectId, cancellationToken))
            .Where(review => review.RepositoryId == repositoryId)
            .ToList();

        // Pass 1: selection and baseline of every set
        var candidates = new List<Candidate>();
        foreach (var definition in definitions)
        {
            var candidate = Candidate.For(definition);
            if (candidate.Problem is null)
            {
                await ChooseBaselineAsync(
                    candidate, session, projectId, existing, headSha, mode, explicitRevision, explicitSha, cancellationToken);
            }

            candidates.Add(candidate);
        }

        // Pass 2: the changes, listed once per distinct baseline, then the classification of every set
        var lists = new Dictionary<string, GitChangeSet> { [string.Empty] = headList };
        var baselines = candidates
            .Where(c => c.Problem is null && !c.Unchanged && c.BaselineSha is not null)
            .Select(c => c.BaselineSha!)
            .Distinct();
        foreach (var baseline in baselines)
        {
            lists[baseline] = await session.ListChangesAsync(baseline, headSha, cancellationToken);
        }

        var rows = candidates
            .Select(candidate => ToRow(candidate, lists, existing, headSha, mode))
            .ToList();
        return new ReviewPlan(
            access.RepositoryId,
            access.Name,
            head,
            headSha,
            mode,
            explicitRevision,
            explicitSha,
            rows);
    }

    /// <summary>
    ///     Creates the reviews a user picked from a plan.
    /// </summary>
    /// <param name="projectId">The identity of the project the reviews belong to.</param>
    /// <param name="repositoryId">The identity of an enabled registered repository.</param>
    /// <param name="headSha">The full head commit of the plan.</param>
    /// <param name="requests">
    ///     The picked rows, at most <see cref="ReviewLimits.MaxReviewSets"/>; created in the order given. Must not be null.
    /// </param>
    /// <param name="actor">The signed-in user creating the reviews; recorded in the audit trail. Must not be null.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>One result per request, in the same order.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="requests"/> or <paramref name="actor"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when there are more than <see cref="ReviewLimits.MaxReviewSets"/> requests.</exception>
    /// <exception cref="DomainRuleException">
    ///     Thrown when the whole batch cannot start: the commit, project or repository is not valid, or git cannot
    ///     open the repository. A problem with one request is reported in its result instead.
    /// </exception>
    public async Task<IReadOnlyList<PlanCreateResult>> CreateAsync(
        int projectId,
        int repositoryId,
        string headSha,
        IReadOnlyList<PlanCreateRequest> requests,
        Actor actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requests);
        ArgumentNullException.ThrowIfNull(actor);
        if (requests.Count > ReviewLimits.MaxReviewSets)
        {
            throw new ArgumentException($"At most {ReviewLimits.MaxReviewSets} reviews can be created at once.", nameof(requests));
        }

        // The commits come from a page, so they are checked like anything else that reaches git
        RequireCommit(headSha);
        await EnsureProjectAsync(projectId, cancellationToken);
        var access = await _repositories.GetAccessAsync(repositoryId, cancellationToken);

        // One session for the batch: one fetch, and every review is made from the commits of the plan
        var results = new List<PlanCreateResult>();
        await using var session = await _git.OpenAsync(access, cancellationToken);
        foreach (var request in requests)
        {
            results.Add(await CreateOneAsync(session, access, projectId, headSha, request, actor, cancellationToken));
        }

        return results;
    }

    /// <summary>
    ///     Builds the review id for a review-set: the set id made safe for page addresses, made unique among the given ids.
    /// </summary>
    /// <remarks>
    ///     Characters outside letters, digits, <c>.</c>, <c>_</c> and <c>-</c> become <c>-</c>; runs of <c>-</c> collapse;
    ///     leading characters that are not letters or digits and trailing <c>-</c>, <c>.</c> and <c>_</c> are removed;
    ///     an empty result becomes <c>review</c>; the base is cut to 96 characters so a suffix always fits. When the
    ///     id is taken, <c>-2</c>, <c>-3</c> and so on are appended until one is free.
    /// </remarks>
    /// <param name="setId">The review-set identifier. Must not be null.</param>
    /// <param name="taken">The review ids already in use in the project (compared ordinally). Must not be null.</param>
    /// <returns>A valid review id that is not in <paramref name="taken"/>.</returns>
    internal static string GenerateId(string setId, ISet<string> taken)
    {
        ArgumentNullException.ThrowIfNull(setId);
        ArgumentNullException.ThrowIfNull(taken);

        // Replace what an id may not contain, then collapse and trim so the id starts and ends cleanly
        var builder = new StringBuilder(setId.Length);
        foreach (var character in setId)
        {
            var allowed = char.IsAsciiLetterOrDigit(character) || character is '.' or '_';
            var next = allowed ? character : '-';
            if (next == '-' && builder.Length > 0 && builder[^1] == '-')
            {
                continue;
            }

            builder.Append(next);
        }

        // Leading characters that are not a letter or digit cannot start an id
        var start = 0;
        while (start < builder.Length && !char.IsAsciiLetterOrDigit(builder[start]))
        {
            start++;
        }

        var cleaned = builder.ToString()[start..];
        var baseId = TrimEnd(cleaned.Length > MaxIdBaseLength ? cleaned[..MaxIdBaseLength] : cleaned);
        if (baseId.Length == 0)
        {
            baseId = "review";
        }

        // Append a number until the id is free
        var candidate = baseId;
        var suffix = 2;
        while (taken.Contains(candidate))
        {
            candidate = $"{baseId}-{suffix}";
            suffix++;
        }

        return candidate;
    }

    /// <summary>
    ///     Builds the title of a planned review.
    /// </summary>
    /// <param name="setTitle">The title of the review-set.</param>
    /// <param name="baselineSha">The baseline commit, or <c>null</c> for all files.</param>
    /// <param name="headSha">The head commit.</param>
    /// <returns>
    ///     The set title followed by the range, for example <c>Core (4f2a9c1..8d3e0b2)</c> or
    ///     <c>Core (all files at 8d3e0b2)</c>; the set title is cut so the whole title is at most
    ///     <see cref="ReviewLimits.MaxReviewTitleLength"/> characters.
    /// </returns>
    internal static string GenerateTitle(string setTitle, string? baselineSha, string headSha)
    {
        var suffix = baselineSha is null
            ? $"(all files at {Short(headSha)})"
            : $"({Short(baselineSha)}..{Short(headSha)})";
        return AuditText.Clean(setTitle, ReviewLimits.MaxReviewTitleLength - suffix.Length - 1) + " " + suffix;
    }

    /// <summary>
    ///     Shortens a commit identifier for display.
    /// </summary>
    /// <param name="sha">The commit identifier.</param>
    /// <returns>The first seven characters.</returns>
    private static string Short(string sha) => sha.Length > ShortShaLength ? sha[..ShortShaLength] : sha;

    /// <summary>
    ///     Removes trailing characters an id may not end with.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>The text without trailing <c>-</c>, <c>.</c> and <c>_</c>.</returns>
    private static string TrimEnd(string text) => text.TrimEnd('-', '.', '_');

    /// <summary>
    ///     Checks that a text is a full commit identifier.
    /// </summary>
    /// <param name="sha">The text.</param>
    /// <exception cref="DomainRuleException">Thrown with <see cref="DomainRule.RevisionInvalid"/> when it is not 40 or 64 lowercase hexadecimal characters.</exception>
    private static void RequireCommit(string? sha)
    {
        var valid = sha is not null
            && (sha.Length == 40 || sha.Length == 64)
            && sha.All(c => c is (>= '0' and <= '9') or (>= 'a' and <= 'f'));
        if (!valid)
        {
            throw new DomainRuleException(DomainRule.RevisionInvalid, "A commit must be given as its full hexadecimal identifier.");
        }
    }

    /// <summary>
    ///     Checks that the project exists.
    /// </summary>
    /// <param name="projectId">The project identity.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the project exists.</returns>
    /// <exception cref="DomainRuleException">Thrown with <see cref="DomainRule.ProjectNotFound"/>.</exception>
    private async Task EnsureProjectAsync(int projectId, CancellationToken cancellationToken)
    {
        if (await _store.GetProjectAsync(projectId, cancellationToken) is null)
        {
            throw new DomainRuleException(DomainRule.ProjectNotFound, $"Project {projectId} does not exist.");
        }
    }

    /// <summary>
    ///     Finds and reads the <c>.reviewmark.yaml</c> at the root of the head.
    /// </summary>
    /// <param name="session">The open session.</param>
    /// <param name="headList">The listing of all files at the head.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The bytes of the file.</returns>
    /// <exception cref="DomainRuleException">Thrown with <see cref="DomainRule.ReviewSetConfigMissing"/> or <see cref="DomainRule.ReviewSetConfigInvalid"/> (too large).</exception>
    private static async Task<byte[]> ReadConfigurationAsync(
        IGitRepository session,
        GitChangeSet headList,
        CancellationToken cancellationToken)
    {
        // A link or submodule of that name is in Skipped, not Changes, so it counts as missing
        var blob = headList.Changes.FirstOrDefault(change => string.Equals(change.Path, ConfigPath, StringComparison.Ordinal))?.HeadBlobSha;
        if (blob is null)
        {
            throw new DomainRuleException(
                DomainRule.ReviewSetConfigMissing,
                "This revision has no .reviewmark.yaml in its root.");
        }

        // The size is checked before the content is read
        if (await session.GetBlobSizeAsync(blob, cancellationToken) > ReviewLimits.MaxReviewSetFileBytes)
        {
            throw new DomainRuleException(
                DomainRule.ReviewSetConfigInvalid,
                $"The file .reviewmark.yaml is larger than {ReviewLimits.MaxReviewSetFileBytes / 1024} KiB.");
        }

        return await session.ReadBlobAsync(blob, ReviewLimits.MaxReviewSetFileBytes, cancellationToken);
    }

    /// <summary>
    ///     Chooses the baseline of one set according to the mode.
    /// </summary>
    /// <param name="candidate">The set; its baseline, note and unchanged flag are set.</param>
    /// <param name="session">The open session, for the ancestor check.</param>
    /// <param name="projectId">The project identity.</param>
    /// <param name="existing">The reviews of the project made from this repository.</param>
    /// <param name="headSha">The head commit.</param>
    /// <param name="mode">The baseline mode.</param>
    /// <param name="explicitRevision">The baseline revision as entered, for explicit mode.</param>
    /// <param name="explicitSha">The baseline commit, for explicit mode.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the baseline is chosen.</returns>
    private async Task ChooseBaselineAsync(
        Candidate candidate,
        IGitRepository session,
        int projectId,
        IReadOnlyList<Review> existing,
        string headSha,
        BaselineMode mode,
        string? explicitRevision,
        string? explicitSha,
        CancellationToken cancellationToken)
    {
        switch (mode)
        {
            case BaselineMode.None:
                candidate.Note = "Full review: all selected files";
                return;

            case BaselineMode.Explicit:
                candidate.BaselineSha = explicitSha;
                candidate.Note = $"Changes since {explicitRevision} ({Short(explicitSha!)})";
                return;
        }

        // Last reviewed: the most recently signed review of this set is the candidate for the baseline
        var last = await FindLastSignedAsync(projectId, existing, candidate.Definition.Id, cancellationToken);
        if (last is null)
        {
            candidate.Note = "Full review: no signed review of this set yet";
            return;
        }

        // A changed selection would let newly selected files escape review in a delta, so it forces a full review
        if (!string.Equals(last.Selection, candidate.SelectionText, StringComparison.Ordinal))
        {
            candidate.Note = $"Full review: the file selection changed since the last signed review ({last.Id})";
            return;
        }

        if (string.Equals(last.HeadSha, headSha, StringComparison.Ordinal))
        {
            candidate.Unchanged = true;
            candidate.Note = $"Unchanged since review '{last.Id}'";
            return;
        }

        // The baseline must be an ancestor of the head; after a force-push or on another branch it is not
        try
        {
            if (await session.IsAncestorAsync(last.HeadSha!, headSha, cancellationToken))
            {
                candidate.BaselineSha = last.HeadSha;
                candidate.Note = $"Changes since review '{last.Id}' ({Short(last.HeadSha!)})";
            }
            else
            {
                candidate.Note = $"Full review: the last signed review ({last.Id}) is not an ancestor of this revision " +
                    "(history was rewritten or it is on another branch)";
            }
        }
        catch (DomainRuleException ex) when (ex.Rule is DomainRule.GitAccessFailed or DomainRule.GitTimeout)
        {
            // Not being able to tell is not a reason to fail the page; a full review is always correct
            candidate.Note = $"Full review: it could not be checked whether the last signed review ({last.Id}) is an ancestor of this revision";
        }
    }

    /// <summary>
    ///     Finds the most recently signed review of a review-set.
    /// </summary>
    /// <remarks>
    ///     "Signed" is the signing service's approval: at least one reviewer slot and every slot signed with the
    ///     current record digest. "Most recent" is the latest signature time; reviews created at the same time
    ///     fall back to the creation time.
    /// </remarks>
    /// <param name="projectId">The project identity.</param>
    /// <param name="existing">The reviews of the project made from this repository.</param>
    /// <param name="setId">The review-set identifier.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The review, or <c>null</c> when no review of the set is signed.</returns>
    private async Task<Review?> FindLastSignedAsync(
        int projectId,
        IReadOnlyList<Review> existing,
        string setId,
        CancellationToken cancellationToken)
    {
        Review? best = null;
        DateTimeOffset bestTime = default;
        foreach (var review in existing.Where(r => r.ReviewSetId == setId && r.HeadSha is not null))
        {
            var approval = await _signing.GetApprovalAsync(projectId, review.Id, cancellationToken);
            if (approval is not { IsApproved: true })
            {
                continue;
            }

            var signedAt = approval.Slots.Max(slot => slot.SignedAt) ?? review.Created;
            if (best is null || signedAt > bestTime || (signedAt == bestTime && review.Created > best.Created))
            {
                best = review;
                bestTime = signedAt;
            }
        }

        return best;
    }

    /// <summary>
    ///     Classifies one set once the changes for its baseline are known.
    /// </summary>
    /// <param name="candidate">The set.</param>
    /// <param name="lists">The change lists by baseline commit (the empty text for none).</param>
    /// <param name="existing">The reviews of the project made from this repository.</param>
    /// <param name="headSha">The head commit.</param>
    /// <param name="mode">The baseline mode.</param>
    /// <returns>The row.</returns>
    private static PlanRow ToRow(
        Candidate candidate,
        Dictionary<string, GitChangeSet> lists,
        IReadOnlyList<Review> existing,
        string headSha,
        BaselineMode mode)
    {
        var definition = candidate.Definition;
        PlanRow Row(PlanRowStatus status, int added = 0, int modified = 0, int deleted = 0, string? reviewId = null, string? problem = null) =>
            new(
                definition.Id,
                definition.Title,
                candidate.SelectionText,
                status,
                candidate.BaselineSha,
                candidate.Note,
                added,
                modified,
                deleted,
                reviewId,
                problem);

        if (candidate.Problem is not null)
        {
            return Row(PlanRowStatus.CannotPlan, problem: candidate.Problem);
        }

        if (candidate.Unchanged)
        {
            return Row(PlanRowStatus.NothingToReview);
        }

        // Deleted files are matched by their path like any other, as when a review is created
        var selected = lists[candidate.BaselineSha ?? string.Empty].Changes
            .Where(change => SelectionMatcher.IsMatch(change.Path, candidate.Patterns!))
            .ToList();
        if (selected.Count == 0)
        {
            candidate.Note = mode == BaselineMode.None ? "No file matches the paths" : candidate.Note;
            return Row(PlanRowStatus.NothingToReview);
        }

        if (selected.Count > ReviewLimits.MaxFileCount)
        {
            return Row(
                PlanRowStatus.CannotPlan,
                problem: $"Too many files ({selected.Count}); a review can have at most {ReviewLimits.MaxFileCount}. " +
                    "Narrow the paths in .reviewmark.yaml.");
        }

        var added = selected.Count(change => change.Kind == ChangeKind.Added);
        var modified = selected.Count(change => change.Kind == ChangeKind.Modified);
        var deleted = selected.Count(change => change.Kind == ChangeKind.Deleted);
        var filed = existing
            .Where(r => r.ReviewSetId == definition.Id && r.HeadSha == headSha && r.BaselineSha == candidate.BaselineSha)
            .OrderBy(r => r.Created)
            .FirstOrDefault();
        return filed is null
            ? Row(PlanRowStatus.WillCreate, added, modified, deleted)
            : Row(PlanRowStatus.AlreadyFiled, added, modified, deleted, reviewId: filed.Id);
    }

    /// <summary>
    ///     Creates one requested review, or says why it could not be created.
    /// </summary>
    /// <param name="session">The open session.</param>
    /// <param name="access">The repository.</param>
    /// <param name="projectId">The project identity.</param>
    /// <param name="headSha">The head commit.</param>
    /// <param name="request">The request.</param>
    /// <param name="actor">The acting user.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    ///     The result; a rule violation of this request is a <see cref="PlanCreateOutcome.Failed"/> result, not an exception,
    ///     and the text of a git access failure is replaced by <see cref="GitAccessFailedMessage"/>.
    /// </returns>
    private async Task<PlanCreateResult> CreateOneAsync(
        IGitRepository session,
        RepositoryAccess access,
        int projectId,
        string headSha,
        PlanCreateRequest request,
        Actor actor,
        CancellationToken cancellationToken)
    {
        try
        {
            // Everything is validated again: the request may come from a page, and a plan can be stale
            if (request.BaselineSha is not null)
            {
                RequireCommit(request.BaselineSha);
            }

            var (patterns, selectionText) = ReviewService.ParseSelection(request.Selection);
            var title = ReviewService.ValidateTitle(GenerateTitle(request.Title, request.BaselineSha, headSha));

            // A fresh list, because someone else may have created the review since the plan was made
            var current = (await _store.ListReviewsAsync(projectId, cancellationToken)).ToList();
            var filed = current.FirstOrDefault(r =>
                r.RepositoryId == access.RepositoryId
                && r.ReviewSetId == request.SetId
                && r.HeadSha == headSha
                && r.BaselineSha == request.BaselineSha);
            if (filed is not null)
            {
                return new PlanCreateResult(request.SetId, PlanCreateOutcome.AlreadyFiled, filed.Id, $"Review '{filed.Id}' already exists.");
            }

            // The id existence check is not race-free; the store's unique key is, so a duplicate means "try the next suffix"
            var taken = current.Select(r => r.Id).ToHashSet(StringComparer.Ordinal);
            var attempt = 1;
            while (true)
            {
                var id = GenerateId(request.SetId, taken);
                ReviewService.ValidateId(id);
                try
                {
                    var created = await _reviews.CreateFromSessionAsync(
                        session,
                        access,
                        projectId,
                        id,
                        title,
                        headSha,
                        request.BaselineSha,
                        patterns,
                        selectionText,
                        request.SetId,
                        Short(headSha),
                        request.BaselineSha is null ? null : Short(request.BaselineSha),
                        actor,
                        cancellationToken);
                    return new PlanCreateResult(request.SetId, PlanCreateOutcome.Created, created.Review.Id, $"Review '{created.Review.Id}' was created.");
                }
                catch (DomainRuleException ex) when (ex.Rule == DomainRule.ReviewIdDuplicate && attempt < MaxIdAttempts)
                {
                    taken.Add(id);
                    attempt++;
                }
            }
        }
        catch (DomainRuleException ex)
        {
            return new PlanCreateResult(request.SetId, PlanCreateOutcome.Failed, null, SafeMessage(ex));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // An unexpected fault (a database error, for example) must not abort the batch and lose the results of the
            // reviews already created; the row fails with a generic message because the exception text may carry
            // internal details. Each review is stored in one transaction, so nothing is left behind for this row
            return new PlanCreateResult(request.SetId, PlanCreateOutcome.Failed, null, UnexpectedFailureMessage);
        }
    }

    /// <summary>
    ///     Gets the text of a rule violation that is safe to show next to a review-set.
    /// </summary>
    /// <param name="exception">The rule violation.</param>
    /// <returns>The fixed <see cref="GitAccessFailedMessage"/> for a git access failure; the message of the exception otherwise.</returns>
    /// <remarks>
    ///     Only a git access failure can carry text printed by git (a location, for example), so only it is replaced; the
    ///     messages of the other rules are fixed sentences written for users.
    /// </remarks>
    private static string SafeMessage(DomainRuleException exception) =>
        exception.Rule == DomainRule.GitAccessFailed ? GitAccessFailedMessage : exception.Message;

    /// <summary>
    ///     The working data of one review-set while the plan is made.
    /// </summary>
    private sealed class Candidate
    {
        /// <summary>
        ///     Gets the review-set.
        /// </summary>
        public required ReviewSetDefinition Definition { get; init; }

        /// <summary>
        ///     Gets or sets the parsed selection; <c>null</c> when <see cref="Problem"/> is set.
        /// </summary>
        public IReadOnlyList<SelectionPattern>? Patterns { get; set; }

        /// <summary>
        ///     Gets or sets the normalized selection text; empty when <see cref="Problem"/> is set.
        /// </summary>
        public string SelectionText { get; set; } = string.Empty;

        /// <summary>
        ///     Gets or sets why the set cannot be planned, or <c>null</c>.
        /// </summary>
        public string? Problem { get; set; }

        /// <summary>
        ///     Gets or sets the baseline commit, or <c>null</c> for all files.
        /// </summary>
        public string? BaselineSha { get; set; }

        /// <summary>
        ///     Gets or sets the sentence that says where the baseline comes from.
        /// </summary>
        public string? Note { get; set; }

        /// <summary>
        ///     Gets or sets a value indicating whether the head equals the head of the last signed review.
        /// </summary>
        public bool Unchanged { get; set; }

        /// <summary>
        ///     Prepares a set: validates its id and selection.
        /// </summary>
        /// <param name="definition">The review-set.</param>
        /// <returns>The candidate; <see cref="Problem"/> is set when the set cannot be planned.</returns>
        public static Candidate For(ReviewSetDefinition definition)
        {
            var candidate = new Candidate { Definition = definition };
            if (definition.Id.Length > ReviewLimits.MaxReviewSetIdLength)
            {
                candidate.Problem = $"The set id is longer than {ReviewLimits.MaxReviewSetIdLength} characters.";
                return candidate;
            }

            try
            {
                var (patterns, text) = ReviewService.ParseSelection(string.Join('\n', definition.Paths));
                candidate.Patterns = patterns;
                candidate.SelectionText = text;
            }
            catch (DomainRuleException ex)
            {
                candidate.Problem = ex.Message;
            }

            return candidate;
        }
    }
}
