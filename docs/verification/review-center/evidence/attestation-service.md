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
- A review that changes between the rendering and the signing (the record, a slot added, or a set of slots that
  differs even though the review is still complete) gives the status `Changed`, no document, and no audit event; a
  review that does not change is attested after the second check, which runs once and before the audit event.
- A problem of the key or of the signer is reported as an unavailable key, and nothing is recorded.
- A complete review gives a signed PDF of the review, and its release is recorded with the digest, the chain head, the
  hash of the PDF, and the key; each request records its own event.
- A user who cannot see the project is answered `NotFound`, like a missing review. The unit tests have no case for
  this guard; it is verified over HTTP by `ApiProjectAccess_EveryProjectRoute_NonMemberToken_AnswersLikeAMissingProject`
  and `ApiProjectAccess_EveryProjectRoute_MemberAndSystemAdmin_AreNotRefused` in the `Api` subsystem.
The second check is made testable by the internal `AfterRender` step of the service, which the tests use to change the
review in the moment between the rendering and the check. The short window between the second check and the signing
and the audit event is not covered by a test; it contains no read of the review.

#### Test Scenarios

**AttestationService_GenerateAsync_UnknownReview_ReturnsNotFound**: An unknown review is asked for. The status is
`NotFound`. This scenario is tested by `AttestationService_GenerateAsync_UnknownReview_ReturnsNotFound`.

**AttestationService_GenerateAsync_SlotNotSigned_ReturnsNotApprovedWithoutAskingForTheKey**: A review whose slot is not
signed is asked for. The status is `NotApproved` and the key provider was not called. This scenario is tested by
`AttestationService_GenerateAsync_SlotNotSigned_ReturnsNotApprovedWithoutAskingForTheKey`.

**AttestationService_GenerateAsync_RecordChangedAfterSigning_ReturnsNotApproved**: The record of a signed review changes.
The status is `NotApproved`. This scenario is tested by
`AttestationService_GenerateAsync_RecordChangedAfterSigning_ReturnsNotApproved`.

**AttestationService_GenerateAsync_RecordChangesWhileRendering_ReturnsChangedAndRecordsNothing**: The checklist answers
change after the document is rendered and before the second check. The status is `Changed` with the message "The review
changed while the attestation was being prepared; try again.", there is no content or file name, and the audit trail has
no attestation event. This scenario is tested by
`AttestationService_GenerateAsync_RecordChangesWhileRendering_ReturnsChangedAndRecordsNothing`.

**AttestationService_GenerateAsync_SlotAddedWhileRendering_ReturnsChangedAndRecordsNothing**: A second slot that nobody
has signed is added in that moment. The status is `Changed`, there is no content, and nothing is recorded. This scenario
is tested by `AttestationService_GenerateAsync_SlotAddedWhileRendering_ReturnsChangedAndRecordsNothing`.

**AttestationService_GenerateAsync_SlotsChangeWhileRenderingButStillComplete_ReturnsChangedAndRecordsNothing**: A second
slot is added and signed in that moment, so the review is still approved and the digest of the record is the same, but
the slots differ from the rendered ones. The status is `Changed`, there is no content, and nothing is recorded. This
scenario is tested by
`AttestationService_GenerateAsync_SlotsChangeWhileRenderingButStillComplete_ReturnsChangedAndRecordsNothing`.

**AttestationService_GenerateAsync_ReviewUnchangedWhileRendering_PassesRevalidationAndIsAttested**: The step between the
rendering and the check changes nothing. The status is `Ok` with a PDF, one audit event is written, and the step ran
once and before any event existed. This scenario is tested by
`AttestationService_GenerateAsync_ReviewUnchangedWhileRendering_PassesRevalidationAndIsAttested`.

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
