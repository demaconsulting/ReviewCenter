package ReviewCenter {
    private import SoftwareItemMetadata::*;

    part def PathHelpers {
        doc /* Safe path-combination utility guarding against path-traversal. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.ReviewCenter/Utilities/PathHelpers.cs");
            testFiles = ("test/DemaConsulting.ReviewCenter.Tests/Utilities/PathHelpersTests.cs");
            designFiles = ("docs/design/review-center/utilities/path-helpers.md");
            verificationFiles = ("docs/verification/review-center/utilities/path-helpers.md");
            requirementFiles = ("docs/reqstream/review-center/utilities/path-helpers.yaml");
        }
    }
}
