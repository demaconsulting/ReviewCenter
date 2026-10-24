package ReviewCenter {
    part def ThreadService {
        doc /* Starts comment threads on a file of a review, appends replies, resolves and reopens threads, enforces the comment rules, and reads threads back. Also derives what a reader needs to navigate the discussion: a thread's last activity, the thread index (rows across all files, filtered by state and ordered by activity), and the one-sentence next step, which also names the unanswered checklist checks. */

        comment sourceRef /* Source: src/DemaConsulting.ReviewCenter.Core/ThreadService.cs, ReviewThread.cs, ReviewComment.cs, ReviewThreadState.cs, and FileText.cs */
        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Tests/Core/ThreadServiceTests.cs */
        comment designRef /* Design: docs/design/review-center/core/thread-service.md */
        comment verificationRef /* Verification: docs/verification/review-center/core/thread-service.md */
        comment reqRef /* Requirements: docs/reqstream/review-center/core/thread-service.yaml */
    }
}
