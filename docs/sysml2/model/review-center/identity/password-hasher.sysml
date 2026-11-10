package ReviewCenter {
    private import SoftwareItemMetadata::*;

    part def PasswordHasher {
        doc /* Adapter that gives the Core password hasher interface a salted, slow password hash and its verification, using the password hasher of the ASP.NET Core shared framework. */

        metadata info : SoftwareInfo {
            description = "Adapter that gives the Core password hasher interface a salted, slow password hash and its verification, using the password hasher of the ASP.NET Core shared framework.";
            sourceFiles = ("src/DemaConsulting.ReviewCenter/Identity/PasswordHasher.cs");
            testFiles = ("test/DemaConsulting.ReviewCenter.Tests/Identity/PasswordHasherTests.cs");
            designFiles = ("docs/design/review-center/identity/password-hasher.md");
            verificationFiles = ("docs/verification/review-center/identity/password-hasher.md");
            requirementFiles = ("docs/reqstream/review-center/identity/password-hasher.yaml");
        }
    }
}
