### AuditService

#### Verification Approach

`AuditService` is verified with unit tests defined in `AuditServiceTests.cs`. The tests use the in-memory fake store
(`FakeAuditStore`), which implements the same contract as the real store and can be made to fail the next append, to
count reads and the largest batch, and to alter a stored entry, so that failure handling, paging, and batched
verification are checked without a database. The
real store, its transactions, and its gate are verified by the `AuditStore` unit. The paging tests use trails of more
than one page; the verification tests use a batch size smaller than the trail so that a later batch is read.

#### Test Environment

N/A - standard test environment.

#### Acceptance Criteria

- All unit tests pass with zero failures.
- A recorded draft becomes the next entry and the resulting chain verifies; a strict record lets store failures
  propagate and refuses a null draft.
- The best-effort record returns `true` on success; on a store failure it returns `false`, calls the error hook once,
  survives a failing or missing hook, and lets cancellation propagate.
- The first page shows the newest 50 entries with only an Older link; paging older covers every entry once; paging newer
  returns the page before; a trail of exactly one page has no links; an empty trail gives an empty page; a review filter
  returns only that review, also over more than one page; when both positions are given the older one wins.
- Verification of an untouched trail is intact and reads in batches; an empty trail is intact; a tampered entry in a
  later batch is reported with its sequence.
- The coalescing record stores the first event of a key in a minute and does not store the repeats; the first event of
  the next minute is stored with the count of the repeats; different keys do not share a minute; a full key table does
  not store new keys until a minute has passed, and writes one `audit.events-suppressed` entry and one error report per
  minute (the next one carries the previous count); a cancelled call throws before it changes the table; a store
  failure is swallowed and reported; null arguments are refused.

#### Test Scenarios

**AuditService_RecordAsync_Draft_StoresEntryThatVerifies**: A draft is recorded and the trail verifies. This scenario is
tested by `AuditService_RecordAsync_Draft_StoresEntryThatVerifies`; the constructor and the failure of the strict
operation by `AuditService_Constructor_NullStore_Throws` and `AuditService_RecordAsync_NullDraftOrStoreFailure_Throws`.

**AuditService_TryRecordAsync_Draft_StoresEntryAndReturnsTrue**: The best-effort operation stores the entry and returns
`true`. This scenario is tested by `AuditService_TryRecordAsync_Draft_StoresEntryAndReturnsTrue`.

**AuditService_TryRecordAsync_StoreFailure_IsSwallowedAndReported**: The store fails; the call returns `false`, no
exception reaches the caller, and the hook receives the failure. This scenario is tested by
`AuditService_TryRecordAsync_StoreFailure_IsSwallowedAndReported` and
`AuditService_TryRecordAsync_FailingOrMissingHook_StillReturnsFalse`.

**AuditService_TryRecordAsync_Cancellation_Propagates**: A cancelled call is not swallowed, and a null draft is
refused. This scenario is tested by `AuditService_TryRecordAsync_Cancellation_Propagates` and
`AuditService_TryRecordAsync_NullDraft_Throws`.

**AuditService_TryRecordCoalescedAsync_RepeatsWithinWindow_StoresOnlyTheFirst**: The same key is recorded three times
within one minute; the first call returns `true`, the others `false`, and one entry is stored. This scenario is tested
by `AuditService_TryRecordCoalescedAsync_RepeatsWithinWindow_StoresOnlyTheFirst`.

**AuditService_TryRecordCoalescedAsync_NextWindow_StoresEntryWithSuppressedCount**: After three events of a key, the
clock is moved by one minute and the key is recorded again; the new entry carries the text "; 2 more in the minute
after the previous entry". After another minute the next entry carries no count. This scenario is tested by
`AuditService_TryRecordCoalescedAsync_NextWindow_StoresEntryWithSuppressedCount`.

**AuditService_TryRecordCoalescedAsync_DifferentKeys_StoreSeparateEntries**: Two keys are recorded; both are stored.
This scenario is tested by `AuditService_TryRecordCoalescedAsync_DifferentKeys_StoreSeparateEntries`.

**AuditService_TryRecordCoalescedAsync_TooManyKeys_DoesNotStoreNewKeysUntilWindowPasses**: The table is filled with the
largest number of keys; a new key is not stored (only the overflow entry is), and is stored after the minute has
passed. This scenario is tested by
`AuditService_TryRecordCoalescedAsync_TooManyKeys_DoesNotStoreNewKeysUntilWindowPasses`.

**AuditService_TryRecordCoalescedAsync_TableFull_WritesOneOverflowRowAndReportsOncePerWindow**: With a full table,
several events of new keys write exactly one `audit.events-suppressed` entry and call the error hook once. This
scenario is tested by `AuditService_TryRecordCoalescedAsync_TableFull_WritesOneOverflowRowAndReportsOncePerWindow`.

**AuditService_TryRecordCoalescedAsync_TableFullNextWindow_OverflowRowCarriesPreviousCount**: After an overflow window
that suppressed events, the next overflow window writes a new entry that carries the number suppressed before. This
scenario is tested by `AuditService_TryRecordCoalescedAsync_TableFullNextWindow_OverflowRowCarriesPreviousCount`.

**AuditService_TryRecordCoalescedAsync_TableFullAndHookThrows_StillReturnsFalse**: The error hook throws while the
table is full; the call still returns `false` and throws nothing. This scenario is tested by
`AuditService_TryRecordCoalescedAsync_TableFullAndHookThrows_StillReturnsFalse`.

**AuditService_TryRecordCoalescedAsync_CancelledRepeat_ThrowsAndDoesNotCountTheRepeat**: A repeat with a cancelled token
throws `OperationCanceledException` and is not counted, so the next window shows the right count. This scenario is
tested by `AuditService_TryRecordCoalescedAsync_CancelledRepeat_ThrowsAndDoesNotCountTheRepeat`.

**AuditService_TryRecordCoalescedAsync_CancelledFirstEvent_ThrowsAndDoesNotStartWindow**: A first event with a
cancelled token throws and starts no window, so a later event of the key is stored. This scenario is tested by
`AuditService_TryRecordCoalescedAsync_CancelledFirstEvent_ThrowsAndDoesNotStartWindow`.

**AuditService_TryRecordCoalescedAsync_StoreFailure_IsSwallowedAndReported**: The store fails; the call returns `false`,
nothing is stored, and the hook receives the failure. This scenario is tested by
`AuditService_TryRecordCoalescedAsync_StoreFailure_IsSwallowedAndReported`.

**AuditService_TryRecordCoalescedAsync_NullArguments_Throw**: A null draft and a null key are each refused with an
`ArgumentNullException`. This scenario is tested by `AuditService_TryRecordCoalescedAsync_NullArguments_Throw`.

**AuditService_ListAsync_FirstPage_IsNewestFirstWithOlderOnly**: A trail longer than a page shows the newest 50 entries
with an Older link and no Newer link. This scenario is tested by
`AuditService_ListAsync_FirstPage_IsNewestFirstWithOlderOnly`.

**AuditService_ListAsync_PagingOlder_CoversEveryEntryOnce**: Following the Older links visits every entry exactly once.
This scenario is tested by `AuditService_ListAsync_PagingOlder_CoversEveryEntryOnce`.

**AuditService_ListAsync_PagingNewer_ReturnsThePageBefore**: The Newer link returns to the page that was left. This
scenario is tested by `AuditService_ListAsync_PagingNewer_ReturnsThePageBefore`.

**AuditService_ListAsync_ExactlyOnePage_HasNoLinks**: A trail of exactly 50 entries has neither link, and an empty trail
gives an empty page. This scenario is tested by `AuditService_ListAsync_ExactlyOnePage_HasNoLinks` and
`AuditService_ListAsync_EmptyTrail_GivesEmptyPage`.

**AuditService_ListAsync_ReviewFilter_ReturnsOnlyThatReview**: A review filter shows the entries of that review only,
also across pages. This scenario is tested by `AuditService_ListAsync_ReviewFilter_ReturnsOnlyThatReview` and
`AuditService_ListAsync_ReviewFilterOverFiftyEntries_Pages`.

**AuditService_ListAsync_BothPositions_BeforeWins**: With both positions given the older page is returned. This scenario
is tested by `AuditService_ListAsync_BothPositions_BeforeWins`.

**AuditService_VerifyAsync_UntouchedTrail_IsIntactAndReadInBatches**: A trail larger than a batch verifies and the store
was read in more than one batch. This scenario is tested by
`AuditService_VerifyAsync_UntouchedTrail_IsIntactAndReadInBatches` and `AuditService_VerifyAsync_EmptyTrail_IsIntact`.

**AuditService_VerifyAsync_TamperedEntryInLaterBatch_ReportsItsSequence**: An entry in the second batch is altered; the
result is broken at that entry. This scenario is tested by
`AuditService_VerifyAsync_TamperedEntryInLaterBatch_ReportsItsSequence`.
