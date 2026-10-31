### AttestationKeyProvider

#### Verification Approach

The `AttestationKeyProvider` unit is verified with tests defined in `AttestationKeyPolicyTests.cs` (the policy and the
factory), `SelfSignedKeyProviderTests.cs`, `CertificateStoreKeyProviderTests.cs`, and `FileKeyProviderTests.cs`. The
tests use the real classes
with real temporary folders and temporary certificates made by the helper `TestKeys`; the log is captured with a
logger of the tests so that the warning can be checked for secrets. The tests of the file provider make their
certificates (a signer, an intermediate and a root) in the test, write them as PFX and as PEM files in a temporary
folder, with and without a password, and use a fixed clock for the expiry warnings; they use no certificate store.
The test of the Unix
file permissions skips itself on Windows. The tests of the certificate store provider
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
- The file provider loads a PFX or a PEM (with the key in the same file or in another, encrypted or not) with the
  password from a setting or a file, keeps the chain without the signer and the root, and names the setting or file
  (never the password, and for the configuration check not the folder) for a missing path, file or password, a wrong
  password and a file that is no certificate.
- A key without a password is a warning, or an error when encryption is required; a file that others can read is a
  warning on Linux.
- A certificate that ends within `ExpiryWarningDays` or has ended is warned about in the log and in the status, with the
  thumbprint and the date, also when `RequireTrustedKey` is off; 0 days switches the warning off; a key that has
  expired is still refused with `RequireTrustedKey`; the self-signed key gets no expiry warning.
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

**SelfSignedKeyProvider_GetSigningKey_CreationNotAllowedAndFolderIsAFile_ThrowsNamingTheSetting**: A regular file stands
where the folder should be, and a provider that may not create is asked for the key. The error is not "will be created"
but names `Attestation:SelfSignedFolder`, the path-free message holds no path, and the file is left as it was. This
scenario is tested by `SelfSignedKeyProvider_GetSigningKey_CreationNotAllowedAndFolderIsAFile_ThrowsNamingTheSetting`.

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

**AttestationKeyProviderFactory_Create_File_SelectsFileProvider**: `File` and `file` are configured. The file provider is
created. This scenario is tested by `AttestationKeyProviderFactory_Create_File_SelectsFileProvider`.

**AttestationKeyProviderFactory_Create_NegativeExpiryWarningDays_ThrowsNamingTheSetting**: `ExpiryWarningDays` is
negative. An `AttestationKeyException` names `Attestation:ExpiryWarningDays`. This scenario is tested by
`AttestationKeyProviderFactory_Create_NegativeExpiryWarningDays_ThrowsNamingTheSetting`.

**FileKeyProvider_GetSigningKey_PfxWithPasswordSetting_LoadsSignerAndKey**: A protected PFX is written to a temporary
folder and its password is set in `CertificatePassword`. The provider returns the signer certificate and a key that signs.
This scenario is tested by `FileKeyProvider_GetSigningKey_PfxWithPasswordSetting_LoadsSignerAndKey`.

**FileKeyProvider_GetSigningKey_PfxWithPasswordFile_LoadsSigner**: The password is in a file named by
`CertificatePasswordFile`. The signer is loaded. This scenario is tested by
`FileKeyProvider_GetSigningKey_PfxWithPasswordFile_LoadsSigner`.

**FileKeyProvider_GetSigningKey_PfxWithIntermediateAndRoot_ChainHoldsTheIntermediateOnly**: The PFX holds the signer, an
intermediate certificate and the root. The chain of the key is the intermediate certificate only. This scenario is tested
by `FileKeyProvider_GetSigningKey_PfxWithIntermediateAndRoot_ChainHoldsTheIntermediateOnly`.

**FileKeyProvider_GetSigningKey_PemWithSeparateKeyAndChain_LoadsSignerAndChain**: A PEM certificate file with the
intermediate certificate, and the key in the file named by `PrivateKeyPath`. The signer and the chain are loaded. This
scenario is tested by `FileKeyProvider_GetSigningKey_PemWithSeparateKeyAndChain_LoadsSignerAndChain`.

**FileKeyProvider_GetSigningKey_EncryptedPemInOneFile_LoadsSignerWithPassword**: An encrypted PEM with the key in the
same file and a password. The signer is loaded. This scenario is tested by
`FileKeyProvider_GetSigningKey_EncryptedPemInOneFile_LoadsSignerWithPassword`.

**FileKeyProvider_GetSigningKey_EncryptedPemWithSeparateKey_LoadsSignerWithPassword**: An encrypted key in a separate
file and a password. The signer is loaded. This scenario is tested by
`FileKeyProvider_GetSigningKey_EncryptedPemWithSeparateKey_LoadsSignerWithPassword`.

**FileKeyProvider_GetSigningKey_NoCertificatePath_NamesTheSetting**: `CertificatePath` is empty. The message is
"Attestation:CertificatePath is not set; it is required for the File provider." This scenario is tested by
`FileKeyProvider_GetSigningKey_NoCertificatePath_NamesTheSetting`.

**FileKeyProvider_GetSigningKey_MissingFile_NamesTheFileAndTheCheckMessageNamesNoFolder**: The file does not exist. The
message names the file and the setting, and the message for the configuration check holds the file name and not the
folder. This scenario is tested by `FileKeyProvider_GetSigningKey_MissingFile_NamesTheFileAndTheCheckMessageNamesNoFolder`.

**FileKeyProvider_GetSigningKey_MissingKeyFile_NamesTheSetting**: `PrivateKeyPath` names a file that does not exist. The
message names `Attestation:PrivateKeyPath`. This scenario is tested by
`FileKeyProvider_GetSigningKey_MissingKeyFile_NamesTheSetting`.

**FileKeyProvider_GetSigningKey_PfxWithoutPassword_AsksForThePassword**: A protected PFX and no password. The message says
that the file needs a password and names `Attestation:CertificatePassword` and `Attestation:CertificatePasswordFile`. This
scenario is tested by `FileKeyProvider_GetSigningKey_PfxWithoutPassword_AsksForThePassword`.

**FileKeyProvider_GetSigningKey_EncryptedPemWithoutPassword_AsksForThePassword**: An encrypted PEM key and no password.
The same message. This scenario is tested by `FileKeyProvider_GetSigningKey_EncryptedPemWithoutPassword_AsksForThePassword`.

**FileKeyProvider_GetSigningKey_WrongPassword_SaysSoAndEchoesNoPassword**: Another password is set. The message says wrong
password and holds neither password. This scenario is tested by
`FileKeyProvider_GetSigningKey_WrongPassword_SaysSoAndEchoesNoPassword`.

**FileKeyProvider_GetSigningKey_SeparateEncryptedKeyProblems_NameThePrivateKeyFile**: An encrypted PEM key in a
separate file, once without a password and once with a wrong one. Both messages, and their check forms, call the file
the private key file and never the certificate file. This scenario is tested by
`FileKeyProvider_GetSigningKey_SeparateEncryptedKeyProblems_NameThePrivateKeyFile`.

**FileKeyProvider_GetSigningKey_FileThatIsNotACertificate_SaysSo**: The file holds text that is neither PEM nor PFX. The
message says so. This scenario is tested by `FileKeyProvider_GetSigningKey_FileThatIsNotACertificate_SaysSo`.

**FileKeyProvider_GetSigningKey_LegacyEncryptedPemKey_AsksToConvertToPkcs8**: A text file with the headers of a legacy
encrypted key (`BEGIN RSA PRIVATE KEY` and `Proc-Type: 4,ENCRYPTED`) is the certificate file, and a password is set.
The message names the file, says that the legacy encrypted PEM format is not supported and tells the operator to
convert the key to PKCS #8 with `openssl pkcs8 -topk8`; it does not say that the file is not a PEM certificate with a
private key, and it holds neither the password nor, in the form for the configuration check, the folder. This scenario
is tested by `FileKeyProvider_GetSigningKey_LegacyEncryptedPemKey_AsksToConvertToPkcs8`.

**FileKeyProvider_GetSigningKey_PfxWithPrivateKeyPath_IgnoresIt**: A protected PFX is loaded with a `PrivateKeyPath` that
names a file that does not exist. The PFX is used, the setting is ignored, and there is no warning. This scenario is
tested by `FileKeyProvider_GetSigningKey_PfxWithPrivateKeyPath_IgnoresIt`.

**FileKeyProvider_GetSigningKey_PasswordSettingsConflictOrFileMissing_AreRefusedWithTheirMessages**: Both password
settings are set, and a password file does not exist. The messages of `SecretSource` are reported, and the password is not
shown. This scenario is tested by
`FileKeyProvider_GetSigningKey_PasswordSettingsConflictOrFileMissing_AreRefusedWithTheirMessages`.

**FileKeyProvider_GetSigningKey_UnprotectedPfx_LoadsWithWarning**: A PFX without a password and no requirement. The key
loads, and a warning names the file and not the folder. This scenario is tested by
`FileKeyProvider_GetSigningKey_UnprotectedPfx_LoadsWithWarning`.

**FileKeyProvider_GetSigningKey_PfxWithPlainKeyAndPasswordSet_IsTreatedAsUnprotected**: A PFX that has a MAC with a
password but holds its private key in a plain, unencrypted key bag, so the password that is set protects nothing. The
key loads with the unprotected warning, and the same file is refused with `RequireEncryptedKeys`. This scenario is
tested by
`FileKeyProvider_GetSigningKey_PfxWithPlainKeyAndPasswordSet_IsTreatedAsUnprotected`.

**FileKeyProvider_GetSigningKey_UnprotectedKeyWithRequireEncryptedKeys_IsRefused**: The same file with
`RequireEncryptedKeys`. An exception names `DataProtection:RequireEncryptedKeys`. This scenario is tested by
`FileKeyProvider_GetSigningKey_UnprotectedKeyWithRequireEncryptedKeys_IsRefused`.

**FileKeyProvider_GetSigningKey_FilesReadableByOthersOnUnix_WarnsAboutEachOfThem**: On Linux, the PFX gets mode 640 and
the password file mode 604. A warning for each names the file and the mode and no folder; the same files with mode 600
give none. The test skips itself on Windows and is linked with the `ubuntu@` filter. This scenario is tested by
`FileKeyProvider_GetSigningKey_FilesReadableByOthersOnUnix_WarnsAboutEachOfThem`.

**FileKeyProvider_GetSigningKey_SeparateKeyOrPfxWithKeyPath_ChecksOnlyTheFileThatHoldsTheKey**: On Linux, an encrypted
PEM certificate with a separate key file is described with the certificate file at mode 640 and the key file at 600: no
warning, because the certificate file holds no secret. With the key file at mode 640 there is one warning, which names
the private key file. A PFX for the owner only, with a `PrivateKeyPath` that names a file that others can read, gives no
warning, because the setting is ignored for a PFX. The test skips itself on Windows and is linked with the `ubuntu@`
filter. This scenario is tested by
`FileKeyProvider_GetSigningKey_SeparateKeyOrPfxWithKeyPath_ChecksOnlyTheFileThatHoldsTheKey`.

**FileKeyProvider_Describe_EndsWithinWarningDays_WarnsUnlessDisabledOrFarAway**: With the fixed clock, a certificate that
ends in 10 days is described with the default 30 warning days, with 0 and with 5. Only the first has a warning, which
names the thumbprint and the date and is logged once. This scenario is tested by
`FileKeyProvider_Describe_EndsWithinWarningDays_WarnsUnlessDisabledOrFarAway`.

**FileKeyProvider_GetSigningKey_ExpiredWithoutRequireTrustedKey_SignsAndWarns**: The certificate has ended and the
rule is off. The key is returned and a warning that it expired is logged. This scenario is tested by
`FileKeyProvider_GetSigningKey_ExpiredWithoutRequireTrustedKey_SignsAndWarns`.

**FileKeyProvider_GetSigningKey_ExpiredWithRequireTrustedKey_IsRefused**: The certificate has ended and the rule is on.
An exception is raised. This scenario is tested by `FileKeyProvider_GetSigningKey_ExpiredWithRequireTrustedKey_IsRefused`.

**FileKeyProvider_GetSigningKey_ValidOrganizationCertificateWithRequireTrustedKey_Accepts**: An organization certificate
within its validity and the rule on. It is accepted. This scenario is tested by
`FileKeyProvider_GetSigningKey_ValidOrganizationCertificateWithRequireTrustedKey_Accepts`.

**AttestationKeyPolicy_ExpiryWarning_EndsWithinDaysOrHasEnded_NamesThumbprintAndDate**: With 30 warning days, the
time is 10 days, 30 days, one day after, and 400 days after the end of the certificate. Each gives a warning that says
"expires" or "expired" and names the thumbprint and the date. This scenario is tested by
`AttestationKeyPolicy_ExpiryWarning_EndsWithinDaysOrHasEnded_NamesThumbprintAndDate`.

**AttestationKeyPolicy_ExpiryWarning_FarAwayOrDisabled_ReturnsNull**: 90 days before the end with 30 warning days, and
after the end with 0 warning days, give no warning. This scenario is tested by
`AttestationKeyPolicy_ExpiryWarning_FarAwayOrDisabled_ReturnsNull`.

**AttestationKeyProviderBase_GetSigningKey_ExpiredKeyWithoutPolicy_WarnsOnceAndStillSigns**: The provider has an expired
key and no `RequireTrustedKey`. It returns the key on each call and logs the warning once. This scenario is tested by
`AttestationKeyProviderBase_GetSigningKey_ExpiredKeyWithoutPolicy_WarnsOnceAndStillSigns`.

**AttestationKeyProviderBase_Describe_EndsSoonOrLater_WarnsOnlyWhenSoon**: One provider has a clock 10 days before the
end of its certificate and one has a clock a year before it. Only the first status has an expiry warning. This scenario
is tested by `AttestationKeyProviderBase_Describe_EndsSoonOrLater_WarnsOnlyWhenSoon`.

**AttestationKeyProviderFactory_Create_SelfSigned_StatusHasNoExpiryWarning**: The self-signed provider replaces a key
that has expired and logs its own warning. Its status gets no expiry warning, however long the warning period is. This
scenario is tested by `AttestationKeyProviderFactory_Create_SelfSigned_StatusHasNoExpiryWarning`.
