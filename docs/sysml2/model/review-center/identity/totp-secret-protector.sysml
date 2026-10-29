package ReviewCenter {
    part def TotpSecretProtector {
        doc /* Encrypts the secret of a second factor with ASP.NET Data Protection before it is stored, with a purpose that is bound to the user so that a copy of the value of one user cannot be read as that of another, and reports a secret that cannot be read (for example after a loss of the key ring) as unreadable and not as an error. */

        comment sourceRef /* Source: src/DemaConsulting.ReviewCenter/Identity/TotpSecretProtector.cs */
        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Tests/Identity/TotpSecretProtectorTests.cs */
        comment designRef /* Design: docs/design/review-center/identity/totp-secret-protector.md */
        comment verificationRef /* Verification: docs/verification/review-center/identity/totp-secret-protector.md */
        comment reqRef /* Requirements: docs/reqstream/review-center/identity/totp-secret-protector.yaml */
    }
}
