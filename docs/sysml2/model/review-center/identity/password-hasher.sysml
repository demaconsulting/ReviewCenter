package ReviewCenter {
    part def PasswordHasher {
        doc /* Adapter that gives the Core password hasher interface a salted, slow password hash and its verification, using the password hasher of the ASP.NET Core shared framework. */

        comment sourceRef /* Source: src/DemaConsulting.ReviewCenter/Identity/PasswordHasher.cs */
        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Tests/Identity/PasswordHasherTests.cs */
        comment designRef /* Design: docs/design/review-center/identity/password-hasher.md */
        comment verificationRef /* Verification: docs/verification/review-center/identity/password-hasher.md */
        comment reqRef /* Requirements: docs/reqstream/review-center/identity/password-hasher.yaml */
    }
}
