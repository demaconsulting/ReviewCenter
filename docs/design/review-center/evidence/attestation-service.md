### AttestationService

![Evidence Structure](EvidenceView.svg)

#### Purpose

`AttestationService` releases the signed attestation of a review. Its single responsibility is the sequence that makes
the release safe: check that the asker may see the project (or that the project allows anonymous PDFs) and that the
review is complete, get the key, build the
document from the record under the evidence settings of the project, sign it, and record
the release in the audit trail. It makes no decision about what a complete review is (that is `SigningService`), none
about who may see a project (that is `IProjectAccess`), none about which settings a project has (that is
`EvidenceSettingsService` and the project), and
no decision about how the document looks (that is `AttestationRenderer`).

#### Data Model

The service is stateless and thread-safe. It is created with the review store, the signing service, the project access
rules, the audit service,
the key provider, the renderer, the signer, and a clock. The result, `AttestationResult`, holds a status
(`Ok`, `NotFound`, `NotApproved`, `Changed`, or `KeyUnavailable`), the bytes of the signed PDF and a file name when the
status is `Ok`, and a message otherwise. Nothing is stored: the document is made for each request. The audit action is
`attestation.generated`, and the actor text of an anonymous request is the constant `anonymous`. An internal `init`
property, `AfterRender`, is a test seam: a step that runs between rendering
and the second check so that a test can change the review in that moment; it is `null` in production.

An inner class, `PersonLabels`, writes every person that the document names. With the reviewer names switch on it
writes the display name (and the user name where the document showed it before); with the switch off it writes labels
that keep the roles visible: `Reviewer N (primary)` and `Reviewer N (alternate)` for the members of slot N (the
signer of the slot gets the label of the member who signed), `Participant N` for anybody else, numbered in the order in
which they are first named, and `a signed-in user` for who asked. A person is recognized by user name, or by display
name when only that is known. Only the structured name fields (the author of a comment, who answered a check, the
signer, the slot members, who reopened the review, who asked) are replaced; names that people typed into a comment
or a justification are the author's own text and stay.

The document also carries what a follow-up review took over: `FollowsReviewId` and the two counts from the review
(`CarriedAnswerCount`, `CarriedThreadCount`), and for every answer, not-applicable section, and listed thread the review
it was carried from (`CarriedFromReviewId` of the record rows). They are all absent or zero for a review that follows
nothing, and then the document is the same as before. The review identifiers are not personal data, so the reviewer
names switch does not affect them.

The document also carries the reopen history of the approval (`ReviewApproval.Reopens`, read by `SigningService`): for
every time an administrator reopened the review after an approval, when, by whom (written as "display name (user name)"
with the reviewer names switch on and as a participant label with it off, like every other person), the reason, and the
digest of the record that was reopened. The history is empty for a review that was never reopened, and then the
document is the same as before. It is information about the history of the record; the signatures in the document are
still only those of the current record, so a signature of an earlier record never appears as a sign-off. A reopened
review is not complete until it is signed again, so `GenerateAsync` gives `NotApproved` (or `NotFound` to an anonymous
request) in that time.

#### Key Methods

**GenerateAsync**: Creates the signed attestation of a review.

- *Parameters*: `int projectId`, `string reviewId`, `Actor? actor` — the user who asks, or `null` for a request without
  signing in, and a cancellation token.
- *Returns*: `AttestationResult`.
- *Postconditions*: In this order: the project is read, and the asker is checked. An actor must be allowed by
  `IProjectAccess.CanViewAsync`; a request without an actor is allowed only when the project has
  `AnonymousAttestations` on. A project that does not exist, an actor who may not view the project, and an anonymous
  request to a project that does not allow it all get `NotFound` ("The review was not found.", the same as for a
  missing review), before
  anything else is read or recorded. The evidence settings are taken from the project at this point and used for the
  whole call. The approval is read from `SigningService`; a missing
  review gives `NotFound` and a
  review that is not complete gives `NotApproved`, in both cases without asking for the key. A request without an actor
  gets `NotFound` ("The review was not found.", the same as for a missing review) for a review that is not complete, so
  that an anonymous caller cannot list review identifiers or their state; `NotApproved` is for an actor. Then the key is
  requested;
  a failure gives `KeyUnavailable` and records nothing. The snapshot and the review are read (the first
  snapshot) and the digest of the record is computed again and compared with the approved one, and the slots of the
  approval are compared with the slots and signatures of the snapshot; a difference gives `Changed`. The audit chain is
  verified to get its head, and the document is rendered from the record, with the settings, the labels for the people,
  and the actor (or the statement that the request was anonymous). Then the project settings and the review are **read
  again and revalidated**: the settings must be equal to the ones used, the
  approval must still be complete with the same digest, and the snapshot must have the same digest and the same slots,
  signers, and signature digests as the rendered one. If anything differs the result is `Changed` ("The review changed
  while the attestation was being prepared; try again."), nothing is signed, and **no audit event is written**. Only
  then is the document signed (a failure of the signer gives `KeyUnavailable` and records nothing). Last, an
  `attestation.generated` event is recorded with the actor (the actor text `anonymous` and no user for a request
  without an actor), the project, and the review, and the detail `record digest
  {digest}, chain head {head}, pdf sha256 {hash}, key {thumbprint}, selfSigned={true or false}`; the digest in the
  event is the digest that was attested. The event is written strictly: when it cannot be written, the exception reaches
  the caller and no document is released.

**FileNameFor**: Gets the file name.

- *Parameters*: `int projectId`, `string reviewId`.
- *Returns*: `attestation-{projectId}-{reviewId}.pdf` in which every character other than a letter, a digit, a dot, an
  underscore, or a hyphen of the review identifier is replaced by a hyphen.

#### Error Handling

Expected situations are statuses and not exceptions, so that the endpoint maps each one to its answer. A null argument
raises `ArgumentNullException`; a null actor is not an error (it is an anonymous request). An audit failure is not
caught: an attestation that cannot be traced is not released. The event follows the signing, because it contains the
hash of the signed document, so the chain head that is printed in the document precedes the event of the document.

**Snapshots and the residual window**: The approval and the content are separate reads, and reading, verifying the
audit chain, and rendering take time, so a reviewer could add a slot or change the record in between. The service
therefore reads again after rendering and before signing (see `GenerateAsync`) and refuses with `Changed` when the
review is no longer complete or no longer the same. The check is not a lock: a change that happens after the second
read and before the signing and the audit event is not seen. That window is tiny (a signature computation and one
audit write, with no I/O of the review between them), and the audit event states the record digest that was attested,
so such a document can be recognized against the record afterwards. The second read compares the approval, the record
digest, and the slots with their counting signatures, and the evidence settings of the project; it does not compare
the review, project, or repository names, so a rename in that window would be rendered under the old name (the names
are not part of the record digest, see the renderer design). A change of the settings inside the window is the one
case that is seen, because a document that mixed two settings would state a content scope that was never true. A
transaction around the whole operation is not
used because the stores are separate and the cost is not justified for a rare operation.

**Cost of the audit check**: `AuditService.VerifyAsync` verifies the whole audit chain on every download. That is linear
in the number of audit entries and is done for every request. It is intentional: the document states whether the chain
is intact and prints its head, which is integrity evidence, so the head is never taken on trust. No other limit is
applied; the cost is accepted because an attestation is rare and the number of audit entries of a review server is
modest.

#### Dependencies

- **SigningService**, **AuditService**, **IReviewStore**, **IProjectAccess**, **RecordDigest**, **Actor**,
  **AuditDraft**, **Project**, **EvidenceSettings** — Core. The settings are those of the project record, which the
  store reads together with the project.
- **IAttestationKeyProvider**, **IAttestationRenderer**, **IAttestationSigner** — the other units of this subsystem.

#### Callers

- **AttestationEndpoint** — asks for the document of a download request.
- **ApiEndpoints** — asks for the document of an API request, with an actor or, without a token, with none.
