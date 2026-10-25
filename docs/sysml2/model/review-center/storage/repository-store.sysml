package ReviewCenter {
    part def RepositoryStore {
        doc /* Database implementation of the repository store contract: persists repository registrations with the encrypted access token, enforces a unique name that ignores case, and records each change together with its audit entry in one transaction. */

        comment sourceRef /* Source: src/DemaConsulting.ReviewCenter/Storage/RepositoryStore.cs and RepositoryRecord.cs */
        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Tests/Storage/RepositoryStoreTests.cs */
        comment designRef /* Design: docs/design/review-center/storage/repository-store.md */
        comment verificationRef /* Verification: docs/verification/review-center/storage/repository-store.md */
        comment reqRef /* Requirements: docs/reqstream/review-center/storage/repository-store.yaml */
    }
}
