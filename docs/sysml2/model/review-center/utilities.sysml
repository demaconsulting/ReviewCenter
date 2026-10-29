package ReviewCenter {
    part def UtilitiesSubsystem {
        doc /* Shared, independently testable helper functions used by other subsystems. */

        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Tests/Utilities/UtilitiesSubsystemTests.cs */
        comment designRef /* Design: docs/design/review-center/utilities.md */
        comment verificationRef /* Verification: docs/verification/review-center/utilities.md */
        comment reqRef /* Requirements: docs/reqstream/review-center/utilities.yaml */

        part pathHelpers : PathHelpers;
        part temporaryDirectory : TemporaryDirectory;
    }
}
