package ReviewCenter {
    part def KeyRingProtection {
        doc /* Decides how the Data Protection key ring is protected at rest and owns the `DataProtection` settings: `Auto` (the default) chooses the certificate when `DataProtection:CertificatePath` is set, otherwise Windows DPAPI on Windows and the permissions of the key folder (0700, key files 0600) with a warning at every start elsewhere; `Dpapi` (current user or local machine, Windows only), `Certificate` (an RSA certificate with a private key from a PFX, PKCS#12, or PEM file, with the password resolved by SecretSource and previous certificates that can still decrypt the ring), and `PermissionsOnly` can be chosen explicitly, and `RequireEncryptedKeys` refuses a ring that is not encrypted. It wires the encryption into the Data Protection builder and checks the ring before a key can be created, so that a wrong certificate stops the start instead of silently creating a new key. A wrong setting stops the start with a message that names the setting and never holds the password. */

        comment sourceRef /* Source: src/DemaConsulting.ReviewCenter/Hosting/KeyRingProtection.cs, src/DemaConsulting.ReviewCenter/Hosting/KeyRingProtectionMode.cs */
        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Tests/Hosting/KeyRingProtectionTests.cs */
        comment designRef /* Design: docs/design/review-center/hosting/key-ring-protection.md */
        comment verificationRef /* Verification: docs/verification/review-center/hosting/key-ring-protection.md */
        comment reqRef /* Requirements: docs/reqstream/review-center/hosting/key-ring-protection.yaml */
    }
}
