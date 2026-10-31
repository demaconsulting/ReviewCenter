package ReviewCenter {
    part def ValidationChecks {
        doc /* The hermetic self-validation checks, from git to the web host (including the one-time codes of the second factor against the published test vectors), plus the opt-in configured-database check and the opt-in configured-keys check (the key ring and the protected secrets of the database, and a description of the attestation signing key that creates nothing: no key, folder, or file). */

        comment sourceRef /* Source: src/DemaConsulting.ReviewCenter/SelfTest/Checks/ */
        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Tests/SelfTest/ValidationChecksTests.cs */
        comment designRef /* Design: docs/design/review-center/self-test/validation-checks.md */
        comment verificationRef /* Verification: docs/verification/review-center/self-test/validation-checks.md */
        comment reqRef /* Requirements: docs/reqstream/review-center/self-test/validation-checks.yaml */

        part temporaryDirectory : TemporaryDirectory;
    }
}
