### ReviewService

#### Verification Approach

`ReviewService` is verified with unit tests defined in `ReviewServiceTests.cs`. The tests use the real service and
the real `Fingerprint` with a fake in-memory store (`FakeReviewStore`) and a fixed clock, so every rule and every
limit can be exercised without a database. Failures are checked through the rule carried by the
`DomainRuleException`. Limits are tested at the boundary: the limit itself is accepted and one more is rejected.
The tests that use large content build it in memory. The blank-path, invalid-id, and blank-title tests are
theories over several invalid values.

#### Test Environment

N/A - standard test environment.

#### Acceptance Criteria

- All unit tests pass with zero failures.
- A valid request stores a Draft review with the known fingerprint, the hash and size of each file, and the bytes
  unchanged, including binary content and carriage returns.
- The fingerprint does not depend on file paths or order.
- Each rule violation is rejected with the matching rule.
- Every limit is accepted at its value and rejected above it.
- A review can be read back with its files, listed, and its file content read; the content of a missing file or
  review is null.

#### Test Scenarios

**ReviewService_CreateDraftAsync_ValidInput_StoresDraftWithFingerprint**: A review of three files is created; it
is stored in the Draft state with the known fingerprint, and every file has the size and the known SHA-256 hash
of its own content. This scenario is tested by `ReviewService_CreateDraftAsync_ValidInput_StoresDraftWithFingerprint`.

**ReviewService_CreateDraftAsync_BinaryAndCrLfContent_KeepsBytesVerbatim**: A file with binary content and
carriage returns and an empty file are created; the stored bytes equal the uploaded bytes, and the sizes and
hashes (including the hash of empty content) are those of the input. This scenario is tested by
`ReviewService_CreateDraftAsync_BinaryAndCrLfContent_KeepsBytesVerbatim`.

**ReviewService_CreateDraftAsync_SameBytesDifferentPathsAndOrder_GivesSameFingerprint**: The same contents under
other paths and in another order give the same fingerprint. This scenario is tested by
`ReviewService_CreateDraftAsync_SameBytesDifferentPathsAndOrder_GivesSameFingerprint`.

**ReviewService_CreateDraftAsync_InvalidId_ThrowsReviewIdInvalid**: Null, empty, and ill-formed identifiers are
rejected with `ReviewIdInvalid`. This scenario is tested by
`ReviewService_CreateDraftAsync_InvalidId_ThrowsReviewIdInvalid`.

**ReviewService_CreateDraftAsync_IdLength_LimitIsInclusive**: An identifier of the maximum length is accepted and
one character longer is rejected. This scenario is tested by
`ReviewService_CreateDraftAsync_IdLength_LimitIsInclusive`.

**ReviewService_CreateDraftAsync_DuplicateIdInSameProject_ThrowsReviewIdDuplicate**: A second review with an
existing identifier in the same project is rejected with `ReviewIdDuplicate`. This scenario is tested by
`ReviewService_CreateDraftAsync_DuplicateIdInSameProject_ThrowsReviewIdDuplicate`.

**ReviewService_CreateDraftAsync_SameIdInOtherProject_IsAllowed**: The same identifier in another project is
accepted. This scenario is tested by `ReviewService_CreateDraftAsync_SameIdInOtherProject_IsAllowed`.

**ReviewService_CreateDraftAsync_BlankTitle_ThrowsReviewTitleInvalid**: Null, empty, and white-space titles are
rejected with `ReviewTitleInvalid`. This scenario is tested by
`ReviewService_CreateDraftAsync_BlankTitle_ThrowsReviewTitleInvalid`.

**ReviewService_CreateDraftAsync_TitleTooLong_ThrowsReviewTitleInvalid**: A title longer than the maximum is
rejected with `ReviewTitleInvalid`. This scenario is tested by
`ReviewService_CreateDraftAsync_TitleTooLong_ThrowsReviewTitleInvalid`.

**ReviewService_CreateDraftAsync_UnknownProject_ThrowsProjectNotFound**: A review in a project that does not exist
is rejected with `ProjectNotFound`. This scenario is tested by
`ReviewService_CreateDraftAsync_UnknownProject_ThrowsProjectNotFound`.

**ReviewService_CreateDraftAsync_NoFiles_ThrowsNoFiles**: A review without files is rejected with `NoFiles`. This
scenario is tested by `ReviewService_CreateDraftAsync_NoFiles_ThrowsNoFiles`.

**ReviewService_CreateDraftAsync_DuplicatePaths_ThrowsDuplicatePath**: Two files with the same path are rejected
with `DuplicatePath`. This scenario is tested by `ReviewService_CreateDraftAsync_DuplicatePaths_ThrowsDuplicatePath`.

**ReviewService_CreateDraftAsync_PathsDifferingInCase_AreDistinct**: Two paths that differ only in letter case are
both accepted, because paths are compared ordinally. This scenario is tested by
`ReviewService_CreateDraftAsync_PathsDifferingInCase_AreDistinct`.

**ReviewService_CreateDraftAsync_BlankPath_ThrowsPathInvalid**: Empty and white-space paths are rejected with
`PathInvalid`. This scenario is tested by `ReviewService_CreateDraftAsync_BlankPath_ThrowsPathInvalid`.

**ReviewService_CreateDraftAsync_PathTooLong_ThrowsPathInvalid**: A path longer than the maximum is rejected with
`PathInvalid`. This scenario is tested by `ReviewService_CreateDraftAsync_PathTooLong_ThrowsPathInvalid`.

**ReviewService_CreateDraftAsync_FileSize_LimitIsInclusive**: A file of exactly the maximum size is accepted and a
file one byte larger is rejected with `FileTooLarge`. This scenario is tested by
`ReviewService_CreateDraftAsync_FileSize_LimitIsInclusive`.

**ReviewService_CreateDraftAsync_TooManyFiles_ThrowsTooManyFiles**: More files than the maximum are rejected with
`TooManyFiles`. This scenario is tested by `ReviewService_CreateDraftAsync_TooManyFiles_ThrowsTooManyFiles`.

**ReviewService_CreateDraftAsync_TotalTooLarge_ThrowsTotalSizeExceeded**: Files that are each within the limit but
together larger than the total limit are rejected with `TotalSizeExceeded`. This scenario is tested by
`ReviewService_CreateDraftAsync_TotalTooLarge_ThrowsTotalSizeExceeded`.

**ReviewService_GetAsync_CreatedReview_ReturnsReviewListAndContent**: A created review is read back, appears in
the list of its project, and the content of a file is read. This scenario is tested by
`ReviewService_GetAsync_CreatedReview_ReturnsReviewListAndContent`.

**ReviewService_GetFileContentAsync_MissingFileOrReview_ReturnsNull**: The content of a file that does not exist
in an existing review, and of a file of a review that does not exist, is read; both return null. This scenario is
tested by `ReviewService_GetFileContentAsync_MissingFileOrReview_ReturnsNull`.
