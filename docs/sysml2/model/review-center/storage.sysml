package ReviewCenter {
    part def StorageSubsystem {
        doc /* Persistence of users (with the system administrator flag and, for an account of an OpenID Connect provider, its issuer and subject, unique together), projects, project memberships, reviews, review comment threads, and review checklists, reviewer slots, signatures, the git repositories of the projects, personal access tokens (by hash only), the second factor of users (the encrypted secret and the hashes of the recovery codes), the read state of the inbox of users for reviews, and the hash-chained audit trail in a relational database through Entity Framework Core, with SQLite as the supported provider. */

        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Tests/Storage/StorageSubsystemTests.cs */
        comment designRef /* Design: docs/design/review-center/storage.md */
        comment verificationRef /* Verification: docs/verification/review-center/storage.md */
        comment reqRef /* Requirements: docs/reqstream/review-center/storage.yaml */

        part dbContext : ReviewCenterDbContext;
        part reviewStore : ReviewStore;
        part userStore : UserStore;
        part auditStore : AuditStore;
        part repositoryStore : RepositoryStore;
        part personalAccessTokenStore : PersonalAccessTokenStore;
        part totpStore : TotpStore;
        part inboxStore : InboxStore;
        part databaseSetup : DatabaseSetup;
    }
}
