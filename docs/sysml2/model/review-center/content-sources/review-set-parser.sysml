package ReviewCenter {
    private import SoftwareItemMetadata::*;

    part def ReviewSetParser {
        doc /* Reads the review-sets (identifier, title, and paths) from the bytes of a ReviewMark configuration file with a tolerant YAML reader that ignores every other key, refuses aliases, and enforces size and count limits with clear messages. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.ReviewCenter/ContentSources/ReviewMarkConfigParser.cs");
            testFiles = ("test/DemaConsulting.ReviewCenter.Tests/ContentSources/ReviewMarkConfigParserTests.cs");
            designFiles = ("docs/design/review-center/content-sources/review-set-parser.md");
            verificationFiles = ("docs/verification/review-center/content-sources/review-set-parser.md");
            requirementFiles = ("docs/reqstream/review-center/content-sources/review-set-parser.yaml");
        }
    }
}
