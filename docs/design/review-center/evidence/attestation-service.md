### AttestationService

![Evidence Structure](EvidenceView.svg)

#### Purpose

`AttestationService` releases the signed attestation of a review. Its single responsibility is the sequence that makes
the release safe: check that the review is complete, get the key, build the document from the record, sign it, and record
the release in the audit trail. It makes no decision about what a complete review is (that is `SigningService`) and
no decision about how the document looks (that is `AttestationRenderer`).

#### Data Model

The service is stateless and thread-safe. It is created with the review store, the signing service, the audit service,
the key provider, the renderer, the signer, and a clock. The result, `AttestationResult`, holds a status
(`Ok`, `NotFound`, `NotApproved`, or `KeyUnavailable`), the bytes of the signed PDF and a file name when the status is
`Ok`, and a message otherwise. Nothing is stored: the document is made for each request. The audit action is
`attestation.generated`.

#### Key Methods

**GenerateAsync**: Creates the signed attestation of a review.

- *Parameters*: `int projectId`, `string reviewId`, `Actor actor` — the signed-in user who asks, and a cancellation
  token.
- *Returns*: `AttestationResult`.
- *Postconditions*: In this order: the approval is read from `SigningService`; a missing review gives `NotFound` and a
  review that is not complete gives `NotApproved`, in both cases without asking for the key. Then the key is requested;
  a failure gives `KeyUnavailable` and records nothing. The snapshot, the review, and the project are read and the
  digest of the record is computed again and compared with the approved one, so a record that changed in between gives
  `NotApproved`. The audit chain is verified to get its head. The document is rendered and signed (a failure of the signer
  gives `KeyUnavailable` and records nothing). Last, an `attestation.generated` event is recorded with the actor, the
  project, and the review, and the detail `record digest {digest}, chain head {head}, pdf sha256 {hash}, key
  {thumbprint}, selfSigned={true or false}`. The event is written strictly: when it cannot be written, the exception
  reaches the caller and no document is released.

**FileNameFor**: Gets the file name.

- *Parameters*: `int projectId`, `string reviewId`.
- *Returns*: `attestation-{projectId}-{reviewId}.pdf` in which every character other than a letter, a digit, a dot, an
  underscore, or a hyphen of the review identifier is replaced by a hyphen.

#### Error Handling

Expected situations are statuses and not exceptions, so that the endpoint maps each one to its answer. A null argument
raises `ArgumentNullException`. An audit failure is not caught: an attestation that cannot be traced is not released. The
event follows the signing, because it contains the hash of the signed document, so the chain head that is printed in
the document precedes the event of the document. Finding the head verifies the whole chain (it reads every entry),
which is acceptable for an operation that is rare.

#### Dependencies

- **SigningService**, **AuditService**, **IReviewStore**, **RecordDigest**, **Actor**, **AuditDraft** — Core.
- **IAttestationKeyProvider**, **IAttestationRenderer**, **IAttestationSigner** — the other units of this subsystem.

#### Callers

- **AttestationEndpoint** — asks for the document of a download request.
