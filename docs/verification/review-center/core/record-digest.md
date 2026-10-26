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
- The canonical text of a record has the version line, the fingerprint, the source (upload, or repository with its
  changes), the answers, the sections that do not apply, and the threads with their comments, written as the design
  states.
- The digest is the SHA-256 of the UTF-8 canonical text as 64 lowercase hexadecimal characters, and equals the known
  values for the two known records (an upload and a repository review), for the review-set variants, and for the two
  thread sides.
- Changing a covered value (the fingerprint, an answer, the reason of a section, a thread, the side of a thread, a
  comment, the source
  kind, the repository, a commit, the selection, the review-set, or the path, kind, or hash of a change, or an added
  or removed change) changes the digest.
- Reordering the answers, sections, threads, and changes, or giving them other identities, does not change the
  digest; a different order of the comments of a thread does.
- Who answered a check, and a section that applies, do not change the digest.
- Text that contains the delimiters of the format cannot produce the digest of another record.
- A null record is rejected with an `ArgumentNullException` by both methods.

#### Test Scenarios

**RecordDigest_Canonicalize_SmallRecord_MatchesDocumentedFormat**: A record with one answer, one section that does not
apply, one section that does, and one thread with one comment gives exactly the canonical text of the design, with
the length-prefixed strings, the version line, and only the section that does not apply. This scenario is tested by
`RecordDigest_Canonicalize_SmallRecord_MatchesDocumentedFormat`.

**RecordDigest_Compute_SameRecord_IsStableSha256OfCanonicalText**: Two equal records give the same digest, which is
64 lowercase hexadecimal characters and equals the SHA-256 of the UTF-8 bytes of the canonical text. This scenario is
tested by `RecordDigest_Compute_SameRecord_IsStableSha256OfCanonicalText`.

**RecordDigest_Compute_KnownRecord_MatchesKnownValue**: The upload record with the fingerprint `abc` and nothing else
has the digest `244341c991c7ad467cb6f84ea3236bf06cddaada609cabc5ef7a5f3326f7aeb0` (version 4), and not the digest
of the same record in version 3. This scenario is tested by `RecordDigest_Compute_KnownRecord_MatchesKnownValue`.

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
known digest `1c2c05b15941c7bc7f8c6b63aff356e4efd5123c0ed8d97ffa3ead7635046387`. This scenario is tested by
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

**RecordDigest_Compute_AnsweredByAndApplicableSection_NotCovered**: A record whose answer was given by another person,
and that has an additional section that applies, has the same digest as the original. This scenario is tested by
`RecordDigest_Compute_AnsweredByAndApplicableSection_NotCovered`.

**RecordDigest_Compute_TextWithDelimiters_DoesNotCollide**: A record with one comment whose text contains a line feed
and a fake comment line, and a record with two real comments, give different digests. This scenario is tested by
`RecordDigest_Compute_TextWithDelimiters_DoesNotCollide`.

**RecordDigest_Compute_NullRecord_Throws**: A null record is rejected with an `ArgumentNullException` by both
methods. This scenario is tested by `RecordDigest_Compute_NullRecord_Throws`.
