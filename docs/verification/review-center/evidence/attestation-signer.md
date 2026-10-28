### AttestationSigner

#### Verification Approach

`AttestationSigner` is verified with tests defined in `AttestationSignerTests.cs`. The tests sign a document written by
the real `PdfDocumentWriter` with temporary keys and certificates made by the test helper `TestKeys` (RSA, ECDSA, a
key that cannot be exported, and an organization-like chain), and verify the result in two independent ways: with
`SignedCms` of `System.Security.Cryptography.Pkcs` over the bytes of the byte range, and with `PdfInspector`, which
reads the PDF structure (the cross-reference table, the byte range, the signature dictionary, and the DocMDP
permission). The tests of the
refusals use a PDF that the writer did not make, a signed one, and a chain that is too large. The produced PDF was also
loaded once with the independent libraries pypdf and pyHanko (not part of the automated tests): both read the DocMDP
reference, pyHanko reports the permission as "no changes", and the signature as intact, valid and unmodified.

A **manual check** remains, because Adobe Acrobat cannot run in the automated tests: open a freshly downloaded
attestation in Adobe Acrobat and check that the Signature Properties (or the signature panel) say that no changes are
permitted (certified document, P=1). The signer is shown as not trusted for a self-signed or code-signing certificate;
that is expected.

#### Test Environment

N/A - standard test environment (keys and certificates are created in memory by each test).

#### Acceptance Criteria

- All tests pass with zero failures.
- A signature of an RSA key and of an ECDSA key verifies, and so does one of a key that cannot be exported.
- The original bytes are unchanged, the form and the signature field are declared, and the byte range covers everything
  except the signature value.
- The signature is a certifying signature with DocMDP permission 1 that the catalog names, and it still verifies, for
  RSA and ECDSA; changing the permission to another value makes the verification fail.
- A change of a signed byte makes the verification fail, and appended data is not covered.
- The chain is embedded, and one that does not fit is an `AttestationKeyException`.
- The same input gives the same bytes, and a signed or a foreign document is refused.

#### Test Scenarios

**AttestationSigner_Sign_RsaKey_ProducesVerifiableDetachedSignature**: A document is signed with an RSA key. The CMS
signature verifies over the byte range, uses SHA-256, and is detached. This scenario is tested by
`AttestationSigner_Sign_RsaKey_ProducesVerifiableDetachedSignature`.

**AttestationSigner_Sign_EcdsaKey_ProducesVerifiableDetachedSignature**: The same with an ECDSA key. This scenario is
tested by `AttestationSigner_Sign_EcdsaKey_ProducesVerifiableDetachedSignature`.

**AttestationSigner_Sign_NonExportableKey_ProducesVerifiableSignature**: A key whose private part cannot be exported
signs the document. The signature verifies. This scenario is tested by
`AttestationSigner_Sign_NonExportableKey_ProducesVerifiableSignature`.

**AttestationSigner_Sign_Output_StartsWithTheOriginalBytes**: A document is signed. The first bytes of the result are
the original document. This scenario is tested by `AttestationSigner_Sign_Output_StartsWithTheOriginalBytes`.

**AttestationSigner_Sign_Output_DeclaresAcroFormSignatureFieldAndAdobeFilter**: A document is signed. The result declares
the form, the signature field, and the `adbe.pkcs7.detached` sub-filter. This scenario is tested by
`AttestationSigner_Sign_Output_DeclaresAcroFormSignatureFieldAndAdobeFilter`.

**AttestationSigner_Sign_ByteRange_CoversEverythingExceptTheContentsHex**: The byte range of a signed document is read.
It starts at zero, ends at the end of the file, and leaves out exactly the hexadecimal signature value. This scenario is
tested by `AttestationSigner_Sign_ByteRange_CoversEverythingExceptTheContentsHex`.

**AttestationSigner_Sign_Output_IsCertifiedWithDocMdpNoChangesAllowed**: A document is signed and the PDF structure is
read. The signature dictionary has a DocMDP reference whose permission is 1 (not 2), the catalog names the same
signature object in its permissions, and there is one signature and one DocMDP entry. This scenario is tested by
`AttestationSigner_Sign_Output_IsCertifiedWithDocMdpNoChangesAllowed`.

**AttestationSigner_Sign_CertifiedOutput_SignatureStillVerifiesOverTheByteRange**: Documents are signed with an RSA key
and with an ECDSA key. The CMS signature verifies over the byte range, the signer is the key's certificate, the covered
bytes hold the permission, and the update is a consistent extension of the original. This scenario is tested by
`AttestationSigner_Sign_CertifiedOutput_SignatureStillVerifiesOverTheByteRange`.

**AttestationSigner_Sign_CertifiedOutput_TamperedPermission_SignatureNoLongerVerifies**: The permission 1 of a signed
document is changed to 2. The verification fails, so the permission is covered by the signature. This scenario is tested
by `AttestationSigner_Sign_CertifiedOutput_TamperedPermission_SignatureNoLongerVerifies`.

**AttestationSigner_Sign_TamperedContent_SignatureNoLongerVerifies**: A byte of the signed content is changed. The
verification fails. This scenario is tested by `AttestationSigner_Sign_TamperedContent_SignatureNoLongerVerifies`.

**AttestationSigner_Sign_DataAppendedAfterSigning_IsNotCoveredByTheByteRange**: Data is appended to a signed document.
The byte range no longer reaches the end of the file, which is how a reader detects it. This scenario is tested by
`AttestationSigner_Sign_DataAppendedAfterSigning_IsNotCoveredByTheByteRange`.

**AttestationSigner_Sign_OrganizationKeyWithChain_EmbedsTheChainCertificates**: A key with a certificate chain signs the
document. The signature holds the certificate of the signer and the intermediate certificates. This scenario is tested
by `AttestationSigner_Sign_OrganizationKeyWithChain_EmbedsTheChainCertificates`.

**AttestationSigner_Sign_SameInputAndRsaKey_ProducesIdenticalBytes**: The same document is signed twice with the same RSA
key and time. The bytes are equal. This scenario is tested by
`AttestationSigner_Sign_SameInputAndRsaKey_ProducesIdenticalBytes`.

**AttestationSigner_Sign_AlreadySignedPdf_Throws**: A signed document is signed again. An exception is raised. This
scenario is tested by `AttestationSigner_Sign_AlreadySignedPdf_Throws`.

**AttestationSigner_Sign_NotAnAttestationPdf_ThrowsInvalidOperation**: A PDF that the writer did not make is signed.
`InvalidOperationException` is raised. This scenario is tested by
`AttestationSigner_Sign_NotAnAttestationPdf_ThrowsInvalidOperation`.

**AttestationSigner_Sign_ChainTooLargeForTheReservedSpace_ThrowsKeyException**: A key with a chain that does not fit
in the
space for the signature signs a document. An `AttestationKeyException` is raised. This scenario is tested by
`AttestationSigner_Sign_ChainTooLargeForTheReservedSpace_ThrowsKeyException`.
