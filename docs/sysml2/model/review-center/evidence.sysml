package ReviewCenter {
    part def EvidenceSubsystem {
        doc /* The signed PDF attestation of a complete review: a plain PDF writer with standard fonts, the layout of the attestation document, a detached CMS signature made with a key of the server (a self-signed key or a certificate from a certificate store, never exported), the selection and description of that key, the service that checks the approval, applies the evidence settings of the project (which parts of a review a PDF holds, with a Content scope section that says what is left out), renders, signs and records the release in the audit trail, the authorized download route of the attestation, and the service that lists the completed reviews of a project, for a user who may see the project or, when the project allows it, for a request without a user, as the evidence index for ReviewMark. */

        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Tests/Evidence/EvidenceSubsystemTests.cs */
        comment designRef /* Design: docs/design/review-center/evidence.md */
        comment verificationRef /* Verification: docs/verification/review-center/evidence.md */
        comment reqRef /* Requirements: docs/reqstream/review-center/evidence.yaml */

        part pdfDocumentWriter : PdfDocumentWriter;
        part attestationRenderer : AttestationRenderer;
        part attestationSigner : AttestationSigner;
        part attestationKeyProvider : AttestationKeyProvider;
        part attestationService : AttestationService;
        part attestationEndpoint : AttestationEndpoint;
        part evidenceIndexService : EvidenceIndexService;
    }
}
