package ReviewCenter {
    part def ReviewSetParser {
        doc /* Reads the review-sets (identifier, title, and paths) from the bytes of a ReviewMark configuration file with a tolerant YAML reader that ignores every other key, refuses aliases, and enforces size and count limits with clear messages. */

        comment sourceRef /* Source: src/DemaConsulting.ReviewCenter/ContentSources/ReviewMarkConfigParser.cs */
        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Tests/ContentSources/ReviewMarkConfigParserTests.cs */
        comment designRef /* Design: docs/design/review-center/content-sources/review-set-parser.md */
        comment verificationRef /* Verification: docs/verification/review-center/content-sources/review-set-parser.md */
        comment reqRef /* Requirements: docs/reqstream/review-center/content-sources/review-set-parser.yaml */
    }
}
