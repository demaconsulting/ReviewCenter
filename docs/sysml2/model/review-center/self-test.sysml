package ReviewCenter {
    private import SoftwareItemMetadata::*;

    part def SelfTestSubsystem {
        doc /* Self-validation framework exercising the tool's own capabilities on demand. */

        metadata info : SoftwareInfo {
            description = "Self-validation framework exercising the tool's own capabilities on demand.";
            testFiles = ("test/DemaConsulting.ReviewCenter.Tests/SelfTest/SelfTestSubsystemTests.cs");
            designFiles = ("docs/design/review-center/self-test.md");
            verificationFiles = ("docs/verification/review-center/self-test.md");
            requirementFiles = ("docs/reqstream/review-center/self-test.yaml");
        }

        part validation : Validation;
        part validationChecks : ValidationChecks;
    }
}
