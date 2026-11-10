package ReviewCenter {
    private import SoftwareItemMetadata::*;

    part def RepositoryStore {
        doc /* Database implementation of the repository store contract: persists repository registrations of a project with the encrypted access token, lists the repositories of one project, enforces a name that is unique within a project and ignores case, and records each change together with its audit entry in one transaction. */

        metadata info : SoftwareInfo {
            description = "Database implementation of the repository store contract: persists repository registrations of a project with the encrypted access token, lists the repositories of one project, enforces a name that is unique within a project and ignores case, and records each change together with its audit entry in one transaction.";
            sourceFiles = ("src/DemaConsulting.ReviewCenter/Storage/RepositoryStore.cs", "src/DemaConsulting.ReviewCenter/Storage/RepositoryRecord.cs");
            testFiles = ("test/DemaConsulting.ReviewCenter.Tests/Storage/RepositoryStoreTests.cs");
            designFiles = ("docs/design/review-center/storage/repository-store.md");
            verificationFiles = ("docs/verification/review-center/storage/repository-store.md");
            requirementFiles = ("docs/reqstream/review-center/storage/repository-store.yaml");
        }
    }
}
