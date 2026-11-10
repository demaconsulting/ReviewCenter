package ReviewCenter {
    private import SoftwareItemMetadata::*;

    part def EvidenceSubsystem {
        doc /* The signed PDF attestation of a concluded review, approved or rejected: a plain PDF writer with standard fonts, the layout of the attestation document (with the included findings, the conclusion, and a ballots appendix), a detached CMS signature made with a key of the server (a self-signed key, a certificate from a certificate store, or a certificate file, never exported), the selection and description of that key, the service that checks the conclusion, applies the evidence settings of the project (which parts of a review a PDF holds, with a Content scope section that says what is left out), renders, signs and records the release in the audit trail, the authorized download route of the attestation, and the service that lists the approved reviews of a project, for a user who may see the project or, when the project allows it, for a request without a user, as the evidence index for ReviewMark (a rejected review is never listed). */

        metadata info : SoftwareInfo {
            description = "The signed PDF attestation of a concluded review, approved or rejected: a plain PDF writer with standard fonts, the layout of the attestation document (with the included findings, the conclusion, and a ballots appendix), a detached CMS signature made with a key of the server (a self-signed key, a certificate from a certificate store, or a certificate file, never exported), the selection and description of that key, the service that checks the conclusion, applies the evidence settings of the project (which parts of a review a PDF holds, with a Content scope section that says what is left out), renders, signs and records the release in the audit trail, the authorized download route of the attestation, and the service that lists the approved reviews of a project, for a user who may see the project or, when the project allows it, for a request without a user, as the evidence index for ReviewMark (a rejected review is never listed).";
            testFiles = ("test/DemaConsulting.ReviewCenter.Tests/Evidence/EvidenceSubsystemTests.cs");
            designFiles = ("docs/design/review-center/evidence.md");
            verificationFiles = ("docs/verification/review-center/evidence.md");
            requirementFiles = ("docs/reqstream/review-center/evidence.yaml");
        }

        part pdfDocumentWriter : PdfDocumentWriter;
        part attestationRenderer : AttestationRenderer;
        part attestationSigner : AttestationSigner;
        part attestationKeyProvider : AttestationKeyProvider;
        part attestationService : AttestationService;
        part attestationEndpoint : AttestationEndpoint;
        part evidenceIndexService : EvidenceIndexService;
    }
}
