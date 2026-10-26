package ReviewCenter {
    part def PlanPage {
        doc /* Page at /projects/{id}/plan that plans the reviews of a registered git repository: it takes a head revision and a baseline choice (none, an explicit revision, or the last signed review), shows one plain summary, the review-sets that need a review first (ticked by default), the sets that cannot be planned and those already filed only when they exist, and the sets with nothing to review collapsed, and creates the ticked reviews with a result for each. Every failure is explained in plain English with what to do next. */

        comment sourceRef /* Source: src/DemaConsulting.ReviewCenter/Web/PlanPage.razor and PlanPage.razor.cs */
        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Tests/Web/PlanPageTests.cs */
        comment designRef /* Design: docs/design/review-center/web/plan-page.md */
        comment verificationRef /* Verification: docs/verification/review-center/web/plan-page.md */
        comment reqRef /* Requirements: docs/reqstream/review-center/web/plan-page.yaml */
    }
}
