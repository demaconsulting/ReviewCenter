### AuditChain

#### Verification Approach

`AuditChain` is verified with unit tests defined in `AuditChainTests.cs` and `AuditTextTests.cs`. The tests need no
database and no clock: they build `AuditEntry` values by hand. The encoding is verified with two **pinned known-answer
hashes** (one entry with every field and one without the optional fields) that were computed independently of the
code under test, so that an accidental change of the encoding is caught, and with property tests that change one field
at a time and move text between adjacent fields. The verifier is verified by building a valid chain of entries with
the real hash function and then damaging it the ways a person with database access could: editing an entry, editing an
entry and recomputing its hash, deleting an entry in the middle or the first entry, reordering, inserting, and
duplicating entries. The cleaning of text and the short hash are verified with ordinary, empty, padded, control, long,
and surrogate texts.

#### Test Environment

N/A - standard test environment.

#### Acceptance Criteria

- All unit tests pass with zero failures.
- The genesis hash is 64 zeros, and the hash of an entry is 64 lowercase hexadecimal characters that match the pinned
  values, ignore the stored hash, and do not depend on the offset of the time.
- Changing any field changes the hash; a missing review and an empty review differ; text moved between adjacent
  fields changes the hash; Unicode text is hashed.
- An empty chain is intact with the genesis hash as head; a valid chain is intact with the last hash as head.
- An edited entry is reported as that entry; an edited entry whose hash was recomputed is reported at the next entry;
  a deleted entry is reported at the missing sequence; reordering, insertion, duplication, a wrong previous hash, and a
  first entry with another sequence are all reported; with two bad entries the first is reported; after a failure the
  verifier stays failed and keeps the last good head.
- Text is trimmed, control characters and line breaks become spaces, lone surrogates are replaced, a long text is cut
  with an ellipsis within the limit without splitting a pair, and a limit below four is refused.
- The short hash is the prefix of the SHA-256 of the trimmed text.
- A draft names a signed-in actor as the display name and user name, and the command line by its display name only.

#### Test Scenarios

**AuditChain_ComputeHash_KnownEntry_MatchesPinnedValue**: The hash of an entry with every field set equals the pinned
value. This scenario is tested by `AuditChain_ComputeHash_KnownEntry_MatchesPinnedValue` and, for an entry without the
optional fields, by `AuditChain_ComputeHash_KnownEntryWithoutOptionalFields_MatchesPinnedValue`.

**AuditChain_ComputeHash_AnyFieldChanged_ChangesHash**: Each field is changed in turn and the hash differs each time.
This scenario is tested by `AuditChain_ComputeHash_AnyFieldChanged_ChangesHash`.

**AuditChain_ComputeHash_TextMovedBetweenAdjacentFields_ChangesHash**: Text that moves from the end of one field to the
start of the next gives another hash, which shows the length prefixes work. This scenario is tested by
`AuditChain_ComputeHash_TextMovedBetweenAdjacentFields_ChangesHash` and
`AuditChain_ComputeHash_TextMovedFromDetailToReview_ChangesHash`.

**AuditChain_ComputeHash_NullReviewVersusEmptyReview_DiffersFromEachOther**: A missing review and an empty review give
different hashes. This scenario is tested by `AuditChain_ComputeHash_NullReviewVersusEmptyReview_DiffersFromEachOther`.

**AuditChain_ComputeHash_SameFields_IsStableLowercaseHexAndIgnoresStoredHash**: The same fields give the same 64
character lowercase hash whatever hash the entry already carries. This scenario is tested by
`AuditChain_ComputeHash_SameFields_IsStableLowercaseHexAndIgnoresStoredHash`, and the time offset case by
`AuditChain_ComputeHash_SameInstantOtherOffset_SameHash`.

**AuditChain_ComputeHash_UnicodeText_IsHashed**: Text outside the basic Latin range is hashed and changes the hash.
This scenario is tested by `AuditChain_ComputeHash_UnicodeText_IsHashed`; the genesis value by
`AuditChain_GenesisHash_IsSixtyFourZeros`; and a null entry by `AuditChain_ComputeHash_NullEntry_Throws`.

**AuditChain_Verify_ValidChain_IsIntactWithLastHashAsHead**: A valid chain is intact and its head is the hash of the
last entry; an empty chain is intact with the genesis hash. This scenario is tested by
`AuditChain_Verify_ValidChain_IsIntactWithLastHashAsHead` and `AuditChain_Verify_EmptyChain_IsIntactWithGenesisHead`.

**AuditChain_Verify_EditedEntry_ReportsThatEntry**: The detail of an entry is changed; verification reports that entry.
This scenario is tested by `AuditChain_Verify_EditedEntry_ReportsThatEntry`.

**AuditChain_Verify_EditedEntryWithRecomputedHash_BreaksAtNextEntry**: An entry is edited and its own hash is
recomputed; the chain breaks at the next entry because its previous hash no longer matches. This scenario is tested by
`AuditChain_Verify_EditedEntryWithRecomputedHash_BreaksAtNextEntry`.

**AuditChain_Verify_DeletedEntry_ReportsTheMissingSequence**: An entry in the middle is removed; the missing sequence is
reported, and when the first entry is removed, sequence one is reported. This scenario is tested by
`AuditChain_Verify_DeletedEntry_ReportsTheMissingSequence` and `AuditChain_Verify_DeletedFirstEntry_ReportsSequenceOne`.

**AuditChain_Verify_ReorderedEntries_ReportsFirstOutOfOrderSequence**: Two entries swap places; the first one out of
order is reported. This scenario is tested by `AuditChain_Verify_ReorderedEntries_ReportsFirstOutOfOrderSequence`.

**AuditChain_Verify_InsertedEntry_IsDetected**: An entry is inserted, and one is duplicated; both are detected. This
scenario is tested by `AuditChain_Verify_InsertedEntry_IsDetected` and `AuditChain_Verify_DuplicatedEntry_IsDetected`.

**AuditChain_Verify_WrongPreviousHash_ReportsThatEntry**: An entry has a previous hash that is not that of its
predecessor; it is reported, and so is a first entry whose sequence is not one. This scenario is tested by
`AuditChain_Verify_WrongPreviousHash_ReportsThatEntry` and
`AuditChain_Verify_FirstEntryWithOtherSequence_ReportsSequenceOne`.

**AuditChain_Verify_TwoBadEntries_ReportsTheFirst**: Two entries are damaged; the first is reported and the verifier
stops at it. This scenario is tested by `AuditChain_Verify_TwoBadEntries_ReportsTheFirst`,
`AuditChain_Verify_BadEntry_StopsReadingTheSequence`, and
`AuditChainVerifier_Add_AfterFailure_StaysFailedWithLastGoodHead`.

**AuditText_Clean_PlainText_IsUnchanged**: A plain text passes unchanged, and null and padded texts become empty or
trimmed. This scenario is tested by `AuditText_Clean_PlainText_IsUnchanged` and
`AuditText_Clean_NullAndPadded_AreEmptyAndTrimmed`.

**AuditText_Clean_ControlCharactersAndLineBreaks_BecomeSpaces**: Control characters and line breaks become spaces. This
scenario is tested by `AuditText_Clean_ControlCharactersAndLineBreaks_BecomeSpaces`.

**AuditText_Clean_Surrogates_PairKeptLoneReplaced**: A valid surrogate pair stays; a lone surrogate is replaced. This
scenario is tested by `AuditText_Clean_Surrogates_PairKeptLoneReplaced`.

**AuditText_Clean_LongText_IsCutWithEllipsisWithinLimit**: A text over the limit is cut with an ellipsis, never inside a
surrogate pair, and a limit below four is refused. This scenario is tested by
`AuditText_Clean_LongText_IsCutWithEllipsisWithinLimit`, `AuditText_Clean_CutInsideSurrogatePair_DoesNotSplitThePair`,
and `AuditText_Clean_LimitBelowFour_Throws`.

**AuditText_ShortHash_Text_IsPrefixOfSha256OfTrimmedText**: The short hash is the prefix of the SHA-256 of the trimmed
text, and null behaves as empty text. This scenario is tested by
`AuditText_ShortHash_Text_IsPrefixOfSha256OfTrimmedText` and
`AuditText_ShortHash_NullAndDifferentText_BehavesAsEmptyAndDiffers`.

**AuditDraft_By_SignedInActor_NamesDisplayNameAndUserName**: A draft made for a signed-in actor names the display name
and the user name; for the command line only the display name. This scenario is tested by
`AuditDraft_By_SignedInActor_NamesDisplayNameAndUserName` and
`AuditDraft_By_CommandLineActor_NamesOnlyTheDisplayName`.
