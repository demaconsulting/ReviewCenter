package ReviewCenter {
    part def PersonalAccessTokenService {
        doc /* Owns the rules for personal access tokens: creates a token for a user with a name and an expiry of 30, 90, or 365 days within a limit of 20 active tokens and shows the value only once, keeps only the SHA-256 hash, lists and revokes the tokens of the owner, decides whether a presented value is valid (active, owner enabled, otherwise one generic failure), records the last use at most every five minutes, refuses to create, list, or revoke tokens (and refuses a presented token) while the owner has a temporary password, and records creation, revocation, and refused use in the audit trail without a token value. Also defines the token, token store, and result types of the Core subsystem. */

        comment sourceRef /* Source: src/DemaConsulting.ReviewCenter.Core/PersonalAccessTokenService.cs, PersonalAccessToken.cs, and IPersonalAccessTokenStore.cs */
        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Tests/Core/PersonalAccessTokenServiceTests.cs */
        comment designRef /* Design: docs/design/review-center/core/personal-access-token-service.md */
        comment verificationRef /* Verification: docs/verification/review-center/core/personal-access-token-service.md */
        comment reqRef /* Requirements: docs/reqstream/review-center/core/personal-access-token-service.yaml */
    }
}
