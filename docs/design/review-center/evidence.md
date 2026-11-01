## Evidence

![Evidence Structure](EvidenceView.svg)

### Overview

The `Evidence` subsystem turns the approval of a review into a document that can leave the tool: a PDF attestation of a
completed review that the server signs digitally, and a list of the completed reviews of a project in the form that
ReviewMark
reads. It holds no domain rule. Whether a review is complete is decided by `SigningService` of `Core`, and the subsystem
only
asks. It contains seven units: `PdfDocumentWriter`, a small writer of
plain PDF files; `AttestationRenderer`, which lays out the record of a review in that writer; `AttestationSigner`, which
adds a detached CMS signature to the result; `AttestationKeyProvider`, which finds the key that signs (a self-signed key
that the server creates, a certificate of a certificate store, or a certificate file); `AttestationService`, which
checks the review, gets
the
key, renders, signs, and records the release in the audit trail; `AttestationEndpoint`, the download address; and
`EvidenceIndexService`, which lists the completed reviews that have a
review-set id as the evidence index. The
units are internal to the application, except `AttestationKeyException`, which `Hosting` and the pages see. The
subsystem uses one OTS item, `System.Security.Cryptography.Pkcs`, for the CMS signature, and no other library.

### Interfaces

**AttestationEndpoint**: The download of the attestation.

- *Type*: HTTP endpoint, `GET /projects/{projectId}/reviews/{reviewId}/attestation.pdf`.
- *Role*: Provider.
- *Contract*: A signed-in user who may view the project (a member, or a system administrator) gets the signed PDF
  (`application/pdf`, file name
  `attestation-{projectId}-{reviewId}.pdf`, `Cache-Control: no-store`). The statuses are 200 for the document, 404 (empty
  on this route) for a review that does not exist and for a project that the user may not view, 409 (a problem
  description, "The review is not complete") for a review that is not
  approved (a review concluded as rejected gets the message "The review was rejected. A signed attestation of a rejected
  review is not available yet.") and 409 ("The review changed during generation") for a review that changed while the
  document was being
  prepared, and 500 (a problem description, "Attestation signing key unavailable", with the reason) when the key cannot
  be used. A visitor who is not signed in is redirected to the sign-in page by the authentication scheme; anonymous
  access, when a project allows it, exists only on the API route of the `Api` subsystem, never on this page route.
- *Constraints*: The attestation is made for every request and is stored nowhere; every successful request adds an
  `attestation.generated` event to the audit trail. The route includes the project number because reviews are
  addressed by project everywhere else in the system. What the document contains follows the evidence settings of the
  project (see Design).

**EvidenceIndexService**: The evidence index of a project.

- *Type*: In-process .NET class.
- *Role*: Provider, to `ApiEndpoints` of the `Api` subsystem, which serves it as `GET
  /api/projects/{projectId}/evidence/index.json`.
- *Contract*: `BuildAsync` takes the actor (or `null` for a request without a signed-in user) and the project. It
  refuses an actor who may not view the project, and a request without an actor for a project that does not exist or
  that does not allow an anonymous evidence index, as for a project that does not exist. It returns one entry (the
  review-set id, the content fingerprint, the UTC day of the
  latest
  signature,
  and the review it stands for) for each review-set id and fingerprint of the complete reviews of the project that have
  a
  review-set id, ordered by id and then by fingerprint. See the design of `EvidenceIndexService`.
- *Constraints*: Nothing is stored and no audit event is written: the index is made for each request.

**IAttestationKeyProvider (as AttestationKeyProvider)**: The supplier of the signing key.

- *Type*: In-process .NET interface with a status.
- *Role*: Provider, to `AttestationService`, `ServerHost`, and `ReviewPage`.
- *Contract*: `GetSigningKey` returns the key (a handle of the private key, the certificate, and the intermediate
  certificates) or throws an `AttestationKeyException` that says what to correct. `Describe` returns the status for the
  review page (whether a key can be used, whether it is self-signed, the subject, the thumbprint, and the problem) and
  never throws.
- *Constraints*: The private key is never exported or written anywhere by the server.

**Attestation settings**: The `Attestation` configuration section, bound to `AttestationOptions`.

- *Type*: Configuration (`appsettings.json` and environment variables, for example `Attestation__Provider`).
- *Role*: Consumer.
- *Contract*: `Provider` is `SelfSigned` (the default), `CertificateStore` or `File`; `RequireTrustedKey` (default
  false); `CertificateThumbprint`, `StoreLocation` (`CurrentUser` by default or `LocalMachine`), and `StoreName` (`My`
  by default) for the certificate store; `CertificatePath`, `PrivateKeyPath`, `CertificatePassword` (a secret) and
  `CertificatePasswordFile` for the File provider; `ExpiryWarningDays` (default 30, 0 for no warning) for the
  certificate store and the File provider; `SelfSignedFolder` for the self-signed key.
- *Constraints*: An unknown provider, a missing or malformed thumbprint, a missing or unreadable certificate file, a
  missing or wrong password, a negative `ExpiryWarningDays`, or a key that does not meet the rule stops the server at
  start-up with an error that names the setting. A private key that is not protected by a password stops it only when
  `DataProtection:RequireEncryptedKeys` is set; otherwise it is loaded with a warning.

**Core services (consumed)**: `SigningService`, `AuditService`, `IReviewStore`, `IProjectAccess`, and
`EvidenceSettingsService`.

- *Type*: In-process .NET classes and interfaces.
- *Role*: Consumer.
- *Contract*: The approval decision and the record digest, the audit trail (the head of the chain, and the recording of
  the release), the review with its files, checklist, threads, and signatures, the decision which actor may view
  which project, and the evidence settings of a project (`EvidenceSettings`: which parts the PDF contains and whether
  the index and the PDFs may be read without signing in).
- *Constraints*: None.

### Design

**What the attestation is for**: Approval inside the tool is of little use to someone who has no access to it. The
attestation is a portable record of the approval: it states what was reviewed (the content fingerprint, the revisions,
the files), what the reviewers agreed in their submitted ballots (and who answered each check and when) and discussed,
who signed which slot and
when, the reopen history of the review
(when it was reopened after an approval, by whom, and why; only for a review that was reopened), what a follow-up
review took over from the review it follows (only for a follow-up), and the head of
the audit chain at
the time. The server then signs the document, so that a change made after the signing shows in a PDF reader or any CMS
tool.

**What the signature proves, and what it does not**: This is stated in the document and in the Help, because a digital
signature is easy to overstate. The signature proves **integrity**: the signed bytes are the bytes that the server
signed. It proves **origin of the server key**: the document was signed by the holder of the key in the certificate
that is embedded in it. It does **not** prove who the reviewers are. The reviewers are identified by their signatures in
the review record, each made after a re-authentication with the password, and the attestation reports them. The signing
time in the signature is the clock of the server; **there is no timestamp authority**, so the time is not a trusted
timestamp and proves nothing about when the document really existed. A reader who needs a trusted time must get it
elsewhere until a timestamp authority is supported.

**Writing and signing a PDF**: `PdfDocumentWriter` writes a plain PDF 1.5 file with a classic cross-reference table,
with
standard fonts, no compression, and no embedded font, so it has no dependency, always gives the same bytes for the
same input, and has a layout that `AttestationSigner` can rely on. The price is that characters outside the WinAnsi
encoding are shown as question marks. `AttestationSigner` leaves those bytes unchanged and adds an incremental update
with a signature field, a form, and a signature dictionary (`adbe.pkcs7.detached`) whose value is a detached CMS
signature
with SHA-256 over the whole file except the hexadecimal value of the signature. The signature is a **certifying
signature** (DocMDP permission 1, no changes allowed): a PDF reader reports any later change, and Adobe Acrobat shows
that no changes are permitted. The space for the signature is reserved
before the signature is known, so a certificate chain that does not fit is reported and no broken document is produced.
The signer works only with a document that has the layout of its own writer.

**The keys**: Three providers exist. The **self-signed provider** is the default, so that a new installation works without
configuration. It creates an RSA key and a self-signed certificate on first use, in a file in a folder beside the Data
Protection keys (`attestation-selfsigned` under the folder of `DataProtection:KeysPath`, or
`Attestation:SelfSignedFolder`),
and loads the same file afterwards, so that the key persists across restarts. The file
(`attestation-selfsigned.pfx.protected`) holds the PFX protected by the Data Protection key ring of the server, so a
copy of the file is useless without the ring, and the file is restricted to its owner on systems that support it. The
key is **for development and test only**. The key says so in its certificate subject, in
the log, in the user interface, in the document, and in the Help. A PDF reader reports the signer of a self-signed
document as not trusted, which is correct. The **certificate store provider** uses the certificate with a configured
thumbprint from a certificate store of the server account. It never exports the key: it keeps the handle of the private
key (a key of the Windows key store, a smart card, or a hardware module) and signs through it, so the key never reaches
the memory of the server in a form that could be copied. The server account needs access to the key. The provider
supports RSA and ECDSA keys, and keeps the intermediate certificates of the chain (without revocation checks) so that
the signature carries them. It does not sign a test value at start-up, so a smart card is not asked for its PIN just
because the server
started. The **file provider** reads a certificate with its private key from a PFX or PEM file that an administrator
deploys, with the password from a setting or from a password file, keeps the key in memory only, and takes the other
certificates of the file as the chain; a key without a password is a warning, or an error when
`DataProtection:RequireEncryptedKeys` is set. For the certificate store and file providers the server warns, at start
and on the review page, when the certificate ends within `ExpiryWarningDays` or has ended; the self-signed key renews
itself. A certificate that is meant for code signing only is not suited to signing a PDF: Adobe Acrobat will not trust
it for document signatures.
Use a document-signing certificate (an internal certification authority is fine, if its root is distributed to
the readers). The provider logs a warning at start-up for such a certificate, and the attestation prints a one-line
note in its signing-key section; it does not refuse the certificate. The server does not check the certificate chain
or revocation; the PDF viewer does.

**RequireTrustedKey**: When this setting is on, the server refuses a key that is self-signed, a certificate whose key
usage does not allow digital signatures or non-repudiation, and a certificate that is not within its validity period. It
checks **only** that. It does **not** check that the certificate chain leads to a trusted root and does **not** check
revocation, because the server cannot know whom its readers trust. The rule is checked at start-up and again at every
signing, so a certificate that stops being valid while the server runs (an expired certificate) stops the attestation
but not the server, and the review page then shows the reason instead of the download. A self-signed key and a
certificate without a signing key usage cannot change while the server runs, so they are refused at start-up and the
server does not start. Because the key is obtained at start-up, a
configuration that cannot work fails at once, with an error that names the setting; the price is that a server whose key
is in a hardware module that is not present does not start.

**Release of an attestation**: `AttestationService` first reads the project and decides whether the asker may read its
attestations: an actor must be allowed by `IProjectAccess` to view the project, and a request without an actor (an
anonymous request from the API) is allowed only when the project switch `AnonymousAttestations` is on. Whoever is not
allowed gets exactly the answer for a review that does not exist, so that the answer does not tell a
non-member whether the project or the review exists. Then it asks `SigningService` for the approval, so a missing
review is
reported as not found and a review that is not complete is refused without the key being touched. Then it gets the key
(a key problem is reported, and nothing is recorded), reads the review, checks again that the record digest is the one
that was approved, verifies the audit chain to get its head, renders the document, reads the review and the project
settings again and
checks that it is still complete and unchanged and that the settings are the ones that were rendered (otherwise the
attestation is refused and nothing is recorded), and
signs it. Last, it records the
release in the audit trail with the record digest, the chain head that is printed in the document, the SHA-256 hash of
the PDF, and the key; the actor of the event is the user, or `anonymous` for a request without an actor. The event is
written strictly: if the trail cannot be written, nothing is released. The event
follows the signing, so the chain head in the document precedes its own event. The head is found by verifying the chain,
which reads every entry on every request; this is intentional integrity evidence and is acceptable because an
attestation is rare. A change that happens between the second check and the audit event is not seen; that window is
tiny and the event states the digest that was attested. The user interface offers the download only
for an approved review, but the endpoint decides again, so the rule does not depend on the page. The document prints
every checklist comment in full; the only caps are on the list of resolved threads (see `AttestationRenderer`).

**Approval lock, reopen, and the evidence**: A concluded review is locked in `Core`, so the record that an attestation
describes cannot change after the approval, and the "changed during generation" window that this subsystem checks is
only left for a reopen or a stale signature. Reading, the attestation, and the evidence index keep working on a locked
review. When an administrator reopens a review, the review is no longer complete: the attestation endpoint answers as for
a review that is not approved, and the review leaves the evidence index, until the review is signed again. The earlier
approval stays visible as the reopen history of the next attestation, which lists each reopen with its time, who reopened
it (a name or a label, as the reviewer names switch decides), the reason, and the digest of the record that was
reopened. The attestation of an earlier approval that was downloaded before the reopen is a signed document of that
earlier record and is not invalidated.

**Evidence settings and the content of a PDF**: Each project has evidence settings (see `EvidenceSettings` in `Core`).
Five switches decide what every attestation PDF of the project contains: discussion threads, reviewer names, the
wording of the checklist, the file list with hashes, and the git context (repository name and revisions); all are on by
default. They apply to **every** PDF of the project, not only to anonymous ones, so a project that keeps names out of
its PDFs keeps them out for its members too. `AttestationService` reads the settings once at the start and builds the
document from them: with the reviewer names switch off every person field of the document is already replaced
(`Reviewer 1 (primary)`, `Reviewer 1 (alternate)` for the members of a slot, `Participant N` in the order of first
mention for everybody else, and a fixed label for who asked), and `AttestationRenderer` leaves out the parts that the
other switches turn off. The settings are read again, with the review, before the document is signed; a difference gives
`Changed`, so a document never mixes two settings. Every PDF carries a **Content scope** section that states, from the
settings that were in effect at the time of generation, what the document leaves out, so a reader cannot take a
reduced document for a complete one. The signature is made after the redaction and covers the redacted bytes, so it
stays valid. The record digest, which the reviewers signed, still covers the complete record, whatever the PDF shows.
Two switches are not about content: an anonymous evidence index and anonymous attestation PDFs say whether the two API
routes may be read without a token (see the `Api` subsystem).

**The evidence index**: ReviewMark checks in a pipeline that every review-set has a current review, and it can read that
evidence from a URL as an `index.json` file. `EvidenceIndexService` makes that file from the approvals of ReviewCenter,
so the
evidence is not kept twice. The completion rule is the one of the attestation: a review is listed only while every slot
has
signed the record that the review has now. Only a review that has a review-set id is listed, because ReviewMark matches
by
that id and a made-up id could satisfy a review-set that nobody reviewed. The result is always `pass`, because only an
approved review is listed: a review concluded as rejected is locked but never listed. ReviewMark keeps one entry for an
id and fingerprint, so the index has one: the
review
that was completed last. The `file` of an entry is the address of the attestation, which the endpoint builds from the
request.
The fingerprint is the content fingerprint of the review, which is the algorithm of ReviewMark (SHA-256 of the sorted
SHA-256 hashes of the files); a contract test checks it against an independent implementation. A review that was made
from the changes since a baseline holds only the changed files, so its fingerprint differs from the one that ReviewMark
computes for the whole review-set, and ReviewMark shows it as stale. The index discloses the review-sets and
fingerprints of a project, so `BuildAsync` refuses an actor who may not view the project with the rule of a missing
project, and refuses a request without an actor unless the project allows an anonymous evidence index (with the same
rule, so a project that does not allow it looks like a project that does not exist).

**Known limitations and planned follow-ups**: These are not implemented, and the subsystem has no requirement and no
dependency for them. A provider for Azure Key Vault would keep the key in the vault and sign through the Azure SDK; the
design leaves a place for it, because the key type that the signer uses is an RSA or ECDSA object that need not hold the
key itself. A timestamp authority (RFC 3161) would add a trusted time to the signature as an unsigned attribute of the
CMS signature. The attestation of a review can also be downloaded through the JSON API with a personal access token, or
without one when the project allows it (see
the `Api` subsystem). The PDF
is generated for every request and is not stored; the audit event holds its hash, so a copy can be checked against the
trail. Tests of the certificate store provider use a temporary certificate in the store of the current user and skip
themselves when the store is not available; the tests of the file provider write their certificates to temporary
files and never use a store; the local machine store and a real hardware module need a manual check.
