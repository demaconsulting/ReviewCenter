package ReviewCenter {
    private import SoftwareItemMetadata::*;

    part def AttestationService {
        doc /* Produces the attestation of a concluded review (approved or rejected) for an actor who may see the project, or for a request without an actor when the project has switched on anonymous attestation PDFs (otherwise, and always for a rejected review, the result is not found): refuses a review that is not concluded before any ballot or finding is read, reads the evidence settings of the project, builds the document description from the record (the checklist is the consolidated checklist of the submitted ballots, never a draft; the findings and included comments are every thread with an included comment (the set the record digest binds) with only its included comments, a thread whose starting comment is not included being a discussion thread, never a comment that was not included; the conclusion; the ballots appendix with a ballot fingerprint; with the reopenings of the review, whose reopener names follow the person labels) with the person names replaced by neutral labels when names are off, has it rendered, reads the review and the settings again and reports a change instead of signing, signs the rendered document, and records the release and its outcome in the audit trail (actor "anonymous" without an actor) only after the signing succeeded. Reports the outcome as a result with a status. Also holds the result type. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.ReviewCenter/Evidence/AttestationService.cs", "src/DemaConsulting.ReviewCenter/Evidence/AttestationResult.cs");
            testFiles = ("test/DemaConsulting.ReviewCenter.Tests/Evidence/AttestationServiceTests.cs", "test/DemaConsulting.ReviewCenter.Tests/Evidence/AttestationServiceRejectedTests.cs", "test/DemaConsulting.ReviewCenter.Tests/Evidence/AttestationContentSettingsTests.cs");
            designFiles = ("docs/design/review-center/evidence/attestation-service.md");
            verificationFiles = ("docs/verification/review-center/evidence/attestation-service.md");
            requirementFiles = ("docs/reqstream/review-center/evidence/attestation-service.yaml");
        }
    }
}
