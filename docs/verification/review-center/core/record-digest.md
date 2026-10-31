### RecordDigest

#### Verification Approach

`RecordDigest` is verified with unit tests defined in `RecordDigestTests.cs` (the format and everything outside the
report), `RecordDigestFollowUpTests.cs` (the follow-up lines), and `RecordDigestReportTests.cs` (the `report` block).
The tests build small `ReviewRecord`
values by hand and call the static methods directly. The format is proved twice: the exact canonical text of a small
record is compared with the text of the design, and the digest of a record with no answers, sections, or threads is
compared with a known SHA-256 value that was calculated independently of the implementation. The properties that the
design promises (what changes the digest, what does not, and that the text cannot be confused) are proved by changing
one value of a record at a time and comparing digests. The `report` block is pinned by golden vectors that an
independent model (a Python script that builds the text from the rules of the design) produced before the tests were
run against the code. No mocking is applied.

#### Test Environment

N/A - standard test environment.

#### Acceptance Criteria

- All unit tests pass with zero failures.
- The canonical text of a record has the version line (`ReviewCenter record v2`), the fingerprint, the source (upload,
  or repository with its changes), the checklist wording, the answers with who answered, the sections that do not
  apply, and the `report` block, written as the design states. The `report` block is the `report` marker line followed
  by one `thread file` block for every thread that has an included comment, with the side, the anchor, and only the
  included comments (`claim`, `comment`, or `resolution`); with no such thread it is the marker line alone.
- The digest is the SHA-256 of the UTF-8 canonical text as 64 lowercase hexadecimal characters, and equals the known
  values for the known records (an upload, a repository review, an upload with a checklist, and a repository review with
  the less common values), for the review-set variants, for the two thread sides, and for the report vectors
  (a resolved Finding, an open Finding, a Question with an included reply, a baseline Finding, a carried Finding,
  a record with reopens, and two included threads).
- Changing a covered value (the fingerprint, an answer, the reason of a section, an included comment and its text,
  author, user name, kind, or time, the side, either line, or the whole-file anchor of a thread with an included
  comment, the source kind, the repository, a commit, the selection, the review-set, or the path, kind, or hash of a change,
  or an added or removed change, or the wording of a check, a section title, or the checklist version) changes the
  digest.
- Chat is not covered: a thread of only chat comments gives the digest of a record without threads, chat added to a
  Finding does not change the digest, and neither does the state of a thread, its creation time, or its identity.
  Promoting, demoting, or toggling the flag of a reply changes the digest and doing it back restores it; a resolution
  note is covered only when it is included.
- Reordering the answers, sections, threads, and changes, the checklist sections and checks, or giving them other
  identities, does not change the digest; a different order of the included comments of a thread does.
- Who answered a check (display name or user name) changes the digest; when it was answered, a section that applies,
  and who marked a section as not applicable and when, do not.
- A stored checklist definition that is not valid JSON, or is incomplete (a missing version, sections, section id,
  title or items, or item id or text), is not hidden: it raises a `JsonException`. A minimal complete definition
  still computes.
- Text that contains the delimiters of the format, a path, title, or name that runs into the next value, and an absent
  value versus the text `-` or the empty text, cannot produce the digest of another record.
- A record that was never reopened (a reopen count of 0) has no `reopens` line, so its canonical text and digest are
  those of the same record in the version before reopening existed; a record that was reopened gets a final
  `reopens {count}` line, so its digest differs from that of the same record without reopens and from that of the same
  record with another count.
- A review that follows nothing and has no carried row has no `follows` line and no `carried` suffix; a follow-up gets
  a `follows {id}` line after the source block and the suffix `carried {id}`, after a space, on a carried answer,
  not-applicable section, and included thread line, and any change of these marks changes the digest, while the number
  that a carried thread had in the earlier review does not.
- A null record is rejected with an `ArgumentNullException` by both methods.

#### Test Scenarios

**RecordDigest_Canonicalize_SmallRecord_MatchesDocumentedFormat**: A record with one answer, one section that does not
apply, one section that does, and one thread with one included comment gives exactly the canonical text of the design,
with the length-prefixed strings, the version line, who answered, only the section that does not apply, and the
`report` block with the `claim` line. This scenario is
tested by `RecordDigest_Canonicalize_SmallRecord_MatchesDocumentedFormat`.

**RecordDigest_Compute_SameRecord_IsStableSha256OfCanonicalText**: Two equal records give the same digest, which is
64 lowercase hexadecimal characters and equals the SHA-256 of the UTF-8 bytes of the canonical text. This scenario is
tested by `RecordDigest_Compute_SameRecord_IsStableSha256OfCanonicalText`.

**RecordDigest_Compute_KnownRecord_MatchesKnownValue**: The upload record with the fingerprint `abc` and nothing else
has the digest `eb725d508e04716f367b32ac442820c89667cc920020b10eacebf181d7365ab3` (version 2). This scenario is
tested by `RecordDigest_Compute_KnownRecord_MatchesKnownValue`.

**RecordDigest_Compute_ChecklistRecord_MatchesDocumentedFormatAndKnownValue**: An upload record whose checklist
defines its sections and checks out of id order gives exactly the canonical text of the design (checklist version,
sections and checks sorted by id, the answer with who answered) and the known digest
`2a6b6bc4de3a50d758ee5d5a9438c10c2f398576946ce3a828d5713ef70d91ef`. This scenario is tested by
`RecordDigest_Compute_ChecklistRecord_MatchesDocumentedFormatAndKnownValue`.

**RecordDigest_Compute_ChecklistWordingChange_ChangesDigest**: A theory over a changed question wording, a changed
section title, a changed checklist version, a removed check, and a removed checklist; each gives a digest that
differs from that of the unchanged record. This scenario is tested by
`RecordDigest_Compute_ChecklistWordingChange_ChangesDigest`.

**RecordDigest_Compute_ChecklistDefinitionOrder_SameDigest**: The same checklist with its sections and checks listed
in another order gives the same digest. This scenario is tested by
`RecordDigest_Compute_ChecklistDefinitionOrder_SameDigest`.

**RecordDigest_Compute_AnsweredByChanged_ChangesDigestButAnsweredAtDoesNot**: Answers that differ in the display name
or in the user name of the person give digests that differ from the original; an answer that differs only in the
time of answering has the same digest. This scenario is tested by
`RecordDigest_Compute_AnsweredByChanged_ChangesDigestButAnsweredAtDoesNot`.

**RecordDigest_Canonicalize_MalformedChecklistDefinition_Throws**: A theory over corrupted stored definitions: text
that is not JSON, `null`, `{}`, a missing or null version, a missing or null list of sections, a null section, a
section without id, title or items, a null item, and an item without id or text. Each is refused with a
`JsonException` (never a `NullReferenceException`). This scenario is tested by
`RecordDigest_Canonicalize_MalformedChecklistDefinition_Throws`.

**RecordDigest_Canonicalize_MinimalChecklistDefinition_Computes**: A theory over a definition without sections and a
section without an applicability hint: the canonical text is still built, so the check is not too strict. This scenario
is tested by `RecordDigest_Canonicalize_MinimalChecklistDefinition_Computes`.

**RecordDigest_Compute_ThreadSide_IsWrittenAndMatchesKnownValue**: A theory over the side `Head` and the side
`Baseline`: the thread line of the canonical text carries `4:Head` or `8:Baseline` between the path and the range,
and the digest equals the known value of the design for that side, calculated with an independent script. This
scenario is tested by `RecordDigest_Compute_ThreadSide_IsWrittenAndMatchesKnownValue`.

**RecordDigest_Compute_ThreadSideChange_ChangesDigest**: The same record with the thread on the head side and on
the baseline side gives two different digests. This scenario is tested by
`RecordDigest_Compute_ThreadSideChange_ChangesDigest`.

**RecordDigest_Canonicalize_GitSource_MatchesDocumentedFormat**: A repository record whose changes are given out of
path order gives exactly the canonical text of the design (source line with the length-prefixed commits and
selection and `-` for no review-set, the changes sorted by path, `-` for the head hash of a deleted file) and the
known digest `bb2723058729bb669e8c8858e0b4f913fd786e59d68d9d785093994b08012842`. This scenario is tested by
`RecordDigest_Canonicalize_GitSource_MatchesDocumentedFormat`.

**RecordDigest_Compute_ReviewSetId_IsCoveredAndDistinguishesNullFromEmpty**: The repository record with no review-set,
with the review-set `core`, and with the empty review-set gives three different digests, equal to the known values
of the design (calculated with an independent tool; they start with `bb27`, `a595`, and `8759`), and the source line
ends with `-`, `4:core`, and `0:`. This
scenario is tested by `RecordDigest_Compute_ReviewSetId_IsCoveredAndDistinguishesNullFromEmpty`.

**RecordDigest_Compute_SourceChange_ChangesDigest**: A theory over a change of the source kind, the repository, the
baseline (changed, or removed), the head, the selection, the review-set, the path, kind, baseline hash, or head hash
of a change, an added change, and a removed change; each gives a digest that differs from that of the unchanged
record. This scenario is tested by `RecordDigest_Compute_SourceChange_ChangesDigest`.

**RecordDigest_Compute_ChangeOrder_SameDigest**: The same changes in another order give the same digest. This
scenario is tested by `RecordDigest_Compute_ChangeOrder_SameDigest`.

**RecordDigest_Compute_CoveredChange_ChangesDigest**: A theory over a change of the fingerprint, the outcome (to Fail,
or to NotApplicable) or the justification of an answer (to another text, or from absent to empty), an added answer, a
section that no longer applies, the reason of such a section, an added included thread, a changed anchor (both lines,
the start line alone, the end line alone, a removed start or end line, or the whole file) or path of a thread, the
text, the author display name, the author user name, or the time of an included comment, and an added included
comment; each gives a digest that differs from that of the unchanged record (the state and the creation time of a
thread are covered by no case, because they are no longer part of the record). This scenario is tested by
`RecordDigest_Compute_CoveredChange_ChangesDigest`.

**RecordDigest_Compute_CoveredFieldsRecord_MatchesDocumentedFormatAndKnownValue**: A repository record with an added
file, a NotApplicable answer with an empty justification, and a resolved whole-file thread with an included comment
gives exactly the canonical text of the design and the known digests (without, and with
one reopen) calculated with an independent script. This scenario is tested by
`RecordDigest_Compute_CoveredFieldsRecord_MatchesDocumentedFormatAndKnownValue`.

**RecordDigest_Compute_SectionSetterAndTime_NotCovered**: A section that does not apply, set by another person at
another time, gives the same digest. This scenario is tested by
`RecordDigest_Compute_SectionSetterAndTime_NotCovered`.

**RecordDigest_Compute_ReorderedAndRenumbered_SameDigest**: The same answers, sections, and threads, listed in another
order and with other row identities, give the same digest. This scenario is tested by
`RecordDigest_Compute_ReorderedAndRenumbered_SameDigest`.

**The follow-up lines** are verified in `RecordDigestFollowUpTests.cs` with the same hand-built records.

**RecordDigest_Compute_NoFollowUp_EqualsLegacyDigest**: The smallest known record (an upload with the fingerprint `abc`)
has its known digest, a null `FollowsReviewId` is the same as none, and the
text of a full record without markers has neither `follows` nor `carried`. This scenario is tested by
`RecordDigest_Compute_NoFollowUp_EqualsLegacyDigest`.

**RecordDigest_Canonicalize_FollowUpRecord_MatchesDocumentedFormat**: A record that follows `rev-0` and carries an
answer, a not-applicable section, and a thread has the `follows` line straight after the source and the `carried`
suffix on the three lines, exactly as the design states. This scenario is tested by
`RecordDigest_Canonicalize_FollowUpRecord_MatchesDocumentedFormat`.

**RecordDigest_Compute_FollowUpMarkerChange_ChangesDigest**: A theory: another followed review, and a missing or other
carried mark on the answer, the section, or the thread, each gives a digest that differs from the baseline and from the
same record that follows nothing. This scenario is tested by `RecordDigest_Compute_FollowUpMarkerChange_ChangesDigest`.

**RecordDigest_Compute_CarriedFromThreadId_NotCovered**: Two records whose carried thread had different numbers in the
earlier review give the same digest. This scenario is tested by `RecordDigest_Compute_CarriedFromThreadId_NotCovered`.

**ReviewService_FollowUp_DigestBindsToFollowAndIsImmuneToLaterChecklistChange**: At the level of the service, the digest
of a follow-up differs from that of a review that follows nothing and is not changed by a later change of the checklist
of the project. This scenario is tested by `ReviewService_FollowUp_DigestBindsToFollowAndIsImmuneToLaterChecklistChange`,
which is defined in `ReviewServiceFollowUpTests.cs`.

**RecordDigest_Compute_CommentOrder_ChangesDigest**: The same two comments of a thread in the other order give another
digest. This scenario is tested by `RecordDigest_Compute_CommentOrder_ChangesDigest`.

**RecordDigest_Compute_ApplicableSection_NotCovered**: A record with an additional section that applies has the same
digest as the original. This scenario is tested by `RecordDigest_Compute_ApplicableSection_NotCovered`.

**RecordDigest_Compute_TextWithDelimiters_DoesNotCollide**: A theory over five pairs of different records that would
have the very same text if strings were written without their length (values joined by a space, lines by a line
feed): a comment whose text holds the next comment line versus two comments, a justification that runs into the
display name of the answerer, an absent justification versus the text `-`, a path that runs into the change kind
versus the shorter path with the kind and the baseline hash, and a checklist section title that holds an item line
versus a title with a real item. Each pair gives different digests and different canonical texts; the test fails when
the length prefix is removed. This scenario is tested by `RecordDigest_Compute_TextWithDelimiters_DoesNotCollide`.

**RecordDigest_Compute_EmptyAndSplitValues_DoNotCollide**: Two pairs of records that would have the same text if
strings were written without their length: an empty justification with the answerer "a b" versus the justification
" a" (a leading space) with the answerer "b", and the justification "a b" with an empty answerer versus "a" with the
answerer "b " (a trailing space).
Each pair gives different digests; the test fails when the length prefix is removed. This scenario is tested by
`RecordDigest_Compute_EmptyAndSplitValues_DoNotCollide`.

**RecordDigest_Compute_ZeroReopens_EqualsDigestWithoutReopensLine**: A record with a reopen count of 0 has no `reopens`
line and the same digest as the known values of the records of the other tests: the upload, the repository, the
checklist, and the head-side thread records, each with an explicit count of 0. This
scenario is tested by `RecordDigest_Compute_ZeroReopens_EqualsDigestWithoutReopensLine`.

**RecordDigest_Compute_ReopensChangeDigest**: The same record with a reopen count of 1 and of 2 gives two digests that
differ from each other and from the record without reopens, and the canonical text ends with the line `reopens 1`
for the first. This scenario is tested by `RecordDigest_Compute_ReopensChangeDigest`.

**The report block** is verified in `RecordDigestReportTests.cs` with the same hand-built records.

**RecordDigest_Canonicalize_EmptyReport_MatchesDocumentedFormatAndKnownValue**: A record without an included thread
ends with the `report` marker line alone and has the known digest. This scenario is tested by
`RecordDigest_Canonicalize_EmptyReport_MatchesDocumentedFormatAndKnownValue`.

**RecordDigest_Canonicalize_Header_IsV2**: The first line of the canonical text is `ReviewCenter record v2`. This
scenario is tested by `RecordDigest_Canonicalize_Header_IsV2`.

**RecordDigest_Compute_ResolvedFinding_MatchesKnownValue**: A resolved Finding with its included resolution note has
the canonical text of the design and a known digest calculated with an independent model. This scenario is tested by
`RecordDigest_Compute_ResolvedFinding_MatchesKnownValue`.

**RecordDigest_Compute_OpenFinding_MatchesKnownValue**: An open Finding has a known digest that differs from the
resolved one only by the resolution note. This scenario is tested by
`RecordDigest_Compute_OpenFinding_MatchesKnownValue`.

**RecordDigest_Compute_QuestionWithIncludedReply_MatchesKnownValue**: A Question whose only included comment is a reply
has the block with the `comment` line alone and a known digest. This scenario is tested by
`RecordDigest_Compute_QuestionWithIncludedReply_MatchesKnownValue`.

**RecordDigest_Compute_BaselineFinding_MatchesKnownValue**: A Finding on the baseline side has a known digest. This
scenario is tested by `RecordDigest_Compute_BaselineFinding_MatchesKnownValue`.

**RecordDigest_Compute_CarriedFinding_MatchesKnownValue**: A carried Finding has the `carried` suffix on its thread
line and a known digest. This scenario is tested by `RecordDigest_Compute_CarriedFinding_MatchesKnownValue`.

**RecordDigest_Compute_Reopens_MatchesKnownValueAfterReport**: A record with reopens has the `reopens` line after the
`report` block and a known digest. This scenario is tested by
`RecordDigest_Compute_Reopens_MatchesKnownValueAfterReport`.

**RecordDigest_Compute_TwoIncludedThreads_SortedBlocksMatchKnownValue**: Two included threads are written as sorted
blocks and have a known digest. This scenario is tested by
`RecordDigest_Compute_TwoIncludedThreads_SortedBlocksMatchKnownValue`.

**RecordDigest_Compute_ChatOnlyThreads_EqualsEmptyReport**: Threads that have chat only give the digest of a record
without threads. This scenario is tested by `RecordDigest_Compute_ChatOnlyThreads_EqualsEmptyReport`.

**RecordDigest_Compute_ChatAddedToFinding_DigestUnchanged**: Chat added to a Finding does not change the digest. This
scenario is tested by `RecordDigest_Compute_ChatAddedToFinding_DigestUnchanged`.

**RecordDigest_Compute_ThreadState_NotCovered**: An open and a resolved thread give the same digest. This scenario is
tested by `RecordDigest_Compute_ThreadState_NotCovered`.

**RecordDigest_Compute_ThreadCreatedAndIds_NotCovered**: The creation time and the identities of threads and comments
do not change the digest. This scenario is tested by `RecordDigest_Compute_ThreadCreatedAndIds_NotCovered`.

**RecordDigest_Compute_PromoteDemote_ChangesDigestAndRoundTrips**: Promoting a thread changes the digest and demoting
it again restores the first digest. This scenario is tested by
`RecordDigest_Compute_PromoteDemote_ChangesDigestAndRoundTrips`.

**RecordDigest_Compute_ReplyInclusionToggle_ChangesDigest**: Including a reply of a Finding changes the digest. This
scenario is tested by `RecordDigest_Compute_ReplyInclusionToggle_ChangesDigest`.

**RecordDigest_Compute_ResolutionNote_CoveredOnlyWhenIncluded**: An included resolution note changes the digest and an
excluded one does not. This scenario is tested by `RecordDigest_Compute_ResolutionNote_CoveredOnlyWhenIncluded`.

**RecordDigest_Compute_ThreadOrder_SameDigest**: The same included threads in another order give the same digest.
This scenario is tested by `RecordDigest_Compute_ThreadOrder_SameDigest`.

**RecordDigest_Compute_ReportCommentOrder_ChangesDigest**: The included comments of a thread in another order give
another digest. This scenario is tested by `RecordDigest_Compute_ReportCommentOrder_ChangesDigest`.

**RecordDigest_Compute_DelimitersInReportText_DoNotCollide**: Text in an included comment that holds the delimiters of
the format cannot produce the digest of another record. This scenario is tested by
`RecordDigest_Compute_DelimitersInReportText_DoNotCollide`.

**RecordDigest_Compute_NullRecord_Throws**: A null record is rejected with an `ArgumentNullException` by both
methods. This scenario is tested by `RecordDigest_Compute_NullRecord_Throws`.
