package ReviewCenter {
    private import SoftwareItemMetadata::*;

    part def ProjectChecklistService {
        doc /* Reads and changes the checklist definition that a project gives to its new reviews (the built-in default when it has none) for system administrators and project administrators of the project, never through an API token, accepting only a definition that passes the strict validation and carries a new version label when the wording changes, with a compare-and-set against the hash the caller started from and an audit entry that never contains the wording. */

        metadata info : SoftwareInfo {
            description = "Reads and changes the checklist definition that a project gives to its new reviews (the built-in default when it has none) for system administrators and project administrators of the project, never through an API token, accepting only a definition that passes the strict validation and carries a new version label when the wording changes, with a compare-and-set against the hash the caller started from and an audit entry that never contains the wording.";
            sourceFiles = ("src/DemaConsulting.ReviewCenter.Core/ProjectChecklistService.cs", "src/DemaConsulting.ReviewCenter.Core/ProjectChecklist.cs", "src/DemaConsulting.ReviewCenter.Core/Project.cs", "src/DemaConsulting.ReviewCenter.Core/IReviewStore.cs");
            testFiles = ("test/DemaConsulting.ReviewCenter.Tests/Core/ProjectChecklistServiceTests.cs", "test/DemaConsulting.ReviewCenter.Tests/Storage/ProjectChecklistStoreTests.cs");
            designFiles = ("docs/design/review-center/core/project-checklist-service.md");
            verificationFiles = ("docs/verification/review-center/core/project-checklist-service.md");
            requirementFiles = ("docs/reqstream/review-center/core/project-checklist-service.yaml");
        }
    }
}
