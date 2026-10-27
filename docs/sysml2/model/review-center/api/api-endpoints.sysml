package ReviewCenter {
    part def ApiEndpoints {
        doc /* The routes of the JSON API under /api, the immutable request and answer types, and the mapping of a broken domain rule or an unusable key to an HTTP status with one problem description (application/problem+json): lists projects and reviews, reads a review, creates a draft review from files or from the changes of a repository, downloads the signed attestation of a complete review, and returns the evidence index of a project (the complete reviews that have a review-set identifier, each with an absolute attestation address; never cached). Offers no route that signs or arranges reviewers, and answers an unknown address under /api with a JSON 404. */

        comment sourceRef /* Source: src/DemaConsulting.ReviewCenter/Api/ApiEndpoints.cs, ApiModels.cs, and ApiProblems.cs */
        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Tests/Api/ApiEndpointsTests.cs */
        comment designRef /* Design: docs/design/review-center/api/api-endpoints.md */
        comment verificationRef /* Verification: docs/verification/review-center/api/api-endpoints.md */
        comment reqRef /* Requirements: docs/reqstream/review-center/api/api-endpoints.yaml */
    }
}
