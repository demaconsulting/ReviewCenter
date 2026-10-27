package ReviewCenter {
    part def IdentitySubsystem {
        doc /* Local accounts and sign-in: password hashing, cookie authentication with a per-request check of the user, the login and logout endpoints, the sign-in throttle, a safe return address, the role-based authorization of pages, the periodic re-check of the user of every open page, and the signed-in user as the actor of review actions. */

        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Tests/Identity/IdentitySubsystemTests.cs */
        comment designRef /* Design: docs/design/review-center/identity.md */
        comment verificationRef /* Verification: docs/verification/review-center/identity.md */
        comment reqRef /* Requirements: docs/reqstream/review-center/identity.yaml */

        part passwordHasher : PasswordHasher;
        part signIn : SignIn;
        part signInThrottle : SignInThrottle;
        part revalidatingAuthenticationStateProvider : RevalidatingAuthenticationStateProvider;
    }
}
