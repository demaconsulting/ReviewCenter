package ReviewCenter {
    private import SoftwareItemMetadata::*;

    part def AuditChain {
        doc /* Defines the audit entry and draft types, the canonical encoding and SHA-256 hash of an entry, the cleaning of the text that enters the audit trail, and the streaming verifier of the hash chain, which detects edited, missing, inserted, and reordered entries in the middle of the history. */

        metadata info : SoftwareInfo {
            description = "Defines the audit entry and draft types, the canonical encoding and SHA-256 hash of an entry, the cleaning of the text that enters the audit trail, and the streaming verifier of the hash chain, which detects edited, missing, inserted, and reordered entries in the middle of the history.";
            sourceFiles = ("src/DemaConsulting.ReviewCenter.Core/AuditChain.cs", "src/DemaConsulting.ReviewCenter.Core/AuditText.cs", "src/DemaConsulting.ReviewCenter.Core/AuditEntry.cs", "src/DemaConsulting.ReviewCenter.Core/AuditDraft.cs");
            testFiles = ("test/DemaConsulting.ReviewCenter.Tests/Core/AuditChainTests.cs", "test/DemaConsulting.ReviewCenter.Tests/Core/AuditTextTests.cs");
            designFiles = ("docs/design/review-center/core/audit-chain.md");
            verificationFiles = ("docs/verification/review-center/core/audit-chain.md");
            requirementFiles = ("docs/reqstream/review-center/core/audit-chain.yaml");
        }
    }
}
