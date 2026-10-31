### UserImpactService

#### Verification Approach

`UserImpactService` is verified with unit tests defined in `UserImpactServiceTests.cs`. The tests use the real
`UserImpactService`, `UserService`, `ReviewService`, `ChecklistService`, `ThreadService`, `SigningService`, and
`ProjectAccess` over the in-memory fakes `FakeReviewStore` and `FakeUserStore`, with a movable clock, through the shared
test world `InboxTestWorld` (the same world as the inbox tests). The world has the project Apollo with the users `ann`,
`bob`, `cyd`, `dee`, `eve`, and `author`, a read-only `aud`, a
user `out` outside every project, and a system administrator `root`. Each test adds a second project, Borealis, and the
project administrators `adm_a` (Apollo) and `adm_b` (Borealis). Slots are signed through the real signing rule, and a
failed check changes the record to make a signature out of date. The two store reads behind the unit are verified
against a real database in the `ReviewStore` unit (`ReviewStoreUserImpactTests.cs`), and the slot indexes in
`DatabaseSetupTests.cs`. The rule that the token of a disabled user stops working, which the offboarding steps rely on,
is verified in the `PersonalAccessTokenService` unit by
`PersonalAccessTokenService_Authenticate_DisabledOrLockedOwner_IsRejected`.

#### Test Environment

N/A - standard test environment.

#### Acceptance Criteria

- All unit tests pass with zero failures.
- The slots of a user as primary and as alternate are listed with the project, the review, the title, the number of the
  slot, and the role; a slot with a current signature and a slot of an approved review are not listed; an unlocked
  review is; a signature that no longer covers the record gives the state out of date; the work of a disabled user is
  still listed.
- The open work is ordered by project, review, and slot, and costs one batch read of the snapshots for each project and
  no single read.
- A project administrator who is the only enabled one is reported; another enabled administrator, or a second
  administrator who is disabled, changes the answer as the rule says; a disabled user is not reported.
- A system administrator sees every project; a project administrator sees only the projects the asker administers; a
  member, a read-only member, a user outside every project, an unknown actor, the command line, and a disabled project
  administrator are refused with `ActorNotAllowed`; an unknown target user is `UserNotFound` for an administrator and
  the refusal comes first for everyone else.
- A null actor and a null collaborator are refused with an argument exception.

#### Test Scenarios

**UserImpactService_GetImpactAsync_PrimaryAndAlternateSlots_AreOpenWork**: Slot 1 has `ann` as primary and `bob` as
alternate, slot 2 has `cyd`. Each of the three finds the review with the number and the role of the slot, the title, and
the project name, with the state not signed. This scenario is tested by
`UserImpactService_GetImpactAsync_PrimaryAndAlternateSlots_AreOpenWork`.

**UserImpactService_GetImpactAsync_SignedSlot_IsNotOpenWork**: The first slot is signed after the checklist was
answered; neither its primary nor its alternate has open work, and the reviewer of the unsigned slot still has. This
scenario is tested by `UserImpactService_GetImpactAsync_SignedSlot_IsNotOpenWork`.

**UserImpactService_GetImpactAsync_OutOfDateSlot_IsOpenWorkAsOutOfDate**: A signature is given and a check is then
failed, which changes the record; the slot is listed with the state out of date. This scenario is tested by
`UserImpactService_GetImpactAsync_OutOfDateSlot_IsOpenWorkAsOutOfDate`.

**UserImpactService_GetImpactAsync_ApprovedReviewExcluded_UnlockedReviewIncluded**: One review is approved by `ann`'s
signature and another is not; only the second is listed. This scenario is tested by
`UserImpactService_GetImpactAsync_ApprovedReviewExcluded_UnlockedReviewIncluded`.

**UserImpactService_GetImpactAsync_ManyReviews_AreOrderedAndReadInOneBatchPerProject**: Two reviews in Apollo, created
in reverse order, and one in Borealis wait for `ann`. The list is Apollo/r1, Apollo/r2, Borealis/b1, and the store counted
two batch reads and no single read. This scenario is tested by
`UserImpactService_GetImpactAsync_ManyReviews_AreOrderedAndReadInOneBatchPerProject`.

**UserImpactService_GetImpactAsync_DisabledUserWithSlot_StillShowsOpenWork**: A user with a slot is disabled and the
work is still listed; a user without work has an empty impact. This scenario is tested by
`UserImpactService_GetImpactAsync_DisabledUserWithSlot_StillShowsOpenWork`.

**UserImpactService_GetImpactAsync_OnlyEnabledProjectAdministrator_IsSoleAdmin**: `adm_a` is the only administrator of
Apollo and the system administrator `root`, who is no member, does not count; the project is reported. This scenario is
tested by `UserImpactService_GetImpactAsync_OnlyEnabledProjectAdministrator_IsSoleAdmin`.

**UserImpactService_GetImpactAsync_AnotherEnabledAdministrator_IsNotSoleAdmin**: A second enabled administrator is added
to Apollo; neither is the sole one. This scenario is tested by
`UserImpactService_GetImpactAsync_AnotherEnabledAdministrator_IsNotSoleAdmin`.

**UserImpactService_GetImpactAsync_DisabledSecondAdministrator_DoesNotCount**: The second administrator is disabled;
`adm_a` is the sole one again, and the disabled one is not reported. This scenario is tested by
`UserImpactService_GetImpactAsync_DisabledSecondAdministrator_DoesNotCount`.

**UserImpactService_GetImpactAsync_SystemAdministrator_SeesEveryProject**: `root` asks about `ann`, who waits in both
projects, and about `adm_b`; the work of both projects and the sole administrator project Borealis are listed. This
scenario is tested by `UserImpactService_GetImpactAsync_SystemAdministrator_SeesEveryProject`.

**UserImpactService_GetImpactAsync_ProjectAdministrator_SeesOnlyOwnProjects**: `adm_a` and `adm_b` ask about `ann`; each
sees only the work of their own project. `adm_a` asking about `adm_b` sees nothing, and `adm_b` sees its own sole
administrator project. This scenario is tested by
`UserImpactService_GetImpactAsync_ProjectAdministrator_SeesOnlyOwnProjects`.

**UserImpactService_GetImpactAsync_ActorWithoutAdministration_IsRefused**: A theory over a member, a read-only member, a
user outside every project, an unknown user, and the command line; each gets `ActorNotAllowed`. This scenario is tested
by `UserImpactService_GetImpactAsync_ActorWithoutAdministration_IsRefused`.

**UserImpactService_GetImpactAsync_DisabledProjectAdministrator_IsRefused**: A project administrator whose account is
disabled gets `ActorNotAllowed`. This scenario is tested by
`UserImpactService_GetImpactAsync_DisabledProjectAdministrator_IsRefused`.

**UserImpactService_GetImpactAsync_UnknownUser_IsNotFoundForAnAdministratorOnly**: An unknown identity gives
`UserNotFound` to a system administrator and `ActorNotAllowed` to a member. This scenario is tested by
`UserImpactService_GetImpactAsync_UnknownUser_IsNotFoundForAnAdministratorOnly`.

**UserImpactService_NullArguments_AreRefused**: A null actor, review store, user store, or access rule each raise
`ArgumentNullException`. This scenario is tested by `UserImpactService_NullArguments_AreRefused`.
