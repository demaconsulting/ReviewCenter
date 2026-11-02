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
///     needs one snapshot for each review the user is involved in, because "concluded" (approved or rejected) depends
///     on the record digest.
///     Read state is not audited and not part of the record digest. The service holds no state of its own and is
///     safe for concurrent use as long as the injected stores are.
/// </remarks>
public sealed class InboxService
{
    /// <summary>
    ///     The chip of a reviewer who has not started the ballot.
    /// </summary>
    public const string NotStartedChip = "Not started";

    /// <summary>
    ///     The chip of a reviewer whose ballot is a draft.
    /// </summary>
    public const string DraftChip = "Draft";

    /// <summary>
    ///     The chip of a slot without any signature.
    /// </summary>
    public const string NotSignedChip = "Not signed";

    /// <summary>
    ///     The chip of a slot whose signature no longer covers the record.
    /// </summary>
    public const string SignAgainChip = "The review changed, sign again";

    /// <summary>
    ///     The chip of an approved review.
    /// </summary>
    public const string ApprovedChip = "Approved";

    /// <summary>
    ///     The chip of a rejected review.
    /// </summary>
    public const string RejectedChip = "Rejected";

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
        var names = new UserNames(_users);
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

            // Who may see the answers of the other reviewers: the people who steer or check the review, and those who
            // have handed in their own ballot (see BallotVisibility)
            var hidden = snapshot.Slots.Count > 1
                         && !BallotVisibility.CanSeeOthers(
                             await SeesAllAsync(actor, user, candidate, right.Contribute, cancellationToken),
                             snapshot,
                             user.Id);
            var project = visible[candidate.ProjectId];
            await AddItemsAsync(
                items,
                new ReviewContext(
                    user,
                    project.Name,
                    candidate,
                    snapshot,
                    state,
                    today,
                    right.Contribute,
                    right.Member,
                    hidden,
                    InboxRules.AuthorBlocked(project, candidate.CreatedBy, user.Username),
                    names),
                cancellationToken);
        }

        var ordered = items
            .OrderBy(item => item.Section)
            .ThenBy(item => item.ProjectName, StringComparer.Ordinal)
            .ThenBy(item => item.ReviewId, StringComparer.Ordinal)
            .ToList();
        var actionCount = ordered
            .Where(item => InboxSections.IsAction(item.Section))
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
    ///     Tells whether the user sees everything in a review without handing in a ballot: the creator of the review who
    ///     can still contribute, an administrator of the project or the system, and an Auditor.
    /// </summary>
    /// <param name="actor">The signed-in user.</param>
    /// <param name="user">The user behind the actor.</param>
    /// <param name="candidate">The light description of the review.</param>
    /// <param name="contribute">Whether the user may contribute in the project.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns><c>true</c> when the user steers or checks the review.</returns>
    private async Task<bool> SeesAllAsync(
        Actor? actor,
        User user,
        InboxCandidate candidate,
        bool contribute,
        CancellationToken cancellationToken)
    {
        // The creator leads the review while they can still work in the project; an API token is never the lead
        if (contribute && actor is { ViaApiToken: false } && InboxRules.SameUser(candidate.CreatedBy, user.Username))
        {
            return true;
        }

        return await BallotVisibility.SeesAllInProjectAsync(_access, actor, candidate.ProjectId, cancellationToken);
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
        var conclusion = ReviewLock.ConcludedOutcome(record, slots, signatures);
        var concluded = conclusion != ReviewConclusion.None;
        var lastSeenCommentId = review.State?.LastSeenCommentId ?? 0;
        var lastSeenAt = review.State?.LastSeenAt ?? DateTimeOffset.MinValue;
        var username = review.User.Username;
        var isAuthor = InboxRules.SameUser(review.Candidate.CreatedBy, username);
        var mine = slots.Where(slot => InboxRules.IsAssigned(slot, review.User.Id)).ToList();
        var overdue = !concluded && review.Candidate.DueDate is { } due && due < review.Today;

        // The threads the user may know of; a discussion about a check stays hidden from a user who may not see the
        // other reviewers' answers yet, so neither its count nor its replies show
        var threads = review.Hidden ? record.Threads.Where(thread => !thread.IsCheckThread).ToList() : record.Threads;

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
            // The ballot, the disputes, the signature and the waiting of a reviewer: one standing decides which row
            // the review gets, so it is never "To answer" and "Ready to sign" at once
            await AddStandingItemAsync(
                items,
                review,
                new StandingFacts(mine, signatures, digest, lastSeenCommentId, lastSeenAt),
                Row,
                cancellationToken);

            // Replies waiting: someone answered in a thread of mine, or on my review
            if (review.Contribute)
            {
                var waiting = InboxRules.WaitingThreads(threads, username, isAuthor);
                if (waiting.Count > 0)
                {
                    var unread = InboxRules.CountUnread(waiting, username, lastSeenCommentId) > 0;
                    var counts = ThreadCounts.From(waiting);
                    items.Add(Row(
                        InboxSection.RepliesWaiting,
                        $"{waiting.Count} waiting",
                        unread,
                        $"Waiting for your reply: {counts.OpenText}."));
                }
            }

            // Failed checks: my own review has checks the reviewers agreed on as Fail (disputed checks are a section of
            // their own); if that stands, the review is rejected and its reasons must be written down
            if (isAuthor && !review.Hidden)
            {
                var failed = InboxRules.FailedAnswers(record, slots.Count);
                if (failed.Count > 0)
                {
                    var unread = failed.Any(
                        answer => !InboxRules.SameUser(answer.AnsweredByUsername, username) && answer.AnsweredAt > lastSeenAt);
                    items.Add(Row(
                        InboxSection.FailedChecks,
                        $"{failed.Count} failed",
                        unread,
                        $"Reviewers marked {failed.Count} {(failed.Count == 1 ? "check" : "checks")} as Fail so far. "
                        + "If that stands, the review will be rejected and its reasons must be written down."));
                }
            }
        }

        if (mine.Count > 0 || isAuthor)
        {
            await AddUpdatesAsync(items, review, Row, new ApprovalFacts(conclusion, digest, signatures, lastSeenAt), cancellationToken);
        }
    }

    /// <summary>
    ///     Adds the row that says where the user stands in a review that is not concluded: to answer, disputed, ready
    ///     to sign, or waiting for others; or no row when nothing is asked of the user.
    /// </summary>
    /// <remarks>
    ///     The decision is <see cref="InboxRules.ReviewerStanding"/>. The ballot of the user is the one of the slot with
    ///     the lowest identity the user holds, as the checklist page picks it. Disputes are only counted for a user who
    ///     may see the other ballots, so a hidden user never reaches the count and no dispute can leak. Who is waited
    ///     for is progress, not a verdict, and is named for every user.
    /// </remarks>
    /// <param name="items">The rows collected so far.</param>
    /// <param name="review">What is known of the review.</param>
    /// <param name="facts">The slots and signatures the standing is decided from.</param>
    /// <param name="row">Creates a row of the review.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the row is added.</returns>
    private async Task AddStandingItemAsync(
        List<InboxItem> items,
        ReviewContext review,
        StandingFacts facts,
        Func<InboxSection, string, bool, string?, InboxItem> row,
        CancellationToken cancellationToken)
    {
        var (record, slots, _) = review.Snapshot;
        var mine = facts.Mine;
        var holdsSlot = mine.Count > 0;

        // A slot holder must be a member who may contribute; the author of a review without a slot only has to
        // contribute
        if (!review.Contribute || (holdsSlot && !review.Member))
        {
            return;
        }

        var hasChecklist = record.Checklist is not null;
        var ownSlot = ReviewActorRoles.FindOwnSlot(review.User.Id, slots);
        var ownBallot = ownSlot is null ? null : record.Ballots.FirstOrDefault(ballot => ballot.SlotId == ownSlot.Id);
        var unsigned = mine
            .Select(slot => InboxRules.SlotState(slot, facts.Signatures, facts.Digest))
            .Where(state => state != SlotSignatureState.Signed)
            .ToList();

        // The disputes are only counted for a user who may see them, so a hidden user never reaches the count
        var disputed = hasChecklist && !review.Hidden
            ? BallotProjection.Consolidate(record, slots.Count)?.Result.Disputed ?? 0
            : 0;
        var standing = InboxRules.ReviewerStanding(
            false,
            holdsSlot,
            hasChecklist,
            review.AuthorBlocked,
            ownBallot?.State ?? BallotState.NotStarted,
            review.Hidden,
            disputed,
            unsigned.Count > 0,
            unsigned.Count > 0 && SigningService.CanSignAfter(record, slots.Count, review.Hidden));
        switch (standing)
        {
            case InboxStanding.ToAnswer:
                // Nothing about the other reviewers is used here, so the row is the same whatever they answered
                var progress = BallotProjection.ForBallot(record.Checklist!.Parse(), ownSlot!.Id, ownBallot).Overall;
                items.Add(row(
                    InboxSection.ToAnswer,
                    ownBallot is null ? NotStartedChip : DraftChip,
                    review.State is null,
                    InboxRules.ToAnswerText(progress)));
                break;

            case InboxStanding.Disputed:
                var otherSubmitted = record.Ballots.Any(ballot =>
                    ballot.Status == BallotStatus.Submitted
                    && ballot.SubmittedAt > facts.LastSeenAt
                    && !InboxRules.SameUser(ballot.SubmittedByUsername, review.User.Username));
                items.Add(row(
                    InboxSection.Disputed,
                    $"{disputed} disputed",
                    review.State is null || otherSubmitted,
                    holdsSlot
                        ? "Change your answer or discuss it."
                        : "The reviewers answered differently. Discuss it with them."));
                break;

            case InboxStanding.ToSign:
                var chip = unsigned.Contains(SlotSignatureState.NotSigned) ? NotSignedChip : SignAgainChip;
                var unread = await IsToSignUnreadAsync(review, facts.LastSeenCommentId, facts.LastSeenAt, cancellationToken);
                items.Add(row(InboxSection.ToSign, chip, unread, null));
                break;

            case InboxStanding.Waiting:
                await AddWaitingItemAsync(items, review, facts, row, cancellationToken);
                break;
        }
    }

    /// <summary>
    ///     Adds the "Waiting for others" row: the user has done their part and the review waits for other people or
    ///     for open items.
    /// </summary>
    /// <param name="items">The rows collected so far.</param>
    /// <param name="review">What is known of the review.</param>
    /// <param name="facts">The slots and signatures the standing is decided from.</param>
    /// <param name="row">Creates a row of the review.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the row is added.</returns>
    private static async Task AddWaitingItemAsync(
        List<InboxItem> items,
        ReviewContext review,
        StandingFacts facts,
        Func<InboxSection, string, bool, string?, InboxItem> row,
        CancellationToken cancellationToken)
    {
        var (record, slots, _) = review.Snapshot;

        // The other slots that have no current signature, named by their primary and, when another person, alternate
        var others = slots
            .Where(slot => !facts.Mine.Contains(slot)
                           && InboxRules.SlotState(slot, facts.Signatures, facts.Digest) != SlotSignatureState.Signed)
            .ToList();
        var chip = InboxRules.WaitingChip(others.Count);
        string detail;
        if (others.Count > 0)
        {
            var users = await review.Names.GetAsync(cancellationToken);
            var people = new List<string>();
            foreach (var slot in others)
            {
                foreach (var userId in new[] { slot.PrimaryUserId, slot.AlternateUserId ?? slot.PrimaryUserId }.Distinct())
                {
                    if (users.TryGetValue(userId, out var name) && !people.Contains(name))
                    {
                        people.Add(name);
                    }
                }
            }

            detail = InboxRules.WaitingForText(people);
        }
        else if (record.Checklist is null)
        {
            detail = "This review has no checklist yet.";
        }
        else
        {
            // What is left is known to the user: only a user who handed in the ballot waits, and they see everything
            var open = ThreadCounts.From(
                review.Hidden ? record.Threads.Where(thread => !thread.IsCheckThread) : record.Threads);
            detail = open.Open > 0
                ? $"Open items remain: {open.OpenText}."
                : "Nothing is left for you to do now.";
        }

        items.Add(row(InboxSection.WaitingForOthers, chip, false, detail));
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
            || InboxRules.CountUnread(
                   review.Hidden ? record.Threads.Where(thread => !thread.IsCheckThread).ToList() : record.Threads,
                   username,
                   lastSeenCommentId) > 0)
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
        if (facts.Conclusion != ReviewConclusion.None)
        {
            // A signature of my own that completes the conclusion is not news to me
            var completing = InboxRules.CompletingSignature(facts.Signatures, facts.Digest);
            if (completing is not null && completing.UserId != review.User.Id && completing.SignedAt > facts.LastSeenAt)
            {
                var chip = facts.Conclusion == ReviewConclusion.Rejected ? RejectedChip : ApprovedChip;
                items.Add(row(InboxSection.Updates, chip, true, $"Signed by {completing.DisplayName}."));
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
    /// <param name="Hidden">
    ///     Whether the user may not see the answers of the other reviewers yet, so nothing about the threads of the
    ///     checks may show (see <see cref="BallotVisibility"/>).
    /// </param>
    /// <param name="AuthorBlocked">
    ///     Whether the user created the review and the project does not let the author answer its checklist.
    /// </param>
    /// <param name="Names">The display names of the users, read once and only when a row needs them.</param>
    private sealed record ReviewContext(
        User User,
        string ProjectName,
        InboxCandidate Candidate,
        ReviewSnapshot Snapshot,
        ReviewUserState? State,
        DateOnly Today,
        bool Contribute,
        bool Member,
        bool Hidden,
        bool AuthorBlocked,
        UserNames Names);

    /// <summary>
    ///     What the standing of the user in a review is decided from.
    /// </summary>
    /// <param name="Mine">The slots the user holds as primary or alternate reviewer.</param>
    /// <param name="Signatures">The signatures of the review.</param>
    /// <param name="Digest">The digest of the current record.</param>
    /// <param name="LastSeenCommentId">The highest comment identity the user has seen; zero when none.</param>
    /// <param name="LastSeenAt">The time the user last saw the review; the minimum value when never.</param>
    private sealed record StandingFacts(
        IReadOnlyList<ReviewerSlot> Mine,
        IReadOnlyList<ReviewSignature> Signatures,
        string Digest,
        int LastSeenCommentId,
        DateTimeOffset LastSeenAt);

    /// <summary>
    ///     The display names of the users, read from the store once for one inbox and only when a row needs a name.
    /// </summary>
    /// <remarks>
    ///     An instance lives for one call of <see cref="GetInboxAsync"/> and is not shared between calls or threads.
    /// </remarks>
    private sealed class UserNames
    {
        /// <summary>
        ///     The store of the users.
        /// </summary>
        private readonly IUserStore _users;

        /// <summary>
        ///     The names by user identity once read; <c>null</c> before.
        /// </summary>
        private Dictionary<int, string>? _names;

        /// <summary>
        ///     Initializes a new instance of the <see cref="UserNames"/> class.
        /// </summary>
        /// <param name="users">The store of the users.</param>
        public UserNames(IUserStore users)
        {
            _users = users;
        }

        /// <summary>
        ///     Gets the display names of all users by identity.
        /// </summary>
        /// <param name="cancellationToken">Cancels the operation.</param>
        /// <returns>The names; read from the store on the first call only.</returns>
        public async Task<IReadOnlyDictionary<int, string>> GetAsync(CancellationToken cancellationToken)
        {
            _names ??= (await _users.ListAsync(cancellationToken)).ToDictionary(user => user.Id, user => user.DisplayName);
            return _names;
        }
    }

    /// <summary>
    ///     The approval facts of one review that the information rows need.
    /// </summary>
    /// <param name="Conclusion">How the review is concluded; <see cref="ReviewConclusion.None"/> while it is in review.</param>
    /// <param name="Digest">The digest of the current record.</param>
    /// <param name="Signatures">The signatures of the review.</param>
    /// <param name="LastSeenAt">The time the user last saw the review; the minimum value when never.</param>
    private sealed record ApprovalFacts(
        ReviewConclusion Conclusion,
        string Digest,
        IReadOnlyList<ReviewSignature> Signatures,
        DateTimeOffset LastSeenAt);
}
