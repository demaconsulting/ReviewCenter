package ReviewCenter {
    part def ReviewCenterDbContext {
        doc /* Entity Framework Core model that maps users (with the external issuer and subject columns and a unique index over the pair), projects (with the seven boolean columns of their evidence settings and the boolean column of the rule that requires independent reviewers), project memberships, reviews, review files, threads, comments, and checklist snapshots, section states, and answers, reviewer slots and signatures, the reopenings of reviews (a table that belongs to the review), git repositories, and personal access tokens (with the creator of a review) and the enrollments of the second factor with their recovery codes (a table each, with a unique index over the user and the hash of a code) to database tables, keys, and relationships. */

        comment sourceRef /* Source: src/DemaConsulting.ReviewCenter/Storage/ReviewCenterDbContext.cs */
        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Tests/Storage/ReviewStoreTests.cs, ReviewStoreRepositoryTests.cs, ReviewStoreMembershipTests.cs, EvidenceSettingsStoreTests.cs, RepositoryStoreTests.cs, and DatabaseSetupTests.cs */
        comment designRef /* Design: docs/design/review-center/storage/review-center-db-context.md */
        comment verificationRef /* Verification: docs/verification/review-center/storage/review-center-db-context.md */
        comment reqRef /* Requirements: docs/reqstream/review-center/storage/review-center-db-context.yaml */
    }
}
