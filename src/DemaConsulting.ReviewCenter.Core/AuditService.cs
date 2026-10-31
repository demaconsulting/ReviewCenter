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

using System.Globalization;

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
///     read and verification side, and the streaming export of the entries that match a filter
///     (<see cref="PrepareExportAsync"/> and <see cref="ExportAsync"/>). <see cref="TryRecordAsync"/> never throws for a storage failure, so a
///     problem with the trail cannot lock a person out; the failure is reported through the error hook given
///     at construction. <see cref="TryRecordCoalescedAsync"/> bounds how many rows a repeating event (one that
///     anybody can trigger as often as they like) can add: the first event of a key in a one-minute window is
///     stored and the repeats are only counted; when the table of keys is full the events of new keys are
///     counted in one overflow window that stores a single <see cref="OverflowAction"/> entry. The counts live in memory, behind one lock that is never held
///     across an await, so the service is thread-safe; the state is per process and lost on restart.
/// </remarks>
public sealed class AuditService
{
    /// <summary>
    ///     The number of entries on one page when the caller does not choose.
    /// </summary>
    public const int DefaultPageSize = 50;

    /// <summary>
    ///     The most entries one page can hold, whatever the caller asks for.
    /// </summary>
    public const int MaxPageSize = 200;

    /// <summary>
    ///     Gets the page sizes the audit pages offer, smallest first; all are within <see cref="MaxPageSize"/>.
    /// </summary>
    public static IReadOnlyList<int> PageSizes { get; } = [25, DefaultPageSize, 100];

    /// <summary>
    ///     The number of entries read from the store at once while verifying.
    /// </summary>
    private const int VerifyBatchSize = 500;

    /// <summary>
    ///     The number of entries read from the store at once while exporting.
    /// </summary>
    public const int ExportBatchSize = 500;

    /// <summary>
    ///     The action of the entry that records an export of the audit trail.
    /// </summary>
    public const string ExportedAction = "audit.exported";

    /// <summary>
    ///     The most keys <see cref="TryRecordCoalescedAsync"/> tracks at once; also the most rows per window.
    /// </summary>
    public const int MaxCoalescedKeys = 500;

    /// <summary>
    ///     The action of the entry that tells events were suppressed because the table of coalesced keys was full.
    /// </summary>
    public const string OverflowAction = "audit.events-suppressed";

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
    ///     When the current overflow window started (the first event suppressed because the table was full); <c>null</c> for none.
    /// </summary>
    private DateTimeOffset? _overflowStart;

    /// <summary>
    ///     How many events the current overflow window has suppressed, the one that started it included.
    /// </summary>
    private int _overflowSuppressed;

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
            ReportError(ex);
            return false;
        }
    }

    /// <summary>
    ///     Passes a failure to the error hook; a failing hook is swallowed.
    /// </summary>
    /// <param name="exception">The failure to report.</param>
    private void ReportError(Exception exception)
    {
        try
        {
            _onError?.Invoke(exception);
        }
        catch (Exception)
        {
            // The hook is only for diagnostics; its failure must not surface either
        }
    }

    /// <summary>
    ///     Records a repeating event on a best-effort basis, at most once per key and minute.
    /// </summary>
    /// <param name="draft">What to record. Must not be null.</param>
    /// <param name="key">Identifies the repeating event (for example the action and the account); repeats of one key share a window. Must not be null.</param>
    /// <param name="cancellationToken">Cancels the operation; cancellation is not swallowed.</param>
    /// <returns><c>true</c> when an entry was stored; <c>false</c> when the event was only counted, suppressed or storing failed.</returns>
    /// <exception cref="OperationCanceledException">Thrown when cancellation was already requested (nothing is counted or started) or is requested while storing.</exception>
    /// <remarks>
    ///     The first event of a key is stored at once and starts a one-minute window; repeats inside the window
    ///     are only counted. The first event of the next window is stored with the count of the repeats of the
    ///     previous window appended to its detail, because entries are immutable and the count can only ride on
    ///     a later one. The count of the last window is never stored when the key does not occur again. An event
    ///     whose cancellation was already requested changes nothing. At most <see cref="MaxCoalescedKeys"/> keys
    ///     are tracked: when the table is full, expired keys are removed, and if it is still full the event of a
    ///     new key is only counted in an overflow window. The first suppressed event of an overflow window stores
    ///     one <see cref="OverflowAction"/> entry (with the count of the previous overflow window) and reports
    ///     the saturation once to the error hook, so the rows per minute stay bounded and the gap is visible.
    ///     A cancellation that arrives while the first event of a window is being stored loses that event for
    ///     the window, as a store failure does.
    /// </remarks>
    public async Task<bool> TryRecordCoalescedAsync(AuditDraft draft, string key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(key);

        // A cancelled request must not count a repeat or start a window
        cancellationToken.ThrowIfCancellationRequested();

        var carried = 0;
        int? overflowCarried = null;
        lock (_windowsLock)
        {
            var now = _timeProvider.GetUtcNow();
            if (_windows.TryGetValue(key, out var window))
            {
                if (now - window.Start < CoalesceWindow)
                {
                    _windows[key] = (window.Start, window.Suppressed + 1);
                    return false;
                }

                carried = window.Suppressed;
            }
            else if (_windows.Count >= MaxCoalescedKeys && !TryMakeRoom(now))
            {
                if (_overflowStart is { } overflowStart && now - overflowStart < CoalesceWindow)
                {
                    _overflowSuppressed++;
                    return false;
                }

                // The first suppressed event of a new overflow window: it is counted, and the row is stored below
                overflowCarried = _overflowSuppressed;
                _overflowStart = now;
                _overflowSuppressed = 1;
            }

            if (overflowCarried is null)
            {
                _windows[key] = (now, 0);
            }
        }

        if (overflowCarried is { } previous)
        {
            await RecordOverflowAsync(previous, cancellationToken);
            return false;
        }

        var stored = carried == 0
            ? draft
            : draft with { Detail = $"{draft.Detail}; {carried} more in the minute after the previous entry" };
        return await TryRecordAsync(stored, cancellationToken);
    }

    /// <summary>
    ///     Removes the expired keys from a full table; the caller holds <see cref="_windowsLock"/>.
    /// </summary>
    /// <param name="now">The current time.</param>
    /// <returns><c>true</c> when there is room for one more key.</returns>
    private bool TryMakeRoom(DateTimeOffset now)
    {
        var expired = _windows.Where(pair => now - pair.Value.Start >= CoalesceWindow).Select(pair => pair.Key).ToList();
        foreach (var expiredKey in expired)
        {
            _windows.Remove(expiredKey);
        }

        return _windows.Count < MaxCoalescedKeys;
    }

    /// <summary>
    ///     Stores the one entry of an overflow window and reports the saturation to the error hook.
    /// </summary>
    /// <param name="previousSuppressed">The number of events suppressed in the previous overflow window.</param>
    /// <param name="cancellationToken">Cancels the operation; cancellation is not swallowed.</param>
    private async Task RecordOverflowAsync(int previousSuppressed, CancellationToken cancellationToken)
    {
        ReportError(new InvalidOperationException(
            $"Audit coalescing table full ({MaxCoalescedKeys} keys); events of new keys are suppressed"));

        var detail = previousSuppressed == 0
            ? "tracking table full, events of new keys are not recorded"
            : $"{previousSuppressed} events suppressed in the previous window, tracking table full";
        await TryRecordAsync(new AuditDraft(OverflowAction, null, "audit service", null, null, detail), cancellationToken);
    }

    /// <summary>
    ///     Reads one page of the trail, newest first.
    /// </summary>
    /// <param name="filter">
    ///     What to look for; validated here (see <see cref="AuditFilter.Validated"/>). With a project set the page
    ///     holds only entries of that project and never an entry that names no project; the caller must have
    ///     checked the right to see the project (or, with no project, the right to see the whole trail) first.
    ///     Must not be null.
    /// </param>
    /// <param name="beforeSequence">Start with the entries older than this sequence (the "Older" page), or <c>null</c>.</param>
    /// <param name="afterSequence">Start with the entries newer than this sequence (the "Newer" page), or <c>null</c>.</param>
    /// <param name="pageSize">
    ///     The most entries on the page; a value below 1 means <see cref="DefaultPageSize"/> and a value above
    ///     <see cref="MaxPageSize"/> is lowered to it.
    /// </param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    ///     The page of entries that match the filter. When both positions are given, <paramref name="beforeSequence"/>
    ///     wins.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="filter"/> is null.</exception>
    /// <exception cref="DomainRuleException">
    ///     Thrown with <see cref="DomainRule.AuditFilterInvalid"/> when the filter is not valid.
    /// </exception>
    /// <remarks>
    ///     Paging is by position (the sequence of the last or first entry shown), never by a count of rows, so a read
    ///     costs the same on page one and page one thousand and no total is computed; whether an older or a newer
    ///     page exists is learned by looking one entry past each end of the page with the same filter.
    /// </remarks>
    public async Task<AuditEntryPage> ListAsync(
        AuditFilter filter,
        long? beforeSequence,
        long? afterSequence,
        int pageSize = DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var valid = filter.Validated();
        var take = pageSize < 1 ? DefaultPageSize : Math.Min(pageSize, MaxPageSize);

        // Only one direction at a time
        var after = beforeSequence is null ? afterSequence : null;
        var entries = await _store.ListAsync(valid, beforeSequence, after, take, cancellationToken);
        if (entries.Count == 0)
        {
            return new AuditEntryPage(entries, false, false);
        }

        // One cheap look past each end of the page tells whether a link is needed
        var older = await _store.ListAsync(valid, entries[^1].Sequence, null, 1, cancellationToken);
        var newer = await _store.ListAsync(valid, null, entries[0].Sequence, 1, cancellationToken);
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

    /// <summary>
    ///     Prepares an export: validates the filter, fixes the snapshot and verifies the chain, so the chain status
    ///     is known before the first byte of the export is written.
    /// </summary>
    /// <param name="filter">
    ///     What to export; validated here. With a project set the export holds only entries of that project; the
    ///     caller must have checked the right to export first. Must not be null.
    /// </param>
    /// <param name="format">The file format.</param>
    /// <param name="actor">The exporting user, the actor of the audit entry the export leaves. Must not be null.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The prepared export.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="filter"/> or <paramref name="actor"/> is null.</exception>
    /// <exception cref="DomainRuleException">
    ///     Thrown with <see cref="DomainRule.AuditFilterInvalid"/> when the filter is not valid.
    /// </exception>
    /// <remarks>
    ///     The newest sequence is read before the verification starts, so every entry the export can include is
    ///     covered by the verified chain; entries appended afterwards are not part of the snapshot. The whole chain
    ///     is verified once (see <see cref="VerifyAsync"/>), whatever the filter: a filtered export is a view of a
    ///     verified chain, and the result says only whether the chain is intact and where it first broke. A broken
    ///     chain does not stop the export; the evidence is exactly what an auditor needs, with the status said first.
    /// </remarks>
    public async Task<AuditExport> PrepareExportAsync(
        AuditFilter filter,
        AuditExportFormat format,
        Actor actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(actor);
        var valid = filter.Validated();
        var snapshot = await _store.LatestSequenceAsync(cancellationToken);
        var chain = await VerifyAsync(cancellationToken);
        return new AuditExport(format, valid, actor, chain, snapshot, _timeProvider.GetUtcNow());
    }

    /// <summary>
    ///     Streams a prepared export to the target and then records that it happened.
    /// </summary>
    /// <param name="export">The export from <see cref="PrepareExportAsync"/>. Must not be null.</param>
    /// <param name="target">The stream to write the file to. Must not be null.</param>
    /// <param name="cancellationToken">Cancels the streaming; the audit entry is still recorded.</param>
    /// <returns>The number of entries written.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    /// <exception cref="OperationCanceledException">
    ///     Thrown when <paramref name="cancellationToken"/> is cancelled before the end; the export is then audited as
    ///     incomplete.
    /// </exception>
    /// <remarks>
    ///     The entries are read in ascending sequence (the order of the chain) in keyset batches of
    ///     <see cref="ExportBatchSize"/>, up to the snapshot sequence of the export, and each batch is written and
    ///     passed on before the next is read, so memory use does not depend on the size of the trail and the
    ///     trail may keep growing meanwhile. After the stream ended (or failed, or was cancelled) an
    ///     <see cref="ExportedAction"/> entry is appended with a token that is never cancelled, so an export
    ///     leaves a trace even when the download was aborted; its detail starts with the format, the number of rows
    ///     and whether the export is complete, then names the filter. The entry belongs to the project of the
    ///     filter, or to no project for an export of the whole trail. Because it is appended after the stream it is
    ///     not inside the snapshot of its own export. A failure to store the entry is reported to the error hook
    ///     and does not replace the outcome of the export.
    /// </remarks>
    public async Task<long> ExportAsync(AuditExport export, Stream target, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(export);
        ArgumentNullException.ThrowIfNull(target);
        long rows = 0;
        var complete = false;
        try
        {
            using var writer = new AuditExportWriter(export);
            writer.WriteStart();
            await writer.FlushToAsync(target, cancellationToken);

            var last = 0L;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var batch = await _store.ReadMatchingAsync(
                    export.Filter, last, export.SnapshotSequence, ExportBatchSize, cancellationToken);
                if (batch.Count == 0)
                {
                    break;
                }

                writer.WriteEntries(batch);
                await writer.FlushToAsync(target, cancellationToken);
                rows += batch.Count;
                last = batch[^1].Sequence;
                if (batch.Count < ExportBatchSize)
                {
                    break;
                }
            }

            writer.WriteEnd();
            await writer.FlushToAsync(target, cancellationToken);
            complete = true;
            return rows;
        }
        finally
        {
            var state = complete ? "complete" : "incomplete (cancelled or failed)";
            var detail = $"{export.Format.ToString().ToLowerInvariant()} export, {rows.ToString(CultureInfo.InvariantCulture)} rows, {state}; {export.Filter.Describe()}";
            await TryRecordAsync(
                AuditDraft.By(export.Actor, ExportedAction, detail, export.Filter.ProjectId),
                CancellationToken.None);
        }
    }
}
