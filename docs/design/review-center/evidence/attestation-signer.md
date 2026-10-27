### AttestationSigner

![Evidence Structure](EvidenceView.svg)

#### Purpose

`AttestationSigner` signs the PDF that `AttestationRenderer` made. Its single responsibility is to add a detached
CMS signature (`adbe.pkcs7.detached`, SHA-256) in a signature field, so that a PDF reader or any CMS library can verify
the document without ReviewCenter.

#### Data Model

The signer is stateless and thread-safe. It works on the layout of `PdfDocumentWriter` (a classic cross-reference table,
no compression), which it finds by a bounded search of the end of the file. It adds an incremental update, so the
original bytes stay exactly as they were: a new revision of the page that carries the signature field, an
interactive form (`AcroForm`) that lists the field, the signature dictionary with the filter, the sub-filter, the
reason, the signing time, and the byte range, and a new cross-reference section and trailer. The byte range is a
placeholder of fixed width that is filled in last, with four numbers that cover the whole file except the hexadecimal
value of the signature. The value has room for 32768 bytes of signature; the signature includes the certificate of the
signer and the intermediate certificates of the key.

#### Key Methods

**Sign**: Signs a document.

- *Parameters*: `byte[] pdf` — the unsigned document; `AttestationSigningKey key` — the key, the certificate, and the
  intermediate certificates; `DateTimeOffset signedAt` — the signing time that is written to the signature, which is
  the clock of the server and not a trusted timestamp (it is written both to the signature dictionary and as a signed
  attribute of the CMS signature); `string reason` — the reason text.
- *Returns*: `byte[]` — the document with the signature.
- *Preconditions*: `pdf` has the layout of `PdfDocumentWriter` and is not signed.
- *Postconditions*: The result starts with the bytes of `pdf`. The signature is made with the private key handle that
  the key holds (RSA or ECDSA, SHA-256), through the `CmsSigner` of `System.Security.Cryptography.Pkcs`, so a key that
  cannot be exported signs as well as one that can. The same input and RSA key give the same bytes. Data that is
  appended after the signing is not covered by the byte range.

#### Error Handling

A document that is not the layout of the writer, or that already has a signature, raises `InvalidOperationException`:
the signer finds the places it changes from the layout of its own writer, and guessing would produce a document that
looks signed and is not. A certificate chain that does not fit into the space for the signature, and a failure of the
key to sign (a smart card that was removed, access to the key that was denied), raise an `AttestationKeyException`
with a
message that says what to check, so that no broken document is returned. Null arguments raise `ArgumentNullException`.

#### Dependencies

- **AttestationSigningKey**, **AttestationKeyException** — the key and the error type of the `AttestationKeyProvider`
  unit.
- **System.Security.Cryptography.Pkcs** — the CMS signature (see the OTS design).

#### Callers

- **AttestationService** — signs the document it rendered.
