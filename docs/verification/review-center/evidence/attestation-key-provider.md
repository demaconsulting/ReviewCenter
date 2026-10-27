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
- The self-signed key is created once, with a limited validity, reloaded, and the same for every caller, also when many
  start together.
- Every problem of the self-signed provider and of the certificate store provider names the setting or the file.
- `RequireTrustedKey` is checked on every call; the rule accepts an organization certificate and refuses a self-signed
  one, one without a signing key usage, and one outside its validity.
- A certificate for code signing only is warned about and not refused.
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

**AttestationKeyPolicy_Warnings_ExtendedKeyUsage_WarnsOnlyForCodeSigningOnly**: Certificates with several extended key
usages are examined. Only one that allows code signing and no document signing gets a warning. This scenario is tested by
`AttestationKeyPolicy_Warnings_ExtendedKeyUsage_WarnsOnlyForCodeSigningOnly`.

**SelfSignedKeyProvider_GetSigningKey_FirstUse_CreatesSelfSignedKeyFile**: The key is requested from an empty folder. A
key file is created and the certificate is self-signed. This scenario is tested by
`SelfSignedKeyProvider_GetSigningKey_FirstUse_CreatesSelfSignedKeyFile`.

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
