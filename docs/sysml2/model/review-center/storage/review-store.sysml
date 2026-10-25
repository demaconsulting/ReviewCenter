package ReviewCenter {
    part def ReviewStore {
        doc /* Database implementation of the Core store interface that saves and reads projects, reviews (with their source and change kinds), file content and baseline content, comment threads, checklist snapshots and answers, reviewer slots, and signatures, reads the whole record of a review, and refuses a signature of a record that changed. */

        comment sourceRef /* Source: src/DemaConsulting.ReviewCenter/Storage/ReviewStore.cs */
        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Tests/Storage/ReviewStoreTests.cs, ReviewStoreRepositoryTests.cs, and ReviewStoreSigningTests.cs */
        comment designRef /* Design: docs/design/review-center/storage/review-store.md */
        comment verificationRef /* Verification: docs/verification/review-center/storage/review-store.md */
        comment reqRef /* Requirements: docs/reqstream/review-center/storage/review-store.yaml */
    }
}
