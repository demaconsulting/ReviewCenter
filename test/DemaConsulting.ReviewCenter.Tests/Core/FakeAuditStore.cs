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
///     In-memory <see cref="IAuditStore"/> used to test the audit service and the pages without a database.
/// </summary>
/// <remarks>
///     It builds the real hash chain (with <see cref="AuditChain"/>), so the entries it holds verify exactly as
///     the stored ones do. Tests can make the next append fail, and can replace a stored entry to simulate
///     tampering. Like the real store, which serializes appends, every access is serialized, so parallel appends
///     all land in one gap-free chain.
/// </remarks>
/// <param name="timeProvider">The clock that stamps the entries.</param>
internal sealed class FakeAuditStore(TimeProvider timeProvider) : IAuditStore
{
    /// <summary>
    ///     The stored entries in sequence order.
    /// </summary>
    private readonly List<AuditEntry> _entries = [];

    /// <summary>
    ///     Serializes every access to the entries and the counters.
    /// </summary>
    private readonly object _lock = new();

    /// <summary>
    ///     The number of read batches requested for verification.
    /// </summary>
    private int _readAfterCalls;

    /// <summary>
    ///     The largest batch size any verification read asked for.
    /// </summary>
    private int _largestReadCount;

    /// <summary>
    ///     The number of filtered batches requested for an export.
    /// </summary>
    private int _matchingReads;

    /// <summary>
    ///     The largest batch size any export read asked for.
    /// </summary>
    private int _largestMatchingCount;

    /// <summary>
    ///     Gets or sets the exception thrown by the next append, or <c>null</c> for none; it is cleared when thrown.
    /// </summary>
    public Exception? FailNextAppend { get; set; }

    /// <summary>
    ///     Gets or sets what runs after each export batch was read, with the number of the batch (1 for the first);
    ///     <c>null</c> for nothing. It runs outside the lock, so it may append entries or cancel an operation.
    /// </summary>
    public Action<int>? AfterMatchingRead { get; set; }

    /// <summary>
    ///     Gets the number of filtered batches requested for an export.
    /// </summary>
    public int MatchingReads
    {
        get
        {
            lock (_lock)
            {
                return _matchingReads;
            }
        }
    }

    /// <summary>
    ///     Gets the largest batch size any export read asked for.
    /// </summary>
    public int LargestMatchingCount
    {
        get
        {
            lock (_lock)
            {
                return _largestMatchingCount;
            }
        }
    }

    /// <summary>
    ///     Gets a snapshot of the stored entries in sequence order.
    /// </summary>
    public IReadOnlyList<AuditEntry> Entries
    {
        get
        {
            lock (_lock)
            {
                return _entries.ToArray();
            }
        }
    }

    /// <summary>
    ///     Gets the number of read batches requested for verification.
    /// </summary>
    public int ReadAfterCalls
    {
        get
        {
            lock (_lock)
            {
                return _readAfterCalls;
            }
        }
    }

    /// <summary>
    ///     Gets the largest batch size any verification read asked for.
    /// </summary>
    public int LargestReadCount
    {
        get
        {
            lock (_lock)
            {
                return _largestReadCount;
            }
        }
    }

    /// <inheritdoc />
    public Task<AuditEntry> AppendAsync(AuditDraft draft, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (FailNextAppend is { } failure)
            {
                FailNextAppend = null;
                throw failure;
            }

            var head = _entries.Count == 0 ? null : _entries[^1];
            var entry = new AuditEntry
            {
                Sequence = (head?.Sequence ?? 0) + 1,
                TimestampUtc = timeProvider.GetUtcNow(),
                ActorUserId = null,
                ActorName = AuditText.Clean(draft.ActorName, AuditText.MaxActorNameLength),
                Action = AuditText.Clean(draft.Action, AuditText.MaxActionLength),
                ProjectId = draft.ProjectId,
                ReviewId = draft.ReviewId,
                Detail = AuditText.Clean(draft.Detail, AuditText.MaxDetailLength),
                PreviousHash = head?.Hash ?? AuditChain.GenesisHash
            };
            var stored = Rehash(entry);
            _entries.Add(stored);
            return Task.FromResult(stored);
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<AuditEntry>> ListAsync(
        AuditFilter filter,
        long? beforeSequence,
        long? afterSequence,
        int take,
        CancellationToken cancellationToken = default)
    {
        // Query a snapshot, so a parallel append cannot change the list while it is enumerated
        var query = Entries.Where(e => Matches(e, filter));

        if (beforeSequence is { } before)
        {
            query = query.Where(e => e.Sequence < before);
        }

        IReadOnlyList<AuditEntry> result = afterSequence is { } after
            ? query.Where(e => e.Sequence > after).OrderBy(e => e.Sequence).Take(take)
                .OrderByDescending(e => e.Sequence).ToList()
            : query.OrderByDescending(e => e.Sequence).Take(take).ToList();

        return Task.FromResult(result);
    }

    /// <summary>
    ///     Tells whether an entry matches a filter, like the real store: a project selects only entries of that
    ///     project (never global entries), the actor text is a case-insensitive literal substring.
    /// </summary>
    private static bool Matches(AuditEntry entry, AuditFilter filter)
    {
        return (filter.ProjectId is null || entry.ProjectId == filter.ProjectId)
               && (filter.ReviewId is null || entry.ReviewId == filter.ReviewId)
               && (filter.ActorUserId is null || entry.ActorUserId == filter.ActorUserId)
               && (filter.ActorText is null
                   || entry.ActorName.Contains(filter.ActorText, StringComparison.OrdinalIgnoreCase))
               && (filter.Action is null || entry.Action == filter.Action)
               && (filter.ActionPrefix is null || entry.Action.StartsWith(filter.ActionPrefix, StringComparison.Ordinal))
               && (filter.FromUtc is null || entry.TimestampUtc >= filter.FromUtc)
               && (filter.ToExclusiveUtc is null || entry.TimestampUtc < filter.ToExclusiveUtc);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<AuditEntry>> ReadAfterAsync(
        long afterSequence,
        int count,
        CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            _readAfterCalls++;
            _largestReadCount = Math.Max(_largestReadCount, count);
            IReadOnlyList<AuditEntry> result = _entries.Where(e => e.Sequence > afterSequence)
                .OrderBy(e => e.Sequence)
                .Take(count)
                .ToList();
            return Task.FromResult(result);
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<AuditEntry>> ReadMatchingAsync(
        AuditFilter filter,
        long afterSequence,
        long upToSequence,
        int count,
        CancellationToken cancellationToken = default)
    {
        int call;
        IReadOnlyList<AuditEntry> result;
        lock (_lock)
        {
            call = ++_matchingReads;
            _largestMatchingCount = Math.Max(_largestMatchingCount, count);
            result = _entries
                .Where(e => e.Sequence > afterSequence && e.Sequence <= upToSequence && Matches(e, filter))
                .OrderBy(e => e.Sequence)
                .Take(count)
                .ToList();
        }

        // The hook runs outside the lock so that it may append entries (the trail keeps growing during an export)
        AfterMatchingRead?.Invoke(call);
        return Task.FromResult(result);
    }

    /// <inheritdoc />
    public Task<long> LatestSequenceAsync(CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            return Task.FromResult(_entries.Count == 0 ? 0L : _entries[^1].Sequence);
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyDictionary<string, DateTimeOffset>> LastActivityAsync(
        int projectId,
        CancellationToken cancellationToken = default)
    {
        // Like the real store: the newest (highest sequence) entry of each review of the project
        IReadOnlyDictionary<string, DateTimeOffset> result = Entries
            .Where(e => e.ProjectId == projectId && e.ReviewId is not null)
            .GroupBy(e => e.ReviewId!)
            .ToDictionary(group => group.Key, group => group.MaxBy(e => e.Sequence)!.TimestampUtc);
        return Task.FromResult(result);
    }

    /// <summary>
    ///     Replaces the detail of a stored entry without updating its hash, as an attacker editing the database would.
    /// </summary>
    /// <param name="sequence">The sequence of the entry to change.</param>
    /// <param name="detail">The new detail text.</param>
    public void TamperDetail(long sequence, string detail)
    {
        lock (_lock)
        {
            var index = _entries.FindIndex(e => e.Sequence == sequence);
            var old = _entries[index];
            _entries[index] = new AuditEntry
            {
                Sequence = old.Sequence,
                TimestampUtc = old.TimestampUtc,
                ActorUserId = old.ActorUserId,
                ActorName = old.ActorName,
                Action = old.Action,
                ProjectId = old.ProjectId,
                ReviewId = old.ReviewId,
                Detail = detail,
                PreviousHash = old.PreviousHash,
                Hash = old.Hash
            };
        }
    }

    /// <summary>
    ///     Copies an entry with its hash computed from its fields.
    /// </summary>
    /// <param name="entry">The entry whose <see cref="AuditEntry.Hash"/> is not set.</param>
    /// <returns>The entry with its hash.</returns>
    private static AuditEntry Rehash(AuditEntry entry)
    {
        return new AuditEntry
        {
            Sequence = entry.Sequence,
            TimestampUtc = entry.TimestampUtc,
            ActorUserId = entry.ActorUserId,
            ActorName = entry.ActorName,
            Action = entry.Action,
            ProjectId = entry.ProjectId,
            ReviewId = entry.ReviewId,
            Detail = entry.Detail,
            PreviousHash = entry.PreviousHash,
            Hash = AuditChain.ComputeHash(entry)
        };
    }
}
