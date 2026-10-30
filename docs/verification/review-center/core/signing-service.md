### SigningService

#### Verification Approach

`SigningService` is verified with unit tests defined in four files in the `Core` test folder: `SigningServiceTests.cs`
(slots, signing, re-authentication with a password, the out-of-date logic, the lock, and the reopening),
`SigningServiceExternalTests.cs` (the signature of a user of an external identity provider), `SigningServiceTotpTests.cs`
(the second factor), and `AuditEventTests.cs` (the audit entries), with the counting user store of `CountingUserStore.cs`
as a helper of the first. The service depends on a review
store, on `UserService`, and on `IProjectAccess`; the tests use the in-memory fake store (`FakeReviewStore`), the real
`UserService` over the
in-memory user store and a trivial password hasher (`FakePasswordHasher`), so that the re-authentication goes through
the real login path and its lockout without the cost of the real hasher, and a clock the test can move
(`MovableTimeProvider`). The real services for threads and checklists are used to change the record. The real store is
verified by its own unit and by the `Storage` subsystem; the digest is verified by the `RecordDigest` unit. Most tests
use the test double `AllowAllProjectAccess` as project access, which lets every actor see the project that exists and
administer it, but decides membership from the memberships stored in the fake store; the access rules themselves are
verified by the `ProjectAccess` unit. The tests of the rules that depend on who the actor is (who may reopen a review,
and that whoever may see the project may change its slots, also a system administrator who is not a member) use the real
`ProjectAccess` over the fake stores. The rules
are checked through the `DomainRuleException` that names the broken rule, and through the state of the fake store, so
that a refused request is shown to store nothing. The fake store does not re-check the approval lock inside a write
(the real store does, and its tests are in the `ReviewStore` unit), so these tests show the lock decided by the
service. The fake store records the audit drafts it is given, which shows that the service passes each draft with the
change; it cannot show that the real store saves the two in one transaction, which is verified in the `ReviewStore` unit.
The attempt that is spent when the store refuses a signature with `RecordChanged` after the password, the
authenticator code, or the proof was already checked is a design property that is not verified by a unit test of this
unit, because the fake store recomputes the digest in the same call and so offers no point between the two to change
the record. The review is created by a user (`author`) who is not a test user, so the many tests that sign with the other
users are not affected by the rule of independent reviewers; the tests of that rule use a review created by a member.

#### Test Environment

N/A - standard test environment.

#### Acceptance Criteria

- All unit tests pass with zero failures.
- Any actor who may see the project adds slots (a member, and also a system administrator who is not a member), which
  are numbered in creation order; a slot has a primary and an optional alternate reviewer, and a review may have no
  slot. A user who cannot see the project, and a project that does not exist, are refused with `ProjectNotFound`.
- The reviewers of a slot must be enabled members of the project (a system administrator who is not a member is
  refused), must differ, and must not sit in another slot of the review.
- A change of slots by an unknown or disabled user, or for a missing review or slot, is refused; so is the removal of a
  slot and the reopening of a review by an unknown or disabled user.
- A slot with a signature, current or out of date, cannot be changed or removed; an unsigned one can.
- The primary or the alternate reviewer signs with the password entered again, and the signature records the slot, the
  user, the role, the time, the digest, and the meaning text.
- Nobody else can sign a slot, a disabled user cannot sign, and a reviewer who has been removed from the project
  cannot sign.
- A wrong, empty, or missing password gives `ReauthenticationFailed`, and repeated failures lock the account as at
  sign-in.
- A user of an external identity provider signs only with a live proof for exactly this user, project, review, and
  slot, which the signature spends; without one (also with a password, an expired proof, a proof of another binding,
  or no store of proofs) the signature is refused with `ReauthenticationFailed`, nothing is signed, the failure is
  recorded in the audit trail as `signature.reauth-failed` with the detail `oidc: no-proof` and no secret, and a
  refusal before the password step keeps the proof.
- The page can ask whether a proof exists without spending it, and the check before a confirmation accepts only an
  external member who is a reviewer of the slot.
- Signing is refused with the blockers named while a check is unanswered, a check of an applicable section is answered
  Fail, or a thread is open; a Fail in a section that does not apply does not block.
- Signing is refused when the record changed after the signer saw it, and when the slot already has a signature of the
  current record.
- A signature goes out of date when the record changes, by the reopening of the review or by a change of the content
  of a review that is not approved (a checklist answer or a comment) while a signature is current; the review is then
  not approved, the slot can be signed again, and the earlier signature is kept.
- A review is approved only when it has at least one slot and every slot has a current signature.
- An approved review is locked: adding, changing, and removing a slot and signing are refused with `ReviewLocked`,
  the message of which says that an administrator must reopen the review, and nothing is stored.
- A project administrator (a system administrator, who need not be a member, or an administrator of the project) reopens
  a locked review with a
  reason of 1 to 250 characters after trimming; a user who does not administer the project and an actor that came
  through a token are refused with `ActorNotAllowed`, a user who cannot see the project with `ProjectNotFound`, an empty
  or too long reason with `ReopenReasonInvalid`, a review that does not exist with `ReviewNotFound`, and a
  review that is not approved with `ReviewNotLocked`; nothing is stored in these cases.
- A reopen stores who, when, why, and the digest of the approved record, and records the audit entry
  `review.reopened`, naming the actor, with the previous digest and the reason. The review is In review afterwards, the
  earlier
  signatures are kept as out-of-date history, also when the content is changed back, and the review is approved again
  only when every slot is signed again.
- While the project requires independent reviewers (the default), the user who created the review is refused as
  primary or alternate reviewer when a slot is added or changed and is refused when signing, also in a slot that was
  filled before the rule was switched on, with `ReviewerNotIndependent` and before the password is asked for; with the
  rule off the author may be assigned and may sign.
- The candidates for a slot are the enabled members of the project.
- The status of a slot says for the primary and the alternate reviewer whether the user is still a member of the
  project, and a removed user stays assigned to the slot.
- An actor that came through a personal access token cannot sign, and cannot add, change, or remove a slot; the refusal
  is `ActorNotAllowed` with the message that names the API token and comes before every other check, so that a request
  with everything right and one with everything wrong give the same answer, and the wrong passwords are not counted
  toward the lockout and are not recorded.
- A refusal that comes before the password step (a changed record, a blocker, a signer who is not in the slot) does not
  count the password that was given as a failed attempt, so the signer can sign with the right password afterwards.
- Null arguments are rejected with an `ArgumentNullException`.
- A user with a second factor signs with the password and a fresh code of the authenticator, which the signature
  spends; a missing, empty, wrong, malformed, recovery, or already used code is refused with the one message that
  names the password and the code, nothing is signed, and a recovery code is not spent; a wrong password is refused
  with the same message and does not spend the code; wrong codes count toward the lockout shared with the password;
  a service without the second factor service refuses an enrolled user (fail closed).
- A user without a second factor signs with the password alone and gets the message that names only the password; an
  external user is not affected; an actor that came through a token is refused and does not spend the code; after an
  administrator reset the user signs with the password alone again.

#### Test Scenarios

**SigningService_AddSlotAsync_Valid_AddsNumberedUnsignedSlot**: Two slots are added to a review, one with a primary
and an alternate and one with a primary only. The status lists them as slots 1 and 2 with the chosen reviewers, and
neither is signed. This scenario is tested by `SigningService_AddSlotAsync_Valid_AddsNumberedUnsignedSlot`.

**SigningService_AddSlotAsync_SameUserTwice_ThrowsSlotUserInvalid**: A slot whose primary and alternate are the same
user is refused with `SlotUserInvalid`. This scenario is tested by
`SigningService_AddSlotAsync_SameUserTwice_ThrowsSlotUserInvalid`.

**SigningService_AddSlotAsync_UserInOtherSlot_ThrowsSlotUserInvalid**: A theory over a user who already sits in a slot
as primary or as alternate and is chosen again as primary or alternate of a second slot; each is refused with
`SlotUserInvalid`. This scenario is tested by `SigningService_AddSlotAsync_UserInOtherSlot_ThrowsSlotUserInvalid`.

**SigningService_AddSlotAsync_UnknownOrDisabledUser_ThrowsSlotUserInvalid**: A primary or an alternate reviewer that
does not exist or is disabled is refused with `SlotUserInvalid`. This scenario is tested by
`SigningService_AddSlotAsync_UnknownOrDisabledUser_ThrowsSlotUserInvalid`.

**SigningService_AddSlotAsync_ActorDisabledOrUnknown_ThrowsActorNotAllowed**: An actor that does not exist or has been
disabled cannot add a slot; the request is refused with `ActorNotAllowed`. This scenario is tested by
`SigningService_AddSlotAsync_ActorDisabledOrUnknown_ThrowsActorNotAllowed`.

**SigningService_AddSlotAsync_MissingReview_ThrowsReviewNotFound**: A slot for a review that does not exist is refused
with `ReviewNotFound`. This scenario is tested by `SigningService_AddSlotAsync_MissingReview_ThrowsReviewNotFound`.

**SigningService_UpdateRemoveAndReopen_ActorDisabledOrUnknown_ThrowsActorNotAllowed**: On an approved review with a
signed slot, a disabled user and an unknown user each try to change the slot, remove it, and reopen the review; all six
requests are refused with `ActorNotAllowed`, nothing is recorded in the audit trail, the review is still approved, and
no reopen is stored. This scenario is tested by
`SigningService_UpdateRemoveAndReopen_ActorDisabledOrUnknown_ThrowsActorNotAllowed`.

**SigningService_SlotSignAndReopenCalls_ProjectNotFound_ThrowsProjectNotFound**: Adding, changing, and removing a slot,
signing, reopening, and the check before a confirmation, each for a project that does not exist, are refused with
`ProjectNotFound`. This scenario is tested by `SigningService_SlotSignAndReopenCalls_ProjectNotFound_ThrowsProjectNotFound`.

**SigningService_SlotChanges_RealAccessRules_NeedViewAccessOnly**: With the real access rules, a user outside the
project is refused adding, changing, and removing a slot with `ProjectNotFound`, and a system administrator who is not a
member does all three; the audit trail names the administrator for each change. This is the rule that whoever may see
the project arranges its slots, while only a member can be a reviewer and sign. This scenario is tested by
`SigningService_SlotChanges_RealAccessRules_NeedViewAccessOnly`.

**SigningService_UpdateAndRemoveSlotAsync_Unsigned_Succeed**: The reviewers of a slot without a signature are changed,
and then the slot is removed; the status shows the change and then no slot. This scenario is tested by
`SigningService_UpdateAndRemoveSlotAsync_Unsigned_Succeed`.

**SigningService_UpdateAndRemoveSlotAsync_Signed_ThrowsSlotSigned**: A theory over a slot with a current signature and
a slot whose signature is out of date, each next to a second slot nobody has signed so that the review is not locked;
changing and removing each is refused with `SlotSigned` and the slot is unchanged. This scenario is tested by `SigningService_UpdateAndRemoveSlotAsync_Signed_ThrowsSlotSigned`.

**SigningService_UpdateAndRemoveSlotAsync_UnknownSlot_ThrowsSlotNotFound**: Changing or removing a slot that is not
part of the review is refused with `SlotNotFound`. This scenario is tested by
`SigningService_UpdateAndRemoveSlotAsync_UnknownSlot_ThrowsSlotNotFound`.

**SigningService_UpdateSlotAsync_KeepOwnUsers_Succeeds**: Saving a slot with its own current reviewers (which are
"in a slot" but not in another one) is accepted. This scenario is tested by
`SigningService_UpdateSlotAsync_KeepOwnUsers_Succeeds`.

**SigningService_SignAsync_PrimaryWithPassword_StoresSignatureAndApproves**: The primary reviewer signs a ready review
with the digest seen and the right password, after the clock moved two hours. The signature has the slot, the
user, the display name, the Primary role, the time, the digest, and the meaning text; the slot is Signed with the
signer and the time, and the review is approved with the next step "Approved. Every reviewer slot has a current
signature." This scenario is tested by `SigningService_SignAsync_PrimaryWithPassword_StoresSignatureAndApproves`.

**SigningService_SignAsync_Alternate_RecordsAlternateRole**: The alternate reviewer signs the slot, and the signature
records the Alternate role. This scenario is tested by `SigningService_SignAsync_Alternate_RecordsAlternateRole`.

**SigningService_SignAsync_NotInSlot_ThrowsActorNotAllowed**: A user who is neither primary nor alternate of the slot,
even with the right password, is refused with `ActorNotAllowed` and nothing is signed. This scenario is tested by
`SigningService_SignAsync_NotInSlot_ThrowsActorNotAllowed`.

**SigningService_SignAsync_ActorDisabled_ThrowsActorNotAllowed**: A reviewer of the slot who has been disabled is
refused with `ActorNotAllowed`. This scenario is tested by `SigningService_SignAsync_ActorDisabled_ThrowsActorNotAllowed`.

**SigningService_SignAsync_WrongPassword_ThrowsReauthenticationFailed**: A theory over a wrong, an empty, and a missing
password; each is refused with `ReauthenticationFailed` and nothing is signed. This scenario is tested by
`SigningService_SignAsync_WrongPassword_ThrowsReauthenticationFailed`.

**SigningService_SignAsync_RepeatedWrongPassword_LocksOutLikeLogin**: Five wrong passwords at the signature lock the
account, so that the right password is then refused at the signature and at sign-in, and nothing is signed. This
scenario is tested by `SigningService_SignAsync_RepeatedWrongPassword_LocksOutLikeLogin`.

**SigningService_SignAsync_Blocker_ThrowsSigningNotAllowedAndListsBlocker**: A theory over a review with an unanswered
check, with a check answered Fail, and with an open thread; each is refused with `SigningNotAllowed`, the message and
the blockers of the status name the blocker, the review is not approved, and nothing is stored. This scenario is
tested by `SigningService_SignAsync_Blocker_ThrowsSigningNotAllowedAndListsBlocker`.

**SigningService_GetApprovalAsync_NotApplicableSectionFail_DoesNotBlock**: A check answered Fail blocks signing; after
its section is marked as not applicable the blockers are empty. This scenario is tested by
`SigningService_GetApprovalAsync_NotApplicableSectionFail_DoesNotBlock`.

**SigningService_SignAsync_StaleExpectedDigest_ThrowsRecordChanged**: The signer saw a digest, then a thread was
opened and resolved. Signing with the digest seen is refused with `RecordChanged` and nothing is signed. This scenario
is tested by `SigningService_SignAsync_StaleExpectedDigest_ThrowsRecordChanged`.

**SigningService_SignAsync_AlreadySignedCurrent_ThrowsSigningNotAllowed**: A slot that the primary reviewer has signed,
next to a second slot nobody has signed so that the review is not locked,
cannot be signed again for the same record by the alternate; the request is refused with `SigningNotAllowed` and one
signature remains. This scenario is tested by `SigningService_SignAsync_AlreadySignedCurrent_ThrowsSigningNotAllowed`.

**SigningService_SignAsync_RecordChangedAfterSigning_OutOfDateThenResignable**: After a signature, an administrator
reopens the approved review, which changes the record (an approved review accepts no other change). The slot is
OutOfDate, the review is not approved, and the digest differs. The alternate signs again;
the slot is Signed by the alternate, the review is approved, and both signatures are kept. This scenario is tested by
`SigningService_SignAsync_RecordChangedAfterSigning_OutOfDateThenResignable`; it covers the change of the record by a
reopening only.

**SigningService_GetApprovalAsync_ContentChangedAfterSigning_OutOfDateThenResignable**: A theory over a checklist answer
that is changed and a comment that is added. On a review with two slots, the first is signed, so the review is not
approved and not locked. After the content change the digest differs, the first slot is OutOfDate, the second is not
signed, the review is not approved, and the signature is kept. For the comment, the open thread blocks signing with
`SigningNotAllowed` until it is resolved. The alternate then signs the new record, both signatures are kept, and the
review is approved when the second slot has signed. This scenario is tested by
`SigningService_GetApprovalAsync_ContentChangedAfterSigning_OutOfDateThenResignable`.

**SigningService_GetApprovalAsync_TwoSlots_ApprovedOnlyWhenBothSigned**: With two slots, the review is not approved
and the next step is "Ready for signatures." after one signature, and it is approved after both. This scenario is
tested by `SigningService_GetApprovalAsync_TwoSlots_ApprovedOnlyWhenBothSigned`.

**SigningService_GetApprovalAsync_NoSlots_NotApprovedAndAsksForSlot**: A review that is otherwise ready but has no slot
is not approved, has no blockers, has the next step "Add a reviewer slot.", and has a digest of 64 lowercase
hexadecimal characters. This scenario is tested by `SigningService_GetApprovalAsync_NoSlots_NotApprovedAndAsksForSlot`.

**SigningService_GetApprovalAsync_MissingReview_ReturnsNull**: The status of a review that does not exist is `null`.
This scenario is tested by `SigningService_GetApprovalAsync_MissingReview_ReturnsNull`.

**SigningService_GetApprovalsAsync_SeveralReviews_EqualsSingleReadsAndLoadsUsersOnce**: Two reviews and a review that
does not exist are read in one batch over a user store that counts list reads. The batch has the two existing reviews,
the status of a review equals the single read, and the user list was read once. This scenario is tested by
`SigningService_GetApprovalsAsync_SeveralReviews_EqualsSingleReadsAndLoadsUsersOnce`.

**SigningService_GetApprovalsAsync_NoReviews_ReturnsEmptyWithoutReadingUsers**: An empty sequence of review
identifiers is read in one batch over a user store that counts list reads. The result is empty and the user list was
never read. This scenario is tested by `SigningService_GetApprovalsAsync_NoReviews_ReturnsEmptyWithoutReadingUsers`.

**SigningService_ListCandidatesAsync_ReturnsEnabledMembersOnly**: With one member disabled and one user who is not a
member of the project, the candidates are the other enabled members. This scenario is tested by
`SigningService_ListCandidatesAsync_ReturnsEnabledMembersOnly`.

**SigningService_GetApprovalAsync_AssigneeRemovedFromProject_IsMarkedNotAMember**: A slot has a primary and an
alternate reviewer who are both members; a control read marks both as members. After both memberships are removed
from the fake store, the single read and the batch read keep the slot assigned to the same users and mark the primary
and the alternate as no longer members. This scenario is tested by
`SigningService_GetApprovalAsync_AssigneeRemovedFromProject_IsMarkedNotAMember`.

**SigningService_AddSlotAsync_NonMember_ThrowsSlotUserInvalid**: A user who is not a member of the project, even a
system administrator, is refused as primary and as alternate with `SlotUserInvalid`, and no slot is stored. This
scenario is tested by `SigningService_AddSlotAsync_NonMember_ThrowsSlotUserInvalid`.

**SigningService_SignAsync_NotAMember_IsRefused**: The primary reviewer of a ready slot is removed from the project and
then tries to sign; the signing is refused with `ActorNotAllowed` and nothing is signed. This scenario is tested by
`SigningService_SignAsync_NotAMember_IsRefused`.

**SigningService_SignAsync_ApiTokenActor_IsRefusedBeforeAnyCheck**: A review that can be signed is prepared, and the
primary reviewer's identity arrives through a token. Signing with the right password, slot, and digest is refused with
`ActorNotAllowed` and the message that names the API token. Signing six times, more than the lockout allows, with a wrong
password, a missing review, an unknown slot, and a stale digest gives the same refusal each time, which shows that
nothing else is checked first. The slot stays unsigned, nothing is recorded in the audit trail, and the real person
can then sign with the right password, which shows that no wrong password was counted. This scenario is tested by
`SigningService_SignAsync_ApiTokenActor_IsRefusedBeforeAnyCheck`.

**SigningService_SignAsync_RefusedBeforePassword_DoesNotCountWrongPasswords**: A theory over a stale digest, an open
thread, and a signer who is a member but not in the slot. Six signature requests with a wrong password are each
refused for the real reason (`RecordChanged`, `SigningNotAllowed`, `ActorNotAllowed`), and after the cause is removed
the primary reviewer signs with the right password, which shows that the password was not asked for and no failed
attempt was counted. This scenario is tested by
`SigningService_SignAsync_RefusedBeforePassword_DoesNotCountWrongPasswords`.

**SigningService_AddUpdateRemoveSlot_ApiTokenActor_IsRefused**: An actor that came through a token adds, changes, and
removes a slot; each is refused with `ActorNotAllowed` and the existing slot is unchanged. This scenario is tested by
`SigningService_AddUpdateRemoveSlot_ApiTokenActor_IsRefused`.

**SigningService_AddSlotAsync_AuthorAsPrimaryOrAlternate_ThrowsReviewerNotIndependent**: A theory over the author of the
review (a project member who created it) chosen as primary and as alternate reviewer of a new slot, with the project
at its default of requiring independent reviewers; each is refused with `ReviewerNotIndependent` and no slot is stored.
This scenario is tested by `SigningService_AddSlotAsync_AuthorAsPrimaryOrAlternate_ThrowsReviewerNotIndependent`.

**SigningService_UpdateSlotAsync_AuthorAsPrimaryOrAlternate_ThrowsReviewerNotIndependent**: A theory over an unsigned
slot of another reviewer that is changed so that the author is the primary or the alternate; each is refused with
`ReviewerNotIndependent` and the slot keeps its reviewers. This scenario is tested by
`SigningService_UpdateSlotAsync_AuthorAsPrimaryOrAlternate_ThrowsReviewerNotIndependent`.

**SigningService_AddSlotAsync_AuthorAndIndependenceOff_Succeeds**: With the requirement switched off for the project,
the author is accepted as the primary reviewer of a slot. This scenario is tested by
`SigningService_AddSlotAsync_AuthorAndIndependenceOff_Succeeds`.

**SigningService_SignAsync_AuthorInPreexistingSlot_ThrowsReviewerNotIndependent**: The author is put into a slot while
the requirement is off, and the requirement is then switched on. The author signing the ready review, with the right
password, is refused with `ReviewerNotIndependent` and nothing is signed. This scenario is tested by
`SigningService_SignAsync_AuthorInPreexistingSlot_ThrowsReviewerNotIndependent`.

**SigningService_AddUpdateRemoveSlotAsync_ApprovedReview_ThrowsReviewLocked**: A review that every slot has signed is
approved. Adding, changing, and removing a slot are each refused with `ReviewLocked`; the slots are unchanged and the
review is still approved with the same digest. This scenario is tested by
`SigningService_AddUpdateRemoveSlotAsync_ApprovedReview_ThrowsReviewLocked`.

**SigningService_SignAsync_ApprovedReview_ThrowsReviewLocked**: Signing an approved review again is refused with
`ReviewLocked` (not with the "already signed" message), and the signatures are unchanged. This scenario is tested by `SigningService_SignAsync_ApprovedReview_ThrowsReviewLocked`.

**SigningService_ReopenReviewAsync_NotAdmin_ThrowsActorNotAllowed**: A member of the project who does not administer it
asks to reopen an approved review; the request is refused with `ActorNotAllowed`, and the review is still approved with
no reopen stored. This scenario is tested by `SigningService_ReopenReviewAsync_NotAdmin_ThrowsActorNotAllowed`.

**SigningService_ReopenReviewAsync_InvalidReason_ThrowsReopenReasonInvalid**: A missing, a white-space, and a too long
reason (more than 250 characters) are each refused with `ReopenReasonInvalid` and the review stays approved. This
scenario is tested by `SigningService_ReopenReviewAsync_InvalidReason_ThrowsReopenReasonInvalid`.

**SigningService_ReopenReviewAsync_NotLocked_ThrowsReviewNotLocked**: Reopening a review that is not approved (one
without a current signature in every slot) is refused with `ReviewNotLocked` and nothing is stored. This scenario is tested
by `SigningService_ReopenReviewAsync_NotLocked_ThrowsReviewNotLocked`.

**SigningService_ReopenReviewAsync_ApiToken_ThrowsActorNotAllowed**: An administrator whose identity arrives through a
token asks to reopen an approved review with a valid reason; the request is refused with `ActorNotAllowed` and the
review stays approved. This scenario is tested by `SigningService_ReopenReviewAsync_ApiToken_ThrowsActorNotAllowed`.

**SigningService_ReopenReviewAsync_Admin_ReturnsToInReviewKeepsSignaturesAndAudits**: An administrator reopens an
approved review with a reason that has surrounding white space. The review is no longer approved, the digest differs
from the approved one, the signature is kept and its slot is OutOfDate, the history has one reopen with the trimmed
reason, the administrator, the time, and the previous digest, and the audit trail has `review.reopened` with the
previous digest and the reason in its detail. This scenario is tested by
`SigningService_ReopenReviewAsync_Admin_ReturnsToInReviewKeepsSignaturesAndAudits`; the entry names the actor as
`Cyd (cyd)`.

**SigningService_ReopenReviewAsync_MissingReview_ThrowsReviewNotFound**: Reopening a review that does not exist, with a
valid reason, is refused with `ReviewNotFound` and nothing is recorded. This scenario is tested by
`SigningService_ReopenReviewAsync_MissingReview_ThrowsReviewNotFound`.

**SigningService_ReopenReviewAsync_ReasonAtLimitWithBlanks_IsAcceptedTrimmed**: A reason of 250 characters plus one more
character, with blanks around it, is refused with `ReopenReasonInvalid`; a reason of exactly 250 characters with blanks
around it is accepted, and the history and the audit entry hold it trimmed. This scenario is tested by
`SigningService_ReopenReviewAsync_ReasonAtLimitWithBlanks_IsAcceptedTrimmed`.

**SigningService_ReopenReviewAsync_RealAccessRules_ReopensOnlyForAdministrators**: With the real access rules, a theory
over a project administrator and a system administrator who is not a member, who each reopen an approved review (the
review is no longer approved, one reopen is stored for the user, and the audit entry names the user), and over a plain
member, who is refused with `ActorNotAllowed`, and a user outside the project, who is refused with `ProjectNotFound`
(the review stays approved and nothing is recorded). This scenario is tested by
`SigningService_ReopenReviewAsync_RealAccessRules_ReopensOnlyForAdministrators`.

**SigningService_ReopenReviewAsync_ThenResign_ApprovesAgainWithBothSignatures**: After the reopen, a checklist answer is
changed and changed back, so the content is what it was when the old signature was made; the old signature is still out
of date, because the reopening is part of the record, and the review is not approved. The reviewer then signs the new
record; the review is approved again, both the old and the new signature are stored, and only the new one is
current. This scenario is tested by `SigningService_ReopenReviewAsync_ThenResign_ApprovesAgainWithBothSignatures`.

**SigningService external scenarios**: The signature of a user of an external identity provider is verified in
`SigningServiceExternalTests.cs`, over the same fakes and a real `ReauthProofStore` that shares the clock of the
service. A live proof for the user, review, and slot lets the user sign without a password, and the proof is spent by
it, which is tested by `SigningService_SignAsync_ExternalUserWithProof_SignsOnce`. A theory over a null, an empty, and a
right-looking password without a proof, and over a proof left to expire, is refused with `ReauthenticationFailed`, the
slot stays unsigned, and the audit trail holds one `signature.reauth-failed` entry with the detail `oidc: no-proof`
that does not contain the password, which is tested by
`SigningService_SignAsync_ExternalUserWithoutLiveProof_IsRefused`. Proofs
for another slot, another review, and another user do not let the user sign, and the proof of the other slot is not
spent by the attempt, which is tested by `SigningService_SignAsync_ProofOfOtherBinding_IsRefused`. A service without a
store of proofs refuses an external user even when a proof was issued elsewhere, which is tested by
`SigningService_SignAsync_ExternalUserWithoutProofStore_IsRefused`. An actor that came through a token with a live
proof is refused with `ActorNotAllowed` and the proof stays, which is tested by
`SigningService_SignAsync_ApiTokenActorWithProof_IsRefusedAndKeepsProof`; a stale expected digest is refused with
`RecordChanged` and the proof stays, which is tested by `SigningService_SignAsync_RecordChanged_KeepsTheProof`.
`HasReauthProofAsync` answers `true` twice for a live proof (so it does not spend it) and `false` for another slot, a
local user, a token actor, and an unknown user, which is tested by
`SigningService_HasReauthProofAsync_AnswersWithoutSpending`. `RequireConfirmableAsync` returns the stored user for the
reviewer of the slot and refuses a local user and a token actor with `ActorNotAllowed`, an unknown slot with
`SlotNotFound`, and an unknown user with `ActorNotAllowed`, which is tested by
`SigningService_RequireConfirmableAsync_AcceptsOnlyAReviewerOfTheSlot`; a second external user is refused with
`ActorNotAllowed` and the message about membership while the user is not a member of the project, and with the message
"You are not a reviewer of this slot." once the user is a member who is not a reviewer of the slot, which is tested by
`SigningService_RequireConfirmableAsync_NotAReviewerOfTheSlot_IsRefused`.

**SigningService_Constructor_NullArguments_Throw**: A null store, user service, project access, or clock is rejected
with an
`ArgumentNullException`. This scenario is tested by `SigningService_Constructor_NullArguments_Throw`.

**SigningService_SlotAndSignatureEvents_AreRecordedWithMembersAndDigest**: A slot is added, changed, and signed; the
entries `slot.added`, `slot.updated`, and `signature.added` carry the members and the signed record digest, and no
password. This scenario is tested by `SigningService_SlotAndSignatureEvents_AreRecordedWithMembersAndDigest`. A refused
add of an unknown user (`SlotUserInvalid`, before signing) and a refused add to the approved review (`ReviewLocked`,
after signing) record nothing. The removal of a slot records `slot.removed` with its members, which is tested by
`SigningService_RemoveSlotAsync_RecordsSlotRemovedWithItsMembers`. Both tests are defined in `AuditEventTests.cs`.

**SigningService_SignAsync_WrongPasswordThenLocked_RecordsReauthFailedEntriesWithoutPassword**: After four wrong
passwords through `ReauthenticateAsync`, signing with a wrong password (the fifth failure, which locks the account)
and then with the right password (met by the lock) are both refused with `ReauthenticationFailed`; the trail holds five
`signature.reauth-failed` entries with the detail `wrong password` and one with `account locked`, and no entry
contains the typed password. This
scenario is tested by `SigningService_SignAsync_WrongPasswordThenLocked_RecordsReauthFailedEntriesWithoutPassword`,
defined in `AuditEventTests.cs`.

**SigningService second factor scenarios**: These tests are defined in `SigningServiceTotpTests.cs`. They use the real
`SigningService` with the real `TotpService` from `TotpTestWorld`, a review with a slot for an enrolled user and a
slot for an external user, and a clock moved to the next time step so that the enrollment code is no longer fresh.
The right password and a fresh code sign, the signature shows the display name, and the code is spent (one step
advance and a later verification fails), which is tested by
`SigningService_SignAsync_EnrolledUserWithPasswordAndFreshCode_Signs`. A theory over a missing, empty, wrong,
malformed, recovery, and already used code is refused with `ReauthenticationFailed` and the message "The password or
the code is incorrect.", nothing is signed, all ten recovery codes remain, and no step is spent; this is tested by
`SigningService_SignAsync_EnrolledUserWithoutRightCode_IsRefused`. A wrong password with a right code is refused with
the same message and the code still works afterwards, which is tested by
`SigningService_SignAsync_WrongPasswordRightCode_IsRefusedAndKeepsTheCode`. Five wrong codes lock the account, the
right password and code are then refused, the sign-in password step is locked as well, and `signature.reauth-failed`
entries with the detail `wrong code` exist; this is tested by
`SigningService_SignAsync_WrongCodes_LockTheAccountForSigningAndSignIn`. A signing
service without the second factor service refuses an enrolled user, which is tested by
`SigningService_SignAsync_EnrolledUserWithoutTotpService_FailsClosed`. A user without a second factor signs with the
password alone and a wrong password gets the message "The password is incorrect.", which is tested by
`SigningService_SignAsync_UserWithoutSecondFactor_NeedsNoCode`. An external user signs with a proof, with no password
and no code, which is tested by `SigningService_SignAsync_ExternalUser_IsNotAffectedByTheSecondFactor`. An actor that
came through a token is refused with `ActorNotAllowed` and the code is still good for the person, which is tested by
`SigningService_SignAsync_ApiTokenActorWithPasswordAndCode_IsRefusedAndKeepsTheCode`. After an administrator reset the
user signs with the password alone, which is tested by `SigningService_SignAsync_AfterAdministratorReset_NeedsNoCode`.
