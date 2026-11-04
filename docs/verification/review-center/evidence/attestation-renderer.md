### AttestationRenderer

#### Verification Approach

`AttestationRenderer` is verified with tests defined in `AttestationRendererTests.cs` and, for the rejection mark, the
conclusion, the findings and the ballots appendix, `AttestationRendererReportTests.cs`. The tests build an
`AttestationDocument` with plain objects, so no database and no key is needed, render it with the real renderer and the
real `PdfDocumentWriter`, and read the text of the result back with `PdfInspector` (a helper of the tests). The wording
that the document must contain, in particular the statement of what the signature proves and the notice about a
self-signed key, is verified by looking for it in the text.

#### Test Environment

N/A - standard test environment (everything runs in memory).

#### Acceptance Criteria

- All tests pass with zero failures.
- The document shows the review, its fingerprint, files, checklist, findings, conclusion, sign-offs, ballots appendix,
  audit head, and tool version, as far as the evidence settings include them (see the criteria for the Content scope
  below).
- A checklist comment and a comment of a finding are printed in full, however long, and run over pages without loss;
  every finding is listed, with no cap; each thread shows its status, and a thread whose starting comment is not
  included is shown as a discussion thread, not as a finding.
- A rejected document has the title "Review attestation - REJECTED" and a banner; an approved one has neither.
- The conclusion section prints the outcome and its text; the ballots appendix prints every slot's answers and the
  fingerprint, with identifiers only when the wording is off, and is absent when the input holds none.
- Every checklist answer shows who gave the agreed answer and the time of the answer, in UTC to the second like the
  other times of the document; an answer without a time shows no time, and a check that is pending or disputed shows as
  "Not answered".
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

**AttestationRenderer_Render_Document_PrintsFindingsWithTheirComments**: A review with one resolved finding of two
included comments (the finding and its resolution) is rendered. The text has the heading "Findings and comments in this
report", the location `src/a.txt, lines 3-5`, each comment as "Finding by Bob, time" and "Resolution by Ann, time" with
its text, the line "Status: Resolved", no "Discussion on" heading, and no "Comment threads" heading. This scenario is
tested by
`AttestationRenderer_Render_Document_PrintsFindingsWithTheirComments`.

**AttestationRenderer_Render_ManyFindings_ListsEveryFinding**: A review with 130 findings is rendered. All 130 are
listed, in order, from the first remark to the last, and there is no line about findings that were left out. This
scenario is tested by `AttestationRenderer_Render_ManyFindings_ListsEveryFinding`.

**AttestationRenderer_Render_ManyFindings_ListsThemInTheOrderGiven**: A review with 130 numbered findings is rendered.
The locations of the findings are in the text in exactly the order of the document. This scenario is tested by
`AttestationRenderer_Render_ManyFindings_ListsThemInTheOrderGiven`.

**AttestationRenderer_Render_ManyFindings_ContinueOntoFurtherPages**: A review with 130 findings is rendered. The
document has more pages than the sample, and the last finding is in the text. This scenario is tested by
`AttestationRenderer_Render_ManyFindings_ContinueOntoFurtherPages`.

**AttestationRenderer_Render_NoFindings_SaysNoneWereIncluded**: A document without a finding is rendered. The findings
section says that no finding or comment was included in the report. This scenario is tested by
`AttestationRenderer_Render_NoFindings_SaysNoneWereIncluded`.

**AttestationRenderer_Render_LongFindingComment_IsPrintedInFull**: A finding with a comment of several hundred numbered
words is rendered. Every word is in the text, in order, and no line holds an ellipsis. This scenario is tested by
`AttestationRenderer_Render_LongFindingComment_IsPrintedInFull`.

**AttestationRenderer_Render_FindingStatus_PrintsResolvedAndOpen**: A document has a resolved finding and an open one.
The text has one line "Status: Resolved" right after the location of the first and one line "Status: Open" right
after the location of the second. This scenario is tested by
`AttestationRenderer_Render_FindingStatus_PrintsResolvedAndOpen`.

**AttestationRenderer_Render_DiscussionThread_IsNotPresentedAsFinding**: A document has a thread whose only included
comment is a reply (its starting comment was not included). The text has the line "Discussion on" followed by the
location, a line that starts "Discussion thread, not a finding", the line "Reply by Bob, time" with the text of the
reply, no line that starts "Finding by", and no heading with the bare location. This scenario is tested by
`AttestationRenderer_Render_DiscussionThread_IsNotPresentedAsFinding`.

**AttestationRenderer_Render_Rejected_ShowsTitleAndBannerOnlyForRejection**: A rejected and an approved document are
rendered. The rejected one has the title "Review attestation - REJECTED" (also in the document title property) and the
banner text; the approved one has neither. This scenario is tested by
`AttestationRenderer_Render_Rejected_ShowsTitleAndBannerOnlyForRejection`.

**AttestationRenderer_Render_Rejected_PrintsBannerBetweenTitleAndReviewSection**: A rejected document is rendered. The
first line of the banner follows the title line directly, and the Review section comes after the banner. This scenario
is tested by `AttestationRenderer_Render_Rejected_PrintsBannerBetweenTitleAndReviewSection`.

**AttestationRenderer_Render_Conclusion_PrintsOutcomeAndText**: Documents with a rejection, an approval, and a blank
conclusion text are rendered. The section prints the outcome, the reasons for a rejection or the conclusion of an
approval in full, and no text for a blank one. This scenario is tested by
`AttestationRenderer_Render_Conclusion_PrintsOutcomeAndText`.

**AttestationRenderer_Render_BallotAppendix_ShowsSlotsAnswersAndFingerprint**: A rejected document with a ballots
appendix of two slots is rendered. Each slot is named with its reviewer and the submission time, each answer has its
outcome, justification and answerer, a section that a slot marked not applicable shows its reason, and the fingerprint is
printed with the version header of the ballots. This scenario is tested by
`AttestationRenderer_Render_BallotAppendix_ShowsSlotsAnswersAndFingerprint`.

**AttestationRenderer_Render_NoBallotAppendix_OmitsTheSection**: A document without a ballots appendix (for example a
review without a checklist) is rendered. There is no appendix section and no fingerprint. This scenario is tested by
`AttestationRenderer_Render_NoBallotAppendix_OmitsTheSection`.

**AttestationRenderer_Render_BallotAppendixWordingOff_PrintsIdentifiersOnly**: A document with the checklist wording off
is rendered. The appendix prints identifiers and outcomes, with neither the question text nor the section title. This
scenario is tested by `AttestationRenderer_Render_BallotAppendixWordingOff_PrintsIdentifiersOnly`.

**AttestationRenderer_Render_Document_ShowsSlotsSignersTimestampsAndDigests**: Slots with signatures are rendered. Each
slot shows its signer, the time, and the digest it covers. This scenario is tested by
`AttestationRenderer_Render_Document_ShowsSlotsSignersTimestampsAndDigests`.

**AttestationRenderer_Render_Document_ShowsSlotDigestOnOneLine**: A signed slot is rendered with its 64-character record
digest. The digest is a single line and is not wrapped. This scenario is tested by
`AttestationRenderer_Render_Document_ShowsSlotDigestOnOneLine`.

**AttestationRenderer_Render_ReviewerNamesOff_PrintsTheNamesTheDocumentHolds**: A document with the reviewer names turned
off is rendered, with slots that hold the labels `Reviewer 1` and `Reviewer 2` instead of names. The labels and the
signer with the role are printed as they are, and the names of the sample are not in the text. This scenario is tested
by `AttestationRenderer_Render_ReviewerNamesOff_PrintsTheNamesTheDocumentHolds`.

**AttestationRenderer_Render_Document_ShowsAuditHeadAndToolVersion**: The audit anchor and the tool version are
rendered. The text has the head hash, the number of entries, and the version. This scenario is tested by
`AttestationRenderer_Render_Document_ShowsAuditHeadAndToolVersion`.

**AttestationRenderer_Render_Document_SetsMetadataWithFingerprintAndReviewId**: A document is rendered. The properties of
the PDF hold the fingerprint and the review identifier. This scenario is tested by
`AttestationRenderer_Render_Document_SetsMetadataWithFingerprintAndReviewId`.

**AttestationRenderer_Render_SelfSignedKey_PrintsTheNotice**: A document with a self-signed key is rendered. The text has
the notice that the key is self-signed and not an organization key, with the advice that it is for development and test
only. This scenario is tested by
`AttestationRenderer_Render_SelfSignedKey_PrintsTheNotice`.

**AttestationRenderer_Render_OrganizationKey_PrintsSubjectIssuerAndNoNotice**: A document with an organization key is
rendered. The text has the subject and the issuer and does not have the notice. This scenario is tested by
`AttestationRenderer_Render_OrganizationKey_PrintsSubjectIssuerAndNoNotice`.

**AttestationRenderer_Render_CodeSigningKey_PrintsTheNoteOnlyWhenItApplies**: Documents are rendered with a key that is
for code signing only and with one that is not. The text of the first has the code-signing note (the lines of the
wrapped paragraph joined), the second does not, and the self-signed notice is printed in both. This scenario is tested
by `AttestationRenderer_Render_CodeSigningKey_PrintsTheNoteOnlyWhenItApplies`.

**AttestationRenderer_Render_Document_ExplainsWhatTheSignatureProves**: A document is rendered. The text has the heading
"What this signature proves" and the statement that the signature proves that the document has not changed since it was
signed. This scenario is tested by `AttestationRenderer_Render_Document_ExplainsWhatTheSignatureProves`.

**AttestationRenderer_Render_Document_SaysTheSignatureProvesTheOriginOfTheKey**: A document is rendered. The statement,
with its wrapped lines joined, says that the document was produced by the holder of the signing key named in the
document. This scenario is tested by `AttestationRenderer_Render_Document_SaysTheSignatureProvesTheOriginOfTheKey`.

**AttestationRenderer_Render_Document_SaysTheSignatureDoesNotIdentifyTheReviewers**: A document is rendered. The
statement, with its wrapped lines joined, says that the signature does not identify the reviewers and that the reviewers
were authenticated by ReviewCenter, which recorded their sign-offs. This scenario is tested by
`AttestationRenderer_Render_Document_SaysTheSignatureDoesNotIdentifyTheReviewers`.

**AttestationRenderer_Render_Document_SaysTheSigningTimeIsNotATrustedTimestamp**: A document is rendered. The statement,
with its wrapped lines joined, says that the signing time comes from the server clock and is not a trusted timestamp.
This scenario is tested by `AttestationRenderer_Render_Document_SaysTheSigningTimeIsNotATrustedTimestamp`.

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

**AttestationRenderer_Render_Document_PrintsContentScopeDirectlyAfterReviewSection**: A document with the default
settings is rendered. The Review section comes first and holds the fingerprint, no other section heading is between it
and the Content scope heading, and the list of files comes after the Content scope. This scenario is tested by
`AttestationRenderer_Render_Document_PrintsContentScopeDirectlyAfterReviewSection`.

**AttestationRenderer_Render_AnonymousRequest_ContentScopeSaysSo**: A document for a request without signing in is
rendered. The Content scope section says that the request was made without signing in. This scenario is tested by
`AttestationRenderer_Render_AnonymousRequest_ContentScopeSaysSo`.

**AttestationRenderer_Render_ThreadsOff_OmitsThreadsAndSaysSo**: A document with the threads turned off is rendered. The
findings and their text are missing, the section says that they are not included by project setting, and the Content
scope section names the omission. This scenario is
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
