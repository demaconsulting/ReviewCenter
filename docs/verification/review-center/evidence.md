## Evidence

### Verification Approach

The `Evidence` subsystem is verified by subsystem tests defined in `EvidenceSubsystemTests.cs`. The tests run the real
application in process (`TestWebApp`, the real server over a temporary SQLite database, with the real `SigningService`,
`AuditService`, `AttestationService`, renderer, signer, and the default self-signed key provider) and drive it with the
test browser (`TestBrowser`, a real HTTP client that signs in): a review is created and completed by the signature of
its
reviewer slot, then the review page and the download address are requested. The subsystem exists to join these parts:
the
rule that only an approved review has an attestation, the key, the document, the signature, and the audit event. The
units are verified in detail by their own unit tests: `PdfDocumentWriter` (the file structure, the wrapping, and the
encoding), `AttestationRenderer` (the wording and
the content), `AttestationSigner` (the signature, checked with `System.Security.Cryptography.Pkcs` and with a reader of
the PDF structure written for the tests, `PdfInspector`), `AttestationKeyProvider` (the key files, the certificate
store, and the policy), `AttestationService` (the sequence and the audit event), `AttestationEndpoint` (the statuses),
and
`EvidenceIndexService` (the entries, the identity, and the order).

The browser system tests were **not** extended for this subsystem. A fully signed review needs a large
checklist to be answered and every reviewer slot to be signed by a user who enters the password again, which is
expensive
to drive through a browser and adds no evidence that the in-process tests do not give: the page markup is verified by
the
component tests of `ReviewPage`, and the download by real HTTP requests to the real server.

### Test Environment

Tests create temporary SQLite database files and key folders in private temporary folders, which are removed after the
test run. The tests of the certificate store provider create a temporary self-signed certificate in the certificate
store of the current user, remove it afterwards, and **skip themselves when that store is not available** (for example
on a system without a certificate store). No network access and no external program are needed. The **local machine
store and a real hardware module or smart card are not covered by automated tests** and need a manual check (see
Acceptance Criteria).

### Acceptance Criteria

- All subsystem tests pass with zero failures.
- A completed review is offered on the review page, and the offered link downloads a PDF with a signature that verifies,
  whose byte range covers everything except the signature value, that holds the review, and that carries the notice
  about the self-signed key; the download is in the audit trail.
- A review that is not complete is not offered on the page, and the download is refused with 409.
- A review concluded as rejected is offered the download like an approved one and gets a signed attestation marked as
  a rejection on its first page, which is verified by `EvidenceSubsystem_RejectedReview_MarksRejectionOnFirstPage`.
  Its findings, conclusion and ballots appendix, the rule that the evidence index never lists it, and the answer
  `NotFound` to a request without credentials are verified in the unit tests of `AttestationService` and
  `EvidenceIndexService`.
- The attestation download and the evidence index answer a user who is not a member of the project exactly like a
  project that does not exist (404), and a member and a system administrator are served. A request without credentials
  is served only when the project has switched on that route (see the `Api` subsystem).
- A PDF follows the evidence settings of the project: a part that a switch turns off is missing, the neutral labels
  replace the names when those are off, every PDF has a Content scope section, and the signature of the reduced PDF
  verifies.
- A review that is approved, and therefore locked, still gets its attestation PDF and its evidence index. A PDF of a
  review that was reopened after an approval and approved again carries a "Reopen history" section, which is verified
  by `EvidenceSubsystem_ReopenedAndApprovedAgain_StatesReopenHistory`. That the administrator is named only when the
  reviewer names are on, and that a PDF of a review that was never reopened has no such section, are verified in the
  unit tests of `AttestationService`.
- The PDF of a completed review states the review, its fingerprint and files, the checklist with who answered, the
  findings that were included, the conclusion, the ballots appendix, the reviewer sign-offs, the head of the audit
  trail, and the key that signed it; it leaves out the reviewer names, the checklist wording, and the findings when the
  evidence settings say so. These are verified by the subsystem tests named in the scenarios below.
- The evidence index that the application serves equals a committed file, has the shape that ReviewMark reads, and
  carries
  a fingerprint that equals the one that the algorithm of ReviewMark computes from the same files.
- A manual check, made by the people who deploy with an organization key, confirms that a certificate in the local
  machine store, or on a hardware module, can be named by its thumbprint, that the service account can use its key, and
  that a PDF reader shows the signature with the certificate chain of the organization.

### Test Scenarios

**EvidenceSubsystem_CompletedReview_IsOfferedAndDownloadedAsSignedPdf**: A review is completed by the signature of its
reviewer slot, and a signed-in user opens the review page. The page shows the download button with the address of the
attestation; following it answers with a PDF whose signature verifies, whose byte range covers everything except the
signature value, that holds the project and file names of the review, and that carries the notice about the self-signed
key of the server; and the audit trail has one `attestation.generated` event. This scenario is tested by
`EvidenceSubsystem_CompletedReview_IsOfferedAndDownloadedAsSignedPdf`.

**EvidenceSubsystem_ReviewNotComplete_IsNotOfferedAndRefused**: A review that nobody has signed shows no download
button,
and a request for the download answers 409. This scenario is tested by
`EvidenceSubsystem_ReviewNotComplete_IsNotOfferedAndRefused`.

**EvidenceSubsystem_CompletedReview_StatesReviewFingerprintAndFiles**: The document of the approved review is
downloaded. It has the Review section with the project, the review identifier and the content fingerprint, which equals
the fingerprint that the code of the tests computes from the content of the one file, and the Files section with the
file. This scenario is tested by `EvidenceSubsystem_CompletedReview_StatesReviewFingerprintAndFiles`.

**EvidenceSubsystem_CompletedReview_StatesChecklistWithWhoAnswered**: The document of the approved review has the
Checklist section with the first check of the default checklist answered `Pass`, and a line that says who answered
it and when, in UTC. This scenario is tested by `EvidenceSubsystem_CompletedReview_StatesChecklistWithWhoAnswered`.

**EvidenceSubsystem_CompletedReview_StatesConclusionApproved**: The document of the approved review has the Conclusion
section with the outcome `Approved` and does not carry the title of a rejection. This scenario is tested by
`EvidenceSubsystem_CompletedReview_StatesConclusionApproved`.

**EvidenceSubsystem_CompletedReview_StatesBallotsAppendixWithFingerprint**: The document of the approved review has the
Ballots appendix section with the ballot of slot 1, the time it was submitted, and the ballot fingerprint of 64
hexadecimal characters with the name of its algorithm. This scenario is tested by
`EvidenceSubsystem_CompletedReview_StatesBallotsAppendixWithFingerprint`.

**EvidenceSubsystem_CompletedReview_StatesReviewerSignOffs**: The document of the approved review has the Reviewer
sign-offs section with the primary reviewer, who signed, when, the meaning of the signature and the record digest. This
scenario is tested by `EvidenceSubsystem_CompletedReview_StatesReviewerSignOffs`.

**EvidenceSubsystem_CompletedReview_StatesAuditTrailHead**: The document of the approved review has the Audit trail
section, and the value after the row `Chain head hash` is 64 hexadecimal characters. This scenario is tested by
`EvidenceSubsystem_CompletedReview_StatesAuditTrailHead`.

**EvidenceSubsystem_CompletedReview_StatesTheSigningKey**: The document of the approved review has the Digital
signature section with the subject and the algorithm of the key, and the thumbprint that it states equals the
thumbprint of the certificate in the signature of the same file. This scenario is tested by
`EvidenceSubsystem_CompletedReview_StatesTheSigningKey`.

**EvidenceSubsystem_RejectedReview_MarksRejectionOnFirstPage**: The only reviewer answers every check `Fail` and writes
a conclusion text, and the review is signed. In the downloaded document the title `Review attestation - REJECTED` is
followed directly by the banner of the rejection, the Review section follows the banner, and the conclusion is
`Rejected`. This scenario is tested by `EvidenceSubsystem_RejectedReview_MarksRejectionOnFirstPage`.

**EvidenceSubsystem_ReopenedAndApprovedAgain_StatesReopenHistory**: An administrator other than the signer reopens the
approved review with a reason, and the reviewer signs again. The downloaded document has the Reopen history section
with the administrator, the reason, and the digest of the approval that was replaced. This scenario is tested by
`EvidenceSubsystem_ReopenedAndApprovedAgain_StatesReopenHistory`.

**EvidenceSubsystem_SettingsLeaveOut_NamesWordingAndFindings_AreLeftOutOfTheDocument**: A review has an included,
resolved finding and is signed, and the project switches off the reviewer names, the checklist wording, and the
discussion threads. The downloaded document names none of the people, has no wording of the first check but still its
identifier and outcome, does not have the text of the finding and says that the findings are not included, and
names the three parts in the line that says what was left out. This scenario is tested by
`EvidenceSubsystem_SettingsLeaveOut_NamesWordingAndFindings_AreLeftOutOfTheDocument`.

**EvidenceSubsystem_IncludedFinding_IsStatedInFindingsSection**: A review has a finding that was included in the report
and resolved, and is signed. The downloaded document has the section `Findings and comments in this report` with the
line that names the author of the finding, the text of the finding, and the status `Resolved`. This scenario is tested
by `EvidenceSubsystem_IncludedFinding_IsStatedInFindingsSection`.

**ReviewMarkContract_GoldenIndex_MatchesCommittedFile**: A project with reviews in several states is built with a fixed
clock,
and the evidence index is requested. The text equals `TestData/evidence-index.golden.json`, so a change of the shape,
the
order, the fingerprint algorithm, the result, or the address of an entry is a visible change of a committed file.
This scenario is tested by
`ReviewMarkContract_GoldenIndex_MatchesCommittedFile`.

**ReviewMarkContract_Index_HasTheSchemaReviewMarkReads**,
**ReviewMarkContract_IndexFingerprint_EqualsReviewMarkAlgorithm**,
and **ReviewMarkContract_Index_FollowsReviewMarkSemantics**: The index is read with an independent reader of the
ReviewMark schema (a root object `reviews` with the fields id, fingerprint, date, result, and file); the fingerprint of
the
entry equals the SHA-256 of the sorted lowercase-hex hashes of the files, computed here without the code of the product;
and
a reader that follows the matching and stale rule of ReviewMark finds the entry current for the right fingerprint and
stale
for another. These scenarios are tested by `ReviewMarkContract_Index_HasTheSchemaReviewMarkReads`,
`ReviewMarkContract_IndexFingerprint_EqualsReviewMarkAlgorithm`, and
`ReviewMarkContract_Index_FollowsReviewMarkSemantics`.

**Visibility**: The attestation and the evidence index are guarded by the project rules of `Core`
(`AttestationService` answers `NotFound` and `EvidenceIndexService.BuildAsync` refuses the project for a user who
cannot see it). The units have non-member cases of their own
(`AttestationService_GenerateAsync_ActorMayNotViewProject_ReturnsNotFoundLikeMissingReview`,
`EvidenceIndexService_Build_NonMemberActor_ThrowsProjectNotFound`, and
`AttestationEndpoint_Get_NonMemberSession_Answers404AndRecordsNothing`), and the guard is also verified over HTTP,
through the real application, by the tests of the `Api` subsystem:
`ApiProjectAccess_EveryProjectRoute_NonMemberToken_AnswersLikeAMissingProject`
(the attestation and the evidence index route, with a bearer token and with Basic credentials),
`ApiProjectAccess_EveryProjectRoute_MemberAndSystemAdmin_AreNotRefused` (a member and a system administrator are served),
and `ApiProjectAccess_AttestationPage_NonMemberSession_Answers404` (the web route answers an empty 404 for a signed-in
non-member). An anonymous request to the web route is redirected to the login page, which is tested by
`AttestationEndpoint_Get_SignedOut_RedirectsToLogin`. A request without an actor is refused like a missing project
unless the project has switched on the matching anonymous setting, which is tested in the units
(`AttestationService_GenerateAsync_AnonymousToggleOff_ReturnsNotFound` and
`EvidenceIndexService_Build_NoActor_NeedsTheAnonymousIndexSetting`) and over HTTP in the `Api` subsystem. A request
without an actor for a review that is not complete is answered like a review that does not exist
(`AttestationService_GenerateAsync_AnonymousIncompleteReview_ReturnsNotFoundLikeMissing`, and over HTTP
`AnonymousEvidenceAccess_AttestationPdf_IncompleteReview_AnswersLikeMissingReview`), while a signed-in caller still gets
409 (`AttestationEndpoint_Get_ReviewNotSigned_Answers409`).

**Content settings**: The evidence settings of a project decide what a PDF contains. The units verify it: the renderer
tests print every switch, the Content scope section, and each omission
(`AttestationRenderer_Render_DefaultSettings_ContentScopeSaysNothingIsLeftOut`,
`AttestationRenderer_Render_EverythingOff_KeepsTheAlwaysIncludedParts`, and the tests of the single switches), and the
tests of `AttestationContentSettingsTests.cs` run the service with each switch off, check the neutral labels, and verify
the signature of the reduced PDF (`AttestationService_GenerateAsync_NamesOff_ReplacesPeopleWithLabelsAndStillVerifies`),
the audit actor `anonymous` (`AttestationService_GenerateAsync_AnonymousToggleOn_ReturnsPdfAndAuditsAnonymous`), and the
check of the settings before the signing (`AttestationService_GenerateAsync_SettingsChangeWhileRendering_ReturnsChanged`).
The same PDF is downloaded without credentials by `AnonymousEvidenceAccess_BothOn_ServesIndexEntryAndSignedPdfAndAuditsAnonymous`
in the `Api` subsystem. The page that changes the settings is verified in the `Web` subsystem.

**Follow-up review**: The attestation of a review that follows another names the followed review and the numbers of
carried answers and threads in the Review section and marks the carried checks, sections, and threads, while the
attestation of a review that follows nothing shows none of it. This is tested by
`AttestationRenderer_Render_FollowUp_ShowsFollowedReviewAndCarriedMarks` and
`AttestationService_GenerateAsync_FollowUpReview_NamesFollowedReviewAndCarriedAnswers`.

**Units**: The file structure and the layout are verified in the unit tests of `PdfDocumentWriter`; the content and the
statements of the document in the unit tests of `AttestationRenderer`; the signature, its structure, and its resistance
to a change in the unit tests of `AttestationSigner`; the keys, the certificate store, and `RequireTrustedKey` in the
unit tests of `AttestationKeyProvider`; the sequence of the release and the audit event in the unit tests of
`AttestationService`; the statuses and the headers in the unit tests of `AttestationEndpoint`; and the entries, the
identity,
and the order of the evidence index in the unit tests of `EvidenceIndexService`. The start-up failure
for an unusable key and the registration are verified in the `Hosting` subsystem, and the button and the note in the
`Web` subsystem.
