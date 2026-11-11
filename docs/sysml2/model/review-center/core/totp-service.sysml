package ReviewCenter {
    private import SoftwareItemMetadata::*;

    part def TotpService {
        doc /* The rules of the optional second factor of local accounts: set up with the password and confirm with a code, ten recovery codes, the second step of the sign-in, the fresh code of a signature, one use for each code, wrong codes that count toward the lockout of the passwords, removal by the owner, change of the own password with a fresh code, and reset by a system administrator, with every change in the audit trail and no secret or code in any record or message. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.ReviewCenter.Core/TotpService.cs", "src/DemaConsulting.ReviewCenter.Core/ITotpAlgorithm.cs", "src/DemaConsulting.ReviewCenter.Core/ITotpSecretProtector.cs", "src/DemaConsulting.ReviewCenter.Core/ITotpStore.cs", "src/DemaConsulting.ReviewCenter.Core/TotpSettings.cs", "src/DemaConsulting.ReviewCenter.Core/UserTotp.cs", "src/DemaConsulting.ReviewCenter.Core/TotpRecoveryCode.cs");
            testFiles = ("test/DemaConsulting.ReviewCenter.Tests/Core/TotpServiceTests.cs", "test/DemaConsulting.ReviewCenter.Tests/Core/UserServiceTotpTests.cs");
            designFiles = ("docs/design/review-center/core/totp-service.md");
            verificationFiles = ("docs/verification/review-center/core/totp-service.md");
            requirementFiles = ("docs/reqstream/review-center/core/totp-service.yaml");
        }
    }
}
