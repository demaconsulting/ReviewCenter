### AttestationService

#### Verification Approach

`AttestationService` is verified with tests defined in `AttestationServiceTests.cs`. The tests use the real
`AttestationService`, `SigningService`, `AuditService`, renderer, and signer over in-memory stores built by the helper
`AttestationFixture` (a review with a checklist that is answered, and one reviewer slot that is signed or not), and a
fake key provider (`FakeAttestationKeyProvider`) that returns a temporary key, throws, or counts its calls, so that the
order of the sequence and the audit event can be checked. A fake signer that throws covers the failure of signing.

The effect of the evidence settings is verified with tests defined in `AttestationContentSettingsTests.cs`. They use
the same fixture, set the settings through the real `EvidenceSettingsService`, call the service with an actor or
without one, read the text of the signed PDF with `PdfInspector`, and verify its signature.

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
- The PDF shows the time each answer was given. Giving the same answers again later changes that time but neither the
  record digest nor the approval.
- A user who cannot see the project is answered `NotFound`, like a missing review. The unit tests have no case for
  this guard; it is verified over HTTP by `ApiProjectAccess_EveryProjectRoute_NonMemberToken_AnswersLikeAMissingProject`
  and `ApiProjectAccess_EveryProjectRoute_MemberAndSystemAdmin_AreNotRefused` in the `Api` subsystem.
- The evidence settings of the project decide the content: with the defaults the PDF has everything; with the
  reviewer names off every person is a label (`Reviewer N (primary)`, `Reviewer N (alternate)`, `Participant N`) and the
  requester is a signed-in user; with the threads, the files, or the checklist wording off the section is missing; and
  in every case the signature of the PDF verifies.
- A settings change between the rendering and the signing gives the status `Changed`, and nothing is recorded.
- A request without an actor is `NotFound` while the project does not allow anonymous attestation PDFs and for a
  project that does not exist, and is served, with a PDF that says the request was anonymous, when it does; the audit
  event then names the actor `anonymous`.
- A request without an actor for a review that is not complete gets `NotFound`, the same answer as for a review that
  does not exist, while an actor gets `NotApproved`.
- A review that is approved is locked, and the attestation is still produced for it. A review that was reopened by an
  administrator and signed again gives a PDF with a "Reopen history" section (time, who, reason, and the previous
  digest of each reopen), and a review that was never reopened gives a PDF without that section. When the reviewer
  names are off the administrator is a label (`Participant N`), not a name.

The second check is made testable by the internal `AfterRender` step of the service, which the tests use to change the
review in the moment between the rendering and the check. The short window between the second check and the signing
and the audit event is not covered by a test; it contains no read of the review.

#### Test Scenarios

**AttestationService_GenerateAsync_UnknownReview_ReturnsNotFound**: An unknown review is asked for. The status is
`NotFound`. This scenario is tested by `AttestationService_GenerateAsync_UnknownReview_ReturnsNotFound`.

**AttestationService_GenerateAsync_SlotNotSigned_ReturnsNotApprovedWithoutAskingForTheKey**: A review whose slot is not
signed is asked for. The status is `NotApproved` and the key provider was not called. This scenario is tested by
`AttestationService_GenerateAsync_SlotNotSigned_ReturnsNotApprovedWithoutAskingForTheKey`.

**AttestationService_GenerateAsync_RecordChangedAfterSigning_ReturnsNotApproved**: The record of a signed review
changes (an administrator reopens the approved review). The status is `NotApproved`. This scenario is tested by
`AttestationService_GenerateAsync_RecordChangedAfterSigning_ReturnsNotApproved`.

**AttestationService_GenerateAsync_RecordChangesWhileRendering_ReturnsChangedAndRecordsNothing**: The checklist answers
change after the document is rendered and before the second check. The change is written to the store directly by the
fixture, because the services refuse to change an approved review. The status is `Changed` with the message "The
review changed while the attestation was being prepared; try again.", there is no content or file name, and the audit
trail has no attestation event. This scenario is tested by
`AttestationService_GenerateAsync_RecordChangesWhileRendering_ReturnsChangedAndRecordsNothing`.

**AttestationService_GenerateAsync_SlotAddedWhileRendering_ReturnsChangedAndRecordsNothing**: A second slot that nobody
has signed is added in that moment, directly in the store for the same reason. The status is `Changed`, there is no
content, and nothing is recorded. This scenario is tested by
`AttestationService_GenerateAsync_SlotAddedWhileRendering_ReturnsChangedAndRecordsNothing`.

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

**AttestationService_GenerateAsync_ReAnsweredLaterWithSameValue_ShowsAnswerTimeAndKeepsDigestAndApproval**: A signed
review is answered again three hours later with the same outcomes, and the attestation is asked for an hour after that.
The record digest is the same as before, the status is `Ok` (the signatures are still current), and the PDF shows the
time of the later answers, neither the time of the signature nor the time of the request. This scenario is tested by
`AttestationService_GenerateAsync_ReAnsweredLaterWithSameValue_ShowsAnswerTimeAndKeepsDigestAndApproval`.

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

**AttestationService_GenerateAsync_DefaultSettings_IncludesEverything**: A complete review is asked for with the default
settings. The PDF names the reviewers, has the question text and the files, has the content scope section, and its
signature verifies. This scenario is tested by `AttestationService_GenerateAsync_DefaultSettings_IncludesEverything`.

**AttestationService_GenerateAsync_NamesOff_ReplacesPeopleWithLabelsAndStillVerifies**: The reviewer names are turned
off. No user name or display name appears in the PDF; the people of the slot are `Reviewer 1 (primary)` and
`Reviewer 1 (alternate)`, anybody else is `Participant 1`, the requester is a signed-in user, and the signature
verifies over the reduced PDF. This scenario is tested by
`AttestationService_GenerateAsync_NamesOff_ReplacesPeopleWithLabelsAndStillVerifies`.

**AttestationService_GenerateAsync_ThreadsOff_OmitsThreadSectionAndStillVerifies**: The threads are turned off. The
thread section and the comment text are missing, and the signature verifies. This scenario is tested by
`AttestationService_GenerateAsync_ThreadsOff_OmitsThreadSectionAndStillVerifies`.

**AttestationService_GenerateAsync_FilesOff_OmitsFileListAndStillVerifies**: The files are turned off. The list of files
and hashes is missing, and the signature verifies. This scenario is tested by
`AttestationService_GenerateAsync_FilesOff_OmitsFileListAndStillVerifies`.

**AttestationService_GenerateAsync_ChecklistWordingOff_OmitsQuestionTextAndStillVerifies**: The checklist wording is
turned off. The question text is missing, the identifiers and outcomes stay, and the signature verifies. This scenario
is tested by `AttestationService_GenerateAsync_ChecklistWordingOff_OmitsQuestionTextAndStillVerifies`.

**AttestationService_GenerateAsync_AnonymousToggleOff_ReturnsNotFound**: A request without an actor is made while the
project does not allow anonymous attestation PDFs. The status is `NotFound` and nothing is recorded. This scenario is
tested by `AttestationService_GenerateAsync_AnonymousToggleOff_ReturnsNotFound`.

**AttestationService_GenerateAsync_AnonymousToggleOn_ReturnsPdfAndAuditsAnonymous**: A request without an actor is made
while the project allows it. The PDF is returned and says that the request was anonymous, and the audit entry names the
actor `anonymous`. This scenario is tested by
`AttestationService_GenerateAsync_AnonymousToggleOn_ReturnsPdfAndAuditsAnonymous`.

**AttestationService_GenerateAsync_AnonymousUnknownProject_ReturnsNotFound**: A request without an actor is made for a
project that does not exist while another project allows anonymous PDFs. The status is `NotFound`. This scenario is
tested by `AttestationService_GenerateAsync_AnonymousUnknownProject_ReturnsNotFound`.

**AttestationService_GenerateAsync_AnonymousIncompleteReview_ReturnsNotFoundLikeMissing**: A project that allows
anonymous attestation PDFs has a review that is not complete. A request without an actor for it, and one for a review
that does not exist, both give `NotFound` with the same message ("The review was not found."), nothing is recorded and
the key provider is not called; a request of a member for the incomplete review still gives `NotApproved`. This scenario
is tested by `AttestationService_GenerateAsync_AnonymousIncompleteReview_ReturnsNotFoundLikeMissing`.

**AttestationService_GenerateAsync_SettingsChangeWhileRendering_ReturnsChanged**: The settings change in the moment
between the rendering and the second check. The status is `Changed`, there is no content, and nothing is recorded. This
scenario is tested by `AttestationService_GenerateAsync_SettingsChangeWhileRendering_ReturnsChanged`.

**AttestationService_GenerateAsync_ReopenedReview_IncludesReopenHistory**: A review is approved, reopened by an
administrator with a reason, and signed again. The PDF has the "Reopen history" section with the time, the
administrator's name, the reason, and the previous digest of the reopen, and its signature verifies. This scenario is
tested by `AttestationService_GenerateAsync_ReopenedReview_IncludesReopenHistory`.

**AttestationService_GenerateAsync_NeverReopened_OmitsReopenHistory**: A complete review that was never reopened is
asked for. The PDF has no "Reopen history" section. This scenario is tested by
`AttestationService_GenerateAsync_NeverReopened_OmitsReopenHistory`.

**AttestationService_GenerateAsync_ReopenedWithoutReviewerNames_OmitsReopenedByName**: The reviewer names are turned
off for a reopened review. The "Reopen history" section is there with the reason, but the administrator's user name and
display name appear nowhere in the PDF, which names a participant label instead. This scenario is tested by
`AttestationService_GenerateAsync_ReopenedWithoutReviewerNames_OmitsReopenedByName`.

**AttestationService_GenerateAsync_LockedReview_StillRenders**: An approved (locked) review is asked for. The status is
`Ok` with a signed PDF, so the lock does not stop the attestation. This scenario is tested by
`AttestationService_GenerateAsync_LockedReview_StillRenders`.

**AttestationService_GenerateAsync_FollowUpReview_NamesFollowedReviewAndCarriedAnswers**: An approved review and an
approved follow-up that carried every Pass answer are asked for. The follow-up's attestation names the followed review
and the carried counts, and the original's names neither the row nor the marks. This scenario is tested by
`AttestationService_GenerateAsync_FollowUpReview_NamesFollowedReviewAndCarriedAnswers`.
