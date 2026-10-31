package ReviewCenter {
    part def HostingSubsystem {
        doc /* Web server composition: listen address, endpoints, structured logging, database wiring, the Data Protection key ring and git settings wiring and validation (`Git:TimeoutSeconds` 1 to 3600), the protection of the key ring at rest by Windows DPAPI, a certificate, or the permissions of its folder (KeyRingProtection) with a guard that refuses a ring that the certificate does not open, a read-only scan of the stored secrets (SecretScan) that refuses a start with an empty key ring next to a database that holds secrets unless `DataProtection:AllowNewKeyRing` is true, and the authentication pipeline for the serve verb, the validation of the optional OpenID Connect sign-in settings (`Identity:Oidc`, off by default) and its registration only when enabled, the validation of the settings of the optional second factor (`Identity:Totp`, on by default) and its registration, the registration of the project access rules, the user add verb that creates users (the first one as a system administrator), and the audit verify verb that verifies the audit trail. */

        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Tests/Hosting/HostingSubsystemTests.cs */
        comment designRef /* Design: docs/design/review-center/hosting.md */
        comment verificationRef /* Verification: docs/verification/review-center/hosting.md */
        comment reqRef /* Requirements: docs/reqstream/review-center/hosting.yaml */

        part serverHost : ServerHost;
        part keyRingProtection : KeyRingProtection;
        part secretScan : SecretScan;
        part userCommand : UserCommand;
        part auditCommand : AuditCommand;
    }
}
