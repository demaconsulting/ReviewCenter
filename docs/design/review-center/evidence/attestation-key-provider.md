### AttestationKeyProvider

![Evidence Structure](EvidenceView.svg)

#### Purpose

The `AttestationKeyProvider` unit finds the key that signs the attestation. Its single responsibility is to turn the
`Attestation` settings into a key that the signer can use and to refuse a key that the settings do not allow. It is a
group of small types that belong together: the interface `IAttestationKeyProvider` with the status type
`AttestationKeyStatus`, the base class `AttestationKeyProviderBase`, the providers `SelfSignedKeyProvider`,
`CertificateStoreKeyProvider` and `FileKeyProvider`, the `AttestationKeyProviderFactory`, the `AttestationKeyPolicy`,
the types
`AttestationSigningKey` and `AttestationKeyInfo`, the public `AttestationKeyException`, and the options type
`AttestationOptions`.

#### Data Model

`AttestationOptions` is bound to the `Attestation` section: `Provider` (`SelfSigned` by default, `CertificateStore` or
`File`), `RequireTrustedKey` (false by default), `CertificateThumbprint`, `StoreLocation` (`CurrentUser` by default or
`LocalMachine`), `StoreName` (`My` by default), `SelfSignedFolder` (set by `ServerHost` to a folder beside the Data
Protection keys unless the operator sets it), the settings of the File provider `CertificatePath`, `PrivateKeyPath`,
`CertificatePassword` (the one secret setting) and `CertificatePasswordFile`, and `ExpiryWarningDays` (30 by default;
0 turns the expiry warnings off). An `AttestationSigningKey` holds the private key as an RSA or ECDSA object
(a handle, not key material), the signer certificate, the intermediate certificates, and an `AttestationKeyInfo` (provider,
subject, issuer, thumbprint, serial number, algorithm, validity, whether the certificate is self-signed, whether its key
usage allows signing, whether the certificate is for code signing only, and whether `RequireTrustedKey` is on).
`AttestationKeyStatus` (provider, whether a key can be
used, the `AttestationKeyInfo`, the problem, and the list of warnings) is the answer for the review page.

`AttestationKeyProviderBase` loads the key once, on the first request, and keeps it; a failure is not kept, so the next
call tries again. It logs the choice at information level and a warning for a self-signed key and for a certificate
that is for code signing only. `AttestationKeyPolicy.Enforce` runs on every call, not only on the first, so a certificate
that expires while the server runs stops the attestation and not the server.

**Expiry warnings.** `AttestationKeyPolicy.ExpiryWarning(info, now, days)` returns `null` when `days` is 0 or the
certificate ends later than `days` days from now, and otherwise a text that names the thumbprint and the date (UTC):
"expires on" when the end is ahead and "expired on" when it has passed. The base class evaluates it on every use of the
key, for the providers that do not replace their key (`WarnsAboutExpiry`; the self-signed provider does not, because it
archives an expired key and creates a new one with its own warning), logs it at warning level once for each text, so the
log is not filled by every signing, and puts it into the `Warnings` of the status. A certificate that has expired
warns also when `RequireTrustedKey` is not set, where the server signs with it; with `RequireTrustedKey` the policy
refuses the key as before. A warning that a provider notes while it loads the key (the `FileKeyProvider` notes some)
is kept with the loaded key and is part of `Warnings` as well.

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

The **file provider** reads a certificate with its private key from a file of the server, which an administrator
deploys (for example from a secret store). `CertificatePath` is the file; the format is detected from the content: text
that holds `-----BEGIN` is PEM, anything else is PKCS #12 (PFX). The password comes from `CertificatePassword` or from
the contents of `CertificatePasswordFile`, trimmed of surrounding white space, resolved by the `SecretSource` unit, so
setting both, or naming a password
file that does not exist, is refused with the messages of that unit. A PFX is loaded with `X509CertificateLoader`
as a collection with an ephemeral key set, so the key lives in memory only and is not written to the key store of the
user (on macOS, where an ephemeral key set is not supported for a PFX, the default key set is used, as the key ring
protection does). `PrivateKeyPath` is for PEM only: for a PFX it is ignored (not read, not checked, and not used for the
permission check). Whether the key is protected is decided from the structure of the PKCS #12 file (`Pkcs12Info`: a
password is set, the safes that hold the key are encrypted or the key is a shrouded key bag, and no plain key bag
exists), not from whether a password is set: a password that the file does not use protects nothing, so such a file
is treated as unprotected. The signer is the first certificate of the collection with a private key; the other
certificates are
the chain, without the signer itself and without a self-signed root, which the reader has to trust. A PEM
certificate is loaded with `CreateFromPemFile`, or `CreateFromEncryptedPemFile` when the key is an
`ENCRYPTED PRIVATE KEY`, with the key in the same file or in `PrivateKeyPath`; the other certificates of the PEM file
are the chain. A legacy encrypted key (`Proc-Type: 4,ENCRYPTED`, as in `BEGIN RSA PRIVATE KEY`) is not supported by
that loader: it is recognized by its header, before the load, and refused with a message that tells the operator to
convert it to PKCS #8 (for example with `openssl pkcs8 -topk8`). The bytes that were read from the files, which can hold
an unencrypted private key, are searched as bytes (no text copy of the key is made) and cleared with
`CryptographicOperations.ZeroMemory` after the load. A key is *protected* when it needs a password (a PFX loaded with a
password, an encrypted PEM key). A key
that is not protected is a warning that names the file, and an error, naming `DataProtection:RequireEncryptedKeys`, when
that setting is true (`ServerHost` and the configuration check pass the setting on through the factory). On Linux and
macOS the provider warns, by file name, when the file that holds the private key or the password file can be accessed
by the group or by others, and tells the operator to restrict the mode (for example `chmod 600`). The file that holds
the key is the certificate file, except for a PEM certificate with a separate `PrivateKeyPath`, where it is the key
file and the certificate file is not checked, because it holds no secret. The provider does not refuse for the mode,
because a deployment can have a reason for a shared group. Every failure is an `AttestationKeyException` that names the
setting or the file: the path is not set, the file does not exist, the key file does not exist, the file is neither
PEM nor PFX, the file needs a password, the password is wrong or the file holds no private key. A password is never
part of a message. Each such exception also carries `MessageWithoutPath`, with the file name instead of the full path,
for the configuration check. The provider does not create or change any file, so `allowCreate` does not apply.

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
  thumbprint, and whether it is self-signed), the problem, and the warnings (the expiry warning and what the provider
  noted while it loaded the key).
- *Postconditions*: Never throws. An `AttestationKeyException` is returned as its message (which names what to correct).
  Any other non-fatal exception (for example an invalid folder name, or a store or chain error of the runtime) is logged
  at warning level and returned as the generic problem "The attestation signing key could not be loaded because of an
  unexpected error; see the server log.", without the detail of the exception, so the review page never fails because
  of the key. Only fatal runtime errors (out of memory, stack overflow, access violation) are not caught.

**AttestationKeyProviderFactory.Create**: Chooses the provider.

- *Parameters*: The options, the clock, the logger factory, the `IDataProtectionProvider` that protects the
  self-signed key, `allowCreate` (`false` to describe without creating anything), and `requireEncryptedKeys` (the value
  of `DataProtection:RequireEncryptedKeys`, which the File provider enforces for its key).
- *Returns*: The provider for `SelfSigned` (also for an empty value), `CertificateStore` or `File` (the names are not
  case sensitive); the certificate store provider ignores the last three parameters. A negative `ExpiryWarningDays` and
  any other provider name are an `AttestationKeyException` that names the setting; the message for a provider name ends
  with "Use SelfSigned, CertificateStore or File."

**AttestationKeyPolicy.Enforce, HasSigningKeyUsage, IsCodeSigningOnly, Warnings, ExpiryWarning**: The rules above.

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
  store, the key, and the chain (and, for the File provider, the PEM and PKCS #12 loaders and the `Pkcs12Info` decoder
  that tells a wrong password from a file that is no PFX). These belong to the runtime and are not OTS software items
  of their own.
- **Microsoft.Extensions.Options and Logging** — the options and the log of the application framework.
- **Microsoft.AspNetCore.DataProtection** — the `IDataProtectionProvider` that protects the self-signed key.
- **SecretSource** — resolves the password of the File provider from the setting or the password file.

#### Callers

- **AttestationService** — gets the key for each attestation.
- **ServerHost** — registers the provider by the factory (with the Data Protection provider of the server) and obtains
  the key at start-up.
- **ConfiguredKeysCheck** — describes the key with `allowCreate` set to `false`.
- **ReviewPage** — asks `Describe` for the note under the download button.
