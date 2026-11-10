package ReviewCenter {
    private import SoftwareItemMetadata::*;

    part def TotpSignIn {
        doc /* The second step of the sign-in of an account with a second factor and its settings (`Identity:Totp`, on by default): the pending-sign-in cookie that is bound to the browser, the endpoint that takes the code or a recovery code only with an antiforgery token, the shared sign-in throttle and lockout, the same answer for every kind of failure, and the endpoint that is mapped only while the feature is on. */

        metadata info : SoftwareInfo {
            description = "The second step of the sign-in of an account with a second factor and its settings (`Identity:Totp`, on by default): the pending-sign-in cookie that is bound to the browser, the endpoint that takes the code or a recovery code only with an antiforgery token, the shared sign-in throttle and lockout, the same answer for every kind of failure, and the endpoint that is mapped only while the feature is on.";
            sourceFiles = ("src/DemaConsulting.ReviewCenter/Identity/TotpSignIn.cs", "src/DemaConsulting.ReviewCenter/Identity/TotpOptions.cs");
            testFiles = ("test/DemaConsulting.ReviewCenter.Tests/Identity/TotpSignInTests.cs", "test/DemaConsulting.ReviewCenter.Tests/Identity/TotpOptionsTests.cs");
            designFiles = ("docs/design/review-center/identity/totp-sign-in.md");
            verificationFiles = ("docs/verification/review-center/identity/totp-sign-in.md");
            requirementFiles = ("docs/reqstream/review-center/identity/totp-sign-in.yaml");
        }
    }
}
