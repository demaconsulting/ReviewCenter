package ReviewCenter {
    private import SoftwareItemMetadata::*;

    part def PlanPage {
        doc /* Page at /projects/{id}/plan that plans the reviews of a git repository of the project (a project the user may not see looks like a missing one): it takes a head revision and a baseline choice (none, an explicit revision, or the last approved review), shows one plain summary, the review-sets that need a review first (ticked by default), the sets that cannot be planned and those already filed only when they exist, and the sets with nothing to review collapsed, and creates the ticked reviews with a result for each. Every failure is explained in plain English with what to do next. */

        metadata info : SoftwareInfo {
            description = "Page at /projects/{id}/plan that plans the reviews of a git repository of the project (a project the user may not see looks like a missing one): it takes a head revision and a baseline choice (none, an explicit revision, or the last approved review), shows one plain summary, the review-sets that need a review first (ticked by default), the sets that cannot be planned and those already filed only when they exist, and the sets with nothing to review collapsed, and creates the ticked reviews with a result for each. Every failure is explained in plain English with what to do next.";
            sourceFiles = ("src/DemaConsulting.ReviewCenter/Web/PlanPage.razor", "src/DemaConsulting.ReviewCenter/Web/PlanPage.razor.cs");
            testFiles = ("test/DemaConsulting.ReviewCenter.Tests/Web/PlanPageTests.cs");
            designFiles = ("docs/design/review-center/web/plan-page.md");
            verificationFiles = ("docs/verification/review-center/web/plan-page.md");
            requirementFiles = ("docs/reqstream/review-center/web/plan-page.yaml");
        }
    }
}
