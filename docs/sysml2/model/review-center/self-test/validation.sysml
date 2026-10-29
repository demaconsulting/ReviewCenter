package ReviewCenter {
    part def Validation {
        doc /* Orchestrates the self-validation test suite and reports pass/fail results. Every check runs concurrently under a deadline (60 seconds) that the runner enforces itself, so a check that blocks or ignores its token is reported as timed out. */

        comment sourceRef /* Source: src/DemaConsulting.ReviewCenter/SelfTest/Validation.cs */
        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Tests/SelfTest/ValidationTests.cs */
        comment designRef /* Design: docs/design/review-center/self-test/validation.md */
        comment verificationRef /* Verification: docs/verification/review-center/self-test/validation.md */
        comment reqRef /* Requirements: docs/reqstream/review-center/self-test/validation.yaml */
    }
}
