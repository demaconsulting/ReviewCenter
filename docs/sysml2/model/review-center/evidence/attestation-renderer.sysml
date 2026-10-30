package ReviewCenter {
    part def AttestationRenderer {
        doc /* Lays out the attestation document of a review from a description of it: the review and its content, a Content scope section that states the evidence settings in effect and every omitted part, the findings (checklist and threads), the sign-offs with the record digest, a Reopen history section only for a review that was reopened, the statement about the signing key (with a note for a code-signing-only certificate), and the statement of what the signature proves and does not prove. Applies the content switches of the project (threads, checklist wording, file list, git context are omitted when off; the person names arrive already replaced by labels). Also holds the renderer interface and the document description type. */

        comment sourceRef /* Source: src/DemaConsulting.ReviewCenter/Evidence/AttestationRenderer.cs, IAttestationRenderer.cs, AttestationDocument.cs */
        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Tests/Evidence/AttestationRendererTests.cs */
        comment designRef /* Design: docs/design/review-center/evidence/attestation-renderer.md */
        comment verificationRef /* Verification: docs/verification/review-center/evidence/attestation-renderer.md */
        comment reqRef /* Requirements: docs/reqstream/review-center/evidence/attestation-renderer.yaml */
    }
}
