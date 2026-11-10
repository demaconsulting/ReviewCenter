package ReviewCenter {
    private import SoftwareItemMetadata::*;

    part def SecretSource {
        doc /* Resolves a secret that is given inline or as the path of a file: the inline value is returned unchanged, the file is read as UTF-8 text (at most 8192 bytes, never past one byte beyond) and trimmed, nothing is returned when neither is set, and both set, a missing, unreadable, empty, oversized, or non-UTF-8 file are refused with a message that names the settings and the file and never holds the secret. */

        metadata info : SoftwareInfo {
            description = "Resolves a secret that is given inline or as the path of a file: the inline value is returned unchanged, the file is read as UTF-8 text (at most 8192 bytes, never past one byte beyond) and trimmed, nothing is returned when neither is set, and both set, a missing, unreadable, empty, oversized, or non-UTF-8 file are refused with a message that names the settings and the file and never holds the secret.";
            sourceFiles = ("src/DemaConsulting.ReviewCenter/Utilities/SecretSource.cs");
            testFiles = ("test/DemaConsulting.ReviewCenter.Tests/Utilities/SecretSourceTests.cs");
            designFiles = ("docs/design/review-center/utilities/secret-source.md");
            verificationFiles = ("docs/verification/review-center/utilities/secret-source.md");
            requirementFiles = ("docs/reqstream/review-center/utilities/secret-source.yaml");
        }
    }
}
