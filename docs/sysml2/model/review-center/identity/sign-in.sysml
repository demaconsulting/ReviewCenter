package ReviewCenter {
    part def SignIn {
        doc /* Cookie authentication for the web application: the session cookie and its settings, the login and logout endpoints with antiforgery protection, the call of the sign-in throttle before the password is checked and the generic throttled answer, the audit entries of refused attempts, the local-only return address, the per-request check that the user still exists, is enabled, and has the same system administrator state as the role claim of the cookie and the same must-change-password state as the claim of the cookie (a rule shared with the re-check of open pages), the redirect of a user with a temporary password to the account page and the claim that replaces the administrator role for that user, the redirect of anonymous requests to the login page and the refusal of users who are not system administrators on the administration pages, the registration of the revalidating authentication state provider and the re-check setting, and the conversion between the signed-in principal and the Core actor, which refuses with a message when nobody is signed in. */

        comment sourceRef /* Source: src/DemaConsulting.ReviewCenter/Identity/SignIn.cs and AuthOptions.cs */
        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Tests/Identity/SignInTests.cs */
        comment designRef /* Design: docs/design/review-center/identity/sign-in.md */
        comment verificationRef /* Verification: docs/verification/review-center/identity/sign-in.md */
        comment reqRef /* Requirements: docs/reqstream/review-center/identity/sign-in.yaml */
    }
}
