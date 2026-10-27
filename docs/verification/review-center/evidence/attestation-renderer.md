### AttestationRenderer

#### Verification Approach

`AttestationRenderer` is verified with tests defined in `AttestationRendererTests.cs`. The tests build an
`AttestationDocument` with plain objects, so no database and no key is needed, render it with the real renderer and the
real `PdfDocumentWriter`, and read the text of the result back with `PdfInspector` (a helper of the tests). The wording
that the document must contain, in particular the statement of what the signature proves and the notice about a
self-signed key, is verified by looking for it in the text.

#### Test Environment

N/A - standard test environment (everything runs in memory).

#### Acceptance Criteria

- All tests pass with zero failures.
- The document shows the review, its fingerprint, files, checklist, threads, sign-offs, audit head, and tool version.
- A self-signed key gets the notice; an organization key gets its subject and issuer and no notice.
- The statement of what the signature proves and does not prove is in the document.
- The result is deterministic, grows over pages, and handles characters beyond WinAnsi and a missing document.

#### Test Scenarios

**AttestationRenderer_Render_Document_ShowsReviewIdentityRevisionsAndFingerprint**: A review of a repository is rendered.
The text has the project, the review identifier and title, the repository and both commits, and the content
fingerprint. This scenario is tested by `AttestationRenderer_Render_Document_ShowsReviewIdentityRevisionsAndFingerprint`.

**AttestationRenderer_Render_UploadedReview_SaysFilesWereUploaded**: A review without a repository is rendered. The text
says that the files were uploaded. This scenario is tested by
`AttestationRenderer_Render_UploadedReview_SaysFilesWereUploaded`.

**AttestationRenderer_Render_Document_ListsFilesWithStatus**: Files with several statuses are rendered. Each path is
listed with its status, size, and hash. This scenario is tested by
`AttestationRenderer_Render_Document_ListsFilesWithStatus`.

**AttestationRenderer_Render_Document_ShowsChecklistAnswersAndComments**: A checklist with answers and comments is
rendered. Every check shows its outcome, its comment, and who answered it. This scenario is tested by
`AttestationRenderer_Render_Document_ShowsChecklistAnswersAndComments`.

**AttestationRenderer_Render_Document_SummarizesThreads**: A review with open and resolved threads is rendered. The text
gives the count of open threads and a summary of each resolved one. This scenario is tested by
`AttestationRenderer_Render_Document_SummarizesThreads`.

**AttestationRenderer_Render_Document_ShowsSlotsSignersTimestampsAndDigests**: Slots with signatures are rendered. Each
slot shows its signer, the time, and the digest it covers. This scenario is tested by
`AttestationRenderer_Render_Document_ShowsSlotsSignersTimestampsAndDigests`.

**AttestationRenderer_Render_Document_ShowsSlotDigestOnOneLine**: A signed slot is rendered with its 64-character record
digest. The digest is a single line and is not wrapped. This scenario is tested by
`AttestationRenderer_Render_Document_ShowsSlotDigestOnOneLine`.

**AttestationRenderer_Render_Document_ShowsAuditHeadAndToolVersion**: The audit anchor and the tool version are
rendered. The text has the head hash, the number of entries, and the version. This scenario is tested by
`AttestationRenderer_Render_Document_ShowsAuditHeadAndToolVersion`.

**AttestationRenderer_Render_Document_SetsMetadataWithFingerprintAndReviewId**: A document is rendered. The properties of
the PDF hold the fingerprint and the review identifier. This scenario is tested by
`AttestationRenderer_Render_Document_SetsMetadataWithFingerprintAndReviewId`.

**AttestationRenderer_Render_SelfSignedKey_PrintsTheNotice**: A document with a self-signed key is rendered. The text has
the notice that the key is self-signed and not an organization key. This scenario is tested by
`AttestationRenderer_Render_SelfSignedKey_PrintsTheNotice`.

**AttestationRenderer_Render_OrganizationKey_PrintsSubjectIssuerAndNoNotice**: A document with an organization key is
rendered. The text has the subject and the issuer and does not have the notice. This scenario is tested by
`AttestationRenderer_Render_OrganizationKey_PrintsSubjectIssuerAndNoNotice`.

**AttestationRenderer_Render_Document_ExplainsWhatTheSignatureProves**: A document is rendered. The text states what the
signature proves, that it does not prove who the reviewers are, and that the time is the clock of the server and not a
trusted timestamp. This scenario is tested by `AttestationRenderer_Render_Document_ExplainsWhatTheSignatureProves`.

**AttestationRenderer_Render_SameDocument_ProducesIdenticalBytes**: The same document is rendered twice. The bytes are
equal. This scenario is tested by `AttestationRenderer_Render_SameDocument_ProducesIdenticalBytes`.

**AttestationRenderer_Render_ManyFiles_BreaksIntoSeveralPages**: A review with many files is rendered. The document has
several pages and every file is listed. This scenario is tested by
`AttestationRenderer_Render_ManyFiles_BreaksIntoSeveralPages`.

**AttestationRenderer_Render_UnicodeNames_AreReplacedByQuestionMarks**: Names with characters beyond WinAnsi are
rendered. They appear as question marks, and the document is still valid. This scenario is tested by
`AttestationRenderer_Render_UnicodeNames_AreReplacedByQuestionMarks`.

**AttestationRenderer_Render_NoChecklist_SaysSo**: A review without a checklist is rendered. The text says that there is
none. This scenario is tested by `AttestationRenderer_Render_NoChecklist_SaysSo`.

**AttestationRenderer_Render_NullDocument_Throws**: A missing document is rendered. `ArgumentNullException` is raised.
This scenario is tested by `AttestationRenderer_Render_NullDocument_Throws`.
