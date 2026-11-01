package ReviewCenter {
    part def ReviewCenterDbContext {
        doc /* Entity Framework Core model that maps users (with the external issuer and subject columns and a unique index over the pair), projects (with the seven boolean columns of their evidence settings and the three boolean columns of the review rules: independent reviewers, non-author checklist answers, and non-reopener signing, all on by default), project memberships, reviews, review files, threads (with their kind), comments (with their kind and the report flag), and checklist snapshots, section states, and answers, reviewer slots and signatures, the reopenings of reviews (a table that belongs to the review), git repositories, and personal access tokens (with the creator of a review) and the enrollments of the second factor with their recovery codes (a table each, with a unique index over the user and the hash of a code), the read state of the inbox (a row for each user and review, removed with either), and the indexes on the reviewer slots and the creator of a review that the inbox uses, and the optional review conclusion text of a review (at most 4000 characters), to database tables, keys, and relationships. */

        comment sourceRef /* Source: src/DemaConsulting.ReviewCenter/Storage/ReviewCenterDbContext.cs */
        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Tests/Storage/ReviewStoreTests.cs, ReviewStoreRepositoryTests.cs, ReviewStoreMembershipTests.cs, EvidenceSettingsStoreTests.cs, ProjectChecklistStoreTests.cs, ReviewDueDateStoreTests.cs, ReviewFollowUpStoreTests.cs, ReviewStoreRecordContentTests.cs, ReviewStoreReviewConclusionTextTests.cs, ReviewStoreBallotTests.cs, ReviewStoreCheckThreadTests.cs, RepositoryStoreTests.cs, and DatabaseSetupTests.cs */
        comment designRef /* Design: docs/design/review-center/storage/review-center-db-context.md */
        comment verificationRef /* Verification: docs/verification/review-center/storage/review-center-db-context.md */
        comment reqRef /* Requirements: docs/reqstream/review-center/storage/review-center-db-context.yaml */
    }
}
