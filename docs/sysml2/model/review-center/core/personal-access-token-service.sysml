package ReviewCenter {
    private import SoftwareItemMetadata::*;

    part def PersonalAccessTokenService {
        doc /* Owns the rules for personal access tokens: creates a token for a user with a name and an expiry of 30, 90, or 365 days within a limit of 20 active tokens and shows the value only once, keeps only the SHA-256 hash, lists and revokes the tokens of the owner, decides whether a presented value is valid (active, owner enabled, otherwise one generic failure), records the last use at most every five minutes, refuses to create, list, or revoke tokens (and refuses a presented token) while the owner has a temporary password, and records creation, revocation, and refused use in the audit trail without a token value. Also defines the token, token store, and result types of the Core subsystem. */

        metadata info : SoftwareInfo {
            description = "Owns the rules for personal access tokens: creates a token for a user with a name and an expiry of 30, 90, or 365 days within a limit of 20 active tokens and shows the value only once, keeps only the SHA-256 hash, lists and revokes the tokens of the owner, decides whether a presented value is valid (active, owner enabled, otherwise one generic failure), records the last use at most every five minutes, refuses to create, list, or revoke tokens (and refuses a presented token) while the owner has a temporary password, and records creation, revocation, and refused use in the audit trail without a token value. Also defines the token, token store, and result types of the Core subsystem.";
            sourceFiles = ("src/DemaConsulting.ReviewCenter.Core/PersonalAccessTokenService.cs", "src/DemaConsulting.ReviewCenter.Core/PersonalAccessToken.cs", "src/DemaConsulting.ReviewCenter.Core/IPersonalAccessTokenStore.cs");
            testFiles = ("test/DemaConsulting.ReviewCenter.Tests/Core/PersonalAccessTokenServiceTests.cs");
            designFiles = ("docs/design/review-center/core/personal-access-token-service.md");
            verificationFiles = ("docs/verification/review-center/core/personal-access-token-service.md");
            requirementFiles = ("docs/reqstream/review-center/core/personal-access-token-service.yaml");
        }
    }
}
