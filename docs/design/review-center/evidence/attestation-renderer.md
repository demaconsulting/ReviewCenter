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
  same bytes. Long text is cut at a fixed length with a mark, and a large review continues on further pages.

#### Error Handling

A null document raises `ArgumentNullException`. Text that the PDF fonts cannot show becomes question marks (see
`PdfDocumentWriter`). A review without a checklist is reported as such in the document and is not an error.

The wording is the one place where the subsystem can mislead, so it is deliberate. The signature section says that the
signature proves that the document is unchanged since the signing and that it was signed by the holder of the key in
the embedded certificate; it says that the signature does not prove the identity of the reviewers, that their identity is
the re-authenticated signatures shown in the sign-offs section, and that the time of the signing is the clock of the
server and not a trusted timestamp. For a self-signed key the document begins the signature section with the prominent
notice. For an organization key it names the subject and the issuer of the certificate and prints no warning.

#### Dependencies

- **PdfDocumentWriter** — lays the content out.
- **AttestationKeyInfo** — the description of the key that will sign (subject, issuer, thumbprint, and whether it is
  self-signed).

#### Callers

- **AttestationService** — renders the document before it is signed.
