package ReviewCenter {
    part def ApiSubsystem {
        doc /* The JSON interface under /api for scripts and pipelines: the authentication scheme and policy that accept a personal access token, as a bearer token or as the password of HTTP Basic credentials, and nothing else, the routes that list the projects the owner of the token may see and their reviews, create a draft review from files or from the changes of a repository, download the attestation of a complete review, and serve the evidence index of a project (/projects/{id}/evidence/index.json) in the format ReviewMark reads, the single way of reporting a problem, and the OpenAPI document with its committed snapshot. It holds no domain rule; every route of a project first checks that the actor may see the project and answers a non-member exactly like a missing project (404), calls the same Core services as the web pages, and a token follows the memberships of its owner and never signs, arranges reviewers, or opens a page. The evidence index and the attestation PDF are the only two routes that a project can open, each by its own evidence setting, to a request without credentials (a wrong token is never anonymous; a refusal is the same generic 401 whether or not the project exists; the callers are limited per address), and the OpenAPI document marks exactly those two operations. */

        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Tests/Api/ApiSubsystemTests.cs, ApiProjectAccessTests.cs and AnonymousEvidenceAccessTests.cs */
        comment designRef /* Design: docs/design/review-center/api.md */
        comment verificationRef /* Verification: docs/verification/review-center/api.md */
        comment reqRef /* Requirements: docs/reqstream/review-center/api.yaml */

        part apiTokenAuthentication : ApiTokenAuthentication;
        part apiEndpoints : ApiEndpoints;
        part apiDocument : ApiDocument;
    }
}
