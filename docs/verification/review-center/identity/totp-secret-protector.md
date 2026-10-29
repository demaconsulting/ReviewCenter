### TotpSecretProtector

#### Verification Approach

`TotpSecretProtector` is verified with unit tests defined in `TotpSecretProtectorTests.cs`. The tests create the class
directly over the real ASP.NET Core Data Protection system with an ephemeral in-memory key ring
(`EphemeralDataProtectionProvider`), so the real encryption is used and nothing is mocked. A second protector over
another key ring stands for a lost or changed key ring. How the service uses the protector, and the refusal that an
unreadable secret leads to, is verified by the `TotpService` tests.

#### Test Environment

N/A - standard test environment. The tests need no database, no files, and no network.

#### Acceptance Criteria

- All unit tests pass with zero failures.
- A secret is returned as encrypted text from which neither the Base32 form nor the raw bytes can be read, and it is
  decrypted again to exactly the same bytes for the same user.
- The text of one user is refused for another user.
- Text from another key ring, text that is not Base64, and truncated text are reported as unreadable with the rule
  `TotpSecretUnreadable` and a message that holds no secret.
- Missing arguments are refused.

#### Test Scenarios

**TotpSecretProtector_ProtectAndUnprotect_RoundTripsAndHidesTheSecret**: The published test secret is protected and
unprotected for one user; the same bytes come back, and neither the Base32 text nor the ASCII text of the secret
appears in the stored value or in its decoded bytes. This scenario is tested by
`TotpSecretProtector_ProtectAndUnprotect_RoundTripsAndHidesTheSecret`.

**TotpSecretProtector_Unprotect_ValueOfAnotherUser_IsUnreadable**: A value protected for one user is read as another
user; the rule `TotpSecretUnreadable` is raised and the stored text is not in the message. This scenario is tested by
`TotpSecretProtector_Unprotect_ValueOfAnotherUser_IsUnreadable`.

**TotpSecretProtector_Unprotect_DamagedOrForeignValue_IsUnreadable**: A theory over a value of another key ring, text
that is not Base64, and truncated text; each raises `TotpSecretUnreadable`. This scenario is tested by
`TotpSecretProtector_Unprotect_DamagedOrForeignValue_IsUnreadable`.

**TotpSecretProtector_NullArguments_Throw**: A null provider, a null secret, and a null stored value raise
`ArgumentNullException`. This scenario is tested by `TotpSecretProtector_NullArguments_Throw`.
