package ReviewCenter {
    part def HostingSubsystem {
        doc /* Web server composition: listen address, endpoints, structured logging, database wiring, the Data Protection key ring and git settings wiring and validation (`Git:TimeoutSeconds` 1 to 3600), and the authentication pipeline for the serve verb, the user add verb that creates users, and the audit verify verb that verifies the audit trail. */

        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Tests/Hosting/HostingSubsystemTests.cs */
        comment designRef /* Design: docs/design/review-center/hosting.md */
        comment verificationRef /* Verification: docs/verification/review-center/hosting.md */
        comment reqRef /* Requirements: docs/reqstream/review-center/hosting.yaml */

        part serverHost : ServerHost;
        part userCommand : UserCommand;
        part auditCommand : AuditCommand;
    }
}
