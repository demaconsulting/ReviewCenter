package ReviewCenter {
    part def ReviewStore {
        doc /* Database implementation of the Core store interface that saves and reads projects (with their evidence settings, which are changed only by an atomic compare-and-set together with its audit entry), project memberships (added, changed, and removed with an audit entry that carries the project, the last enabled project administrator protected atomically on request), reviews (with their source and change kinds), file content and baseline content, comment threads, checklist snapshots and answers, reviewer slots, and signatures, the reopenings of an approved review (with their audit entry in the same transaction) and the per-project rule that requires independent reviewers (an atomic compare-and-set with its audit entry), reads the whole record of a review (including the number of reopenings), refuses a signature of a record that changed, and refuses inside the transaction every change of the content or the reviewers of a review that is approved and locked (ReviewLocked). */

        comment sourceRef /* Source: src/DemaConsulting.ReviewCenter/Storage/ReviewStore.cs */
        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Tests/Storage/ReviewStoreTests.cs, ReviewStoreRepositoryTests.cs, ReviewStoreSigningTests.cs, ReviewStoreMembershipTests.cs, ProjectMembersStoreTests.cs, and EvidenceSettingsStoreTests.cs */
        comment designRef /* Design: docs/design/review-center/storage/review-store.md */
        comment verificationRef /* Verification: docs/verification/review-center/storage/review-store.md */
        comment reqRef /* Requirements: docs/reqstream/review-center/storage/review-store.yaml */
    }
}
