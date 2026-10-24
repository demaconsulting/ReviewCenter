package ReviewCenter {
    part def LoginPage {
        doc /* Static page that asks for a user name and password and posts them to the login endpoint, shows one generic message when sign-in failed, and keeps a local return address. */

        comment sourceRef /* Source: src/DemaConsulting.ReviewCenter/Web/LoginPage.razor and LoginPage.razor.cs */
        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Tests/Identity/SignInTests.cs (the page is a static form exercised through HTTP) */
        comment designRef /* Design: docs/design/review-center/web/login-page.md */
        comment verificationRef /* Verification: docs/verification/review-center/web/login-page.md */
        comment reqRef /* Requirements: docs/reqstream/review-center/web/login-page.yaml */
    }
}
