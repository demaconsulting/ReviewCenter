package ReviewCenter {
    part def ApiSubsystem {
        doc /* The JSON interface under /api for scripts and pipelines: the authentication scheme and policy that accept a personal access token and nothing else, the routes that list projects and reviews, create a draft review from files or from the changes of a repository, and download the attestation of a complete review, the single way of reporting a problem, and the OpenAPI document with its committed snapshot. It holds no domain rule; every route calls the same Core services as the web pages, and a token never signs, arranges reviewers, or opens a page. */

        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Tests/Api/ApiSubsystemTests.cs */
        comment designRef /* Design: docs/design/review-center/api.md */
        comment verificationRef /* Verification: docs/verification/review-center/api.md */
        comment reqRef /* Requirements: docs/reqstream/review-center/api.yaml */

        part apiTokenAuthentication : ApiTokenAuthentication;
        part apiEndpoints : ApiEndpoints;
        part apiDocument : ApiDocument;
    }
}
