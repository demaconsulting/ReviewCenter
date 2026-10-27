package ReviewCenter {
    part def PersonalAccessTokenStore {
        doc /* Database implementation of the token store contract: persists personal access tokens by their hash only, finds a token by hash with its owner, lists the tokens of a user, revokes a token, and records the last use, with the audit entry of each change stored in the same transaction. */

        comment sourceRef /* Source: src/DemaConsulting.ReviewCenter/Storage/PersonalAccessTokenStore.cs */
        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Tests/Storage/PersonalAccessTokenStoreTests.cs */
        comment designRef /* Design: docs/design/review-center/storage/personal-access-token-store.md */
        comment verificationRef /* Verification: docs/verification/review-center/storage/personal-access-token-store.md */
        comment reqRef /* Requirements: docs/reqstream/review-center/storage/personal-access-token-store.yaml */
    }
}
