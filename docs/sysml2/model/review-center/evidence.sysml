package ReviewCenter {
    part def EvidenceSubsystem {
        doc /* The signed PDF attestation of a complete review: a plain PDF writer with standard fonts, the layout of the attestation document, a detached CMS signature made with a key of the server (a self-signed key or a certificate from a certificate store, never exported), the selection and description of that key, the service that checks the approval, renders, signs and records the release in the audit trail, and the authorized download endpoint. */

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
    }
}
