### EvidenceIndexService

#### Verification Approach

`EvidenceIndexService` is verified with tests defined in `EvidenceIndexServiceTests.cs` and
`ReviewMarkContractTests.cs`.
The unit tests use the real `EvidenceIndexService`, `ReviewService`, and `SigningService` over in-memory stores built by
the helper `AttestationFixture`, and reviews that carry a review-set id, which only a plan sets, added to the store by
`EvidenceTestSupport`. The contract tests start the real application in process with a fixed clock, create a fixed
project with fixed reviews and files, and read the index over HTTP with HTTP Basic credentials, as ReviewMark does. They
check the document against the golden file `TestData/evidence-index.golden.json`, against a reader and a classifier that
mirror the rules of ReviewMark 1.4.0, and against an independent implementation of its fingerprint algorithm that is
also checked against vectors that were verified with ReviewMark itself.

#### Test Environment

N/A - standard test environment (everything runs in memory or over a temporary database, with no network access). The
contract tests do not start ReviewMark: its rules are mirrored in the tests, and a run of the real tool against a
running server is a manual check.

#### Acceptance Criteria

- All tests pass with zero failures.
- A complete review that has a review-set id is listed with that id, its content fingerprint, the UTC day of its latest
  signature, and the review that it stands for.
- A review without a review-set id, an incomplete review, a review that changed after it was signed, and a review of
  another project are not listed.
- Of several reviews with the same id and fingerprint, the one that was completed last is listed, and for the same time
  the one with the lowest review id; the order is by id and then by fingerprint.
- The document of the fixed project is the golden file, has the schema that ReviewMark reads, and loads without one
  entry
  overwriting another.
- The fingerprints are the ones that the algorithm of ReviewMark gives for the same files.

#### Test Scenarios

**EvidenceIndexService_Build_CompleteReviewWithSetId_ListsEntry**: A complete review has a review-set id. The index has
one entry with the id, the fingerprint of the review, the date of the signature, and the review id. This scenario is
tested by `EvidenceIndexService_Build_CompleteReviewWithSetId_ListsEntry`.

**EvidenceIndexService_Build_IncompleteReview_IsNotListed**: A review whose slot is not signed has a review-set id. The
index is empty. This scenario is tested by `EvidenceIndexService_Build_IncompleteReview_IsNotListed`.

**EvidenceIndexService_Build_ReviewChangedAfterSignature_IsNotListed**: The record of a signed review changes. The index
is
empty. This scenario is tested by `EvidenceIndexService_Build_ReviewChangedAfterSignature_IsNotListed`.

**EvidenceIndexService_Build_NoSetId_IsSkipped**: A complete review has no review-set id. The index is empty. This
scenario is tested by `EvidenceIndexService_Build_NoSetId_IsSkipped`.

**EvidenceIndexService_Build_SameIdAndFingerprint_KeepsLatestCompletion**: Two complete reviews have the same id and
fingerprint and were completed on different days. The one completed last is listed, with its date. This scenario is
tested by `EvidenceIndexService_Build_SameIdAndFingerprint_KeepsLatestCompletion`.

**EvidenceIndexService_Build_SameCompletionTime_KeepsLowestReviewId**: Two complete reviews have the same id and
fingerprint and the same completion time. The one with the lowest review id is listed. This scenario is tested by
`EvidenceIndexService_Build_SameCompletionTime_KeepsLowestReviewId`.

**EvidenceIndexService_Build_Order_IsByIdThenFingerprint**: Several complete reviews are added in an arbitrary order.
The
entries are ordered by id and then by fingerprint, in ordinal order. This scenario is tested by
`EvidenceIndexService_Build_Order_IsByIdThenFingerprint`.

**EvidenceIndexService_Build_Date_IsLatestSignatureAsUtcDay**: A review with two slots is signed on different days, the
last one close to midnight UTC. The date is the UTC day of the last signature. This scenario is tested by
`EvidenceIndexService_Build_Date_IsLatestSignatureAsUtcDay`.

**EvidenceIndexService_Build_OtherProject_IsNotListed**: A complete review belongs to another project. The index of the
project is empty. This scenario is tested by `EvidenceIndexService_Build_OtherProject_IsNotListed`.

**ReviewMarkContract_GoldenIndex_MatchesCommittedFile**: The index of the fixed project is read over HTTP. After the
line
endings are normalized it is the golden file: three entries, one of them for a review set that has two reviews with
different files. This scenario is tested by `ReviewMarkContract_GoldenIndex_MatchesCommittedFile`.

**ReviewMarkContract_Index_HasTheSchemaReviewMarkReads**: The document is an object with the one property `reviews`, an
array of objects with the five non-blank string fields, a date of the form `yyyy-MM-dd`, the result `pass`, and an
absolute file address, and ReviewMark's reader keeps every entry. This scenario is tested by
`ReviewMarkContract_Index_HasTheSchemaReviewMarkReads`.

**ReviewMarkContract_IndexFingerprint_EqualsReviewMarkAlgorithm**: Every listed review has the fingerprint that an
independent implementation of the algorithm of ReviewMark gives for its files, and that implementation reproduces the
known vectors. This scenario is tested by `ReviewMarkContract_IndexFingerprint_EqualsReviewMarkAlgorithm`.

**ReviewMarkContract_Index_FollowsReviewMarkSemantics**: ReviewMark's rules, applied to the index, give current for the
fingerprint of a listed review, stale (naming the newest date) for another fingerprint of a listed id, and missing for
an id that is not listed, such as a review set whose only review is not signed. This scenario is tested by
`ReviewMarkContract_Index_FollowsReviewMarkSemantics`.
