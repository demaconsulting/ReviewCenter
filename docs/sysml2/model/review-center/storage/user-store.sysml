package ReviewCenter {
    private import SoftwareItemMetadata::*;

    part def UserStore {
        doc /* Database implementation of the Core user store interface that saves, finds, lists, and updates users with their system administrator and must-change-password flags and the time of the last sign-in, finds a user by the exact issuer and subject of an OpenID Connect provider, creates a user together with a project membership and its audit entry in one transaction, with user names unique regardless of case. */

        metadata info : SoftwareInfo {
            description = "Database implementation of the Core user store interface that saves, finds, lists, and updates users with their system administrator and must-change-password flags and the time of the last sign-in, finds a user by the exact issuer and subject of an OpenID Connect provider, creates a user together with a project membership and its audit entry in one transaction, with user names unique regardless of case.";
            sourceFiles = ("src/DemaConsulting.ReviewCenter/Storage/UserStore.cs");
            testFiles = ("test/DemaConsulting.ReviewCenter.Tests/Storage/UserStoreTests.cs", "test/DemaConsulting.ReviewCenter.Tests/Storage/ProjectMembersStoreTests.cs");
            designFiles = ("docs/design/review-center/storage/user-store.md");
            verificationFiles = ("docs/verification/review-center/storage/user-store.md");
            requirementFiles = ("docs/reqstream/review-center/storage/user-store.yaml");
        }
    }
}
