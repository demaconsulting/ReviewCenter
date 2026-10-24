package ReviewCenter {
    part def StorageSubsystem {
        doc /* Persistence of projects, reviews, and review comment threads in a relational database through Entity Framework Core, with SQLite as the supported provider. */

        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Tests/Storage/StorageSubsystemTests.cs */
        comment designRef /* Design: docs/design/review-center/storage.md */
        comment verificationRef /* Verification: docs/verification/review-center/storage.md */
        comment reqRef /* Requirements: docs/reqstream/review-center/storage.yaml */

        part dbContext : ReviewCenterDbContext;
        part reviewStore : ReviewStore;
        part databaseSetup : DatabaseSetup;
    }
}
