### RepositoryService

#### Verification Approach

`RepositoryService` is verified with tests defined in `RepositoryServiceTests.cs`. The service is created with the real
`UserService`, the fake stores (`FakeUserStore` and `FakeRepositoryStore`), a trivial token protector
(`FakeCredentialProtector`, which can be set to fail as after a lost key ring), a trivial password hasher, and a fixed
clock, because the rules of the registry are what is verified here; the real database is verified in the `Storage`
subsystem and the real encryption in the `ContentSources` subsystem. The fake store records the audit drafts that the
service hands over, so the tests check the action and the detail of each audit entry. Tests of the token check the
stored text, every message, every audit detail, and the text form of the token types for the token, because a secret
that leaks into any of them is the main risk of the unit.

#### Test Environment

N/A - standard test environment.

#### Acceptance Criteria

- All tests pass with zero failures.
- A repository is registered enabled; the token is stored only encrypted and is never returned by a listing or shown by
  a text form.
- Every allowed location form registers; a name, a location, or a credential that breaks a rule is refused with the
  matching rule, and nothing is stored.
- Only enabled administrators can use the registry; the other actors are refused for every operation.
- Every change is audited with the changed field names and no secret; a change that changes nothing makes no entry.
- A disabled repository is not offered to users and cannot be opened by the git layer.
- A token that cannot be decrypted is reported by repository name and without the stored text.
- A token equal to the repository name or the user name is refused when registering and when updating (with a new token,
  or with the stored token kept while the name or user name changes to it); a different case is accepted.
- The access data is one consistent version of the registration, even when an update runs at the same time.
- Null arguments are refused.

#### Test Scenarios

**RepositoryService_CreateAsync_WithToken_StoresItEncryptedAndWriteOnly**: A repository with a user name and token is
registered enabled; the stored token is the encrypted text, and no read model or listing holds the token. This scenario
is tested by `RepositoryService_CreateAsync_WithToken_StoresItEncryptedAndWriteOnly`.

**RepositoryService_CreateAsync_AllowedLocation_Registers**: An anonymous repository registers with each allowed location
form (`https`, `http`, and `file` addresses). This scenario is tested by `RepositoryService_CreateAsync_AllowedLocation_Registers`.

**RepositoryService_CreateAsync_AbsoluteLocalPath_Registers**: A fully qualified local path is a valid location. This
scenario is tested by `RepositoryService_CreateAsync_AbsoluteLocalPath_Registers`.

**RepositoryService_CreateAsync_InvalidName_ThrowsRepositoryNameInvalid**: Names that are empty, start with a character
other than a letter or digit, or hold characters outside the allowed set are refused with `RepositoryNameInvalid`. This
scenario is tested by `RepositoryService_CreateAsync_InvalidName_ThrowsRepositoryNameInvalid`.

**RepositoryService_CreateAsync_NameLimitsAndDuplicate_AreEnforced**: A name of 40 characters is accepted and one of 41
is
refused; a name that differs only in case from an existing one is refused with `RepositoryNameTaken`. This scenario is
tested by `RepositoryService_CreateAsync_NameLimitsAndDuplicate_AreEnforced`.

**RepositoryService_CreateAsync_InvalidLocation_ThrowsRepositoryLocationInvalid**: Locations that are empty, too long,
start with `-`, hold control characters or white space, carry user information or a query string or fragment, lack a
host, use an unsupported scheme, or
are SSH or scp-style addresses or relative paths are refused with `RepositoryLocationInvalid`. This scenario is tested by
`RepositoryService_CreateAsync_InvalidLocation_ThrowsRepositoryLocationInvalid`.

**RepositoryService_CreateAsync_InvalidCredentials_ThrowsRepositoryCredentialInvalid**: A user name or token for a
location that is not `https`, a token without a user name, a user name with `:` or control characters, and a token
that is
too long or holds white space are refused with `RepositoryCredentialInvalid`. This scenario is tested by
`RepositoryService_CreateAsync_InvalidCredentials_ThrowsRepositoryCredentialInvalid`.

**RepositoryService_CreateAsync_TokenEqualsNameOrUsername_ThrowsRepositoryCredentialInvalid**: A token that is exactly
the repository name or exactly the user name is refused with `RepositoryCredentialInvalid`, with nothing stored or
audited and without the token in the message; a token that differs only in letter case is accepted. This scenario is
tested by `RepositoryService_CreateAsync_TokenEqualsNameOrUsername_ThrowsRepositoryCredentialInvalid`.

**RepositoryService_UpdateAsync_TokenEqualsNameOrUsername_ThrowsRepositoryCredentialInvalid**: On update, replacing the
token with the new repository name or user name is refused, and so is keeping the stored token while the repository or
the user is renamed to the stored token text (the service decrypts the stored token under its lock to compare); every
refusal is `RepositoryCredentialInvalid`, the message has no token, and nothing changes. This scenario is tested by
`RepositoryService_UpdateAsync_TokenEqualsNameOrUsername_ThrowsRepositoryCredentialInvalid`.

**RepositoryService_UpdateAsync_KeepUnreadableOrClearedToken_DoesNotCompare**: A kept token that cannot be decrypted
cannot be compared and does not block a rename, and clearing the token while renaming to its old text is allowed. This
scenario is tested by `RepositoryService_UpdateAsync_KeepUnreadableOrClearedToken_DoesNotCompare`.

**RepositoryService_AdminOnly_RefusesOtherActors**: A reviewer, an unknown user, and a disabled administrator are refused
with `ActorNotAllowed` for every operation that needs an administrator. This scenario is tested by
`RepositoryService_AdminOnly_RefusesOtherActors`.

**RepositoryService_Changes_AreAuditedWithoutSecrets**: Creating, updating (with each token change), enabling, and
disabling are recorded with their actions and the names of the changed fields, and no audit text or message contains the
token. This scenario is tested by `RepositoryService_Changes_AreAuditedWithoutSecrets`.

**RepositoryService_NoOpChanges_AreNotAudited**: Keeping the token leaves the stored token alone; an update that changes
nothing and a repeated enable make no audit entry. This scenario is tested by `RepositoryService_NoOpChanges_AreNotAudited`.

**RepositoryService_UpdateAsync_InvalidRequests_ThrowRules**: A `Replace` without a token, an update of an unknown
repository, and a change that would leave a user name on a repository without a token are handled by the rules. This
scenario is tested by `RepositoryService_UpdateAsync_InvalidRequests_ThrowRules`.

**RepositoryService_DisabledRepository_IsNotSelectable**: Ordinary users see only the identity and name of enabled
repositories, and a disabled repository is neither listed nor available to the git layer (`RepositoryDisabled`). This
scenario is tested by `RepositoryService_DisabledRepository_IsNotSelectable`.

**RepositoryService_GetAccessAsync_UnreadableToken_ThrowsCredentialsUnreadable**: A token that can no longer be
decrypted is
reported as `CredentialsUnreadable` with the repository name and without the stored text, and an unknown repository is
`RepositoryNotFound`. This scenario is tested by `RepositoryService_GetAccessAsync_UnreadableToken_ThrowsCredentialsUnreadable`.

**RepositoryService_GetAccessAsync_ConcurrentUpdate_ReturnsConsistentPair**: A fake store starts an administrator's update
at the moment between the read of the registration and the read of the token; the update waits for the service lock,
so the access data has the location and token of one version, and a later read has the location and token of the
updated one. This scenario is tested by `RepositoryService_GetAccessAsync_ConcurrentUpdate_ReturnsConsistentPair`.

**RepositoryService_TokenTypes_ToStringHidesToken**: The text forms of the token change and of the access data never
show a
token. This scenario is tested by `RepositoryService_TokenTypes_ToStringHidesToken`.

**RepositoryService_NullArguments_ThrowArgumentNull**: Null arguments raise `ArgumentNullException`. This scenario is
tested by `RepositoryService_NullArguments_ThrowArgumentNull`.
