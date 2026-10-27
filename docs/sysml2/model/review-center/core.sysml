package ReviewCenter {
    part def CoreSubsystem {
        doc /* Domain model and rules for users, projects, reviews, review comment threads, the review checklist, and reviewer slots with re-authenticated signatures, including the review content fingerprint, the record digest, the hash-chained audit trail, the registry of git repositories, file selection patterns, revision name validation, the planning of reviews from review-sets, the personal access tokens of users, and the line comparison of a file against its baseline, independent of web and database technology. */

        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Tests/Core/CoreSubsystemTests.cs */
        comment designRef /* Design: docs/design/review-center/core.md */
        comment verificationRef /* Verification: docs/verification/review-center/core.md */
        comment reqRef /* Requirements: docs/reqstream/review-center/core.yaml */

        part fingerprint : Fingerprint;
        part recordDigest : RecordDigest;
        part projectService : ProjectService;
        part reviewService : ReviewService;
        part threadService : ThreadService;
        part checklistService : ChecklistService;
        part userService : UserService;
        part signingService : SigningService;
        part auditChain : AuditChain;
        part auditService : AuditService;
        part selectionMatcher : SelectionMatcher;
        part revisionValidator : RevisionValidator;
        part repositoryService : RepositoryService;
        part reviewPlanner : ReviewPlanner;
        part lineDiff : LineDiff;
        part personalAccessTokenService : PersonalAccessTokenService;
    }
}
