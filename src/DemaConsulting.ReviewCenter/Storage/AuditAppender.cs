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

using System.Data;
using DemaConsulting.ReviewCenter.Core;
using Microsoft.EntityFrameworkCore;

namespace DemaConsulting.ReviewCenter.Storage;

/// <summary>
///     Appends audit entries to the hash chain, one at a time, in the same transaction as the change they describe.
/// </summary>
/// <remarks>
///     The chain is linear, so two appends must never run at once. One process-wide gate (a semaphore) serializes
///     every transaction that writes an audit entry, whichever store it comes from; the gate is acquired first
///     and released last, and nothing that can wait for another lock (a service gate) is called while it is
///     held, so no lock cycle is possible. Inside the gate the appender reads the head of the chain, stamps the
///     time, hashes the entry and adds it to the caller's context; the transaction is a SQLite <c>BEGIN IMMEDIATE</c>,
///     so a second process (the command line) is serialized by the database lock as well. The primary key on the
///     sequence is the last line of defense: a duplicate sequence can never be stored. The class is a singleton
///     shared by all stores and is thread-safe.
/// </remarks>
/// <param name="timeProvider">The clock that stamps entries; injected so tests control time.</param>
internal sealed class AuditAppender(TimeProvider timeProvider)
{
    /// <summary>
    ///     Gets the process-wide gate that serializes every write transaction that carries an audit entry.
    /// </summary>
    public SemaphoreSlim Gate { get; } = new(1, 1);

    /// <summary>
    ///     Runs a change and its audit entry in one transaction.
    /// </summary>
    /// <typeparam name="TResult">The type of result the change produces.</typeparam>
    /// <param name="factory">The factory that creates the context for the transaction.</param>
    /// <param name="work">
    ///     Makes the change in the given context and returns its result together with the audit draft; a
    ///     <c>null</c> draft means the change did not take effect or is not audited, and no entry is appended.
    ///     The work may save changes itself (for example to learn an assigned identity); everything is still
    ///     inside the transaction. When it throws, nothing is stored.
    /// </param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The result of the change.</returns>
    public async Task<TResult> ExecuteAsync<TResult>(
        IDbContextFactory<ReviewCenterDbContext> factory,
        Func<ReviewCenterDbContext, Task<(TResult Result, AuditDraft? Audit)>> work,
        CancellationToken cancellationToken)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            await using var context = await factory.CreateDbContextAsync(cancellationToken);

            // Serializable makes SQLite take the write lock when the transaction begins, so no other process can
            // append between reading the head of the chain and saving the new entry
            await using var transaction = await context.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);
            var (result, audit) = await work(context);
            if (audit is not null)
            {
                await AppendAsync(context, audit, cancellationToken);
            }

            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>
    ///     Adds the next entry of the chain to a context; the caller saves and commits.
    /// </summary>
    /// <param name="context">The context of the open transaction. The caller holds <see cref="Gate"/>.</param>
    /// <param name="draft">What to record. Must not be null.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The entry that was added (not yet saved).</returns>
    public async Task<AuditEntry> AppendAsync(
        ReviewCenterDbContext context,
        AuditDraft draft,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);

        // The head of the chain: the newest entry, or the genesis state of an empty trail
        var head = await context.AuditEntries.AsNoTracking()
            .OrderByDescending(a => a.Sequence)
            .Select(a => new { a.Sequence, a.Hash })
            .FirstOrDefaultAsync(cancellationToken);

        // The acting user, when the actor is a known user (the user name column compares without regard to case)
        int? actorUserId = null;
        if (draft.ActorUsername is { } username)
        {
            actorUserId = await context.Users.AsNoTracking()
                .Where(u => u.Username == username)
                .Select(u => (int?)u.Id)
                .FirstOrDefaultAsync(cancellationToken);
        }

        // Build the entry without its hash, then hash exactly what will be stored
        var pending = new AuditEntry
        {
            Sequence = (head?.Sequence ?? 0) + 1,
            TimestampUtc = timeProvider.GetUtcNow(),
            ActorUserId = actorUserId,
            ActorName = AuditText.Clean(draft.ActorName, AuditText.MaxActorNameLength),
            Action = AuditText.Clean(draft.Action, AuditText.MaxActionLength),
            ProjectId = draft.ProjectId,
            ReviewId = draft.ReviewId,
            Detail = AuditText.Clean(draft.Detail, AuditText.MaxDetailLength),
            PreviousHash = head?.Hash ?? AuditChain.GenesisHash
        };
        var entry = new AuditEntry
        {
            Sequence = pending.Sequence,
            TimestampUtc = pending.TimestampUtc,
            ActorUserId = pending.ActorUserId,
            ActorName = pending.ActorName,
            Action = pending.Action,
            ProjectId = pending.ProjectId,
            ReviewId = pending.ReviewId,
            Detail = pending.Detail,
            PreviousHash = pending.PreviousHash,
            Hash = AuditChain.ComputeHash(pending)
        };
        context.AuditEntries.Add(entry);
        return entry;
    }
}
