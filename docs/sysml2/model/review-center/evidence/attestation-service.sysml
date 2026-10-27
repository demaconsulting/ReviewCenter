package ReviewCenter {
    part def AttestationService {
        doc /* Produces the attestation of a review: refuses a review that is not approved, builds the document description from the record, has it rendered and signed, and records the release in the audit trail only after the signing succeeded. Reports the outcome as a result with a status. Also holds the result type. */

        comment sourceRef /* Source: src/DemaConsulting.ReviewCenter/Evidence/AttestationService.cs, AttestationResult.cs */
        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Tests/Evidence/AttestationServiceTests.cs */
        comment designRef /* Design: docs/design/review-center/evidence/attestation-service.md */
        comment verificationRef /* Verification: docs/verification/review-center/evidence/attestation-service.md */
        comment reqRef /* Requirements: docs/reqstream/review-center/evidence/attestation-service.yaml */
    }
}
