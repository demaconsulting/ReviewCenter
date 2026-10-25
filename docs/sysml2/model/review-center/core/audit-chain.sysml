package ReviewCenter {
    part def AuditChain {
        doc /* Defines the audit entry and draft types, the canonical encoding and SHA-256 hash of an entry, the cleaning of the text that enters the audit trail, and the streaming verifier of the hash chain, which detects edited, missing, inserted, and reordered entries in the middle of the history. */

        comment sourceRef /* Source: src/DemaConsulting.ReviewCenter.Core/AuditChain.cs, AuditText.cs, AuditEntry.cs, and AuditDraft.cs */
        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Tests/Core/AuditChainTests.cs and AuditTextTests.cs */
        comment designRef /* Design: docs/design/review-center/core/audit-chain.md */
        comment verificationRef /* Verification: docs/verification/review-center/core/audit-chain.md */
        comment reqRef /* Requirements: docs/reqstream/review-center/core/audit-chain.yaml */
    }
}
