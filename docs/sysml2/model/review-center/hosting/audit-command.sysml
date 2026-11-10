package ReviewCenter {
    private import SoftwareItemMetadata::*;

    part def AuditCommand {
        doc /* Handler of the audit verify verb: verifies the hash chain of the audit trail in the configured database and reports the result through the exit code, 0 when the chain is intact and 1 when it is broken or cannot be read. */

        metadata info : SoftwareInfo {
            description = "Handler of the audit verify verb: verifies the hash chain of the audit trail in the configured database and reports the result through the exit code, 0 when the chain is intact and 1 when it is broken or cannot be read.";
            sourceFiles = ("src/DemaConsulting.ReviewCenter/Hosting/AuditCommand.cs");
            testFiles = ("test/DemaConsulting.ReviewCenter.Tests/Hosting/AuditCommandTests.cs");
            designFiles = ("docs/design/review-center/hosting/audit-command.md");
            verificationFiles = ("docs/verification/review-center/hosting/audit-command.md");
            requirementFiles = ("docs/reqstream/review-center/hosting/audit-command.yaml");
        }
    }
}
