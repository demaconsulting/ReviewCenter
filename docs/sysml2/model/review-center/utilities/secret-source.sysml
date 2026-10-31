package ReviewCenter {
    part def SecretSource {
        doc /* Resolves a secret that is given inline or as the path of a file: the inline value is returned unchanged, the file is read as UTF-8 text (at most 8192 bytes, never past one byte beyond) and trimmed, nothing is returned when neither is set, and both set, a missing, unreadable, empty, oversized, or non-UTF-8 file are refused with a message that names the settings and the file and never holds the secret. */

        comment sourceRef /* Source: src/DemaConsulting.ReviewCenter/Utilities/SecretSource.cs */
        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Tests/Utilities/SecretSourceTests.cs */
        comment designRef /* Design: docs/design/review-center/utilities/secret-source.md */
        comment verificationRef /* Verification: docs/verification/review-center/utilities/secret-source.md */
        comment reqRef /* Requirements: docs/reqstream/review-center/utilities/secret-source.yaml */
    }
}
