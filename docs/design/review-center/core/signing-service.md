### SigningService

![Core Structure](CoreView.svg)

#### Purpose

`SigningService` owns the rules for reviewer slots and signatures: it adds, changes, and removes the slots of a review,
records a signature after the signer has confirmed their identity (with the password and, for an account with a second
factor, a current code, or for a user of an external
identity provider with a fresh sign-in at the provider), and computes the approval status of a review. It
is the one place where these rules live, so every caller (the pages, a later API) gets the same answer. It also
enforces the integrity of an approved review: an approved review is locked, only an administrator can reopen it, and
(by default) the author of a review cannot review it. It is also
the home of the supporting types of signing: `SlotRole`, `ReviewerSlot`, `ReviewSignature`, `ReviewRecord`,
`ReviewApproval`, `SlotStatus`, `SlotMember`, `SlotSignatureState`, `ReviewLock`, `ReviewReopen`, and `ReopenRules`,
together with
the signing rules of `DomainRule`. `ReviewLock`, `ReviewReopen`, `ReopenRules`, and `SignatureImpact` live in the files
of this unit;
they are not units of their own. The service is a singleton, as `UserService` is.

#### Data Model

**ReviewerSlot**: a data class of `Id` (assigned by the store), `ProjectId`, `ReviewId`, `PrimaryUserId`, and
`AlternateUserId` (`null` when there is no alternate). A review has any number of slots, including none. Either the
primary or the alternate reviewer can sign the slot. A slot is addressed by its identity; the number shown to people
is its position in creation order, counted from 1, and that position is the "slot N" in the audit entries.

**SlotRole**: a closed enumeration with the values `Primary` and `Alternate`. It is stored by name.

**ReviewSignature**: an immutable record of one signature: `Id`, `ProjectId`, `ReviewId`, `SlotId`, `UserId`,
`DisplayName` (copied when signing, so it stays readable if the user is renamed or disabled), `Role` (the role the
signer had in the slot), `SignedAt` (UTC), `RecordDigest`, and `Meaning`. Signatures are never changed or deleted.

**Meaning**: every signature carries the text of `SigningService.MeaningText`: "I have reviewed this record and
approve it."

**ReviewRecord**: the immutable snapshot of a review that the digest and the blockers are computed from: the content
fingerprint, the frozen checklist (or `null`), the section decisions, the answers, and the threads with their
comments, `Reopens`, the number of times an administrator reopened the review (default `0`), and `FollowsReviewId`, the
review it follows (default null; carried rows are marked on the answers, section states, and threads themselves). See
`RecordDigest` for what the digest covers.

**ReviewReopen**: an immutable record of one reopen: `Id` (assigned by the store), `ProjectId`, `ReviewId`,
`ReopenedAt` (UTC), `ReopenedBy` (user name), `ReopenedByDisplayName` (copied, so it stays readable), `Reason`
(at most `ReviewLimits.MaxReopenReasonLength`, 250, characters), and `PreviousDigest` (the digest of the approved
record that was reopened). A reopen is history: it is never changed or deleted.

**ReviewApproval**: the computed status of a review: `IsApproved`, the current `Digest`, the `Slots` (one `SlotStatus`
each), the `Blockers`, the `NextStep` sentence, and `Reopens`, the reopen history, oldest first (`null` or empty for
a review that was never reopened; it is read only when the record's reopen count is above zero). `IsApproved` is
also the lock: there is no separate stored or computed "locked" flag. A `SlotStatus` holds the slot identity, its
number, the primary and alternate as `SlotMember` (identity, user name, display name, and `IsMember`), the state,
and, for a signed slot, the name
of the signer and the signing time. `IsMember` is `false` when the user is no longer a member of the project (a slot
keeps its assignee after a removal, so the status says who can no longer sign; step 1 of signing refuses such a user);
the candidates of `ListCandidatesAsync` are members by definition. A member whose role is `Auditor` is never a
reviewer: `AddSlotAsync` and `UpdateSlotAsync` refuse such a user as primary or alternate with `SlotUserInvalid`, and
signing refuses an Auditor who is still named in a slot with `ActorNotAllowed` (a role change to Auditor is refused
while the member holds open work, so this only defends against a race or old data). A placeholder for an unknown user
keeps the flag too.
`SlotSignatureState` has the values `NotSigned`, `Signed`, and `OutOfDate`.

**Status is computed, never stored.** A slot is `Signed` when it has a signature whose digest equals the digest of the
record now, `OutOfDate` when it has signatures but none of them is current, and `NotSigned` otherwise. A review is
approved when it has at least one slot and every slot is `Signed`; a review without slots is "In review". A change to
the record (a new comment, a changed answer, a changed state) therefore makes the approval go away by itself, and
setting the record back restores it, because the signatures were never touched. The one definition of "approved" is
`ReviewLock.IsApproved(record, slots, signatures)`: at least one slot, and every slot holds a signature whose digest
equals the current digest. `GetApprovalAsync`, the lock checks of all Core services, and the lock checks inside the
store transactions all call it, so they cannot disagree.

**Approval lock**: an approved review is locked, so that what the signatures cover cannot change silently. While the
review is approved, every change of reviewed content is refused with `ReviewLocked` ("This review is approved and
locked. A project administrator must reopen it before it can change."): starting, replying to, resolving, and reopening
a thread (`ThreadService`), a checklist answer and the applicability of a section (`ChecklistService`), and adding,
changing, and removing a slot and signing (this service). Locked is computed from the same snapshot as the approval,
never stored. The Core check is `ReviewLock.EnsureUnlockedAsync(store, projectId, reviewId)`; the store repeats the
check inside its write transactions (see `ReviewStore`), which closes the gap between the check and a concurrent final
signature. A review that does not exist is not locked, so the not-found rule of the caller applies unchanged. Reading,
the attestation, and the evidence index are not affected.

**Signature impact**: a change that is allowed can still make a current signature out of date, and the person who
makes it should know which signatures they withdraw. `SignatureImpact` (an internal helper in the files of this unit)
decides this for `ThreadService` and `ChecklistService`, from the snapshot that `ReviewLock.EnsureUnlockedAsync` has
already loaded (the method returns that snapshot, or `null` for a review that does not exist), so it costs no further
read and no trial write:

1. `CurrentSignatures` numbers the slots by their identity order (slot 1 is the first added, as the page shows them)
   and keeps, for each slot, its latest signature whose digest equals the digest of the record now. A slot without
   such a signature is not affected by anything.
2. `RequireAcknowledged(snapshot, changed, acknowledged)` returns when the review does not exist, when no signature is
   current, or when `changed` has the same `RecordDigest` as the current record. A new thread or comment passes
   `changed = null`, which always counts as a change. A checklist change passes the record as it would be after the
   change, so a save that changes nothing (the same person saving the same value again, or marking a section
   applicable that has no row) is not a change. The answer time is not part of the digest; the person who answered is.
3. Otherwise every current signature whose slot identity is not in `acknowledged` is affected. When there is one,
   `DomainRuleException` is thrown with `SignaturesWouldBecomeOutOfDate`, the message "This change makes signatures
   out of date: slot 1 (Rita), slot 3 (Tess). Acknowledge them to continue.", and `AffectedSlots`, a copy of the list of
   `AffectedSignature` (slot identity, slot number, signer display name, signer identity). The signer is the person who
   actually signed, which is the alternate when the alternate signed.

The caller passes the slot identities it acknowledges as the optional last argument `acknowledgedSlotIds` of the
service methods (`null` means none). A partial acknowledgement leaves the others in the exception, and an identity that
is not affected is ignored. The check sits after the lock check and after all validation of the request, and before
anything is stored, so `ReviewLocked` and validation errors win, and a refusal changes nothing. `DomainRule` gets the
one value `SignaturesWouldBecomeOutOfDate`, added at the end.

**Known limitation**: the check and the write are two steps without a common transaction. A signature that is added
between them is not named in the refusal and goes out of date silently. This is accepted: the signature does become out
of date, as the digest rule always does, so the record stays correct, and the warning is best effort. Closing the gap
would need the store to repeat the check inside its transaction.

**Behavior of the pages**: `ReviewPage` and `ChecklistPage` of the `Web` subsystem call the service without an
acknowledgement. When `SignaturesWouldBecomeOutOfDate` is raised they show a warning that names the slots and signers
of `AffectedSlots`, and only when the person confirms do they call the service again with those slot identities. A
signature that appeared in the meantime is named in the next refusal, and the page asks again with the merged list.
Cancelling stores nothing.

**Reopen**: `ReopenReviewAsync` is the only way out of the lock. It needs a project administrator or a system
administrator (anyone else: `ActorNotAllowed`; an API token is always refused) and a reason of 1 to 250 characters after
trimming (`ReopenReasonInvalid`). It works only on an approved review (`ReviewNotLocked`: "The review is not locked, so
there is nothing to reopen."). The reopen is stored as a `ReviewReopen` with the previous digest, and the number of reopens
becomes part of the record digest (see `RecordDigest`). Consequently the earlier signatures are kept as history but are
out of date and can never be current again, not even when the content is changed back; the review is "In review" and
needs fresh signatures from the reviewers (each slot signed before is `OutOfDate`, and a slot with an out-of-date
signature cannot be edited, as before).

**Independent reviewers**: when the project's `RequireIndependentReviewers` setting is on (the default; see
`ProjectService`), the author of the review cannot be the primary or the alternate reviewer of a slot
(`ReviewerNotIndependent` when a slot is added or changed: "{name} created this review and cannot be one of its
reviewers while independent reviewers are required.") and cannot sign it (`ReviewerNotIndependent`: "You created this
review, so you cannot be a reviewer of it. A different project member must review and sign it, or a project administrator
can switch off 'Require independent reviewers'."). The author is the `Review.CreatedBy` user name (compared ignoring case);
for a review created with an API token that is the token's owner, and a review without an author (created from the
command line) is not restricted. The signing check is repeated at signing time, so a slot that was filled while the
setting was off cannot be signed by the author once it is on. The rule is not retroactive: it runs when a reviewer
is chosen and when a review is signed, and existing signatures stay valid. Limitation: the authors of the git commits of
the reviewed files are not known to Review Center and are not matched; only the user who created the review in the tool
is.

**Reopener rule**: when the project's `RequireNonReopenerSigning` setting is on (the default, also for projects that
existed before the rule was enforced; see `ProjectService`), a person who reopened the review, now or earlier, cannot
sign it again (`ReopenerCannotSign`: "You reopened this review, so you cannot sign it again. Another reviewer must sign
it, ..."). `SignWithCredentialsAsync` checks it before the record check and before the password is asked for, like the
independence rule, so a refused call costs nothing and no attempt is counted. A review that was never reopened needs no
further read. Whoever reopened is matched by user name, ignoring case, in the reopen history.

**Stranded slots**: the reopener rule alone would let an administrator make a review impossible to approve, because a
reopen needs an approved review, so every slot has a signature and none of them can be changed afterwards. While the
rule is on, `ReopenReviewAsync` therefore refuses a reopen with `ReopenWouldStrandSlot` when a slot would have nobody
who may sign: "Slot 1 would have no one who may sign; ask another administrator or switch the rule off." The blocked
people are the administrator who reopens and the earlier reopening administrators; the pure helper
`ReopenRules.StrandedSlots` finds
the slots whose primary is blocked and whose alternate is missing or blocked, numbered as the review page shows them,
and `ReopenRules.StrandedMessage` words the refusal. Every slot counts, not only the ones that were signed by the
administrator. The check runs after the review is known to be locked and before anything is stored, so a refusal
records nothing and the review stays approved. Out of scope: an occupant that is disabled or no longer a member is not
considered, and the inbox can still list the review under "To sign" for a blocked reopener who shares a slot.

**Rules for slots**:

- The primary reviewer must be an enabled user who is a member of the project. The alternate reviewer is optional;
  when given it must be an enabled member of the project and a different user (`SlotUserInvalid`: "The primary/alternate
  reviewer must be an enabled member of the project."). The system administrator right does not make a user a
  candidate; only a membership does.
- A user occupies at most one slot of a review, as primary or as alternate. Otherwise one person could fill a review
  with their own approvals.
- Any actor who may work in the project can add, change, and remove slots: a member with the right to work, and also a
  system administrator who is not a member; the actor must have an enabled account (`ActorNotAllowed` otherwise), a
  read-only member (an auditor) is refused (`ActorNotAllowed`), and an actor who may not see
  the project is refused as if it did not exist (`ProjectNotFound`). Membership is not required to arrange the slots,
  only to be a reviewer in one (see the rule above) and to sign (step 1 of signing). A slot that has any signature,
  current or out of date, can
  no longer be changed or removed, because a signature must always point to a slot that still describes who signed.
  A change of reviewers after a signature is therefore a new slot. While the review is approved (locked), slots cannot
  be added, changed, or removed at all (`ReviewLocked`).

**Blockers**: a review can be signed only when it has a checklist, every check of an applicable section is answered,
no check of an applicable section is answered Fail, and no thread is open. Otherwise the blockers are returned as
plain sentences, in this order: "This review has no checklist.", "{n} checklist check(s) is/are unanswered", "{n}
check(s) is/are answered Fail", and "{n} thread(s) is/are open". The checklist of a review is fixed when it is created.
Carried answers (see `ReviewService`) count as answered and carried open threads count as open threads, so the
blockers need no extra rule: only a carried thread that is still open, never a carried Pass or N/A answer, blocks the
signature of a follow-up. A carried answer is not a Fail, because Fail answers are never carried.

**Next step**: "Approved. Every reviewer slot has a current signature." when the review is approved; otherwise, when
there are blockers, the sentence of `ThreadService.NextStep` for the threads, the checklist progress, and the
number of failed checks; otherwise "Add a reviewer slot." when there is no slot and "Ready for signatures." when there
is one.

**Signing** takes the actor (the signed-in user, never a form value), the slot, the digest the signer was looking at
(`expectedDigest`), and the password. It checks, in this order, and stops at the first failure:

1. The actor can work in the project (`ProjectNotFound` when it is not visible, `ActorNotAllowed` for a read-only
   member), has an existing, enabled account (`ActorNotAllowed`), and is a
   member of the project (`ActorNotAllowed`: "Only a member of the project can sign its reviews."). A system
   administrator who is not a member cannot sign.
2. The review and the slot exist (`ReviewNotFound`, `SlotNotFound`).
3. The actor is the primary or the alternate reviewer of the slot (`ActorNotAllowed`: "You are not a reviewer of this
   slot."). The role is taken from the seat.
   Then, when the project requires independent reviewers and the actor created the review, the signature is refused
   (`ReviewerNotIndependent`); and when the review is already approved it is locked and the signature is refused
   (`ReviewLocked`), which tells the signer why instead of "already signed".
4. The digest of the record now equals `expectedDigest` (`RecordChanged`: "The record changed while you were signing.
   Review it and try again."), so nobody signs something they have not seen.
5. The slot has no signature of the current digest yet (`SigningNotAllowed`).
6. There are no blockers (`SigningNotAllowed`, with the blockers in the message).
7. The password is right: it is checked by `UserService.ReauthenticatePasswordAsync`, which is the check the sign-in
   uses with its own action code, so the hashing, the lockout counting, and the generic failure are the same. Any
   failure (wrong, locked out, disabled, unknown) gives
   `ReauthenticationFailed` with the message "The password is incorrect." and signs nothing. A wrong password at a
   signature counts toward the same lockout as at sign-in, and `UserService` records the failure in the audit trail as
   `signature.reauth-failed` (a wrong password, a locked account, and so on, never the password), so the trail shows
   that the password was tried at a signature and not at sign-in. A signature attempt is not throttled by client
   address: the signer is already signed in, and the lockout applies. A user of an external identity provider has no
   password; for that user this step takes the proof that the sign-in callback issued for exactly this user, project,
   review, and slot from the `ReauthProofStore`, and spends it with `TryConsume`. When there is no live proof (or the
   service has no store of proofs) the step gives `ReauthenticationFailed` with the message "Your identity is not
   confirmed. Confirm it again, then sign.", `UserService.RecordExternalReauthFailureAsync` records
   `signature.reauth-failed` (detail `oidc: no-proof`), and nothing is signed.

   **Second factor**: a local signer whose account has a confirmed second factor (`TotpService.IsEnrolledAsync`) must
   also give the current authenticator code (`SignerCredentials.TotpCode`). The password is checked first, then the
   code through the internal `TotpService.VerifyForSignatureAsync`; a recovery code is never accepted here. A wrong
   password, a wrong
   code, or a code that was already used (also one used a moment ago to sign in: the signer waits for the next code)
   gives `ReauthenticationFailed` with the one message "The password or the code is incorrect." for a signer with a
   second factor, so the message never tells which of the two was wrong. A wrong code counts toward the lockout and is
   recorded as `signature.reauth-failed` (detail `wrong code`); a right code clears the counter. When the service has no
   `TotpService` a required second factor cannot be satisfied and the signature is refused (fail closed). A user of an
   external identity provider and a signer without a second factor are not affected, and the code is ignored for them.
8. The signature is stored by `IReviewStore.AddSignatureAsync`, which recomputes the digest inside a serializable
   transaction and refuses the signature when the record changed since step 4.

The checks of steps 2 to 6 come before the password is asked for, so a refused signing does not count as a failed
password attempt in the typical case. For the same reason an external user's proof is spent only in step 7: a refusal
in steps 1 to 6 (an API token, a changed record, a blocker) keeps it, so the person can correct the cause and sign
within its lifetime. The password check, the authenticator code, and the proof are spent in step 7 and not before: once
spent, they are gone, also when step 8 then refuses the signature with `RecordChanged` because the record changed
between step 4 and the store transaction. The password attempt has been counted (a right one clears the counter of
failures), the authenticator code is marked as used for its time step, and the proof is consumed, so the signer has to
start again, for an authenticator code with the next one. The password is never stored, logged, or put into a message.

**External confirmation without spending**: `HasReauthProofAsync` (actor, project identity, review identifier, slot
identity) answers whether an enabled external user has a live proof for exactly this slot, without spending it; it is
`false` for a local user, an API token, an unknown user, and a service without a store of proofs. A page uses it to show
"Identity confirmed". `RequireConfirmableAsync` (same arguments) checks, read-only, that the actor may be asked to
confirm for a slot: not an API token, able to work in the project, an enabled external account that is a member of the
project and a reviewer (primary or alternate) of the slot; it returns the stored user whose identity the confirmation
must match, and otherwise raises `ProjectNotFound`, `SlotNotFound`, or `ActorNotAllowed`. The sign-in endpoint calls it
before anything is sent to the provider, so no confirmation is asked for a signature the user could not make.

**Serialization**: all operations that change slots or signatures run one at a time under one semaphore, which makes
the one-slot-per-user rule and the signed-slot rule free of races for a single instance, so the service is registered
as a singleton. The store transaction of step 8 covers changes of the record that do not pass through this service.

**DomainRule values** added for signing: `ReviewNotFound`, `SlotNotFound`, `SlotUserInvalid`, `SlotSigned`,
`ActorNotAllowed`, `SigningNotAllowed`, `ReauthenticationFailed`, and `RecordChanged`. `ActorNotAllowed` is the rule
that every change made by an actor is re-authorized by the service, whatever the caller showed. The integrity rules add
`ReviewLocked`, `ReviewNotLocked`, `ReopenReasonInvalid`, and `ReviewerNotIndependent`. The impact check adds
`SignaturesWouldBecomeOutOfDate`.

**Interface additions of IReviewStore**: `GetRecordAsync`, `GetSnapshotAsync`, `ListSlotsAsync`, `AddSlotAsync`,
`UpdateSlotAsync`, `RemoveSlotAsync`, `ListSignaturesAsync`, `AddSignatureAsync`, `ReopenReviewAsync`, and
`ListReopensAsync`. `GetApprovalsAsync` also uses the batch reads `ListSnapshotsAsync` and the `ListReopensAsync`
overload for several reviews. The store has no operation that
changes or deletes a signature or a reopen. `UpdateSlotAsync` and `RemoveSlotAsync` return `false` when the slot is
not found.

#### Key Methods

**GetApprovalAsync**: Computes the status of a review.

- *Parameters*: project identity, review identifier, and a cancellation token.
- *Returns*: `Task<ReviewApproval?>` — the status, or `null` when the review does not exist.
- *Postconditions*: Nothing is stored. The record, slots, and signatures come from one `GetSnapshotAsync` read, so a
  concurrent change cannot make the digest, the blockers, and the signature status describe different states. The
  members of the project are read through `ListMembershipsAsync` to fill `SlotMember.IsMember`. The reopen history is
  read through `ListReopensAsync` only when the record's reopen count is above zero.
- *Blocker counts*: `ReviewApproval` also carries `OpenThreads`, `UnansweredChecks`, `FailedChecks`, and the flag
  `MissingChecklist`. They are counted in the one place that writes the blocker sentences (`BuildApproval`, one additive
  region), so each number equals the one in its sentence and the flag is true exactly when the "no checklist" sentence
  is present; the sentences are unchanged. `ReviewProgressService` and any later list of work use
  the counts and do not repeat the rule. `SlotMember.Enabled` tells whether the account of a person in a slot is enabled
  (true by default).

**GetApprovalsAsync**: Computes the status of several reviews of a project.

- *Parameters*: project identity, the review identifiers, and a cancellation token.
- *Returns*: `Task<IReadOnlyDictionary<string, ReviewApproval>>` — the status of each review that exists, by review
  identifier; a review that does not exist is absent. Each status equals the one `GetApprovalAsync` gives.
- *Postconditions*: The user list and the identities of the current project members are loaded once for the whole batch,
  not once for each review, and not at all when there are no review identifiers. The snapshots of all the reviews come
  from one `IReviewStore.ListSnapshotsAsync` call (a set-based read; no snapshot read per review), and the reopen history
  of the reviews whose record counts a reopen from one `ListReopensAsync` call for those reviews. The status of each
  review is built by the same private step (`BuildApproval`) as the single read, so the two cannot differ. A duplicated
  identifier is read once.

**ListCandidatesAsync**: Lists the users that can be chosen for a slot.

- *Parameters*: the actor, the project identity, and a cancellation token.
- *Preconditions*: The actor may work in the project (`ProjectNotFound` otherwise, `ActorNotAllowed` for a read-only
  member).
- *Returns*: `Task<IReadOnlyList<SlotMember>>` — the enabled members of the project, except members whose role is
  `Auditor` (they cannot sign, so they are never offered), ordered by display name and then
  user name; nothing secret is included.

**AddSlotAsync**: Adds a slot.

- *Parameters*: the actor, project identity, review identifier, the primary user identity, and the alternate user
  identity or `null`.
- *Returns*: `Task<ReviewerSlot>` — the stored slot.
- *Preconditions*: The actor is not null.
- *Postconditions*: Nothing is stored when a rule fails. The review must not be locked (`ReviewLocked`), and neither
  chosen user may be the author while independent reviewers are required (`ReviewerNotIndependent`).

**UpdateSlotAsync**: Changes the primary and the alternate reviewer of a slot that has no signature.

- *Parameters*: the actor, project identity, review identifier, slot identity, and the new user identities.
- *Postconditions*: Nothing is stored when a rule fails; the lock and independence rules of `AddSlotAsync` apply.

**RemoveSlotAsync**: Removes a slot that has no signature.

- *Parameters*: the actor, project identity, review identifier, and slot identity.
- *Postconditions*: Nothing is removed when a rule fails; a locked review refuses it (`ReviewLocked`).

**ReopenReviewAsync**: Reopens an approved (locked) review.

- *Parameters*: the actor, project identity, review identifier, the reason, and a cancellation token.
- *Preconditions*: The actor and the review identifier are not null. The actor is not an API token, has an enabled
  account, and administers the project (project administrator or system administrator); the reason, trimmed, has 1 to
  250 characters.
- *Algorithm*: under the gate, the checks above (`ActorNotAllowed`, `ProjectNotFound`, `ReopenReasonInvalid`), then
  `ReviewNotFound` or `ReviewNotLocked` from one `GetSnapshotAsync` read, then `IReviewStore.ReopenReviewAsync` with a
  `ReviewReopen` (previous digest, the actor's user name and display name, `TimeProvider` time) and the audit draft
  `review.reopened`. When the project does not let a reopener sign again, the stranded-slot check (see "Stranded slots")
  runs after the snapshot read and before the store call (`ReopenWouldStrandSlot`).
- *Postconditions*: Nothing is stored when a rule fails. After success the record's reopen count is one higher, so the
  digest differs, the review is "In review", and the earlier signatures are out of date.

**SignAsync**: Signs the review in a slot as described above. A signer with a second factor uses
`SignWithCredentialsAsync`, which has the same rules and takes the password and the code together.

- *Parameters*: the actor, project identity, review identifier, slot identity, the expected digest, the password
  (ignored for a user of an external identity provider), and a cancellation token.
- *Returns*: `Task<ReviewSignature>` — the stored signature.
- *Preconditions*: The actor and the expected digest are not null.
- *Postconditions*: Nothing is stored when a rule fails.

**SignWithCredentialsAsync**: Signs the review in a slot, taking what the signer typed as a `SignerCredentials` (the
password, and the optional current authenticator code `TotpCode`, which is only used for a signer who has a second
factor; never stored, logged, or put into a message). `SignAsync` calls it with a password only.

- *Parameters*: the actor, project identity, review identifier, slot identity, the expected digest, the credentials,
  and a cancellation token.
- *Returns*: `Task<ReviewSignature>` — the stored signature.
- *Preconditions*: The actor, the expected digest and the credentials are not null.
- *Postconditions*: Nothing is stored when a rule fails.

**API tokens cannot sign**: The first statement of `SignAsync`, `AddSlotAsync`, `UpdateSlotAsync`,
`RemoveSlotAsync`, and `ReopenReviewAsync` refuses an actor that came in with a personal access token
(`Actor.ViaApiToken`) with
`ActorNotAllowed` and the message that reviewer slots and signatures cannot be changed with an API token. The check comes
before the gate, the password check, and any read of the store, so a refused call has no effect and costs nothing. A
signature records that a named person confirmed the review with the password, which a script holding a token cannot do.
The API has no route for these operations, so this check is the second, independent line of defense: it protects against
a route that is added later without the rule. Reading the approval is not restricted.

**Audit**: Adding, updating, and removing a slot record `slot.added`, `slot.updated`, and `slot.removed`, and signing
records `signature.added` with the slot, the role, and the record digest the reviewer signed. The slot is named by its
number on the review page (its position in creation order, counted from 1, so after a removal the numbers of the later
slots move down by one, as on the page), never by its database identifier: a slot is added as
`slots.Count + 1`, and a changed, removed, or signed slot gets the count of the slots with a smaller identity plus one,
computed before a removal. A reopen records
`review.reopened` with the previous record digest and the reason (`previous digest {hex}; reason: {reason}`; the reason
limit of 250 characters keeps it within the audit detail limit). Each draft is passed to
the store and saved in the same transaction as the change, so a signature and its entry exist together or not at all.
That atomicity is a property of the store (`ReviewStore`), not of this unit: this unit's part is to build the draft and
pass it in the same store call as the change, and never to append it afterwards, which the unit tests check with a store
that records the drafts it is given; the all-or-nothing behavior of the real transaction is verified with the tests of
`ReviewStore`.
Signing runs under the gate of this service; the audit gate of the `AuditAppender` is taken inside it (the lock order is
the service gates first, the appender gate last). This is also what protects the signature history: the table of
signatures can still be edited by someone with write access to the database, but the entry of each signature is in the
hash chain. See the `AuditChain` unit for what the chain does and does not prove.

#### Error Handling

A null actor, expected digest, or constructor argument raises `ArgumentNullException`. A violated rule raises
`DomainRuleException` with the rule value named above and a message for the user; nothing is stored then. The messages
never contain a password or a hash. An infrastructure failure of the store passes through unchanged.

#### Dependencies

- **IReviewStore** — reads the record, the slots, the signatures, the review, the project, and the reopen history, and
  stores slots, signatures, and reopens.
- **IProjectAccess** — requires that the actor may work in the project, that the signer and the reviewers are members, and
  (for a reopen) that the actor administers the project.
- **UserService** — looks up users, describes the reviewers, re-authenticates the signer (`ReauthenticatePasswordAsync`),
  and records a refused confirmation of an external user.
- **ReauthProofStore** — holds the single-use proofs of external users (optional, injected; without it an external
  user cannot sign).
- **TotpService** — tells whether the signer has a second factor and checks the code (optional, injected; without it a
  signer who needs a code cannot sign).
- **RecordDigest** — calculates the digest of the record.
- **ReviewLock**, **ReviewReopen**, **ReopenRules**, and **SignatureImpact** — the helpers and the history record of
  this unit (see above).
- **ThreadService** — `NextStep` words the next step when signing is blocked.
- **ChecklistState** — decides which sections apply and what the answers are.
- **.NET runtime library** — `TimeProvider` for the signing time and `SemaphoreSlim` for the serialization.

#### Callers

- **ReviewPage** and **ProjectPage** (`Web` subsystem) — show the approval, the slots, and the blockers, and make the
  changes of the signed-in user; the review page also asks `HasReauthProofAsync` for an external user and offers the
  reopen to an administrator.
- **ThreadService** and **ChecklistService** (`Core` subsystem) — call `ReviewLock.EnsureUnlockedAsync` before they
  change a thread or the checklist, and `SignatureImpact.RequireAcknowledged` with the snapshot it returns.
- **ReviewStore** (`Storage` subsystem) — calls `ReviewLock.IsApproved` inside its write transactions.
- **AttestationService** (`Evidence` subsystem) — reads the approval, including the reopen history.
- **ReviewWorkflowCheck** (`SelfTest` subsystem) — signs, shows the lock, and reopens.
- **OidcSignIn** (`Identity` subsystem) — asks `RequireConfirmableAsync` before it starts a confirmation at the provider.
- **ApiEndpoints** (`Api` subsystem) — reads the approval of a review; it cannot change slots or signatures.
- **ServerHost** (`Hosting` subsystem) — registers the service as a singleton.
