### SigningService

#### Verification Approach

`SigningService` is verified with unit tests defined in `SigningServiceTests.cs`. The service depends on a review
store and on `UserService`; the tests use the in-memory fake store (`FakeReviewStore`), the real `UserService` over the
in-memory user store and a trivial password hasher (`FakePasswordHasher`), so that the re-authentication goes through
the real login path and its lockout without the cost of the real hasher, and a clock the test can move
(`MovableTimeProvider`). The real services for threads and checklists are used to change the record. The real store is
verified by its own unit and by the `Storage` subsystem; the digest is verified by the `RecordDigest` unit. The rules
are checked through the `DomainRuleException` that names the broken rule, and through the state of the fake store, so
that a refused request is shown to store nothing.

#### Test Environment

N/A - standard test environment.

#### Acceptance Criteria

- All unit tests pass with zero failures.
- Any signed-in user adds slots, which are numbered in creation order; a slot has a primary and an optional alternate
  reviewer, and a review may have no slot.
- The reviewers of a slot must be enabled users, must differ, and must not sit in another slot of the review.
- A change of slots by an unknown or disabled user, or for a missing review or slot, is refused.
- A slot with a signature, current or out of date, cannot be changed or removed; an unsigned one can.
- The primary or the alternate reviewer signs with the password entered again, and the signature records the slot, the
  user, the role, the time, the digest, and the meaning text.
- Nobody else can sign a slot, and a disabled user cannot sign.
- A wrong, empty, or missing password gives `ReauthenticationFailed`, and repeated failures lock the account as at
  sign-in.
- Signing is refused with the blockers named while a check is unanswered, a check of an applicable section is answered
  Fail, or a thread is open; a Fail in a section that does not apply does not block.
- Signing is refused when the record changed after the signer saw it, and when the slot already has a signature of the
  current record.
- A signature goes out of date when the record changes, the slot can be signed again, and the earlier signature is
  kept.
- A review is approved only when it has at least one slot and every slot has a current signature.
- The candidates for a slot are the enabled users.
- Null arguments are rejected with an `ArgumentNullException`.

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

**SigningService_UpdateAndRemoveSlotAsync_Unsigned_Succeed**: The reviewers of a slot without a signature are changed,
and then the slot is removed; the status shows the change and then no slot. This scenario is tested by
`SigningService_UpdateAndRemoveSlotAsync_Unsigned_Succeed`.

**SigningService_UpdateAndRemoveSlotAsync_Signed_ThrowsSlotSigned**: A theory over a slot with a current signature and
a slot whose signature is out of date; changing and removing each is refused with `SlotSigned` and the slot is
unchanged. This scenario is tested by `SigningService_UpdateAndRemoveSlotAsync_Signed_ThrowsSlotSigned`.

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

**SigningService_SignAsync_AlreadySignedCurrent_ThrowsSigningNotAllowed**: A slot that the primary reviewer has signed
cannot be signed again for the same record by the alternate; the request is refused with `SigningNotAllowed` and one
signature remains. This scenario is tested by `SigningService_SignAsync_AlreadySignedCurrent_ThrowsSigningNotAllowed`.

**SigningService_SignAsync_RecordChangedAfterSigning_OutOfDateThenResignable**: After a signature, a thread is opened
and resolved. The slot is OutOfDate, the review is not approved, and the digest differs. The alternate signs again;
the slot is Signed by the alternate, the review is approved, and both signatures are kept. This scenario is tested by
`SigningService_SignAsync_RecordChangedAfterSigning_OutOfDateThenResignable`.

**SigningService_GetApprovalAsync_TwoSlots_ApprovedOnlyWhenBothSigned**: With two slots, the review is not approved
and the next step is "Ready for signatures." after one signature, and it is approved after both. This scenario is
tested by `SigningService_GetApprovalAsync_TwoSlots_ApprovedOnlyWhenBothSigned`.

**SigningService_GetApprovalAsync_NoSlots_NotApprovedAndAsksForSlot**: A review that is otherwise ready but has no slot
is not approved, has no blockers, has the next step "Add a reviewer slot.", and has a digest of 64 lowercase
hexadecimal characters. This scenario is tested by `SigningService_GetApprovalAsync_NoSlots_NotApprovedAndAsksForSlot`.

**SigningService_GetApprovalAsync_MissingReview_ReturnsNull**: The status of a review that does not exist is `null`.
This scenario is tested by `SigningService_GetApprovalAsync_MissingReview_ReturnsNull`.

**SigningService_ListCandidatesAsync_ReturnsEnabledUsersOnly**: With one user disabled, the candidates are the other
enabled users. This scenario is tested by `SigningService_ListCandidatesAsync_ReturnsEnabledUsersOnly`.

**SigningService_Constructor_NullArguments_Throw**: A null store, user service, or clock is rejected with an
`ArgumentNullException`. This scenario is tested by `SigningService_Constructor_NullArguments_Throw`.
