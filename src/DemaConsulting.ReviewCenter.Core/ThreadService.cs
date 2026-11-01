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
///     with one comment; anyone can reply. A comment is chat unless it is included in the signed report
///     (<see cref="ReviewComment.IncludeInReport"/>): a thread whose starting comment is included is a
///     <see cref="ThreadKind.Finding"/>, any other a <see cref="ThreadKind.Question"/>. Only included comments are part
///     of the record digest, so chat never makes a signature out of date; a thread can be promoted to a Finding or
///     demoted to a Question, and a reply can be included or taken out, by the people <see cref="ThreadKindRules"/>
///     names. A Finding can only be resolved with a note, stored as a <see cref="ReviewCommentKind.Resolution"/> comment
///     that follows the kind of its thread. Comments are
///     immutable in what they say, so the service offers no way to edit or delete one; the other changes to an existing
///     thread are its <see cref="ReviewThreadState"/> and which comments the report includes. The author of a comment is the signed-in
///     <see cref="Actor"/>, never free text, so it cannot be spoofed by a form field.
///     Every change is refused with <see cref="DomainRule.ReviewLocked"/> while the review is concluded (see
///     <see cref="ReviewLock"/>), because the report and the discussion are what the signatures were given on.
///     A change that would alter the digest and so make a current signature out of date is refused with
///     <see cref="DomainRule.SignaturesWouldBecomeOutOfDate"/> unless the caller acknowledges the affected slots; a
///     change that leaves the digest alone (chat, the state of a thread, a note on a Question) needs none. The checks
///     of every change run in one order: input, the right to contribute, the lock, the thread, the person's
///     relation to it, the rules, the acknowledgement, the store.
///     Two pure static helpers shape threads for display (<see cref="BuildIndex"/>, <see cref="NextStep"/>).
///     A thread is anchored to a file or, through <see cref="ThreadAnchor.ForCheck"/>, to one check of the frozen
///     checklist; who may start the second kind is decided by <see cref="CheckThreadRules"/>, and replying, resolving
///     and reopening work the same for both kinds.
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
    ///     Resolves a user name to a user, to find out whether a person holds a reviewer slot.
    /// </summary>
    private readonly IUserStore _users;

    /// <summary>
    ///     The clock that supplies creation times.
    /// </summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>
    ///     Initializes a new instance of the <see cref="ThreadService"/> class.
    /// </summary>
    /// <param name="store">The store that persists threads. Must not be null.</param>
    /// <param name="access">The access rules. Must not be null.</param>
    /// <param name="users">The user store used to find the reviewer slots of a person. Must not be null.</param>
    /// <param name="timeProvider">The clock used for creation times; injected so tests control time. Must not be null.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    public ThreadService(IReviewStore store, IProjectAccess access, IUserStore users, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(access);
        ArgumentNullException.ThrowIfNull(users);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _store = store;
        _access = access;
        _users = users;
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
    ///     <see cref="ThreadSide.Baseline"/>). A check anchor (<see cref="ThreadAnchor.ForCheck"/>) names one check of
    ///     the frozen checklist instead of a file and carries no file, lines or baseline side.
    ///     <see cref="ThreadSide.Baseline"/> needs a repository review with a
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
    /// <param name="acknowledgedSlotIds">
    ///     The identities of the reviewer slots whose signatures the person agreed to make out of date, or
    ///     <c>null</c> for none. Only needed when <paramref name="includeInReport"/> is set and a signature is
    ///     current: a chat thread leaves the digest alone.
    /// </param>
    /// <param name="includeInReport">
    ///     <c>true</c> to include the starting comment in the signed report, which makes the thread a
    ///     <see cref="ThreadKind.Finding"/>; <c>false</c> (the default) for chat, a <see cref="ThreadKind.Question"/>.
    /// </param>
    /// <returns>The stored thread in the <see cref="ReviewThreadState.Open"/> state, with its first comment.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="anchor"/> or <paramref name="actor"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the side of <paramref name="anchor"/> is not a defined value.</exception>
    /// <exception cref="DomainRuleException">
    ///     Thrown when a rule is violated (see <see cref="DomainRule"/>), among them
    ///     <see cref="DomainRule.ReviewLocked"/> when the review is concluded and
    ///     <see cref="DomainRule.SignaturesWouldBecomeOutOfDate"/> when the change alters the digest and a current
    ///     signature is not acknowledged; nothing is stored then. For a check anchor also
    ///     <see cref="DomainRule.ThreadAnchorInvalid"/> when the anchor carries a file, lines or the baseline side,
    ///     <see cref="DomainRule.ChecklistNotFound"/> or <see cref="DomainRule.ChecklistItemNotFound"/> when the review
    ///     has no checklist or the check is not in it, and <see cref="DomainRule.ActorNotAllowed"/> when the actor
    ///     is not allowed by <see cref="CheckThreadRules"/>.
    /// </exception>
    public async Task<ReviewThread> StartThreadAsync(
        int projectId,
        string reviewId,
        ThreadAnchor anchor,
        Actor actor,
        string? text,
        CancellationToken cancellationToken = default,
        IReadOnlyCollection<int>? acknowledgedSlotIds = null,
        bool includeInReport = false)
    {
        // Validate everything that needs no I/O first so a bad request fails fast and cheaply
        ArgumentNullException.ThrowIfNull(anchor);
        ArgumentNullException.ThrowIfNull(actor);
        await _access.RequireContributeAsync(actor, projectId, cancellationToken);
        var (filePath, startLine, endLine, side, checkItemId) = anchor;
        if (!Enum.IsDefined(side))
        {
            throw new ArgumentOutOfRangeException(nameof(anchor), side, "The side of the anchor is not a known value.");
        }

        var trimmedText = ValidateText(text);
        if (checkItemId is not null)
        {
            ValidateCheckAnchorShape(filePath, startLine, endLine, side, checkItemId);
        }
        else
        {
            ValidateAnchorShape(startLine, endLine);
        }

        // A whole-file anchor has no line numbers, so it has no side to choose: it is always stored as Head
        if (checkItemId is null && startLine is null && side == ThreadSide.Baseline)
        {
            throw new DomainRuleException(
                DomainRule.ThreadAnchorInvalid,
                "A whole-file comment has no baseline side; comment on a baseline line instead.");
        }

        // A concluded review is locked: nothing a signature covers may change until an administrator reopens it. The
        // lock is checked before the store is asked about the file, so a concluded review reports the lock whatever
        // the anchor says; a review that does not exist has no snapshot and is reported below as before
        var snapshot = await ReviewLock.EnsureUnlockedAsync(_store, projectId, reviewId, cancellationToken);

        // The file must exist in the review; the metadata read does not load any file bytes. A check anchor has no
        // file: its check must exist in the frozen checklist and the actor must be one who may start it
        var review = await _store.GetReviewAsync(projectId, reviewId, cancellationToken);
        if (checkItemId is not null)
        {
            await RequireCheckThreadStartAsync(actor, snapshot, review, checkItemId, cancellationToken);
        }
        else
        {
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
        }

        // Only a comment included in the report alters the digest; the acknowledgement is asked for once the request
        // is valid, from the record as the new thread would leave it
        var now = _timeProvider.GetUtcNow();
        var thread = new ReviewThread
        {
            ProjectId = projectId,
            ReviewId = reviewId,
            FilePath = filePath,
            CheckItemId = checkItemId,
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
                    Created = now,
                    IncludeInReport = includeInReport
                }
            ]
        };
        SignatureImpact.RequireAcknowledged(
            snapshot,
            snapshot is null ? null : snapshot.Record with { Threads = [.. snapshot.Record.Threads, thread] },
            acknowledgedSlotIds);

        return await _store.AddThreadAsync(
            thread,
            stored =>
            {
                var first = stored.Comments[0];

                // The comment text itself is never audited; its short digest still identifies it
                var inReport = first.IncludeInReport ? "; in report" : string.Empty;
                return AuditDraft.By(
                    actor,
                    "thread.started",
                    $"thread {stored.Id} on {AnchorText(stored)}; comment {first.Id}, text sha256 {AuditText.ShortHash(first.Text)}{inReport}",
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
    /// <param name="acknowledgedSlotIds">
    ///     The identities of the reviewer slots whose signatures the person agreed to make out of date, or
    ///     <c>null</c> for none. Only needed when <paramref name="includeInReport"/> is set and a signature is
    ///     current: a chat reply leaves the digest alone.
    /// </param>
    /// <param name="includeInReport">
    ///     <c>true</c> to include the reply in the signed report (a reply of a Question may be included on its own);
    ///     <c>false</c> (the default) for chat.
    /// </param>
    /// <returns>
    ///     The stored comment. Earlier comments are untouched and the thread state does not change, so a
    ///     resolved thread can still receive replies.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="actor"/> is null.</exception>
    /// <exception cref="DomainRuleException">
    ///     Thrown when a rule is violated (see <see cref="DomainRule"/>), among them
    ///     <see cref="DomainRule.ReviewLocked"/> when the review is concluded and
    ///     <see cref="DomainRule.SignaturesWouldBecomeOutOfDate"/> when the change alters the digest and a current
    ///     signature is not acknowledged; nothing is stored then.
    /// </exception>
    public async Task<ReviewComment> ReplyAsync(
        int projectId,
        string reviewId,
        int threadId,
        Actor actor,
        string? text,
        CancellationToken cancellationToken = default,
        IReadOnlyCollection<int>? acknowledgedSlotIds = null,
        bool includeInReport = false)
    {
        ArgumentNullException.ThrowIfNull(actor);
        await _access.RequireContributeAsync(actor, projectId, cancellationToken);
        var comment = new ReviewComment
        {
            Author = actor.DisplayName,
            AuthorUsername = actor.Username,
            Text = ValidateText(text),
            Created = _timeProvider.GetUtcNow(),
            IncludeInReport = includeInReport
        };

        // A concluded review is locked: replies, like every other change to a thread, wait for a reopen
        var snapshot = await ReviewLock.EnsureUnlockedAsync(_store, projectId, reviewId, cancellationToken);

        // Only a reply included in the report alters the digest; a thread that does not exist is reported by the store
        // as before
        var existing = snapshot?.Record.Threads.FirstOrDefault(t => t.Id == threadId);
        if (snapshot is not null && existing is not null)
        {
            SignatureImpact.RequireAcknowledged(
                snapshot,
                WithThread(snapshot.Record, CopyThread(existing, existing.State, [.. existing.Comments, comment])),
                acknowledgedSlotIds);
        }

        return await _store.AddCommentAsync(
                   projectId,
                   reviewId,
                   threadId,
                   comment,
                   stored => AuditDraft.By(
                       actor,
                       "thread.replied",
                       $"thread {threadId}, comment {stored.Id}, text sha256 {AuditText.ShortHash(stored.Text)}" +
                       (stored.IncludeInReport ? "; in report" : string.Empty),
                       projectId,
                       reviewId),
                   cancellationToken)
               ?? throw ThreadNotFound(threadId);
    }

    /// <summary>
    ///     Marks a thread resolved, with a note that is required for a Finding.
    /// </summary>
    /// <param name="projectId">The identity of the owning project.</param>
    /// <param name="reviewId">The review identifier.</param>
    /// <param name="threadId">The identity of the thread; it must belong to the given review.</param>
    /// <param name="actor">The signed-in user who resolves the thread; recorded in the audit trail. Must not be null.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <param name="acknowledgedSlotIds">
    ///     The identities of the reviewer slots whose signatures the person agreed to make out of date, or
    ///     <c>null</c> for none. Only needed when the thread is a Finding, it is open and a signature is current: the
    ///     resolution note then enters the report.
    /// </param>
    /// <param name="resolutionNote">
    ///     The note that says what was done, or <c>null</c> or white space for none; trimmed, at most
    ///     <see cref="ReviewLimits.MaxCommentLength"/> characters. Required for a Finding. It is stored in the same
    ///     transaction as a <see cref="ReviewCommentKind.Resolution"/> comment, included in the report exactly when the
    ///     thread is a Finding.
    /// </param>
    /// <returns>
    ///     A task that completes when the state is stored; resolving a resolved thread does nothing (the note is
    ///     ignored, nothing is audited).
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="actor"/> is null.</exception>
    /// <exception cref="DomainRuleException">
    ///     Thrown with <see cref="DomainRule.ThreadNotFound"/> when the thread does not exist in the review,
    ///     <see cref="DomainRule.ResolutionNoteRequired"/> when a Finding has no note,
    ///     <see cref="DomainRule.CommentTextInvalid"/> when the note is too long,
    ///     <see cref="DomainRule.ReviewLocked"/> when the review is concluded,
    ///     <see cref="DomainRule.RecordChanged"/> when the thread was promoted or demoted between the check of this
    ///     method and the write (the note and the acknowledgement were decided for the other kind; nothing is stored,
    ///     nothing is audited), or with
    ///     <see cref="DomainRule.SignaturesWouldBecomeOutOfDate"/> when the change alters the digest and a current
    ///     signature is not acknowledged.
    /// </exception>
    public async Task ResolveAsync(
        int projectId,
        string reviewId,
        int threadId,
        Actor actor,
        CancellationToken cancellationToken = default,
        IReadOnlyCollection<int>? acknowledgedSlotIds = null,
        string? resolutionNote = null)
    {
        ArgumentNullException.ThrowIfNull(actor);

        // A blank note is no note; a given note follows the rules of every comment text
        var note = string.IsNullOrWhiteSpace(resolutionNote) ? null : ValidateText(resolutionNote);
        await _access.RequireContributeAsync(actor, projectId, cancellationToken);

        // A concluded review is locked: a thread can neither be resolved nor reopened until the review is reopened
        var snapshot = await ReviewLock.EnsureUnlockedAsync(_store, projectId, reviewId, cancellationToken);
        var thread = snapshot?.Record.Threads.FirstOrDefault(t => t.Id == threadId)
                     ?? throw ThreadNotFound(threadId);

        // Resolving a resolved thread is a no-op: no note, no acknowledgement, no audit entry
        if (thread.State == ReviewThreadState.Resolved)
        {
            return;
        }

        // The resolution of a Finding is part of the signed report, so it must say what was done
        var isFinding = thread.Kind == ThreadKind.Finding;
        if (isFinding && note is null)
        {
            throw new DomainRuleException(
                DomainRule.ResolutionNoteRequired,
                "A finding can only be resolved with a note that says what was done.");
        }

        var resolution = note is null
            ? null
            : new ReviewComment
            {
                Author = actor.DisplayName,
                AuthorUsername = actor.Username,
                Text = note,
                Created = _timeProvider.GetUtcNow(),
                IncludeInReport = isFinding,
                Kind = ReviewCommentKind.Resolution
            };

        // The note of a Finding changes the report; a Question's note and the state itself do not
        SignatureImpact.RequireAcknowledged(
            snapshot,
            WithThread(
                snapshot.Record,
                CopyThread(
                    thread,
                    ReviewThreadState.Resolved,
                    resolution is null ? thread.Comments : [.. thread.Comments, resolution])),
            acknowledgedSlotIds);

        // The store re-reads the kind inside its transaction, refuses with RecordChanged when it is no longer the one the
        // note, the acknowledgement and the flag above were decided for, and saves the entry only when it resolved the thread
        var detail = note is null ? $"thread {threadId}" : $"thread {threadId}; note sha256 {AuditText.ShortHash(note)}";
        var audit = AuditDraft.By(actor, "thread.resolved", detail, projectId, reviewId);
        await _store.ResolveThreadAsync(projectId, reviewId, threadId, thread.Kind, resolution, audit, cancellationToken);
    }

    /// <summary>
    ///     Marks a thread open again.
    /// </summary>
    /// <param name="projectId">The identity of the owning project.</param>
    /// <param name="reviewId">The review identifier.</param>
    /// <param name="threadId">The identity of the thread; it must belong to the given review.</param>
    /// <param name="actor">The signed-in user who reopens the thread; recorded in the audit trail. Must not be null.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <param name="acknowledgedSlotIds">
    ///     The identities of the reviewer slots whose signatures the person agreed to make out of date, or
    ///     <c>null</c> for none. The state is not part of the digest, so reopening never needs one; the parameter
    ///     stays so callers do not change.
    /// </param>
    /// <returns>
    ///     A task that completes when the state is stored; reopening an open thread does nothing. Earlier resolution
    ///     notes stay as history.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="actor"/> is null.</exception>
    /// <exception cref="DomainRuleException">
    ///     Thrown with <see cref="DomainRule.ThreadNotFound"/> when the thread does not exist in the review,
    ///     <see cref="DomainRule.ReviewLocked"/> when the review is concluded, or with
    ///     <see cref="DomainRule.SignaturesWouldBecomeOutOfDate"/> when a current signature is not acknowledged and
    ///     the change somehow alters the digest.
    /// </exception>
    public async Task ReopenAsync(
        int projectId,
        string reviewId,
        int threadId,
        Actor actor,
        CancellationToken cancellationToken = default,
        IReadOnlyCollection<int>? acknowledgedSlotIds = null)
    {
        ArgumentNullException.ThrowIfNull(actor);
        await _access.RequireContributeAsync(actor, projectId, cancellationToken);

        // A concluded review is locked: a thread can neither be resolved nor reopened until the review is reopened
        var snapshot = await ReviewLock.EnsureUnlockedAsync(_store, projectId, reviewId, cancellationToken);

        // Only a real change of state is looked at; a missing thread is reported by the store as before
        var existing = snapshot?.Record.Threads.FirstOrDefault(t => t.Id == threadId);
        if (snapshot is not null && existing is not null && existing.State != ReviewThreadState.Open)
        {
            SignatureImpact.RequireAcknowledged(
                snapshot,
                WithThread(snapshot.Record, CopyThread(existing, ReviewThreadState.Open, existing.Comments)),
                acknowledgedSlotIds);
        }

        // The store saves the entry only when the thread exists, so a refused call leaves no trace
        var audit = AuditDraft.By(actor, "thread.reopened", $"thread {threadId}", projectId, reviewId);
        if (!await _store.ReopenThreadAsync(projectId, reviewId, threadId, audit, cancellationToken))
        {
            throw ThreadNotFound(threadId);
        }
    }

    /// <summary>
    ///     Promotes a thread to a Finding: its starting comment (and its resolution notes) become part of the signed
    ///     report.
    /// </summary>
    /// <param name="projectId">The identity of the owning project.</param>
    /// <param name="reviewId">The review identifier.</param>
    /// <param name="threadId">The identity of the thread; it must belong to the given review.</param>
    /// <param name="actor">
    ///     The signed-in user: the person who started the thread, a reviewer (primary or alternate) of any slot of the
    ///     review, or the lead of the review. Must not be null.
    /// </param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <param name="acknowledgedSlotIds">
    ///     The identities of the reviewer slots whose signatures the person agreed to make out of date, or
    ///     <c>null</c> for none. Needed when a signature is current: the digest changes.
    /// </param>
    /// <returns>A task that completes when the change is stored; promoting a Finding does nothing.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="actor"/> is null.</exception>
    /// <exception cref="DomainRuleException">
    ///     Thrown with <see cref="DomainRule.ThreadNotFound"/>, <see cref="DomainRule.ActorNotAllowed"/> (also for a
    ///     read-only user and an auditor), <see cref="DomainRule.ReviewLocked"/> or
    ///     <see cref="DomainRule.SignaturesWouldBecomeOutOfDate"/>; nothing is stored then.
    /// </exception>
    public Task PromoteAsync(
        int projectId,
        string reviewId,
        int threadId,
        Actor actor,
        CancellationToken cancellationToken = default,
        IReadOnlyCollection<int>? acknowledgedSlotIds = null)
    {
        return SetKindAsync(projectId, reviewId, threadId, ThreadKind.Finding, actor, acknowledgedSlotIds, cancellationToken);
    }

    /// <summary>
    ///     Demotes a thread to a Question: its starting comment (and its resolution notes) leave the signed report.
    /// </summary>
    /// <param name="projectId">The identity of the owning project.</param>
    /// <param name="reviewId">The review identifier.</param>
    /// <param name="threadId">The identity of the thread; it must belong to the given review.</param>
    /// <param name="actor">
    ///     The signed-in user: the person who started the thread or the lead of the review. A reviewer who did not
    ///     start the thread may promote it but not demote it. Must not be null.
    /// </param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <param name="acknowledgedSlotIds">
    ///     The identities of the reviewer slots whose signatures the person agreed to make out of date, or
    ///     <c>null</c> for none. Needed when a signature is current: the digest changes.
    /// </param>
    /// <returns>A task that completes when the change is stored; demoting a Question does nothing. Replies keep their own flags.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="actor"/> is null.</exception>
    /// <exception cref="DomainRuleException">
    ///     Thrown with <see cref="DomainRule.ThreadNotFound"/>, <see cref="DomainRule.ActorNotAllowed"/>,
    ///     <see cref="DomainRule.ReviewLocked"/> or <see cref="DomainRule.SignaturesWouldBecomeOutOfDate"/>; nothing is
    ///     stored then.
    /// </exception>
    public Task DemoteAsync(
        int projectId,
        string reviewId,
        int threadId,
        Actor actor,
        CancellationToken cancellationToken = default,
        IReadOnlyCollection<int>? acknowledgedSlotIds = null)
    {
        return SetKindAsync(projectId, reviewId, threadId, ThreadKind.Question, actor, acknowledgedSlotIds, cancellationToken);
    }

    /// <summary>
    ///     Includes a reply in the signed report or takes it out again.
    /// </summary>
    /// <param name="projectId">The identity of the owning project.</param>
    /// <param name="reviewId">The review identifier.</param>
    /// <param name="threadId">The identity of the thread; it must belong to the given review.</param>
    /// <param name="commentId">The identity of the reply; it must belong to the thread.</param>
    /// <param name="include"><c>true</c> to include the reply in the report, <c>false</c> to take it out.</param>
    /// <param name="actor">
    ///     The signed-in user: the author of the reply or the lead of the review. Must not be null.
    /// </param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <param name="acknowledgedSlotIds">
    ///     The identities of the reviewer slots whose signatures the person agreed to make out of date, or
    ///     <c>null</c> for none. Needed when a signature is current: the digest changes.
    /// </param>
    /// <returns>A task that completes when the change is stored; setting a flag to its present value does nothing.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="actor"/> is null.</exception>
    /// <exception cref="DomainRuleException">
    ///     Thrown with <see cref="DomainRule.ThreadNotFound"/> (also for a comment that is not in the thread),
    ///     <see cref="DomainRule.ActorNotAllowed"/>, <see cref="DomainRule.CommentNotTogglable"/> for the starting
    ///     comment (promote or demote the thread instead) and for a resolution note (it follows the thread),
    ///     <see cref="DomainRule.ReviewLocked"/> or <see cref="DomainRule.SignaturesWouldBecomeOutOfDate"/>; nothing is
    ///     stored then.
    /// </exception>
    public async Task SetCommentIncludedAsync(
        int projectId,
        string reviewId,
        int threadId,
        int commentId,
        bool include,
        Actor actor,
        CancellationToken cancellationToken = default,
        IReadOnlyCollection<int>? acknowledgedSlotIds = null)
    {
        ArgumentNullException.ThrowIfNull(actor);
        await _access.RequireContributeAsync(actor, projectId, cancellationToken);
        var snapshot = await ReviewLock.EnsureUnlockedAsync(_store, projectId, reviewId, cancellationToken);
        var thread = snapshot?.Record.Threads.FirstOrDefault(t => t.Id == threadId)
                     ?? throw ThreadNotFound(threadId);
        var comment = thread.Comments.FirstOrDefault(c => c.Id == commentId)
                      ?? throw ThreadNotFound(threadId);

        // Only the author of the reply or the lead may decide that the reply is part of the report
        var roles = await RolesAsync(actor, snapshot, thread, comment, projectId, reviewId, cancellationToken);
        if (!ThreadKindRules.CanToggleReply(roles))
        {
            throw NotAllowed("Only the author of a reply or the lead of the review can include it in the report or take it out.");
        }

        if (!ThreadKindRules.IsToggleable(thread, comment))
        {
            throw new DomainRuleException(
                DomainRule.CommentNotTogglable,
                "Only a reply can be included on its own; promote or demote the thread to change its starting comment " +
                "and its resolution notes.");
        }

        // Setting the flag to the value it has is a no-op: no acknowledgement, no audit entry
        if (comment.IncludeInReport == include)
        {
            return;
        }

        SignatureImpact.RequireAcknowledged(
            snapshot,
            WithThread(
                snapshot.Record,
                CopyThread(
                    thread,
                    thread.State,
                    thread.Comments.Select(c => c.Id == commentId ? CopyComment(c, include) : c).ToList())),
            acknowledgedSlotIds);

        var audit = AuditDraft.By(
            actor,
            "thread.comment-report-changed",
            $"thread {threadId}, comment {commentId}: {(include ? "included in report" : "taken out of report")}",
            projectId,
            reviewId);
        await _store.SetCommentReportAsync(projectId, reviewId, threadId, commentId, include, audit, cancellationToken);
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
    ///     Lists the threads of a review as a given viewer may see them: the threads about a check are left out while
    ///     the viewer may not see the answers of the other reviewers yet (see <see cref="BallotVisibility"/>), because
    ///     such a thread, or only its number, could give away how another reviewer answered.
    /// </summary>
    /// <param name="viewer">
    ///     The person who looks; <c>null</c> is treated as a viewer who has handed in nothing, so check threads are left
    ///     out of a review that has to hide them (fail closed).
    /// </param>
    /// <param name="projectId">The project identity.</param>
    /// <param name="reviewId">The review identifier.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The visible threads in creation order; empty when there are none or the review does not exist.</returns>
    public async Task<IReadOnlyList<ReviewThread>> ListAsync(
        Actor? viewer,
        int projectId,
        string reviewId,
        CancellationToken cancellationToken = default)
    {
        var threads = await _store.ListThreadsAsync(projectId, reviewId, cancellationToken);

        // Nothing to hide without a check thread, so no role needs to be looked up
        if (!threads.Any(thread => thread.IsCheckThread))
        {
            return threads;
        }

        var snapshot = await _store.GetSnapshotAsync(projectId, reviewId, cancellationToken);
        var review = snapshot is null ? null : await _store.GetReviewAsync(projectId, reviewId, cancellationToken);
        if (snapshot is null || review is null)
        {
            // The review vanished between the two reads: nothing is known about who may see what, so fail closed
            return threads.Where(thread => !thread.IsCheckThread).ToList();
        }

        var seesAll = await BallotVisibility.SeesAllInReviewAsync(_access, viewer, review, cancellationToken);
        var user = viewer is null ? null : await _users.GetByUsernameAsync(viewer.Username, cancellationToken);
        var visible = BallotVisibility.CanSeeOthers(seesAll, snapshot, user?.Id);
        return visible ? threads : threads.Where(thread => !thread.IsCheckThread).ToList();
    }

    /// <summary>
    ///     Tells what a person may start in a review, so the page offers only what would be accepted.
    /// </summary>
    /// <param name="actor">The signed-in user; <c>null</c> may start nothing.</param>
    /// <param name="projectId">The project identity.</param>
    /// <param name="reviewId">The review identifier.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    ///     The capabilities; <see cref="ThreadCapabilities.None"/> for nobody, a person who cannot contribute, a review
    ///     that does not exist and a concluded review (it is locked).
    /// </returns>
    public async Task<ThreadCapabilities> GetCapabilitiesAsync(
        Actor? actor,
        int projectId,
        string reviewId,
        CancellationToken cancellationToken = default)
    {
        if (actor is null || !await _access.CanContributeAsync(actor, projectId, cancellationToken))
        {
            return ThreadCapabilities.None;
        }

        var snapshot = await _store.GetSnapshotAsync(projectId, reviewId, cancellationToken);
        var review = snapshot is null ? null : await _store.GetReviewAsync(projectId, reviewId, cancellationToken);
        if (snapshot is null || review is null
            || ReviewLock.IsConcluded(snapshot.Record, snapshot.Slots, snapshot.Signatures))
        {
            return ThreadCapabilities.None;
        }

        // The same facts the start of a check thread gathers; the rule alone decides
        var isSystemAdmin = await _access.IsSystemAdminAsync(actor, cancellationToken);
        var isLead = await ReviewActorRoles.IsLeadAsync(_access, actor, review, cancellationToken);
        var slot = await ReviewActorRoles.FindOwnSlotAsync(_users, actor, snapshot.Slots, cancellationToken);
        var ballot = slot is null ? null : snapshot.Record.Ballots.FirstOrDefault(b => b.SlotId == slot.Id);
        var canStartCheck = snapshot.Record.Checklist is not null
                            && CheckThreadRules.CanStart(isSystemAdmin, isLead, slot is not null, ballot?.Status, snapshot.Slots.Count);
        return new ThreadCapabilities(true, canStartCheck);
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
    ///     The number of checks answered Fail; named in the sentence so the reader knows the review will be rejected
    ///     (a Fail does not block signing by itself). Ignored without a checklist. Zero (the default) leaves the
    ///     wording as it was before signing existed.
    /// </param>
    /// <param name="disputedChecks">
    ///     The number of checks on which the submitted ballots disagree; each one blocks signing, so it is named in the
    ///     sentence and the checklist is not complete while one remains. Ignored without a checklist. Zero (the
    ///     default) leaves the wording unchanged.
    /// </param>
    /// <param name="conclusionRequired">
    ///     <c>true</c> when the review is to be rejected and has no conclusion text yet, so the sentence asks for it.
    ///     Ignored without a checklist. <c>false</c> (the default) leaves the wording unchanged.
    /// </param>
    /// <returns>
    ///     One short sentence: the number of unanswered checks, disputed checks, failed checks and open threads, the
    ///     need for the conclusion, that the checklist is complete, that all threads are resolved, or (without a
    ///     checklist and without threads) to start a discussion.
    /// </returns>
    public static string NextStep(
        IEnumerable<ReviewThread> threads,
        ChecklistProgress? checklist = null,
        int failedChecks = 0,
        int disputedChecks = 0,
        bool conclusionRequired = false)
    {
        ArgumentNullException.ThrowIfNull(threads);

        var all = threads.ToList();
        var open = all.Count(thread => thread.State == ReviewThreadState.Open);

        // Without a checklist the sentence depends on the threads alone
        if (checklist is null)
        {
            return ThreadsOnlyStep(all.Count, open);
        }

        // With a checklist the unanswered checks come first, then disputed and failed checks, then the open threads
        // and, last, the conclusion a rejected review needs
        var unanswered = checklist.Value.Unanswered;
        var parts = new List<string>();
        if (unanswered > 0)
        {
            parts.Add(Counted(unanswered, "checklist check unanswered", "checklist checks unanswered"));
        }

        if (disputedChecks > 0)
        {
            parts.Add(Counted(disputedChecks, "check disputed between the ballots", "checks disputed between the ballots"));
        }

        if (failedChecks > 0)
        {
            parts.Add(Counted(failedChecks, "check answered Fail", "checks answered Fail"));
        }

        if (open > 0)
        {
            parts.Add(Counted(open, "open thread to resolve", "open threads to resolve"));
        }

        if (conclusionRequired)
        {
            parts.Add("a conclusion stating the reasons is needed");
        }

        if (parts.Count > 0)
        {
            var sentence = string.Join(", ", parts);
            return unanswered > 0 || disputedChecks > 0 ? $"{sentence}." : $"Checklist complete; {sentence}.";
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
    ///     Promotes or demotes a thread: includes or excludes its starting comment and its resolution notes.
    /// </summary>
    /// <param name="projectId">The identity of the owning project.</param>
    /// <param name="reviewId">The review identifier.</param>
    /// <param name="threadId">The identity of the thread.</param>
    /// <param name="target">The kind the thread should have afterwards.</param>
    /// <param name="actor">The signed-in user; recorded in the audit trail.</param>
    /// <param name="acknowledgedSlotIds">The slot identities the person acknowledged, or <c>null</c> for none.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the change is stored or found to be unnecessary.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="actor"/> is null.</exception>
    /// <exception cref="DomainRuleException">
    ///     Thrown with <see cref="DomainRule.ThreadNotFound"/>, <see cref="DomainRule.ActorNotAllowed"/>,
    ///     <see cref="DomainRule.ReviewLocked"/> or <see cref="DomainRule.SignaturesWouldBecomeOutOfDate"/>.
    /// </exception>
    private async Task SetKindAsync(
        int projectId,
        string reviewId,
        int threadId,
        ThreadKind target,
        Actor actor,
        IReadOnlyCollection<int>? acknowledgedSlotIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        await _access.RequireContributeAsync(actor, projectId, cancellationToken);
        var snapshot = await ReviewLock.EnsureUnlockedAsync(_store, projectId, reviewId, cancellationToken);
        var thread = snapshot?.Record.Threads.FirstOrDefault(t => t.Id == threadId)
                     ?? throw ThreadNotFound(threadId);

        // Who may change the kind depends on the direction: a reviewer may promote, only the starter or lead may demote
        var roles = await RolesAsync(actor, snapshot, thread, null, projectId, reviewId, cancellationToken);
        var promoting = target == ThreadKind.Finding;
        var allowed = promoting ? ThreadKindRules.CanPromote(roles) : ThreadKindRules.CanDemote(roles);
        if (!allowed)
        {
            throw NotAllowed(promoting
                ? "Only the person who started the thread, a reviewer of the review or its lead can make a thread a finding."
                : "Only the person who started the thread or the lead of the review can make a finding a question.");
        }

        // A thread that already has the kind is a no-op: no acknowledgement, no audit entry
        var current = ThreadKindRules.KindOf(thread);
        if (current == target)
        {
            return;
        }

        var include = promoting;

        // A resolved thread becomes a Finding only when it already carries a resolution note: the note of a Finding is
        // part of the report, and the store refuses the same transition inside its transaction
        if (promoting
            && thread.State == ReviewThreadState.Resolved
            && !thread.Comments.Any(c => c.Kind == ReviewCommentKind.Resolution))
        {
            throw new DomainRuleException(
                DomainRule.ResolutionNoteRequired,
                "A resolved question without a resolution note cannot become a finding. " +
                "Reopen the thread and resolve it again with a note.");
        }

        SignatureImpact.RequireAcknowledged(
            snapshot,
            WithThread(snapshot.Record, CopyThread(thread, thread.State, WithKindFlags(thread, include))),
            acknowledgedSlotIds);

        var audit = AuditDraft.By(
            actor,
            "thread.kind-changed",
            $"thread {threadId}: {ThreadKindRules.KindText(current)} -> {ThreadKindRules.KindText(target)}",
            projectId,
            reviewId);
        await _store.SetThreadKindAsync(projectId, reviewId, threadId, target, audit, cancellationToken);
    }

    /// <summary>
    ///     Works out how a person is related to a thread and to its review, for the permission rules.
    /// </summary>
    /// <param name="actor">The signed-in user.</param>
    /// <param name="snapshot">The snapshot the thread was read from; its slots decide who is a reviewer.</param>
    /// <param name="thread">The thread.</param>
    /// <param name="comment">The comment in question, or <c>null</c> when the action is about the thread.</param>
    /// <param name="projectId">The identity of the owning project.</param>
    /// <param name="reviewId">The review identifier.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The relations that apply; <see cref="ThreadActorRole.None"/> for a plain contributor.</returns>
    private async Task<ThreadActorRole> RolesAsync(
        Actor actor,
        ReviewSnapshot snapshot,
        ReviewThread thread,
        ReviewComment? comment,
        int projectId,
        string reviewId,
        CancellationToken cancellationToken)
    {
        var roles = ThreadActorRole.None;
        if (thread.Comments.Count > 0 && SameUser(thread.Comments[0].AuthorUsername, actor.Username))
        {
            roles |= ThreadActorRole.Starter;
        }

        if (comment is not null && SameUser(comment.AuthorUsername, actor.Username))
        {
            roles |= ThreadActorRole.CommentAuthor;
        }

        if (await ReviewActorRoles.IsSlotOwnerAsync(_users, actor, snapshot.Slots, cancellationToken))
        {
            roles |= ThreadActorRole.SlotOwner;
        }

        var review = await _store.GetReviewAsync(projectId, reviewId, cancellationToken);
        if (review is not null && await ReviewActorRoles.IsLeadAsync(_access, actor, review, cancellationToken))
        {
            roles |= ThreadActorRole.Lead;
        }

        return roles;
    }

    /// <summary>
    ///     Compares two user names the way the user store does: without regard to case.
    /// </summary>
    /// <param name="left">The first user name.</param>
    /// <param name="right">The second user name.</param>
    /// <returns><c>true</c> when the names are equal without regard to case.</returns>
    private static bool SameUser(string left, string right)
    {
        return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     Builds the refusal for a person who may contribute but not do this.
    /// </summary>
    /// <param name="message">The operator-readable reason.</param>
    /// <returns>The exception to throw.</returns>
    private static DomainRuleException NotAllowed(string message)
    {
        return new DomainRuleException(DomainRule.ActorNotAllowed, message);
    }

    /// <summary>
    ///     Replaces one thread of a record, to ask what the digest would be after a change without storing anything.
    /// </summary>
    /// <param name="record">The record as it is now.</param>
    /// <param name="replacement">The changed thread; it replaces the thread with the same identity.</param>
    /// <returns>The record as the change would leave it.</returns>
    private static ReviewRecord WithThread(ReviewRecord record, ReviewThread replacement)
    {
        return record with
        {
            Threads = record.Threads.Select(t => t.Id == replacement.Id ? replacement : t).ToList()
        };
    }

    /// <summary>
    ///     Copies a thread with another state and other comments; the classes have no <c>with</c>.
    /// </summary>
    /// <param name="thread">The thread to copy.</param>
    /// <param name="state">The state of the copy.</param>
    /// <param name="comments">The comments of the copy, in posting order.</param>
    /// <returns>The copy, which is only used in memory to compute a digest.</returns>
    private static ReviewThread CopyThread(
        ReviewThread thread,
        ReviewThreadState state,
        IEnumerable<ReviewComment> comments)
    {
        return new ReviewThread
        {
            Id = thread.Id,
            ProjectId = thread.ProjectId,
            ReviewId = thread.ReviewId,
            FilePath = thread.FilePath,
            CheckItemId = thread.CheckItemId,
            Side = thread.Side,
            StartLine = thread.StartLine,
            EndLine = thread.EndLine,
            State = state,
            Created = thread.Created,
            CarriedFromReviewId = thread.CarriedFromReviewId,
            CarriedFromThreadId = thread.CarriedFromThreadId,
            Comments = [.. comments]
        };
    }

    /// <summary>
    ///     Copies a comment with another report flag.
    /// </summary>
    /// <param name="comment">The comment to copy.</param>
    /// <param name="include">The report flag of the copy.</param>
    /// <returns>The copy, which is only used in memory to compute a digest.</returns>
    private static ReviewComment CopyComment(ReviewComment comment, bool include)
    {
        return new ReviewComment
        {
            Id = comment.Id,
            ThreadId = comment.ThreadId,
            Author = comment.Author,
            AuthorUsername = comment.AuthorUsername,
            Text = comment.Text,
            Created = comment.Created,
            IncludeInReport = include,
            Kind = comment.Kind
        };
    }

    /// <summary>
    ///     Sets the report flag of the starting comment and of every resolution note of a thread, as promoting and
    ///     demoting do; replies keep their own flags.
    /// </summary>
    /// <param name="thread">The thread.</param>
    /// <param name="include">The flag to set.</param>
    /// <returns>The comments with the flags set.</returns>
    private static List<ReviewComment> WithKindFlags(ReviewThread thread, bool include)
    {
        return thread.Comments
            .Select((comment, index) =>
                index == 0 || comment.Kind == ReviewCommentKind.Resolution ? CopyComment(comment, include) : comment)
            .ToList();
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
    ///     Checks that a check anchor has the one shape the service accepts for a check.
    /// </summary>
    /// <param name="filePath">The path of the anchor; must be empty.</param>
    /// <param name="startLine">The first line of the anchor; must be <c>null</c>.</param>
    /// <param name="endLine">The last line of the anchor; must be <c>null</c>.</param>
    /// <param name="side">The side of the anchor; must be <see cref="ThreadSide.Head"/>.</param>
    /// <param name="checkItemId">The check identifier; must not be empty.</param>
    /// <exception cref="DomainRuleException">
    ///     Thrown with <see cref="DomainRule.ThreadAnchorInvalid"/> when the anchor mixes a file or lines with a check,
    ///     or names no check.
    /// </exception>
    private static void ValidateCheckAnchorShape(
        string filePath,
        int? startLine,
        int? endLine,
        ThreadSide side,
        string checkItemId)
    {
        if (checkItemId.Length == 0)
        {
            throw new DomainRuleException(DomainRule.ThreadAnchorInvalid, "A check discussion needs a check.");
        }

        // A thread is anchored to a file or to a check, never to both, and a check has no lines
        if (filePath.Length > 0 || startLine is not null || endLine is not null || side != ThreadSide.Head)
        {
            throw new DomainRuleException(
                DomainRule.ThreadAnchorInvalid,
                "A discussion about a check has no file, no lines and no baseline side.");
        }
    }

    /// <summary>
    ///     Checks that the review has the check and that the actor may start a discussion about it.
    /// </summary>
    /// <remarks>
    ///     The facts are gathered here (the frozen checklist, the roles of the actor and the status of their ballot) and
    ///     <see cref="CheckThreadRules"/> decides. Any check of the frozen checklist qualifies, not only a disputed one.
    /// </remarks>
    /// <param name="actor">The signed-in user.</param>
    /// <param name="snapshot">The snapshot of the review, or <c>null</c> when it does not exist.</param>
    /// <param name="review">The review, or <c>null</c> when it does not exist.</param>
    /// <param name="checkItemId">The check identifier.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <exception cref="DomainRuleException">
    ///     Thrown with <see cref="DomainRule.ReviewNotFound"/> when the review does not exist,
    ///     <see cref="DomainRule.ChecklistNotFound"/> when it has no checklist, <see cref="DomainRule.ChecklistItemNotFound"/>
    ///     when the checklist has no such check, and <see cref="DomainRule.ActorNotAllowed"/> when the person may not
    ///     start a check discussion.
    /// </exception>
    private async Task RequireCheckThreadStartAsync(
        Actor actor,
        ReviewSnapshot? snapshot,
        Review? review,
        string checkItemId,
        CancellationToken cancellationToken)
    {
        // The check must exist in the checklist the review was frozen with
        if (snapshot is null || review is null)
        {
            throw new DomainRuleException(DomainRule.ReviewNotFound, "The review does not exist.");
        }

        var checklist = snapshot.Record.Checklist
            ?? throw new DomainRuleException(DomainRule.ChecklistNotFound, "This review has no checklist.");
        if (!checklist.Parse().Sections.SelectMany(section => section.Items).Any(item => item.Id == checkItemId))
        {
            throw new DomainRuleException(
                DomainRule.ChecklistItemNotFound,
                $"The checklist has no check '{checkItemId}'.");
        }

        // Gather the facts of the actor; the rule alone decides
        var isSystemAdmin = await _access.IsSystemAdminAsync(actor, cancellationToken);
        var isLead = await ReviewActorRoles.IsLeadAsync(_access, actor, review, cancellationToken);
        var slot = await ReviewActorRoles.FindOwnSlotAsync(_users, actor, snapshot.Slots, cancellationToken);
        var ballot = slot is null ? null : snapshot.Record.Ballots.FirstOrDefault(b => b.SlotId == slot.Id);
        if (!CheckThreadRules.CanStart(isSystemAdmin, isLead, slot is not null, ballot?.Status, snapshot.Slots.Count))
        {
            throw NotAllowed(
                "A discussion about a check can be started by the lead of the review or by a reviewer who has " +
                "submitted their own ballot, so that no one learns how the others voted too early.");
        }
    }

    /// <summary>
    ///     Describes where a thread is anchored, for the audit trail.
    /// </summary>
    /// <param name="thread">The stored thread.</param>
    /// <returns>
    ///     <c>check {id}</c> for a check thread; otherwise the path followed by "line 3", "lines 3-5" or
    ///     "(whole file)", with "(baseline)" added for a baseline line (the head is the default and keeps the text
    ///     unchanged).
    /// </returns>
    private static string AnchorText(ReviewThread thread)
    {
        if (thread.CheckItemId is not null)
        {
            return $"check {thread.CheckItemId}";
        }

        string anchor;
        if (thread.StartLine is not { } start)
        {
            anchor = "(whole file)";
        }
        else if (thread.EndLine == start)
        {
            anchor = $"line {start}";
        }
        else
        {
            anchor = $"lines {start}-{thread.EndLine}";
        }

        if (thread.StartLine is not null && thread.Side == ThreadSide.Baseline)
        {
            anchor += " (baseline)";
        }

        return $"{thread.FilePath} {anchor}";
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
