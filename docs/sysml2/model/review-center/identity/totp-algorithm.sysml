package ReviewCenter {
    private import SoftwareItemMetadata::*;

    part def TotpAlgorithm {
        doc /* The one-time codes of the second factor by RFC 6238 (SHA-1, six digits, thirty second steps): creates a 160-bit secret, reads and writes it as base32 text, builds the otpauth address for an authenticator app, computes the code of a step, and checks a typed code within one step either side, reporting the step that matched. The only unit that uses the Otp.NET library. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.ReviewCenter/Identity/TotpAlgorithm.cs");
            testFiles = ("test/DemaConsulting.ReviewCenter.Tests/Identity/TotpAlgorithmTests.cs");
            designFiles = ("docs/design/review-center/identity/totp-algorithm.md");
            verificationFiles = ("docs/verification/review-center/identity/totp-algorithm.md");
            requirementFiles = ("docs/reqstream/review-center/identity/totp-algorithm.yaml");
        }
    }
}
