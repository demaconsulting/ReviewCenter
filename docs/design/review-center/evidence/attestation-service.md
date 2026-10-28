### AttestationService

![Evidence Structure](EvidenceView.svg)

#### Purpose

`AttestationService` releases the signed attestation of a review. Its single responsibility is the sequence that makes
the release safe: check that the actor may see the project and that the review is complete, get the key, build the
document from the record, sign it, and record
the release in the audit trail. It makes no decision about what a complete review is (that is `SigningService`), none
about who may see a project (that is `IProjectAccess`), and
no decision about how the document looks (that is `AttestationRenderer`).

#### Data Model

The service is stateless and thread-safe. It is created with the review store, the signing service, the project access
rules, the audit service,
the key provider, the renderer, the signer, and a clock. The result, `AttestationResult`, holds a status
(`Ok`, `NotFound`, `NotApproved`, `Changed`, or `KeyUnavailable`), the bytes of the signed PDF and a file name when the
status is `Ok`, and a message otherwise. Nothing is stored: the document is made for each request. The audit action is
`attestation.generated`. An internal `init` property, `AfterRender`, is a test seam: a step that runs between rendering
and the second check so that a test can change the review in that moment; it is `null` in production.

#### Key Methods

**GenerateAsync**: Creates the signed attestation of a review.

- *Parameters*: `int projectId`, `string reviewId`, `Actor actor` — the signed-in user who asks, and a cancellation
  token.
- *Returns*: `AttestationResult`.
- *Postconditions*: In this order: `IProjectAccess.CanViewAsync` is asked for the actor and the project, and an actor
  who may not view the project gets `NotFound` ("The review was not found.", the same as for a missing review), before
  anything is read or recorded; the approval is read from `SigningService`; a missing review gives `NotFound` and a
  review that is not complete gives `NotApproved`, in both cases without asking for the key. Then the key is requested;
  a failure gives `KeyUnavailable` and records nothing. The snapshot, the review, and the project are read (the first
  snapshot) and the digest of the record is computed again and compared with the approved one, and the slots of the
  approval are compared with the slots and signatures of the snapshot; a difference gives `Changed`. The audit chain is
  verified to get its head, and the document is rendered. Then the review is **read again and revalidated**: the
  approval must still be complete with the same digest, and the snapshot must have the same digest and the same slots,
  signers, and signature digests as the rendered one. If anything differs the result is `Changed` ("The review changed
  while the attestation was being prepared; try again."), nothing is signed, and **no audit event is written**. Only
  then is the document signed (a failure of the signer gives `KeyUnavailable` and records nothing). Last, an
  `attestation.generated` event is recorded with the actor, the project, and the review, and the detail `record digest
  {digest}, chain head {head}, pdf sha256 {hash}, key {thumbprint}, selfSigned={true or false}`; the digest in the
  event is the digest that was attested. The event is written strictly: when it cannot be written, the exception reaches
  the caller and no document is released.

**FileNameFor**: Gets the file name.

- *Parameters*: `int projectId`, `string reviewId`.
- *Returns*: `attestation-{projectId}-{reviewId}.pdf` in which every character other than a letter, a digit, a dot, an
  underscore, or a hyphen of the review identifier is replaced by a hyphen.

#### Error Handling

Expected situations are statuses and not exceptions, so that the endpoint maps each one to its answer. A null argument
raises `ArgumentNullException`. An audit failure is not caught: an attestation that cannot be traced is not released. The
event follows the signing, because it contains the hash of the signed document, so the chain head that is printed in
the document precedes the event of the document.

**Snapshots and the residual window**: The approval and the content are separate reads, and reading, verifying the
audit chain, and rendering take time, so a reviewer could add a slot or change the record in between. The service
therefore reads again after rendering and before signing (see `GenerateAsync`) and refuses with `Changed` when the
review is no longer complete or no longer the same. The check is not a lock: a change that happens after the second
read and before the signing and the audit event is not seen. That window is tiny (a signature computation and one
audit write, with no I/O of the review between them), and the audit event states the record digest that was attested,
so such a document can be recognized against the record afterwards. The second read compares the approval, the record
digest, and the slots with their counting signatures; it does not re-read the review, project, or repository names, so a
rename in that window would be rendered under the old name (the names are not part of the record digest, see the
renderer design). A transaction around the whole operation is not
used because the stores are separate and the cost is not justified for a rare operation.

**Cost of the audit check**: `AuditService.VerifyAsync` verifies the whole audit chain on every download. That is linear
in the number of audit entries and is done for every request. It is intentional: the document states whether the chain
is intact and prints its head, which is integrity evidence, so the head is never taken on trust. No other limit is
applied; the cost is accepted because an attestation is rare and the number of audit entries of a review server is
modest.

#### Dependencies

- **SigningService**, **AuditService**, **IReviewStore**, **IProjectAccess**, **RecordDigest**, **Actor**,
  **AuditDraft** — Core.
- **IAttestationKeyProvider**, **IAttestationRenderer**, **IAttestationSigner** — the other units of this subsystem.

#### Callers

- **AttestationEndpoint** — asks for the document of a download request.
