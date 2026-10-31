package ReviewCenter {
    part def ProjectChecklistService {
        doc /* Reads and changes the checklist definition that a project gives to its new reviews (the built-in default when it has none) for system administrators and project administrators of the project, never through an API token, accepting only a definition that passes the strict validation and carries a new version label when the wording changes, with a compare-and-set against the hash the caller started from and an audit entry that never contains the wording. */

        comment sourceRef /* Source: src/DemaConsulting.ReviewCenter.Core/ProjectChecklistService.cs, ProjectChecklist.cs, Project.cs, and IReviewStore.cs */
        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Tests/Core/ProjectChecklistServiceTests.cs and Storage/ProjectChecklistStoreTests.cs */
        comment designRef /* Design: docs/design/review-center/core/project-checklist-service.md */
        comment verificationRef /* Verification: docs/verification/review-center/core/project-checklist-service.md */
        comment reqRef /* Requirements: docs/reqstream/review-center/core/project-checklist-service.yaml */
    }
}
