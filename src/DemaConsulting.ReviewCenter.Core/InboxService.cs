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
///     Builds the inbox of a user (the work waiting for the user and what is new) and keeps the per-user read state
///     that the unread markers of the review page use.
/// </summary>
/// <remarks>
///     The inbox is also the notification list: there is no notification record, every row is computed from the
///     review data when the inbox is read, and the read state of a user and review (see
///     <see cref="ReviewUserState"/>) says what is new. Only reviews of projects the user can see are considered, so
///     a removed member, a disabled user, a user with a temporary password and an anonymous or command-line actor
///     get an empty inbox and no error. The rules live in <see cref="InboxRules"/>. Reading is cheap per review but
///     needs one snapshot for each review the user is involved in, because "approved" depends on the record digest.
///     Read state is not audited and not part of the record digest. The service holds no state of its own and is
///     safe for concurrent use as long as the injected stores are.
/// </remarks>
public sealed class InboxService
{
    /// <summary>
    ///     The chip of a slot without any signature.
    /// </summary>
    public const string NotSignedChip = "Not signed";

    /// <summary>
    ///     The chip of a slot whose signature no longer covers the record.
    /// </summary>
    public const string SignAgainChip = "Out of date, sign again";

    /// <summary>
    ///     The chip of an approved review.
    /// </summary>
    public const string ApprovedChip = "Approved";

    /// <summary>
    ///     The chip of a reopened review.
    /// </summary>
    public const string ReopenedChip = "Reopened";

    /// <summary>
    ///     The store of the read state and of the involved reviews.
    /// </summary>
    private readonly IInboxStore _inbox;

    /// <summary>
    ///     The store of the reviews, threads and signatures.
    /// </summary>
    private readonly IReviewStore _reviews;

    /// <summary>
    ///     Decides who may see which project.
    /// </summary>
    private readonly IProjectAccess _access;

    /// <summary>
    ///     The store of the users, to find the identity of an actor.
    /// </summary>
    private readonly IUserStore _users;

    /// <summary>
    ///     The clock for the read times and the due dates.
    /// </summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>
    ///     Initializes a new instance of the <see cref="InboxService"/> class.
    /// </summary>
    /// <param name="inbox">The store of the read state. Must not be null.</param>
    /// <param name="reviews">The store of the reviews. Must not be null.</param>
    /// <param name="access">The access rules. Must not be null.</param>
    /// <param name="users">The store of the users. Must not be null.</param>
    /// <param name="timeProvider">The clock; injected so tests control time. Must not be null.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    public InboxService(
        IInboxStore inbox,
        IReviewStore reviews,
        IProjectAccess access,
        IUserStore users,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(inbox);
        ArgumentNullException.ThrowIfNull(reviews);
        ArgumentNullException.ThrowIfNull(access);
        ArgumentNullException.ThrowIfNull(users);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _inbox = inbox;
        _reviews = reviews;
        _access = access;
        _users = users;
        _timeProvider = timeProvider;
    }

    /// <summary>
    ///     Reports whether a comment is new to a user, for the unread markers of the review page.
    /// </summary>
    /// <param name="comment">The comment. Must not be null.</param>
    /// <param name="username">The user name of the user; the user's own comments are never unread. Must not be null.</param>
    /// <param name="lastSeenCommentId">The highest comment identity the user has seen; zero when none.</param>
    /// <returns><c>true</c> when the comment is by someone else and newer than the last seen one.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    public static bool IsUnread(ReviewComment comment, string username, int lastSeenCommentId)
    {
        ArgumentNullException.ThrowIfNull(comment);
        ArgumentNullException.ThrowIfNull(username);
        return InboxRules.IsUnread(comment, username, lastSeenCommentId);
    }

    /// <summary>
    ///     Counts the comments of a review that are new to a user.
    /// </summary>
    /// <param name="threads">The threads of the review with their comments. Must not be null.</param>
    /// <param name="username">The user name of the user. Must not be null.</param>
    /// <param name="lastSeenCommentId">The highest comment identity the user has seen; zero when none.</param>
    /// <returns>The number of unread comments.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    public static int CountUnread(IEnumerable<ReviewThread> threads, string username, int lastSeenCommentId)
    {
        ArgumentNullException.ThrowIfNull(threads);
        ArgumentNullException.ThrowIfNull(username);
        return InboxRules.CountUnread(threads, username, lastSeenCommentId);
    }

    /// <summary>
    ///     Finds the highest comment identity of a review, which is what the user has seen once the review was shown.
    /// </summary>
    /// <param name="threads">The threads of the review with their comments. Must not be null.</param>
    /// <returns>The highest comment identity, or zero when there is no comment.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="threads"/> is null.</exception>
    public static int MaxCommentId(IEnumerable<ReviewThread> threads)
    {
        ArgumentNullException.ThrowIfNull(threads);
        return InboxRules.MaxCommentId(threads);
    }

    /// <summary>
    ///     Finds the highest comment identity of a review among the comments posted at or before a moment, which is
    ///     what the user has seen of a review that was read from that moment on.
    /// </summary>
    /// <param name="threads">The threads of the review with their comments. Must not be null.</param>
    /// <param name="notAfter">The moment the reading started; comments posted later are not counted.</param>
    /// <returns>The highest comment identity, or zero when there is no such comment.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="threads"/> is null.</exception>
    public static int MaxCommentId(IEnumerable<ReviewThread> threads, DateTimeOffset notAfter)
    {
        ArgumentNullException.ThrowIfNull(threads);
        return InboxRules.MaxCommentId(threads, notAfter);
    }

    /// <summary>
    ///     Builds the inbox of a user.
    /// </summary>
    /// <param name="actor">The signed-in user; <c>null</c> and users without access give the empty inbox.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The inbox: the rows by section and the number of reviews that need the user's action.</returns>
    public async Task<InboxView> GetInboxAsync(Actor? actor, CancellationToken cancellationToken = default)
    {
        var user = await FindUserAsync(actor, cancellationToken);
        if (user is null)
        {
            return InboxView.Empty;
        }

        // Only projects the user can see are considered, which also empties the view of a disabled user
        var visible = (await _access.ListVisibleProjectsAsync(actor, cancellationToken)).ToDictionary(project => project.Id);
        if (visible.Count == 0)
        {
            return InboxView.Empty;
        }

        var candidates = (await _inbox.ListInvolvedReviewsAsync(user.Id, user.Username, cancellationToken))
            .Where(candidate => visible.ContainsKey(candidate.ProjectId))
            .ToList();
        var states = (await _inbox.ListStatesAsync(user.Id, cancellationToken))
            .ToDictionary(state => (state.ProjectId, state.ReviewId));
        var today = DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime);

        // What the user may do in a project is the same for all its reviews
        var rights = new Dictionary<int, (bool Contribute, bool Member)>();
        var items = new List<InboxItem>();
        foreach (var candidate in candidates)
        {
            if (!rights.TryGetValue(candidate.ProjectId, out var right))
            {
                right = (
                    await _access.CanContributeAsync(actor, candidate.ProjectId, cancellationToken),
                    await _access.IsMemberAsync(user.Id, candidate.ProjectId, cancellationToken));
                rights[candidate.ProjectId] = right;
            }

            var snapshot = await _reviews.GetSnapshotAsync(candidate.ProjectId, candidate.ReviewId, cancellationToken);
            if (snapshot is null)
            {
                continue;
            }

            states.TryGetValue((candidate.ProjectId, candidate.ReviewId), out var state);
            await AddItemsAsync(
                items,
                new ReviewContext(user, visible[candidate.ProjectId].Name, candidate, snapshot, state, today, right.Contribute, right.Member),
                cancellationToken);
        }

        var ordered = items
            .OrderBy(item => item.Section)
            .ThenBy(item => item.ProjectName, StringComparer.Ordinal)
            .ThenBy(item => item.ReviewId, StringComparer.Ordinal)
            .ToList();
        var actionCount = ordered
            .Where(item => item.Section != InboxSection.Updates)
            .Select(item => (item.ProjectId, item.ReviewId))
            .Distinct()
            .Count();
        return new InboxView(ordered, actionCount);
    }

    /// <summary>
    ///     Counts the reviews that need the action of a user.
    /// </summary>
    /// <param name="actor">The signed-in user; <c>null</c> and users without access give zero.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The number of distinct reviews with an action row (see <see cref="InboxView.ActionCount"/>).</returns>
    public async Task<int> CountAsync(Actor? actor, CancellationToken cancellationToken = default)
    {
        return (await GetInboxAsync(actor, cancellationToken)).ActionCount;
    }

    /// <summary>
    ///     Reads what a user had seen of a review before it is shown now; the review page marks comments after this
    ///     one as unread.
    /// </summary>
    /// <param name="actor">The signed-in user. Must not be null.</param>
    /// <param name="projectId">The identity of the owning project.</param>
    /// <param name="reviewId">The review identifier.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    ///     The highest comment identity the user has seen; zero when the user has not seen the review or is not a
    ///     user of the system.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="actor"/> is null.</exception>
    public async Task<int> GetReviewUnreadAsync(
        Actor actor,
        int projectId,
        string reviewId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var user = await FindUserAsync(actor, cancellationToken);
        if (user is null)
        {
            return 0;
        }

        var state = await _inbox.GetStateAsync(user.Id, projectId, reviewId, cancellationToken);
        return state?.LastSeenCommentId ?? 0;
    }

    /// <summary>
    ///     Records that a user has seen a review up to a comment; the state never moves backwards.
    /// </summary>
    /// <remarks>
    ///     Nothing is stored for a user who cannot see the project or when the review does not exist, so a
    ///     non-viewer never leaves a row behind.
    /// </remarks>
    /// <param name="actor">The signed-in user. Must not be null.</param>
    /// <param name="projectId">The identity of the owning project.</param>
    /// <param name="reviewId">The review identifier.</param>
    /// <param name="maxCommentId">The highest comment identity that was shown to the user; zero when none.</param>
    /// <param name="seenAt">
    ///     The time (UTC) read before the review was loaded, so that a change made while the page loaded is not
    ///     marked as seen; <c>null</c> uses the current time.
    /// </param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns><c>true</c> when the state was stored; <c>false</c> when the user may not see the review.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="actor"/> is null.</exception>
    public async Task<bool> MarkSeenAsync(
        Actor actor,
        int projectId,
        string reviewId,
        int maxCommentId,
        DateTimeOffset? seenAt = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var user = await FindUserAsync(actor, cancellationToken);
        if (user is null
            || !await _access.CanViewAsync(actor, projectId, cancellationToken)
            || !await _reviews.ReviewExistsAsync(projectId, reviewId, cancellationToken))
        {
            return false;
        }

        await _inbox.MarkSeenAsync(
            user.Id,
            projectId,
            reviewId,
            Math.Max(0, maxCommentId),
            seenAt ?? _timeProvider.GetUtcNow(),
            cancellationToken);
        return true;
    }

    /// <summary>
    ///     Marks every review in the inbox of a user as seen, including the ones the user never opened.
    /// </summary>
    /// <param name="actor">The signed-in user. Must not be null.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The number of reviews that were marked.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="actor"/> is null.</exception>
    public async Task<int> MarkAllReadAsync(Actor actor, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);

        // The time is read first so that a change made while the inbox is read stays new
        var now = _timeProvider.GetUtcNow();
        var user = await FindUserAsync(actor, cancellationToken);
        if (user is null)
        {
            return 0;
        }

        var view = await GetInboxAsync(actor, cancellationToken);
        var marks = new List<InboxSeenMark>();
        foreach (var (projectId, reviewId) in view.Items.Select(item => (item.ProjectId, item.ReviewId)).Distinct())
        {
            var threads = await _reviews.ListThreadsAsync(projectId, reviewId, cancellationToken);
            marks.Add(new InboxSeenMark(projectId, reviewId, InboxRules.MaxCommentId(threads, now)));
        }

        await _inbox.MarkSeenManyAsync(user.Id, marks, now, cancellationToken);
        return marks.Count;
    }

    /// <summary>
    ///     Finds the user behind an actor.
    /// </summary>
    /// <param name="actor">The actor; may be null.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The user, or <c>null</c> for no actor, the command line and an unknown user name.</returns>
    private async Task<User?> FindUserAsync(Actor? actor, CancellationToken cancellationToken)
    {
        if (actor is null || string.IsNullOrEmpty(actor.Username) || actor.Username == Actor.CommandLine.Username)
        {
            return null;
        }

        return await _users.GetByUsernameAsync(actor.Username, cancellationToken);
    }

    /// <summary>
    ///     Adds the rows of one review to the inbox.
    /// </summary>
    /// <param name="items">The rows collected so far.</param>
    /// <param name="review">What is known of the review and of the user's rights in its project.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the rows of the review are added.</returns>
    private async Task AddItemsAsync(List<InboxItem> items, ReviewContext review, CancellationToken cancellationToken)
    {
        var (record, slots, signatures) = review.Snapshot;
        var digest = RecordDigest.Compute(record);
        var concluded = ReviewLock.IsConcluded(record, slots, signatures);
        var lastSeenCommentId = review.State?.LastSeenCommentId ?? 0;
        var lastSeenAt = review.State?.LastSeenAt ?? DateTimeOffset.MinValue;
        var username = review.User.Username;
        var isAuthor = InboxRules.SameUser(review.Candidate.CreatedBy, username);
        var mine = slots.Where(slot => InboxRules.IsAssigned(slot, review.User.Id)).ToList();
        var overdue = !concluded && review.Candidate.DueDate is { } due && due < review.Today;

        InboxItem Row(InboxSection section, string chip, bool unread, string? detail = null)
        {
            // The due date is only worth showing where the user has to act
            var action = section != InboxSection.Updates;
            return new InboxItem(
                section,
                review.Candidate.ProjectId,
                review.ProjectName,
                review.Candidate.ReviewId,
                review.Candidate.Title,
                chip,
                unread,
                action ? review.Candidate.DueDate : null,
                action && overdue,
                detail);
        }

        if (!concluded)
        {
            // To sign: one of my slots has no current signature
            if (review.Contribute && review.Member)
            {
                var unsigned = mine
                    .Select(slot => InboxRules.SlotState(slot, signatures, digest))
                    .Where(state => state != SlotSignatureState.Signed)
                    .ToList();
                if (unsigned.Count > 0)
                {
                    var chip = unsigned.Contains(SlotSignatureState.NotSigned) ? NotSignedChip : SignAgainChip;
                    var unread = await IsToSignUnreadAsync(review, lastSeenCommentId, lastSeenAt, cancellationToken);
                    items.Add(Row(InboxSection.ToSign, chip, unread));
                }
            }

            // Replies waiting: someone answered in a thread of mine, or on my review
            if (review.Contribute)
            {
                var waiting = InboxRules.WaitingThreads(record.Threads, username, isAuthor);
                if (waiting.Count > 0)
                {
                    var unread = InboxRules.CountUnread(waiting, username, lastSeenCommentId) > 0;
                    items.Add(Row(InboxSection.RepliesWaiting, $"{waiting.Count} waiting", unread));
                }
            }

            // Failed checks: my own review has checks answered Fail
            if (isAuthor)
            {
                var failed = InboxRules.FailedAnswers(record, slots.Count);
                if (failed.Count > 0)
                {
                    var unread = failed.Any(
                        answer => !InboxRules.SameUser(answer.AnsweredByUsername, username) && answer.AnsweredAt > lastSeenAt);
                    items.Add(Row(InboxSection.FailedChecks, $"{failed.Count} failed", unread));
                }
            }
        }

        if (mine.Count > 0 || isAuthor)
        {
            await AddUpdatesAsync(items, review, Row, new ApprovalFacts(concluded, digest, signatures, lastSeenAt), cancellationToken);
        }
    }

    /// <summary>
    ///     Decides whether a To sign row is new to the user: the user never saw the review, or something that bears
    ///     on the signature happened after the user last saw it.
    /// </summary>
    /// <param name="review">What is known of the review.</param>
    /// <param name="lastSeenCommentId">The highest comment identity the user has seen; zero when none.</param>
    /// <param name="lastSeenAt">The time the user last saw the review; the minimum value when never.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns><c>true</c> when there is no read state, or when someone else signed, answered a check, commented,
    /// or reopened the review after the user last saw it.</returns>
    private async Task<bool> IsToSignUnreadAsync(
        ReviewContext review,
        int lastSeenCommentId,
        DateTimeOffset lastSeenAt,
        CancellationToken cancellationToken)
    {
        if (review.State is null)
        {
            return true;
        }

        var (record, slots, signatures) = review.Snapshot;
        var username = review.User.Username;

        // Only a submitted ballot is news to the others (it is what the record covers); the sole ballot of a
        // single-slot review counts as submitted, consistent with the projection
        var visibleBallots = slots.Count == 1
            ? record.Ballots
            : record.Ballots.Where(ballot => ballot.Status == BallotStatus.Submitted).ToList();
        if (signatures.Any(signature => signature.UserId != review.User.Id && signature.SignedAt > lastSeenAt)
            || visibleBallots.Any(ballot => ballot.Answers.Any(answer =>
                   !InboxRules.SameUser(answer.AnsweredByUsername, username) && answer.AnsweredAt > lastSeenAt))
            || InboxRules.CountUnread(record.Threads, username, lastSeenCommentId) > 0)
        {
            return true;
        }

        if (record.Reopens == 0)
        {
            return false;
        }

        var reopens = await _reviews.ListReopensAsync(review.Candidate.ProjectId, review.Candidate.ReviewId, cancellationToken);
        return reopens.Any(reopen => !InboxRules.SameUser(reopen.ReopenedBy, username) && reopen.ReopenedAt > lastSeenAt);
    }

    /// <summary>
    ///     Adds the information rows of one review: approved or reopened since the user last looked.
    /// </summary>
    /// <param name="items">The rows collected so far.</param>
    /// <param name="review">What is known of the review.</param>
    /// <param name="row">Creates a row of the review.</param>
    /// <param name="facts">The approval facts of the review.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the rows are added.</returns>
    private async Task AddUpdatesAsync(
        List<InboxItem> items,
        ReviewContext review,
        Func<InboxSection, string, bool, string?, InboxItem> row,
        ApprovalFacts facts,
        CancellationToken cancellationToken)
    {
        if (facts.Approved)
        {
            // A signature of my own that completes the approval is not news to me
            var completing = InboxRules.CompletingSignature(facts.Signatures, facts.Digest);
            if (completing is not null && completing.UserId != review.User.Id && completing.SignedAt > facts.LastSeenAt)
            {
                items.Add(row(InboxSection.Updates, ApprovedChip, true, $"Signed by {completing.DisplayName}."));
            }

            return;
        }

        if (review.Snapshot.Record.Reopens == 0)
        {
            return;
        }

        var reopens = await _reviews.ListReopensAsync(review.Candidate.ProjectId, review.Candidate.ReviewId, cancellationToken);
        var latest = reopens
            .Where(reopen => !InboxRules.SameUser(reopen.ReopenedBy, review.User.Username) && reopen.ReopenedAt > facts.LastSeenAt)
            .OrderBy(reopen => reopen.ReopenedAt)
            .LastOrDefault();
        if (latest is not null)
        {
            items.Add(row(InboxSection.Updates, ReopenedChip, true, $"Reopened by {latest.ReopenedByDisplayName}."));
        }
    }

    /// <summary>
    ///     What is known of one review while its rows are built.
    /// </summary>
    /// <param name="User">The user the inbox is for.</param>
    /// <param name="ProjectName">The name of the owning project.</param>
    /// <param name="Candidate">The light description of the review.</param>
    /// <param name="Snapshot">The record, slots and signatures of the review.</param>
    /// <param name="State">The read state of the user, or <c>null</c> when the user never saw the review.</param>
    /// <param name="Today">The current date (UTC), for the due date.</param>
    /// <param name="Contribute">Whether the user may contribute in the project.</param>
    /// <param name="Member">Whether the user is an explicit member of the project.</param>
    private sealed record ReviewContext(
        User User,
        string ProjectName,
        InboxCandidate Candidate,
        ReviewSnapshot Snapshot,
        ReviewUserState? State,
        DateOnly Today,
        bool Contribute,
        bool Member);

    /// <summary>
    ///     The approval facts of one review that the information rows need.
    /// </summary>
    /// <param name="Approved">Whether the review is approved.</param>
    /// <param name="Digest">The digest of the current record.</param>
    /// <param name="Signatures">The signatures of the review.</param>
    /// <param name="LastSeenAt">The time the user last saw the review; the minimum value when never.</param>
    private sealed record ApprovalFacts(
        bool Approved,
        string Digest,
        IReadOnlyList<ReviewSignature> Signatures,
        DateTimeOffset LastSeenAt);
}
