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

namespace DemaConsulting.ReviewCenter.Core;

/// <summary>
///     One page of the audit trail, newest entry first.
/// </summary>
/// <param name="Entries">The entries of the page, newest first.</param>
/// <param name="HasOlder"><c>true</c> when there are entries older than the page.</param>
/// <param name="HasNewer"><c>true</c> when there are entries newer than the page.</param>
public sealed record AuditEntryPage(IReadOnlyList<AuditEntry> Entries, bool HasOlder, bool HasNewer)
{
    /// <summary>
    ///     Gets the sequence to pass as <c>beforeSequence</c> to read the next older page; <c>null</c> when the page is empty.
    /// </summary>
    public long? OlderBefore => Entries.Count == 0 ? null : Entries[^1].Sequence;

    /// <summary>
    ///     Gets the sequence to pass as <c>afterSequence</c> to read the next newer page; <c>null</c> when the page is empty.
    /// </summary>
    public long? NewerAfter => Entries.Count == 0 ? null : Entries[0].Sequence;
}

/// <summary>
///     Reads, records and verifies the audit trail.
/// </summary>
/// <remarks>
///     Changes of data are audited by the store in the same transaction as the change (see
///     <see cref="IReviewStore"/>); this service records the events that change no data and offers the
///     read and verification side. <see cref="TryRecordAsync"/> never throws for a storage failure, so a
///     problem with the trail cannot lock a person out; the failure is reported through the error hook given
///     at construction. <see cref="TryRecordCoalescedAsync"/> bounds how many rows a repeating event (one that
///     anybody can trigger as often as they like) can add: the first event of a key in a one-minute window is
///     stored and the repeats are only counted. The counts live in memory, behind one lock that is never held
///     across an await, so the service is thread-safe; the state is per process and lost on restart.
/// </remarks>
public sealed class AuditService
{
    /// <summary>
    ///     The number of entries on one page.
    /// </summary>
    public const int PageSize = 50;

    /// <summary>
    ///     The number of entries read from the store at once while verifying.
    /// </summary>
    private const int VerifyBatchSize = 500;

    /// <summary>
    ///     The most keys <see cref="TryRecordCoalescedAsync"/> tracks at once; also the most rows per window.
    /// </summary>
    public const int MaxCoalescedKeys = 500;

    /// <summary>
    ///     The length of the window in which repeats of a coalesced event are only counted.
    /// </summary>
    private static readonly TimeSpan CoalesceWindow = TimeSpan.FromMinutes(1);

    /// <summary>
    ///     The store that persists the trail.
    /// </summary>
    private readonly IAuditStore _store;

    /// <summary>
    ///     Called with the failure when a best-effort record could not be stored; <c>null</c> for none.
    /// </summary>
    private readonly Action<Exception>? _onError;

    /// <summary>
    ///     The clock for the coalescing windows.
    /// </summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>
    ///     Guards <see cref="_windows"/>; never held across an await.
    /// </summary>
    private readonly object _windowsLock = new();

    /// <summary>
    ///     For each coalesced key, when its current window started and how many repeats it has suppressed.
    /// </summary>
    private readonly Dictionary<string, (DateTimeOffset Start, int Suppressed)> _windows = new(StringComparer.Ordinal);

    /// <summary>
    ///     Initializes a new instance of the <see cref="AuditService"/> class.
    /// </summary>
    /// <param name="store">The store that persists the trail. Must not be null.</param>
    /// <param name="onError">Receives the failure of a best-effort record, for logging; may be <c>null</c>.</param>
    /// <param name="timeProvider">The clock for the coalescing windows; <c>null</c> for the system clock.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="store"/> is null.</exception>
    public AuditService(IAuditStore store, Action<Exception>? onError = null, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
        _onError = onError;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    ///     Records an event and lets a failure propagate.
    /// </summary>
    /// <param name="draft">What to record. Must not be null.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The stored entry.</returns>
    public Task<AuditEntry> RecordAsync(AuditDraft draft, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        return _store.AppendAsync(draft, cancellationToken);
    }

    /// <summary>
    ///     Records an event on a best-effort basis: a storage failure is reported to the error hook and swallowed.
    /// </summary>
    /// <param name="draft">What to record. Must not be null.</param>
    /// <param name="cancellationToken">Cancels the operation; cancellation is not swallowed.</param>
    /// <returns><c>true</c> when the entry was stored; <c>false</c> when storing failed.</returns>
    public async Task<bool> TryRecordAsync(AuditDraft draft, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        try
        {
            await _store.AppendAsync(draft, cancellationToken);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Best effort: the caller's own work (for example a sign-in) must not fail because the trail did
            try
            {
                _onError?.Invoke(ex);
            }
            catch (Exception)
            {
                // The hook is only for diagnostics; its failure must not surface either
            }

            return false;
        }
    }

    /// <summary>
    ///     Records a repeating event on a best-effort basis, at most once per key and minute.
    /// </summary>
    /// <param name="draft">What to record. Must not be null.</param>
    /// <param name="key">Identifies the repeating event (for example the action and the account); repeats of one key share a window. Must not be null.</param>
    /// <param name="cancellationToken">Cancels the operation; cancellation is not swallowed.</param>
    /// <returns><c>true</c> when an entry was stored; <c>false</c> when the event was only counted, dropped or storing failed.</returns>
    /// <remarks>
    ///     The first event of a key is stored at once and starts a one-minute window; repeats inside the window
    ///     are only counted. The first event of the next window is stored with the count of the repeats of the
    ///     previous window appended to its detail, because entries are immutable and the count can only ride on
    ///     a later one. The count of the last window is never stored when the key does not occur again. At most
    ///     <see cref="MaxCoalescedKeys"/> keys are tracked: when the table is full, expired keys are removed, and
    ///     if it is still full a new key is dropped (neither stored nor counted), which also bounds the rows
    ///     per minute.
    /// </remarks>
    public Task<bool> TryRecordCoalescedAsync(AuditDraft draft, string key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(key);

        int carried;
        lock (_windowsLock)
        {
            var now = _timeProvider.GetUtcNow();
            if (_windows.TryGetValue(key, out var window))
            {
                if (now - window.Start < CoalesceWindow)
                {
                    _windows[key] = (window.Start, window.Suppressed + 1);
                    return Task.FromResult(false);
                }

                carried = window.Suppressed;
            }
            else
            {
                carried = 0;
                if (_windows.Count >= MaxCoalescedKeys)
                {
                    var expired = _windows.Where(pair => now - pair.Value.Start >= CoalesceWindow).Select(pair => pair.Key).ToList();
                    foreach (var expiredKey in expired)
                    {
                        _windows.Remove(expiredKey);
                    }

                    if (_windows.Count >= MaxCoalescedKeys)
                    {
                        return Task.FromResult(false);
                    }
                }
            }

            _windows[key] = (now, 0);
        }

        var stored = carried == 0
            ? draft
            : draft with { Detail = $"{draft.Detail}; {carried} more in the minute after the previous entry" };
        return TryRecordAsync(stored, cancellationToken);
    }

    /// <summary>
    ///     Reads one page of the trail, newest first.
    /// </summary>
    /// <param name="projectId">With <paramref name="reviewId"/>, limits the page to one review; <c>null</c> for the whole trail.</param>
    /// <param name="reviewId">The review to limit to, or <c>null</c> for the whole trail.</param>
    /// <param name="beforeSequence">Start with the entries older than this sequence (the "Older" page), or <c>null</c>.</param>
    /// <param name="afterSequence">Start with the entries newer than this sequence (the "Newer" page), or <c>null</c>.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The page, <see cref="PageSize"/> entries at most. When both positions are given, <paramref name="beforeSequence"/> wins.</returns>
    public async Task<AuditEntryPage> ListAsync(
        int? projectId,
        string? reviewId,
        long? beforeSequence,
        long? afterSequence,
        CancellationToken cancellationToken = default)
    {
        // Only one direction at a time
        var after = beforeSequence is null ? afterSequence : null;
        var entries = await _store.ListAsync(projectId, reviewId, beforeSequence, after, PageSize, cancellationToken);
        if (entries.Count == 0)
        {
            return new AuditEntryPage(entries, false, false);
        }

        // One cheap look past each end of the page tells whether a link is needed
        var older = await _store.ListAsync(projectId, reviewId, entries[^1].Sequence, null, 1, cancellationToken);
        var newer = await _store.ListAsync(projectId, reviewId, null, entries[0].Sequence, 1, cancellationToken);
        return new AuditEntryPage(entries, older.Count > 0, newer.Count > 0);
    }

    /// <summary>
    ///     Verifies the whole chain, reading it in order and in batches.
    /// </summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The result, naming the first bad sequence when the chain is broken.</returns>
    public async Task<AuditVerificationResult> VerifyAsync(CancellationToken cancellationToken = default)
    {
        var verifier = new AuditChainVerifier();
        long last = 0;
        while (true)
        {
            var batch = await _store.ReadAfterAsync(last, VerifyBatchSize, cancellationToken);
            if (batch.Count == 0)
            {
                break;
            }

            if (batch.Any(entry => !verifier.Add(entry)))
            {
                return verifier.Result;
            }

            last = batch[^1].Sequence;
        }

        return verifier.Result;
    }
}
