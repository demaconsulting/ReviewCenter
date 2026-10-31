### AuditService

#### Verification Approach

`AuditService` is verified with unit tests defined in `AuditServiceTests.cs`, and its export with `AuditExportTests.cs`
(the service) and `AuditExportWriterTests.cs` (the pure CSV and JSON writer, no HTTP and no store). The tests use the
in-memory fake store
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
- The first page shows the newest 50 entries (the default size) with only an Older link; paging older covers every entry
  once; paging newer
  returns the page before; a trail of exactly one page has no links; an empty trail gives an empty page; a review filter
  returns only that review, also over more than one page; a project filter without a review returns every event of that
  project and none of another project or without a project; when both positions are given the older one wins; a page
  size of less than one gives 50, and a size above 200 gives 200.
- The filter narrows the listing by project, review, actor identity, actor text, exact action, group of actions, and
  range of days, alone and together, and keeps the paging links right over the matching entries; a project filter
  never returns an entry without a project; an invalid filter (a text that is too long, From after To) is refused with
  `AuditFilterInvalid` before the store is called; texts are trimmed and an empty text means no condition; the day
  range starts at the start of the From day and ends at the start of the day after To.
- The vocabulary of `AuditActions` is sorted, has no duplicates, every action has a known group, and the list equals the
  set of action codes that the product sources record, so a new action that is not added to the list fails a test.
- Verification of an untouched trail is intact and reads in batches; an empty trail is intact; a tampered entry in a
  later batch is reported with its sequence.
- The export writes the entries that match the filter once, in ascending sequence order, in batches of 500 (a trail of
  more than 1,200 entries is read in three batches), up to the sequence that was newest when it was prepared (an entry
  appended before the first batch, or while streaming, is not in it); a project export holds no entry without a
  project; an empty trail gives a valid document; an invalid filter is refused before anything is read.
- The status of the chain is known after preparing and before writing: intact for an untouched trail, and broken with
  the first broken sequence for a tampered one, which is still exported; a project export reveals no count or head
  hash and nothing of other projects.
- Every export, also a cancelled one and one whose target fails, records one `audit.exported` entry after the stream,
  for the exporting user, with the project of the filter or none, the format, the number of rows written, complete or
  incomplete, and the filter summary; a failing append is reported to the hook and does not fail the export.
- The CSV has the byte order mark, the nine columns, CRLF, RFC 4180 quoting, UTC timestamps, and, for the text cells
  only, the apostrophe before a leading `=`, `+`, `-`, `@`, tab or carriage return, so hashes stay unchanged; the JSON
  document has the export, chain, and entries objects, escapes hostile text, and is valid when empty.
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

**AuditService_ListAsync_ProjectOnly_ReturnsOnlyThatProjectsEvents**: A trail holds four events of project 1 in
several reviews, one event of project 2, and one event of no project. Listing project 1 without a review returns the
four events newest first, and none of the others. This scenario is tested by
`AuditService_ListAsync_ProjectOnly_ReturnsOnlyThatProjectsEvents`.

**AuditService_ListAsync_BothPositions_BeforeWins**: With both positions given the older page is returned. This scenario
is tested by `AuditService_ListAsync_BothPositions_BeforeWins`.

**AuditService_ListAsync_PageSize_DefaultsAndIsCapped**: A trail of 250 entries is listed without a size, with a size
of zero and a negative size (50 entries each), with 25, and with 1,000 (200 entries). This scenario is tested by
`AuditService_ListAsync_PageSize_DefaultsAndIsCapped`.

**AuditService_ListAsync_Filter_NarrowsByEachFieldAndKeepsProjectScoping**: Each field of the filter is set alone and
together with another; only the matching entries are returned, and a filter with a project never returns an entry
without one. This scenario is tested by `AuditService_ListAsync_Filter_NarrowsByEachFieldAndKeepsProjectScoping`.

**AuditService_ListAsync_FilterWithPaging_LinksFollowTheMatchingEntries**: A filtered listing is paged older and newer;
the links follow the matching entries only. This scenario is tested by
`AuditService_ListAsync_FilterWithPaging_LinksFollowTheMatchingEntries`.

**AuditService_ListAsync_InvalidFilter_IsRefused**: A filter with a From date after the To date, and one with a text
that is too long, are refused with `AuditFilterInvalid`. This scenario is tested by
`AuditService_ListAsync_InvalidFilter_IsRefused`.

**AuditFilter_Validated_TextsAreTrimmedAndEmptyBecomesNull**: The texts of a filter are trimmed and an empty text becomes
`null`. This scenario is tested by `AuditFilter_Validated_TextsAreTrimmedAndEmptyBecomesNull` and
`AuditFilter_None_ValidatesToItself`.

**AuditFilter_Validated_TextTooLong_IsRefused**: A text longer than the field it is matched with is refused. This
scenario is tested by `AuditFilter_Validated_TextTooLong_IsRefused`.

**AuditFilter_Validated_ReversedRange_IsRefusedAndSingleDayAccepted**: A From date after the To date is refused, and one
day (From equal to To) is accepted. This scenario is tested by
`AuditFilter_Validated_ReversedRange_IsRefusedAndSingleDayAccepted`.

**AuditFilter_Range_StartOfFromDayAndStartOfDayAfterTo**: The instants of the range are the start of the From day and the
start of the day after the To day, so the end day is included. This scenario is tested by
`AuditFilter_Range_StartOfFromDayAndStartOfDayAfterTo`.

**AuditActions_All_IsSortedUniqueAndWellFormed**: The list of actions is sorted, has no duplicate, and every action is
written as a group, a dot, and a name. This scenario is tested by `AuditActions_All_IsSortedUniqueAndWellFormed`,
`AuditActions_Prefixes_AreTheGroupsOfTheActions`, and `AuditActions_IsKnown_AcceptsOnlyListedValues`.

**AuditActions_All_MatchesTheActionsTheSourcesRecord**: The action codes found in the product sources equal the list,
so a
new action that is not listed fails the test. This scenario is tested by
`AuditActions_All_MatchesTheActionsTheSourcesRecord`.

**AuditService_VerifyAsync_UntouchedTrail_IsIntactAndReadInBatches**: A trail larger than a batch verifies and the store
was read in more than one batch. This scenario is tested by
`AuditService_VerifyAsync_UntouchedTrail_IsIntactAndReadInBatches` and `AuditService_VerifyAsync_EmptyTrail_IsIntact`.

**AuditService_VerifyAsync_TamperedEntryInLaterBatch_ReportsItsSequence**: An entry in the second batch is altered; the
result is broken at that entry. This scenario is tested by
`AuditService_VerifyAsync_TamperedEntryInLaterBatch_ReportsItsSequence`.

**AuditExport_ExportAsync_Filter_WritesMatchingEntriesAscending**: A trail of ten entries over three projects is
exported with a project filter; the CSV holds that project's entries in ascending order. This scenario is tested by
`AuditExport_ExportAsync_Filter_WritesMatchingEntriesAscending`; the scope by
`AuditExport_ExportAsync_Scope_ProjectNeverHoldsGlobalRows`, which also shows that an export without a project holds
every entry including the audit entry of the first export; the empty trail by
`AuditExport_ExportAsync_EmptyTrail_IsValidAndAudited`; the summary of the filter by
`AuditFilter_Describe_SetParts_AreListedInOrder`; and the refusals by
`AuditExport_PrepareExportAsync_InvalidFilter_ThrowsDomainRule` and `AuditExport_NullArguments_Throw`.

**AuditExport_ExportAsync_EntriesAppendedAfterPrepare_AreNotIncluded**: An entry is appended after the export was
prepared and another while the first batch is read (through the hook of the fake store); neither is in the output.
This scenario is tested by `AuditExport_ExportAsync_EntriesAppendedAfterPrepare_AreNotIncluded`.

**AuditExport_ExportAsync_MoreThanOneBatch_ReadsInBatchesOf500**: 1,201 entries are exported with three reads, none
larger than 500, and 1,000 entries end after an empty read. This scenario is tested by
`AuditExport_ExportAsync_MoreThanOneBatch_ReadsInBatchesOf500` and
`AuditExport_ExportAsync_ExactlyWholeBatches_EndsAfterAnEmptyRead`.

**AuditExport_PrepareExportAsync_TamperedChain_ReportsBrokenAtFirstBadSequence**: A stored entry is altered; the
prepared export is broken at its sequence, an untouched trail is intact, the file name says so, and a project export
carries no count of the other projects. This scenario is tested by
`AuditExport_PrepareExportAsync_TamperedChain_ReportsBrokenAtFirstBadSequence`,
`AuditExport_PrepareExportAsync_IntactChain_ReportsIntact`,
`AuditExport_ExportAsync_ProjectExport_RevealsNoCountsOfOtherProjects`, and
`AuditExport_FileNameAndContentType_FollowScopeFormatAndStatus`.

**AuditExport_ExportAsync_ProjectScope_RecordsAuditEntryAfterStream**: After a project export the trail ends with an
`audit.exported` entry of the exporting user, scoped to the project, naming the format, the row count, `complete`, and
the filter; a global export records one without a project. This scenario is tested by
`AuditExport_ExportAsync_ProjectScope_RecordsAuditEntryAfterStream`,
`AuditExport_ExportAsync_GlobalScope_RecordsEntryWithoutProject`, and
`AuditExport_ExportedAction_IsInTheActionCatalog`. A filter with every text at its maximum length gives a detail that
the store has to cut, so the order of the summary keeps the dates and drops the end of the actor text; this is tested
by `AuditExport_ExportAsync_MaximalFilter_DetailFitsAndKeepsCountAndDates`, which asserts that the stored detail is
within 500 characters and still starts with the format, the row count, the state, and the date range.

**AuditExport_ExportAsync_CancelledMidStream_RecordsIncompleteEntry**: The token is cancelled while the second batch
is read; the export throws, and the entry says `500 rows, incomplete`. A failing target is recorded the same way and
rethrown, and a failing append is reported to the hook. This scenario is tested by
`AuditExport_ExportAsync_CancelledMidStream_RecordsIncompleteEntry`,
`AuditExport_ExportAsync_TargetFails_RecordsIncompleteEntryAndRethrows`, and
`AuditExport_ExportAsync_AuditAppendFails_ReportsErrorAndStillCompletes`.

**AuditExportWriter_Csv_StartsWithBomAndHeader_AndUsesCrlf**: The CSV starts with the byte order mark and the header
row, and every line ends with CR LF. This scenario is tested by
`AuditExportWriter_Csv_StartsWithBomAndHeader_AndUsesCrlf`, `AuditExportWriter_Csv_Row_HasColumnsInOrderAndUtcTimestamp`,
`AuditExportWriter_Csv_NoEntries_IsHeaderOnly`, `AuditExportWriter_Csv_NoProject_LeavesProjectCellEmpty`, and
`AuditExportWriter_Csv_Unicode_IsWrittenAsUtf8`.

**AuditExportWriter_Quote_SpecialCharacters_QuotedPerRfc4180**: A comma, a quote, a carriage return and a line feed make
a quoted cell with doubled quotes, and plain text stays unquoted. This scenario is tested by
`AuditExportWriter_Quote_SpecialCharacters_QuotedPerRfc4180` and
`AuditExportWriter_Csv_DetailWithCommaQuoteAndNewline_IsOneQuotedCell`.

**AuditExportWriter_Neutralize_LeadingFormulaCharacter_IsPrefixed**: Cells that start with `=cmd|' /C calc'!A0`, `+1`,
`-2`, `@SUM`, a tab or a carriage return get an apostrophe; benign text is untouched; the hashes, the sequence and the
timestamp of a row are not changed. This scenario is tested by
`AuditExportWriter_Neutralize_LeadingFormulaCharacter_IsPrefixed` and
`AuditExportWriter_Csv_TextCells_AreNeutralizedAndGeneratedCellsAreNot`.

**AuditExportWriter_Json_Envelope_HasExportChainAndEntries**: The JSON document has `export` (with only the set
filter conditions), `chain`, and `entries`; hostile text is escaped and round-trips; an empty result and a broken chain
(first broken sequence, no count, no head hash) are valid documents; the buffer is emptied by every flush; null
arguments are refused. This scenario is tested by `AuditExportWriter_Json_Envelope_HasExportChainAndEntries`,
`AuditExportWriter_Json_HostileText_IsEscapedAndRoundTrips`, `AuditExportWriter_Json_NoEntries_IsValidDocument`,
`AuditExportWriter_Json_BrokenChain_NamesFirstBrokenSequenceAndNoCounts`,
`AuditExportWriter_FlushToAsync_EmptiesTheBufferEachTime`, `AuditExportWriter_NullArguments_Throw`, and
`AuditExportWriter_Dispose_ReleasesBuffersAndIsRepeatable` (the writer can be disposed twice and refuses writes after).
