package ReviewCenter {
    part def CoreSubsystem {
        doc /* Domain model and rules for users, projects, project memberships and access, the evidence settings of projects (anonymous access to the evidence and the content of the attestation PDFs), reviews, review comment threads, the review checklist, and reviewer slots with re-authenticated signatures (by password, or by a single-use proof of a sign-in at an identity provider; with a fresh code of the second factor when the signer has one), including the review content fingerprint, the record digest, the hash-chained audit trail, the registry of git repositories of a project, file selection patterns, revision name validation, the planning of reviews from review-sets, the personal access tokens of users, the line comparison of a file against its baseline, and the inbox of a user with the read state of comments, independent of web and database technology. */

        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Tests/Core/CoreSubsystemTests.cs */
        comment designRef /* Design: docs/design/review-center/core.md */
        comment verificationRef /* Verification: docs/verification/review-center/core.md */
        comment reqRef /* Requirements: docs/reqstream/review-center/core.yaml */

        part fingerprint : Fingerprint;
        part recordDigest : RecordDigest;
        part projectService : ProjectService;
        part projectAccess : ProjectAccess;
        part evidenceSettingsService : EvidenceSettingsService;
        part projectChecklistService : ProjectChecklistService;
        part reviewProgressService : ReviewProgressService;
        part reviewService : ReviewService;
        part threadService : ThreadService;
        part checklistService : ChecklistService;
        part userService : UserService;
        part signingService : SigningService;
        part reauthProofStore : ReauthProofStore;
        part totpService : TotpService;
        part totpChallengeStore : TotpChallengeStore;
        part auditChain : AuditChain;
        part auditService : AuditService;
        part selectionMatcher : SelectionMatcher;
        part revisionValidator : RevisionValidator;
        part repositoryService : RepositoryService;
        part reviewPlanner : ReviewPlanner;
        part lineDiff : LineDiff;
        part personalAccessTokenService : PersonalAccessTokenService;
        part inboxService : InboxService;
    }
}
