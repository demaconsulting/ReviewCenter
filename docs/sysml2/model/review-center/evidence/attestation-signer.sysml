package ReviewCenter {
    private import SoftwareItemMetadata::*;

    part def AttestationSigner {
        doc /* Signs a PDF with a detached CMS signature (adbe.pkcs7.detached, SHA-256) made through the key of the attestation key provider, so that a non-exportable key is never read; appends the signature field and the signature as an incremental update that leaves the original bytes unchanged, makes it a certifying signature that allows no changes (DocMDP permission 1), and refuses keys and documents it cannot sign. Also holds the signer interface. */

        metadata info : SoftwareInfo {
            description = "Signs a PDF with a detached CMS signature (adbe.pkcs7.detached, SHA-256) made through the key of the attestation key provider, so that a non-exportable key is never read; appends the signature field and the signature as an incremental update that leaves the original bytes unchanged, makes it a certifying signature that allows no changes (DocMDP permission 1), and refuses keys and documents it cannot sign. Also holds the signer interface.";
            sourceFiles = ("src/DemaConsulting.ReviewCenter/Evidence/AttestationSigner.cs", "src/DemaConsulting.ReviewCenter/Evidence/IAttestationSigner.cs");
            testFiles = ("test/DemaConsulting.ReviewCenter.Tests/Evidence/AttestationSignerTests.cs");
            designFiles = ("docs/design/review-center/evidence/attestation-signer.md");
            verificationFiles = ("docs/verification/review-center/evidence/attestation-signer.md");
            requirementFiles = ("docs/reqstream/review-center/evidence/attestation-signer.yaml");
        }
    }
}
