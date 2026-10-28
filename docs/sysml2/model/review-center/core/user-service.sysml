package ReviewCenter {
    part def UserService {
        doc /* Creates users and checks their sign-in: enforces the user name, display name, and password rules, keeps only a password hash, authenticates with a lockout after repeated failures (also for the re-authentication of a signature, under its own audit action), records failed attempts including those on unknown and locked accounts, enables and disables users, sets the system administrator right, changes passwords, and protects the last enabled system administrator. Also defines the user, actor, hasher, and user store types of the Core subsystem. */

        comment sourceRef /* Source: src/DemaConsulting.ReviewCenter.Core/UserService.cs, User.cs, Actor.cs, IPasswordHasher.cs, and IUserStore.cs */
        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Tests/Core/UserServiceTests.cs */
        comment designRef /* Design: docs/design/review-center/core/user-service.md */
        comment verificationRef /* Verification: docs/verification/review-center/core/user-service.md */
        comment reqRef /* Requirements: docs/reqstream/review-center/core/user-service.yaml */
    }
}
