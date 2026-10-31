### AttestationKeyProvider

![Evidence Structure](EvidenceView.svg)

#### Purpose

The `AttestationKeyProvider` unit finds the key that signs the attestation. Its single responsibility is to turn the
`Attestation` settings into a key that the signer can use and to refuse a key that the settings do not allow. It is a
group of small types that belong together: the interface `IAttestationKeyProvider` with the status type
`AttestationKeyStatus`, the base class `AttestationKeyProviderBase`, the providers `SelfSignedKeyProvider` and
`CertificateStoreKeyProvider`, the `AttestationKeyProviderFactory`, the `AttestationKeyPolicy`, the types
`AttestationSigningKey` and `AttestationKeyInfo`, the public `AttestationKeyException`, and the options type
`AttestationOptions`.

#### Data Model

`AttestationOptions` is bound to the `Attestation` section: `Provider` (`SelfSigned` by default or `CertificateStore`),
`RequireTrustedKey` (false by default), `CertificateThumbprint`, `StoreLocation` (`CurrentUser` by default or
`LocalMachine`), `StoreName` (`My` by default), and `SelfSignedFolder` (set by `ServerHost` to a folder beside the Data
Protection keys unless the operator sets it). An `AttestationSigningKey` holds the private key as an RSA or ECDSA object
(a handle, not key material), the signer certificate, the intermediate certificates, and an `AttestationKeyInfo` (provider,
subject, issuer, thumbprint, serial number, algorithm, validity, whether the certificate is self-signed, whether its key
usage allows signing, whether the certificate is for code signing only, and whether `RequireTrustedKey` is on).
`AttestationKeyStatus` (provider, whether a key can be
used, the `AttestationKeyInfo`, and the problem) is the answer for the review page.

`AttestationKeyProviderBase` loads the key once, on the first request, and keeps it; a failure is not kept, so the next
call tries again. It logs the choice at information level and a warning for a self-signed key and for a certificate
that is for code signing only. `AttestationKeyPolicy.Enforce` runs on every call, not only on the first, so a certificate
that expires while the server runs stops the attestation and not the server.

The kinds of key, from the weakest to the strongest evidence: a **self-signed** key (the default, labeled in the
attestation and logged), a **code-signing** certificate (used, with a note in the attestation and a warning in the log:
Adobe Acrobat will not trust it for document signatures), and a **document-signing** certificate (no note). The server
does not check the chain or revocation for any of them; the PDF viewer does, against its own trust store.

The **self-signed provider** creates, when its file is missing, an RSA key of 3072 bits with a certificate that is signed
by itself (SHA-256, the subject "ReviewCenter self-signed attestation key (not an organization key)", ten years of
validity that start ten minutes in the past to allow for clock differences, and the key usage for digital signatures).
The key is saved, protected, in `attestation-selfsigned.pfx.protected` in the folder, which is created with access for its
owner only on systems that support it. The exported PFX (without a password) is protected with an
`IDataProtectionProvider`, purpose `DemaConsulting.ReviewCenter.AttestationSelfSignedKey.v1`, which is the provider of
the server, so the file is only readable with the Data Protection key ring and is not a PFX on disk; the plaintext
bytes are cleared after use and the certificate is loaded with an ephemeral key set. The file is written to a temporary
name and moved into place without overwriting, so two processes that start together end with one key, and the one that
lost loads the file of the winner. The folder is, by default, `attestation-selfsigned` in the folder of the Data
Protection keys, so it is backed up with them and is lost with them. A plaintext `attestation-selfsigned.pfx` of an
earlier version is **not** read or migrated: it is ignored with a warning that tells the operator to delete it (it is
never deleted by the server), and a new key is created. A file that the ring cannot decrypt (the ring is lost or is
another ring) is an error that names the file and says to restore the ring or delete the file; only a failure to decrypt
or load the file is reported that way, while a cryptographic failure while a key is created is reported as a failure to
create the key. The storage and unreadable errors also carry `MessageWithoutPath`, a message that names the setting or
the file name but not the folder, which the configuration check prints instead of the full message. A key whose
certificate
has expired is moved to `attestation-selfsigned.pfx.protected.retired-<thumbprint>` and a new key is created, with a
warning; PDFs that were signed earlier are not affected because they carry their own certificate. The replacement
assumes one server per folder. **The key is still a self-signed key, for development and test only.**

The factory and the provider take a flag `allowCreate`. With `false` (the mode of `--validate --check-config`) the
provider only describes: it creates no folder and no file, does not archive an expired key, and, when no protected file
exists, throws an `AttestationKeyException` with `WillBeCreated` set, whose message is "will be created on first
start". Before that, a regular file at the configured folder path is reported as an error that names
`Attestation:SelfSignedFolder` in both modes, because the start would fail there too.

The **certificate store provider** opens the configured store read-only and without creating it, finds the
certificate by its thumbprint (the text is normalized, so a thumbprint copied from a dialog with spaces and the invisible
left-to-right marks works), and checks that the certificate has a private key of a supported kind (RSA or ECDSA). It
keeps the handle of the private key and **never exports** it; the key can be in the Windows key store, on a smart card,
or in a hardware module. No test signature is made at start-up, so a card is not asked for its PIN. It builds the chain
of the certificate with no revocation check and accepts an unknown root, and keeps the intermediate certificates only, so
the signature carries what a reader needs to follow the chain, and the root is left to the reader to trust.

#### Key Methods

**GetSigningKey**: Returns the key, loading it on first use.

- *Parameters*: None.
- *Returns*: `AttestationSigningKey`.
- *Postconditions*: The policy was checked with the current time: when `RequireTrustedKey` is set, the certificate must
  not be self-signed, its key usage (when it has one) must allow digital signatures or non-repudiation, and now must be
  within its validity period. **This is all that the rule checks: it does not check that the chain leads to a trusted
  root and does not check revocation**, because the server cannot know whom its readers trust. A warning is logged once,
  when the key is first loaded, for a certificate that is for code signing only (`AttestationKeyPolicy.IsCodeSigningOnly`:
  the extended key usage lists code signing and none of e-mail protection, Microsoft or Adobe document signing, or any
  usage; a certificate without the extension is not code-signing-only). The text is
  `AttestationKeyPolicy.CodeSigningNotice`: "This certificate is a code-signing certificate. Adobe Acrobat will not trust
  it for document signatures; use a document-signing certificate for production." The code-signing usage is not a
  reason to refuse the key, also not when `RequireTrustedKey` is set (a key that fails that policy for another reason,
  such as a key usage that does not allow signing or a validity period that has ended, is still refused).
  `AttestationKeyInfo.IsCodeSigningOnly` carries the classification to the attestation.

**Describe**: Returns the status for the page.

- *Parameters*: None.
- *Returns*: `AttestationKeyStatus` — the provider name, whether a key can be used, its description (subject,
  thumbprint, and whether it is self-signed), and the problem.
- *Postconditions*: Never throws. An `AttestationKeyException` is returned as its message (which names what to correct).
  Any other non-fatal exception (for example an invalid folder name, or a store or chain error of the runtime) is logged
  at warning level and returned as the generic problem "The attestation signing key could not be loaded because of an
  unexpected error; see the server log.", without the detail of the exception, so the review page never fails because
  of the key. Only fatal runtime errors (out of memory, stack overflow, access violation) are not caught.

**AttestationKeyProviderFactory.Create**: Chooses the provider.

- *Parameters*: The options, the clock, the logger factory, the `IDataProtectionProvider` that protects the
  self-signed key, and `allowCreate` (`false` to describe without creating anything).
- *Returns*: The provider for `SelfSigned` (also for an empty value) or `CertificateStore`; the certificate store
  provider ignores both new parameters.

**AttestationKeyPolicy.Enforce, HasSigningKeyUsage, IsCodeSigningOnly, Warnings**: The rules above.

#### Error Handling

Every problem is an `AttestationKeyException` whose message names the setting or the file to correct: an unknown
provider, a missing or malformed thumbprint, an unknown store location, a certificate that is not in the store (with the
thumbprint and the store), a certificate without a private key or with a key of an unsupported kind, a self-signed
folder that is not set or is a file, a key file that cannot be read or decrypted (the message names the file), and a
certificate that fails the policy. The
messages and the logs never contain a secret. `Describe` turns the exception into a status (and any other non-fatal
exception into a generic status, with the detail in the log only). `ServerHost` calls
`GetSigningKey` at start-up and turns the exception into a start-up error, so a configuration that cannot work stops the
server with a message; the consequence is that a server whose key is in a hardware module that is not present does not
start.

The unit has no connection to Azure Key Vault and no timestamp authority. A provider for Azure Key Vault (the key stays
in the vault and signs through the Azure SDK) and the support of a timestamp authority (RFC 3161) are planned and
**not implemented**; the key type of `AttestationSigningKey` is an RSA or ECDSA object, which is where such a provider
would plug in.

#### Dependencies

- **System.Security.Cryptography.X509Certificates** and the cryptography of the .NET runtime — the certificate, the
  store, the key, and the chain. These belong to the runtime and are not OTS software items of their own.
- **Microsoft.Extensions.Options and Logging** — the options and the log of the application framework.
- **Microsoft.AspNetCore.DataProtection** — the `IDataProtectionProvider` that protects the self-signed key.

#### Callers

- **AttestationService** — gets the key for each attestation.
- **ServerHost** — registers the provider by the factory (with the Data Protection provider of the server) and obtains
  the key at start-up.
- **ConfiguredKeysCheck** — describes the key with `allowCreate` set to `false`.
- **ReviewPage** — asks `Describe` for the note under the download button.
