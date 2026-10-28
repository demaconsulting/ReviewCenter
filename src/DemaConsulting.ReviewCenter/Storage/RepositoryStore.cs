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
///     The <see cref="IRepositoryStore"/> implementation backed by <see cref="ReviewCenterDbContext"/>.
/// </summary>
/// <remarks>
///     Each operation uses its own short-lived context from the factory, so the store is safe for concurrent use.
///     Reads are untracked and return <see cref="SourceRepository"/> values, which cannot carry the encrypted
///     token; only <see cref="GetEncryptedTokenAsync"/> reads it. The unique, case-insensitive index on the name is
///     translated to a <see cref="DomainRuleException"/>. Every change is saved together with its audit entry.
/// </remarks>
/// <param name="factory">The factory that creates a context per operation.</param>
/// <param name="appender">The appender that runs each change together with its audit entry.</param>
internal sealed class RepositoryStore(IDbContextFactory<ReviewCenterDbContext> factory, AuditAppender appender) : IRepositoryStore
{
    /// <summary>
    ///     The SQLite extended result code for a unique constraint violation.
    /// </summary>
    private const int SqliteUniqueViolation = 2067;

    /// <inheritdoc />
    public async Task<SourceRepository> AddAsync(
        NewRepository repository,
        Func<SourceRepository, AuditDraft> audit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(audit);

        return await appender.ExecuteAsync<SourceRepository>(
            factory,
            async context =>
            {
                var record = new RepositoryRecord
                {
                    ProjectId = repository.ProjectId,
                    Name = repository.Name,
                    Location = repository.Location,
                    Username = repository.Username,
                    EncryptedToken = repository.EncryptedToken,
                    Enabled = true,
                    CreatedAt = repository.CreatedAt,
                    UpdatedAt = repository.CreatedAt
                };
                context.Repositories.Add(record);
                await SaveAsync(context, repository.Name, cancellationToken);
                var stored = ToModel(record);
                return (stored, audit(stored));
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<SourceRepository?> GetAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        var record = await context.Repositories.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        return record is null ? null : ToModel(record);
    }

    /// <inheritdoc />
    public async Task<string?> GetEncryptedTokenAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        return await context.Repositories.AsNoTracking()
            .Where(r => r.Id == id)
            .Select(r => r.EncryptedToken)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SourceRepository>> ListAsync(
        int projectId,
        CancellationToken cancellationToken = default)
    {
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        var records = await context.Repositories.AsNoTracking()
            .Where(r => r.ProjectId == projectId)
            .OrderBy(r => r.Name)
            .ThenBy(r => r.Id)
            .ToListAsync(cancellationToken);
        return records.Select(ToModel).ToList();
    }

    /// <inheritdoc />
    public async Task<SourceRepository> UpdateAsync(
        int id,
        RepositoryUpdate update,
        AuditDraft audit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        ArgumentNullException.ThrowIfNull(audit);

        return await appender.ExecuteAsync<SourceRepository>(
            factory,
            async context =>
            {
                var record = await context.Repositories.FirstOrDefaultAsync(r => r.Id == id, cancellationToken)
                    ?? throw new DomainRuleException(DomainRule.RepositoryNotFound, "The repository does not exist.");
                record.Name = update.Name;
                record.Location = update.Location;
                record.Username = update.Username;
                record.Enabled = update.Enabled;
                record.UpdatedAt = update.UpdatedAt;
                switch (update.Token.Action)
                {
                    case TokenAction.Replace:
                        record.EncryptedToken = update.Token.Token;
                        break;
                    case TokenAction.Clear:
                        record.EncryptedToken = null;
                        break;
                    default:
                        break;
                }

                await SaveAsync(context, update.Name, cancellationToken);
                return (ToModel(record), audit);
            },
            cancellationToken);
    }

    /// <summary>
    ///     Converts a row into the read model, which has no token member.
    /// </summary>
    /// <param name="record">The row.</param>
    /// <returns>The read model.</returns>
    private static SourceRepository ToModel(RepositoryRecord record) =>
        new(
            record.Id,
            record.ProjectId,
            record.Name,
            record.Location,
            record.Username,
            record.EncryptedToken is not null,
            record.Enabled,
            record.CreatedAt,
            record.UpdatedAt);

    /// <summary>
    ///     Saves the changes of a context, translating a duplicate name into a domain exception.
    /// </summary>
    /// <param name="context">The context.</param>
    /// <param name="name">The name that is being stored, for the message.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the changes are saved.</returns>
    /// <exception cref="DomainRuleException">Thrown with <see cref="DomainRule.RepositoryNameTaken"/> when the name exists.</exception>
    private static async Task SaveAsync(ReviewCenterDbContext context, string name, CancellationToken cancellationToken)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqliteException { SqliteExtendedErrorCode: SqliteUniqueViolation })
        {
            throw new DomainRuleException(
                DomainRule.RepositoryNameTaken,
                $"A repository with the name '{name}' already exists.");
        }
    }
}
