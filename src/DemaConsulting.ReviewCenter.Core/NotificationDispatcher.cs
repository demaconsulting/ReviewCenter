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
///     12 hours; after the sixth failure the server gives up on that batch: the items are recorded as given up
///     (<see cref="NotifiedItem.GaveUp"/>), not as delivered, so the dead mail is not tried again and no further mail
///     is sent about the same state, while a later change of an item is mailed as usual. When the count of waiting
///     replies of an item falls, the stored count follows it down without a mail, so a later rise is measured from
///     the count the item has now. The server-wide
///     hourly limit and a limit per user and day defer a mail without counting it as a failure; both are applied by
///     the store, which counts the mail (a reservation) in the database right before the send, so senders inside
///     the one host (this dispatcher and the confirmation mails of the account page) can not both pass the same
///     count, and the limits cover the confirmation mails too; a send that fails or is cancelled gives its
///     reservation back. Exactly one host may run the dispatcher for a database: the server is a single instance by
///     design and the dispatcher is not coordinated between hosts, so two hosts on one database could send the same
///     mail twice. The items and the state of a mail are recorded (together) only after it was sent, so a crash between
///     the send and the record can repeat a mail but never lose one; the record is conditional on the confirmed
///     address and the generation of the
///     preference that the mail was prepared for, so a mail whose recipient changed the address or the mode meanwhile
///     is neither sent when the change is seen before the send nor recorded when it is seen after it. To keep an idle
///     server cheap, the inbox of a recipient is read only when the
///     audit trail has a new entry, every <see cref="SweepInterval"/>, or when stored time-driven work is due. A failure
///     of one recipient is reported through the error callback and does not stop the pass, and the next pass reads
///     every inbox again. The dispatcher keeps
///     only the last audit sequence and the last sweep time in memory, and keeps them only after a pass without a
///     failed recipient; one pass runs at a time (a later call waits),
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
    ///     The new, lowered and stale items of a recipient, and all candidates of the inbox.
    /// </summary>
    /// <param name="Candidates">All candidates of the inbox.</param>
    /// <param name="Fresh">The candidates that are new.</param>
    /// <param name="Stale">The stored items that are not in the inbox any more.</param>
    /// <param name="Lowered">
    ///     The stored items whose count of waiting replies fell, with the lower current state and the flag as stored;
    ///     they are not new and are never mailed.
    /// </param>
    private sealed record Diff(
        IReadOnlyList<NotificationCandidate> Candidates,
        IReadOnlyList<NotificationCandidate> Fresh,
        IReadOnlyList<NotifiedKey> Stale,
        IReadOnlyList<NotifiedEntry> Lowered);

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
                // One recipient must not stop the others; the markers are not kept, so the next pass reads again
                clean = false;
                _onError?.Invoke(exception);
            }
        }

        if (clean)
        {
            _lastSequence = sequence;
            if (sweep)
            {
                _lastSweepUtc = now;
            }
        }
        else
        {
            // A recipient failed: forget both markers, so the next pass reads every inbox again, whatever the audit
            // sequence says and however recently a sweep ran. What a failed send waits for is stored with the
            // recipient (see RetryDelays), so the extra read can not send earlier than that
            _lastSequence = null;
            _lastSweepUtc = null;
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
        await _store.ApplyAsync(
            recipient.UserId,
            new NotificationCommit(now, [], [], all, state, recipient.Address, recipient.Generation),
            cancellationToken);
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
                await ApplyAsync(recipient, now, [], diff.Stale, diff.Lowered, state with { BatchStartedUtc = now }, cancellationToken);
            }
            else if (diff.Stale.Count > 0 || diff.Lowered.Count > 0)
            {
                await ApplyAsync(recipient, now, [], diff.Stale, diff.Lowered, state, cancellationToken);
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
            await ApplyAsync(recipient, now, [], current.Stale, current.Lowered, ClearedState(state, null), cancellationToken);
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
            await ApplyAsync(recipient, now, [], diff.Stale, diff.Lowered, ClearedState(state, now), cancellationToken);
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
    /// <param name="kind">The kind of the send-log row that reserves the mail.</param>
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
        var composition = _composer.Compose(diff.Fresh, outstanding, recipient.Mode);
        var message = new NotificationMessage(recipient.Address, composition.Subject, composition.Text, composition.Html);
        var state = recipient.State;
        var upserts = diff.Fresh.Select(c => new NotifiedEntry(c.Key, c.State)).ToList();

        // Look at the preference once more right before the send: when the user confirmed another address or changed
        // the mode since the pass read the recipient, this mail was prepared for a state that no longer holds, so
        // nothing is sent and nothing is recorded; the next pass starts from the current preference
        if (!await IsStillCurrentAsync(recipient, cancellationToken))
        {
            return Outcome.None;
        }

        // The limits are applied by the store, which counts the mail in the same transaction (the reservation), so
        // senders that race can not both slip under a limit. A refused reservation holds the mail back without
        // changing anything; the next pass asks again
        var (reservation, logId) = await _store.ReserveSendAsync(
            recipient.UserId,
            kind,
            recipient.Address,
            now,
            _settings.MaxMailsPerHour,
            MaxMailsPerUserPerDay,
            cancellationToken);
        if (reservation != SendReservation.Reserved)
        {
            return Outcome.Deferred;
        }

        try
        {
            await _sender.SendAsync(message, cancellationToken);
        }
        catch (Exception exception)
        {
            // The mail did not go out, so it is no longer counted; the release does not honor the cancellation of
            // the pass, so a shutdown does not leak the reservation
            await _store.ReleaseSendAsync(recipient.UserId, logId, CancellationToken.None);
            if (exception is OperationCanceledException && cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            var failures = state.FailureCount + 1;
            if (failures <= RetryDelays.Count)
            {
                var waiting = state with { FailureCount = failures, NextAttemptUtc = now + RetryDelays[failures - 1] };
                await ApplyAsync(recipient, now, [], [], [], waiting, cancellationToken);
                return Outcome.Failed;
            }

            // Give up: the items are recorded as given up (not as delivered), so the dead mail is not tried again
            // and again; the same state is not mailed any more, a later new state of an item still is
            var gaveUp = diff.Fresh.Select(c => new NotifiedEntry(c.Key, c.State, true)).ToList();
            await ApplyAsync(recipient, now, gaveUp, diff.Stale, diff.Lowered, ClearedState(state, DigestHandledFor(recipient, now)), cancellationToken);
            _onError?.Invoke(exception);
            return Outcome.Failed;
        }

        // The mail is out and its reservation is the log row: record items and state together. When the commit is
        // stale the row stays, because the mail was sent and is counted
        await ApplyAsync(recipient, now, upserts, diff.Stale, diff.Lowered, ClearedState(state, DigestHandledFor(recipient, now)), cancellationToken);
        return Outcome.Sent;
    }

    /// <summary>
    ///     Tells whether the stored preference of a recipient is still the one a mail was prepared for.
    /// </summary>
    /// <param name="recipient">The recipient as the pass read it.</param>
    /// <param name="cancellationToken">Cancels the pass.</param>
    /// <returns>
    ///     <c>true</c> when the row exists, has a mode, and still has the confirmed address and the generation that the
    ///     recipient was read with.
    /// </returns>
    private async Task<bool> IsStillCurrentAsync(NotificationRecipient recipient, CancellationToken cancellationToken)
    {
        var current = await _store.GetAsync(recipient.UserId, cancellationToken);
        return current is not null
               && current.Mode != NotificationMode.None
               && current.Generation == recipient.Generation
               && string.Equals(current.Email, recipient.Address, StringComparison.Ordinal);
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
    /// <param name="lowered">The items whose stored count of waiting replies follows the current, lower one.</param>
    /// <param name="state">The new state.</param>
    /// <param name="cancellationToken">Cancels the pass.</param>
    /// <returns>A task that completes when the change is stored.</returns>
    private async Task ApplyAsync(
        NotificationRecipient recipient,
        DateTimeOffset now,
        IReadOnlyList<NotifiedEntry> upserts,
        IReadOnlyList<NotifiedKey> stale,
        IReadOnlyList<NotifiedEntry> lowered,
        NotificationDispatchState state,
        CancellationToken cancellationToken)
    {
        await _store.ApplyAsync(
            recipient.UserId,
            new NotificationCommit(now, [.. upserts, .. lowered], stale, null, state, recipient.Address, recipient.Generation),
            cancellationToken);
    }

    /// <summary>
    ///     Reads the inbox of a recipient as that recipient and compares it with what the recipient was told.
    /// </summary>
    /// <param name="recipient">The recipient.</param>
    /// <param name="cancellationToken">Cancels the pass.</param>
    /// <returns>The new, lowered and stale items.</returns>
    private async Task<Diff> ComputeAsync(NotificationRecipient recipient, CancellationToken cancellationToken)
    {
        var candidates = await ReadCandidatesAsync(recipient, cancellationToken);
        var stored = await _store.GetNotifiedAsync(recipient.UserId, cancellationToken);
        var items = stored.ToDictionary(i => new NotifiedKey(i.ProjectId, i.ReviewId, i.Section, i.Kind));
        var fresh = new List<NotificationCandidate>();
        var lowered = new List<NotifiedEntry>();
        foreach (var candidate in candidates)
        {
            var item = items.GetValueOrDefault(candidate.Key);
            if (NotificationComposer.IsNew(item?.State, candidate.State))
            {
                fresh.Add(candidate);
            }
            else if (item is not null && NotificationComposer.IsLowered(item.State, candidate.State))
            {
                // Not new, but the count fell: remember the lower count (as stored otherwise) without a mail
                lowered.Add(new NotifiedEntry(candidate.Key, candidate.State, item.GaveUp));
            }
        }

        var present = candidates.Select(c => c.Key).ToHashSet();
        var stale = items.Keys.Where(key => !present.Contains(key)).ToList();
        return new Diff(candidates, fresh, stale, lowered);
    }

    /// <summary>
    ///     Reads the inbox of a recipient, as that recipient, and keeps the rows a mail may mention.
    /// </summary>
    /// <param name="recipient">The recipient.</param>
    /// <param name="cancellationToken">Cancels the pass.</param>
    /// <returns>The candidates, one for each item (project, review, section and kind of reason).</returns>
    /// <remarks>
    ///     An inbox can list the same review twice in the section "to answer" (the standing of the reviewer and a
    ///     question for the user). The two rows have different kinds, so both are kept and each is told on its own; a
    ///     second row of the same kind is dropped, so the key stays unique.
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
