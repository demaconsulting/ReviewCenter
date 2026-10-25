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
- The canonical text of a record has the version line, the fingerprint, the answers, the sections that do not apply,
  and the threads with their comments, written as the design states.
- The digest is the SHA-256 of the UTF-8 canonical text as 64 lowercase hexadecimal characters, and equals the known
  value for the known record.
- Changing a covered value (the fingerprint, an answer, the reason of a section, a thread, or a comment) changes the
  digest.
- Reordering the answers, sections, and threads, or giving them other identities, does not change the digest; a
  different order of the comments of a thread does.
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

**RecordDigest_Compute_KnownRecord_MatchesKnownValue**: The record with the fingerprint `abc` and nothing else has the
digest `9755f10e118733d548ae1037dd68b000d6d50ca8b62f53d4411693d85a3fe35a`. This scenario is tested by
`RecordDigest_Compute_KnownRecord_MatchesKnownValue`.

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
