### AttestationRenderer

![Evidence Structure](EvidenceView.svg)

#### Purpose

`AttestationRenderer` lays out the record of a completed review as the unsigned attestation PDF. Its single
responsibility is the content and the wording of the document: what the review is, what it contained, what the
reviewers answered and discussed, who signed, which key will sign the document, and what that signature proves and does
not prove. It does not decide whether a review is complete and does not sign.

#### Data Model

The input is an `AttestationDocument`, built by `AttestationService` from the review, so that the renderer reads nothing
and has no access to the database. It holds the project and the review (identifier and title, and for a repository
review the repository, the baseline and head commits, and the review-set), the content fingerprint, the record digest and
its version, the files (path, status, SHA-256, and size), the checklist sections with their items, answers, comments,
and the time of each answer (`AttestationCheck.AnsweredAt`), the count of open threads and the summaries of the
resolved ones, the reviewer slots with their signatures
(signer, role, time, the digest signed, and its meaning), the reopen history of the review (time, who, reason, and the
digest of the record that was reopened; none when the review was never reopened), the head of the audit chain with
its length and whether it is intact, the version of the tool, the time of the creation, who asked for it (a name,
a label, or a statement that the
request was anonymous), the description of the key
(`AttestationKeyInfo`), the evidence settings that were in effect (`Content`), and whether the request was anonymous.
The person fields of the input are already the final text: `AttestationService` replaces names with labels when the
settings keep names out, so the renderer never sees a name that it has to hide. The output is the
bytes of a PDF. Two public constants hold the standing statements: `SelfSignedNotice` ("SELF-SIGNED KEY - NOT AN
ORGANIZATION KEY. Dev/test only; do not rely on it as organizational evidence.") and `ProvesStatement`. The document has
these sections in order: the review, the content scope, the files, the checklist, the threads, the sign-offs, the reopen
history, the audit anchor, and the signature. The files and the threads are left out when their switch is off, and the
reopen history only appears when the review was reopened at least once.

#### Key Methods

**Render**: Builds the unsigned attestation.

- *Parameters*: `AttestationDocument document`.
- *Returns*: `byte[]` — the PDF written with `PdfDocumentWriter`.
- *Preconditions*: `document` is not null.
- *Postconditions*: The document properties carry the fingerprint and the review identifier. The same document gives the
  same bytes. A large review continues on further pages. Checklist comments are printed in full and are never cut: a
  comment is the reviewer's own words and part of the signed evidence, and a long one is wrapped and runs over pages.
  The only caps are on the list of resolved threads, listed below. The counts of open and resolved threads are always
  exact. The content switches of the document apply to every PDF:
  - *Discussion threads* off: the threads section (the counts and the list of resolved threads) is left out.
  - *Files* off: the list of files with their status, hashes, and sizes is left out. The number of files is still
    printed in the content scope, and the content fingerprint, which covers them, stays in the review section.
  - *Checklist wording* off: a section is printed by its identifier instead of its title and a check by identifier and
    outcome without its text, with a line that says so; the justifications and comments (the reviewers' own words) stay.
  - *Git context* off: for a review of a repository, the repository name, the head and baseline revisions are replaced
    by one line that says the git context is omitted; the line "Files uploaded to ReviewCenter" of an uploaded review is
    not git context and stays.
  - *Reviewer names* off: nothing is hidden by the renderer, because the labels are already in the input; the switch is
    only reported in the content scope.

**Answer time**: Below a check that has an answer, a small italic line reads `Answered by X, yyyy-MM-dd HH:mm:ss UTC`,
with the time in UTC to the second, formatted like every other time of the document. An answer without a time prints
`Answered by X` only, and a time without a name prints `Answered at <time>`. A carried answer shows the time it was
originally given, followed by its "Carried from review X" line. The time is not personal data, so the reviewer names
switch does not hide it. It is not part of the record digest (see below), so showing it changes no signature.

**Follow-up review**: A review that was created as a follow-up has one more row in the review section, "Follows review",
with the text `X (N answers and M threads carried)` (the followed review and the numbers of carried answers and open
threads at creation), and a review that follows nothing has no such row, so its document is unchanged. In the checklist
a not-applicable section, and a check whose answer was carried over, gets a small italic line "Carried from review X"
below it, and a listed thread that was carried gets the same line, so the document always says which statements were
given in this review and which were taken over (the answerer shown is the person who gave the answer originally). Open
threads are only counted; a thread is listed only when it is resolved, and a carried open thread that was resolved in
the follow-up is listed with the same "Carried from review X" line.

**Reopen history**: A review that was reopened by an administrator after an approval has a section "Reopen history"
after the sign-offs and before the audit anchor, and a review that never was has none, so its document is unchanged.
The section says in an italic sentence that the review was approved earlier and reopened by an administrator, and that
the sign-offs above are of the current record, and then prints for every reopen, oldest first, the rows "Reopened at",
"Reopened by", "Reason", and "Previous digest" (in a fixed-width font). The names come from the input, so with the
reviewer names switch off they are labels. The reason is the administrator's own words and is printed in full, like a
comment.

**Content scope**: Every PDF has a section "Content scope", right after the review section, that prints the settings as
they were when the document was generated (so a reader can see what a project's own setting was on that day), says
whether the request was made without signing in, states for each of the five switches whether its part is included or
omitted, adds the number of files when the file list is omitted, and names in one sentence what is left out of the
document (`EvidenceSettings.OmittedSections`) or states that nothing is. It also states that the record digest covers
the complete record, including what the document leaves out. The section is printed also when nothing is left out, so a
reader never has to guess whether a part is missing on purpose. The wording is built from the settings, not from the
data, so a document with an omitted section and one without can never read the same.

**Caps**: Each remaining cap is stated here, because anything cut is a place where the document says less than the
record.

- *Resolved-thread list*: at most `MaxListedResolvedThreads` (100) resolved threads are listed, in the order of the
  document (the order in which the threads were created, which is the order of their identity in the store). When there
  are more, one line says `and N more resolved threads (see the review record)`. This bounds the size of the PDF and the
  time to make it; the count printed under "Resolved" stays exact.
- *Summary of a resolved thread*: `AttestationService` keeps at most 300 characters of the author and the first comment
  of a resolved thread, and ends a cut summary with an ellipsis; the renderer prints the summary as it receives it. It
  is an overview of a thread that was closed, and the complete thread is in the review record.
- *Nothing else is cut*: not the checklist comments (justifications), the checklist item text, the file paths, the names,
  or the signatures, unless the evidence settings of the project turn that part off, in which case the content scope
  says so. A line that is wider than the page is wrapped by the writer (a single word wider than a line is
  broken where the line is full), not clipped; runs of spaces in a text are shown as one space.

#### Error Handling

A null document raises `ArgumentNullException`. Text that the PDF fonts cannot show becomes question marks (see
`PdfDocumentWriter`). A review without a checklist is reported as such in the document and is not an error.

**What the digest binds and what the PDF shows**: The record digest (`ReviewCenter record v2`) that the reviewers'
signatures bind to covers: the content fingerprint; the source of the files (repository identity, baseline and head
commit, selection, review-set, and for every changed file its path, kind, and baseline and head hashes); the checklist
the review was created with (version, and for every section its identifier and title and for every check its
identifier and wording); every checklist answer (check identifier, outcome, justification, and who answered, by display
name and user name); the sections that do not apply (identifier and reason); and the report: every thread that has a
comment included in the report, with its anchor and, for each included comment, the author, the user name, the text,
and the time; and, only when the review was
reopened, the number of reopens.
It does **not** cover chat (comments that are not included in the report), the state and the creation time of a thread,
the time an answer was given (`AnsweredAt`), the review title, the project name, the repository
name, the reviewer slots, and the audit chain head, which the PDF may also show. The PDF does print the time of every
answer (see the checklist rendering above), so a burst of answers within seconds is visible to a reader, and the time is
protected by the PDF signature, not by the reviewers' own signatures; it is the clock of the server, not a trusted
timestamp. The reopen history (who reopened the
review, when, and why) is shown by the PDF but only its count is bound by the digest. The signature over the PDF covers
every byte of the PDF, so these extra items are protected by the PDF signature (and, for what the audit event holds,
by the audit chain); they are not bound by the reviewers' own signatures on the record.

The wording is the one place where the subsystem can mislead, so it is deliberate. The signature section says that the
signature proves that the document is unchanged since the signing and that it was signed by the holder of the key in
the embedded certificate; it says that the signature does not prove the identity of the reviewers, that their identity is
the re-authenticated signatures shown in the sign-offs section, and that the time of the signing is the clock of the
server and not a trusted timestamp. For a self-signed key the document begins the signature section with the prominent
notice. For an organization key it names the subject and the issuer of the certificate and prints no warning. For a
certificate that is for code signing only (`AttestationKeyInfo.IsCodeSigningOnly`) it also prints the one-line
`AttestationKeyPolicy.CodeSigningNotice` below the issuer text, because Adobe Acrobat will not trust such a certificate
for document signatures; the key is still used.

#### Dependencies

- **PdfDocumentWriter** — lays the content out.
- **AttestationKeyInfo** — the description of the key that will sign (subject, issuer, thumbprint, whether it is
  self-signed, and whether it is for code signing only).
- **EvidenceSettings** — Core; the five content switches and the description of what is left out (`IsReduced` and
  `OmittedSections`).

#### Callers

- **AttestationService** — renders the document before it is signed.
