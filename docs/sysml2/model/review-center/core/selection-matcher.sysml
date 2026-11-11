package ReviewCenter {
    private import SoftwareItemMetadata::*;

    part def SelectionMatcher {
        doc /* Parses include and exclude patterns for the selection of repository files and decides whether a path is selected, with a defined glob dialect. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.ReviewCenter.Core/SelectionMatcher.cs");
            testFiles = ("test/DemaConsulting.ReviewCenter.Tests/Core/SelectionMatcherTests.cs");
            designFiles = ("docs/design/review-center/core/selection-matcher.md");
            verificationFiles = ("docs/verification/review-center/core/selection-matcher.md");
            requirementFiles = ("docs/reqstream/review-center/core/selection-matcher.yaml");
        }
    }
}
