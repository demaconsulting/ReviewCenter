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
///     The <see cref="IAuditStore"/> implementation backed by <see cref="ReviewCenterDbContext"/>.
/// </summary>
/// <remarks>
///     The store only appends and reads: it has no method that updates or deletes an entry. A standalone
///     append takes the shared <see cref="AuditAppender"/> gate and a write transaction, like every change that
///     carries an audit entry, so the chain stays linear. Reads are untracked and never take the gate, and the
///     verification and export reads return one ordered batch at a time so the whole trail is never in memory.
/// </remarks>
/// <param name="factory">The factory that creates a context per operation.</param>
/// <param name="appender">The appender that owns the gate and builds the chain.</param>
internal sealed class AuditStore(IDbContextFactory<ReviewCenterDbContext> factory, AuditAppender appender) : IAuditStore
{
    /// <summary>
    ///     The escape character of the <c>LIKE</c> pattern of the actor text.
    /// </summary>
    private const string LikeEscape = "\\";

    /// <inheritdoc />
    public async Task<AuditEntry> AppendAsync(AuditDraft draft, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);

        await appender.Gate.WaitAsync(cancellationToken);
        try
        {
            await using var context = await factory.CreateDbContextAsync(cancellationToken);
            await using var transaction = await context.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);
            var entry = await appender.AppendAsync(context, draft, cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return entry;
        }
        finally
        {
            appender.Gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AuditEntry>> ListAsync(
        AuditFilter filter,
        long? beforeSequence,
        long? afterSequence,
        int take,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        var query = Filter(context.AuditEntries.AsNoTracking(), filter);

        if (beforeSequence is { } before)
        {
            query = query.Where(a => a.Sequence < before);
        }

        // The newer page is the nearest entries above the position: read them oldest first, then show newest first
        if (afterSequence is { } after)
        {
            var newer = await query.Where(a => a.Sequence > after)
                .OrderBy(a => a.Sequence)
                .Take(take)
                .ToListAsync(cancellationToken);
            newer.Reverse();
            return newer;
        }

        return await query.OrderByDescending(a => a.Sequence).Take(take).ToListAsync(cancellationToken);
    }

    /// <summary>
    ///     Narrows the entries to those that match a filter.
    /// </summary>
    /// <param name="query">The entries to narrow.</param>
    /// <param name="filter">The filter; every set part is applied.</param>
    /// <returns>The narrowed query (no ordering, no position, no limit).</returns>
    /// <remarks>
    ///     A project filter compares the project column, which is <c>NULL</c> for a global event, so a global event
    ///     can never match a project (the scoping rule needs no extra test). The actor text is a case-insensitive
    ///     substring of the stored actor text, matched with <c>LIKE</c> and an escape character so the user's text is
    ///     literal. An action prefix uses <c>StartsWith</c>, which the provider turns into a literal comparison. The
    ///     time range compares the stored UTC text of the timestamp (see <see cref="ReviewCenterDbContext"/>): the
    ///     first instant is inclusive and the end is the start of the day after the last day, exclusive.
    /// </remarks>
    private static IQueryable<AuditEntry> Filter(IQueryable<AuditEntry> query, AuditFilter filter)
    {
        if (filter.ProjectId is { } project)
        {
            query = query.Where(a => a.ProjectId == project);
        }

        if (filter.ReviewId is { } review)
        {
            query = query.Where(a => a.ReviewId == review);
        }

        if (filter.ActorUserId is { } actorUserId)
        {
            query = query.Where(a => a.ActorUserId == actorUserId);
        }

        if (filter.ActorText is { } actorText)
        {
            var pattern = "%" + EscapeLike(actorText) + "%";
            query = query.Where(a => EF.Functions.Like(a.ActorName, pattern, LikeEscape));
        }

        if (filter.Action is { } action)
        {
            query = query.Where(a => a.Action == action);
        }

        if (filter.ActionPrefix is { } prefix)
        {
            query = query.Where(a => a.Action.StartsWith(prefix));
        }

        if (filter.FromUtc is { } from)
        {
            query = query.Where(a => a.TimestampUtc >= from);
        }

        if (filter.ToExclusiveUtc is { } until)
        {
            query = query.Where(a => a.TimestampUtc < until);
        }

        return query;
    }

    /// <summary>
    ///     Escapes the characters that have a meaning in a <c>LIKE</c> pattern, so the text matches literally.
    /// </summary>
    /// <param name="text">The text to match.</param>
    /// <returns>The text with <c>\</c>, <c>%</c> and <c>_</c> each preceded by the escape character.</returns>
    private static string EscapeLike(string text)
    {
        return text.Replace(LikeEscape, LikeEscape + LikeEscape, StringComparison.Ordinal)
            .Replace("%", LikeEscape + "%", StringComparison.Ordinal)
            .Replace("_", LikeEscape + "_", StringComparison.Ordinal);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AuditEntry>> ReadAfterAsync(
        long afterSequence,
        int count,
        CancellationToken cancellationToken = default)
    {
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        return await context.AuditEntries.AsNoTracking()
            .Where(a => a.Sequence > afterSequence)
            .OrderBy(a => a.Sequence)
            .Take(count)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AuditEntry>> ReadMatchingAsync(
        AuditFilter filter,
        long afterSequence,
        long upToSequence,
        int count,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        await using var context = await factory.CreateDbContextAsync(cancellationToken);

        // Keyset batch in chain order: the position is the sequence, so no offset is ever skipped over
        return await Filter(context.AuditEntries.AsNoTracking(), filter)
            .Where(a => a.Sequence > afterSequence && a.Sequence <= upToSequence)
            .OrderBy(a => a.Sequence)
            .Take(count)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<long> LatestSequenceAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        return await context.AuditEntries.AsNoTracking().MaxAsync(a => (long?)a.Sequence, cancellationToken) ?? 0;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, DateTimeOffset>> LastActivityAsync(
        int projectId,
        CancellationToken cancellationToken = default)
    {
        await using var context = await factory.CreateDbContextAsync(cancellationToken);

        // The newest entry of a review is the one with the highest sequence (the index of project, review and
        // sequence serves the grouping); the time is read from those entries, so no time value is aggregated
        var latest = context.AuditEntries.AsNoTracking()
            .Where(a => a.ProjectId == projectId && a.ReviewId != null)
            .GroupBy(a => a.ReviewId)
            .Select(g => g.Max(a => a.Sequence));
        var rows = await context.AuditEntries.AsNoTracking()
            .Where(a => latest.Contains(a.Sequence))
            .Select(a => new { a.ReviewId, a.TimestampUtc })
            .ToListAsync(cancellationToken);
        return rows.ToDictionary(row => row.ReviewId!, row => row.TimestampUtc);
    }
}
