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
using Microsoft.EntityFrameworkCore;

namespace DemaConsulting.ReviewCenter.Storage;

/// <summary>
///     The <see cref="ITotpStore"/> implementation backed by <see cref="ReviewCenterDbContext"/>.
/// </summary>
/// <remarks>
///     Each operation uses its own short-lived context from the factory, so the store is safe for concurrent use.
///     Every decision that must have exactly one winner is one conditional <c>UPDATE</c> whose affected-row count is
///     the answer (no entity is loaded and written back), so two requests that race can not both succeed; the
///     conditions use only equality, null checks and integer comparison because the SQLite provider cannot compare
///     <see cref="DateTimeOffset"/> values in a query. A change that is audited runs in one transaction together with
///     its audit entry; the replay step is advanced without an entry (the sign-in records its own).
/// </remarks>
/// <param name="factory">The factory that creates a context per operation.</param>
/// <param name="appender">The appender that runs each change together with its audit entry.</param>
internal sealed class TotpStore(IDbContextFactory<ReviewCenterDbContext> factory, AuditAppender appender) : ITotpStore
{
    /// <inheritdoc />
    public async Task<UserTotp?> GetAsync(int userId, CancellationToken cancellationToken = default)
    {
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        return await context.UserTotps.AsNoTracking().FirstOrDefaultAsync(t => t.UserId == userId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlySet<int>> ListActiveUserIdsAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        var ids = await context.UserTotps.AsNoTracking()
            .Where(t => t.ConfirmedAt != null)
            .Select(t => t.UserId)
            .ToListAsync(cancellationToken);
        return ids.ToHashSet();
    }

    /// <inheritdoc />
    public async Task<bool> SavePendingAsync(UserTotp pending, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pending);
        await using var context = await factory.CreateDbContextAsync(cancellationToken);

        // An earlier pending enrollment is replaced; a confirmed one is never touched
        _ = await context.UserTotps
            .Where(t => t.UserId == pending.UserId && t.ConfirmedAt == null)
            .ExecuteDeleteAsync(cancellationToken);
        if (await context.UserTotps.AnyAsync(t => t.UserId == pending.UserId, cancellationToken))
        {
            return false;
        }

        context.UserTotps.Add(new UserTotp
        {
            UserId = pending.UserId,
            ProtectedSecret = pending.ProtectedSecret,
            Created = pending.Created,
            ConfirmedAt = null,
            LastAcceptedStep = 0
        });
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException)
        {
            // Another request confirmed an enrollment between the check and the insert; anything else is a real failure
            if (await context.UserTotps.AnyAsync(t => t.UserId == pending.UserId && t.ConfirmedAt != null, cancellationToken))
            {
                return false;
            }

            throw;
        }
    }

    /// <inheritdoc />
    public async Task<bool> ConfirmAsync(
        int userId,
        long acceptedStep,
        DateTimeOffset now,
        IReadOnlyList<string> recoveryHashes,
        AuditDraft audit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(recoveryHashes);
        ArgumentNullException.ThrowIfNull(audit);
        return await appender.ExecuteAsync<bool>(
            factory,
            async context =>
            {
                // Only a pending enrollment can be confirmed, and only one caller changes it
                var changed = await context.UserTotps
                    .Where(t => t.UserId == userId && t.ConfirmedAt == null)
                    .ExecuteUpdateAsync(
                        setters => setters
                            .SetProperty(t => t.ConfirmedAt, now)
                            .SetProperty(t => t.LastAcceptedStep, acceptedStep),
                        cancellationToken);
                if (changed == 0)
                {
                    return (false, null);
                }

                AddCodes(context, userId, recoveryHashes, now);
                return (true, audit);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> TryAdvanceStepAsync(int userId, long step, CancellationToken cancellationToken = default)
    {
        // One conditional update: it succeeds for exactly one caller per step, and only while the enrollment is
        // confirmed and the step is greater than the last accepted one (compare-and-set)
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        var changed = await context.UserTotps
            .Where(t => t.UserId == userId && t.ConfirmedAt != null && t.LastAcceptedStep < step)
            .ExecuteUpdateAsync(setters => setters.SetProperty(t => t.LastAcceptedStep, step), cancellationToken);
        return changed == 1;
    }

    /// <inheritdoc />
    public async Task<bool> TryConsumeRecoveryCodeAsync(
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
                // The code is spent by the conditional update; an unknown or used code changes nothing and records nothing
                var changed = await context.TotpRecoveryCodes
                    .Where(c => c.UserId == userId && c.CodeHash == codeHash && c.UsedAt == null)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(c => c.UsedAt, now), cancellationToken);
                return changed == 1 ? (true, audit) : (false, null);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> ReplaceRecoveryCodesAsync(
        int userId,
        IReadOnlyList<string> hashes,
        DateTimeOffset now,
        AuditDraft audit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(hashes);
        ArgumentNullException.ThrowIfNull(audit);
        return await appender.ExecuteAsync<bool>(
            factory,
            async context =>
            {
                if (!await context.UserTotps.AnyAsync(t => t.UserId == userId && t.ConfirmedAt != null, cancellationToken))
                {
                    return (false, null);
                }

                _ = await context.TotpRecoveryCodes.Where(c => c.UserId == userId).ExecuteDeleteAsync(cancellationToken);
                AddCodes(context, userId, hashes, now);
                return (true, audit);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<int> CountUnusedRecoveryCodesAsync(int userId, CancellationToken cancellationToken = default)
    {
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        return await context.TotpRecoveryCodes.CountAsync(c => c.UserId == userId && c.UsedAt == null, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> RemoveAsync(int userId, AuditDraft audit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(audit);
        return await appender.ExecuteAsync<bool>(
            factory,
            async context =>
            {
                _ = await context.TotpRecoveryCodes.Where(c => c.UserId == userId).ExecuteDeleteAsync(cancellationToken);
                var removed = await context.UserTotps.Where(t => t.UserId == userId).ExecuteDeleteAsync(cancellationToken);
                return removed > 0 ? (true, audit) : (false, null);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> RemovePendingAsync(int userId, CancellationToken cancellationToken = default)
    {
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        return await context.UserTotps
            .Where(t => t.UserId == userId && t.ConfirmedAt == null)
            .ExecuteDeleteAsync(cancellationToken) > 0;
    }

    /// <summary>
    ///     Adds the recovery codes to a context; the caller saves.
    /// </summary>
    /// <param name="context">The context.</param>
    /// <param name="userId">The identity of the user.</param>
    /// <param name="hashes">The hashes of the codes.</param>
    /// <param name="now">The moment the codes are generated.</param>
    private static void AddCodes(ReviewCenterDbContext context, int userId, IReadOnlyList<string> hashes, DateTimeOffset now)
    {
        foreach (var hash in hashes)
        {
            context.TotpRecoveryCodes.Add(new TotpRecoveryCode { UserId = userId, CodeHash = hash, Created = now });
        }
    }
}
