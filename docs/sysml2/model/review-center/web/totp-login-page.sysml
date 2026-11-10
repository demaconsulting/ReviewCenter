package ReviewCenter {
    private import SoftwareItemMetadata::*;

    part def TotpLoginPage {
        doc /* The second step of the sign-in at /login/totp: a form with one code field that posts to the endpoint of the second step, a general message for a wrong code and a different one for too many attempts, no user name, and a redirect to the login page with a notice when there is no valid pending sign-in for the client. */

        metadata info : SoftwareInfo {
            description = "The second step of the sign-in at /login/totp: a form with one code field that posts to the endpoint of the second step, a general message for a wrong code and a different one for too many attempts, no user name, and a redirect to the login page with a notice when there is no valid pending sign-in for the client.";
            sourceFiles = ("src/DemaConsulting.ReviewCenter/Web/TotpLoginPage.razor", "src/DemaConsulting.ReviewCenter/Web/TotpLoginPage.razor.cs");
            testFiles = ("test/DemaConsulting.ReviewCenter.Tests/Web/TotpLoginPageTests.cs");
            designFiles = ("docs/design/review-center/web/totp-login-page.md");
            verificationFiles = ("docs/verification/review-center/web/totp-login-page.md");
            requirementFiles = ("docs/reqstream/review-center/web/totp-login-page.yaml");
        }
    }
}
