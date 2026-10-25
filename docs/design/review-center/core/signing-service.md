### SigningService

![Core Structure](CoreView.svg)

#### Purpose

`SigningService` owns the rules for reviewer slots and signatures: it adds, changes, and removes the slots of a review,
records a signature after the signer has entered the password again, and computes the approval status of a review. It
is the one place where these rules live, so every caller (the pages, a later API) gets the same answer. It is also
the home of the supporting types of signing: `SlotRole`, `ReviewerSlot`, `ReviewSignature`, `ReviewRecord`,
`ReviewApproval`, `SlotStatus`, `SlotMember`, and `SlotSignatureState`, together with the signing rules of
`DomainRule`. The service is a singleton, as `UserService` is.

#### Data Model

**ReviewerSlot**: a data class of `Id` (assigned by the store), `ProjectId`, `ReviewId`, `PrimaryUserId`, and
`AlternateUserId` (`null` when there is no alternate). A review has any number of slots, including none. Either the
primary or the alternate reviewer can sign the slot. A slot is addressed by its identity; the number shown to people
is its position in creation order.

**SlotRole**: a closed enumeration with the values `Primary` and `Alternate`. It is stored by name.

**ReviewSignature**: an immutable record of one signature: `Id`, `ProjectId`, `ReviewId`, `SlotId`, `UserId`,
`DisplayName` (copied when signing, so it stays readable if the user is renamed or disabled), `Role` (the role the
signer had in the slot), `SignedAt` (UTC), `RecordDigest`, and `Meaning`. Signatures are never changed or deleted.

**Meaning**: every signature carries the text of `SigningService.MeaningText`: "I have reviewed this record and
approve it."

**ReviewRecord**: the immutable snapshot of a review that the digest and the blockers are computed from: the content
fingerprint, the frozen checklist (or `null`), the section decisions, the answers, and the threads with their
comments. See `RecordDigest` for what the digest covers.

**ReviewApproval**: the computed status of a review: `IsApproved`, the current `Digest`, the `Slots` (one `SlotStatus`
each), the `Blockers`, and the `NextStep` sentence. A `SlotStatus` holds the slot identity, its number, the primary and
alternate as `SlotMember` (identity, user name, display name), the state, and, for a signed slot, the name of the
signer and the signing time. `SlotSignatureState` has the values `NotSigned`, `Signed`, and `OutOfDate`.

**Status is computed, never stored.** A slot is `Signed` when it has a signature whose digest equals the digest of the
record now, `OutOfDate` when it has signatures but none of them is current, and `NotSigned` otherwise. A review is
approved when it has at least one slot and every slot is `Signed`; a review without slots is "In review". A change to
the record (a new comment, a changed answer, a changed state) therefore makes the approval go away by itself, and
setting the record back restores it, because the signatures were never touched.

**Rules for slots**:

- The primary reviewer must be an enabled user. The alternate reviewer is optional; when given it must be an enabled
  user and a different one.
- A user occupies at most one slot of a review, as primary or as alternate. Otherwise one person could fill a review
  with their own approvals.
- Any signed-in user can add, change, and remove slots. A slot that has any signature, current or out of date, can
  no longer be changed or removed, because a signature must always point to a slot that still describes who signed.
  A change of reviewers after a signature is therefore a new slot.

**Blockers**: a review can be signed only when it has a checklist, every check of an applicable section is answered,
no check of an applicable section is answered Fail, and no thread is open. Otherwise the blockers are returned as
plain sentences, in this order: "This review has no checklist.", "{n} checklist check(s) is/are unanswered", "{n}
check(s) is/are answered Fail", and "{n} thread(s) is/are open". The checklist of a review is fixed when it is created.

**Next step**: "Approved. Every reviewer slot has a current signature." when the review is approved; otherwise, when
there are blockers, the sentence of `ThreadService.NextStep` for the threads, the checklist progress, and the
number of failed checks; otherwise "Add a reviewer slot." when there is no slot and "Ready for signatures." when there
is one.

**Signing** takes the actor (the signed-in user, never a form value), the slot, the digest the signer was looking at
(`expectedDigest`), and the password. It checks, in this order, and stops at the first failure:

1. The actor has an existing, enabled account (`ActorNotAllowed`).
2. The review and the slot exist (`ReviewNotFound`, `SlotNotFound`).
3. The actor is the primary or the alternate reviewer of the slot (`ActorNotAllowed`: "You are not a reviewer of this
   slot."). The role is taken from the seat.
4. The digest of the record now equals `expectedDigest` (`RecordChanged`: "The record changed while you were signing.
   Review it and try again."), so nobody signs something they have not seen.
5. The slot has no signature of the current digest yet (`SigningNotAllowed`).
6. There are no blockers (`SigningNotAllowed`, with the blockers in the message).
7. The password is right: it is checked by `UserService.AuthenticateAsync`, the path the sign-in uses, so the hashing,
   the lockout counting, and the generic failure are the same. Any failure (wrong, locked out, disabled, unknown) gives
   `ReauthenticationFailed` with the message "The password is incorrect." and signs nothing. A wrong password at a
   signature counts toward the same lockout as at sign-in.
8. The signature is stored by `IReviewStore.AddSignatureAsync`, which recomputes the digest inside a serializable
   transaction and refuses the signature when the record changed since step 4.

The checks of steps 2 to 6 come before the password is asked for, so a refused signing does not count as a failed
password attempt in the typical case. The password is never stored, logged, or put into a message.

**Serialization**: all operations that change slots or signatures run one at a time under one semaphore, which makes
the one-slot-per-user rule and the signed-slot rule free of races for a single instance, so the service is registered
as a singleton. The store transaction of step 8 covers changes of the record that do not pass through this service.

**DomainRule values** added for signing: `ReviewNotFound`, `SlotNotFound`, `SlotUserInvalid`, `SlotSigned`,
`ActorNotAllowed`, `SigningNotAllowed`, `ReauthenticationFailed`, and `RecordChanged`. `ActorNotAllowed` is the rule
that every change made by an actor is re-authorized by the service, whatever the caller showed.

**Interface additions of IReviewStore**: `GetRecordAsync`, `GetSnapshotAsync`, `ListSlotsAsync`, `AddSlotAsync`,
`UpdateSlotAsync`, `RemoveSlotAsync`, `ListSignaturesAsync`, and `AddSignatureAsync`. The store has no operation that
changes or deletes a signature. `UpdateSlotAsync` and `RemoveSlotAsync` return `false` when the slot is not found.

#### Key Methods

**GetApprovalAsync**: Computes the status of a review.

- *Parameters*: project identity, review identifier, and a cancellation token.
- *Returns*: `Task<ReviewApproval?>` — the status, or `null` when the review does not exist.
- *Postconditions*: Nothing is stored. The record, slots, and signatures come from one `GetSnapshotAsync` read, so a
  concurrent change cannot make the digest, the blockers, and the signature status describe different states.

**ListCandidatesAsync**: Lists the users that can be chosen for a slot.

- *Returns*: `Task<IReadOnlyList<SlotMember>>` — the enabled users ordered by display name and then user name;
  nothing secret is included.

**AddSlotAsync**: Adds a slot.

- *Parameters*: the actor, project identity, review identifier, the primary user identity, and the alternate user
  identity or `null`.
- *Returns*: `Task<ReviewerSlot>` — the stored slot.
- *Preconditions*: The actor is not null.
- *Postconditions*: Nothing is stored when a rule fails.

**UpdateSlotAsync**: Changes the primary and the alternate reviewer of a slot that has no signature.

- *Parameters*: the actor, project identity, review identifier, slot identity, and the new user identities.
- *Postconditions*: Nothing is stored when a rule fails.

**RemoveSlotAsync**: Removes a slot that has no signature.

- *Parameters*: the actor, project identity, review identifier, and slot identity.
- *Postconditions*: Nothing is removed when a rule fails.

**SignAsync**: Signs the review in a slot as described above.

- *Parameters*: the actor, project identity, review identifier, slot identity, the expected digest, the password, and
  a cancellation token.
- *Returns*: `Task<ReviewSignature>` — the stored signature.
- *Preconditions*: The actor and the expected digest are not null.
- *Postconditions*: Nothing is stored when a rule fails.

**Audit**: Adding, updating, and removing a slot record `slot.added`, `slot.updated`, and `slot.removed`, and signing
records `signature.added` with the slot, the role, and the record digest the reviewer signed. Each draft is passed to
the store and saved in the same transaction as the change, so a signature and its entry exist together or not at all.
Signing runs under the gate of this service; the audit gate of the `AuditAppender` is taken inside it (the lock order is
the service gates first, the appender gate last). This is also what protects the signature history: the table of
signatures can still be edited by someone with write access to the database, but the entry of each signature is in the
hash chain. See the `AuditChain` unit for what the chain does and does not prove.

#### Error Handling

A null actor, expected digest, or constructor argument raises `ArgumentNullException`. A violated rule raises
`DomainRuleException` with the rule value named above and a message for the user; nothing is stored then. The messages
never contain a password or a hash. An infrastructure failure of the store passes through unchanged.

#### Dependencies

- **IReviewStore** — reads the record, the slots, and the signatures, and stores slots and signatures.
- **UserService** — looks up users, lists them for the candidates, and re-authenticates the signer.
- **RecordDigest** — calculates the digest of the record.
- **ThreadService** — `NextStep` words the next step when signing is blocked.
- **ChecklistState** — decides which sections apply and what the answers are.
- **.NET runtime library** — `TimeProvider` for the signing time and `SemaphoreSlim` for the serialization.

#### Callers

- **ReviewPage** and **ProjectPage** (`Web` subsystem) — show the approval, the slots, and the blockers, and make the
  changes of the signed-in user.
- **ServerHost** (`Hosting` subsystem) — registers the service as a singleton.
