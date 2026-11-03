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
            // Touch both tables; a database that was created before them fails with "no such table"
            _ = await context.NotificationPreferences.AsNoTracking().AnyAsync(cancellationToken);
            _ = await context.NotificationSendLog.AsNoTracking().AnyAsync(cancellationToken);
            return true;
        }
        catch (SqliteException ex) when (IsMissingTable(ex))
        {
            return false;
        }
    }

    /// <summary>
    ///     Tells whether a database failure is the one of a missing table (SQLite error 1, "no such table"), which is
    ///     what a database from before e-mail notifications reports. Every other failure is a real fault.
    /// </summary>
    /// <param name="exception">The failure.</param>
    /// <returns><c>true</c> when a table does not exist.</returns>
    private static bool IsMissingTable(SqliteException exception)
    {
        return exception.SqliteErrorCode == MissingTableErrorCode
               && exception.Message.Contains("no such table", StringComparison.OrdinalIgnoreCase);
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
                return removed > 0 ? (true, audit) : (false, null);
            },
            cancellationToken);
    }
}
