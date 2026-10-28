package ReviewCenter {
    part def UserService {
        doc /* Creates users and checks their sign-in: enforces the user name, display name, and password rules, keeps only a password hash, authenticates with a lockout after repeated failures (also for the re-authentication of a signature, under its own audit action), records failed attempts including those on unknown and locked accounts, enables and disables users, sets the system administrator right, changes passwords (a temporary password must be replaced and cannot confirm a signature), and protects the last enabled system administrator. Signs in an account of an OpenID Connect provider matched only by issuer and subject (the first sign-in creates an enabled account without rights and without a password, with a sanitized display name and a unique safe user name; a disabled account is refused), and refuses every password operation for such an account. Also defines the user (with its must-change-password flag and external issuer and subject), actor, hasher, and user store types of the Core subsystem. */

        comment sourceRef /* Source: src/DemaConsulting.ReviewCenter.Core/UserService.cs, User.cs, Actor.cs, IPasswordHasher.cs, and IUserStore.cs */
        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Tests/Core/UserServiceTests.cs, MustChangePasswordTests.cs, and UserServiceExternalTests.cs */
        comment designRef /* Design: docs/design/review-center/core/user-service.md */
        comment verificationRef /* Verification: docs/verification/review-center/core/user-service.md */
        comment reqRef /* Requirements: docs/reqstream/review-center/core/user-service.yaml */
    }
}
