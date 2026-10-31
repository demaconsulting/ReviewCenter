### AttestationKeyProvider

#### Verification Approach

The `AttestationKeyProvider` unit is verified with tests defined in `AttestationKeyPolicyTests.cs` (the policy and the
factory), `SelfSignedKeyProviderTests.cs`, and `CertificateStoreKeyProviderTests.cs`. The tests use the real classes
with real temporary folders and temporary certificates made by the helper `TestKeys`; the log is captured with a
logger of the tests so that the warning can be checked for secrets. The tests of the certificate store provider
install a temporary self-signed certificate in the certificate store of the current user (`CurrentUser\My`) and remove it
afterwards. They **skip themselves when that store is not available**. The local machine store and a key on a smart card
or a hardware module are **not covered by automated tests** and are a manual check: configure
`Attestation:StoreLocation` and the thumbprint of such a certificate, start the server, download an attestation, and
check the signature in a PDF reader.

#### Test Environment

N/A - standard test environment (temporary folders and certificates are created by each test and removed after it). The
certificate store tests need the store of the current user.

#### Acceptance Criteria

- All tests pass with zero failures (or skip because the certificate store is not available).
- The factory chooses the provider by the setting and refuses an unknown value, naming the setting.
- The self-signed key is created once, protected by the key ring so that the file is not a readable PFX, with a limited
  validity, reloaded with the same ring, and the same for every caller, also when many start together.
- A plaintext file of an earlier version is ignored with a warning, never used or deleted; a file of another ring is an
  error that names the file; an expired key is archived and replaced with a warning.
- A provider that may not create creates and changes nothing and reports that the key will be created.
- No message or log holds a key, a password, or ciphertext.
- Every problem of the self-signed provider and of the certificate store provider names the setting or the file.
- `RequireTrustedKey` is checked on every call; the rule accepts an organization certificate and refuses a self-signed
  one, one without a signing key usage, and one outside its validity.
- A certificate for code signing only is classified (not when it has no extended key usage or a document purpose), is
  warned about once with the defined text, is flagged in the key description, and is not refused because of its
  code-signing usage.
- `Describe` never throws: a known key problem is reported with its message, and an unexpected failure is reported as
  unavailable with a generic message.

#### Test Scenarios

**AttestationKeyProviderFactory_Create_BlankOrSelfSigned_SelectsSelfSignedProvider**: A blank value and `SelfSigned` are
configured. The self-signed provider is created. This scenario is tested by
`AttestationKeyProviderFactory_Create_BlankOrSelfSigned_SelectsSelfSignedProvider`.

**AttestationKeyProviderFactory_Create_CertificateStore_SelectsCertificateStoreProvider**: `CertificateStore` is
configured. The certificate store provider is created. This scenario is tested by
`AttestationKeyProviderFactory_Create_CertificateStore_SelectsCertificateStoreProvider`.

**AttestationKeyProviderFactory_Create_UnknownProvider_ThrowsNamingTheSetting**: An unknown value is configured. An
`AttestationKeyException` names `Attestation:Provider`. This scenario is tested by
`AttestationKeyProviderFactory_Create_UnknownProvider_ThrowsNamingTheSetting`.

**AttestationKeyPolicy_Enforce_PolicyOff_AcceptsSelfSignedKey**: `RequireTrustedKey` is off. A self-signed key is
accepted. This scenario is tested by `AttestationKeyPolicy_Enforce_PolicyOff_AcceptsSelfSignedKey`.

**AttestationKeyPolicy_Enforce_TrustedKeyRequiredAndSelfSigned_Throws**: The rule is on and the key is self-signed. An
exception is raised. This scenario is tested by `AttestationKeyPolicy_Enforce_TrustedKeyRequiredAndSelfSigned_Throws`.

**AttestationKeyPolicy_Enforce_TrustedKeyRequiredAndOrganizationKey_Accepts**: The rule is on and the key is an
organization certificate within its validity. It is accepted. This scenario is tested by
`AttestationKeyPolicy_Enforce_TrustedKeyRequiredAndOrganizationKey_Accepts`.

**AttestationKeyPolicy_Enforce_TrustedKeyRequiredAndNoSigningUsage_Throws**: The rule is on and the key usage does not
allow signing. An exception is raised. This scenario is tested by
`AttestationKeyPolicy_Enforce_TrustedKeyRequiredAndNoSigningUsage_Throws`.

**AttestationKeyPolicy_Enforce_TrustedKeyRequiredAndOutsideValidity_Throws**: The rule is on and the certificate is not
yet valid or has expired. An exception is raised. This scenario is tested by
`AttestationKeyPolicy_Enforce_TrustedKeyRequiredAndOutsideValidity_Throws`.

**AttestationKeyPolicy_HasSigningKeyUsage_Usage_AllowsOnlyDigitalSignatureOrNonRepudiation**: Certificates with several
key usages are examined. Only digital signature and non-repudiation allow signing. This scenario is tested by
`AttestationKeyPolicy_HasSigningKeyUsage_Usage_AllowsOnlyDigitalSignatureOrNonRepudiation`.

**AttestationKeyPolicy_HasSigningKeyUsage_NoExtension_AllowsSigning**: A certificate without a key usage extension is
examined. It allows signing. This scenario is tested by
`AttestationKeyPolicy_HasSigningKeyUsage_NoExtension_AllowsSigning`.

**AttestationKeyPolicy_IsCodeSigningOnly_ExtendedKeyUsage_ClassifiesCertificates**: Certificates with several extended
key usages, and one without the extension, are generated and classified. Only one that allows code signing and no
document signing purpose (code signing alone, or with server authentication) is code-signing-only; document signing
(Microsoft or Adobe), e-mail protection, any purpose, no extension, and a mix of code signing with a document purpose are
not. The warning list agrees and holds exactly the defined text. This scenario is tested by
`AttestationKeyPolicy_IsCodeSigningOnly_ExtendedKeyUsage_ClassifiesCertificates`.

**AttestationKeyPolicy_CodeSigningNotice_HasTheDefinedWording**: The notice is compared with the defined text. This
scenario is tested by `AttestationKeyPolicy_CodeSigningNotice_HasTheDefinedWording`.

**AttestationSigningKey_Create_CodeSigningOnlyCertificate_SetsInfoFlag**: Keys are created for a code-signing-only
certificate, a certificate without extended key usage, and the default test key. Only the first has
`IsCodeSigningOnly` set. This scenario is tested by `AttestationSigningKey_Create_CodeSigningOnlyCertificate_SetsInfoFlag`.

**AttestationKeyProviderBase_GetSigningKey_CodeSigningOnlyCertificate_LogsWarningOnceAndDoesNotRefuse**: A provider
that returns a code-signing-only certificate issued by an authority is asked for the key twice with `RequireTrustedKey`
on. The key is returned both times, and one warning with the notice and the thumbprint is logged. The certificate store
provider is not exercised with a code-signing certificate; it shares this base class. This scenario is tested by
`AttestationKeyProviderBase_GetSigningKey_CodeSigningOnlyCertificate_LogsWarningOnceAndDoesNotRefuse`.

**SelfSignedKeyProvider_GetSigningKey_FirstUse_CreatesSelfSignedKeyFile**: The key is requested from an empty folder. A
key file is created and the certificate is self-signed. This scenario is tested by
`SelfSignedKeyProvider_GetSigningKey_FirstUse_CreatesSelfSignedKeyFile`.

**SelfSignedKeyProvider_GetSigningKey_FirstUse_FileIsNotAReadablePfx**: The key is requested from an empty folder. The
file `attestation-selfsigned.pfx.protected` is the only file, the runtime refuses to load it as a PFX, and the subject
text of the certificate is not in its bytes. This scenario is tested by
`SelfSignedKeyProvider_GetSigningKey_FirstUse_FileIsNotAReadablePfx`.

**SelfSignedKeyProvider_GetSigningKey_RestartWithPersistedRing_ReloadsTheSameKey**: A key is created with a Data
Protection provider over a temporary key folder, and a new provider over the same ring and folder is asked for it, as
after a restart. It has the same thumbprint. This scenario is tested by
`SelfSignedKeyProvider_GetSigningKey_RestartWithPersistedRing_ReloadsTheSameKey`.

**SelfSignedKeyProvider_GetSigningKey_OtherKeyRing_ThrowsNamingTheFileAndKeepsIt**: The key file was written with one
ring and is read with another. The error names the file and tells the operator to restore the ring or delete the file,
and the file is unchanged. This scenario is tested by
`SelfSignedKeyProvider_GetSigningKey_OtherKeyRing_ThrowsNamingTheFileAndKeepsIt`.

**SelfSignedKeyProvider_GetSigningKey_StorageAndUnreadableFailures_MessageWithoutPathHoldsNoFolder**: A file stands where
the folder should be, and a key file was written with another ring. The messages for the server log hold the folder
path, while `MessageWithoutPath`, which the configuration check prints, names the setting `Attestation:SelfSignedFolder`
or the file name and does not hold the folder. This scenario is tested by
`SelfSignedKeyProvider_GetSigningKey_StorageAndUnreadableFailures_MessageWithoutPathHoldsNoFolder`.

**SelfSignedKeyProvider_GetSigningKey_ProtectFails_IsNotReportedAsUnreadable**: The protector of the ring fails
with a
cryptographic error while the new key is protected. The error says that the key could not be created, is not flagged
unreadable, does not say "decrypted", keeps the inner error, and no key file is left. This scenario is tested by
`SelfSignedKeyProvider_GetSigningKey_ProtectFails_IsNotReportedAsUnreadable`.

**SelfSignedKeyProvider_GetSigningKey_LegacyPlaintextFile_IsIgnoredWithWarning**: A plaintext
`attestation-selfsigned.pfx` of an earlier version is in the folder. It is not used: a new key with another thumbprint
is created and protected, a warning tells the operator to delete the old file, and the old file is left in place. This
scenario is tested by `SelfSignedKeyProvider_GetSigningKey_LegacyPlaintextFile_IsIgnoredWithWarning`.

**SelfSignedKeyProvider_GetSigningKey_ExpiredKey_IsArchivedAndRegeneratedWithWarning**: The clock is set past the
validity of the key. The protected file is moved to a `.retired-<thumbprint>` file, a new key with another thumbprint is
created, a warning names the expired key, and a later provider loads the new key. This scenario is tested by
`SelfSignedKeyProvider_GetSigningKey_ExpiredKey_IsArchivedAndRegeneratedWithWarning`.

**SelfSignedKeyProvider_GetSigningKey_CreationNotAllowedAndNoKey_ReportsWillBeCreatedAndCreatesNothing**: A provider
that may not create is asked for a key in a folder that does not exist. The error says that the key will be created on
first start, the status is unavailable, and the folder does not exist. This scenario is tested by
`SelfSignedKeyProvider_GetSigningKey_CreationNotAllowedAndNoKey_ReportsWillBeCreatedAndCreatesNothing`.

**SelfSignedKeyProvider_GetSigningKey_CreationNotAllowedAndExpiredKey_DescribesItAndChangesNothing**: A provider that may
not create is asked for an expired key. It describes the key as it is, and the folder holds only the original file. This
scenario is tested by
`SelfSignedKeyProvider_GetSigningKey_CreationNotAllowedAndExpiredKey_DescribesItAndChangesNothing`.

**SelfSignedKeyProvider_GetSigningKey_AnyScenario_NoSecretOrCiphertextInMessages**: A key is created with a capturing
log, and a provider with another ring fails to read it. No log entry, error message, or status problem holds a
fragment of the key file (in base64 or hexadecimal) or the text "PRIVATE KEY". This scenario is tested by
`SelfSignedKeyProvider_GetSigningKey_AnyScenario_NoSecretOrCiphertextInMessages`.

**AttestationKeyProviderFactory_Create_CreationNotAllowed_ProviderCreatesNothing**: The factory is called with
`allowCreate` set to `false` for an empty folder. The provider reports that the key will be created, and nothing is
created. This scenario is tested by `AttestationKeyProviderFactory_Create_CreationNotAllowed_ProviderCreatesNothing`.

**SelfSignedKeyProvider_GetSigningKey_FirstUse_SetsTheValidityPeriod**: The certificate that is created is examined.
It is
valid from shortly before now for about ten years. This scenario is tested by
`SelfSignedKeyProvider_GetSigningKey_FirstUse_SetsTheValidityPeriod`.

**SelfSignedKeyProvider_GetSigningKey_ExistingFile_ReloadsTheSameKeyAndSigns**: A second provider over the same
folder is
asked for the key. It has the same certificate and signs. This scenario is tested by
`SelfSignedKeyProvider_GetSigningKey_ExistingFile_ReloadsTheSameKeyAndSigns`.

**SelfSignedKeyProvider_GetSigningKey_RepeatedCalls_ReturnTheSameKey**: The key is requested twice from one
provider. It is
the same key. This scenario is tested by `SelfSignedKeyProvider_GetSigningKey_RepeatedCalls_ReturnTheSameKey`.

**SelfSignedKeyProvider_GetSigningKey_ConcurrentFirstUse_CreatesOneKeyForEveryone**: Several providers over one empty
folder ask for the key at once. They all get the same certificate. This scenario is tested by
`SelfSignedKeyProvider_GetSigningKey_ConcurrentFirstUse_CreatesOneKeyForEveryone`.

**SelfSignedKeyProvider_GetSigningKey_RequireTrustedKey_IsCheckedOnEveryCall**: The rule is on. The first call and every
later call are refused. This scenario is tested by
`SelfSignedKeyProvider_GetSigningKey_RequireTrustedKey_IsCheckedOnEveryCall`.

**SelfSignedKeyProvider_GetSigningKey_FirstUse_LogsSelfSignedWarningWithoutSecrets**: The key is created. The log has a
warning that the key is self-signed and for development and test, and no secret. This scenario is tested by
`SelfSignedKeyProvider_GetSigningKey_FirstUse_LogsSelfSignedWarningWithoutSecrets`.

**SelfSignedKeyProvider_GetSigningKey_FolderNotSet_ThrowsNamingTheSetting**: No folder is set. The error names
`Attestation:SelfSignedFolder`. This scenario is tested by
`SelfSignedKeyProvider_GetSigningKey_FolderNotSet_ThrowsNamingTheSetting`.

**SelfSignedKeyProvider_GetSigningKey_FolderIsAFile_ThrowsNamingTheSetting**: The folder is a file. The error names the
setting. This scenario is tested by `SelfSignedKeyProvider_GetSigningKey_FolderIsAFile_ThrowsNamingTheSetting`.

**SelfSignedKeyProvider_GetSigningKey_CorruptFile_ThrowsNamingTheFile**: The key file holds garbage. The error names the
file. This scenario is tested by `SelfSignedKeyProvider_GetSigningKey_CorruptFile_ThrowsNamingTheFile`.

**SelfSignedKeyProvider_Describe_UnexpectedFailure_ReportsUnavailableWithoutDetail**: The folder is a name that the
runtime rejects with an exception that is not an `AttestationKeyException`, so loading the key fails unexpectedly.
`Describe` does not throw; it reports the key as unavailable with the generic message about an unexpected error, which
does not contain the detail of the exception. This scenario is tested by
`SelfSignedKeyProvider_Describe_UnexpectedFailure_ReportsUnavailableWithoutDetail`.

**CertificateStoreKeyProvider_NormalizeThumbprint_CopiedText_GivesUpperCaseHex**: A thumbprint with spaces and invisible
marks, in lower case, is normalized. The result is 40 upper-case hexadecimal digits. This scenario is tested by
`CertificateStoreKeyProvider_NormalizeThumbprint_CopiedText_GivesUpperCaseHex`.

**CertificateStoreKeyProvider_NormalizeThumbprint_MissingOrMalformed_ThrowsNamingTheSetting**: A missing and a malformed
thumbprint are normalized. The error names `Attestation:CertificateThumbprint`. This scenario is tested by
`CertificateStoreKeyProvider_NormalizeThumbprint_MissingOrMalformed_ThrowsNamingTheSetting`.

**CertificateStoreKeyProvider_GetSigningKey_UnknownStoreLocation_ThrowsNamingTheSetting**: An unknown store location is
configured. The error names the setting. This scenario is tested by
`CertificateStoreKeyProvider_GetSigningKey_UnknownStoreLocation_ThrowsNamingTheSetting`.

**CertificateStoreKeyProvider_GetSigningKey_CertificateNotInStore_ThrowsNamingThumbprintAndStore**: The thumbprint of a
certificate that is not in the store is configured. The error names the thumbprint and the store. This scenario is
tested by `CertificateStoreKeyProvider_GetSigningKey_CertificateNotInStore_ThrowsNamingThumbprintAndStore`.

**CertificateStoreKeyProvider_GetSigningKey_CertificateInStore_FindsItAndSigns**: A temporary certificate is in the store
of the current user. The provider finds it by the thumbprint and the key signs. This scenario is tested by
`CertificateStoreKeyProvider_GetSigningKey_CertificateInStore_FindsItAndSigns`.

**CertificateStoreKeyProvider_GetSigningKey_RequireTrustedKeyAndSelfSignedCertificate_Throws**: The rule is on and the
certificate in the store is self-signed. An exception is raised. This scenario is tested by
`CertificateStoreKeyProvider_GetSigningKey_RequireTrustedKeyAndSelfSignedCertificate_Throws`.

**CertificateStoreKeyProvider_GetSigningKey_CertificateWithoutPrivateKey_Throws**: A certificate without a private key is
in the store. An exception names the problem. This scenario is tested by
`CertificateStoreKeyProvider_GetSigningKey_CertificateWithoutPrivateKey_Throws`.

**CertificateStoreKeyProvider_GetSigningKey_ConfiguredForTheServer_StartsWithTheCertificate**: The server application is
created with settings that choose the certificate store and name the temporary certificate by its thumbprint. It starts,
the registered provider is the certificate store provider, and its key is that certificate. This scenario is tested by
`CertificateStoreKeyProvider_GetSigningKey_ConfiguredForTheServer_StartsWithTheCertificate`.
