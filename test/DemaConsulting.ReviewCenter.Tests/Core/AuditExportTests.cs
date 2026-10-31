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

using System.Text;
using System.Text.Json;
using DemaConsulting.ReviewCenter.Core;

namespace DemaConsulting.ReviewCenter.Tests.Core;

/// <summary>
///     Tests for the export of <see cref="AuditService"/> over the in-memory audit store: the filter, the order, the
///     snapshot, the batches, the chain status, the cancellation and the audit entry the export leaves.
/// </summary>
public class AuditExportTests
{
    /// <summary>
    ///     Creates a service over a new store.
    /// </summary>
    /// <param name="errors">Receives the failures the service reports.</param>
    /// <returns>The service and its store.</returns>
    private static (AuditService Service, FakeAuditStore Store) Create(List<Exception>? errors = null)
    {
        var clock = new FixedTimeProvider(new DateTimeOffset(2025, 3, 4, 5, 6, 7, TimeSpan.Zero));
        var store = new FakeAuditStore(clock);
        return (new AuditService(store, errors is null ? null : errors.Add, clock), store);
    }

    /// <summary>
    ///     Records the given number of entries, alternating between project 1, project 2 and no project.
    /// </summary>
    /// <param name="service">The service.</param>
    /// <param name="count">The number of entries.</param>
    /// <returns>A task that completes when the entries are recorded.</returns>
    private static async Task Fill(AuditService service, int count)
    {
        for (var i = 1; i <= count; i++)
        {
            int? project = (i % 3) switch { 1 => 1, 2 => 2, _ => null };
            await service.RecordAsync(
                AuditDraft.By(TestActors.Ann, "test.event", "event " + i, project),
                TestContext.Current.CancellationToken);
        }
    }

    /// <summary>
    ///     Exports into memory and returns the text of the CSV without its byte order mark.
    /// </summary>
    /// <param name="service">The service.</param>
    /// <param name="filter">The filter.</param>
    /// <returns>The prepared export and the CSV text.</returns>
    private static async Task<(AuditExport Export, string Text)> ExportCsvAsync(AuditService service, AuditFilter filter)
    {
        var export = await service.PrepareExportAsync(filter, AuditExportFormat.Csv, TestActors.Ann, TestContext.Current.CancellationToken);
        using var target = new MemoryStream();
        await service.ExportAsync(export, target, TestContext.Current.CancellationToken);
        var bytes = target.ToArray();
        return (export, new UTF8Encoding(false).GetString(bytes, 3, bytes.Length - 3));
    }

    /// <summary>
    ///     Gets the sequences of the rows of a CSV text.
    /// </summary>
    /// <param name="csv">The CSV text.</param>
    /// <returns>The sequences in file order.</returns>
    private static List<long> Sequences(string csv)
    {
        return [.. csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Skip(1).Select(l => long.Parse(l.Split(',')[0], System.Globalization.CultureInfo.InvariantCulture))];
    }

    /// <summary>
    ///     Test that the export has the entries of the filter in ascending chain order and nothing else.
    /// </summary>
    [Fact]
    public async Task AuditExport_ExportAsync_Filter_WritesMatchingEntriesAscending()
    {
        // Arrange: sequences 1, 4, 7, 10 are project 1
        var (service, _) = Create();
        await Fill(service, 10);

        // Act
        var (_, text) = await ExportCsvAsync(service, new AuditFilter(ProjectId: 1));

        // Assert
        Assert.Equal([1L, 4L, 7L, 10L], Sequences(text));
    }

    /// <summary>
    ///     Test that a project export never holds an entry without a project and that an export without a project
    ///     holds all of them.
    /// </summary>
    [Fact]
    public async Task AuditExport_ExportAsync_Scope_ProjectNeverHoldsGlobalRows()
    {
        // Arrange: sequences 3, 6, 9 have no project
        var (service, _) = Create();
        await Fill(service, 9);

        // Act
        var (_, project) = await ExportCsvAsync(service, new AuditFilter(ProjectId: 2));
        var (_, everything) = await ExportCsvAsync(service, AuditFilter.None);

        // Assert
        // Assert: the first export's own entry (sequence 10, project 2) is part of the second one
        Assert.Equal([2L, 5L, 8L], Sequences(project));
        Assert.Equal(Enumerable.Range(1, 10).Select(i => (long)i), Sequences(everything));
    }

    /// <summary>
    ///     Test that entries appended after the export was prepared, and while it streams, are not part of it.
    /// </summary>
    [Fact]
    public async Task AuditExport_ExportAsync_EntriesAppendedAfterPrepare_AreNotIncluded()
    {
        // Arrange
        var (service, store) = Create();
        await Fill(service, 1200);
        var export = await service.PrepareExportAsync(
            AuditFilter.None, AuditExportFormat.Csv, TestActors.Ann, TestContext.Current.CancellationToken);
        await Fill(service, 5);

        // The trail keeps growing while the second batch is read
        store.AfterMatchingRead = call =>
        {
            if (call == 2)
            {
                service.RecordAsync(AuditDraft.By(TestActors.Ann, "test.event", "late"), CancellationToken.None).GetAwaiter().GetResult();
            }
        };

        // Act
        using var target = new MemoryStream();
        var rows = await service.ExportAsync(export, target, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1200, export.SnapshotSequence);
        Assert.Equal(1200, rows);
        var bytes = target.ToArray();
        var sequences = Sequences(new UTF8Encoding(false).GetString(bytes, 3, bytes.Length - 3));
        Assert.Equal(Enumerable.Range(1, 1200).Select(i => (long)i), sequences);
    }

    /// <summary>
    ///     Test that a long trail is read in keyset batches of 500 and never in one piece.
    /// </summary>
    [Fact]
    public async Task AuditExport_ExportAsync_MoreThanOneBatch_ReadsInBatchesOf500()
    {
        // Arrange
        var (service, store) = Create();
        await Fill(service, 1201);

        // Act
        var (_, text) = await ExportCsvAsync(service, AuditFilter.None);

        // Assert: 500 + 500 + 201
        var sequences = Sequences(text);
        Assert.Equal(1201, sequences.Count);
        Assert.Equal(sequences.OrderBy(s => s), sequences);
        Assert.Equal(3, store.MatchingReads);
        Assert.Equal(AuditService.ExportBatchSize, store.LargestMatchingCount);
        Assert.Equal(500, AuditService.ExportBatchSize);
    }

    /// <summary>
    ///     Test that a trail that fills whole batches ends with one more, empty read.
    /// </summary>
    [Fact]
    public async Task AuditExport_ExportAsync_ExactlyWholeBatches_EndsAfterAnEmptyRead()
    {
        // Arrange
        var (service, store) = Create();
        await Fill(service, 1000);

        // Act
        var (_, text) = await ExportCsvAsync(service, AuditFilter.None);

        // Assert
        Assert.Equal(1000, Sequences(text).Count);
        Assert.Equal(3, store.MatchingReads);
    }

    /// <summary>
    ///     Test that an export is audited after the stream, with the format, the row count and the filter, as an
    ///     entry of the project of the filter and by the exporting user.
    /// </summary>
    [Fact]
    public async Task AuditExport_ExportAsync_ProjectScope_RecordsAuditEntryAfterStream()
    {
        // Arrange
        var (service, store) = Create();
        await Fill(service, 10);

        // Act
        var (export, text) = await ExportCsvAsync(service, new AuditFilter(ProjectId: 1, ActionPrefix: "test."));

        // Assert
        var last = store.Entries[^1];
        Assert.Equal(11, last.Sequence);
        Assert.Equal(AuditService.ExportedAction, last.Action);
        Assert.Equal("audit.exported", last.Action);
        Assert.Equal(1, last.ProjectId);
        Assert.Equal("Ann (ann)", last.ActorName);
        Assert.Equal("csv export, 4 rows, complete; project 1, action group \"test.\"", last.Detail);
        Assert.Equal(10, export.SnapshotSequence);
        Assert.DoesNotContain("exported", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that an export without a project is audited without a project, and that the entry is not part of its
    ///     own export.
    /// </summary>
    [Fact]
    public async Task AuditExport_ExportAsync_GlobalScope_RecordsEntryWithoutProject()
    {
        // Arrange
        var (service, store) = Create();
        await Fill(service, 3);
        var export = await service.PrepareExportAsync(
            AuditFilter.None, AuditExportFormat.Json, TestActors.Ann, TestContext.Current.CancellationToken);
        using var target = new MemoryStream();

        // Act
        var rows = await service.ExportAsync(export, target, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(3, rows);
        var last = store.Entries[^1];
        Assert.Equal(AuditService.ExportedAction, last.Action);
        Assert.Null(last.ProjectId);
        Assert.Equal("json export, 3 rows, complete; no filter", last.Detail);
        using var document = JsonDocument.Parse(target.ToArray());
        Assert.Equal(3, document.RootElement.GetProperty("entries").GetArrayLength());
    }

    /// <summary>
    ///     Test that a cancelled export still leaves its audit entry, marked incomplete, and rethrows the cancellation.
    /// </summary>
    [Fact]
    public async Task AuditExport_ExportAsync_CancelledMidStream_RecordsIncompleteEntry()
    {
        // Arrange
        var (service, store) = Create();
        await Fill(service, 1200);
        var export = await service.PrepareExportAsync(
            AuditFilter.None, AuditExportFormat.Csv, TestActors.Ann, TestContext.Current.CancellationToken);
        using var cancellation = new CancellationTokenSource();
        store.AfterMatchingRead = call =>
        {
            if (call == 2)
            {
                cancellation.Cancel();
            }
        };
        using var target = new MemoryStream();

        // Act
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.ExportAsync(export, target, cancellation.Token));

        // Assert: the first batch was written, the second was read but not passed on; the entry was appended although the token is cancelled
        var last = store.Entries[^1];
        Assert.Equal(1201, last.Sequence);
        Assert.Equal(AuditService.ExportedAction, last.Action);
        Assert.Equal("csv export, 500 rows, incomplete (cancelled or failed); no filter", last.Detail);
    }

    /// <summary>
    ///     Test that a failing target is audited as incomplete and the failure is not hidden.
    /// </summary>
    [Fact]
    public async Task AuditExport_ExportAsync_TargetFails_RecordsIncompleteEntryAndRethrows()
    {
        // Arrange
        var (service, store) = Create();
        await Fill(service, 5);
        var export = await service.PrepareExportAsync(
            AuditFilter.None, AuditExportFormat.Csv, TestActors.Ann, TestContext.Current.CancellationToken);

        // Act
        await Assert.ThrowsAsync<IOException>(() => service.ExportAsync(export, new FailingStream(), TestContext.Current.CancellationToken));

        // Assert
        Assert.Contains("incomplete", store.Entries[^1].Detail, StringComparison.Ordinal);
        Assert.Equal(AuditService.ExportedAction, store.Entries[^1].Action);
    }

    /// <summary>
    ///     Test that a failure to store the audit entry is reported to the error hook and does not fail the export.
    /// </summary>
    [Fact]
    public async Task AuditExport_ExportAsync_AuditAppendFails_ReportsErrorAndStillCompletes()
    {
        // Arrange
        var errors = new List<Exception>();
        var (service, store) = Create(errors);
        await Fill(service, 3);
        var export = await service.PrepareExportAsync(
            AuditFilter.None, AuditExportFormat.Csv, TestActors.Ann, TestContext.Current.CancellationToken);
        store.FailNextAppend = new InvalidOperationException("disk full");
        using var target = new MemoryStream();

        // Act
        var rows = await service.ExportAsync(export, target, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(3, rows);
        Assert.Single(errors);
        Assert.Equal(3, store.Entries.Count);
    }

    /// <summary>
    ///     Test that an intact chain is reported as intact, before anything is streamed.
    /// </summary>
    [Fact]
    public async Task AuditExport_PrepareExportAsync_IntactChain_ReportsIntact()
    {
        // Arrange
        var (service, store) = Create();
        await Fill(service, 5);

        // Act
        var export = await service.PrepareExportAsync(
            new AuditFilter(ProjectId: 1), AuditExportFormat.Csv, TestActors.Ann, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("intact", export.ChainStatus);
        Assert.Null(export.FirstBrokenSequence);
        Assert.Contains("chain-intact.csv", export.FileName, StringComparison.Ordinal);
        Assert.Equal(5, export.SnapshotSequence);
        Assert.Equal(0, store.MatchingReads);
    }

    /// <summary>
    ///     Test that a tampered chain is reported as broken with its first broken sequence, in the file name too, and
    ///     that the entries are still exported.
    /// </summary>
    [Fact]
    public async Task AuditExport_PrepareExportAsync_TamperedChain_ReportsBrokenAtFirstBadSequence()
    {
        // Arrange
        var (service, store) = Create();
        await Fill(service, 6);
        store.TamperDetail(3, "rewritten");

        // Act
        var (export, text) = await ExportCsvAsync(service, new AuditFilter(ProjectId: 1));

        // Assert
        Assert.Equal("broken", export.ChainStatus);
        Assert.Equal(3, export.FirstBrokenSequence);
        Assert.Contains("chain-broken.csv", export.FileName, StringComparison.Ordinal);
        Assert.Equal([1L, 4L], Sequences(text));
    }

    /// <summary>
    ///     Test that a project export reports the status of the whole chain and nothing that counts the entries of
    ///     other projects: neither the chain result nor the file mention how many entries there are in all.
    /// </summary>
    [Fact]
    public async Task AuditExport_ExportAsync_ProjectExport_RevealsNoCountsOfOtherProjects()
    {
        // Arrange: 100 entries, 34 of project 1, tampered at 50 (which belongs to project 2)
        var (service, store) = Create();
        await Fill(service, 100);
        store.TamperDetail(50, "rewritten");
        var export = await service.PrepareExportAsync(
            new AuditFilter(ProjectId: 1), AuditExportFormat.Json, TestActors.Ann, TestContext.Current.CancellationToken);
        using var target = new MemoryStream();

        // Act
        await service.ExportAsync(export, target, TestContext.Current.CancellationToken);

        // Assert: the status is the whole chain's, but the document has no total or head hash
        using var document = JsonDocument.Parse(target.ToArray());
        var chain = document.RootElement.GetProperty("chain");
        Assert.Equal("broken", chain.GetProperty("status").GetString());
        Assert.Equal(50, chain.GetProperty("firstBrokenSequence").GetInt64());
        Assert.Equal(["status", "firstBrokenSequence"], chain.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.Equal(34, document.RootElement.GetProperty("entries").GetArrayLength());
        Assert.DoesNotContain("100", chain.GetRawText(), StringComparison.Ordinal);
        Assert.All(
            document.RootElement.GetProperty("entries").EnumerateArray(),
            e => Assert.Equal(1, e.GetProperty("project").GetInt32()));
    }

    /// <summary>
    ///     Test that an empty trail gives a valid, empty export.
    /// </summary>
    [Fact]
    public async Task AuditExport_ExportAsync_EmptyTrail_IsValidAndAudited()
    {
        // Arrange
        var (service, store) = Create();

        // Act
        var (export, text) = await ExportCsvAsync(service, AuditFilter.None);

        // Assert
        Assert.Equal(0, export.SnapshotSequence);
        Assert.Empty(Sequences(text));
        Assert.Single(store.Entries);
        Assert.Equal("csv export, 0 rows, complete; no filter", store.Entries[0].Detail);
    }

    /// <summary>
    ///     Test that a filter that is not valid is refused before anything is read.
    /// </summary>
    [Fact]
    public async Task AuditExport_PrepareExportAsync_InvalidFilter_ThrowsDomainRule()
    {
        // Arrange
        var (service, _) = Create();
        var reversed = new AuditFilter(From: new DateOnly(2026, 2, 1), To: new DateOnly(2026, 1, 1));

        // Act
        var ex = await Assert.ThrowsAsync<DomainRuleException>(
            () => service.PrepareExportAsync(reversed, AuditExportFormat.Csv, TestActors.Ann, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(DomainRule.AuditFilterInvalid, ex.Rule);
    }

    /// <summary>
    ///     Test that the export methods refuse null arguments.
    /// </summary>
    [Fact]
    public async Task AuditExport_NullArguments_Throw()
    {
        // Arrange
        var (service, _) = Create();
        var export = await service.PrepareExportAsync(
            AuditFilter.None, AuditExportFormat.Csv, TestActors.Ann, TestContext.Current.CancellationToken);

        // Act / Assert
        var token = TestContext.Current.CancellationToken;
        await Assert.ThrowsAsync<ArgumentNullException>(() => service.PrepareExportAsync(null!, AuditExportFormat.Csv, TestActors.Ann, token));
        await Assert.ThrowsAsync<ArgumentNullException>(() => service.PrepareExportAsync(AuditFilter.None, AuditExportFormat.Csv, null!, token));
        await Assert.ThrowsAsync<ArgumentNullException>(() => service.ExportAsync(null!, new MemoryStream(), token));
        await Assert.ThrowsAsync<ArgumentNullException>(() => service.ExportAsync(export, null!, token));
    }

    /// <summary>
    ///     Test the human-readable filter summary that goes into the audit entry.
    /// </summary>
    [Fact]
    public void AuditFilter_Describe_SetParts_AreListedInOrder()
    {
        // Arrange
        var filter = new AuditFilter(
            ProjectId: 5,
            ReviewId: "r1",
            ActorUserId: 9,
            ActorText: "bob",
            Action: "review.signed",
            ActionPrefix: "review.",
            From: new DateOnly(2026, 1, 2),
            To: new DateOnly(2026, 3, 4));

        // Act / Assert
        Assert.Equal("no filter", AuditFilter.None.Describe());
        Assert.Equal(
            "from 2026-01-02, to 2026-03-04, project 5, review \"r1\", actor id 9, action \"review.signed\", action group \"review.\", actor contains \"bob\"",
            filter.Describe());
    }

    /// <summary>
    ///     Test that the audit entry of an export with the largest possible filter stays within the limit of a stored
    ///     detail and still shows the format, the row count, the state and the date range, because the long free texts
    ///     come last.
    /// </summary>
    [Fact]
    public async Task AuditExport_ExportAsync_MaximalFilter_DetailFitsAndKeepsCountAndDates()
    {
        // Arrange
        var (service, store) = Create();
        await Fill(service, 3);
        var filter = new AuditFilter(
            ProjectId: int.MaxValue,
            ReviewId: new string('r', ReviewLimits.MaxReviewIdLength),
            ActorUserId: int.MaxValue,
            ActorText: new string('a', AuditText.MaxActorNameLength),
            Action: new string('x', AuditText.MaxActionLength),
            ActionPrefix: new string('p', AuditText.MaxActionLength),
            From: new DateOnly(2026, 1, 2),
            To: new DateOnly(2026, 3, 4));
        var export = await service.PrepareExportAsync(
            filter, AuditExportFormat.Json, TestActors.Ann, TestContext.Current.CancellationToken);
        using var target = new MemoryStream();

        // Act
        await service.ExportAsync(export, target, TestContext.Current.CancellationToken);

        // Assert: the line that was described is longer than the limit, so the store cut its end
        Assert.True(filter.Describe().Length + 80 > AuditText.MaxDetailLength);
        var detail = store.Entries[^1].Detail;
        Assert.True(detail.Length <= AuditText.MaxDetailLength);
        Assert.StartsWith("json export, 0 rows, complete; from 2026-01-02, to 2026-03-04, project ", detail, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that the export action is part of the catalog of known actions.
    /// </summary>
    [Fact]
    public void AuditExport_ExportedAction_IsInTheActionCatalog()
    {
        // Arrange / Act / Assert
        Assert.Contains(AuditService.ExportedAction, AuditActions.All);
    }

    /// <summary>
    ///     A stream whose every write fails.
    /// </summary>
    private sealed class FailingStream : Stream
    {
        /// <inheritdoc />
        public override bool CanRead => false;

        /// <inheritdoc />
        public override bool CanSeek => false;

        /// <inheritdoc />
        public override bool CanWrite => true;

        /// <inheritdoc />
        public override long Length => throw new NotSupportedException();

        /// <inheritdoc />
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        /// <inheritdoc />
        public override void Flush()
        {
        }

        /// <inheritdoc />
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        /// <inheritdoc />
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        /// <inheritdoc />
        public override void SetLength(long value) => throw new NotSupportedException();

        /// <inheritdoc />
        public override void Write(byte[] buffer, int offset, int count) => throw new IOException("the connection is gone");

        /// <inheritdoc />
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromException(new IOException("the connection is gone"));
        }
    }
}
