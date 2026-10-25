package ReviewCenter {
    part def RevisionValidator {
        doc /* Validates a git revision name before it is passed to git, refusing names that could be read as an option or that contain forbidden characters or exceed the length limit. */

        comment sourceRef /* Source: src/DemaConsulting.ReviewCenter.Core/RevisionValidator.cs */
        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Tests/Core/RevisionValidatorTests.cs */
        comment designRef /* Design: docs/design/review-center/core/revision-validator.md */
        comment verificationRef /* Verification: docs/verification/review-center/core/revision-validator.md */
        comment reqRef /* Requirements: docs/reqstream/review-center/core/revision-validator.yaml */
    }
}
