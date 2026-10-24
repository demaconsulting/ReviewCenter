package ReviewCenter {
    part def CoreSubsystem {
        doc /* Domain model and rules for projects, reviews, and review comment threads, including the review content fingerprint, independent of web and database technology. */

        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Tests/Core/CoreSubsystemTests.cs */
        comment designRef /* Design: docs/design/review-center/core.md */
        comment verificationRef /* Verification: docs/verification/review-center/core.md */
        comment reqRef /* Requirements: docs/reqstream/review-center/core.yaml */

        part fingerprint : Fingerprint;
        part projectService : ProjectService;
        part reviewService : ReviewService;
        part threadService : ThreadService;
    }
}
