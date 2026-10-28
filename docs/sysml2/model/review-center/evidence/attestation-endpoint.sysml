package ReviewCenter {
    part def AttestationEndpoint {
        doc /* The authorized download route of the attestation, GET /projects/{projectId}/reviews/{reviewId}/attestation.pdf: requires a signed-in user, answers a user who may not see the project with an empty 404, like a missing project, answers the signed PDF as a download with caching disabled, and maps a missing review to 404, a review that is not complete to 409, and an unusable key to 500. The result mapping `ToResult` is shared with the attestation route of the API. */

        comment sourceRef /* Source: src/DemaConsulting.ReviewCenter/Evidence/AttestationEndpoint.cs */
        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Tests/Evidence/AttestationEndpointTests.cs */
        comment designRef /* Design: docs/design/review-center/evidence/attestation-endpoint.md */
        comment verificationRef /* Verification: docs/verification/review-center/evidence/attestation-endpoint.md */
        comment reqRef /* Requirements: docs/reqstream/review-center/evidence/attestation-endpoint.yaml */
    }
}
