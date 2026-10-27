### AttestationService

#### Verification Approach

`AttestationService` is verified with tests defined in `AttestationServiceTests.cs`. The tests use the real
`AttestationService`, `SigningService`, `AuditService`, renderer, and signer over in-memory stores built by the helper
`AttestationFixture` (a review with a checklist that is answered, and one reviewer slot that is signed or not), and a
fake key provider (`FakeAttestationKeyProvider`) that returns a temporary key, throws, or counts its calls, so that the
order of the sequence and the audit event can be checked. A fake signer that throws covers the failure of signing.

#### Test Environment

N/A - standard test environment (everything runs in memory).

#### Acceptance Criteria

- All tests pass with zero failures.
- A missing review is not found, and a review that is not complete is not approved, without the key being asked for.
- A record that changed after the signature is not approved.
- A problem of the key or of the signer is reported as an unavailable key, and nothing is recorded.
- A complete review gives a signed PDF of the review, and its release is recorded with the digest, the chain head, the
  hash of the PDF, and the key; each request records its own event.

#### Test Scenarios

**AttestationService_GenerateAsync_UnknownReview_ReturnsNotFound**: An unknown review is asked for. The status is
`NotFound`. This scenario is tested by `AttestationService_GenerateAsync_UnknownReview_ReturnsNotFound`.

**AttestationService_GenerateAsync_SlotNotSigned_ReturnsNotApprovedWithoutAskingForTheKey**: A review whose slot is not
signed is asked for. The status is `NotApproved` and the key provider was not called. This scenario is tested by
`AttestationService_GenerateAsync_SlotNotSigned_ReturnsNotApprovedWithoutAskingForTheKey`.

**AttestationService_GenerateAsync_RecordChangedAfterSigning_ReturnsNotApproved**: The record of a signed review changes.
The status is `NotApproved`. This scenario is tested by
`AttestationService_GenerateAsync_RecordChangedAfterSigning_ReturnsNotApproved`.

**AttestationService_GenerateAsync_KeyProblem_ReturnsKeyUnavailableAndRecordsNothing**: The key provider throws. The status
is `KeyUnavailable` with its message, and the audit trail has no attestation event. This scenario is tested by
`AttestationService_GenerateAsync_KeyProblem_ReturnsKeyUnavailableAndRecordsNothing`.

**AttestationService_GenerateAsync_SignerFails_ReturnsKeyUnavailableAndRecordsNothing**: The signer throws a key error.
The status is `KeyUnavailable` and nothing is recorded. This scenario is tested by
`AttestationService_GenerateAsync_SignerFails_ReturnsKeyUnavailableAndRecordsNothing`.

**AttestationService_GenerateAsync_CompleteReview_ReturnsSignedPdfOfTheReview**: A complete review is asked for. The result
is a PDF with a signature that verifies, with the content of the review and a file name. This scenario is tested by
`AttestationService_GenerateAsync_CompleteReview_ReturnsSignedPdfOfTheReview`.

**AttestationService_GenerateAsync_CompleteReview_RecordsAuditEventWithDigestAndPdfHash**: A complete review is asked for.
An `attestation.generated` event of the actor has the record digest, the chain head, the SHA-256 of the PDF, and the key.
This scenario is tested by `AttestationService_GenerateAsync_CompleteReview_RecordsAuditEventWithDigestAndPdfHash`.

**AttestationService_GenerateAsync_AskedTwice_RecordsTwoEvents**: The attestation is asked for twice. There are two
events. This scenario is tested by `AttestationService_GenerateAsync_AskedTwice_RecordsTwoEvents`.

**AttestationService_FileNameFor_ReviewId_KeepsOnlySafeCharacters**: A review identifier with unsafe characters is turned
into a file name. Only letters, digits, dot, underscore, and hyphen remain. This scenario is tested by
`AttestationService_FileNameFor_ReviewId_KeepsOnlySafeCharacters`.

**AttestationService_Constructor_NullCollaborator_Throws**: A missing collaborator is passed. `ArgumentNullException` is
raised. This scenario is tested by `AttestationService_Constructor_NullCollaborator_Throws`.
