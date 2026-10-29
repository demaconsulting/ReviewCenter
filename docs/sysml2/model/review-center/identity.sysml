package ReviewCenter {
    part def IdentitySubsystem {
        doc /* Local accounts and sign-in: password hashing, cookie authentication with a per-request check of the user, the login and logout endpoints, the sign-in throttle, sign-in with an OpenID Connect provider and the confirmation of identity for a signature through it, the optional second factor of local accounts (a code of an authenticator app after the password, with encrypted secrets and recovery codes), a safe return address, the system administrator role claim (issued only to system administrators) and the authorization of pages, the periodic re-check of the user of every open page, and the signed-in user as the actor of review actions. */

        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Tests/Identity/IdentitySubsystemTests.cs */
        comment designRef /* Design: docs/design/review-center/identity.md */
        comment verificationRef /* Verification: docs/verification/review-center/identity.md */
        comment reqRef /* Requirements: docs/reqstream/review-center/identity.yaml */

        part passwordHasher : PasswordHasher;
        part signIn : SignIn;
        part signInThrottle : SignInThrottle;
        part oidcSignIn : OidcSignIn;
        part totpAlgorithm : TotpAlgorithm;
        part totpSecretProtector : TotpSecretProtector;
        part totpSignIn : TotpSignIn;
        part revalidatingAuthenticationStateProvider : RevalidatingAuthenticationStateProvider;
    }
}
