package ReviewCenter {
    part def TotpSignIn {
        doc /* The second step of the sign-in of an account with a second factor and its settings (`Identity:Totp`, on by default): the pending-sign-in cookie that is bound to the browser, the endpoint that takes the code or a recovery code only with an antiforgery token, the shared sign-in throttle and lockout, the same answer for every kind of failure, and the endpoint that is mapped only while the feature is on. */

        comment sourceRef /* Source: src/DemaConsulting.ReviewCenter/Identity/TotpSignIn.cs and TotpOptions.cs */
        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Tests/Identity/TotpSignInTests.cs and TotpOptionsTests.cs */
        comment designRef /* Design: docs/design/review-center/identity/totp-sign-in.md */
        comment verificationRef /* Verification: docs/verification/review-center/identity/totp-sign-in.md */
        comment reqRef /* Requirements: docs/reqstream/review-center/identity/totp-sign-in.yaml */
    }
}
