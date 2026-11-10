package ReviewCenter {
    private import SoftwareItemMetadata::*;

    part def RepositoryService {
        doc /* Rules for the registry of git repositories of a project: registration, editing, and enabling and disabling by the administrators of the project (a repository belongs to exactly one project and is never deleted), validation of names, locations, user names, and tokens, and protection of access tokens. Also defines the repository store, token protector, and git repository provider contracts and the repository types. File selection patterns are validated by SelectionMatcher, not here. */

        metadata info : SoftwareInfo {
            description = "Rules for the registry of git repositories of a project: registration, editing, and enabling and disabling by the administrators of the project (a repository belongs to exactly one project and is never deleted), validation of names, locations, user names, and tokens, and protection of access tokens. Also defines the repository store, token protector, and git repository provider contracts and the repository types. File selection patterns are validated by SelectionMatcher, not here.";
            sourceFiles = ("src/DemaConsulting.ReviewCenter.Core/RepositoryService.cs", "src/DemaConsulting.ReviewCenter.Core/SourceRepository.cs", "src/DemaConsulting.ReviewCenter.Core/IRepositoryStore.cs", "src/DemaConsulting.ReviewCenter.Core/ICredentialProtector.cs", "src/DemaConsulting.ReviewCenter.Core/IGitRepository.cs");
            testFiles = ("test/DemaConsulting.ReviewCenter.Tests/Core/RepositoryServiceTests.cs");
            designFiles = ("docs/design/review-center/core/repository-service.md");
            verificationFiles = ("docs/verification/review-center/core/repository-service.md");
            requirementFiles = ("docs/reqstream/review-center/core/repository-service.yaml");
        }
    }
}
