package ReviewCenter {
    private import SoftwareItemMetadata::*;

    part def AttestationEndpoint {
        doc /* The authorized download route of the attestation, GET /projects/{projectId}/reviews/{reviewId}/attestation.pdf: requires a signed-in user (the anonymous setting of a project opens only the API route), answers a user who may not see the project with an empty 404, like a missing project, answers the signed PDF of an approved or rejected review as a download with caching disabled, and maps a missing review to 404, a review that is not concluded to 409, and an unusable key to 500. The result mapping `ToResult` is shared with the attestation route of the API. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.ReviewCenter/Evidence/AttestationEndpoint.cs");
            testFiles = ("test/DemaConsulting.ReviewCenter.Tests/Evidence/AttestationEndpointTests.cs");
            designFiles = ("docs/design/review-center/evidence/attestation-endpoint.md");
            verificationFiles = ("docs/verification/review-center/evidence/attestation-endpoint.md");
            requirementFiles = ("docs/reqstream/review-center/evidence/attestation-endpoint.yaml");
        }
    }
}
