package ReviewCenter {
    part def AttestationSigner {
        doc /* Signs a PDF with a detached CMS signature (adbe.pkcs7.detached, SHA-256) made through the key of the attestation key provider, so that a non-exportable key is never read; appends the signature field and the signature as an incremental update that leaves the original bytes unchanged, and refuses keys and documents it cannot sign. Also holds the signer interface. */

        comment sourceRef /* Source: src/DemaConsulting.ReviewCenter/Evidence/AttestationSigner.cs, IAttestationSigner.cs */
        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Tests/Evidence/AttestationSignerTests.cs */
        comment designRef /* Design: docs/design/review-center/evidence/attestation-signer.md */
        comment verificationRef /* Verification: docs/verification/review-center/evidence/attestation-signer.md */
        comment reqRef /* Requirements: docs/reqstream/review-center/evidence/attestation-signer.yaml */
    }
}
