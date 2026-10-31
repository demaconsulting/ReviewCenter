## Core

### Verification Approach

The `Core` subsystem is verified by subsystem tests defined in `CoreSubsystemTests.cs`. The tests use the real
`ProjectService`, `ReviewService`, `ThreadService`, `ChecklistService`, `UserService`, `SigningService`,
`RepositoryService`, `PersonalAccessTokenService`, `SelectionMatcher`, `RevisionValidator`, `Fingerprint`,
`RecordDigest`, `ReviewPlanner`, and `ProjectAccess`
together, with
a fixed clock (a clock the test can move for the lockout and the expiry of a token) and fakes for the stores
(`FakeReviewStore`,
`FakeUserStore`, `FakeRepositoryStore`, `FakePersonalAccessTokenStore`), a trivial password hasher
(`FakePasswordHasher`), and a trivial token protector
(`FakeCredentialProtector`) in place of the database and the real hasher and protector,
because the subsystem has no dependency on a
database; the real database is verified in the `Storage` subsystem, the real hasher in the `Identity` subsystem, and the
real token protector and git in the `ContentSources` subsystem.
The tests show that the units cooperate as the
design describes: the services create, store, and read back what the fingerprint algorithm and the rules require.
The rules and the algorithm themselves are verified in detail by the unit tests of the twenty-one units.

### Test Environment

N/A - standard test environment.

### Acceptance Criteria

- All subsystem tests pass with zero failures.
- A project and a review created through the services are read back with the same values, the fingerprint equals
  the known answer for the files, every file keeps its own size and SHA-256 hash, and file bytes are returned
  exactly.
- The same file contents give the same fingerprint when submitted in another order or under other names.
- Every domain rule (all sixty-eight values of `DomainRule`) is reported as a domain rule exception that names it, and
  nothing is stored. The four rules that only a real git can raise (`RevisionNotFound`, `GitUnavailable`,
  `GitAccessFailed`, and `GitTimeout`) are exercised in the `ContentSources` subsystem tests instead.
- A thread started on a file, with replies, resolved and reopened, is read back with its comments in posting order.
- A new review has the whole default checklist; an answer and a section that does not apply are recorded, and the
  progress follows them.
- A user is created and signs in regardless of the case of the user name; repeated failures lock the account and the
  lockout ends on time; a changed password replaces the old one; and the last enabled system administrator cannot
  be demoted.
- Access follows the membership of a project: a system administrator sees and administers every project, a member
  sees the own project, and anyone else, including a disabled, unknown, anonymous, or command line actor, gets the
  same answer as for a missing project; a system administrator creates projects, and a system administrator and the
  administrator of a project manage its members, add an existing user only by the exact user name, are stopped after
  ten failed attempts in ten minutes (a system administrator is not), create an account only together with its
  membership, and may not remove or demote the last project administrator (a system administrator may); a user with a
  temporary password has no access to any project; every membership change is audited with the project; and reviewer
  slots and signing are limited to members (see the unit tests of `ProjectAccess`, `ProjectService`, `SigningService`,
  and `UserService`).
- The evidence settings of a project are private and complete by default; only a system administrator and a project
  administrator of the project read and change them (a plain member is told it may not, and every other actor gets the
  answer for a missing project); a change that was overtaken by another is refused and stores nothing; and every change
  is audited with its changed keys (see the unit tests of `EvidenceSettingsService`).
- The checklist of a project is the built-in default until an administrator replaces it; only a system administrator
  and a project administrator of the project (never through an API token) read and change it, a definition that breaks
  any rule of the strict validation is refused with every error and its path, a replacement with other content needs
  a new version label, a change that was overtaken by another is refused with `RecordChanged`, and every change is
  audited without the wording (see the unit tests of `ProjectChecklistService` and `ChecklistService`). A new review
  freezes the checklist of its project, fails closed when that cannot be used, and is not affected by a later change
  (see the unit tests of `ReviewService`).
- A review with a reviewer slot, answered completely and without open threads, is signed with the password; the
  signature makes the review approved, an administrator's reopen makes it out of date and not approved, and the
  slot can be signed again.
- An approved review is locked: every change of its threads, checklist, and reviewer slots, and a further signature,
  is refused with `ReviewLocked`, and its approval and digest stay as they were; only a project administrator reopens
  it, with a reason, which is audited with the previous digest, keeps the earlier signatures as out-of-date history,
  and needs fresh signatures; reopening a review that is not locked is refused with `ReviewNotLocked`.
- While a project requires independent reviewers (the default), the author of a review is refused as reviewer of a slot
  and as signer, also for a slot that was filled earlier and for a review created through a token (its author is the
  owner of the token); only a project administrator switches the rule, and every switch is audited.
- Every change made through the services is recorded in the audit trail with the person, the action, and a detail
  that never holds a password or a token; recorded entries verify as an intact chain.
- A repository registry that only the administrator of the project (or a system administrator) can change: names,
  locations, and credentials are validated, tokens are stored encrypted and are never part of a listed registration,
  every repository belongs to one project, and every change is audited without a secret (see the unit tests of
  `RepositoryService`).
- A review created from a repository freezes its source and the content of every changed file; a selection with no
  changed file is refused with `NothingToReview` and stores nothing; the fingerprint covers the files at the head;
  the record digest differs when the source, the selection, or any change differs; and a later change of the
  repository does not change the stored review (see the unit tests of `ReviewService` and `RecordDigest`).
- The reviews that a head revision needs are planned from the review-sets in the configuration file of the repository,
  one for each set, as a full review or as the changes since a given or since the last signed revision of that set;
  a baseline that cannot be trusted gives a full review with a note, sets already filed or without a change are told
  apart, and the picked reviews are created each in its own transaction, with the review-set recorded in the review
  (see the unit tests of `ReviewPlanner`, and the real-git tests of the planner in the `ContentSources` subsystem tests).
- A user creates personal access tokens, which are listed without their value; the value authenticates the owner only
  while the token is active; a revoked token and a wrong value are refused; and an actor that came through a token
  cannot create a token, name a reviewer, or sign, while it can create a review that is flagged as created through the
  API (see the unit tests of `PersonalAccessTokenService`, `SigningService`, and `ReviewService`).
- An account of an identity provider is found only by the pair of issuer and subject, is created on the first sign-in
  without any project, system administrator right, or password, and refuses every password operation; its user signs a
  slot only with a live single-use proof of a confirmation (see the unit tests of `UserService`, `SigningService`, and
  `ReauthProofStore`).
- A local account may turn on a second factor: the enrollment is confirmed with a code, a code of a step is accepted
  once even when many requests present it at the same time, a wrong code counts toward the lockout of the password,
  recovery codes work once for signing in and never for signing, a signing needs the password and a fresh code, and
  a system administrator can reset the second factor; each change is audited without a secret or a code (see the unit
  tests of `TotpService`, `TotpChallengeStore`, `UserService`, and `SigningService`).

### Test Scenarios

**CoreSubsystem_ProjectAndReviewWorkflow_ValidInput_StoresFingerprintsAndReadsBack**: A project is created, a
review of three files (text, text, and binary with carriage returns) is created in it, and both are read back
through the services. The review has the Draft state, the known fingerprint, and three files, each with its own
size and known SHA-256 hash, and the exact bytes of the binary file can be read. This scenario is tested by
`CoreSubsystem_ProjectAndReviewWorkflow_ValidInput_StoresFingerprintsAndReadsBack`.

**CoreSubsystem_Fingerprint_SameContentOtherNamesAndOrder_GivesSameFingerprint**: Two reviews are created in one
project from the same three file contents, the second in reverse order and under other file names. Both reviews
have the known fingerprint of the contents. This scenario is tested by
`CoreSubsystem_Fingerprint_SameContentOtherNamesAndOrder_GivesSameFingerprint`.

**CoreSubsystem_RuleViolation_IsReportedAsDomainRuleAndStoresNothing**: A theory over every value of
`DomainRule`, so that a rule added later without a scenario fails the test. For each rule a request that breaks
it is made through the services (a project with a blank name or a description that is too long; a review for a
missing project, with an invalid or duplicate identifier, an invalid title, no files, too many files, a duplicate
or invalid path, a file that is too large, or files that are together too large; a comment with a blank text, an
anchor on a missing file or beyond the end of the file, or a reply to a thread that does not exist; a
checklist answer for a missing checklist, for a check that is not in the checklist, or a failed
check without a justification; a user with an invalid or taken user name, a blank display name, or a too short
password, a wrong current password, a change of a user that does not exist, or disabling the last enabled
system administrator; a member added twice, a change of a user who is not a member, removing the last project
administrator, a project administrator who fails ten adds in a row, and a user with a temporary password who creates a
token (these use the real `ProjectAccess` and `PersonalAccessTokenService`, over `FakeUserStore` connected to
`FakeReviewStore`); a slot for a missing review,
a slot with an unknown reviewer or one that does not exist or is not a member, a change of a
signed slot, a signing by a user who is not in the slot, a signing that is blocked, a wrong password at a signing, or
a signing of a record that changed; a token with an invalid name, an invalid lifetime, or beyond the limit of 20 active
tokens, and an actor that came through a token that signs or names a reviewer; a change of an approved review, a
reopen without a reason, a reopen of a review that is not approved, and a reviewer who is the author of the review
(`ReviewLocked`, `ReopenReasonInvalid`, `ReviewNotLocked`, and `ReviewerNotIndependent`); a repository with an invalid name,
location, or credential, a name already used,
an unknown or disabled repository, or a token that cannot be decrypted; a revision that is not a safe name; and a
selection with no valid pattern; and a selection that matches no changed file; and, over the real `TotpService` in a
`TotpTestWorld`, the six second factor rules: not available, already enrolled, not enrolled, code incorrect, enrollment
expired, and secret unreadable). Each
fails with a `DomainRuleException` that names the rule, and the store holds only the project, review, and
administrator created
first, except that the git-only rules are skipped here. This scenario is tested by `CoreSubsystem_RuleViolation_IsReportedAsDomainRuleAndStoresNothing`.

**CoreSubsystem_ThreadWorkflow_StartReplyResolveReopen_ReadsBackInOrder**: A thread is started on lines 1 to 2 of a
file of a review, two replies are added, the thread is resolved and read back, then reopened and read back again.
The thread has the stored range, the Resolved and then the Open state, and its three comments are in posting order
with their authors (the display names of the actors), texts, and the clock time. This scenario is tested by
`CoreSubsystem_ThreadWorkflow_StartReplyResolveReopen_ReadsBackInOrder`.

**CoreSubsystem_Checklist_FrozenAtCreationAnswerAndProgress**: A review is created through the services and its
checklist is read: it has the default version and 0 of 58 checks answered. One check is then answered Fail with a
padded justification and one section is marked as not applicable with a reason. Read again, the check has the
trimmed justification, the progress is 1 of 53 (the failed check counts as answered and the skipped section leaves
the total), and the checklist is not complete. This scenario is tested by
`CoreSubsystem_Checklist_FrozenAtCreationAnswerAndProgress`.

**CoreSubsystem_UsersWorkflow_CreateSignInLockoutAndLastAdmin**: A system administrator and a reviewer are created,
and the
reviewer signs in with the user name in another case. Five wrong passwords then lock the account, so that the right
password is refused; after five minutes on the movable clock the right password signs in again. The password is
changed with the current one and the new one works. Demoting the only administrator is refused with `LastAdmin`.
This scenario is tested by `CoreSubsystem_UsersWorkflow_CreateSignInLockoutAndLastAdmin`.

**CoreSubsystem_Signatures_SignChangeOutOfDateAndResign_FollowsTheRecord**: A review has one slot of two users. Its
status is blocked while the checks are unanswered; after all checks are answered it has no blockers. Signing with a
wrong password is refused with `ReauthenticationFailed`; signing with the right one approves the review. An
administrator then reopens the approved review with a reason, which changes the record: the slot is out of date, the
review is not approved, and the digest differs. The alternate signs the new record, and the review is approved again
with the alternate as signer.
This scenario is tested by `CoreSubsystem_Signatures_SignChangeOutOfDateAndResign_FollowsTheRecord`.

**CoreSubsystem_AccessTokens_CreateAuthenticateAndRevoke_FollowTheOwner**: A user is created and a token is made through
`PersonalAccessTokenService`. The value authenticates the owner and a value with a changed ending does not; the list
shows the name; an actor that carries the id of the token is refused when it tries to create a token; and after the owner
revokes the token the value is refused. This scenario is tested by
`CoreSubsystem_AccessTokens_CreateAuthenticateAndRevoke_FollowTheOwner`.

**CoreSubsystem_TokenActor_CreatesFlaggedReviewButCannotSign**: An actor that came through a token creates a review,
which is flagged as created through the API; the same actor is refused with `ActorNotAllowed` when it names a reviewer
or signs, even with the right password and digest, and the slot stays unsigned. This scenario is tested by
`CoreSubsystem_TokenActor_CreatesFlaggedReviewButCannotSign`.

**CoreSubsystem_ApprovedReview_RefusesEveryMutationWithReviewLocked**: A review with one slot is answered completely
and signed, so that it is approved. Starting a thread, replying, resolving, and reopening a thread, answering a check,
changing the applicability of a section, and adding, changing, and removing a slot are each refused with
`ReviewLocked`, and a further signature is refused too; the review is still approved with the same digest. This
scenario is tested by `CoreSubsystem_ApprovedReview_RefusesEveryMutationWithReviewLocked`.

**CoreSubsystem_ReopenReview_RequiresAdminAndReasonAndNeedsFreshSignatures**: On an approved review, a plain member is
refused with `ActorNotAllowed` and an empty reason with `ReopenReasonInvalid`. An administrator then reopens it with a
reason: the review is In review, the old signature is kept and out of date, and the digest differs. The reviewer signs
the new record and the review is approved again with both signatures kept, and the audit trail holds `review.reopened`
with the reason and the previous digest. This scenario is tested by
`CoreSubsystem_ReopenReview_RequiresAdminAndReasonAndNeedsFreshSignatures`.

**CoreSubsystem_ReopenReview_OfUnlockedReview_ThrowsReviewNotLocked**: An administrator reopens a review that is not
approved; the request is refused with `ReviewNotLocked` and nothing is stored. This scenario is tested by
`CoreSubsystem_ReopenReview_OfUnlockedReview_ThrowsReviewNotLocked`.

**CoreSubsystem_IndependentReviewers_AuthorCannotBeSlotReviewerOrSign**: With the rule at its default, the author of a
review is refused as primary and as alternate when a slot is added or changed. A slot filled with the author while the
rule is off cannot be signed by the author once the rule is on, whereas a separate reviewer can sign it. With the rule
off the author is accepted as reviewer. This scenario is tested by
`CoreSubsystem_IndependentReviewers_AuthorCannotBeSlotReviewerOrSign`.

**CoreSubsystem_IndependentReviewers_SettingChangeIsAuditedAndAdminOnly**: A project administrator switches the rule off
and on again and each change is audited with `project.independent-reviewers-changed`; a plain member is refused with
`ActorNotAllowed`. This scenario is tested by `CoreSubsystem_IndependentReviewers_SettingChangeIsAuditedAndAdminOnly`.

**CoreSubsystem_ReviewRuleSwitches_ChangeIsAuditedAdminOnlyAndStale**: A plain member and an administrator's token are
refused with `ActorNotAllowed`. A system administrator switches the two further rules (authors answering the checklist
of their own review, and the person who reopened a review signing it again) off, each change is audited once with
`project.review-rule-changed` and the details `RequireNonAuthorChecklistAnswers on->off` and
`RequireNonReopenerSigning on->off`, a repeated change from the old value is refused with `RecordChanged`, and the
independence rule is still on. This scenario is tested by `CoreSubsystem_ReviewRuleSwitches_ChangeIsAuditedAdminOnlyAndStale`.

**CoreSubsystem_ApiTokenReview_AuthorIsTokenUser**: A review created by an actor that came through a token has the
owner of the token as its author, so that the owner is refused as reviewer of a slot while a different member is
accepted. This scenario is tested by `CoreSubsystem_ApiTokenReview_AuthorIsTokenUser`.

**Review due date**: The unit tests of `ReviewService` and `ReviewPlanner` verify the subsystem requirement for the
optional due date of a review: the date from today to two years ahead at creation (also per planned review), the change
by an administrator or the creator with one audit entry, the refusals (an actor of a token, another member, an approved
review, a date too far ahead), the compare-and-set, and that the date is not part of the record digest, so a signature
stays valid after a change. They are tested by `ReviewService_CreateDraftAsync_DueDateInRange_StoresDateAndAuditsIt`,
`ReviewService_SetDueDateAsync_ProjectAdmin_ChangesAndAuditsOldAndNew`,
`ReviewService_SetDueDateAsync_AfterSignature_KeepsDigestAndSignature`, and
`ReviewPlanner_CreateAsync_DueDates_DefaultOwnAndInvalidPerRow`.

**Follow-up review**: The unit tests of `ReviewService`, `FollowUpCarry`, `RecordDigest`, `ChecklistService`, and
`SigningService` verify the subsystem requirement for the follow-up review: the Pass and not-applicable answers, the
not-applicable sections, and the open threads that are carried with their original authors and times, the fail answers
and the resolved threads that are not, the anchor that moves to the whole file with a note when the file changed, the
refusals with `FollowUpInvalid` (another project or unknown source, another review set, another repository, no file in
common, dropped threads that were not acknowledged), the marker that a new answer clears, the digest that binds the
follow-up, the independence rule, and the signing blockers. They are tested by
`ReviewService_CreateDraftAsync_FollowUp_CarriesAnswersAndSections`,
`ReviewService_CreateDraftAsync_UnknownOrForeignSource_IsRefusedWithoutRevealing`,
`ReviewService_FollowUp_ReAnsweringOrResettingSection_ClearsTheCarriedMarker`,
`ReviewService_FollowUp_CarriedOpenThreadBlocksSigning_CarriedAnswersDoNot`,
`ReviewService_FollowUp_CreatorStaysTheAuthorForIndependence`,
`RecordDigest_Compute_FollowUpMarkerChange_ChangesDigest`, `RecordDigest_Compute_NoFollowUp_EqualsLegacyDigest`, and the
tests of `FollowUpCarryTests.cs`. The rule `FollowUpInvalid` is one row of the theory
`CoreSubsystem_RuleViolation_IsReportedAsDomainRuleAndStoresNothing`.

**Audit trail of the services**: The unit tests of `AuditService` and `UserService` verify the subsystem requirement
for the audit trail. A draft recorded through `AuditService` is stored and the chain verifies, which is tested by
`AuditService_RecordAsync_Draft_StoresEntryThatVerifies`, and the administration events of `UserService` (creation,
enabling, disabling, system administrator change, and password changes) are recorded with the acting person and
without a password,
which is tested by `UserService_AdministrationEvents_AreRecordedWithoutPasswords`. The domain services are tested
against a fake store that holds the entries in memory; the transaction that stores a change with its entry is verified
in the `Storage` subsystem.

**External identities**: The unit tests of `UserService` and `SigningService` verify the subsystem requirement for
accounts of an identity provider. The first sign-in with an issuer and subject creates one plain account, and the
second finds it, which is tested by `UserService_SignInExternalAsync_FirstThenSecond_CreatesOnceAndMatches`. A user of
the provider signs a slot once with a live proof of a confirmation, and the proof is then spent, which is tested by
`SigningService_SignAsync_ExternalUserWithProof_SignsOnce`. The proof store itself is verified by the unit tests of
`ReauthProofStore`.

**Second factor**: The unit tests of `TotpService`, `UserService`, and `SigningService` verify the subsystem
requirement for the second factor. A code of a step is accepted once when thirty-two requests present it at the same
time, which is tested by `TotpService_CompleteSignInAsync_ConcurrentSameCode_ExactlyOneSucceeds`, and a signing of an
enrolled user needs the password and a fresh code, which is tested by
`SigningService_SignAsync_EnrolledUserWithPasswordAndFreshCode_Signs`. The six rules of the second factor are
reported by `CoreSubsystem_RuleViolation_IsReportedAsDomainRuleAndStoresNothing`. The challenge of the sign-in is
verified by the unit tests of `TotpChallengeStore`.
