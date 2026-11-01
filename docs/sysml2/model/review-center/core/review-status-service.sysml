package ReviewCenter {
    part def ReviewStatusService {
        doc /* Tells one viewer of a review where the review stands: the ballot state and the signature state of every reviewer (never the content of a ballot), the checks the reviewers answered differently with whether a discussion about each can be started, the number of open questions and findings, the one thing the viewer should do next (a pure rule that gives exactly one action), and, for each reviewer whose signature no longer matches the report, the changes since the signature that can alter the report, derived from the audit trail and worded in plain language. A viewer who may not see the answers of the other reviewers yet gets no disputed check, no count of discussions about checks, and no answer in a change. The status is computed on demand from the other services, stores nothing, and explains the changes without proving them. */

        comment sourceRef /* Source: src/DemaConsulting.ReviewCenter.Core/ReviewStatusService.cs, src/DemaConsulting.ReviewCenter.Core/ReviewStatus.cs, src/DemaConsulting.ReviewCenter.Core/ReviewNextAction.cs, src/DemaConsulting.ReviewCenter.Core/ReviewChangeRules.cs */
        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Tests/Core/ReviewNextActionTests.cs and test/DemaConsulting.ReviewCenter.Tests/Core/ReviewStatusServiceTests.cs */
        comment designRef /* Design: docs/design/review-center/core/review-status-service.md */
        comment verificationRef /* Verification: docs/verification/review-center/core/review-status-service.md */
        comment reqRef /* Requirements: docs/reqstream/review-center/core/review-status-service.yaml */
    }
}
