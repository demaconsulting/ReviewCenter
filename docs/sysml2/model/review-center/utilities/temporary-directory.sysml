package ReviewCenter {
    part def TemporaryDirectory {
        doc /* Uniquely named temporary directory that is deleted, with its contents, when disposed; a locked file is retried a bounded number of times (5 attempts, about 75 ms apart) and a failure to delete never throws. */

        comment sourceRef /* Source: src/DemaConsulting.ReviewCenter/Utilities/TemporaryDirectory.cs */
        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Tests/Utilities/TemporaryDirectoryTests.cs */
        comment designRef /* Design: docs/design/review-center/utilities/temporary-directory.md */
        comment verificationRef /* Verification: docs/verification/review-center/utilities/temporary-directory.md */
        comment reqRef /* Requirements: docs/reqstream/review-center/utilities/temporary-directory.yaml */
    }
}
