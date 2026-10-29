package ReviewCenter {
    part def TotpLoginPage {
        doc /* The second step of the sign-in at /login/totp: a form with one code field that posts to the endpoint of the second step, a general message for a wrong code and a different one for too many attempts, no user name, and a redirect to the login page with a notice when there is no valid pending sign-in for the client. */

        comment sourceRef /* Source: src/DemaConsulting.ReviewCenter/Web/TotpLoginPage.razor and TotpLoginPage.razor.cs */
        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Tests/Web/TotpLoginPageTests.cs */
        comment designRef /* Design: docs/design/review-center/web/totp-login-page.md */
        comment verificationRef /* Verification: docs/verification/review-center/web/totp-login-page.md */
        comment reqRef /* Requirements: docs/reqstream/review-center/web/totp-login-page.yaml */
    }
}
