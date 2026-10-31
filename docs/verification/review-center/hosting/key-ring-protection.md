### KeyRingProtection

#### Verification Approach

`KeyRingProtection` is verified with unit tests defined in `KeyRingProtectionTests.cs`, together with the
`ServerHost` tests that start the real pipeline (see the verification of `ServerHost`). The decision is a pure function
of the settings and an injectable platform record, so the whole matrix of Windows, Linux, and macOS is covered on any
machine. The Data Protection tests use real key rings in temporary folders, in-memory RSA certificates exported to
password-protected PFX files in the same folder, and the real key encryptors of the framework; nothing uses the
certificate store, the home directory, or a real key ring. The DPAPI encryptor is created through a test seam, without
calling it, except in the one Windows-only test that makes a real round trip.

#### Test Environment

The tests run on every platform. The tests of the file modes (0700 and 0600) run only on Linux and macOS and are
skipped on Windows, which has no such modes; the tests of the access rules of the key folder and the real DPAPI test
run only on Windows and are skipped elsewhere. The requirements name them with the `ubuntu@` and `windows@` source
filters, so that each platform requires the tests it can run. The decorator that restricts the key files as they are
stored is also tested with a recording call in place of the change of the mode, which runs on every platform and
proves the decision (restrict on Unix, do nothing on Windows) without touching the permissions of a file.

#### Acceptance Criteria

- All unit tests pass with zero failures (the tests of the other platform are skipped).
- `Auto` chooses the certificate when a certificate path is set, otherwise DPAPI on Windows and `PermissionsOnly`
  elsewhere; explicit choices are honored in any case.
- `Dpapi` on a platform that is not Windows, an unknown protection or scope, and `Certificate` without a certificate
  path are refused with a message that names the setting and lists the choices.
- DPAPI is created for the current user or, with `LocalMachine`, for the machine, without calling it.
- A certificate in a PFX, PKCS#12, or PEM file (with a separate key or a password) is loaded with its private key; a
  missing file, a wrong password, a file without a private key, a certificate that is not RSA, and both password
  settings are each reported as a problem that names the setting and never holds the password.
- A ring protected by a certificate holds an encrypted secret and no plain key, is read again after a restart, and is
  read with a previous certificate after the certificate was replaced.
- The guard refuses a ring that the certificate cannot open, without creating a second key, refuses an encrypted ring
  with no certificate, passes an empty ring, and reports an older unreadable key as an error without stopping.
- `RequireEncryptedKeys` refuses `PermissionsOnly` and a plain key file.
- On Linux and macOS the key files are restricted to the owner, also when Data Protection stores a new key file while
  the server runs. The warning of `PermissionsOnly` is logged.
- On Windows the `PermissionsOnly` protection restricts the key folder to the current account and the system, without
  inheritance, and the files created in it inherit that; the other protections leave the rules of the folder alone.
- On Windows a ring protected with DPAPI holds an encrypted secret and is read again after a restart.

#### Test Scenarios

**KeyRingProtection_ResolveMode_AutoOnWindowsWithoutCertificate_ChoosesDpapi** and
**KeyRingProtection_ResolveMode_AutoOnUnixWithoutCertificate_ChoosesPermissionsOnly**: `Auto` without a certificate
path is resolved for each platform. These scenarios are tested by the tests of the same names.

**KeyRingProtection_ResolveMode_AutoWithCertificatePath_ChoosesCertificate**: With a certificate path, `Auto` chooses
the certificate on Windows and on Unix. This scenario is tested by the test of the same name.

**KeyRingProtection_ResolveMode_ExplicitChoice_IsHonoredInAnyCase**: The four choices are accepted in any case. This
scenario is tested by the test of the same name.

**KeyRingProtection_ResolveMode_DpapiOnUnix_ThrowsNamingTheSetting**,
**KeyRingProtection_ResolveMode_CertificateWithoutPath_ThrowsNamingTheSettings**, and
**KeyRingProtection_ResolveMode_UnknownProtection_ThrowsListingTheChoices**: The refused choices raise an
`InvalidOperationException` that names `DataProtection:Protection` (and the path setting) and lists the choices. These
scenarios are tested by the tests of the same names.

**KeyRingProtection_Prepare_UnknownProtection_ReturnsProblemWithPrefix**,
**KeyRingProtection_Prepare_DpapiScope_IsCheckedAndCarried**, and
**KeyRingProtection_Prepare_Configuration_BindsTheSectionAndReportsBadValues**: The settings are read from the
configuration section, a bad value is returned as a problem that starts "Invalid key ring settings:", and the scope is
carried into the plan. These scenarios are tested by the tests of the same names.

**KeyRingProtection_Prepare_PermissionsOnlyWithRequireEncryptedKeys_ReturnsProblem** and
**KeyRingProtection_Prepare_AutoOnUnixWithRequireEncryptedKeys_ReturnsProblem**: Requiring encrypted keys with the
permissions-only protection is a problem that names `DataProtection:RequireEncryptedKeys`. These scenarios are tested
by the tests of the same names.

**KeyRingProtection_Prepare_MissingCertificateFile_ReturnsProblemNamingTheSetting**,
**KeyRingProtection_Prepare_WrongPassword_ReturnsProblemWithoutThePassword**,
**KeyRingProtection_Prepare_NotACertificateWithPrivateKey_ReturnsProblem**,
**KeyRingProtection_Prepare_EcdsaCertificate_ReturnsProblemNamingRsa**, and
**KeyRingProtection_Prepare_PasswordAndPasswordFile_ReturnsProblemWithoutThePassword**: Each way that a certificate
cannot be used is a problem that names the setting and never holds the password. These scenarios are tested by the
tests of the same names.

**KeyRingProtection_LoadCertificate_PlatformCannotLoadPfx_ThrowsNamingTheSetting**: The loader of the PFX file is
replaced by one that throws `PlatformNotSupportedException`, as a platform that refuses the key storage flags does. The
result is the usual `InvalidOperationException` that names `DataProtection:CertificatePath` and the path, never the
password, and keeps the cause. This scenario is tested by the test of the same name.

**KeyRingProtection_Prepare_SupportedFormats_LoadWithPrivateKey** and
**KeyRingProtection_Prepare_PreviousCertificates_LoadWithSharedPasswordOrNone**: PFX, PEM with a separate key, and
encrypted PEM files load with a private key; previous certificates load with the shared
password or none, and a bad one is named by its index. These scenarios are tested by the tests of the same names.

**KeyRingProtection_Apply_Dpapi_CreatesTheEncryptorForTheScope** and
**KeyRingProtection_Apply_CertificateAndOthers_WiresTheCertificateEncryptorOnly**: A stub factory shows that DPAPI is
created with the right scope and that no other mode creates it; the certificate mode wires the certificate encryptor.
These scenarios are tested by the tests of the same names.

**KeyRingProtection_CertificateRing_KeyFileIsEncryptedAndRestartReadsIt** and
**KeyRingProtection_CertificateRing_PreviousCertificateAndPasswordFile_StillReadTheRing**: A real key ring in a
temporary folder is protected with an in-memory certificate, read again by a new provider, and read with a previous
certificate after a replacement. The key file holds an encrypted secret and no plain key. These scenarios are tested
by the tests of the same names.

**KeyRingProtection_CheckRing_WrongCertificate_RefusesWithoutCreatingASecondKey**,
**KeyRingProtection_CheckRing_EncryptedRingWithoutCertificate_Refuses**,
**KeyRingProtection_CheckRing_EmptyRing_Passes**,
**KeyRingProtection_CheckRing_RequireEncryptedKeysWithPlaintextKeyFile_Refuses**, and
**KeyRingProtection_CheckRing_OlderKeyUnreadable_LogsErrorAndPasses**: The guard is run on rings made with different
certificates, and the number of key files is counted before and after; a folder that does not exist yet is checked as
well, and the keys of the older-key scenario get explicit creation dates so that the newest key does not depend on the
clock. These scenarios are tested by the tests of the
same names.

**KeyRingProtection_LogProtection_EachMode_LogsTheProtectionInForce**: Each mode logs its protection; the permissions
mode logs a warning. This scenario is tested by the test of the same name.

**KeyRingProtection_RestrictKeyFiles_UnixKeyFiles_AreOwnerReadWriteOnly**: On Linux and macOS only, key files that were
created with wider modes are restricted to 0600. This scenario is tested by the test of the same name.

**KeyRingProtection_KeyFileRestrictingRepository_StoreElementOnUnix_RestrictsTheStoredFile** and
**KeyRingProtection_KeyFileRestrictingRepository_StoreElementOnWindows_DoesNotChangeModes**: A key is stored through the
decorator over a real file repository in a temporary folder, with a recording call in place of the change of the mode.
For a Unix platform the new key file is restricted to 0600 and the key is read back through the decorator; for a
Windows platform the key is stored and no mode is set. These scenarios run on every platform and are tested by the
tests of the same names.

**KeyRingProtection_KeyFileRestrictingRepository_UnixStoredKeyFile_IsOwnerReadWriteOnly**: On Linux and macOS only, a key
stored through the decorator with the real call ends up with mode 0600, as the key file of a ring that rolls while the
server runs. This scenario is tested by the test of the same name.

**KeyRingProtection_RestrictKeyFolder_WindowsPermissionsOnly_AdmitsOnlyOwnerAndSystem**: On Windows only, in a temporary
folder that does not exist yet, the folder is restricted twice (the second call changes nothing). It does not inherit
from its parent, it holds allow rules for the current account and the system and no others, and a file created in it
afterwards inherits exactly those rules. The current account keeps full control, so that the temporary folder can be
deleted. This scenario is tested by the test of the same name.

**KeyRingProtection_RestrictKeyFolder_WindowsDpapi_LeavesTheInheritedRules**: On Windows only, the DPAPI protection, which
encrypts the key files, leaves the folder inheriting the rules of its parent. This scenario is tested by the test of the
same name.

**KeyRingProtection_DpapiRing_WindowsKeyFileIsEncryptedAndRestartReadsIt**: On Windows only, a real ring is protected
with DPAPI in a temporary folder and read again by a new provider; the key file holds an encrypted secret. This
scenario is tested by the test of the same name and is the evidence for the DPAPI encryptor of the framework.
