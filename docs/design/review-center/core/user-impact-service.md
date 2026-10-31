### UserImpactService

![Core Structure](CoreView.svg)

#### Purpose

`UserImpactService` answers one question before a person leaves: what would be left behind? For a user it reports the
reviewer slots of unlocked reviews that still wait for the user, and the projects in which the user is the only enabled
project administrator. A lead can use the answer to hand the work over and to name a second administrator before the
account is disabled. The unit computes the answer on demand from what is stored and keeps no state, so it cannot
disagree with the inbox, the review page, or the rule that protects the last administrator. The unit also holds the
records that carry the answer: `UserImpact`, `UserOpenWork`, `SoleAdminProject`, and the store row `UserReviewSlot`.

The service has no user-visible surface of its own. It is consumed by the Users page (the disable confirmation) and, in
a later slice, by the Members card of the project page, which show the result to the person who asks.

#### Data Model

**UserImpact**: `OpenWork` (a list of `UserOpenWork`), `SoleAdminProjects` (a list of `SoleAdminProject`), and
`IsEmpty`, which is true when both lists are empty.

**UserOpenWork**: `ProjectId`, `ProjectName`, `ReviewId`, `Title`, `SlotNumber` (the 1-based position of the slot among
the slots of the review, in creation order, the number the review page shows), `Role` (`SlotRole.Primary` or
`SlotRole.Alternate`), `SlotState` (`SlotSignatureState.NotSigned` or `OutOfDate`), and the convenience flags
`IsPrimary` and `IsAlternate`. A slot with a current signature is never listed, so `Signed` does not occur.

**SoleAdminProject**: `ProjectId` and `ProjectName`.

**UserReviewSlot**: the row the store returns for a slot of the user: `ProjectId`, `ProjectName`, `ReviewId`, `Title`,
`SlotId`, and `Role`. It carries no signature information, because whether a slot is open depends on the record digest
of the review, which only the service computes.

The service holds the injected collaborators and no other state, so it is safe for concurrent use when they are.

#### Key Methods

**GetImpactAsync**: Gets the impact of a user as seen by an actor.

- *Parameters*: the `Actor` (not null), the identity of the user, and a cancellation token.
- *Returns*: the `UserImpact`; both lists are empty for a user without open work who is nobody's only administrator.
- *Preconditions*: the actor is a system administrator, or administers at least one project. A null actor raises
  `ArgumentNullException`.
- *Postconditions*: nothing is changed and nothing is recorded.
- *Algorithm*: First decide the scope: a system administrator has none (every project), any other actor gets the set of
  projects it may see and administer (`IProjectAccess.ListVisibleProjectsAsync` and `CanAdministerAsync`); an empty set
  is refused. Then check that the user exists (`IUserStore.ListAsync`). Then read the open work and the sole
  administrator projects and keep only those of the scope.

**Open work**: The store lists the slots of the user (`IReviewStore.ListSlotsOfUserAsync`), as primary or as alternate.
For each project that has such slots, one batch read of the snapshots of those reviews
(`IReviewStore.ListSnapshotsAsync`) gives the record, the slots, and the signatures, so the number of reads does not
grow with the number of reviews. A slot is open work unless its review is concluded (`ReviewLock.IsConcluded`, so a
locked review is never listed and an unlocked one is, also a fully signed one that has an open thread) or the slot has
a current signature. The state of the slot is
decided by `InboxRules.SlotState`, the same rule that the inbox uses for "to sign" and "sign again", against the digest
of the record as it is now (`RecordDigest.Compute`). There is no second rule for "signed" or "out of date". The result
is ordered by project name, project identity, review identifier, and slot number.

**Sole administrator projects**: The store lists the projects in which the user is an enabled project administrator and
no other enabled project administrator exists (`IReviewStore.ListSoleAdminProjectsAsync`). Only a project
administrator counts. A system administrator who is not a member of the project does not count, a disabled account does
not count, and a disabled user is never reported as the sole administrator, because disabling again changes nothing.
This is the rule of the last-administrator check of `ReviewStore` (`EnsureAnotherAdminAsync`), so a project in the list
is exactly one where removing or demoting the user would be refused.

**Who may ask**: A system administrator may ask about any user and sees every project. A project administrator may ask
about any user, but the answer is limited to the projects the asker administers, so work in other projects is not
revealed. Every other actor (a member, a read-only member, a user outside every project, a disabled user, an unknown
user, and the command line) is refused with `ActorNotAllowed` before the existence of the user is checked, so a refused
actor learns nothing about whether the user exists. A project administrator whose account is disabled has no rights and
is refused.

**Disabled users**: The impact of a user whose account is already disabled is still given, because the work of a leaver
is often found after the account was disabled. A disabled user is never a sole administrator (see above).

**Access tokens**: Disabling a user also ends the use of their personal access tokens. This is not done by this unit.
`PersonalAccessTokenService.Authenticate` refuses the token of a disabled owner with `TokenFailure.OwnerDisabled`,
which is verified by `PersonalAccessTokenService_Authenticate_DisabledOrLockedOwner_IsRejected`; the offboarding steps
can rely on
it.

#### Error Handling

`DomainRuleException` with `ActorNotAllowed` when the actor is neither a system administrator nor an administrator of
a project, and with `UserNotFound` when the user does not exist. Constructing the service with a null argument, or
calling it with a null actor, raises `ArgumentNullException`. Failures of the stores propagate unchanged.

#### Known Limitations

- The impact is a snapshot of several reads that are not one transaction. A signature given between the reads can make a
  slot appear open for a moment; the next read is consistent.
- The user is found by listing the users, because the user store has no read by identity; the list is small in the
  intended use (a few hundred users).
- The slot indexes `IX_ReviewerSlots_PrimaryUserId` and `IX_ReviewerSlots_AlternateUserId` that make the slot read cheap
  already exist in `ReviewCenterDbContext` (they serve the inbox); this unit adds none.

#### Dependencies

- **IReviewStore** — the slots of the user, the projects the user alone administers, and the snapshots of the reviews
  (injected).
- **IUserStore** — finds out whether the user exists (injected).
- **IProjectAccess** — decides whether the actor is a system administrator or administers a project (injected).
- **InboxRules**, **ReviewLock**, and **RecordDigest** — the signature state and the approval rule (used directly).

#### Callers

- **ServerHost** — registers the service as a single instance.
- The Users page calls it for the disable confirmation; the Members card of the project page calls it in a later
  slice.
