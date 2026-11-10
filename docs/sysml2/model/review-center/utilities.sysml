package ReviewCenter {
    private import SoftwareItemMetadata::*;

    part def UtilitiesSubsystem {
        doc /* Shared, independently testable helper functions used by other subsystems. */

        metadata info : SoftwareInfo {
            description = "Shared, independently testable helper functions used by other subsystems.";
            testFiles = ("test/DemaConsulting.ReviewCenter.Tests/Utilities/UtilitiesSubsystemTests.cs");
            designFiles = ("docs/design/review-center/utilities.md");
            verificationFiles = ("docs/verification/review-center/utilities.md");
            requirementFiles = ("docs/reqstream/review-center/utilities.yaml");
        }

        part pathHelpers : PathHelpers;
        part temporaryDirectory : TemporaryDirectory;
        part secretSource : SecretSource;
    }
}
