package ReviewCenter {
    private import SoftwareItemMetadata::*;

    part def CredentialProtector {
        doc /* Encrypts and decrypts repository access tokens with a purpose-bound Data Protection protector, so a token is never stored or logged as plain text. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.ReviewCenter/ContentSources/CredentialProtector.cs");
            testFiles = ("test/DemaConsulting.ReviewCenter.Tests/ContentSources/CredentialProtectorTests.cs");
            designFiles = ("docs/design/review-center/content-sources/credential-protector.md");
            verificationFiles = ("docs/verification/review-center/content-sources/credential-protector.md");
            requirementFiles = ("docs/reqstream/review-center/content-sources/credential-protector.yaml");
        }
    }
}
