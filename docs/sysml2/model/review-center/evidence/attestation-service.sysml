package ReviewCenter {
    part def AttestationService {
        doc /* Produces the attestation of a review for an actor who may see the project, or for a request without an actor when the project has switched on anonymous attestation PDFs (otherwise the result is not found): refuses a review that is not approved, reads the evidence settings of the project, builds the document description from the record (with the reopenings of the review, whose reopener names follow the person labels) with the person names replaced by neutral labels when names are off, has it rendered, reads the review and the settings again and reports a change instead of signing, signs the rendered document, and records the release in the audit trail (actor "anonymous" without an actor) only after the signing succeeded. Reports the outcome as a result with a status. Also holds the result type. */

        comment sourceRef /* Source: src/DemaConsulting.ReviewCenter/Evidence/AttestationService.cs, AttestationResult.cs */
        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Tests/Evidence/AttestationServiceTests.cs, AttestationContentSettingsTests.cs */
        comment designRef /* Design: docs/design/review-center/evidence/attestation-service.md */
        comment verificationRef /* Verification: docs/verification/review-center/evidence/attestation-service.md */
        comment reqRef /* Requirements: docs/reqstream/review-center/evidence/attestation-service.yaml */
    }
}
