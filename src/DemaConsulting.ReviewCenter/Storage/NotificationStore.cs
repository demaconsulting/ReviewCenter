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
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DemaConsulting.ReviewCenter.Storage;

/// <summary>
///     The <see cref="INotificationStore"/> implementation backed by <see cref="ReviewCenterDbContext"/>.
/// </summary>
/// <remarks>
///     Each operation uses its own short-lived context from the factory, so the store is safe for concurrent use.
///     Every decision that must have exactly one winner is one conditional <c>UPDATE</c> whose affected-row count is
///     the answer, so two requests that race can not both succeed. The sending limits are counted and the pending
///     state is stored in one serialized transaction together with the audit entry (see
///     <see cref="AuditAppender"/>), so two requests can not both slip under a limit. The conditions use only
///     equality, null checks and integer comparison, except the limit counts and the expiry of a confirmation code,
///     which compare the fixed-width UTC text form of a time (see <see cref="ReviewCenterDbContext"/>). A database
///     from before e-mail notifications lacks the tables; <see cref="IsAvailableAsync"/> reports that instead of
///     failing, and lets every other database failure propagate.
/// </remarks>
/// <param name="factory">The factory that creates a context per operation.</param>
/// <param name="appender">The appender that runs each change together with its audit entry.</param>
internal sealed class NotificationStore(IDbContextFactory<ReviewCenterDbContext> factory, AuditAppender appender)
    : INotificationStore
{
    /// <summary>
    ///     The SQLite error code that reports a missing table (<c>SQLITE_ERROR</c>).
    /// </summary>
    private const int MissingTableErrorCode = 1;

    /// <summary>
    ///     The window of the limits per user and per address.
    /// </summary>
    private static readonly TimeSpan _day = TimeSpan.FromHours(24);

    /// <summary>
    ///     The window of the limit of the whole server.
    /// </summary>
    private static readonly TimeSpan _hour = TimeSpan.FromHours(1);

    /// <inheritdoc />
    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        try
        {
            // Touch the tables and the columns of the dispatcher; a database that was created before them fails with
            // "no such table" or "no such column"
            _ = await context.NotificationPreferences.AsNoTracking()
                .Select(p => new
                {
                    p.UserId,
                    p.Mode,
                    p.BaselinePending,
                    p.BatchStartedUtc,
                    p.DigestHandledUtc,
                    p.FailureCount,
                    p.NextAttemptUtc,
                    p.Generation
                })
                .Take(1)
                .ToListAsync(cancellationToken);
            _ = await context.NotificationSendLog.AsNoTracking().AnyAsync(cancellationToken);
            _ = await context.NotifiedItems.AsNoTracking().Select(i => new { i.Id, i.Kind, i.GaveUp }).Take(1).ToListAsync(cancellationToken);
            return true;
        }
        catch (SqliteException ex) when (IsMissingTable(ex))
        {
            return false;
        }
    }

    /// <summary>
    ///     Tells whether a database failure is the one of a missing table or column (SQLite error 1, "no such table"
    ///     or "no such column"), which is what a database from before e-mail notifications reports. Every other
    ///     failure is a real fault.
    /// </summary>
    /// <param name="exception">The failure.</param>
    /// <returns><c>true</c> when a table or column does not exist.</returns>
    private static bool IsMissingTable(SqliteException exception)
    {
        return exception.SqliteErrorCode == MissingTableErrorCode
               && (exception.Message.Contains("no such table", StringComparison.OrdinalIgnoreCase)
                   || exception.Message.Contains("no such column", StringComparison.OrdinalIgnoreCase));
    }

    /// <inheritdoc />
    public async Task<NotificationPreference?> GetAsync(int userId, CancellationToken cancellationToken = default)
    {
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        return await context.NotificationPreferences.AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<(ConfirmationReservation Outcome, int LogId)> ReserveConfirmationAsync(
        ConfirmationRequest request,
        AuditDraft audit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(audit);
        return await appender.ExecuteAsync<(ConfirmationReservation, int)>(
            factory,
            async context =>
            {
                // Rows older than one day can not count towards any limit: delete them, so the log does not keep
                // addresses longer than needed
                var dayStart = request.Now - _day;
                var hourStart = request.Now - _hour;
                _ = await context.NotificationSendLog
                    .Where(e => e.SentUtc < dayStart)
                    .ExecuteDeleteAsync(cancellationToken);

                // Count what the limits look at, inside the serialized transaction
                var existing = await context.NotificationPreferences.AsNoTracking()
                    .FirstOrDefaultAsync(p => p.UserId == request.UserId, cancellationToken);
                var userCount = await context.NotificationSendLog.CountAsync(
                    e => e.UserId == request.UserId && e.Kind == NotificationSendLogEntry.KindConfirmation && e.SentUtc >= dayStart,
                    cancellationToken);
                var addressCount = await context.NotificationSendLog.CountAsync(
                    e => e.ToAddress == request.Address && e.Kind == NotificationSendLogEntry.KindConfirmation && e.SentUtc >= dayStart,
                    cancellationToken);
                var serverCount = await context.NotificationSendLog.CountAsync(e => e.SentUtc >= hourStart, cancellationToken);
                var outcome = request.Decide(existing?.PendingSentUtc, userCount, addressCount, serverCount);
                if (outcome != ConfirmationReservation.Reserved)
                {
                    return ((outcome, 0), null);
                }

                // Count the mail first, so its identity is known when the pending state is stored
                var log = new NotificationSendLogEntry
                {
                    UserId = request.UserId,
                    ToAddress = request.Address,
                    Kind = NotificationSendLogEntry.KindConfirmation,
                    SentUtc = request.Now
                };
                context.NotificationSendLog.Add(log);
                await context.SaveChangesAsync(cancellationToken);

                // Store the pending state; the confirmed address, if any, stays untouched
                if (existing is null)
                {
                    context.NotificationPreferences.Add(new NotificationPreference
                    {
                        UserId = request.UserId,
                        PendingEmail = request.Address,
                        PendingCodeHash = request.CodeHash,
                        PendingCodeSalt = request.CodeSalt,
                        PendingCodeExpiresUtc = request.ExpiresUtc,
                        PendingAttempts = 0,
                        PendingSentUtc = request.Now,
                        Updated = request.Now
                    });
                }
                else
                {
                    _ = await context.NotificationPreferences
                        .Where(p => p.UserId == request.UserId)
                        .ExecuteUpdateAsync(
                            setters => setters
                                .SetProperty(p => p.PendingEmail, request.Address)
                                .SetProperty(p => p.PendingCodeHash, request.CodeHash)
                                .SetProperty(p => p.PendingCodeSalt, request.CodeSalt)
                                .SetProperty(p => p.PendingCodeExpiresUtc, request.ExpiresUtc)
                                .SetProperty(p => p.PendingAttempts, 0)
                                .SetProperty(p => p.PendingSentUtc, request.Now)
                                .SetProperty(p => p.Updated, request.Now),
                            cancellationToken);
                }

                return ((ConfirmationReservation.Reserved, log.Id), audit);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task AbortConfirmationAsync(
        int userId,
        int logId,
        string codeHash,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(codeHash);
        await using var context = await factory.CreateDbContextAsync(cancellationToken);

        // The mail was never sent: it is no longer counted, and its pending state is cleared unless a newer request
        // replaced it in the meantime
        _ = await context.NotificationSendLog
            .Where(e => e.Id == logId && e.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);
        _ = await context.NotificationPreferences
            .Where(p => p.UserId == userId && p.PendingCodeHash == codeHash)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(p => p.PendingEmail, (string?)null)
                    .SetProperty(p => p.PendingCodeHash, (string?)null)
                    .SetProperty(p => p.PendingCodeSalt, (string?)null)
                    .SetProperty(p => p.PendingCodeExpiresUtc, (DateTimeOffset?)null)
                    .SetProperty(p => p.PendingAttempts, 0)
                    .SetProperty(p => p.PendingSentUtc, (DateTimeOffset?)null),
                cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> TryCountAttemptAsync(
        int userId,
        string codeHash,
        int maxAttempts,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(codeHash);

        // One conditional update: it counts the attempt only for the code that waits and only while attempts are left
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        var changed = await context.NotificationPreferences
            .Where(p => p.UserId == userId && p.PendingCodeHash == codeHash && p.PendingAttempts < maxAttempts)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(p => p.PendingAttempts, p => p.PendingAttempts + 1),
                cancellationToken);
        return changed == 1;
    }

    /// <inheritdoc />
    public async Task<bool> ConfirmAsync(
        int userId,
        string codeHash,
        DateTimeOffset now,
        AuditDraft audit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(codeHash);
        ArgumentNullException.ThrowIfNull(audit);
        return await appender.ExecuteAsync<bool>(
            factory,
            async context =>
            {
                // The waiting address becomes the confirmed one, only for the code that waits, only once, and only
                // while the code has not expired at this moment (the expiry is stored as sortable text, so it can be
                // compared in the same conditional update that makes the change)
                var changed = await context.NotificationPreferences
                    .Where(p => p.UserId == userId
                                && p.PendingCodeHash == codeHash
                                && p.PendingEmail != null
                                && p.PendingCodeExpiresUtc > now)
                    .ExecuteUpdateAsync(
                        setters => setters
                            .SetProperty(p => p.Email, p => p.PendingEmail)
                            .SetProperty(p => p.Generation, p => p.Generation + 1)
                            .SetProperty(p => p.PendingEmail, (string?)null)
                            .SetProperty(p => p.PendingCodeHash, (string?)null)
                            .SetProperty(p => p.PendingCodeSalt, (string?)null)
                            .SetProperty(p => p.PendingCodeExpiresUtc, (DateTimeOffset?)null)
                            .SetProperty(p => p.PendingAttempts, 0)
                            .SetProperty(p => p.PendingSentUtc, (DateTimeOffset?)null)
                            .SetProperty(p => p.Updated, now),
                        cancellationToken);
                return changed == 1 ? (true, audit) : (false, null);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> RemoveAsync(int userId, AuditDraft audit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(audit);
        return await appender.ExecuteAsync<bool>(
            factory,
            async context =>
            {
                var removed = await context.NotificationPreferences
                    .Where(p => p.UserId == userId)
                    .ExecuteDeleteAsync(cancellationToken);

                // The mode goes with the row, so what the user was told goes with it
                _ = await context.NotifiedItems
                    .Where(i => i.UserId == userId)
                    .ExecuteDeleteAsync(cancellationToken);
                return removed > 0 ? (true, audit) : (false, null);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> SetModeAsync(
        int userId,
        NotificationMode mode,
        DateTimeOffset nowUtc,
        AuditDraft audit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(audit);
        return await appender.ExecuteAsync<bool>(
            factory,
            async context =>
            {
                var row = await context.NotificationPreferences.AsNoTracking()
                    .FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);
                if (row is null || (mode != NotificationMode.None && row.Email is null) || row.Mode == mode)
                {
                    return (false, null);
                }

                // Switching on from none marks the baseline as pending; between the active modes the mark stays as it is,
                // so a baseline that no pass recorded yet is not lost and the backlog is not mailed; switching to none
                // clears it. Every change starts with no window and no failure
                var baseline = mode != NotificationMode.None && (row.Mode == NotificationMode.None || row.BaselinePending);
                _ = await context.NotificationPreferences
                    .Where(p => p.UserId == userId)
                    .ExecuteUpdateAsync(
                        setters => setters
                            .SetProperty(p => p.Mode, mode)
                            .SetProperty(p => p.Generation, p => p.Generation + 1)
                            .SetProperty(p => p.BaselinePending, baseline)
                            .SetProperty(p => p.BatchStartedUtc, (DateTimeOffset?)null)
                            .SetProperty(p => p.FailureCount, 0)
                            .SetProperty(p => p.NextAttemptUtc, (DateTimeOffset?)null)
                            .SetProperty(p => p.Updated, nowUtc),
                        cancellationToken);
                if (mode == NotificationMode.None)
                {
                    // Nothing is mailed any more, so nothing is remembered
                    _ = await context.NotificationPreferences
                        .Where(p => p.UserId == userId)
                        .ExecuteUpdateAsync(
                            setters => setters.SetProperty(p => p.DigestHandledUtc, (DateTimeOffset?)null),
                            cancellationToken);
                    _ = await context.NotifiedItems
                        .Where(i => i.UserId == userId)
                        .ExecuteDeleteAsync(cancellationToken);
                }

                return (true, audit);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<NotificationRecipient>> ListRecipientsAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        var rows = await (
                from preference in context.NotificationPreferences.AsNoTracking()
                join user in context.Users.AsNoTracking() on preference.UserId equals user.Id
                where preference.Mode != NotificationMode.None && preference.Email != null
                orderby preference.UserId
                select new
                {
                    preference.UserId,
                    user.Username,
                    user.DisplayName,
                    preference.Email,
                    preference.Mode,
                    preference.BaselinePending,
                    preference.BatchStartedUtc,
                    preference.DigestHandledUtc,
                    preference.FailureCount,
                    preference.NextAttemptUtc,
                    IsDisabled = !user.Enabled,
                    user.MustChangePassword,
                    preference.Generation
                })
            .ToListAsync(cancellationToken);
        return
        [
            .. rows.Select(r => new NotificationRecipient(
                r.UserId,
                r.Username,
                r.DisplayName,
                r.Email!,
                r.Mode,
                new NotificationDispatchState(
                    r.BaselinePending,
                    r.BatchStartedUtc,
                    r.DigestHandledUtc,
                    r.FailureCount,
                    r.NextAttemptUtc),
                r.IsDisabled,
                r.MustChangePassword,
                r.Generation))
        ];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<NotifiedItem>> GetNotifiedAsync(int userId, CancellationToken cancellationToken = default)
    {
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        return await context.NotifiedItems.AsNoTracking()
            .Where(i => i.UserId == userId)
            .OrderBy(i => i.Id)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> ApplyAsync(int userId, NotificationCommit commit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(commit);
        return await appender.ExecuteAsync<bool>(
            factory,
            async context =>
            {
                // The conditional update that writes the state of the dispatcher is also the guard: the commit was
                // prepared for one confirmed address and one generation of the preference, and a change of either one
                // (a replaced address, a mode that was switched off and on again) makes it stale. A stale commit
                // changes nothing, so no item, state or mail of it is recorded
                var state = commit.State;
                var address = commit.Address;
                var generation = commit.Generation;
                var changed = await context.NotificationPreferences
                    .Where(p => p.UserId == userId
                                && p.Mode != NotificationMode.None
                                && p.Email == address
                                && p.Generation == generation)
                    .ExecuteUpdateAsync(
                        setters => setters
                            .SetProperty(p => p.BaselinePending, state.BaselinePending)
                            .SetProperty(p => p.BatchStartedUtc, state.BatchStartedUtc)
                            .SetProperty(p => p.DigestHandledUtc, state.DigestHandledUtc)
                            .SetProperty(p => p.FailureCount, state.FailureCount)
                            .SetProperty(p => p.NextAttemptUtc, state.NextAttemptUtc)
                            .SetProperty(p => p.Updated, commit.NowUtc),
                        cancellationToken);
                if (changed != 1)
                {
                    return (false, null);
                }

                await ApplyItemsAsync(context, userId, commit, cancellationToken);

                // Rows that can not count towards a limit any more go; the mail itself was counted when it was reserved
                var dayStart = commit.NowUtc - _day;
                _ = await context.NotificationSendLog
                    .Where(e => e.SentUtc < dayStart)
                    .ExecuteDeleteAsync(cancellationToken);
                return (true, null);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<(SendReservation Outcome, int LogId)> ReserveSendAsync(
        int userId,
        string kind,
        string address,
        DateTimeOffset now,
        int maxPerHour,
        int maxPerUserPerDay,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(kind);
        ArgumentNullException.ThrowIfNull(address);
        return await appender.ExecuteAsync<(SendReservation, int)>(
            factory,
            async context =>
            {
                // Rows older than one day can not count towards any limit; the count and the insert run in the same
                // serialized transaction, so two senders can not both pass the same count
                var dayStart = now - _day;
                var hourStart = now - _hour;
                _ = await context.NotificationSendLog
                    .Where(e => e.SentUtc < dayStart)
                    .ExecuteDeleteAsync(cancellationToken);
                var serverCount = await context.NotificationSendLog.CountAsync(e => e.SentUtc >= hourStart, cancellationToken);
                var userCount = await context.NotificationSendLog.CountAsync(
                    e => e.UserId == userId && e.SentUtc >= dayStart,
                    cancellationToken);
                var outcome = SendLimits.Decide(serverCount, userCount, maxPerHour, maxPerUserPerDay);
                if (outcome != SendReservation.Reserved)
                {
                    return ((outcome, 0), null);
                }

                var log = new NotificationSendLogEntry
                {
                    UserId = userId,
                    ToAddress = address,
                    Kind = kind,
                    SentUtc = now
                };
                context.NotificationSendLog.Add(log);
                await context.SaveChangesAsync(cancellationToken);
                return ((SendReservation.Reserved, log.Id), null);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task ReleaseSendAsync(int userId, int logId, CancellationToken cancellationToken = default)
    {
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        _ = await context.NotificationSendLog
            .Where(e => e.Id == logId && e.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<int> CountSentAsync(int? userId, DateTimeOffset sinceUtc, CancellationToken cancellationToken = default)
    {
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        var query = context.NotificationSendLog.AsNoTracking().Where(e => e.SentUtc >= sinceUtc);
        if (userId is { } id)
        {
            query = query.Where(e => e.UserId == id);
        }

        return await query.CountAsync(cancellationToken);
    }

    /// <summary>
    ///     Changes the notified items of a user as the commit says; the caller owns the transaction.
    /// </summary>
    /// <param name="context">The context of the open transaction.</param>
    /// <param name="userId">The user.</param>
    /// <param name="commit">The commit.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the changes are made (new rows are saved with the transaction).</returns>
    private static async Task ApplyItemsAsync(
        ReviewCenterDbContext context,
        int userId,
        NotificationCommit commit,
        CancellationToken cancellationToken)
    {
        if (commit.ReplaceAll is { } all)
        {
            // The baseline: exactly this set remains
            _ = await context.NotifiedItems.Where(i => i.UserId == userId).ExecuteDeleteAsync(cancellationToken);
            foreach (var entry in all)
            {
                context.NotifiedItems.Add(NewItem(userId, entry, commit.NowUtc));
            }

            return;
        }

        var existing = await context.NotifiedItems.AsNoTracking()
            .Where(i => i.UserId == userId)
            .ToListAsync(cancellationToken);
        var byKey = existing.ToDictionary(i => new NotifiedKey(i.ProjectId, i.ReviewId, i.Section, i.Kind), i => i.Id);

        // Forget what left the inbox
        var removeIds = commit.RemoveKeys.Where(byKey.ContainsKey).Select(key => byKey[key]).ToList();
        if (removeIds.Count > 0)
        {
            _ = await context.NotifiedItems
                .Where(i => removeIds.Contains(i.Id))
                .ExecuteDeleteAsync(cancellationToken);
        }

        // Update what changed state; insert what is new
        foreach (var entry in commit.Upserts)
        {
            if (byKey.TryGetValue(entry.Key, out var id) && !removeIds.Contains(id))
            {
                var state = entry.State;
                var gaveUp = entry.GaveUp;
                var notified = commit.NowUtc;
                _ = await context.NotifiedItems
                    .Where(i => i.Id == id)
                    .ExecuteUpdateAsync(
                        setters => setters
                            .SetProperty(i => i.State, state)
                            .SetProperty(i => i.GaveUp, gaveUp)
                            .SetProperty(i => i.NotifiedUtc, notified),
                        cancellationToken);
            }
            else
            {
                context.NotifiedItems.Add(NewItem(userId, entry, commit.NowUtc));
            }
        }
    }

    /// <summary>
    ///     Builds the row of a notified item.
    /// </summary>
    /// <param name="userId">The user.</param>
    /// <param name="entry">The item and its state.</param>
    /// <param name="nowUtc">The time to store.</param>
    /// <returns>The new row.</returns>
    private static NotifiedItem NewItem(int userId, NotifiedEntry entry, DateTimeOffset nowUtc)
    {
        return new NotifiedItem
        {
            UserId = userId,
            ProjectId = entry.Key.ProjectId,
            ReviewId = entry.Key.ReviewId,
            Section = entry.Key.Section,
            Kind = entry.Key.Kind,
            State = entry.State,
            GaveUp = entry.GaveUp,
            NotifiedUtc = nowUtc
        };
    }
}
