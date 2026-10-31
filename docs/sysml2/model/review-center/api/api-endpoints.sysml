package ReviewCenter {
    part def ApiEndpoints {
        doc /* The routes of the JSON API under /api, the immutable request and answer types, and the mapping of a broken domain rule (among them the last project admin, too many attempts, a pending password change, and a review that is locked or not locked, which are conflicts with status 409) or an unusable key to an HTTP status with one problem description (application/problem+json): lists the projects the actor may see and their reviews (every route of a project asks the project service first and answers a non-member like a missing project with 404), reads a review, creates a draft review from files or from the changes of a repository, downloads the signed attestation of a complete review, and returns the evidence index of a project (the complete reviews that have a review-set identifier, each with an absolute attestation address; never cached), and returns the checklist template of a project (the definition, version label, default flag, hash, and counts; read-only). The attestation and evidence index routes sit in a second route group with their own policies and a per-address limit (429 with Retry-After) for requests without credentials, and are the only routes a project can open to a caller without a token; every other route requires the token policy. Offers no route that signs, arranges reviewers, or reopens a review, and answers an unknown address under /api with a JSON 404. */

        comment sourceRef /* Source: src/DemaConsulting.ReviewCenter/Api/ApiEndpoints.cs, ApiModels.cs, and ApiProblems.cs */
        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Tests/Api/ApiEndpointsTests.cs and ApiProjectAccessTests.cs */
        comment designRef /* Design: docs/design/review-center/api/api-endpoints.md */
        comment verificationRef /* Verification: docs/verification/review-center/api/api-endpoints.md */
        comment reqRef /* Requirements: docs/reqstream/review-center/api/api-endpoints.yaml */
    }
}
