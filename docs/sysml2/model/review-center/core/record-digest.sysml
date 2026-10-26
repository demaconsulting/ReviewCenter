package ReviewCenter {
    part def RecordDigest {
        doc /* Calculates the digest of a review record: the SHA-256 of a canonical text of the fingerprint, the source (upload, or repository, commits, selection), every change (path, kind, baseline and head hashes), the checklist answers, the sections that do not apply, and the threads with their side, anchor, and comments. A signature binds itself to this digest. */

        comment sourceRef /* Source: src/DemaConsulting.ReviewCenter.Core/RecordDigest.cs and ReviewRecord.cs */
        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Tests/Core/RecordDigestTests.cs */
        comment designRef /* Design: docs/design/review-center/core/record-digest.md */
        comment verificationRef /* Verification: docs/verification/review-center/core/record-digest.md */
        comment reqRef /* Requirements: docs/reqstream/review-center/core/record-digest.yaml */
    }
}
