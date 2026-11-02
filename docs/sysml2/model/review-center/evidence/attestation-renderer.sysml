package ReviewCenter {
    part def AttestationRenderer {
        doc /* Lays out the attestation document of a review from a description of it: the review and its content, a Content scope section that states the evidence settings in effect and every omitted part, the checklist (with who answered each check and when, or "Not answered" for a check that the ballots leave pending or disputed), the findings and included comments (each thread with its status and only the comments that were included; a thread whose starting comment is not included is shown as a discussion thread, never a comment that was not included), the review conclusion with its outcome and text, the sign-offs with the record digest, a Reopen history section only for a review that was reopened, a Ballots appendix with the submitted ballot of every slot and a ballot fingerprint, the statement about the signing key (with a note for a code-signing-only certificate), and the statement of what the signature proves and does not prove. A rejected review is titled and bannered as rejected. Applies the content switches of the project (threads, checklist wording, file list, git context are omitted when off; the person names arrive already replaced by labels). Also holds the renderer interface and the document description type. */
        comment sourceRef /* Source: src/DemaConsulting.ReviewCenter/Evidence/AttestationRenderer.cs, IAttestationRenderer.cs, AttestationDocument.cs */
        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Tests/Evidence/AttestationRendererTests.cs, AttestationRendererReportTests.cs */
        comment designRef /* Design: docs/design/review-center/evidence/attestation-renderer.md */
        comment verificationRef /* Verification: docs/verification/review-center/evidence/attestation-renderer.md */
        comment reqRef /* Requirements: docs/reqstream/review-center/evidence/attestation-renderer.yaml */
    }
}
