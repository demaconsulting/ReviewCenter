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
///     The <see cref="IUserStore"/> implementation backed by <see cref="ReviewCenterDbContext"/>.
/// </summary>
/// <remarks>
///     Each operation uses its own short-lived context from the factory, so the store is safe for
///     concurrent use by all browser sessions. Reads are untracked, so every read returns a new object.
///     The unique, case-insensitive index on the user name is translated to a <see cref="DomainRuleException"/>
///     so callers see a domain error, not a database one.
/// </remarks>
/// <param name="factory">The factory that creates a context per operation.</param>
internal sealed class UserStore(IDbContextFactory<ReviewCenterDbContext> factory) : IUserStore
{
    /// <summary>
    ///     The SQLite extended result code for a unique constraint violation.
    /// </summary>
    private const int SqliteUniqueViolation = 2067;

    /// <inheritdoc />
    public async Task<User> AddAsync(User user, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);

        await using var context = await factory.CreateDbContextAsync(cancellationToken);

        // Insert a fresh entity so that the database always assigns the identity, whatever the caller supplied
        var entity = new User
        {
            Username = user.Username,
            DisplayName = user.DisplayName,
            Role = user.Role,
            Enabled = user.Enabled,
            PasswordHash = user.PasswordHash,
            FailedLoginCount = user.FailedLoginCount,
            LockoutUntil = user.LockoutUntil,
            Created = user.Created
        };
        context.Users.Add(entity);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqliteException { SqliteExtendedErrorCode: SqliteUniqueViolation })
        {
            throw new DomainRuleException(
                DomainRule.UserNameTaken,
                $"A user with the name '{user.Username}' already exists.");
        }

        return entity;
    }

    /// <inheritdoc />
    public async Task<User?> GetByUsernameAsync(string username, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(username);

        // The column collation (NOCASE) makes the comparison case-insensitive
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        return await context.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Username == username, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<User>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        return await context.Users.AsNoTracking()
            .OrderBy(u => u.Username)
            .ThenBy(u => u.Id)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task UpdateAsync(User user, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);

        // The user is a detached copy that was read earlier; Update marks every column of that row as changed
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        context.Users.Update(user);
        await context.SaveChangesAsync(cancellationToken);
    }
}
