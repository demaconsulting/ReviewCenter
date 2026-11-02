### AttestationService

#### Verification Approach

`AttestationService` is verified with tests defined in `AttestationServiceTests.cs`. The tests use the real
`AttestationService`, `SigningService`, `AuditService`, renderer, and signer over in-memory stores built by the helper
`AttestationFixture` (a review with a checklist that is answered in the ballot of the first slot, which is handed in,
and one reviewer slot that is signed or not), and a
fake key provider (`FakeAttestationKeyProvider`) that returns a temporary key, throws, or counts its calls, so that the
order of the sequence and the audit event can be checked. A fake signer that throws covers the failure of signing.

The effect of the evidence settings is verified with tests defined in `AttestationContentSettingsTests.cs`. They use
the same fixture, set the settings through the real `EvidenceSettingsService`, call the service with an actor or
without one, read the text of the signed PDF with `PdfInspector`, and verify its signature.

The attestation of a concluded review (an approval or a rejection, with findings, the conclusion and the ballots
appendix) and what it must never print are verified with tests defined in `AttestationServiceRejectedTests.cs`, with
the same fixture, which concludes a review as rejected with `RejectAsync`.

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
- The PDF shows the consolidated checklist of the submitted ballots and the time of each answer (of the lowest agreeing
  slot). Answering again with the same outcomes later changes that time but neither the record digest nor the approval,
  because the digest covers only the agreed outcomes.
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
- A review that is concluded as rejected has a signed attestation: an actor gets `Ok` with a PDF whose title and first
  page say that it is rejected, whose conclusion has the reasons, whose threads are those with an included comment (the
  same set that the record digest binds, with only their included comments), and whose ballots appendix has the final
  ballot of every slot with a
  fingerprint that equals the fingerprint of the stored record; a comment that was not included is
  not printed. The audit event names the outcome. A request without an actor gets `NotFound`, so a rejection is never
  served anonymously, while an approved review is.
- A review that is not concluded reveals no ballot, justification, or outcome of any slot to a member, and the key
  is not asked for.
- With the reviewer names off the appendix and the findings use labels; with the checklist wording off the appendix
  prints identifiers and outcomes only; with the threads off no finding is printed and the document says so.
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
(in the ballot of the first slot) change after the document is rendered and before the second check. The change is
written to the store directly by the fixture, because the services refuse to change an approved review. The status is
`Changed` with the message "The review changed while the attestation was being prepared; try again.", there is no
content or file name, and the audit trail has no attestation event. This scenario is tested by
`AttestationService_GenerateAsync_RecordChangesWhileRendering_ReturnsChangedAndRecordsNothing`.

**AttestationService_GenerateAsync_BallotFieldChangesWhileRendering_ReturnsChangedAndRecordsNothing**: In that moment
the answers of the ballot are written again with the same outcomes but another time, so the record digest is the same
and only the ballot fingerprint differs. The status is `Changed`, there is no content, and nothing is recorded. This
scenario is tested by
`AttestationService_GenerateAsync_BallotFieldChangesWhileRendering_ReturnsChangedAndRecordsNothing`.

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

**AttestationService_GenerateAsync_KeyProblem_ReturnsKeyUnavailableAndRecordsNothing**: The key provider throws. The
status is `KeyUnavailable` with its message, and the audit trail has no attestation event. This scenario is tested by
`AttestationService_GenerateAsync_KeyProblem_ReturnsKeyUnavailableAndRecordsNothing`.

**AttestationService_GenerateAsync_SignerFails_ReturnsKeyUnavailableAndRecordsNothing**: The signer throws a key error.
The status is `KeyUnavailable` and nothing is recorded. This scenario is tested by
`AttestationService_GenerateAsync_SignerFails_ReturnsKeyUnavailableAndRecordsNothing`.

**AttestationService_GenerateAsync_CompleteReview_ReturnsSignedPdfOfTheReview**: A complete review is asked for. The
result is a PDF with a signature that verifies, with the content of the review and a file name. This scenario is tested
by `AttestationService_GenerateAsync_CompleteReview_ReturnsSignedPdfOfTheReview`.

**AttestationService_GenerateAsync_ReAnsweredLaterWithSameValue_ShowsAnswerTimeAndKeepsDigestAndApproval**: A signed
review is answered again three hours later with the same outcomes (written into the ballot of the first slot by the
fixture, which stays handed in), and the attestation is asked for an hour after that. The record digest is the same as
before (the consolidated standing is unchanged), the status is `Ok` (the signatures are still current), and the PDF
shows the time of the later answers, neither the time of the signature nor the time of the request. This scenario is
tested by
`AttestationService_GenerateAsync_ReAnsweredLaterWithSameValue_ShowsAnswerTimeAndKeepsDigestAndApproval`.

**AttestationService_GenerateAsync_CompleteReview_RecordsAuditEventWithDigestAndPdfHash**: A complete review is asked
for. An `attestation.generated` event of the actor has the record digest, the chain head, the SHA-256 of the PDF, and
the key. This scenario is tested by
`AttestationService_GenerateAsync_CompleteReview_RecordsAuditEventWithDigestAndPdfHash`.

**AttestationService_GenerateAsync_AskedTwice_RecordsTwoEvents**: The attestation is asked for twice. There are two
events. This scenario is tested by `AttestationService_GenerateAsync_AskedTwice_RecordsTwoEvents`.

**AttestationService_FileNameFor_ReviewId_KeepsOnlySafeCharacters**: A review identifier with unsafe characters is
turned into a file name. Only letters, digits, dot, underscore, and hyphen remain. This scenario is tested by
`AttestationService_FileNameFor_ReviewId_KeepsOnlySafeCharacters`.

**AttestationService_Constructor_NullCollaborator_Throws**: A missing collaborator is passed. `ArgumentNullException` is
raised. This scenario is tested by `AttestationService_Constructor_NullCollaborator_Throws`.

**AttestationService_GenerateAsync_DefaultSettings_IncludesEverything**: A complete review is asked for with the default
settings. The PDF names the reviewers, has the question text and the files, has the content scope section, and its
signature verifies. This scenario is tested by `AttestationService_GenerateAsync_DefaultSettings_IncludesEverything`.

**AttestationService_GenerateAsync_NamesOff_ReplacesPeopleWithLabelsAndStillVerifies**: The reviewer names are turned
off. No user name or display name appears in the PDF; the people of the slot are `Reviewer 1 (primary)` and
`Reviewer 1 (alternate)`, the answerer of a check is the primary of the slot (the line "Answered by Reviewer 1
(primary)" with the time), anybody else is `Participant 1`, the requester is a signed-in user, and the signature
verifies over the reduced PDF. This scenario is tested by
`AttestationService_GenerateAsync_NamesOff_ReplacesPeopleWithLabelsAndStillVerifies`.

**AttestationService_GenerateAsync_ThreadsOff_OmitsFindingsAndStillVerifies**: The threads are turned off. The
findings and the comment text are missing, the document says that they are not included by project setting, and the
signature verifies. This scenario is tested by
`AttestationService_GenerateAsync_ThreadsOff_OmitsFindingsAndStillVerifies`.

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

**AttestationService_BuildFindings_CheckFinding_PrintsCheckLabel**: A review has a resolved finding about a check and a
resolved finding on `a.txt` line 1, both included in the report with a resolution note, and is signed. The document
names the first as `Check {id}` and the second with its path and line, and prints no empty file path. This scenario is
tested by `AttestationService_BuildFindings_CheckFinding_PrintsCheckLabel`.

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

**AttestationService_GenerateAsync_RejectedConcluded_ReturnsSignedRejectedAttestation**: Every ballot is a Fail, the
conclusion text is written, and the slot signs the rejection. A signed-in caller gets `Ok` with a PDF whose signature
verifies and that says it is rejected, and the audit trail has one `attestation.generated` event whose detail starts
with `outcome rejected`. This scenario is tested by
`AttestationService_GenerateAsync_RejectedConcluded_ReturnsSignedRejectedAttestation`.

**AttestationService_GenerateAsync_RejectedConcludedAnonymous_ReturnsNotFound**: The same rejected, concluded review is
in a project that allows anonymous attestation PDFs. A request without an actor gets `NotFound`, the answer for a review
that does not exist. This scenario is tested by `AttestationService_GenerateAsync_RejectedConcludedAnonymous_ReturnsNotFound`.

**AttestationService_GenerateAsync_RejectedWithFindings_PrintsConclusionFindingsAndAppendixOnly**: A rejected review has
an included finding that was resolved with a note, a reply that was not included, and a chat thread. The PDF prints the
conclusion with its reasons, the finding with its resolution note, and the ballots appendix with the slot's final ballot
and the fingerprint, and neither the chat thread nor the reply that was not included. This scenario is tested by
`AttestationService_GenerateAsync_RejectedWithFindings_PrintsConclusionFindingsAndAppendixOnly`.

**AttestationService_GenerateAsync_Rejected_BallotFingerprintMatchesRecord**: The fingerprint printed in the appendix of
a rejected review equals `RecordDigest.ComputeBallotFingerprint` of the record in the store. This scenario is tested by
`AttestationService_GenerateAsync_Rejected_BallotFingerprintMatchesRecord`.

**AttestationService_GenerateAsync_Approved_PrintsConclusionAndBallotsAppendixWithoutRejectedMark**: An approved review
is asked for. The PDF has the conclusion and the ballots appendix and no rejected title or banner. This scenario is
tested by `AttestationService_GenerateAsync_Approved_PrintsConclusionAndBallotsAppendixWithoutRejectedMark`.

**AttestationService_GenerateAsync_OpenReviewWithBallots_RevealsNoBallotDetail**: A review whose ballots were handed in
with failing answers but that is not concluded (no signature) is asked for by a member. The status is `NotApproved`,
there is no content, the message names no answer, justification or outcome of a slot, and the key provider was not
called. A request without an actor for an open review is covered by
`AttestationService_GenerateAsync_AnonymousIncompleteReview_ReturnsNotFoundLikeMissing`. This scenario is tested by
`AttestationService_GenerateAsync_OpenReviewWithBallots_RevealsNoBallotDetail`.

**AttestationService_GenerateAsync_RejectedNamesOff_AppendixAndFindingsUseLabels**: The reviewer names are off for a
rejected review. The appendix and the findings name people as labels and no user name or display name is printed. This
scenario is tested by `AttestationService_GenerateAsync_RejectedNamesOff_AppendixAndFindingsUseLabels`.

**AttestationService_GenerateAsync_RejectedWordingOff_AppendixPrintsIdentifiersOnly**: The checklist wording is off for
a rejected review. The appendix prints identifiers and outcomes, with neither the question text nor the section title.
This scenario is tested by `AttestationService_GenerateAsync_RejectedWordingOff_AppendixPrintsIdentifiersOnly`.

**AttestationService_GenerateAsync_RejectedThreadsOff_PrintsNoFindings**: The threads are off for a rejected review. No
finding is printed and the document says that it left them out. This scenario is tested by
`AttestationService_GenerateAsync_RejectedThreadsOff_PrintsNoFindings`.

**AttestationService_GenerateAsync_AnonymousApprovedAndRejected_OnlyApprovedIsServed**: Two projects allow anonymous
attestation PDFs; one has an approved review and the other a rejected one. A request without an actor gets the PDF of
the approved one and `NotFound` for the rejected one, with no key request. This scenario is tested by
`AttestationService_GenerateAsync_AnonymousApprovedAndRejected_OnlyApprovedIsServed`.

**AttestationService_GenerateAsync_QuestionThreadWithIncludedReply_PrintsDiscussionWithOnlyIncludedComments**: A
Question (whose starting comment is not included) has a reply by Bob that is included in the report and a reply by Ann
that is not; the thread is resolved and the review approved. The PDF has the line "Discussion on a.txt, line 1", the
included reply as a "Reply by Bob" with its text, neither the starting comment nor the reply that was not included,
no "Finding by" line and no finding heading for the location, and the signature verifies. This scenario is tested by
`AttestationService_GenerateAsync_QuestionThreadWithIncludedReply_PrintsDiscussionWithOnlyIncludedComments`.

**AttestationService_GenerateAsync_MixedThreads_PrintsExactlyTheCommentsTheDigestCovers**: A review has a finding with
an included reply, a hidden reply and a resolution note, a Question with an included reply, a Question with only hidden
replies, a chat-only thread, and a finding about a check, all resolved, and is approved. The set of expected texts is
derived with the predicate of `RecordDigest.Canonicalize` (a thread with an included comment, and within it the
included comments). Every expected text is in the PDF, no text of a comment that was not included is, the number of
"Finding by", "Reply by" and "Resolution by" lines equals the number of expected comments, and the number of status
lines equals the number of threads with an included comment. This scenario is tested by
`AttestationService_GenerateAsync_MixedThreads_PrintsExactlyTheCommentsTheDigestCovers`.

**AttestationService_GenerateAsync_RejectedTwoSlots_AppendixPrintsBothBallotsWithTheirOwnJustifications**: A review has
two slots whose ballots both answer Fail but word the justification differently (so the outcomes agree and nothing is
disputed), and both slots sign the rejection. The ballots appendix has exactly the two lines "Slot 1: Ann (ann)" and
"Slot 2: Cyd (cyd)" in that order, each slot's own justification, and the fingerprint of the stored record. This
scenario is tested by
`AttestationService_GenerateAsync_RejectedTwoSlots_AppendixPrintsBothBallotsWithTheirOwnJustifications`.

**AttestationService_GenerateAsync_FollowUpReview_NamesFollowedReviewAndCarriedAnswers**: An approved review and an
approved follow-up that carried every Pass answer are asked for. The follow-up's attestation names the followed review
and the carried counts, and the original's names neither the row nor the marks. This scenario is tested by
`AttestationService_GenerateAsync_FollowUpReview_NamesFollowedReviewAndCarriedAnswers`.
