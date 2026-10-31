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
- The document shows the review, its fingerprint, files, checklist, threads, sign-offs, audit head, and tool version, as
  far as the evidence settings include them (see the criteria for the Content scope below).
- A checklist comment is printed in full, however long, and runs over pages without loss; only the list of resolved
  threads is capped (100), with exact counts and a line that says how many are left out.
- Every checklist answer shows who answered it and the time of the answer, in UTC to the second like the other times of
  the document; an answer without a time shows no time.
- A self-signed key gets the notice; an organization key gets its subject and issuer and no notice.
- The statement of what the signature proves and does not prove is in the document.
- The result is deterministic, grows over pages, and handles characters beyond WinAnsi and a missing document.
- A Content scope section follows the review section in every PDF: it says that nothing is left out with the default
  settings, names each omitted part otherwise, states whether the request was anonymous, and says that the record digest
  covers the complete record.
- A switch that is off removes its part (the threads, the files, the checklist wording, the git context), and with all
  of them off the parts that are always included (the checklist outcomes, the sign-offs, the fingerprint, the file
  count, the audit head, and the key statement) remain.

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
rendered. Every check shows its outcome, its comment, and who answered it and when (`Answered by Ann, 2025-03-04
05:01:02 UTC`). This scenario is tested by
`AttestationRenderer_Render_Document_ShowsChecklistAnswersAndComments`.

**AttestationRenderer_Render_Document_ShowsAnswerTimeOfEachAnswerInUtc**: Answers are rendered with a time that has an
offset, with two times a second apart, with a carried answer, and with an answer that has no time. The offset time is
printed in UTC, the two answers show distinct seconds, the carried answer shows its time and its "Carried from review"
line, and the answer without a time is printed as `Answered by X` with no time. This scenario is tested by
`AttestationRenderer_Render_Document_ShowsAnswerTimeOfEachAnswerInUtc`.

**AttestationRenderer_Render_LongChecklistComment_IsPrintedInFull**: A comment of 400 numbered words (about 3,600
characters) is rendered. Every word is in the text lines of the PDF, in order, and no line holds an ellipsis. This
scenario is tested by `AttestationRenderer_Render_LongChecklistComment_IsPrintedInFull`.

**AttestationRenderer_Render_VeryLongChecklistComment_RunsOverPagesWithoutLoss**: A comment of 6,000 numbered words
(about 54,000 characters) is rendered. The document has many more pages than the sample, and every word is in the text,
in order. This scenario is tested by `AttestationRenderer_Render_VeryLongChecklistComment_RunsOverPagesWithoutLoss`.

**AttestationRenderer_Render_Document_SummarizesThreads**: A review with open and resolved threads is rendered. The text
gives the count of open threads and a summary of each resolved one. This scenario is tested by
`AttestationRenderer_Render_Document_SummarizesThreads`.

**AttestationRenderer_Render_MoreResolvedThreadsThanTheCap_ListsTheCapAndCountsTheRest**: A review with 130 resolved
threads is rendered. The counts say 2 open and 130 resolved, the first 100 threads are listed in order (the first and the
hundredth are present, the 101st is not), and one line says "and 30 more resolved threads (see the review record)". This
scenario is tested by `AttestationRenderer_Render_MoreResolvedThreadsThanTheCap_ListsTheCapAndCountsTheRest`.

**AttestationRenderer_Render_ExactlyTheCapOfResolvedThreads_ListsAllWithoutAMoreLine**: A review with exactly 100 resolved
threads is rendered. All 100 are listed and there is no line about further threads. This scenario is tested by
`AttestationRenderer_Render_ExactlyTheCapOfResolvedThreads_ListsAllWithoutAMoreLine`.

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

**AttestationRenderer_Render_CodeSigningKey_PrintsTheNoteOnlyWhenItApplies**: Documents are rendered with a key that is
for code signing only and with one that is not. The text of the first has the code-signing note (the lines of the
wrapped paragraph joined), the second does not, and the self-signed notice is printed in both. This scenario is tested
by `AttestationRenderer_Render_CodeSigningKey_PrintsTheNoteOnlyWhenItApplies`.

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

**AttestationRenderer_Render_DefaultSettings_ContentScopeSaysNothingIsLeftOut**: A document with the default settings is
rendered. The Content scope section follows the review section, shows each switch, says that nothing is left out, and
says that the record digest covers the complete record. This scenario is tested by
`AttestationRenderer_Render_DefaultSettings_ContentScopeSaysNothingIsLeftOut`.

**AttestationRenderer_Render_AnonymousRequest_ContentScopeSaysSo**: A document for a request without signing in is
rendered. The Content scope section says that the request was made without signing in. This scenario is tested by
`AttestationRenderer_Render_AnonymousRequest_ContentScopeSaysSo`.

**AttestationRenderer_Render_ThreadsOff_OmitsThreadsAndSaysSo**: A document with the threads turned off is rendered. The
thread section and the thread text are missing, and the Content scope section names the omission. This scenario is
tested by `AttestationRenderer_Render_ThreadsOff_OmitsThreadsAndSaysSo`.

**AttestationRenderer_Render_FilesOff_OmitsFileListButKeepsFingerprint**: A document with the files turned off is
rendered. The list of files is missing, the Content scope section gives the number of files, and the content fingerprint
is still printed. This scenario is tested by `AttestationRenderer_Render_FilesOff_OmitsFileListButKeepsFingerprint`.

**AttestationRenderer_Render_ChecklistWordingOff_PrintsIdsOutcomesAndJustificationsOnly**: A document with the
checklist wording turned off is rendered. The identifiers, outcomes, and justifications are printed, and the question
text and the section title are not. This scenario is tested by
`AttestationRenderer_Render_ChecklistWordingOff_PrintsIdsOutcomesAndJustificationsOnly`.

**AttestationRenderer_Render_GitContextOff_OmitsRepositoryAndRevisions**: A review of a repository is rendered with the
git context turned off. The repository and the revisions are missing, the fingerprint and the review set stay, and an
uploaded review is not affected. This scenario is tested by
`AttestationRenderer_Render_GitContextOff_OmitsRepositoryAndRevisions`.

**AttestationRenderer_Render_EverythingOff_KeepsTheAlwaysIncludedParts**: A document with every switch off is rendered.
The parts that are always included are still printed, and every omitted part is named in the Content scope section. This
scenario is tested by `AttestationRenderer_Render_EverythingOff_KeepsTheAlwaysIncludedParts`.

**AttestationRenderer_Render_FollowUp_ShowsFollowedReviewAndCarriedMarks**: A document that follows a review, with
carried marks on a check, a section, and a thread, is rendered. The Review section has the row "Follows review"
with the followed review and the counts, and the marked parts say they were carried from that review; a document that
follows nothing shows none of them. This scenario is tested by
`AttestationRenderer_Render_FollowUp_ShowsFollowedReviewAndCarriedMarks`.
