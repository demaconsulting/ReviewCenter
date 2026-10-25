package ReviewCenter {
    part def AuditCommand {
        doc /* Handler of the audit verify verb: verifies the hash chain of the audit trail in the configured database and reports the result through the exit code, 0 when the chain is intact and 1 when it is broken or cannot be read. */

        comment sourceRef /* Source: src/DemaConsulting.ReviewCenter/Hosting/AuditCommand.cs */
        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Tests/Hosting/AuditCommandTests.cs */
        comment designRef /* Design: docs/design/review-center/hosting/audit-command.md */
        comment verificationRef /* Verification: docs/verification/review-center/hosting/audit-command.md */
        comment reqRef /* Requirements: docs/reqstream/review-center/hosting/audit-command.yaml */
    }
}
