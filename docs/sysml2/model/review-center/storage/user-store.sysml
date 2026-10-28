package ReviewCenter {
    part def UserStore {
        doc /* Database implementation of the Core user store interface that saves, finds, lists, and updates users with their system administrator and must-change-password flags, creates a user together with a project membership and its audit entry in one transaction, with user names unique regardless of case. */

        comment sourceRef /* Source: src/DemaConsulting.ReviewCenter/Storage/UserStore.cs */
        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Tests/Storage/UserStoreTests.cs and ProjectMembersStoreTests.cs */
        comment designRef /* Design: docs/design/review-center/storage/user-store.md */
        comment verificationRef /* Verification: docs/verification/review-center/storage/user-store.md */
        comment reqRef /* Requirements: docs/reqstream/review-center/storage/user-store.yaml */
    }
}
