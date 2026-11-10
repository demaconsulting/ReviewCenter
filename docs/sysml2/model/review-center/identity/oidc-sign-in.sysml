package ReviewCenter {
    private import SoftwareItemMetadata::*;

    part def OidcSignIn {
        doc /* Sign-in with a generic OpenID Connect provider and confirmation of identity for a signature: settings under Identity:Oidc (the client secret inline or, through the shared SecretSource, in a file named by ClientSecretFile) with start-up validation that names settings and never values, the authorization code flow with PKCE through the framework handler, the endpoints for the login challenge, the callback hand-over and the re-authentication challenge (prompt=login, max_age=0), account matching by issuer and subject only through UserService, the auth_time, subject and issuer checks that fail closed, a single-use proof in ReauthProofStore, a throttle per client address, audit records with the reason class only, local return addresses only, local-only sign-out, and no token, claim or secret in the logging or audit records of this unit (the log of the framework handler, which the unit does not control, may name the issuer and audience of an identity token it refused). */

        metadata info : SoftwareInfo {
            description = "Sign-in with a generic OpenID Connect provider and confirmation of identity for a signature: settings under Identity:Oidc (the client secret inline or, through the shared SecretSource, in a file named by ClientSecretFile) with start-up validation that names settings and never values, the authorization code flow with PKCE through the framework handler, the endpoints for the login challenge, the callback hand-over and the re-authentication challenge (prompt=login, max_age=0), account matching by issuer and subject only through UserService, the auth_time, subject and issuer checks that fail closed, a single-use proof in ReauthProofStore, a throttle per client address, audit records with the reason class only, local return addresses only, local-only sign-out, and no token, claim or secret in the logging or audit records of this unit (the log of the framework handler, which the unit does not control, may name the issuer and audience of an identity token it refused).";
            sourceFiles = ("src/DemaConsulting.ReviewCenter/Identity/OidcSignIn.cs", "src/DemaConsulting.ReviewCenter/Identity/OidcOptions.cs");
            testFiles = ("test/DemaConsulting.ReviewCenter.Tests/Identity/OidcSignInTests.cs", "test/DemaConsulting.ReviewCenter.Tests/Identity/OidcReauthTests.cs", "test/DemaConsulting.ReviewCenter.Tests/Identity/OidcOptionsTests.cs");
            designFiles = ("docs/design/review-center/identity/oidc-sign-in.md");
            verificationFiles = ("docs/verification/review-center/identity/oidc-sign-in.md");
            requirementFiles = ("docs/reqstream/review-center/identity/oidc-sign-in.yaml");
        }
    }
}
