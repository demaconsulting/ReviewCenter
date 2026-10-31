### AuditStore

#### Verification Approach

`AuditStore` and `AuditAppender` are verified with tests defined in `AuditStoreTests.cs`, which run against a real
SQLite database file created by the test (`TestStore`), because the properties to verify are those of
the database: transactions, rollback, and the primary key. The tests append and read through the real stores, and read
the rows again over a second connection (`Microsoft.Data.Sqlite`) to damage them (edit or delete a row) and to check
what was really stored. The chain is checked with the real `AuditChainVerifier` through the real `AuditService`.
Concurrency is verified by starting many appends at once, from one store and from different stores (`ReviewStore`,
`UserStore`, and `AuditStore` share the appender), and checking that the stored sequence has no gap and the chain
verifies. Failure injection uses a database trigger that aborts the audit insert, and a draft function that throws,
and then checks that the change was rolled back and that the gate was released (a later call does not block). The
absence of an update or delete operation is checked on the public surface by reflection. The filter, the date range, and
the stored form of the time are verified in `AuditStoreFilterTests.cs` against the same kind of database, with the
entries written directly through the appender with chosen times and actors, and the indexes in
`StorageSubsystemTests.cs`. The two reads of the export are verified in `AuditStoreExportTests.cs`.

#### Test Environment

N/A - standard test environment (temporary SQLite databases are created by the test helper).

#### Acceptance Criteria

- All tests pass with zero failures.
- Sequential appends have the sequences one, two, three and so on, are linked by their hashes, and verify; the stored
  entry has the resolved user identity, the cleaned text, and the time of the clock.
- A project, a review, or a user is stored together with its audit entry; a change that fails stores no entry; a
  draft function that throws stores nothing; an audit insert that fails rolls back the change and releases the gate.
- A change that has no effect (a no-op) records nothing, and a user update without a draft records nothing.
- Concurrent appends, also from different stores, produce one linear chain without gap or fork.
- A second entry with an existing sequence is refused by the database key.
- The newest-first listing honors the review filter and both cursors; the batch read is ascending and bounded.
- Each field of the filter selects the matching entries alone and together; the actor text is case-insensitive and
  matches `%`, `_`, and `\` literally; an action prefix matches literally; the day range includes the first instant of
  the From day and the last instant of the To day and excludes the instant before and the next day; the filtered
  pages follow the cursors over the matches only; a filter with a project never returns an entry without one; and a
  filtered page of a trail of 3,000 entries is read within a loose time bound.
- The time of an entry is stored as fixed-width UTC text, whatever offset it was written with, and the chain still
  verifies; a database created now has the four indexes of the table.
- The listing for a project returns only that project's entries, none of another project or without a project, and the
  chain over all entries still verifies.
- The last activity of a project is the time of the newest entry of each of its reviews, without entries that have no
  review or belong to another project, and does not change the chain.
- An edited or a deleted row is found by verification, at its sequence.
- The store offers no update or delete operation beyond the reads listed in the public surface test.
- The newest sequence is 0 for an empty trail and the highest sequence otherwise; the matching read honors the filter,
  returns ascending keyset batches after a sequence and up to a snapshot sequence, never returns an entry above the
  snapshot, and never returns an entry without a project for a project filter.

#### Test Scenarios

**AuditStore_AppendAsync_SequentialAppends_GapFreeLinkedAndVerifiable**: Several entries are appended one after another;
they have consecutive sequences, each links to the hash of the previous one, and the chain verifies. This scenario is
tested by `AuditStore_AppendAsync_SequentialAppends_GapFreeLinkedAndVerifiable`.

**AuditStore_AppendAsync_Draft_StoresResolvedActorCleanedTextAndTime**: A draft with an existing user name, padded text,
and a line break is appended; the stored entry has the user identity, the cleaned text, and the clock time. This
scenario is tested by `AuditStore_AppendAsync_Draft_StoresResolvedActorCleanedTextAndTime`.

**AuditStore_AppendAsync_ConcurrentAppends_ProduceLinearChain**: Many appends are started at once; the chain is linear
and verifies. This scenario is tested by `AuditStore_AppendAsync_ConcurrentAppends_ProduceLinearChain`.

**AuditStore_ConcurrentChangesFromDifferentStores_ProduceLinearChain**: Changes from the review store, the user store,
and the audit store run at once; the chain is linear and verifies, which shows the gate is shared. This scenario is
tested by `AuditStore_ConcurrentChangesFromDifferentStores_ProduceLinearChain`.

**AuditStore_Database_DuplicateSequence_IsRefusedByUniqueKey**: A row with an existing sequence is inserted directly
into the table; the database refuses it. This scenario is tested by
`AuditStore_Database_DuplicateSequence_IsRefusedByUniqueKey`.

**ReviewStore_AddProjectAsync_WithDraft_StoresProjectAndEntryTogether**: A project is stored with its draft; the project
and the entry exist afterwards. This scenario is tested by
`ReviewStore_AddProjectAsync_WithDraft_StoresProjectAndEntryTogether`.

**ReviewStore_AddReviewAsync_FailedChange_LeavesNoAuditEntry**: A review that breaks a rule (a duplicate identifier) is
refused and no entry is stored. This scenario is tested by `ReviewStore_AddReviewAsync_FailedChange_LeavesNoAuditEntry`.

**ReviewStore_AddProjectAsync_DraftFactoryThrows_StoresNothing**: The function that builds the draft throws; neither
the project nor an entry is stored. This scenario is tested by
`ReviewStore_AddProjectAsync_DraftFactoryThrows_StoresNothing`.

**ReviewStore_AddProjectAsync_AuditInsertFails_RollsBackTheChange**: The audit insert fails; the project is rolled back.
This scenario is tested by `ReviewStore_AddProjectAsync_AuditInsertFails_RollsBackTheChange`, and for a user, including
the release of the gate, by `UserStore_AddAsync_AuditInsertFails_RollsBackTheUserAndTheGateIsReleased`.

**ReviewStore_NoOpChanges_RecordNothing**: A reply to a missing thread, a state change of a missing thread, and the
removal of a missing slot return no result and append no entry; a user update without a draft appends none either.
This scenario is tested by
`ReviewStore_NoOpChanges_RecordNothing` and `UserStore_UpdateAsync_WithAndWithoutDraft_RecordsOnlyWithDraft`.

**AuditStore_ListAsync_FilterAndCursors_ReturnExpectedEntriesNewestFirst**: Entries of several reviews are listed with
and without the review filter and with both cursors; the result is newest first. This scenario is tested by
`AuditStore_ListAsync_FilterAndCursors_ReturnExpectedEntriesNewestFirst`.

**AuditStore_ListAsync_ProjectOnly_IsIsolatedAndTheChainStillVerifies**: Entries of project one (one with a review),
of project two, and without a project are appended; the listing for project one returns its two entries newest
first, project two returns its one, a project without entries returns none, and the chain over all four entries
verifies. This scenario is tested by `AuditStore_ListAsync_ProjectOnly_IsIsolatedAndTheChainStillVerifies`.

**AuditStore_ListAsync_EachField_SelectsMatchingEntries**: Entries that differ in project, review, actor, action, and
time are listed with one field of the filter at a time; only the matching entries are returned, newest first. This
scenario is tested by `AuditStore_ListAsync_EachField_SelectsMatchingEntries`, and with several fields set at once by
`AuditStore_ListAsync_CombinedFields_AllMustMatch`.

**AuditStore_ListAsync_ProjectFilter_NeverReturnsGlobalEntries**: Entries without a project are never returned for a
filter with a project. This scenario is tested by `AuditStore_ListAsync_ProjectFilter_NeverReturnsGlobalEntries`.

**AuditStore_ListAsync_ActorText_MatchesLiterallyAndIgnoresCase**: Actor names with `%`, `_`, and `\` are searched for
with those characters; they match literally and not as wildcards, and letters match without regard to case. This
scenario is tested by `AuditStore_ListAsync_ActorText_MatchesLiterallyAndIgnoresCase`, and the group of actions by
`AuditStore_ListAsync_ActionPrefix_MatchesLiterally`.

**AuditStore_ListAsync_DateRange_DayBoundariesAreInclusive**: Entries just before the first instant of a day, at it, at
the last instant of the day, and at the first instant of the next day (some written with an offset) are listed for
that day; the two inside are returned. This scenario is tested by
`AuditStore_ListAsync_DateRange_DayBoundariesAreInclusive`.

**AuditStore_Timestamp_StoredAsUtcTextAndKeepsTheChainVerifiable**: Entries written with and without an offset are
stored as fixed-width UTC text, read back as the same instants, and the chain verifies. This scenario is tested by
`AuditStore_Timestamp_StoredAsUtcTextAndKeepsTheChainVerifiable`.

**AuditStore_ListAsync_FilterWithCursors_PagesThroughMatchesOnly**: A filtered listing is paged older and newer; only
the matching entries are visited, each once. This scenario is tested by
`AuditStore_ListAsync_FilterWithCursors_PagesThroughMatchesOnly`.

**AuditStore_ListAsync_LargeTrail_FilteredPagesAreFastAndBounded**: A trail of 3,000 entries is filtered by project,
actor, action, and dates; each page is no larger than requested and the read takes well under the loose bound. This
scenario is tested by `AuditStore_ListAsync_LargeTrail_FilteredPagesAreFastAndBounded`.

**StorageSubsystem_NewDatabase_HasTheAuditEntryIndexes**: A database that is created now has the indexes on project and
sequence, actor and sequence, action and sequence, and project, review, and sequence. This scenario is tested by
`StorageSubsystem_NewDatabase_HasTheAuditEntryIndexes`.

**AuditStore_LastActivityAsync_Entries_NewestPerReviewOfOneProject**: Entries of two reviews of project one, one entry of
the project without a review, and one entry of another project are appended. The last activity of project one names the
time of the newest entry of each of its two reviews and nothing else, a project without entries gives an empty result,
and the chain still verifies. This scenario is tested by `AuditStore_LastActivityAsync_Entries_NewestPerReviewOfOneProject`.

**AuditStore_ReadAfterAsync_Batches_AreAscendingAndBounded**: Entries are read in batches after a sequence; each batch is
ascending and not larger than requested. This scenario is tested by
`AuditStore_ReadAfterAsync_Batches_AreAscendingAndBounded`.

**AuditStore_Verification_EditedRow_ReportsThatSequence**: A row is edited in the database; verification reports its
sequence. This scenario is tested by `AuditStore_Verification_EditedRow_ReportsThatSequence`.

**AuditStore_Verification_DeletedRow_ReportsThatSequence**: A row in the middle is deleted in the database;
verification reports the missing sequence. This scenario is tested by
`AuditStore_Verification_DeletedRow_ReportsThatSequence`.

**AuditStore_PublicSurface_OffersNoUpdateOrDelete**: The public methods of the store and the interface are inspected;
none updates or deletes an entry. This scenario is tested by `AuditStore_PublicSurface_OffersNoUpdateOrDelete`.

**AuditStore_ReadMatchingAsync_FilterAndSnapshot_AscendingKeysetBatches**: A trail with entries of two projects and
without a project is read with a project filter in batches of two, passing the last sequence each time and a snapshot
sequence below the newest; the batches are ascending, bounded, hold only the project, and stop at the snapshot. The
newest sequence of an empty trail is 0 and of a filled trail the highest sequence. This scenario is tested by
`AuditStore_ReadMatchingAsync_FilterAndSnapshot_AscendingKeysetBatches` and
`AuditStore_LatestSequenceAsync_EmptyThenFilled_ReturnsHighestSequence`.
