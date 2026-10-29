package ReviewCenter {
    part def TotpService {
        doc /* The rules of the optional second factor of local accounts: set up with the password and confirm with a code, ten recovery codes, the second step of the sign-in, the fresh code of a signature, one use for each code, wrong codes that count toward the lockout of the passwords, removal by the owner, change of the own password with a fresh code, and reset by a system administrator, with every change in the audit trail and no secret or code in any record or message. */

        comment sourceRef /* Source: src/DemaConsulting.ReviewCenter.Core/TotpService.cs, ITotpAlgorithm.cs, ITotpSecretProtector.cs, ITotpStore.cs, TotpSettings.cs, UserTotp.cs and TotpRecoveryCode.cs */
        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Tests/Core/TotpServiceTests.cs and UserServiceTotpTests.cs */
        comment designRef /* Design: docs/design/review-center/core/totp-service.md */
        comment verificationRef /* Verification: docs/verification/review-center/core/totp-service.md */
        comment reqRef /* Requirements: docs/reqstream/review-center/core/totp-service.yaml */
    }
}
