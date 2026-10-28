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
its version, the files (path, status, SHA-256, and size), the checklist sections with their items, answers, and
comments, the count of open threads and the summaries of the resolved ones, the reviewer slots with their signatures
(signer, role, time, the digest signed, and its meaning), the head of the audit chain with its length and whether it is
intact, the version of the tool, the time of the creation, the user who asked for it, and the description of the key
(`AttestationKeyInfo`). The output is the
bytes of a PDF. Two public constants hold the standing statements: `SelfSignedNotice` ("SELF-SIGNED KEY - NOT AN
ORGANIZATION KEY. Dev/test only; do not rely on it as organizational evidence.") and `ProvesStatement`. The document has
these sections in order: the review, the files, the checklist, the threads, the sign-offs, the audit anchor, and the
signature.

#### Key Methods

**Render**: Builds the unsigned attestation.

- *Parameters*: `AttestationDocument document`.
- *Returns*: `byte[]` — the PDF written with `PdfDocumentWriter`.
- *Preconditions*: `document` is not null.
- *Postconditions*: The document properties carry the fingerprint and the review identifier. The same document gives the
  same bytes. A large review continues on further pages. Checklist comments are printed in full and are never cut: a
  comment is the reviewer's own words and part of the signed evidence, and a long one is wrapped and runs over pages.
  The only caps are on the list of resolved threads, listed below. The counts of open and resolved threads are always
  exact.

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
  or the signatures. A line that is wider than the page is wrapped by the writer (a single word wider than a line is
  broken where the line is full), not clipped; runs of spaces in a text are shown as one space.

#### Error Handling

A null document raises `ArgumentNullException`. Text that the PDF fonts cannot show becomes question marks (see
`PdfDocumentWriter`). A review without a checklist is reported as such in the document and is not an error.

**What the digest binds and what the PDF shows**: The record digest (`ReviewCenter record v4`) that the reviewers'
signatures bind to covers: the content fingerprint; the source of the files (repository identity, baseline and head
commit, selection, review-set, and for every changed file its path, kind, and baseline and head hashes); every checklist
answer (check identifier, outcome, and justification); the sections that do not apply (identifier and reason); and every
thread with its comments (anchor, state, time, and for each comment the author, the user name, the text, and the time).
It does **not** cover items that the PDF also shows: the wording of the checklist items and the titles of the sections
(only the check identifiers are in the digest), the checklist version, who answered each check and when
(`Answered by`), the review title, the project name, the repository name, the reviewer slots, and the audit chain head.
The signature over the PDF covers every byte of the PDF, so these extra items
are protected by the PDF signature (and, for what the audit event holds, by the audit chain); they are not bound by the
reviewers' own signatures on the record. Widening the digest to cover them would be a record format change and is a
known limitation, planned as a follow-up; the digest format is unchanged here.

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

#### Callers

- **AttestationService** — renders the document before it is signed.
