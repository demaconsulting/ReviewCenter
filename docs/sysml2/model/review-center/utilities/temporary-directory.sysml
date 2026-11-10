package ReviewCenter {
    private import SoftwareItemMetadata::*;

    part def TemporaryDirectory {
        doc /* Uniquely named temporary directory that is deleted, with its contents, when disposed; a locked file is retried a bounded number of times (5 attempts, about 75 ms apart) and a failure to delete never throws. */

        metadata info : SoftwareInfo {
            description = "Uniquely named temporary directory that is deleted, with its contents, when disposed; a locked file is retried a bounded number of times (5 attempts, about 75 ms apart) and a failure to delete never throws.";
            sourceFiles = ("src/DemaConsulting.ReviewCenter/Utilities/TemporaryDirectory.cs");
            testFiles = ("test/DemaConsulting.ReviewCenter.Tests/Utilities/TemporaryDirectoryTests.cs");
            designFiles = ("docs/design/review-center/utilities/temporary-directory.md");
            verificationFiles = ("docs/verification/review-center/utilities/temporary-directory.md");
            requirementFiles = ("docs/reqstream/review-center/utilities/temporary-directory.yaml");
        }
    }
}
