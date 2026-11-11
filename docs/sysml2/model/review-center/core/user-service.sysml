package ReviewCenter {
    private import SoftwareItemMetadata::*;

    part def UserService {
        doc /* Creates users and checks their sign-in: enforces the user name, display name, and password rules, keeps only a password hash, authenticates with a lockout after repeated failures (also for the re-authentication of a signature, under its own audit action), records failed attempts including those on unknown and locked accounts, enables and disables users, sets the system administrator right, ends the lockout of a user on request of a system administrator (audited), tells whether a user is locked, stores the time of the last successful sign-in (password, completed second factor, or provider), changes passwords (a temporary password must be replaced and cannot confirm a signature; a password that an administrator sets for another user is temporary), and protects the last enabled system administrator. Signs in an account of an OpenID Connect provider matched only by issuer and subject (the first sign-in creates an enabled account without rights and without a password, with a sanitized display name and a unique safe user name; a disabled account is refused), and refuses every password operation for such an account. Checks the password step of an account with a second factor without opening a session (a correct password does not clear the counter of failures, which wrong codes also count in), and lets the older sign-in paths fail closed for such an account, also the password-only change of the own password, which needs a fresh code from `TotpService` instead. Also defines the user (with its must-change-password flag, last sign-in time, and external issuer and subject), actor, hasher, and user store types of the Core subsystem. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.ReviewCenter.Core/UserService.cs", "src/DemaConsulting.ReviewCenter.Core/User.cs", "src/DemaConsulting.ReviewCenter.Core/Actor.cs", "src/DemaConsulting.ReviewCenter.Core/IPasswordHasher.cs", "src/DemaConsulting.ReviewCenter.Core/IUserStore.cs");
            testFiles = ("test/DemaConsulting.ReviewCenter.Tests/Core/UserServiceTests.cs", "test/DemaConsulting.ReviewCenter.Tests/Core/MustChangePasswordTests.cs", "test/DemaConsulting.ReviewCenter.Tests/Core/UserServiceExternalTests.cs");
            designFiles = ("docs/design/review-center/core/user-service.md");
            verificationFiles = ("docs/verification/review-center/core/user-service.md");
            requirementFiles = ("docs/reqstream/review-center/core/user-service.yaml");
        }
    }
}
