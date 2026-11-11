package ReviewCenter {
    private import SoftwareItemMetadata::*;

    part def LoginPage {
        doc /* Static page that asks for a user name and password and posts them to the login endpoint, shows one generic message when sign-in failed and a different message when the sign-in throttle refused the attempt, and keeps a local return address, shows a "Sign in with" button for the OpenID Connect provider in a form of its own beside the local form only while the provider is enabled (with one general message when the provider failed), and shows a notice to sign in again with the new password after a forced password change (`changed=1`) and a notice that the sign-in with a code has ended after a wait that was too long (`error=totp-expired`). */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.ReviewCenter/Web/LoginPage.razor", "src/DemaConsulting.ReviewCenter/Web/LoginPage.razor.cs");
            testFiles = ("test/DemaConsulting.ReviewCenter.Tests/Identity/SignInTests.cs");
            designFiles = ("docs/design/review-center/web/login-page.md");
            verificationFiles = ("docs/verification/review-center/web/login-page.md");
            requirementFiles = ("docs/reqstream/review-center/web/login-page.yaml");
        }
    }
}
