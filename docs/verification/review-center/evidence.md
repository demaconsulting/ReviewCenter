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

The Puppet end-to-end test of the browser was **not** extended for this subsystem. A fully signed review needs a large
checklist to be answered and every reviewer slot to be signed by a user who enters the password again, which is
expensive
to drive through a browser and adds no evidence that the in-process tests do not give: the page markup is verified by
the
component tests of `ReviewPage`, and the download by real HTTP requests to the real server. The screenshot of the Help
topic was made with the Puppet by hand and is not a test.

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
- The attestation download and the evidence index answer a user who is not a member of the project exactly like a
  project that does not exist (404), and a member and a system administrator are served.
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

**ReviewMarkContract_GoldenIndex_MatchesCommittedFile**: A project with reviews in several states is built with a fixed
clock,
and the evidence index is requested. The text equals `TestData/evidence-index.golden.json`, so a change of the shape,
the
order, or the fingerprint algorithm is a visible change of a committed file. This scenario is tested by
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
cannot see it). The units were not given a non-member case of their own; the guard is verified over HTTP, through the
real application, by the tests of the `Api` subsystem: `ApiProjectAccess_EveryProjectRoute_NonMemberToken_AnswersLikeAMissingProject`
(the attestation and the evidence index route, with a bearer token and with Basic credentials),
`ApiProjectAccess_EveryProjectRoute_MemberAndSystemAdmin_AreNotRefused` (a member and a system administrator are served),
and `ApiProjectAccess_AttestationPage_NonMemberSession_Answers404` (the web route answers an empty 404 for a signed-in
non-member). An anonymous request to the web route is redirected to the login page, which is tested by
`AttestationEndpoint_Get_SignedOut_RedirectsToLogin`.

**Units**: The file structure and the layout are verified in the unit tests of `PdfDocumentWriter`; the content and the
statements of the document in the unit tests of `AttestationRenderer`; the signature, its structure, and its resistance
to a change in the unit tests of `AttestationSigner`; the keys, the certificate store, and `RequireTrustedKey` in the
unit tests of `AttestationKeyProvider`; the sequence of the release and the audit event in the unit tests of
`AttestationService`; the statuses and the headers in the unit tests of `AttestationEndpoint`; and the entries, the
identity,
and the order of the evidence index in the unit tests of `EvidenceIndexService`. The start-up failure
for an unusable key and the registration are verified in the `Hosting` subsystem, and the button and the note in the
`Web` subsystem.
