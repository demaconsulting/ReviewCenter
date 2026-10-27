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
///     The <see cref="IPersonalAccessTokenStore"/> implementation backed by <see cref="ReviewCenterDbContext"/>.
/// </summary>
/// <remarks>
///     Each operation uses its own short-lived context from the factory, so the store is safe for concurrent use.
///     Reads are untracked, so every read returns a new object. A change that is audited (add, revoke) runs in one
///     transaction together with its audit entry; recording a use changes only the last-used column and writes no
///     entry. The SQLite provider cannot compare or order <see cref="DateTimeOffset"/> values in a query, so rows
///     are ordered in memory (a user has at most a few dozen tokens).
/// </remarks>
/// <param name="factory">The factory that creates a context per operation.</param>
/// <param name="appender">The appender that runs each change together with its audit entry.</param>
internal sealed class PersonalAccessTokenStore(IDbContextFactory<ReviewCenterDbContext> factory, AuditAppender appender)
    : IPersonalAccessTokenStore
{
    /// <inheritdoc />
    public async Task<PersonalAccessToken> AddAsync(
        PersonalAccessToken token,
        Func<PersonalAccessToken, AuditDraft> audit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(token);
        ArgumentNullException.ThrowIfNull(audit);

        return await appender.ExecuteAsync<PersonalAccessToken>(
            factory,
            async context =>
            {
                // Insert a fresh entity so that the database always assigns the identity, whatever the caller supplied
                var entity = new PersonalAccessToken
                {
                    UserId = token.UserId,
                    Name = token.Name,
                    DisplayId = token.DisplayId,
                    TokenHash = token.TokenHash,
                    Created = token.Created,
                    Expires = token.Expires,
                    LastUsed = token.LastUsed,
                    Revoked = token.Revoked
                };
                context.PersonalAccessTokens.Add(entity);

                // Saved now to learn the identity; the audit entry joins the same transaction
                await context.SaveChangesAsync(cancellationToken);
                return (entity, audit(entity));
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PersonalAccessToken>> ListForUserAsync(
        int userId,
        CancellationToken cancellationToken = default)
    {
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        var tokens = await context.PersonalAccessTokens.AsNoTracking()
            .Where(t => t.UserId == userId)
            .ToListAsync(cancellationToken);

        // Newest first; ordered here because the provider cannot order by a DateTimeOffset
        return tokens.OrderByDescending(t => t.Created).ThenByDescending(t => t.Id).ToList();
    }

    /// <inheritdoc />
    public async Task<PersonalAccessToken?> FindByHashAsync(
        string tokenHash,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tokenHash);

        // The unique index on the hash makes this a single indexed lookup
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        return await context.PersonalAccessTokens.AsNoTracking()
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<PersonalAccessToken?> RevokeAsync(
        int tokenId,
        int userId,
        DateTimeOffset when,
        Func<PersonalAccessToken, AuditDraft> audit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(audit);

        return await appender.ExecuteAsync<PersonalAccessToken?>(
            factory,
            async context =>
            {
                // Only the owner's own, not yet revoked token is changed; anything else changes nothing (no audit entry)
                var entity = await context.PersonalAccessTokens
                    .FirstOrDefaultAsync(t => t.Id == tokenId && t.UserId == userId, cancellationToken);
                if (entity is null || entity.Revoked is not null)
                {
                    return (null, null);
                }

                entity.Revoked = when;
                return (entity, audit(entity));
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task TouchAsync(int tokenId, DateTimeOffset when, CancellationToken cancellationToken = default)
    {
        // A targeted update of one column: no entity is loaded, so a concurrent revoke is never overwritten
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        await context.PersonalAccessTokens
            .Where(t => t.Id == tokenId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(t => t.LastUsed, when), cancellationToken);
    }
}
