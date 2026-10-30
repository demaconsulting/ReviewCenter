### RecordDigest

#### Verification Approach

`RecordDigest` is verified with unit tests defined in `RecordDigestTests.cs`. The tests build small `ReviewRecord`
values by hand and call the static methods directly. The format is proved twice: the exact canonical text of a small
record is compared with the text of the design, and the digest of a record with no answers, sections, or threads is
compared with a known SHA-256 value that was calculated independently of the implementation. The properties that the
design promises (what changes the digest, what does not, and that the text cannot be confused) are proved by changing
one value of a record at a time and comparing digests. No mocking is applied.

#### Test Environment

N/A - standard test environment.

#### Acceptance Criteria

- All unit tests pass with zero failures.
- The canonical text of a record has the version line (`ReviewCenter record v1`), the fingerprint, the source (upload,
  or repository with its changes), the checklist wording, the answers with who answered, the sections that do not
  apply, and the threads with their comments, written as the design states.
- The digest is the SHA-256 of the UTF-8 canonical text as 64 lowercase hexadecimal characters, and equals the known
  values for the known records (an upload, a repository review, and an upload with a checklist), for the review-set
  variants, and for the two thread sides.
- Changing a covered value (the fingerprint, an answer, the reason of a section, a thread, the side of a thread, a
  comment, the source
  kind, the repository, a commit, the selection, the review-set, or the path, kind, or hash of a change, or an added
  or removed change, or the wording of a check, a section title, or the checklist version) changes the digest.
- Reordering the answers, sections, threads, and changes, the checklist sections and checks, or giving them other
  identities, does not change the digest; a different order of the comments of a thread does.
- Who answered a check (display name or user name) changes the digest; when it was answered, and a section that
  applies, do not.
- A stored checklist definition that is not valid JSON, or is incomplete (a missing version, sections, section id,
  title or items, or item id or text), is not hidden: it raises a `JsonException`. A minimal complete definition
  still computes.
- Text that contains the delimiters of the format cannot produce the digest of another record.
- A record that was never reopened (a reopen count of 0) has exactly the canonical text, and so the digest, that it had
  before reopening existed, with the same version line; a record that was reopened gets a final `reopens {count}` line,
  so its digest differs from that of the same record without reopens and from that of the same record with another
  count.
- A null record is rejected with an `ArgumentNullException` by both methods.

#### Test Scenarios

**RecordDigest_Canonicalize_SmallRecord_MatchesDocumentedFormat**: A record with one answer, one section that does not
apply, one section that does, and one thread with one comment gives exactly the canonical text of the design, with
the length-prefixed strings, the version line, who answered, and only the section that does not apply. This scenario is
tested by `RecordDigest_Canonicalize_SmallRecord_MatchesDocumentedFormat`.

**RecordDigest_Compute_SameRecord_IsStableSha256OfCanonicalText**: Two equal records give the same digest, which is
64 lowercase hexadecimal characters and equals the SHA-256 of the UTF-8 bytes of the canonical text. This scenario is
tested by `RecordDigest_Compute_SameRecord_IsStableSha256OfCanonicalText`.

**RecordDigest_Compute_KnownRecord_MatchesKnownValue**: The upload record with the fingerprint `abc` and nothing else
has the digest `9fee8bc5c8ddc1c908ed605ccb301bfa1be3e0499cc824d787275c7a783543f0` (version 1). This scenario is
tested by `RecordDigest_Compute_KnownRecord_MatchesKnownValue`.

**RecordDigest_Compute_ChecklistRecord_MatchesDocumentedFormatAndKnownValue**: An upload record whose checklist
defines its sections and checks out of id order gives exactly the canonical text of the design (checklist version,
sections and checks sorted by id, the answer with who answered) and the known digest
`5f6e64aa7091e4156ec43493c56d9f0cc2407a24a92efec3012ae02e1f954e43`. This scenario is tested by
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
known digest `b0eae44ae9062359375b7a361857794617e5c28b5a6a2b7ac732ec36549857d8`. This scenario is tested by
`RecordDigest_Canonicalize_GitSource_MatchesDocumentedFormat`.

**RecordDigest_Compute_ReviewSetId_IsCoveredAndDistinguishesNullFromEmpty**: The repository record with no review-set,
with the review-set `core`, and with the empty review-set gives three different digests, equal to the known values
of the design (calculated with an independent tool), and the source line ends with `-`, `4:core`, and `0:`. This
scenario is tested by `RecordDigest_Compute_ReviewSetId_IsCoveredAndDistinguishesNullFromEmpty`.

**RecordDigest_Compute_SourceChange_ChangesDigest**: A theory over a change of the source kind, the repository, the
baseline (changed, or removed), the head, the selection, the review-set, the path, kind, baseline hash, or head hash
of a change, an added change, and a removed change; each gives a digest that differs from that of the unchanged
record. This scenario is tested by `RecordDigest_Compute_SourceChange_ChangesDigest`.

**RecordDigest_Compute_ChangeOrder_SameDigest**: The same changes in another order give the same digest. This
scenario is tested by `RecordDigest_Compute_ChangeOrder_SameDigest`.

**RecordDigest_Compute_CoveredChange_ChangesDigest**: A theory over a change of the fingerprint, the outcome or the
justification of an answer, an added answer, a section that no longer applies, the reason of such a section, an added
thread, a changed state, anchor, or path of a thread, the text or the author of a comment, and an added comment; each
gives a digest that differs from that of the unchanged record. This scenario is tested by
`RecordDigest_Compute_CoveredChange_ChangesDigest`.

**RecordDigest_Compute_ReorderedAndRenumbered_SameDigest**: The same answers, sections, and threads, listed in another
order and with other row identities, give the same digest. This scenario is tested by
`RecordDigest_Compute_ReorderedAndRenumbered_SameDigest`.

**RecordDigest_Compute_CommentOrder_ChangesDigest**: The same two comments of a thread in the other order give another
digest. This scenario is tested by `RecordDigest_Compute_CommentOrder_ChangesDigest`.

**RecordDigest_Compute_ApplicableSection_NotCovered**: A record with an additional section that applies has the same
digest as the original. This scenario is tested by `RecordDigest_Compute_ApplicableSection_NotCovered`.

**RecordDigest_Compute_TextWithDelimiters_DoesNotCollide**: A record with one comment whose text contains a line feed
and a fake comment line, and a record with two real comments, give different digests. This scenario is tested by
`RecordDigest_Compute_TextWithDelimiters_DoesNotCollide`.

**RecordDigest_Compute_ZeroReopens_EqualsLegacyDigest**: A record with a reopen count of 0 has the same canonical text
(no `reopens` line) and the same digest as the known values of the records that were fixed before reopening existed.
This scenario is tested by `RecordDigest_Compute_ZeroReopens_EqualsLegacyDigest`.

**RecordDigest_Compute_ReopensChangeDigest**: The same record with a reopen count of 1 and of 2 gives two digests that
differ from each other and from the record without reopens, and the canonical text ends with the line `reopens 1`
for the first. This scenario is tested by `RecordDigest_Compute_ReopensChangeDigest`.

**RecordDigest_Compute_NullRecord_Throws**: A null record is rejected with an `ArgumentNullException` by both
methods. This scenario is tested by `RecordDigest_Compute_NullRecord_Throws`.
