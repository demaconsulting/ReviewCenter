### PasswordHasher

#### Verification Approach

`PasswordHasher` is verified with unit tests defined in `PasswordHasherTests.cs`. The unit wraps the password hasher
of ASP.NET Core and depends on nothing else, so the real hasher is used and no mocking is applied. Tests hash and
verify passwords and inspect the returned text.

#### Test Environment

N/A - standard test environment.

#### Acceptance Criteria

- All unit tests pass with zero failures.
- The right password verifies against its hash and a wrong one does not.
- A text that is not a valid hash is rejected without an exception.

#### Test Scenarios

**PasswordHasher_Verify_RightAndWrongPassword_OnlyRightSucceeds**: A password is hashed and the hash is verified
against the right password and a wrong one; only the right password succeeds. This scenario is tested by
`PasswordHasher_Verify_RightAndWrongPassword_OnlyRightSucceeds`.

**PasswordHasher_Verify_GarbageHash_ReturnsFalse**: A text that is not a hash is verified; the result is false and
no exception is thrown. This scenario is tested by `PasswordHasher_Verify_GarbageHash_ReturnsFalse`.
