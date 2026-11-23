package ReviewCenter_UtilitiesSubsystem {
    public import ReviewCenter_PathHelpers::*;
    public import ReviewCenter_TemporaryDirectory::*;
    public import ReviewCenter_SecretSource::*;
    private import SoftwareItemMetadata::*;

    part def UtilitiesSubsystem {
        doc /* Shared, independently testable helper functions used by other subsystems. */

        metadata info : SoftwareInfo {
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
