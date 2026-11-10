package ReviewCenter {
    private import SoftwareItemMetadata::*;

    part def RevisionValidator {
        doc /* Validates a git revision name before it is passed to git, refusing names that could be read as an option or that contain forbidden characters or exceed the length limit. */

        metadata info : SoftwareInfo {
            description = "Validates a git revision name before it is passed to git, refusing names that could be read as an option or that contain forbidden characters or exceed the length limit.";
            sourceFiles = ("src/DemaConsulting.ReviewCenter.Core/RevisionValidator.cs");
            testFiles = ("test/DemaConsulting.ReviewCenter.Tests/Core/RevisionValidatorTests.cs");
            designFiles = ("docs/design/review-center/core/revision-validator.md");
            verificationFiles = ("docs/verification/review-center/core/revision-validator.md");
            requirementFiles = ("docs/reqstream/review-center/core/revision-validator.yaml");
        }
    }
}
