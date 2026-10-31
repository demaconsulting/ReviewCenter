package ReviewCenter {
    part def RecordDigest {
        doc /* Calculates the digest of a review record: the SHA-256 of a canonical text of the fingerprint, the source (upload, or repository, commits, selection), every change (path, kind, baseline and head hashes), the checklist answers, the sections that do not apply, the review conclusion text (only when there is one, so a record without one keeps its digest), and a `report` block (version 2) that lists the Findings and the included comments of every thread with their kind, side, anchor, author, text, and inclusion flag, but not whether a thread is open or resolved, so chat and the state of a thread are not part of the digest, and, only when the review was reopened, the number of reopenings (so a digest of a review that was never reopened is unchanged). A signature binds itself to this digest. */

        comment sourceRef /* Source: src/DemaConsulting.ReviewCenter.Core/RecordDigest.cs and ReviewRecord.cs */
        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Tests/Core/RecordDigestTests.cs, RecordDigestFollowUpTests.cs, and RecordDigestReportTests.cs */
        comment designRef /* Design: docs/design/review-center/core/record-digest.md */
        comment verificationRef /* Verification: docs/verification/review-center/core/record-digest.md */
        comment reqRef /* Requirements: docs/reqstream/review-center/core/record-digest.yaml */
    }
}
