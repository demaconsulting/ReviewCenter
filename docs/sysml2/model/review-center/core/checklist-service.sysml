package ReviewCenter {
    part def ChecklistService {
        doc /* Provides the review checklist: the default checklist (sections of checks, taken from the Continuous Compliance review template), the frozen copy a review keeps from its creation, and the answers. Marks sections as applicable or not (a reason is required when not), records Pass, Fail, or N/A for each check (a justification is required for Fail and N/A) with the signed-in user (user name and display name) and the UTC time, requires that the actor may see the project (`IProjectAccess`) and that the review is not approved and locked (ReviewLocked), enforces these rules, and derives the progress per section and overall, completeness, and the first unanswered check. */

        comment sourceRef /* Source: src/DemaConsulting.ReviewCenter.Core/ChecklistService.cs, ChecklistState.cs, ChecklistDefinition.cs, ChecklistValidator.cs, ChecklistValidationResult.cs, DefaultChecklist.cs, ReviewChecklist.cs, ChecklistAnswer.cs, ChecklistSectionState.cs, ChecklistProgress.cs, and ChecklistOutcome.cs */
        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Tests/Core/ChecklistServiceTests.cs and ChecklistValidatorTests.cs */
        comment designRef /* Design: docs/design/review-center/core/checklist-service.md */
        comment verificationRef /* Verification: docs/verification/review-center/core/checklist-service.md */
        comment reqRef /* Requirements: docs/reqstream/review-center/core/checklist-service.yaml */
    }
}
