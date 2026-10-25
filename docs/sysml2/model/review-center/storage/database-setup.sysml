package ReviewCenter {
    part def DatabaseSetup {
        doc /* Reads the database settings, selects the provider and connection, creates the database on first use, and resolves the data folder that holds the database, the git cache, and the key ring. */

        comment sourceRef /* Source: src/DemaConsulting.ReviewCenter/Storage/DatabaseSetup.cs and DatabaseOptions.cs */
        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Tests/Storage/DatabaseSetupTests.cs */
        comment designRef /* Design: docs/design/review-center/storage/database-setup.md */
        comment verificationRef /* Verification: docs/verification/review-center/storage/database-setup.md */
        comment reqRef /* Requirements: docs/reqstream/review-center/storage/database-setup.yaml */
    }
}
