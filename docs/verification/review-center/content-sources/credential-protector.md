### CredentialProtector

#### Verification Approach

`CredentialProtector` is verified with tests defined in `CredentialProtectorTests.cs`, using the real ASP.NET Core Data
Protection system with a key ring in a private temporary folder, because the properties to verify are those of the key
ring: the protected text, the survival of a restart, a lost key ring, and the isolation of the purpose. A restart is
simulated by creating a new provider over the same folder; a lost key ring by creating a provider over another folder;
the purpose by creating a protector for another purpose on the same key ring.

#### Test Environment

N/A - standard test environment (a temporary key ring folder is created by each test).

#### Acceptance Criteria

- All tests pass with zero failures.
- A token survives a round trip and the protected text does not contain it.
- A persisted key ring lets a new protector read earlier text.
- Text protected under another key ring is `CredentialsUnreadable`, without the text in the message.
- Another purpose on the same key ring cannot read the text.
- Null arguments are refused.

#### Test Scenarios

**CredentialProtector_ProtectUnprotect_RoundTripsAndHidesToken**: A token is protected and unprotected; the result
equals the
token and the protected text does not contain it. This scenario is tested by
`CredentialProtector_ProtectUnprotect_RoundTripsAndHidesToken`.

**CredentialProtector_PersistedKeyRing_SurvivesRestart**: A new protector over the same key ring folder reads text that
the first protector wrote. This scenario is tested by `CredentialProtector_PersistedKeyRing_SurvivesRestart`.

**CredentialProtector_Unprotect_OtherKeyRing_ThrowsCredentialsUnreadable**: Text protected under one key ring is
refused by
a protector over another key ring with `CredentialsUnreadable`, and the message does not contain the text. This scenario
is tested by `CredentialProtector_Unprotect_OtherKeyRing_ThrowsCredentialsUnreadable`.

**CredentialProtector_Purpose_IsolatesTokensFromOtherUses**: A protector with another purpose on the same key ring cannot
read the text. This scenario is tested by `CredentialProtector_Purpose_IsolatesTokensFromOtherUses`.

**CredentialProtector_NullArguments_ThrowArgumentNull**: Null arguments raise `ArgumentNullException`. This scenario is
tested by `CredentialProtector_NullArguments_ThrowArgumentNull`.
