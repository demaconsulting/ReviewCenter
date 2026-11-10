package ReviewCenter {
    private import SoftwareItemMetadata::*;

    part def ValidationChecks {
        doc /* The hermetic self-validation checks, from git to the web host (including the one-time codes of the second factor against the published test vectors, and a notification mail that is composed from a fixture without ballot content and written as a file by the file sender), plus the opt-in configured-database check, the opt-in configured-keys check (the key ring and the protected secrets of the database, and a description of the attestation signing key that creates nothing: no key, folder, or file), and the opt-in configured-notifications check (the e-mail notification settings, reported without a secret and without a network connection). */

        metadata info : SoftwareInfo {
            description = "The hermetic self-validation checks, from git to the web host (including the one-time codes of the second factor against the published test vectors, and a notification mail that is composed from a fixture without ballot content and written as a file by the file sender), plus the opt-in configured-database check, the opt-in configured-keys check (the key ring and the protected secrets of the database, and a description of the attestation signing key that creates nothing: no key, folder, or file), and the opt-in configured-notifications check (the e-mail notification settings, reported without a secret and without a network connection).";
            sourceFiles = ("src/DemaConsulting.ReviewCenter/SelfTest/Checks/AttestationPdfCheck.cs", "src/DemaConsulting.ReviewCenter/SelfTest/Checks/AuditChainCheck.cs", "src/DemaConsulting.ReviewCenter/SelfTest/Checks/ConfiguredDatabaseCheck.cs", "src/DemaConsulting.ReviewCenter/SelfTest/Checks/ConfiguredKeysCheck.cs", "src/DemaConsulting.ReviewCenter/SelfTest/Checks/ConfiguredNotificationsCheck.cs", "src/DemaConsulting.ReviewCenter/SelfTest/Checks/FastCommitInterceptor.cs", "src/DemaConsulting.ReviewCenter/SelfTest/Checks/FingerprintCheck.cs", "src/DemaConsulting.ReviewCenter/SelfTest/Checks/GitAvailableCheck.cs", "src/DemaConsulting.ReviewCenter/SelfTest/Checks/GitRoundTripCheck.cs", "src/DemaConsulting.ReviewCenter/SelfTest/Checks/HostStartCheck.cs", "src/DemaConsulting.ReviewCenter/SelfTest/Checks/NotificationMailCheck.cs", "src/DemaConsulting.ReviewCenter/SelfTest/Checks/PasswordHashCheck.cs", "src/DemaConsulting.ReviewCenter/SelfTest/Checks/ReviewWorkflowCheck.cs", "src/DemaConsulting.ReviewCenter/SelfTest/Checks/SelfTestGit.cs", "src/DemaConsulting.ReviewCenter/SelfTest/Checks/SelfTestServices.cs", "src/DemaConsulting.ReviewCenter/SelfTest/Checks/TotpCheck.cs");
            testFiles = ("test/DemaConsulting.ReviewCenter.Tests/SelfTest/ValidationChecksTests.cs");
            designFiles = ("docs/design/review-center/self-test/validation-checks.md");
            verificationFiles = ("docs/verification/review-center/self-test/validation-checks.md");
            requirementFiles = ("docs/reqstream/review-center/self-test/validation-checks.yaml");
        }

        part temporaryDirectory : TemporaryDirectory;
    }
}
