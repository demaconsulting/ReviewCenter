package ReviewCenter {
    part def CoreSubsystem {
        doc /* Domain model and rules for users, projects, reviews, review comment threads, the review checklist, and reviewer slots with re-authenticated signatures, including the review content fingerprint and the record digest, independent of web and database technology. */

        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Tests/Core/CoreSubsystemTests.cs */
        comment designRef /* Design: docs/design/review-center/core.md */
        comment verificationRef /* Verification: docs/verification/review-center/core.md */
        comment reqRef /* Requirements: docs/reqstream/review-center/core.yaml */

        part fingerprint : Fingerprint;
        part recordDigest : RecordDigest;
        part projectService : ProjectService;
        part reviewService : ReviewService;
        part threadService : ThreadService;
        part checklistService : ChecklistService;
        part userService : UserService;
        part signingService : SigningService;
    }
}
