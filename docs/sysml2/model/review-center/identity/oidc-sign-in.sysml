package ReviewCenter {
    part def OidcSignIn {
        doc /* Sign-in with a generic OpenID Connect provider and confirmation of identity for a signature: settings under Identity:Oidc with start-up validation that names settings and never values, the authorization code flow with PKCE through the framework handler, the endpoints for the login challenge, the callback hand-over and the re-authentication challenge (prompt=login, max_age=0), account matching by issuer and subject only through UserService, the auth_time, subject and issuer checks that fail closed, a single-use proof in ReauthProofStore, a throttle per client address, audit records with the reason class only, local return addresses only, local-only sign-out, and no token, claim or secret in any log. */

        comment sourceRef /* Source: src/DemaConsulting.ReviewCenter/Identity/OidcSignIn.cs and OidcOptions.cs */
        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Tests/Identity/OidcSignInTests.cs, OidcReauthTests.cs and OidcOptionsTests.cs */
        comment designRef /* Design: docs/design/review-center/identity/oidc-sign-in.md */
        comment verificationRef /* Verification: docs/verification/review-center/identity/oidc-sign-in.md */
        comment reqRef /* Requirements: docs/reqstream/review-center/identity/oidc-sign-in.yaml */
    }
}
