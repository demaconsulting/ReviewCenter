package ReviewCenter {
    private import SoftwareItemMetadata::*;

    part def PersonalAccessTokenStore {
        doc /* Database implementation of the token store contract: persists personal access tokens by their hash only, finds a token by hash with its owner, lists the tokens of a user, revokes a token, and records the last use, with the audit entry of each change stored in the same transaction. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.ReviewCenter/Storage/PersonalAccessTokenStore.cs");
            testFiles = ("test/DemaConsulting.ReviewCenter.Tests/Storage/PersonalAccessTokenStoreTests.cs");
            designFiles = ("docs/design/review-center/storage/personal-access-token-store.md");
            verificationFiles = ("docs/verification/review-center/storage/personal-access-token-store.md");
            requirementFiles = ("docs/reqstream/review-center/storage/personal-access-token-store.yaml");
        }
    }
}
