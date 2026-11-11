package ReviewCenter {
    private import SoftwareItemMetadata::*;

    part def TotpSecretProtector {
        doc /* Encrypts the secret of a second factor with ASP.NET Data Protection before it is stored, with a purpose that is bound to the user so that a copy of the value of one user cannot be read as that of another, and reports a secret that cannot be read (for example after a loss of the key ring) as unreadable and not as an error. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.ReviewCenter/Identity/TotpSecretProtector.cs");
            testFiles = ("test/DemaConsulting.ReviewCenter.Tests/Identity/TotpSecretProtectorTests.cs");
            designFiles = ("docs/design/review-center/identity/totp-secret-protector.md");
            verificationFiles = ("docs/verification/review-center/identity/totp-secret-protector.md");
            requirementFiles = ("docs/reqstream/review-center/identity/totp-secret-protector.yaml");
        }
    }
}
