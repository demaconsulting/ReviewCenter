### PersonalAccessTokenService

#### Verification Approach

`PersonalAccessTokenService` is verified with unit tests defined in `PersonalAccessTokenServiceTests.cs`. The tests use
the real service with in-memory fakes of the token store (`FakePersonalAccessTokenStore`, which also keeps the audit
drafts that the service builds) and of the user store, a real `UserService` with a fast fake password hasher for the
owners, and a clock that the tests move. The
fake store can be made to return a row with another hash, so that the second check of the hash is tested. No database,
web framework, or network is involved. The rules are verified here; the storage is verified in the `Storage` subsystem
and the request handling in the `Api` subsystem.

#### Test Environment

N/A - standard test environment (no external resource is needed).

#### Acceptance Criteria

- All tests pass with zero failures.
- A new token has the prefix and 256 bits of random value, and only the SHA-256 hash is stored and no listing has the
  value.
- The display id is short, random, and not taken from the value.
- The name is trimmed and limited, the expiry is one of the three choices, and an unknown or disabled owner is refused.
- No more than 20 tokens are active, where revoked and expired tokens do not count.
- Only the right value authenticates; a malformed value, a revoked or expired token, a disabled or locked owner, and a
  row with another hash are refused.
- The last use is written at most once in five minutes, also for a burst of parallel requests.
- A user revokes only their own tokens, and an actor that came through a token can neither create nor revoke.
- The creation and the revocation are audited with the display id and without the value.
- An owner with a temporary password can neither create, list, nor revoke a token, and an existing token of that owner
  is refused with its own failure reason.

#### Test Scenarios

**PersonalAccessTokenService_Create_ValidRequest_ReturnsTheTokenAndListsIt**: A token is created for the user ann. The
answer has the name, the creation time, no revocation, and no last use, and the list of ann holds exactly that token.
This scenario is tested by `PersonalAccessTokenService_Create_ValidRequest_ReturnsTheTokenAndListsIt`.

**PersonalAccessTokenService_Create_NewToken_HasPrefixAndHighEntropyValue**: Two tokens are created. Each value starts
with `rcpat_`, has 43 base64url characters that decode to 32 bytes, and the two values differ. This scenario is tested by
`PersonalAccessTokenService_Create_NewToken_HasPrefixAndHighEntropyValue`.

**PersonalAccessTokenService_Create_NewToken_StoresOnlyTheSha256Hash**: A token is created. The store holds the lowercase
hexadecimal SHA-256 hash of the value and not the value. This scenario is tested by
`PersonalAccessTokenService_Create_NewToken_StoresOnlyTheSha256Hash`.

**PersonalAccessTokenService_Create_DisplayId_IsShortRandomAndIndependentOfTheSecret**: Several tokens are created. The
display id is a short text of the allowed alphabet that differs between tokens and is not part of the value. This
scenario is tested by `PersonalAccessTokenService_Create_DisplayId_IsShortRandomAndIndependentOfTheSecret`.

**PersonalAccessTokenService_List_AfterCreate_NeverReturnsTheValue**: Tokens are listed after they were created. No field
of the listed tokens holds the value. This scenario is tested by
`PersonalAccessTokenService_List_AfterCreate_NeverReturnsTheValue`.

**PersonalAccessTokenService_Create_NameWithSurroundingSpaces_StoresItTrimmed**: A token is created with a name that has
spaces around it. The answer and the store hold the name without them. This scenario is tested by
`PersonalAccessTokenService_Create_NameWithSurroundingSpaces_StoresItTrimmed`.

**PersonalAccessTokenService_Create_InvalidName_ThrowsTokenNameInvalid**: A missing, an empty, a blank, and a
control-character name are each refused with `TokenNameInvalid`, and nothing is stored. This scenario is tested by
`PersonalAccessTokenService_Create_InvalidName_ThrowsTokenNameInvalid`.

**PersonalAccessTokenService_Create_NameLength_HasInclusiveLimit**: A name over the limit is refused and one at the limit
is accepted. This scenario is tested by `PersonalAccessTokenService_Create_NameLength_HasInclusiveLimit`.

**PersonalAccessTokenService_Create_UnknownOwner_IsRefused**: A user who does not exist creates a token. It is refused
with `ActorNotAllowed`, and nothing is stored or audited. This scenario is tested by
`PersonalAccessTokenService_Create_UnknownOwner_IsRefused`.

**PersonalAccessTokenService_Create_DisabledOwner_IsRefused**: A disabled user creates a token. It is refused with
`ActorNotAllowed`, and nothing is stored or audited. This scenario is tested by
`PersonalAccessTokenService_Create_DisabledOwner_IsRefused`.

**PersonalAccessTokenService_List_UnknownOwner_IsRefused**: A user who does not exist lists tokens. It is refused with
`ActorNotAllowed`. This scenario is tested by `PersonalAccessTokenService_List_UnknownOwner_IsRefused`.

**PersonalAccessTokenService_Create_ExpiryChoice_SetsTheExpiry**: Lifetimes of 30, 90, and 365 days set the expiry that
many days after the creation, in the answer and in the store. This scenario is tested by
`PersonalAccessTokenService_Create_ExpiryChoice_SetsTheExpiry`.

**PersonalAccessTokenService_Create_InvalidExpiry_ThrowsTokenExpiryInvalid**: Lifetimes of 0, -1, 7, and 366 days are
each refused with `TokenExpiryInvalid`, and nothing is stored. This scenario is tested by
`PersonalAccessTokenService_Create_InvalidExpiry_ThrowsTokenExpiryInvalid`.

**PersonalAccessTokenService_Authenticate_AfterExpiry_IsRejected**: A token is accepted before its expiry and refused when
the clock reaches it. This scenario is tested by `PersonalAccessTokenService_Authenticate_AfterExpiry_IsRejected`.

**PersonalAccessTokenService_Create_TwentyActive_RejectsTheTwentyFirst**: Twenty tokens are created and the twenty-first
is refused with `TokenLimitReached`. This scenario is tested by
`PersonalAccessTokenService_Create_TwentyActive_RejectsTheTwentyFirst`.

**PersonalAccessTokenService_Create_RevokedTokens_DoNotCount**: Twenty tokens are active and one is revoked. A replacement
is accepted, twenty tokens are active, and one more is refused with `TokenLimitReached`. This scenario is tested by
`PersonalAccessTokenService_Create_RevokedTokens_DoNotCount`.

**PersonalAccessTokenService_Create_ExpiredTokens_DoNotCount**: Ten tokens expire, then twenty new tokens are created.
All are accepted, twenty are active, and one more is refused with `TokenLimitReached`. This scenario is tested by
`PersonalAccessTokenService_Create_ExpiredTokens_DoNotCount`.

**PersonalAccessTokenService_Authenticate_RightAndWrongValue_OnlyRightSucceeds**: The right value authenticates as the
owner; a wrong value does not. This scenario is tested by
`PersonalAccessTokenService_Authenticate_RightAndWrongValue_OnlyRightSucceeds`.

**PersonalAccessTokenService_Authenticate_StoreReturnsRowWithOtherHash_IsRejected**: The fake store returns a row whose
hash differs from the hash of the presented value. The service refuses it, because it compares the hash again in constant
time. This scenario is tested by
`PersonalAccessTokenService_Authenticate_StoreReturnsRowWithOtherHash_IsRejected`.

**PersonalAccessTokenService_Authenticate_DisabledOrLockedOwner_IsRejected**: The token of a disabled owner and of a
locked-out owner is refused, and works again when the owner does. This scenario is tested by
`PersonalAccessTokenService_Authenticate_DisabledOrLockedOwner_IsRejected`.

**PersonalAccessTokenService_Authenticate_Malformed_IsRejected**: Values of the wrong shape are refused without asking the
store. This scenario is tested by `PersonalAccessTokenService_Authenticate_Malformed_IsRejected`.

**PersonalAccessTokenService_Authenticate_FirstUse_RecordsTheTime**: A token that was never used is used a minute after
its creation. The use succeeds and the store holds the time of the use, written once. This scenario is tested by
`PersonalAccessTokenService_Authenticate_FirstUse_RecordsTheTime`.

**PersonalAccessTokenService_Authenticate_Touch_AtMostOncePerFiveMinutes**: A token is used several times within five
minutes and then after five minutes. The last-used time is written once for the first use and again only after the five
minutes. This scenario is tested by `PersonalAccessTokenService_Authenticate_Touch_AtMostOncePerFiveMinutes`.

**PersonalAccessTokenService_Authenticate_ParallelBurst_TouchesOnce**: Four requests run in parallel and all read the same
snapshot of a token that was never used. All are accepted and the store is written once, because the store applies the
update only to the last-used time that was read. This scenario is tested by
`PersonalAccessTokenService_Authenticate_ParallelBurst_TouchesOnce`.

**PersonalAccessTokenService_Revoke_OwnToken_StopsWorkingAndIsAudited**: A user revokes their own token. It stops working
at once and the revocation is recorded. This scenario is tested by
`PersonalAccessTokenService_Revoke_OwnToken_StopsWorkingAndIsAudited`.

**PersonalAccessTokenService_Revoke_OtherUsersToken_IsRefused**: A user revokes the token of somebody else, a token that
does not exist, and a token that is already revoked. Each is refused with `TokenNotFound`, and the token of ann was
revoked only by ann. This scenario is tested by `PersonalAccessTokenService_Revoke_OtherUsersToken_IsRefused`.

**PersonalAccessTokenService_Revoke_OtherUsersToken_IsRefusedAndTheTokenKeepsWorking**: A user revokes the token of
somebody else. It is refused with `TokenNotFound`, the token is not revoked and still authenticates, and no revocation
is audited. This scenario is tested by
`PersonalAccessTokenService_Revoke_OtherUsersToken_IsRefusedAndTheTokenKeepsWorking`.

**PersonalAccessTokenService_CreateAndRevoke_ApiTokenActor_IsRefused**: An actor that carries the id of a token tries to
create and to revoke a token. Both are refused with `ActorNotAllowed`, no token is created, and the token is not revoked
and still authenticates. This scenario is tested by
`PersonalAccessTokenService_CreateAndRevoke_ApiTokenActor_IsRefused`.

**PersonalAccessTokenService_CreateAndRevoke_RecordAuditEventsWithoutTheValue**: A token is created and revoked. The audit
trail has `token.created` and `token.revoked` with the display id, and no entry has the value or its hash. This scenario
is tested by `PersonalAccessTokenService_CreateAndRevoke_RecordAuditEventsWithoutTheValue`.

**PersonalAccessTokenService_TemporaryPasswordCleared_AllowsEveryUse**: An owner with a token is flagged to change the
password and refused. When the flag is cleared, creating, listing, authenticating, and revoking succeed. The scenario
is defined in `MustChangePasswordTests.cs` and is tested by
`PersonalAccessTokenService_TemporaryPasswordCleared_AllowsEveryUse`.

**PersonalAccessTokenService_TemporaryPassword_RefusesEveryUse**: An owner who had created a token is given a
temporary password (the must-change flag of `User`, set through `FakeUserStore`). Creating, listing, and revoking are
each refused with `PasswordChangeRequired`, and authenticating the earlier value fails with
`OwnerMustChangePassword`. The scenario is defined in `MustChangePasswordTests.cs` and is tested by
`PersonalAccessTokenService_TemporaryPassword_RefusesEveryUse`.
