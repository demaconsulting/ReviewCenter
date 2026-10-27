## System.Security.Cryptography.Pkcs

This document describes the integration and usage design for the `System.Security.Cryptography.Pkcs` OTS software item
(NuGet package `System.Security.Cryptography.Pkcs`).

### Purpose

System.Security.Cryptography.Pkcs is the library of Microsoft that implements the PKCS #7 and CMS (Cryptographic Message
Syntax) standards on top of the cryptography of the .NET runtime. ReviewCenter uses it in the distributed application to
make the detached CMS signature of the signed attestation PDF. The package is not part of the shared framework of
ASP.NET Core, so it is a reference of the application project.

### Features Used

- `CmsSigner`, with a signer certificate (included alone, `EndCertOnly`), the intermediate certificates of the chain added
  explicitly, a private key that is held by the runtime (an RSA or an ECDSA object), the SHA-256 digest algorithm, and the
  signing time as a signed attribute (the clock of the server; it is not a trusted timestamp)
- `ContentInfo` and `SignedCms` in the detached form, to compute the signature over the bytes of the PDF and to encode it
- `SignedCms.CheckSignature` in the tests only, to verify the result

### Integration Pattern

Only the `AttestationSigner` unit uses the library, so that no other unit depends on it. The signer hands the library the
handle of the private key that the key provider supplies, so the library signs through the key store, the smart card, or
the hardware module, and the server never has the key material. The signature is embedded as the value of the
signature dictionary of the PDF. A failure of the library or of the key becomes an `AttestationKeyException` with a plain
message; the types and stack of the library never reach a caller. The library has no self-validation mode; the passing
tests of the signer, which verify the signatures with the library and show that a change of the signed content
invalidates them, are the integration evidence.
