package ReviewCenter {
    part def ReviewCenterDbContext {
        doc /* Entity Framework Core model that maps users, projects, project memberships, reviews, review files, threads, comments, and checklist snapshots, section states, and answers, reviewer slots and signatures, git repositories, and personal access tokens (with the creator of a review) to database tables, keys, and relationships. */

        comment sourceRef /* Source: src/DemaConsulting.ReviewCenter/Storage/ReviewCenterDbContext.cs */
        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Tests/Storage/ReviewStoreTests.cs, ReviewStoreRepositoryTests.cs, ReviewStoreMembershipTests.cs, RepositoryStoreTests.cs, and DatabaseSetupTests.cs */
        comment designRef /* Design: docs/design/review-center/storage/review-center-db-context.md */
        comment verificationRef /* Verification: docs/verification/review-center/storage/review-center-db-context.md */
        comment reqRef /* Requirements: docs/reqstream/review-center/storage/review-center-db-context.yaml */
    }
}
