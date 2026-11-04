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

using DemaConsulting.ReviewCenter.Core;

namespace DemaConsulting.ReviewCenter.Tests.Core;

/// <summary>
///     In-memory <see cref="INotificationStore"/> that keeps the contract of the real one: the limits and the
///     pending state are decided together, a send is reserved (counted) before it happens and released when it
///     fails, and every decision with one winner is made atomically.
/// </summary>
/// <remarks>
///     One lock serializes every access, which is what the serialized transaction and the conditional updates of the
///     database give. The limits are decided by <see cref="ConfirmationRequest.Decide"/> and
///     <see cref="SendLimits.Decide"/>, the same code the real
///     store uses. Like the real store it returns copies. A test can read the stored rows, the counted mails and
///     the audit drafts the changes carried. Thread-safe.
/// </remarks>
internal sealed class FakeNotificationStore : INotificationStore
{
    /// <summary>
    ///     Serializes every access.
    /// </summary>
    private readonly object _lock = new();

    /// <summary>
    ///     The preference rows by user.
    /// </summary>
    private readonly Dictionary<int, NotificationPreference> _rows = [];

    /// <summary>
    ///     The counted mails.
    /// </summary>
    private readonly List<NotificationSendLogEntry> _log = [];

    /// <summary>
    ///     The next identity of a counted mail.
    /// </summary>
    private int _nextLogId = 1;

    /// <summary>
    ///     The notified items by user.
    /// </summary>
    private readonly Dictionary<int, List<NotifiedItem>> _notified = [];

    /// <summary>
    ///     The users of the recipients (user name, display name, disabled, must change the password) by identity.
    /// </summary>
    private readonly Dictionary<int, (string Username, string DisplayName, bool IsDisabled, bool MustChangePassword)> _directory = [];

    /// <summary>
    ///     The next identity of a notified item.
    /// </summary>
    private int _nextItemId = 1;

    /// <summary>
    ///     Gets the commits applied so far, in order.
    /// </summary>
    public List<NotificationCommit> Commits { get; } = [];

    /// <summary>
    ///     Gets or sets an exception that <see cref="ApplyAsync"/> throws instead of applying, or <c>null</c>.
    /// </summary>
    public Exception? ApplyFailure { get; set; }

    /// <summary>
    ///     Gets or sets an action that runs after <see cref="ListRecipientsAsync"/> has read the recipients and before
    ///     it returns them, so a test can change the store while a pass is already working on what it read, or
    ///     <c>null</c> for none.
    /// </summary>
    public Action? AfterListRecipients { get; set; }

    /// <summary>
    ///     Gets the audit drafts received so far, in the order the changes were made.
    /// </summary>
    public List<AuditDraft> Audits { get; } = [];

    /// <summary>
    ///     Gets the number of calls the store received, for tests that show that a feature touches no table.
    /// </summary>
    public int Calls { get; private set; }

    /// <summary>
    ///     Gets a snapshot of the counted mails.
    /// </summary>
    public IReadOnlyList<NotificationSendLogEntry> Log
    {
        get
        {
            lock (_lock)
            {
                return _log.ToList();
            }
        }
    }

    /// <summary>
    ///     Makes a user a confirmed address holder with a mode, as the dispatcher tests need.
    /// </summary>
    /// <param name="userId">The identity of the user.</param>
    /// <param name="username">The user name.</param>
    /// <param name="address">The confirmed address.</param>
    /// <param name="mode">The mode.</param>
    /// <param name="baselinePending">Whether the baseline is pending.</param>
    /// <param name="isDisabled">Whether the account is disabled.</param>
    /// <param name="mustChangePassword">Whether the user must replace a temporary password.</param>
    /// <param name="displayName">The display name; the upper-case user name when omitted.</param>
    public void Seed(
        int userId,
        string username,
        string address,
        NotificationMode mode,
        bool baselinePending = false,
        bool isDisabled = false,
        bool mustChangePassword = false,
        string? displayName = null)
    {
        lock (_lock)
        {
            _rows[userId] = new NotificationPreference
            {
                UserId = userId,
                Email = address,
                Mode = mode,
                BaselinePending = baselinePending,
                Updated = DateTimeOffset.UnixEpoch
            };
            _directory[userId] = (username, displayName ?? username.ToUpperInvariant(), isDisabled, mustChangePassword);
        }
    }

    /// <summary>
    ///     Gets the notified items of a user as a snapshot, ordered by project, review, section and kind.
    /// </summary>
    /// <param name="userId">The user.</param>
    /// <returns>The items.</returns>
    public IReadOnlyList<NotifiedItem> NotifiedOf(int userId)
    {
        lock (_lock)
        {
            return _notified.TryGetValue(userId, out var items)
                ? [.. items.OrderBy(i => i.ProjectId).ThenBy(i => i.ReviewId, StringComparer.Ordinal).ThenBy(i => i.Section).ThenBy(i => i.Kind, StringComparer.Ordinal)]
                : [];
        }
    }

    /// <summary>
    ///     Replaces the confirmed address of a user as a confirmation does, so the generation of the row rises.
    /// </summary>
    /// <param name="userId">The user.</param>
    /// <param name="address">The new confirmed address.</param>
    public void ReplaceConfirmedAddress(int userId, string address)
    {
        lock (_lock)
        {
            var row = _rows[userId];
            _rows[userId] = Rebuild(
                row,
                userId,
                address,
                row.PendingEmail,
                row.PendingCodeHash,
                row.PendingCodeSalt,
                row.PendingCodeExpiresUtc,
                row.PendingAttempts,
                row.PendingSentUtc,
                row.Updated,
                1);
        }
    }

    /// <summary>
    ///     Switches the mode of a user as the person does on the settings page, so the generation of the row rises.
    /// </summary>
    /// <param name="userId">The user.</param>
    /// <param name="mode">The new mode; it must differ from the current one.</param>
    public void SwitchMode(int userId, NotificationMode mode)
    {
        var changed = SetModeAsync(
            userId,
            mode,
            DateTimeOffset.UnixEpoch,
            AuditDraft.By(InboxTestWorld.ActorOf("ann"), "test.mode", mode.ToString()))
            .GetAwaiter()
            .GetResult();
        Assert.True(changed);
    }

    /// <summary>
    ///     Gets the dispatch state of a user as stored.
    /// </summary>
    /// <param name="userId">The user.</param>
    /// <returns>The state.</returns>
    public NotificationDispatchState StateOf(int userId)
    {
        lock (_lock)
        {
            return StateOfRow(_rows[userId]);
        }
    }

    /// <summary>
    ///     Marks an item as already notified, for tests of the dedupe rule.
    /// </summary>
    /// <param name="userId">The user.</param>
    /// <param name="key">The item.</param>
    /// <param name="state">The state token.</param>
    /// <param name="gaveUp">Whether the row records a state the server gave up mailing.</param>
    public void AddNotified(int userId, NotifiedKey key, string state, bool gaveUp = false)
    {
        lock (_lock)
        {
            ReplaceItem(userId, key, state, DateTimeOffset.UnixEpoch, gaveUp);
        }
    }

    /// <summary>
    ///     Counts a mail as if it had been sent, for tests of the limits.
    /// </summary>
    /// <param name="userId">The user the mail was for.</param>
    /// <param name="address">The address the mail went to.</param>
    /// <param name="sentUtc">When the mail was sent.</param>
    /// <param name="kind">The kind of the mail; a confirmation mail when omitted.</param>
    public void AddLog(
        int userId,
        string address,
        DateTimeOffset sentUtc,
        string kind = NotificationSendLogEntry.KindConfirmation)
    {
        lock (_lock)
        {
            _log.Add(new NotificationSendLogEntry
            {
                Id = _nextLogId++,
                UserId = userId,
                ToAddress = address,
                Kind = kind,
                SentUtc = sentUtc
            });
        }
    }

    /// <inheritdoc />
    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        Calls++;
        return Task.FromResult(true);
    }

    /// <inheritdoc />
    public Task<NotificationPreference?> GetAsync(int userId, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            Calls++;
            return Task.FromResult(_rows.TryGetValue(userId, out var row) ? Copy(row) : null);
        }
    }

    /// <inheritdoc />
    public Task<(ConfirmationReservation Outcome, int LogId)> ReserveConfirmationAsync(
        ConfirmationRequest request,
        AuditDraft audit,
        CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            _rows.TryGetValue(request.UserId, out var existing);
            var userCount = _log.Count(e => e.UserId == request.UserId && e.SentUtc >= request.Now.AddHours(-24));
            var addressCount = _log.Count(e => e.ToAddress == request.Address && e.SentUtc >= request.Now.AddHours(-24));
            var serverCount = _log.Count(e => e.SentUtc >= request.Now.AddHours(-1));
            var outcome = request.Decide(existing?.PendingSentUtc, userCount, addressCount, serverCount);
            if (outcome != ConfirmationReservation.Reserved)
            {
                return Task.FromResult((outcome, 0));
            }

            var logId = _nextLogId++;
            _log.Add(new NotificationSendLogEntry
            {
                Id = logId,
                UserId = request.UserId,
                ToAddress = request.Address,
                Kind = NotificationSendLogEntry.KindConfirmation,
                SentUtc = request.Now
            });
            _rows[request.UserId] = Rebuild(
                existing,
                request.UserId,
                existing?.Email,
                request.Address,
                request.CodeHash,
                request.CodeSalt,
                request.ExpiresUtc,
                0,
                request.Now,
                request.Now);
            Audits.Add(audit);
            return Task.FromResult((outcome, logId));
        }
    }

    /// <inheritdoc />
    public Task AbortConfirmationAsync(
        int userId,
        int logId,
        string codeHash,
        CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            _log.RemoveAll(e => e.Id == logId && e.UserId == userId);
            if (_rows.TryGetValue(userId, out var row) && row.PendingCodeHash == codeHash)
            {
                _rows[userId] = Rebuild(row, userId, row.Email, null, null, null, null, 0, null, row.Updated);
            }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<bool> TryCountAttemptAsync(
        int userId,
        string codeHash,
        int maxAttempts,
        CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (!_rows.TryGetValue(userId, out var row) || row.PendingCodeHash != codeHash || row.PendingAttempts >= maxAttempts)
            {
                return Task.FromResult(false);
            }

            var counted = Copy(row);
            _rows[userId] = Rebuild(
                row,
                counted.UserId,
                counted.Email,
                counted.PendingEmail,
                counted.PendingCodeHash,
                counted.PendingCodeSalt,
                counted.PendingCodeExpiresUtc,
                counted.PendingAttempts + 1,
                counted.PendingSentUtc,
                counted.Updated);
            return Task.FromResult(true);
        }
    }

    /// <inheritdoc />
    public Task<bool> ConfirmAsync(
        int userId,
        string codeHash,
        DateTimeOffset now,
        AuditDraft audit,
        CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (!_rows.TryGetValue(userId, out var row) || row.PendingCodeHash != codeHash || row.PendingEmail is null
                || !(row.PendingCodeExpiresUtc > now))
            {
                return Task.FromResult(false);
            }

            _rows[userId] = Rebuild(row, userId, row.PendingEmail, null, null, null, null, 0, null, now, 1);
            Audits.Add(audit);
            return Task.FromResult(true);
        }
    }

    /// <inheritdoc />
    public Task<bool> RemoveAsync(int userId, AuditDraft audit, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (!_rows.Remove(userId))
            {
                return Task.FromResult(false);
            }

            _notified.Remove(userId);
            Audits.Add(audit);
            return Task.FromResult(true);
        }
    }

    /// <inheritdoc />
    public Task<bool> SetModeAsync(
        int userId,
        NotificationMode mode,
        DateTimeOffset nowUtc,
        AuditDraft audit,
        CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            Calls++;
            if (!_rows.TryGetValue(userId, out var row)
                || (mode != NotificationMode.None && row.Email is null)
                || row.Mode == mode)
            {
                return Task.FromResult(false);
            }

            // Switching on from none marks the baseline as pending; between the active modes the mark stays
            var switchedOn = row.Mode == NotificationMode.None || row.BaselinePending;
            _rows[userId] = new NotificationPreference
            {
                UserId = userId,
                Email = row.Email,
                PendingEmail = row.PendingEmail,
                PendingCodeHash = row.PendingCodeHash,
                PendingCodeSalt = row.PendingCodeSalt,
                PendingCodeExpiresUtc = row.PendingCodeExpiresUtc,
                PendingAttempts = row.PendingAttempts,
                PendingSentUtc = row.PendingSentUtc,
                Mode = mode,
                BaselinePending = mode != NotificationMode.None && switchedOn,
                DigestHandledUtc = mode == NotificationMode.None ? null : row.DigestHandledUtc,
                Generation = row.Generation + 1,
                Updated = nowUtc
            };
            if (mode == NotificationMode.None)
            {
                _notified.Remove(userId);
            }

            Audits.Add(audit);
            return Task.FromResult(true);
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<NotificationRecipient>> ListRecipientsAsync(CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            Calls++;
            IReadOnlyList<NotificationRecipient> recipients =
            [
                .. _rows.Values
                    .Where(r => r.Mode != NotificationMode.None && r.Email is not null)
                    .OrderBy(r => r.UserId)
                    .Select(r =>
                    {
                        var user = _directory.TryGetValue(r.UserId, out var known)
                            ? known
                            : ($"user{r.UserId}", $"User {r.UserId}", false, false);
                        return new NotificationRecipient(
                            r.UserId,
                            user.Item1,
                            user.Item2,
                            r.Email!,
                            r.Mode,
                            StateOfRow(r),
                            user.Item3,
                            user.Item4,
                            r.Generation);
                    })
            ];
            AfterListRecipients?.Invoke();
            return Task.FromResult(recipients);
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<NotifiedItem>> GetNotifiedAsync(int userId, CancellationToken cancellationToken = default)
    {
        Calls++;
        return Task.FromResult(NotifiedOf(userId));
    }

    /// <inheritdoc />
    public Task<bool> ApplyAsync(int userId, NotificationCommit commit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(commit);
        lock (_lock)
        {
            Calls++;
            if (ApplyFailure is not null)
            {
                throw ApplyFailure;
            }

            if (!_rows.TryGetValue(userId, out var row)
                || row.Mode == NotificationMode.None
                || row.Email != commit.Address
                || row.Generation != commit.Generation)
            {
                return Task.FromResult(false);
            }

            if (commit.ReplaceAll is { } all)
            {
                _notified[userId] = [];
                foreach (var entry in all)
                {
                    ReplaceItem(userId, entry.Key, entry.State, commit.NowUtc, entry.GaveUp);
                }
            }
            else
            {
                foreach (var key in commit.RemoveKeys)
                {
                    _notified.GetValueOrDefault(userId)?.RemoveAll(i => Matches(i, key));
                }

                foreach (var entry in commit.Upserts)
                {
                    ReplaceItem(userId, entry.Key, entry.State, commit.NowUtc, entry.GaveUp);
                }
            }

            _rows[userId] = new NotificationPreference
            {
                UserId = userId,
                Email = row.Email,
                PendingEmail = row.PendingEmail,
                PendingCodeHash = row.PendingCodeHash,
                PendingCodeSalt = row.PendingCodeSalt,
                PendingCodeExpiresUtc = row.PendingCodeExpiresUtc,
                PendingAttempts = row.PendingAttempts,
                PendingSentUtc = row.PendingSentUtc,
                Mode = row.Mode,
                BaselinePending = commit.State.BaselinePending,
                BatchStartedUtc = commit.State.BatchStartedUtc,
                DigestHandledUtc = commit.State.DigestHandledUtc,
                FailureCount = commit.State.FailureCount,
                NextAttemptUtc = commit.State.NextAttemptUtc,
                Generation = row.Generation,
                Updated = commit.NowUtc
            };
            _log.RemoveAll(e => e.SentUtc < commit.NowUtc.AddDays(-1));
            Commits.Add(commit);
            return Task.FromResult(true);
        }
    }

    /// <inheritdoc />
    public Task<(SendReservation Outcome, int LogId)> ReserveSendAsync(
        int userId,
        string kind,
        string address,
        DateTimeOffset now,
        int maxPerHour,
        int maxPerUserPerDay,
        CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            Calls++;
            _log.RemoveAll(e => e.SentUtc < now.AddDays(-1));
            var serverCount = _log.Count(e => e.SentUtc >= now.AddHours(-1));
            var userCount = _log.Count(e => e.UserId == userId && e.SentUtc >= now.AddDays(-1));
            var outcome = SendLimits.Decide(serverCount, userCount, maxPerHour, maxPerUserPerDay);
            if (outcome != SendReservation.Reserved)
            {
                return Task.FromResult((outcome, 0));
            }

            var logId = _nextLogId++;
            _log.Add(new NotificationSendLogEntry
            {
                Id = logId,
                UserId = userId,
                ToAddress = address,
                Kind = kind,
                SentUtc = now
            });
            return Task.FromResult((outcome, logId));
        }
    }

    /// <inheritdoc />
    public Task ReleaseSendAsync(int userId, int logId, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            Calls++;
            _log.RemoveAll(e => e.Id == logId && e.UserId == userId);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<int> CountSentAsync(int? userId, DateTimeOffset sinceUtc, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            Calls++;
            return Task.FromResult(_log.Count(e => e.SentUtc >= sinceUtc && (userId is null || e.UserId == userId)));
        }
    }

    /// <summary>
    ///     Builds a row from the given address fields, keeping the mode and the dispatch state of an earlier row.
    /// </summary>
    /// <param name="old">The earlier row; <c>null</c> when there is none.</param>
    /// <param name="userId">The user.</param>
    /// <param name="email">The confirmed address.</param>
    /// <param name="pendingEmail">The pending address.</param>
    /// <param name="hash">The hash of the code.</param>
    /// <param name="salt">The salt.</param>
    /// <param name="expires">When the code expires.</param>
    /// <param name="attempts">The wrong attempts.</param>
    /// <param name="sent">When the code was mailed.</param>
    /// <param name="updated">When the row changed.</param>
    /// <param name="generationStep">How much the generation rises; zero when the confirmed address stays.</param>
    /// <returns>The new row.</returns>
    private static NotificationPreference Rebuild(
        NotificationPreference? old,
        int userId,
        string? email,
        string? pendingEmail,
        string? hash,
        string? salt,
        DateTimeOffset? expires,
        int attempts,
        DateTimeOffset? sent,
        DateTimeOffset updated,
        int generationStep = 0)
    {
        return new NotificationPreference
        {
            UserId = userId,
            Email = email,
            PendingEmail = pendingEmail,
            PendingCodeHash = hash,
            PendingCodeSalt = salt,
            PendingCodeExpiresUtc = expires,
            PendingAttempts = attempts,
            PendingSentUtc = sent,
            Mode = old?.Mode ?? NotificationMode.None,
            BaselinePending = old?.BaselinePending ?? false,
            BatchStartedUtc = old?.BatchStartedUtc,
            DigestHandledUtc = old?.DigestHandledUtc,
            FailureCount = old?.FailureCount ?? 0,
            NextAttemptUtc = old?.NextAttemptUtc,
            Generation = (old?.Generation ?? 0) + generationStep,
            Updated = updated
        };
    }

    /// <summary>
    ///     Reads the dispatch state of a row.
    /// </summary>
    /// <param name="row">The row.</param>
    /// <returns>The state.</returns>
    private static NotificationDispatchState StateOfRow(NotificationPreference row)
    {
        return new NotificationDispatchState(
            row.BaselinePending,
            row.BatchStartedUtc,
            row.DigestHandledUtc,
            row.FailureCount,
            row.NextAttemptUtc);
    }

    /// <summary>
    ///     Tells whether a stored item is the one with the key.
    /// </summary>
    /// <param name="item">The stored item.</param>
    /// <param name="key">The key.</param>
    /// <returns><c>true</c> when it matches.</returns>
    private static bool Matches(NotifiedItem item, NotifiedKey key)
    {
        return item.ProjectId == key.ProjectId && item.ReviewId == key.ReviewId && item.Section == key.Section
               && item.Kind == key.Kind;
    }

    /// <summary>
    ///     Inserts an item or replaces the item with the same key; the caller holds the lock.
    /// </summary>
    /// <param name="userId">The user.</param>
    /// <param name="key">The key.</param>
    /// <param name="state">The state token.</param>
    /// <param name="notifiedUtc">The time to store.</param>
    /// <param name="gaveUp">Whether the row records a state the server gave up mailing.</param>
    private void ReplaceItem(int userId, NotifiedKey key, string state, DateTimeOffset notifiedUtc, bool gaveUp = false)
    {
        if (!_notified.TryGetValue(userId, out var items))
        {
            items = [];
            _notified[userId] = items;
        }

        items.RemoveAll(i => Matches(i, key));
        items.Add(new NotifiedItem
        {
            Id = _nextItemId++,
            UserId = userId,
            ProjectId = key.ProjectId,
            ReviewId = key.ReviewId,
            Section = key.Section,
            Kind = key.Kind,
            State = state,
            GaveUp = gaveUp,
            NotifiedUtc = notifiedUtc
        });
    }

    /// <summary>
    ///     Copies a row, as the real store returns copies.
    /// </summary>
    /// <param name="row">The stored row.</param>
    /// <returns>The copy.</returns>
    private static NotificationPreference Copy(NotificationPreference row)
    {
        return new NotificationPreference
        {
            UserId = row.UserId,
            Email = row.Email,
            PendingEmail = row.PendingEmail,
            PendingCodeHash = row.PendingCodeHash,
            PendingCodeSalt = row.PendingCodeSalt,
            PendingCodeExpiresUtc = row.PendingCodeExpiresUtc,
            PendingAttempts = row.PendingAttempts,
            PendingSentUtc = row.PendingSentUtc,
            Mode = row.Mode,
            BaselinePending = row.BaselinePending,
            BatchStartedUtc = row.BatchStartedUtc,
            DigestHandledUtc = row.DigestHandledUtc,
            FailureCount = row.FailureCount,
            NextAttemptUtc = row.NextAttemptUtc,
            Generation = row.Generation,
            Updated = row.Updated
        };
    }
}
