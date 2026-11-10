package ReviewCenter {
    private import SoftwareItemMetadata::*;

    part def Validation {
        doc /* Orchestrates the self-validation test suite and reports pass/fail results. Every check runs concurrently under a deadline (60 seconds) that the runner enforces itself, so a check that blocks or ignores its token is reported as timed out. */

        metadata info : SoftwareInfo {
            description = "Orchestrates the self-validation test suite and reports pass/fail results. Every check runs concurrently under a deadline (60 seconds) that the runner enforces itself, so a check that blocks or ignores its token is reported as timed out.";
            sourceFiles = ("src/DemaConsulting.ReviewCenter/SelfTest/Validation.cs");
            testFiles = ("test/DemaConsulting.ReviewCenter.Tests/SelfTest/ValidationTests.cs");
            designFiles = ("docs/design/review-center/self-test/validation.md");
            verificationFiles = ("docs/verification/review-center/self-test/validation.md");
            requirementFiles = ("docs/reqstream/review-center/self-test/validation.yaml");
        }
    }
}
