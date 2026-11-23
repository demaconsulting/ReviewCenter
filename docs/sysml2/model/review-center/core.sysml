package ReviewCenter_CoreSubsystem {
    public import ReviewCenter_Fingerprint::*;
    public import ReviewCenter_RecordDigest::*;
    public import ReviewCenter_ProjectService::*;
    public import ReviewCenter_ProjectAccess::*;
    public import ReviewCenter_EvidenceSettingsService::*;
    public import ReviewCenter_ProjectChecklistService::*;
    public import ReviewCenter_ReviewProgressService::*;
    public import ReviewCenter_ReviewService::*;
    public import ReviewCenter_ThreadService::*;
    public import ReviewCenter_ChecklistService::*;
    public import ReviewCenter_UserService::*;
    public import ReviewCenter_SigningService::*;
    public import ReviewCenter_ReauthProofStore::*;
    public import ReviewCenter_TotpService::*;
    public import ReviewCenter_TotpChallengeStore::*;
    public import ReviewCenter_AuditChain::*;
    public import ReviewCenter_AuditService::*;
    public import ReviewCenter_SelectionMatcher::*;
    public import ReviewCenter_RevisionValidator::*;
    public import ReviewCenter_RepositoryService::*;
    public import ReviewCenter_ReviewPlanner::*;
    public import ReviewCenter_LineDiff::*;
    public import ReviewCenter_PersonalAccessTokenService::*;
    public import ReviewCenter_InboxService::*;
    public import ReviewCenter_UserImpactService::*;
    public import ReviewCenter_BallotConsolidation::*;
    public import ReviewCenter_BallotVisibility::*;
    public import ReviewCenter_ReviewStatusService::*;
    public import ReviewCenter_NotificationAddress::*;
    public import ReviewCenter_NotificationMessage::*;
    public import ReviewCenter_NotificationPreferenceService::*;
    public import ReviewCenter_NotificationComposer::*;
    public import ReviewCenter_DigestSchedule::*;
    public import ReviewCenter_NotificationDispatcher::*;
    private import SoftwareItemMetadata::*;

    part def CoreSubsystem {
        doc /* Domain model and rules for users, projects, project memberships and access, the evidence settings of projects (anonymous access to the evidence and the content of the attestation PDFs), reviews, review comment threads, the review checklist, and reviewer slots with re-authenticated signatures (by password, or by a single-use proof of a sign-in at an identity provider; with a fresh code of the second factor when the signer has one), including the review content fingerprint, the record digest, the hash-chained audit trail, the registry of git repositories of a project, file selection patterns, revision name validation, the planning of reviews from review-sets, the personal access tokens of users, the line comparison of a file against its baseline, and the inbox of a user with the read state of comments, and the optional e-mail address of a user for notifications (checked, confirmed by a code that is mailed to it, with limits on the mails), and the mails of the notifications (composed from the new items of the inbox, scheduled once for each local date, and dispatched once with batching, retries, and limits), independent of web and database technology. */

        metadata info : SoftwareInfo {
            testFiles = ("test/DemaConsulting.ReviewCenter.Tests/Core/CoreSubsystemTests.cs");
            designFiles = ("docs/design/review-center/core.md");
            verificationFiles = ("docs/verification/review-center/core.md");
            requirementFiles = ("docs/reqstream/review-center/core.yaml");
        }

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
        part userImpactService : UserImpactService;
        part ballotConsolidation : BallotConsolidation;
        part ballotVisibility : BallotVisibility;
        part reviewStatusService : ReviewStatusService;
        part notificationAddress : NotificationAddress;
        part notificationMessage : NotificationMessage;
        part notificationPreferenceService : NotificationPreferenceService;
        part notificationComposer : NotificationComposer;
        part digestSchedule : DigestSchedule;
        part notificationDispatcher : NotificationDispatcher;
    }
}
