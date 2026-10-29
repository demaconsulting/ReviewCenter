package ReviewCenter {
    part def ValidationChecks {
        doc /* The hermetic self-validation checks, from git to the web host, plus the opt-in configured-database check. */

        comment sourceRef /* Source: src/DemaConsulting.ReviewCenter/SelfTest/Checks/ */
        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Tests/SelfTest/ValidationChecksTests.cs */
        comment designRef /* Design: docs/design/review-center/self-test/validation-checks.md */
        comment verificationRef /* Verification: docs/verification/review-center/self-test/validation-checks.md */
        comment reqRef /* Requirements: docs/reqstream/review-center/self-test/validation-checks.yaml */

        part temporaryDirectory : TemporaryDirectory;
    }
}
