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
///     Tests for <see cref="AuditService"/> over the in-memory audit store.
/// </summary>
public class AuditServiceTests
{
    /// <summary>
    ///     Creates a service over a new store.
    /// </summary>
    /// <param name="errors">Receives the failures the service reports.</param>
    /// <param name="clock">The clock of the service (the coalescing windows); <c>null</c> for the system clock.</param>
    /// <returns>The service and its store.</returns>
    private static (AuditService Service, FakeAuditStore Store) Create(List<Exception>? errors = null, TimeProvider? clock = null)
    {
        var store = new FakeAuditStore(new FixedTimeProvider(new DateTimeOffset(2025, 3, 4, 5, 6, 7, TimeSpan.Zero)));
        return (new AuditService(store, errors is null ? null : errors.Add, clock), store);
    }

    /// <summary>
    ///     Records the given number of entries, alternating between two reviews.
    /// </summary>
    /// <param name="service">The service.</param>
    /// <param name="count">The number of entries.</param>
    /// <returns>A task that completes when the entries are recorded.</returns>
    private static async Task Fill(AuditService service, int count)
    {
        for (var i = 1; i <= count; i++)
        {
            await service.RecordAsync(
                AuditDraft.By(TestActors.Ann, "test.event", "event " + i, 1, i % 2 == 0 ? "even" : "odd"),
                TestContext.Current.CancellationToken);
        }
    }

    /// <summary>
    ///     Records one coalesced event for each of the given number of distinct keys.
    /// </summary>
    /// <param name="service">The service.</param>
    /// <param name="draft">The event to record.</param>
    /// <param name="prefix">The start of every key.</param>
    /// <param name="count">The number of keys.</param>
    /// <returns>A task that completes when the events are recorded.</returns>
    private static async Task RecordKeys(AuditService service, AuditDraft draft, string prefix, int count)
    {
        for (var i = 0; i < count; i++)
        {
            await service.TryRecordCoalescedAsync(draft, prefix + i, TestContext.Current.CancellationToken);
        }
    }

    /// <summary>
    ///     Test that the constructor refuses a null store.
    /// </summary>
    [Fact]
    public void AuditService_Constructor_NullStore_Throws()
    {
        // Arrange / Act / Assert
        Assert.Throws<ArgumentNullException>(() => new AuditService(null!));
    }

    /// <summary>
    ///     Test that recording stores a verifiable entry.
    /// </summary>
    [Fact]
    public async Task AuditService_RecordAsync_Draft_StoresEntryThatVerifies()
    {
        // Arrange
        var (service, store) = Create();

        // Act
        var entry = await service.RecordAsync(
            AuditDraft.By(TestActors.Ann, "project.created", "project 'Apollo'", 5),
            TestContext.Current.CancellationToken);
        var result = await service.VerifyAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, entry.Sequence);
        Assert.Equal("Ann (ann)", entry.ActorName);
        Assert.Equal("project.created", entry.Action);
        Assert.Equal(5, entry.ProjectId);
        Assert.Equal(AuditChain.GenesisHash, entry.PreviousHash);
        Assert.Single(store.Entries);
        Assert.True(result.IsIntact);
        Assert.Equal(entry.Hash, result.HeadHash);
    }

    /// <summary>
    ///     Test that a null draft is refused and a storage failure propagates from the strict record.
    /// </summary>
    [Fact]
    public async Task AuditService_RecordAsync_NullDraftOrStoreFailure_Throws()
    {
        // Arrange
        var (service, store) = Create();
        store.FailNextAppend = new InvalidOperationException("disk full");

        // Act / Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => service.RecordAsync(null!, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.RecordAsync(AuditDraft.By(TestActors.Ann, "a.b", "c"), TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Test that the best-effort record stores the entry and reports success.
    /// </summary>
    [Fact]
    public async Task AuditService_TryRecordAsync_Draft_StoresEntryAndReturnsTrue()
    {
        // Arrange
        var errors = new List<Exception>();
        var (service, store) = Create(errors);

        // Act
        var stored = await service.TryRecordAsync(AuditDraft.By(TestActors.Ann, "auth.sign-in", "signed in"), TestContext.Current.CancellationToken);

        // Assert
        Assert.True(stored);
        Assert.Single(store.Entries);
        Assert.Empty(errors);
    }

    /// <summary>
    ///     Test that the best-effort record swallows a storage failure, reports it to the hook and returns false.
    /// </summary>
    [Fact]
    public async Task AuditService_TryRecordAsync_StoreFailure_IsSwallowedAndReported()
    {
        // Arrange
        var errors = new List<Exception>();
        var (service, store) = Create(errors);
        store.FailNextAppend = new InvalidOperationException("disk full");

        // Act
        var stored = await service.TryRecordAsync(AuditDraft.By(TestActors.Ann, "auth.sign-in", "signed in"), TestContext.Current.CancellationToken);

        // Assert
        Assert.False(stored);
        Assert.Empty(store.Entries);
        Assert.Equal("disk full", Assert.Single(errors).Message);
    }

    /// <summary>
    ///     Test that a failing error hook, or no hook at all, does not surface from the best-effort record.
    /// </summary>
    [Fact]
    public async Task AuditService_TryRecordAsync_FailingOrMissingHook_StillReturnsFalse()
    {
        // Arrange
        var store = new FakeAuditStore(TimeProvider.System) { FailNextAppend = new InvalidOperationException("x") };
        var throwing = new AuditService(store, _ => throw new InvalidOperationException("hook"));
        var silent = new AuditService(new FakeAuditStore(TimeProvider.System) { FailNextAppend = new InvalidOperationException("y") });

        // Act
        var first = await throwing.TryRecordAsync(AuditDraft.By(TestActors.Ann, "a.b", "c"), TestContext.Current.CancellationToken);
        var second = await silent.TryRecordAsync(AuditDraft.By(TestActors.Ann, "a.b", "c"), TestContext.Current.CancellationToken);

        // Assert
        Assert.False(first);
        Assert.False(second);
    }

    /// <summary>
    ///     Test that cancellation is not swallowed by the best-effort record.
    /// </summary>
    [Fact]
    public async Task AuditService_TryRecordAsync_Cancellation_Propagates()
    {
        // Arrange
        var (service, store) = Create();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        store.FailNextAppend = new OperationCanceledException(cts.Token);

        // Act / Assert
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => service.TryRecordAsync(AuditDraft.By(TestActors.Ann, "a.b", "c"), cts.Token));
    }

    /// <summary>
    ///     Test that a null draft is refused by the best-effort record.
    /// </summary>
    [Fact]
    public async Task AuditService_TryRecordAsync_NullDraft_Throws()
    {
        // Arrange
        var (service, _) = Create();

        // Act / Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => service.TryRecordAsync(null!, TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Test that the first page is newest first, 50 entries, with an older link only.
    /// </summary>
    [Fact]
    public async Task AuditService_ListAsync_FirstPage_IsNewestFirstWithOlderOnly()
    {
        // Arrange
        var (service, _) = Create();
        await Fill(service, 120);

        // Act
        var page = await service.ListAsync(null, null, null, null, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(50, page.Entries.Count);
        Assert.Equal(120, page.Entries[0].Sequence);
        Assert.Equal(71, page.Entries[^1].Sequence);
        Assert.True(page.HasOlder);
        Assert.False(page.HasNewer);
        Assert.Equal(71, page.OlderBefore);
        Assert.Equal(120, page.NewerAfter);
    }

    /// <summary>
    ///     Test that paging older reaches the oldest entries and then has no older link, and that every entry
    ///     appears exactly once.
    /// </summary>
    [Fact]
    public async Task AuditService_ListAsync_PagingOlder_CoversEveryEntryOnce()
    {
        // Arrange
        var (service, _) = Create();
        await Fill(service, 120);

        // Act
        var first = await service.ListAsync(null, null, null, null, TestContext.Current.CancellationToken);
        var second = await service.ListAsync(null, null, first.OlderBefore, null, TestContext.Current.CancellationToken);
        var third = await service.ListAsync(null, null, second.OlderBefore, null, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(50, second.Entries.Count);
        Assert.True(second.HasOlder);
        Assert.True(second.HasNewer);
        Assert.Equal(20, third.Entries.Count);
        Assert.False(third.HasOlder);
        Assert.True(third.HasNewer);
        Assert.Equal(1, third.Entries[^1].Sequence);
        var all = first.Entries.Concat(second.Entries).Concat(third.Entries).Select(e => e.Sequence).ToList();
        Assert.Equal(Enumerable.Range(1, 120).Reverse().Select(i => (long)i), all);
    }

    /// <summary>
    ///     Test that paging newer returns to the page the older link left, still newest first.
    /// </summary>
    [Fact]
    public async Task AuditService_ListAsync_PagingNewer_ReturnsThePageBefore()
    {
        // Arrange
        var (service, _) = Create();
        await Fill(service, 120);
        var first = await service.ListAsync(null, null, null, null, TestContext.Current.CancellationToken);
        var second = await service.ListAsync(null, null, first.OlderBefore, null, TestContext.Current.CancellationToken);

        // Act
        var back = await service.ListAsync(null, null, null, second.NewerAfter, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(first.Entries.Select(e => e.Sequence), back.Entries.Select(e => e.Sequence));
        Assert.False(back.HasNewer);
        Assert.True(back.HasOlder);
    }

    /// <summary>
    ///     Test that an exactly full page has no older or newer links.
    /// </summary>
    [Fact]
    public async Task AuditService_ListAsync_ExactlyOnePage_HasNoLinks()
    {
        // Arrange
        var (service, _) = Create();
        await Fill(service, 50);

        // Act
        var page = await service.ListAsync(null, null, null, null, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(50, page.Entries.Count);
        Assert.False(page.HasOlder);
        Assert.False(page.HasNewer);
    }

    /// <summary>
    ///     Test that an empty trail gives an empty page without links.
    /// </summary>
    [Fact]
    public async Task AuditService_ListAsync_EmptyTrail_GivesEmptyPage()
    {
        // Arrange
        var (service, _) = Create();

        // Act
        var page = await service.ListAsync(null, null, null, null, TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(page.Entries);
        Assert.False(page.HasOlder);
        Assert.False(page.HasNewer);
        Assert.Null(page.OlderBefore);
        Assert.Null(page.NewerAfter);
    }

    /// <summary>
    ///     Test that the review filter returns only the entries of that review.
    /// </summary>
    [Fact]
    public async Task AuditService_ListAsync_ReviewFilter_ReturnsOnlyThatReview()
    {
        // Arrange
        var (service, _) = Create();
        await Fill(service, 10);

        // Act
        var page = await service.ListAsync(1, "even", null, null, TestContext.Current.CancellationToken);
        var other = await service.ListAsync(2, "even", null, null, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal([10L, 8L, 6L, 4L, 2L], page.Entries.Select(e => e.Sequence));
        Assert.All(page.Entries, e => Assert.Equal("even", e.ReviewId));
        Assert.Empty(other.Entries);
    }

    /// <summary>
    ///     Test that a project filter without a review returns the events of that project only: not another
    ///     project's, and not the events that belong to no project.
    /// </summary>
    [Fact]
    public async Task AuditService_ListAsync_ProjectOnly_ReturnsOnlyThatProjectsEvents()
    {
        // Arrange: events of project 1 (with reviews), one of project 2 and one of no project
        var (service, _) = Create();
        await Fill(service, 4);
        await service.RecordAsync(
            AuditDraft.By(TestActors.Ann, "test.other", "other project", 2), TestContext.Current.CancellationToken);
        await service.RecordAsync(
            AuditDraft.By(TestActors.Ann, "test.global", "no project"), TestContext.Current.CancellationToken);

        // Act
        var page = await service.ListAsync(1, null, null, null, TestContext.Current.CancellationToken);

        // Assert: the four events of project 1 in all its reviews, newest first
        Assert.Equal([4L, 3L, 2L, 1L], page.Entries.Select(e => e.Sequence));
        Assert.All(page.Entries, e => Assert.Equal(1, e.ProjectId));
    }

    /// <summary>
    ///     Test that the page the filter returns is still limited to 50 entries with working links.
    /// </summary>
    [Fact]
    public async Task AuditService_ListAsync_ReviewFilterOverFiftyEntries_Pages()
    {
        // Arrange
        var (service, _) = Create();
        await Fill(service, 130);

        // Act
        var page = await service.ListAsync(1, "odd", null, null, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(50, page.Entries.Count);
        Assert.True(page.HasOlder);
        Assert.False(page.HasNewer);
    }

    /// <summary>
    ///     Test that when both positions are given, the older direction wins.
    /// </summary>
    [Fact]
    public async Task AuditService_ListAsync_BothPositions_BeforeWins()
    {
        // Arrange
        var (service, _) = Create();
        await Fill(service, 10);

        // Act
        var page = await service.ListAsync(null, null, 5, 8, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal([4L, 3L, 2L, 1L], page.Entries.Select(e => e.Sequence));
    }

    /// <summary>
    ///     Test that verification of an untouched trail is intact and reads in order and in bounded batches.
    /// </summary>
    [Fact]
    public async Task AuditService_VerifyAsync_UntouchedTrail_IsIntactAndReadInBatches()
    {
        // Arrange
        var (service, store) = Create();
        await Fill(service, 1200);

        // Act
        var result = await service.VerifyAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsIntact);
        Assert.Equal(1200, result.Count);
        Assert.Equal(store.Entries[^1].Hash, result.HeadHash);
        Assert.True(store.ReadAfterCalls >= 3);
        Assert.True(store.LargestReadCount < 1200);
    }

    /// <summary>
    ///     Test that verification of an empty trail is intact with the genesis head.
    /// </summary>
    [Fact]
    public async Task AuditService_VerifyAsync_EmptyTrail_IsIntact()
    {
        // Arrange
        var (service, _) = Create();

        // Act
        var result = await service.VerifyAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsIntact);
        Assert.Equal(0, result.Count);
        Assert.Equal(AuditChain.GenesisHash, result.HeadHash);
    }

    /// <summary>
    ///     Test that a tampered entry in a later batch is found and reported.
    /// </summary>
    [Fact]
    public async Task AuditService_VerifyAsync_TamperedEntryInLaterBatch_ReportsItsSequence()
    {
        // Arrange
        var (service, store) = Create();
        await Fill(service, 700);
        store.TamperDetail(650, "rewritten");

        // Act
        var result = await service.VerifyAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result.IsIntact);
        Assert.Equal(650, result.FirstBadSequence);
        Assert.Equal(649, result.Count);
    }

    /// <summary>
    ///     Test that repeats of one key inside the window are counted and not stored.
    /// </summary>
    [Fact]
    public async Task AuditService_TryRecordCoalescedAsync_RepeatsWithinWindow_StoresOnlyTheFirst()
    {
        // Arrange
        var clock = new MovableTimeProvider(new DateTimeOffset(2025, 3, 4, 5, 6, 7, TimeSpan.Zero));
        var (service, store) = Create(clock: clock);
        var draft = new AuditDraft("auth.sign-in-failed", null, "unknown user", null, null, "unknown user, name 'x'");

        // Act
        var first = await service.TryRecordCoalescedAsync(draft, "k", TestContext.Current.CancellationToken);
        clock.Advance(TimeSpan.FromSeconds(59));
        var second = await service.TryRecordCoalescedAsync(draft, "k", TestContext.Current.CancellationToken);
        var third = await service.TryRecordCoalescedAsync(draft, "k", TestContext.Current.CancellationToken);

        // Assert: only the first event is stored
        Assert.True(first);
        Assert.False(second);
        Assert.False(third);
        Assert.Equal("unknown user, name 'x'", Assert.Single(store.Entries).Detail);
    }

    /// <summary>
    ///     Test that the first event of the next window is stored with the count of the repeats suppressed before it.
    /// </summary>
    [Fact]
    public async Task AuditService_TryRecordCoalescedAsync_NextWindow_StoresEntryWithSuppressedCount()
    {
        // Arrange
        var clock = new MovableTimeProvider(new DateTimeOffset(2025, 3, 4, 5, 6, 7, TimeSpan.Zero));
        var (service, store) = Create(clock: clock);
        var draft = new AuditDraft("auth.sign-in-locked", "ann", "Ann", null, null, "account locked");
        await service.TryRecordCoalescedAsync(draft, "k", TestContext.Current.CancellationToken);
        await service.TryRecordCoalescedAsync(draft, "k", TestContext.Current.CancellationToken);
        await service.TryRecordCoalescedAsync(draft, "k", TestContext.Current.CancellationToken);

        // Act
        clock.Advance(TimeSpan.FromMinutes(1));
        var stored = await service.TryRecordCoalescedAsync(draft, "k", TestContext.Current.CancellationToken);
        clock.Advance(TimeSpan.FromMinutes(1));
        var storedAgain = await service.TryRecordCoalescedAsync(draft, "k", TestContext.Current.CancellationToken);

        // Assert: the second row carries the 2 suppressed repeats, the third none
        Assert.True(stored);
        Assert.True(storedAgain);
        Assert.Equal(
            ["account locked", "account locked; 2 more in the minute after the previous entry", "account locked"],
            store.Entries.Select(e => e.Detail).ToArray());
    }

    /// <summary>
    ///     Test that different keys do not share a window.
    /// </summary>
    [Fact]
    public async Task AuditService_TryRecordCoalescedAsync_DifferentKeys_StoreSeparateEntries()
    {
        // Arrange
        var (service, store) = Create(clock: new MovableTimeProvider(DateTimeOffset.UnixEpoch));
        var draft = new AuditDraft("auth.sign-in-failed", null, "unknown user", null, null, "d");

        // Act
        var a = await service.TryRecordCoalescedAsync(draft, "a", TestContext.Current.CancellationToken);
        var b = await service.TryRecordCoalescedAsync(draft, "b", TestContext.Current.CancellationToken);

        // Assert
        Assert.True(a);
        Assert.True(b);
        Assert.Equal(2, store.Entries.Count);
    }

    /// <summary>
    ///     Test that a full key table does not store events of new keys until a window has passed, which bounds the rows per window.
    /// </summary>
    [Fact]
    public async Task AuditService_TryRecordCoalescedAsync_TooManyKeys_DoesNotStoreNewKeysUntilWindowPasses()
    {
        // Arrange
        var clock = new MovableTimeProvider(DateTimeOffset.UnixEpoch);
        var (service, store) = Create(clock: clock);
        var draft = new AuditDraft("auth.sign-in-failed", null, "unknown user", null, null, "d");
        await RecordKeys(service, draft, "key", AuditService.MaxCoalescedKeys);

        // Act
        var whileFull = await service.TryRecordCoalescedAsync(draft, "one-too-many", TestContext.Current.CancellationToken);
        clock.Advance(TimeSpan.FromMinutes(1));
        var afterWindow = await service.TryRecordCoalescedAsync(draft, "one-too-many", TestContext.Current.CancellationToken);

        // Assert: the 500 keys, one overflow row for the suppressed event, and the event of the next window
        Assert.False(whileFull);
        Assert.True(afterWindow);
        Assert.Equal(AuditService.MaxCoalescedKeys + 2, store.Entries.Count);
        Assert.Equal(AuditService.OverflowAction, store.Entries[AuditService.MaxCoalescedKeys].Action);
        Assert.Equal("d", store.Entries[^1].Detail);
    }

    /// <summary>
    ///     Test that a cancelled repeat throws and is not counted as a suppressed repeat.
    /// </summary>
    [Fact]
    public async Task AuditService_TryRecordCoalescedAsync_CancelledRepeat_ThrowsAndDoesNotCountTheRepeat()
    {
        // Arrange
        var clock = new MovableTimeProvider(DateTimeOffset.UnixEpoch);
        var (service, store) = Create(clock: clock);
        var draft = new AuditDraft("auth.sign-in-locked", "ann", "Ann", null, null, "account locked");
        await service.TryRecordCoalescedAsync(draft, "k", TestContext.Current.CancellationToken);

        // Act
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.TryRecordCoalescedAsync(draft, "k", new CancellationToken(true)));
        clock.Advance(TimeSpan.FromMinutes(1));
        await service.TryRecordCoalescedAsync(draft, "k", TestContext.Current.CancellationToken);

        // Assert: the next entry carries no count, because the cancelled repeat was not counted
        Assert.Equal(["account locked", "account locked"], store.Entries.Select(e => e.Detail).ToArray());
    }

    /// <summary>
    ///     Test that a cancelled first event throws and does not start a window, so the key is stored later.
    /// </summary>
    [Fact]
    public async Task AuditService_TryRecordCoalescedAsync_CancelledFirstEvent_ThrowsAndDoesNotStartWindow()
    {
        // Arrange
        var (service, store) = Create(clock: new MovableTimeProvider(DateTimeOffset.UnixEpoch));
        var draft = new AuditDraft("auth.sign-in-locked", "ann", "Ann", null, null, "account locked");

        // Act
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.TryRecordCoalescedAsync(draft, "k", new CancellationToken(true)));
        var emptyAfterCancel = store.Entries.Count == 0;
        var stored = await service.TryRecordCoalescedAsync(draft, "k", TestContext.Current.CancellationToken);

        // Assert: nothing was stored by the cancelled call, and the same key is stored at once afterwards
        Assert.True(emptyAfterCancel);
        Assert.True(stored);
        Assert.Single(store.Entries);
    }

    /// <summary>
    ///     Test that a full table writes one overflow entry and reports once per window, while existing keys still coalesce.
    /// </summary>
    [Fact]
    public async Task AuditService_TryRecordCoalescedAsync_TableFull_WritesOneOverflowRowAndReportsOncePerWindow()
    {
        // Arrange
        var errors = new List<Exception>();
        var (service, store) = Create(errors, new MovableTimeProvider(DateTimeOffset.UnixEpoch));
        var draft = new AuditDraft("auth.sign-in-throttled", null, "not signed in", null, null, "d");
        await RecordKeys(service, draft, "key", AuditService.MaxCoalescedKeys);

        // Act: ten events of new keys, and one more event of a key that is tracked
        var results = new List<bool>();
        for (var i = 0; i < 10; i++)
        {
            results.Add(await service.TryRecordCoalescedAsync(draft, "new" + i, TestContext.Current.CancellationToken));
        }

        var tracked = await service.TryRecordCoalescedAsync(draft, "key0", TestContext.Current.CancellationToken);

        // Assert
        Assert.All(results, Assert.False);
        Assert.False(tracked);
        Assert.Equal(AuditService.MaxCoalescedKeys + 1, store.Entries.Count);
        var overflow = store.Entries[^1];
        Assert.Equal(AuditService.OverflowAction, overflow.Action);
        Assert.Equal("audit service", overflow.ActorName);
        Assert.StartsWith("tracking table full", overflow.Detail, StringComparison.Ordinal);
        var error = Assert.Single(errors);
        Assert.IsType<InvalidOperationException>(error);
        Assert.Contains("table full", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that the overflow entry of the next window carries the number of events suppressed in the previous one.
    /// </summary>
    [Fact]
    public async Task AuditService_TryRecordCoalescedAsync_TableFullNextWindow_OverflowRowCarriesPreviousCount()
    {
        // Arrange: the table is full at t0; four events are suppressed from t0+59 s on, the table is refilled at t0+60 s
        var errors = new List<Exception>();
        var clock = new MovableTimeProvider(DateTimeOffset.UnixEpoch);
        var (service, store) = Create(errors, clock);
        var draft = new AuditDraft("auth.sign-in-throttled", null, "not signed in", null, null, "d");
        await RecordKeys(service, draft, "a", AuditService.MaxCoalescedKeys);
        clock.Advance(TimeSpan.FromSeconds(59));
        await RecordKeys(service, draft, "x", 4);
        clock.Advance(TimeSpan.FromSeconds(1));
        await RecordKeys(service, draft, "b", AuditService.MaxCoalescedKeys);

        // Act: the first overflow window ends at t0+119 s while the table is still full of live keys
        clock.Advance(TimeSpan.FromSeconds(59));
        await RecordKeys(service, draft, "y", 3);

        // Assert: one overflow entry and one report per window, the second one with the count of the first
        var overflow = store.Entries.Where(e => e.Action == AuditService.OverflowAction).ToArray();
        Assert.Equal(2, overflow.Length);
        Assert.Equal("tracking table full, events of new keys are not recorded", overflow[0].Detail);
        Assert.Equal("4 events suppressed in the previous window, tracking table full", overflow[1].Detail);
        Assert.Equal(2, errors.Count);
    }

    /// <summary>
    ///     Test that a failing store and a failing error hook cannot make the overflow handling throw.
    /// </summary>
    [Fact]
    public async Task AuditService_TryRecordCoalescedAsync_TableFullAndHookThrows_StillReturnsFalse()
    {
        // Arrange
        var store = new FakeAuditStore(new FixedTimeProvider(new DateTimeOffset(2025, 3, 4, 5, 6, 7, TimeSpan.Zero)));
        var service = new AuditService(
            store,
            _ => throw new InvalidOperationException("hook failed"),
            new MovableTimeProvider(DateTimeOffset.UnixEpoch));
        var draft = new AuditDraft("auth.sign-in-throttled", null, "not signed in", null, null, "d");
        await RecordKeys(service, draft, "key", AuditService.MaxCoalescedKeys);
        store.FailNextAppend = new InvalidOperationException("disk full");

        // Act
        var result = await service.TryRecordCoalescedAsync(draft, "new", TestContext.Current.CancellationToken);

        // Assert: nothing escaped, and nothing but the 500 entries was stored
        Assert.False(result);
        Assert.Equal(AuditService.MaxCoalescedKeys, store.Entries.Count);
    }

    /// <summary>
    ///     Test that a storage failure of a coalesced record is swallowed and reported like any best-effort record.
    /// </summary>
    [Fact]
    public async Task AuditService_TryRecordCoalescedAsync_StoreFailure_IsSwallowedAndReported()
    {
        // Arrange
        var errors = new List<Exception>();
        var (service, store) = Create(errors, new MovableTimeProvider(DateTimeOffset.UnixEpoch));
        store.FailNextAppend = new InvalidOperationException("disk full");

        // Act
        var stored = await service.TryRecordCoalescedAsync(
            new AuditDraft("auth.sign-in-locked", null, "x", null, null, "d"),
            "k",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.False(stored);
        Assert.Empty(store.Entries);
        Assert.Equal("disk full", Assert.Single(errors).Message);
    }

    /// <summary>
    ///     Test that a null draft or key is refused by the coalescing record.
    /// </summary>
    [Fact]
    public async Task AuditService_TryRecordCoalescedAsync_NullArguments_Throw()
    {
        // Arrange
        var (service, _) = Create();
        var draft = AuditDraft.By(TestActors.Ann, "a.b", "c");

        // Act / Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => service.TryRecordCoalescedAsync(null!, "k", TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentNullException>(() => service.TryRecordCoalescedAsync(draft, null!, TestContext.Current.CancellationToken));
    }
}
