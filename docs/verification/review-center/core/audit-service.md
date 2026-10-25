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
