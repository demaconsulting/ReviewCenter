package ReviewCenter {
    part def StorageSubsystem {
        doc /* Persistence of users, projects, reviews, review comment threads, and review checklists, reviewer slots, signatures, the registry of git repositories, and the hash-chained audit trail in a relational database through Entity Framework Core, with SQLite as the supported provider. */

        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Tests/Storage/StorageSubsystemTests.cs */
        comment designRef /* Design: docs/design/review-center/storage.md */
        comment verificationRef /* Verification: docs/verification/review-center/storage.md */
        comment reqRef /* Requirements: docs/reqstream/review-center/storage.yaml */

        part dbContext : ReviewCenterDbContext;
        part reviewStore : ReviewStore;
        part userStore : UserStore;
        part auditStore : AuditStore;
        part repositoryStore : RepositoryStore;
        part databaseSetup : DatabaseSetup;
    }
}
