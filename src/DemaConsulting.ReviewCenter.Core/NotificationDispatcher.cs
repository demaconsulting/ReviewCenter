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
///     How many mails one pass of the notification dispatcher sent, could not send and held back.
/// </summary>
/// <remarks>The record holds counts only, never an address or a name, so it is safe to log. It is immutable.</remarks>
/// <param name="Sent">The number of mails that were sent.</param>
/// <param name="Failed">The number of mails that could not be sent in this pass.</param>
/// <param name="Deferred">The number of mails that were held back by a sending limit.</param>
public sealed record NotificationPassResult(int Sent, int Failed, int Deferred);

/// <summary>
///     Decides, for every user who chose a notification mode, which inbox items are new and mails them.
/// </summary>
/// <remarks>
///     One pass (<see cref="RunPassAsync"/>) looks at every recipient. The inbox of a recipient is always read through
///     <see cref="InboxService"/> as that recipient (never as an API token), so what is mailed can not exceed what
///     the recipient may see, including the masking of ballots that are still hidden. An item is new when it was
///     never mailed in its current state (see <see cref="NotificationComposer.IsNew"/>); items that left the inbox
///     are forgotten, so a reason that comes back is new again. In the mode <see cref="NotificationMode.Every"/> the
///     first new item starts a batch window and one mail with everything new is sent when it ends; in the mode
///     <see cref="NotificationMode.Daily"/> one summary per local date is sent at the digest hour, and the slot is
///     used up even when nothing was new. A user who has just switched a mode on gets no mail for the backlog: the
///     first pass records what exists. A failed send is retried after 5 minutes, 15 minutes, 1 hour, 4 hours and
///     12 hours; after the sixth failure the items are recorded as notified and the mail is dropped. The server-wide
///     hourly limit and a limit per user and day defer a mail without counting it as a failure. A mail is recorded
///     (items, state and send log together) only after it was sent, so a crash between the send and the record can
///     repeat a mail but never lose one. To keep an idle server cheap, the inbox of a recipient is read only when the
///     audit trail has a new entry, every <see cref="SweepInterval"/>, or when stored time-driven work is due. A failure
///     of one recipient is reported through the error callback and does not stop the pass. The dispatcher keeps
///     only the last audit sequence and the last sweep time in memory; one pass runs at a time (a later call waits),
///     so the type is thread-safe.
/// </remarks>
public sealed class NotificationDispatcher
{
    /// <summary>
    ///     The most notification mails one user receives in a rolling day.
    /// </summary>
    public const int MaxMailsPerUserPerDay = 24;

    /// <summary>
    ///     The waits after the first, second, third, fourth and fifth failed send; the sixth failure gives up.
    /// </summary>
    public static readonly IReadOnlyList<TimeSpan> RetryDelays =
    [
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(15),
        TimeSpan.FromHours(1),
        TimeSpan.FromHours(4),
        TimeSpan.FromHours(12)
    ];

    /// <summary>
    ///     How long the dispatcher waits at most before it looks at the inbox of every recipient again although the
    ///     audit trail has no new entry.
    /// </summary>
    public static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(15);

    /// <summary>
    ///     What happened to the mail of one recipient in a pass.
    /// </summary>
    private enum Outcome
    {
        /// <summary>No mail was due or none was sent.</summary>
        None,

        /// <summary>A mail was sent.</summary>
        Sent,

        /// <summary>A send failed.</summary>
        Failed,

        /// <summary>A mail was held back by a limit.</summary>
        Deferred
    }

    /// <summary>
    ///     The new and the stale items of a recipient, and the items that were mailed before and still wait.
    /// </summary>
    /// <param name="Candidates">All candidates of the inbox.</param>
    /// <param name="Fresh">The candidates that are new.</param>
    /// <param name="Stale">The notified items that are not in the inbox any more.</param>
    private sealed record Diff(
        IReadOnlyList<NotificationCandidate> Candidates,
        IReadOnlyList<NotificationCandidate> Fresh,
        IReadOnlyList<NotifiedKey> Stale);

    /// <summary>
    ///     Lets one pass run at a time.
    /// </summary>
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>
    ///     The store of the preferences and the notified items.
    /// </summary>
    private readonly INotificationStore _store;

    /// <summary>
    ///     The inbox of the users.
    /// </summary>
    private readonly InboxService _inbox;

    /// <summary>
    ///     The audit store, read for its latest sequence number.
    /// </summary>
    private readonly IAuditStore _audit;

    /// <summary>
    ///     The transport.
    /// </summary>
    private readonly INotificationSender _sender;

    /// <summary>
    ///     The composer of the mails.
    /// </summary>
    private readonly NotificationComposer _composer;

    /// <summary>
    ///     The schedule of the daily summary.
    /// </summary>
    private readonly DigestSchedule _schedule;

    /// <summary>
    ///     The settings of e-mail notifications.
    /// </summary>
    private readonly NotificationSettings _settings;

    /// <summary>
    ///     The clock.
    /// </summary>
    private readonly TimeProvider _clock;

    /// <summary>
    ///     The length of the batch window of the mode <see cref="NotificationMode.Every"/>.
    /// </summary>
    private readonly TimeSpan _batchWindow;

    /// <summary>
    ///     Receives an unexpected failure, for logging; may be <c>null</c>.
    /// </summary>
    private readonly Action<Exception>? _onError;

    /// <summary>
    ///     The audit sequence the last pass without error saw; <c>null</c> before the first such pass.
    /// </summary>
    private long? _lastSequence;

    /// <summary>
    ///     When the inbox of every recipient was last read because of a sweep; <c>null</c> before the first pass.
    /// </summary>
    private DateTimeOffset? _lastSweepUtc;

    /// <summary>
    ///     Initializes a new instance of the <see cref="NotificationDispatcher"/> class.
    /// </summary>
    /// <param name="store">The store of the preferences and the notified items. Must not be null.</param>
    /// <param name="inbox">The inbox service. Must not be null.</param>
    /// <param name="audit">The audit store. Must not be null.</param>
    /// <param name="sender">The transport. Must not be null.</param>
    /// <param name="composer">The composer of the mails. Must not be null.</param>
    /// <param name="schedule">The schedule of the daily summary. Must not be null.</param>
    /// <param name="settings">The settings. Must not be null.</param>
    /// <param name="clock">The clock. Must not be null.</param>
    /// <param name="batchWindow">The length of the batch window; positive.</param>
    /// <param name="onError">Receives an unexpected failure, for logging; may be <c>null</c>.</param>
    /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="batchWindow"/> is not positive.</exception>
    public NotificationDispatcher(
        INotificationStore store,
        InboxService inbox,
        IAuditStore audit,
        INotificationSender sender,
        NotificationComposer composer,
        DigestSchedule schedule,
        NotificationSettings settings,
        TimeProvider clock,
        TimeSpan batchWindow,
        Action<Exception>? onError = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(inbox);
        ArgumentNullException.ThrowIfNull(audit);
        ArgumentNullException.ThrowIfNull(sender);
        ArgumentNullException.ThrowIfNull(composer);
        ArgumentNullException.ThrowIfNull(schedule);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(batchWindow, TimeSpan.Zero);
        _store = store;
        _inbox = inbox;
        _audit = audit;
        _sender = sender;
        _composer = composer;
        _schedule = schedule;
        _settings = settings;
        _clock = clock;
        _batchWindow = batchWindow;
        _onError = onError;
    }

    /// <summary>
    ///     Runs one pass over every recipient.
    /// </summary>
    /// <param name="cancellationToken">Cancels the pass.</param>
    /// <returns>The counts of the mails of the pass.</returns>
    /// <remarks>
    ///     A failure of one recipient is passed to the error callback and the pass goes on with the next recipient.
    ///     A failure of the store while the recipients are listed ends the pass with that exception.
    /// </remarks>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is canceled.</exception>
    public async Task<NotificationPassResult> RunPassAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return await RunLockedAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    ///     Runs one pass while the gate is held.
    /// </summary>
    /// <param name="cancellationToken">Cancels the pass.</param>
    /// <returns>The counts of the mails of the pass.</returns>
    private async Task<NotificationPassResult> RunLockedAsync(CancellationToken cancellationToken)
    {
        var now = _clock.GetUtcNow();
        var recipients = await _store.ListRecipientsAsync(cancellationToken);
        if (recipients.Count == 0)
        {
            return new NotificationPassResult(0, 0, 0);
        }

        // Read the sequence before the inboxes, so a change during the pass is seen by the next pass
        var sequence = await _audit.LatestSequenceAsync(cancellationToken);
        var sweep = _lastSweepUtc is not { } last || now - last >= SweepInterval || sequence != _lastSequence;
        int sent = 0, failed = 0, deferred = 0;
        var clean = true;
        foreach (var recipient in recipients)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                switch (await HandleAsync(recipient, now, sweep, cancellationToken))
                {
                    case Outcome.Sent:
                        sent++;
                        break;
                    case Outcome.Failed:
                        failed++;
                        break;
                    case Outcome.Deferred:
                        deferred++;
                        break;
                }
            }
            catch (Exception exception) when (!(exception is OperationCanceledException && cancellationToken.IsCancellationRequested))
            {
                // One recipient must not stop the others; the next pass reads again because the sequence is not kept
                clean = false;
                _onError?.Invoke(exception);
            }
        }

        if (clean)
        {
            _lastSequence = sequence;
        }

        if (sweep)
        {
            _lastSweepUtc = now;
        }

        return new NotificationPassResult(sent, failed, deferred);
    }

    /// <summary>
    ///     Handles one recipient.
    /// </summary>
    /// <param name="recipient">The recipient.</param>
    /// <param name="now">The time of the pass.</param>
    /// <param name="sweep">Whether this pass reads the inbox although nothing is due.</param>
    /// <param name="cancellationToken">Cancels the pass.</param>
    /// <returns>What happened to the mail.</returns>
    private async Task<Outcome> HandleAsync(
        NotificationRecipient recipient,
        DateTimeOffset now,
        bool sweep,
        CancellationToken cancellationToken)
    {
        if (recipient.IsDisabled || recipient.MustChangePassword)
        {
            return Outcome.None;
        }

        var state = recipient.State;
        if (state.BaselinePending)
        {
            await RecordBaselineAsync(recipient, now, cancellationToken);
            return Outcome.None;
        }

        if (state.NextAttemptUtc is { } next && next > now)
        {
            return Outcome.None;
        }

        if (recipient.Mode == NotificationMode.Every)
        {
            return await HandleEveryAsync(recipient, now, sweep, cancellationToken);
        }

        return recipient.Mode == NotificationMode.Daily
            ? await HandleDailyAsync(recipient, now, cancellationToken)
            : Outcome.None;
    }

    /// <summary>
    ///     Records the items that exist now as notified, without a mail, because the user has just switched a mode on.
    /// </summary>
    /// <param name="recipient">The recipient.</param>
    /// <param name="now">The time of the pass.</param>
    /// <param name="cancellationToken">Cancels the pass.</param>
    /// <returns>A task that completes when the baseline is stored.</returns>
    private async Task RecordBaselineAsync(
        NotificationRecipient recipient,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var candidates = await ReadCandidatesAsync(recipient, cancellationToken);
        var state = recipient.State with
        {
            BaselinePending = false,
            BatchStartedUtc = null,
            DigestHandledUtc = now,
            FailureCount = 0,
            NextAttemptUtc = null
        };
        var all = candidates.Select(c => new NotifiedEntry(c.Key, c.State)).ToList();
        await _store.ApplyAsync(recipient.UserId, new NotificationCommit(now, [], [], all, state, null), cancellationToken);
    }

    /// <summary>
    ///     Handles a recipient of the mode <see cref="NotificationMode.Every"/>: starts the batch window at the first
    ///     new item and sends one mail when the window ends.
    /// </summary>
    /// <param name="recipient">The recipient.</param>
    /// <param name="now">The time of the pass.</param>
    /// <param name="sweep">Whether this pass reads the inbox although nothing is due.</param>
    /// <param name="cancellationToken">Cancels the pass.</param>
    /// <returns>What happened to the mail.</returns>
    private async Task<Outcome> HandleEveryAsync(
        NotificationRecipient recipient,
        DateTimeOffset now,
        bool sweep,
        CancellationToken cancellationToken)
    {
        var state = recipient.State;
        if (state.BatchStartedUtc is not { } started)
        {
            if (!sweep)
            {
                return Outcome.None;
            }

            var diff = await ComputeAsync(recipient, cancellationToken);
            if (diff.Fresh.Count > 0)
            {
                await ApplyAsync(recipient, now, [], diff.Stale, state with { BatchStartedUtc = now }, null, cancellationToken);
            }
            else if (diff.Stale.Count > 0)
            {
                await ApplyAsync(recipient, now, [], diff.Stale, state, null, cancellationToken);
            }

            return Outcome.None;
        }

        if (now < started + _batchWindow)
        {
            return Outcome.None;
        }

        // The window is over: look again, so that what the user handled meanwhile is not mailed
        var current = await ComputeAsync(recipient, cancellationToken);
        if (current.Fresh.Count == 0)
        {
            await ApplyAsync(recipient, now, [], current.Stale, ClearedState(state, null), null, cancellationToken);
            return Outcome.None;
        }

        return await TrySendAsync(recipient, NotificationSendLogEntry.KindUpdate, current, 0, now, cancellationToken);
    }

    /// <summary>
    ///     Handles a recipient of the mode <see cref="NotificationMode.Daily"/>: at the digest hour, sends one summary
    ///     when something is new, and uses up the slot of the local date.
    /// </summary>
    /// <param name="recipient">The recipient.</param>
    /// <param name="now">The time of the pass.</param>
    /// <param name="cancellationToken">Cancels the pass.</param>
    /// <returns>What happened to the mail.</returns>
    private async Task<Outcome> HandleDailyAsync(
        NotificationRecipient recipient,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var state = recipient.State;
        if (!_schedule.IsDue(now, state.DigestHandledUtc))
        {
            return Outcome.None;
        }

        var diff = await ComputeAsync(recipient, cancellationToken);
        if (diff.Fresh.Count == 0)
        {
            await ApplyAsync(recipient, now, [], diff.Stale, ClearedState(state, now), null, cancellationToken);
            return Outcome.None;
        }

        // Items that were mailed before and still wait are only counted in the summary
        var outstanding = diff.Candidates.Count(c => InboxSections.IsAction(c.Section))
                          - diff.Fresh.Count(c => InboxSections.IsAction(c.Section));
        return await TrySendAsync(
            recipient,
            NotificationSendLogEntry.KindDigest,
            diff,
            Math.Max(0, outstanding),
            now,
            cancellationToken);
    }

    /// <summary>
    ///     Sends the mail for the new items, or holds it back by a limit, or schedules a retry, or gives up.
    /// </summary>
    /// <param name="recipient">The recipient.</param>
    /// <param name="kind">The kind of the send-log row.</param>
    /// <param name="diff">The new and stale items.</param>
    /// <param name="outstanding">The number of items that were mailed before and still wait.</param>
    /// <param name="now">The time of the pass.</param>
    /// <param name="cancellationToken">Cancels the pass.</param>
    /// <returns>What happened to the mail.</returns>
    private async Task<Outcome> TrySendAsync(
        NotificationRecipient recipient,
        string kind,
        Diff diff,
        int outstanding,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        // A limit holds the mail back without changing anything; the next pass asks again
        if (await _store.CountSentAsync(null, now.AddHours(-1), cancellationToken) >= _settings.MaxMailsPerHour
            || await _store.CountSentAsync(recipient.UserId, now.AddDays(-1), cancellationToken) >= MaxMailsPerUserPerDay)
        {
            return Outcome.Deferred;
        }

        var composition = _composer.Compose(diff.Fresh, outstanding, recipient.Mode);
        var message = new NotificationMessage(recipient.Address, composition.Subject, composition.Text, composition.Html);
        var state = recipient.State;
        var upserts = diff.Fresh.Select(c => new NotifiedEntry(c.Key, c.State)).ToList();
        try
        {
            await _sender.SendAsync(message, cancellationToken);
        }
        catch (Exception exception) when (!(exception is OperationCanceledException && cancellationToken.IsCancellationRequested))
        {
            var failures = state.FailureCount + 1;
            if (failures <= RetryDelays.Count)
            {
                var waiting = state with { FailureCount = failures, NextAttemptUtc = now + RetryDelays[failures - 1] };
                await ApplyAsync(recipient, now, [], [], waiting, null, cancellationToken);
                return Outcome.Failed;
            }

            // Give up: the items count as told, so the dead mail is not tried again and again
            await ApplyAsync(recipient, now, upserts, diff.Stale, ClearedState(state, DigestHandledFor(recipient, now)), null, cancellationToken);
            _onError?.Invoke(exception);
            return Outcome.Failed;
        }

        // The mail is out: record items, state and the send-log row together
        var log = new NotificationLogRecord(kind, recipient.Address, now);
        await ApplyAsync(recipient, now, upserts, diff.Stale, ClearedState(state, DigestHandledFor(recipient, now)), log, cancellationToken);
        return Outcome.Sent;
    }

    /// <summary>
    ///     Gets the time to store as the last handled daily slot after a mail.
    /// </summary>
    /// <param name="recipient">The recipient.</param>
    /// <param name="now">The time of the pass.</param>
    /// <returns><paramref name="now"/> in the mode daily; the stored time otherwise.</returns>
    private static DateTimeOffset? DigestHandledFor(NotificationRecipient recipient, DateTimeOffset now)
    {
        return recipient.Mode == NotificationMode.Daily ? now : recipient.State.DigestHandledUtc;
    }

    /// <summary>
    ///     Builds the state after the work of a batch or a slot is done: no window, no failure.
    /// </summary>
    /// <param name="state">The earlier state.</param>
    /// <param name="digestHandledUtc">The time to store as the last handled slot.</param>
    /// <returns>The new state.</returns>
    private static NotificationDispatchState ClearedState(NotificationDispatchState state, DateTimeOffset? digestHandledUtc)
    {
        return state with
        {
            BaselinePending = false,
            BatchStartedUtc = null,
            DigestHandledUtc = digestHandledUtc ?? state.DigestHandledUtc,
            FailureCount = 0,
            NextAttemptUtc = null
        };
    }

    /// <summary>
    ///     Applies a change for a recipient.
    /// </summary>
    /// <param name="recipient">The recipient.</param>
    /// <param name="now">The time of the pass.</param>
    /// <param name="upserts">The items to record.</param>
    /// <param name="stale">The items to forget.</param>
    /// <param name="state">The new state.</param>
    /// <param name="log">The send-log row; <c>null</c> for none.</param>
    /// <param name="cancellationToken">Cancels the pass.</param>
    /// <returns>A task that completes when the change is stored.</returns>
    private async Task ApplyAsync(
        NotificationRecipient recipient,
        DateTimeOffset now,
        IReadOnlyList<NotifiedEntry> upserts,
        IReadOnlyList<NotifiedKey> stale,
        NotificationDispatchState state,
        NotificationLogRecord? log,
        CancellationToken cancellationToken)
    {
        await _store.ApplyAsync(
            recipient.UserId,
            new NotificationCommit(now, upserts, stale, null, state, log),
            cancellationToken);
    }

    /// <summary>
    ///     Reads the inbox of a recipient as that recipient and compares it with what the recipient was told.
    /// </summary>
    /// <param name="recipient">The recipient.</param>
    /// <param name="cancellationToken">Cancels the pass.</param>
    /// <returns>The new and the stale items.</returns>
    private async Task<Diff> ComputeAsync(NotificationRecipient recipient, CancellationToken cancellationToken)
    {
        var candidates = await ReadCandidatesAsync(recipient, cancellationToken);
        var stored = await _store.GetNotifiedAsync(recipient.UserId, cancellationToken);
        var states = stored.ToDictionary(i => new NotifiedKey(i.ProjectId, i.ReviewId, i.Section), i => i.State);
        var fresh = candidates
            .Where(c => NotificationComposer.IsNew(states.GetValueOrDefault(c.Key), c.State))
            .ToList();
        var present = candidates.Select(c => c.Key).ToHashSet();
        var stale = states.Keys.Where(key => !present.Contains(key)).ToList();
        return new Diff(candidates, fresh, stale);
    }

    /// <summary>
    ///     Reads the inbox of a recipient, as that recipient, and keeps the rows a mail may mention.
    /// </summary>
    /// <param name="recipient">The recipient.</param>
    /// <param name="cancellationToken">Cancels the pass.</param>
    /// <returns>The candidates, one for each item (project, review and section).</returns>
    /// <remarks>
    ///     An inbox can list the same review twice in the section "to answer" (the standing of the reviewer and a
    ///     question for the user); the first row wins, so the key stays unique.
    /// </remarks>
    private async Task<IReadOnlyList<NotificationCandidate>> ReadCandidatesAsync(
        NotificationRecipient recipient,
        CancellationToken cancellationToken)
    {
        // No API token: the inbox is exactly what the person sees after signing in
        var view = await _inbox.GetInboxAsync(new Actor(recipient.Username, recipient.DisplayName), cancellationToken);
        return
        [
            .. view.Items
                .Select(NotificationComposer.Classify)
                .OfType<NotificationCandidate>()
                .DistinctBy(candidate => candidate.Key)
        ];
    }
}
