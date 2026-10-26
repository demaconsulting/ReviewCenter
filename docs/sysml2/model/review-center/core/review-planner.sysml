package ReviewCenter {
    part def ReviewPlanner {
        doc /* Plans the reviews of a head revision of a registered repository from the review-sets of its configuration file: chooses a baseline for each set (none, one given revision, or the head of the last signed review of the set when it is an ancestor), classifies each set, and creates the picked reviews one by one with generated identifiers and titles. */

        comment sourceRef /* Source: src/DemaConsulting.ReviewCenter.Core/ReviewPlanner.cs */
        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Tests/Core/ReviewPlannerTests.cs */
        comment designRef /* Design: docs/design/review-center/core/review-planner.md */
        comment verificationRef /* Verification: docs/verification/review-center/core/review-planner.md */
        comment reqRef /* Requirements: docs/reqstream/review-center/core/review-planner.yaml */
    }
}
