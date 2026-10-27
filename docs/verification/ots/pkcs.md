## System.Security.Cryptography.Pkcs Verification

This document provides the verification evidence for the System.Security.Cryptography.Pkcs OTS software item.
Requirements for this OTS item are defined in the System.Security.Cryptography.Pkcs OTS Software Requirements document.

### Required Functionality

System.Security.Cryptography.Pkcs makes a detached CMS signature with SHA-256 over given bytes, with an RSA key or an
ECDSA key, also when the key cannot be exported, includes the certificates that it is given, and verifies the result. A
passing test confirms that the signer of the attestation gets all of this from the library.

### Verification Approach

System.Security.Cryptography.Pkcs has no self-validation mode and is verified by the project's own tests, which run the
real library in memory with temporary keys and certificates. Each scenario names a specific test method that depends on
the library under verification. A passing pipeline run for the scenario constitutes evidence that the requirement is
satisfied.

### Test Scenarios

#### AttestationSigner_Sign_RsaKey_ProducesVerifiableDetachedSignature

**Scenario**: A PDF is signed with an RSA key and the signature is verified over the byte range.

**Expected**: The library verifies the detached signature with SHA-256.

**Requirement coverage**: `ReviewCenter-OTS-Pkcs-DetachedSignature`.

#### AttestationSigner_Sign_EcdsaKey_ProducesVerifiableDetachedSignature

**Scenario**: The same with an ECDSA key.

**Expected**: The library verifies the signature.

**Requirement coverage**: `ReviewCenter-OTS-Pkcs-DetachedSignature`.

#### AttestationSigner_Sign_NonExportableKey_ProducesVerifiableSignature

**Scenario**: A key whose private part cannot be exported signs the document.

**Expected**: The library signs through the key handle, and the signature verifies.

**Requirement coverage**: `ReviewCenter-OTS-Pkcs-DetachedSignature`.

#### AttestationSigner_Sign_OrganizationKeyWithChain_EmbedsTheChainCertificates

**Scenario**: A key with a certificate chain signs the document.

**Expected**: The signature holds the certificate of the signer and the intermediate certificates.

**Requirement coverage**: `ReviewCenter-OTS-Pkcs-DetachedSignature`.

#### AttestationSigner_Sign_TamperedContent_SignatureNoLongerVerifies

**Scenario**: A byte of the signed content is changed and the signature is verified again.

**Expected**: The library reports that the signature does not match.

**Requirement coverage**: `ReviewCenter-OTS-Pkcs-DetachedSignature`.
