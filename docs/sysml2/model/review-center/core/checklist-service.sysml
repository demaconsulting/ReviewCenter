package ReviewCenter {
    part def ChecklistService {
        doc /* Provides the review checklist: the default checklist (sections of checks, taken from the Continuous Compliance review template), the frozen copy a review keeps from its creation, and the answers. Marks sections as applicable or not (a reason is required when not), records Pass, Fail, or N/A for each check (a justification is required for Fail and N/A) with the signed-in user (user name and display name) and the UTC time, requires that the actor may see the project (`IProjectAccess`) and that the review is not approved and locked (ReviewLocked), refuses a change that would make a current signature out of date unless the caller acknowledges the affected slots (SignaturesWouldBecomeOutOfDate), refuses the author of the review to answer a check or change the applicability of a section while the project requires that authors do not answer the checklist of their own review (AuthorCannotAnswer; on by default, for every project; reads are not affected, and `ChecklistState.AuthorBlocked` tells the page), enforces these rules, and derives the progress per section and overall, completeness, and the first unanswered check. */

        comment sourceRef /* Source: src/DemaConsulting.ReviewCenter.Core/ChecklistService.cs, ChecklistState.cs, ChecklistDefinition.cs, ChecklistValidator.cs, ChecklistValidationResult.cs, DefaultChecklist.cs, ReviewChecklist.cs, ChecklistAnswer.cs, ChecklistSectionState.cs, ChecklistProgress.cs, and ChecklistOutcome.cs */
        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Tests/Core/ChecklistServiceTests.cs and ChecklistValidatorTests.cs */
        comment designRef /* Design: docs/design/review-center/core/checklist-service.md */
        comment verificationRef /* Verification: docs/verification/review-center/core/checklist-service.md */
        comment reqRef /* Requirements: docs/reqstream/review-center/core/checklist-service.yaml */
    }
}
