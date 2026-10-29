package ReviewCenter {
    part def SelfTestSubsystem {
        doc /* Self-validation framework exercising the tool's own capabilities on demand. */

        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Tests/SelfTest/SelfTestSubsystemTests.cs */
        comment designRef /* Design: docs/design/review-center/self-test.md */
        comment verificationRef /* Verification: docs/verification/review-center/self-test.md */
        comment reqRef /* Requirements: docs/reqstream/review-center/self-test.yaml */

        part validation : Validation;
        part validationChecks : ValidationChecks;
    }
}
